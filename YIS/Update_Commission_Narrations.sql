-- =========================================================================================
-- SAFE PRODUCTION DB SCRIPT: UPDATE COMMISSION NARRATIONS (PURCHASE VS SALES)
-- Description: Updates narration text in TRANS table:
--              1. Purchase (PUR & DN): Changes 'Commission Payable' -> 'Commission Receivable'
--              2. Sales (SAL & CN): Confirms 'Commission Payable'
-- Safety:
--   1. Zero amount alteration: DEBIT, CREDIT, ACCOUNT_ID, TRANS_ID, TRANS_NO, ENTRY_ID untouched.
--   2. Atomic TRANSACTION with sanity check and auto-rollback on error.
--   3. Checks total debits and credits before and after to guarantee zero balance change.
-- =========================================================================================

SET NOCOUNT ON;
BEGIN TRANSACTION;

BEGIN TRY
    PRINT '>>> Step 1: Pre-Update Sanity Check...';
    
    -- Check total GL balance before update
    DECLARE @TotalDebitBefore FLOAT, @TotalCreditBefore FLOAT;
    SELECT @TotalDebitBefore = SUM(ISNULL(DEBIT, 0)), @TotalCreditBefore = SUM(ISNULL(CREDIT, 0)) FROM TRANS;
    PRINT 'Total GL Debit Before: ' + CAST(@TotalDebitBefore AS VARCHAR(50));
    PRINT 'Total GL Credit Before: ' + CAST(@TotalCreditBefore AS VARCHAR(50));

    -- Count affected rows
    DECLARE @PurRows INT, @DnRows INT, @SalRows INT, @CnRows INT;
    SELECT @PurRows = COUNT(*) FROM TRANS WHERE TRANS_TYPE = 'PUR' AND REMARKS LIKE '%Commission Payable%';
    SELECT @DnRows = COUNT(*) FROM TRANS WHERE TRANS_TYPE = 'DN' AND REMARKS LIKE '%Commission Payable%';
    SELECT @SalRows = COUNT(*) FROM TRANS WHERE TRANS_TYPE = 'SAL' AND REMARKS LIKE '%Commission Receivable%';
    SELECT @CnRows = COUNT(*) FROM TRANS WHERE TRANS_TYPE = 'CN' AND REMARKS LIKE '%Commission Receivable%';

    PRINT 'Rows to update in Purchase (PUR): ' + CAST(@PurRows AS VARCHAR(10));
    PRINT 'Rows to update in Purchase Return (DN): ' + CAST(@DnRows AS VARCHAR(10));

    -- Step 2: Update Purchase Invoices (PUR)
    UPDATE TRANS
    SET REMARKS = REPLACE(REMARKS, 'Commission Expense against Purchase', 'Commission Receivable against Purchase')
    WHERE TRANS_TYPE = 'PUR'
      AND REMARKS LIKE '%Commission Expense against Purchase%';

    UPDATE TRANS
    SET REMARKS = REPLACE(REMARKS, 'Commission Payable to', 'Commission Receivable from')
    WHERE TRANS_TYPE = 'PUR'
      AND REMARKS LIKE '%Commission Payable to%';

    UPDATE TRANS
    SET REMARKS = REPLACE(REMARKS, 'Commission Payable', 'Commission Receivable')
    WHERE TRANS_TYPE = 'PUR'
      AND REMARKS LIKE '%Commission Payable%';

    -- Step 3: Update Purchase Returns (DN)
    UPDATE TRANS
    SET REMARKS = REPLACE(REMARKS, 'Commission Expense Reversal', 'Commission Receivable Reversal')
    WHERE TRANS_TYPE = 'DN'
      AND REMARKS LIKE '%Commission Expense Reversal%';

    UPDATE TRANS
    SET REMARKS = REPLACE(REMARKS, 'Commission Payable Reversal', 'Commission Receivable Reversal')
    WHERE TRANS_TYPE = 'DN'
      AND REMARKS LIKE '%Commission Payable Reversal%';

    UPDATE TRANS
    SET REMARKS = REPLACE(REMARKS, 'Commission Payable', 'Commission Receivable')
    WHERE TRANS_TYPE = 'DN'
      AND REMARKS LIKE '%Commission Payable%';

    -- Step 4: Ensure Sales (SAL & CN) use Commission Payable
    IF (@SalRows > 0)
    BEGIN
        UPDATE TRANS
        SET REMARKS = REPLACE(REMARKS, 'Commission Receivable from', 'Commission Payable to')
        WHERE TRANS_TYPE = 'SAL'
          AND REMARKS LIKE '%Commission Receivable from%';

        UPDATE TRANS
        SET REMARKS = REPLACE(REMARKS, 'Commission Receivable', 'Commission Payable')
        WHERE TRANS_TYPE = 'SAL'
          AND REMARKS LIKE '%Commission Receivable%';
    END

    IF (@CnRows > 0)
    BEGIN
        UPDATE TRANS
        SET REMARKS = REPLACE(REMARKS, 'Commission Receivable Reversal', 'Commission Payable Reversal')
        WHERE TRANS_TYPE = 'CN'
          AND REMARKS LIKE '%Commission Receivable Reversal%';

        UPDATE TRANS
        SET REMARKS = REPLACE(REMARKS, 'Commission Receivable', 'Commission Payable')
        WHERE TRANS_TYPE = 'CN'
          AND REMARKS LIKE '%Commission Receivable%';
    END

    -- Step 5: Post-Update Verification
    PRINT '>>> Step 5: Post-Update Verification...';
    DECLARE @TotalDebitAfter FLOAT, @TotalCreditAfter FLOAT;
    SELECT @TotalDebitAfter = SUM(ISNULL(DEBIT, 0)), @TotalCreditAfter = SUM(ISNULL(CREDIT, 0)) FROM TRANS;
    PRINT 'Total GL Debit After: ' + CAST(@TotalDebitAfter AS VARCHAR(50));
    PRINT 'Total GL Credit After: ' + CAST(@TotalCreditAfter AS VARCHAR(50));

    -- Safety check: Debits and credits must match before and after exactly
    IF (ABS(@TotalDebitBefore - @TotalDebitAfter) > 0.001 OR ABS(@TotalCreditBefore - @TotalCreditAfter) > 0.001)
    BEGIN
        RAISERROR('SANITY CHECK FAILED: Debit or Credit total changed! Rolling back.', 16, 1);
    END

    COMMIT TRANSACTION;
    PRINT '>>> UPDATE COMMITTED SUCCESSFULLY! Zero amount alteration verified.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    PRINT '>>> ERROR ENCOUNTERED: ' + ERROR_MESSAGE();
    PRINT '>>> Transaction has been rolled back safely.';
END CATCH;
