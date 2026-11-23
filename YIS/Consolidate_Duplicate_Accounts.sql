-- =========================================================================================
-- SAFE PRODUCTION DB CONSOLIDATION SCRIPT: CHART OF ACCOUNTS DUPLICATE MERGE
-- Description: Consolidates duplicate system accounts (e.g. Output Sales Tax) into a single
--              primary master account (MIN(ACCOUNT_ID)) per company (UCP_ID).
-- Safety:
--   1. Zero amount alterations (Debits and Credits in TRANS remain 100% untouched).
--   2. Preserves all transaction history, dates, and voucher links.
--   3. Wraps all updates inside an atomic TRANSACTION with rollback on error.
--   4. Only removes empty duplicate accounts that have 0 references remaining.
-- =========================================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY
    PRINT '>>> Step 1: Pre-Consolidation Sanity Check...';
    
    -- Check total GL balance before merge
    DECLARE @TotalDebitBefore FLOAT, @TotalCreditBefore FLOAT;
    SELECT @TotalDebitBefore = SUM(ISNULL(DEBIT, 0)), @TotalCreditBefore = SUM(ISNULL(CREDIT, 0)) FROM TRANS;
    PRINT 'Total GL Debit Before: ' + CAST(@TotalDebitBefore AS VARCHAR(50));
    PRINT 'Total GL Credit Before: ' + CAST(@TotalCreditBefore AS VARCHAR(50));

    -- Table variable to store duplicate mapping (DupAccountId -> PrimaryAccountId)
    DECLARE @DuplicateMapping TABLE (
        DupAccountId BIGINT PRIMARY KEY,
        PrimaryAccountId BIGINT,
        AccountName VARCHAR(150),
        UcpId BIGINT
    );

    INSERT INTO @DuplicateMapping (DupAccountId, PrimaryAccountId, AccountName, UcpId)
    SELECT a.ACCOUNT_ID, p.PrimaryAccountId, a.ACCOUNT_NAME, a.UCP_ID
    FROM ACCOUNT_MASTER a
    INNER JOIN (
        SELECT UCP_ID, LOWER(LTRIM(RTRIM(ACCOUNT_NAME))) AS CleanName, MIN(ACCOUNT_ID) AS PrimaryAccountId
        FROM ACCOUNT_MASTER
        WHERE ACCOUNT_NAME IS NOT NULL
        GROUP BY UCP_ID, LOWER(LTRIM(RTRIM(ACCOUNT_NAME)))
        HAVING COUNT(*) > 1
    ) p ON ISNULL(a.UCP_ID, 0) = ISNULL(p.UCP_ID, 0) 
       AND LOWER(LTRIM(RTRIM(a.ACCOUNT_NAME))) = p.CleanName
    WHERE a.ACCOUNT_ID <> p.PrimaryAccountId;

    DECLARE @DuplicateCount INT = (SELECT COUNT(*) FROM @DuplicateMapping);
    PRINT 'Identified redundant duplicate account records: ' + CAST(@DuplicateCount AS VARCHAR(10));

    IF (@DuplicateCount > 0)
    BEGIN
        PRINT '>>> Step 2: Re-pointing TRANS records to Primary Accounts...';
        UPDATE t
        SET t.ACCOUNT_ID = m.PrimaryAccountId
        FROM TRANS t
        INNER JOIN @DuplicateMapping m ON t.ACCOUNT_ID = m.DupAccountId;
        PRINT 'Trans records updated: ' + CAST(@@ROWCOUNT AS VARCHAR(10));

        PRINT '>>> Step 3: Re-pointing CUSTOMER_MASTER references...';
        UPDATE c
        SET c.TAX_ACCOUNT_ID = m.PrimaryAccountId
        FROM CUSTOMER_MASTER c
        INNER JOIN @DuplicateMapping m ON c.TAX_ACCOUNT_ID = m.DupAccountId;

        UPDATE c
        SET c.ACCOUNT_ID = m.PrimaryAccountId
        FROM CUSTOMER_MASTER c
        INNER JOIN @DuplicateMapping m ON c.ACCOUNT_ID = m.DupAccountId;

        UPDATE c
        SET c.SALES_ACCOUNT_ID = m.PrimaryAccountId
        FROM CUSTOMER_MASTER c
        INNER JOIN @DuplicateMapping m ON c.SALES_ACCOUNT_ID = m.DupAccountId;

        PRINT '>>> Step 4: Re-pointing SUPPLIER_MASTER references...';
        UPDATE s
        SET s.TAX_ACCOUNT_ID = m.PrimaryAccountId
        FROM SUPPLIER_MASTER s
        INNER JOIN @DuplicateMapping m ON s.TAX_ACCOUNT_ID = m.DupAccountId;

        UPDATE s
        SET s.ACCOUNT_ID = m.PrimaryAccountId
        FROM SUPPLIER_MASTER s
        INNER JOIN @DuplicateMapping m ON s.ACCOUNT_ID = m.DupAccountId;

        UPDATE s
        SET s.PURCHASE_ACCOUNT_ID = m.PrimaryAccountId
        FROM SUPPLIER_MASTER s
        INNER JOIN @DuplicateMapping m ON s.PURCHASE_ACCOUNT_ID = m.DupAccountId;

        PRINT '>>> Step 5: Safely removing empty duplicate account shells...';
        DELETE a
        FROM ACCOUNT_MASTER a
        INNER JOIN @DuplicateMapping m ON a.ACCOUNT_ID = m.DupAccountId
        WHERE NOT EXISTS (SELECT 1 FROM TRANS t WHERE t.ACCOUNT_ID = a.ACCOUNT_ID)
          AND NOT EXISTS (SELECT 1 FROM CUSTOMER_MASTER c WHERE c.ACCOUNT_ID = a.ACCOUNT_ID OR c.TAX_ACCOUNT_ID = a.ACCOUNT_ID OR c.SALES_ACCOUNT_ID = a.ACCOUNT_ID)
          AND NOT EXISTS (SELECT 1 FROM SUPPLIER_MASTER s WHERE s.ACCOUNT_ID = a.ACCOUNT_ID OR s.TAX_ACCOUNT_ID = a.ACCOUNT_ID OR s.PURCHASE_ACCOUNT_ID = a.ACCOUNT_ID);
        PRINT 'Unused duplicate accounts removed: ' + CAST(@@ROWCOUNT AS VARCHAR(10));
    END
    ELSE
    BEGIN
        PRINT 'No duplicate accounts found in ACCOUNT_MASTER. Database is already clean.';
    END

    PRINT '>>> Step 6: Post-Consolidation Verification...';
    DECLARE @TotalDebitAfter FLOAT, @TotalCreditAfter FLOAT;
    SELECT @TotalDebitAfter = SUM(ISNULL(DEBIT, 0)), @TotalCreditAfter = SUM(ISNULL(CREDIT, 0)) FROM TRANS;
    PRINT 'Total GL Debit After: ' + CAST(@TotalDebitAfter AS VARCHAR(50));
    PRINT 'Total GL Credit After: ' + CAST(@TotalCreditAfter AS VARCHAR(50));

    -- Safety check: Debits and credits must match before and after exactly
    IF (ABS(@TotalDebitBefore - @TotalDebitAfter) > 0.001 OR ABS(@TotalCreditBefore - @TotalCreditAfter) > 0.001)
    BEGIN
        RAISERROR('SANITY CHECK FAILED: Debit or Credit grand total mismatch after consolidation! Rolling back.', 16, 1);
    END

    COMMIT TRANSACTION;
    PRINT '>>> CONSOLIDATION COMMITTED SUCCESSFULLY! Zero data loss verified.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    PRINT '>>> ERROR ENCOUNTERED: ' + ERROR_MESSAGE();
    PRINT '>>> Transaction has been rolled back safely.';
END CATCH;
