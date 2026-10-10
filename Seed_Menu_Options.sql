-- =========================================================================================
-- SQL Script: Seed / Sync All System Functions & Menu Options into MENU_OPTIONS
-- Description:
--   1. Inserts all available system modules, pages, forms, vouchers, and reports.
--   2. Safe to run multiple times (Idempotent): Existing records will NOT be modified or duplicated.
--   3. Only missing / new menu options will be inserted.
-- =========================================================================================

SET NOCOUNT ON;

-- 1. Ensure Table Exists
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MENU_OPTIONS')
BEGIN
    CREATE TABLE MENU_OPTIONS (
        MO_ID BIGINT IDENTITY(1,1) PRIMARY KEY,
        MO_NAME VARCHAR(200) NULL,
        MO_TYPE VARCHAR(50) NULL,
        MO_URL NVARCHAR(500) NULL,
        MO_DEACTIVE_ON DATE NULL
    );
    PRINT 'Created Table [MENU_OPTIONS].';
END
GO

-- 2. Define All System Functions & Menus in Temporary Table
IF OBJECT_ID('tempdb..#SystemMenus') IS NOT NULL
    DROP TABLE #SystemMenus;

CREATE TABLE #SystemMenus (
    MoName VARCHAR(200),
    MoType VARCHAR(50),
    MoUrl NVARCHAR(500)
);

INSERT INTO #SystemMenus (MoName, MoType, MoUrl) VALUES
    -- === Dashboard & Core ===
    ('Dashboard', 'Page', '/Dashboard'),

    -- === Purchases Module ===
    ('Purchase SODA', 'Page', '/Purchase/IndexSoda'),
    ('Purchase SODA Entry', 'Page', '/Purchase/Soda'),
    ('Purchases', 'Page', '/Purchase/Index'),
    ('Purchase Entry', 'Page', '/Purchase/Entry'),
    ('Purchase Return (Debit Note)', 'Page', '/Purchase/PurchaseReturnIndex'),
    ('Purchase Return Entry', 'Page', '/Purchase/PurchaseReturn'),

    -- === Sales Module ===
    ('Sales SODA', 'Page', '/Sales/IndexSoda'),
    ('Sales SODA Entry', 'Page', '/Sales/Soda'),
    ('Sales', 'Page', '/Sales/Index'),
    ('Sales Entry', 'Page', '/Sales/Entry'),
    ('Sales Return (Credit Note)', 'Page', '/Sales/SaleReturnIndex'),
    ('Sales Return Entry', 'Page', '/Sales/SaleReturn'),
    ('Sales Invoice View', 'Page', '/Sales/InvoiceView'),

    -- === Parties Module ===
    ('Suppliers', 'Page', '/Suppliers/Index'),
    ('Create / Edit Supplier', 'Page', '/Suppliers/CreateSupplier'),
    ('Customers', 'Page', '/Customers/Index'),
    ('Create / Edit Customer', 'Page', '/Customers/CreateCustomer'),

    -- === Inventory Module ===
    ('Items', 'Page', '/Items/Index'),
    ('Create / Edit Item', 'Page', '/Items/CreateItem'),

    -- === Accounting & Ledgers ===
    ('Charts of Accounts', 'Page', '/Accounts/Index'),
    ('Create / Edit Account', 'Page', '/Accounts/CreateAccount'),
    ('Supplier Ledger', 'Page', '/Ledger/SupplierLedger'),
    ('Customer Ledger', 'Page', '/Ledger/CustomerLedger'),
    ('Account Ledger', 'Page', '/Ledger/AccountLedger'),

    -- === Categories & Chart of Accounts Setup ===
    ('Categories Master', 'Page', '/Categories/CategoriesMaster'),
    ('Create / Edit Category Master', 'Page', '/Categories/CreateCategoryMaster'),
    ('Main Categories', 'Page', '/Categories/MainCategories'),
    ('Create / Edit Main Category', 'Page', '/Categories/CreateMainCategory'),
    ('Sub Categories', 'Page', '/Categories/SubCategories'),
    ('Create / Edit Sub Category', 'Page', '/Categories/CreateSubCategory'),
    ('Category Types', 'Page', '/Categories/CategoryTypes'),
    ('Create / Edit Category Type', 'Page', '/Categories/CreateCategoryType'),

    -- === Vouchers Module ===
    ('Payment Vouchers', 'Page', '/Vouchers/PaymentVoucherIndex'),
    ('Payment Voucher Entry', 'Page', '/Vouchers/PaymentVoucher'),
    ('Receipt Vouchers', 'Page', '/Vouchers/ReceiptVoucherIndex'),
    ('Receipt Voucher Entry', 'Page', '/Vouchers/ReceiptVoucher'),
    ('Journal Vouchers', 'Page', '/Vouchers/JournalVoucherIndex'),
    ('Journal Voucher Entry', 'Page', '/Vouchers/JournalVoucher'),

    -- === Reports & Tracking Module ===
    ('Customer Aging (AR)', 'Page', '/Reports/CustomerAging'),
    ('Supplier Aging (AP)', 'Page', '/Reports/SupplierAging'),
    ('General Aging Report', 'Page', '/Reports/AgingReport'),
    ('Purchase Movement (PSD)', 'Page', '/Reports/PsdTrackIndex'),
    ('PSD Record Track Detail', 'Page', '/Reports/PsdRecordTrack'),
    ('Sales Movement (SSD)', 'Page', '/Reports/SsdTrackIndex'),
    ('SSD Record Track Detail', 'Page', '/Reports/SsdTrackRecord'),
    ('Purchase Register', 'Page', '/Reports/PurchaseRegister'),
    ('Sale Register', 'Page', '/Reports/SaleRegister'),
    ('Stock Report', 'Page', '/Reports/Stock'),
    ('Item Ledger Report', 'Page', '/Reports/ItemLedger'),
    ('Supplier Pending Bags', 'Page', '/Reports/SupplierPendingBags'),
    ('Customer Pending Bags', 'Page', '/Reports/CustomerPendingBags'),

    -- === Finance & Invoices ===
    ('Invoices', 'Page', '/Invoices'),

    -- === Business & Company Setup ===
    ('Seller Accounts', 'Page', '/Sellers/Index'),
    ('Create / Edit Seller', 'Page', '/Sellers/Create'),
    ('Company Profile(s)', 'Page', '/CompanyProfile/Index'),
    ('Settings', 'Page', '/Settings'),

    -- === System Configuration & Security ===
    ('Users', 'Page', '/Configuration/Users/Index'),
    ('Create / Edit User', 'Page', '/Configuration/Users/CreateUser'),
    ('User Roles', 'Page', '/Configuration/UserRoles/Index'),
    ('Create / Edit User Role', 'Page', '/Configuration/UserRoles/CreateUserRole'),
    ('User Role Assignments', 'Page', '/Configuration/UserRoleAssignments/CreateUserRoleAssignment'),
    ('Menus', 'Page', '/Configuration/Menus/Index'),
    ('Create / Edit Menu', 'Page', '/Configuration/Menus/CreateMenu');

-- 3. Insert only those that DO NOT already exist (by MO_URL)
DECLARE @InsertedCount INT = 0;

INSERT INTO MENU_OPTIONS (MO_NAME, MO_TYPE, MO_URL, MO_DEACTIVE_ON)
SELECT 
    s.MoName,
    s.MoType,
    s.MoUrl,
    NULL
FROM #SystemMenus s
WHERE NOT EXISTS (
    SELECT 1 
    FROM MENU_OPTIONS m 
    WHERE LOWER(LTRIM(RTRIM(m.MO_URL))) = LOWER(LTRIM(RTRIM(s.MoUrl)))
);

SET @InsertedCount = @@ROWCOUNT;

PRINT '---------------------------------------------------------';
PRINT 'Sync Completed!';
PRINT 'New Menu Option(s) Inserted: ' + CAST(@InsertedCount AS VARCHAR(10));
PRINT 'Existing records were kept completely untouched.';
PRINT '---------------------------------------------------------';

-- 4. Clean up
DROP TABLE #SystemMenus;
GO

-- 5. Preview current state of MENU_OPTIONS
SELECT 
    MO_ID AS [ID],
    MO_NAME AS [Menu Name],
    MO_TYPE AS [Type],
    MO_URL AS [URL],
    MO_DEACTIVE_ON AS [Deactivated On]
FROM MENU_OPTIONS
ORDER BY MO_ID;
GO
