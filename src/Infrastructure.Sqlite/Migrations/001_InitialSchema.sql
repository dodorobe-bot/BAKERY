-- Seruni 22 Bakery - Schema Migration v001
-- Created: 2026-10-24

PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA busy_timeout = 5000;

CREATE TABLE IF NOT EXISTS SchemaVersion (
    Version     INTEGER PRIMARY KEY,
    AppliedAt   TEXT NOT NULL
);

-- =============================================
-- IDENTITY & SECURITY
-- =============================================
CREATE TABLE IF NOT EXISTS Users (
    Id                  INTEGER PRIMARY KEY,
    LegacyId            INTEGER,
    Username            TEXT NOT NULL UNIQUE COLLATE NOCASE,
    PasswordHash        TEXT NOT NULL,
    Role                TEXT NOT NULL CHECK(Role IN ('Owner','Admin','Dapur','Pegawai')),
    IsActive            INTEGER NOT NULL DEFAULT 1,
    MustChangePassword  INTEGER NOT NULL DEFAULT 0,
    CreatedAt           TEXT NOT NULL DEFAULT (datetime('now')),
    LastLoginAt         TEXT,
    FailedLoginCount    INTEGER NOT NULL DEFAULT 0,
    LockedUntil         TEXT
);

CREATE TABLE IF NOT EXISTS AuditLog (
    Id          INTEGER PRIMARY KEY,
    UserId      INTEGER REFERENCES Users(Id),
    Action      TEXT NOT NULL,
    EntityType  TEXT NOT NULL,
    EntityId    INTEGER,
    OldValues   TEXT,
    NewValues   TEXT,
    OccurredAt  TEXT NOT NULL DEFAULT (datetime('now')),
    SessionId   TEXT
);

-- =============================================
-- COMPANY & SETTINGS
-- =============================================
CREATE TABLE IF NOT EXISTS AppSettings (
    Key                 TEXT PRIMARY KEY,
    Value               TEXT,
    UpdatedAt           TEXT,
    UpdatedByUserId     INTEGER REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS FiscalPeriods (
    Id              INTEGER PRIMARY KEY,
    Name            TEXT NOT NULL,
    StartDate       TEXT NOT NULL,
    EndDate         TEXT NOT NULL,
    Status          TEXT NOT NULL DEFAULT 'Open' CHECK(Status IN ('Open','Closed','Locked')),
    ClosedAt        TEXT,
    ClosedByUserId  INTEGER REFERENCES Users(Id)
);

-- =============================================
-- CUSTOMERS
-- =============================================
CREATE TABLE IF NOT EXISTS Customers (
    Id              INTEGER PRIMARY KEY,
    LegacyId        INTEGER,
    Name            TEXT NOT NULL,
    Phone           TEXT UNIQUE,
    Email           TEXT,
    Notes           TEXT,
    LoyaltyPoints   INTEGER NOT NULL DEFAULT 0,
    IsActive        INTEGER NOT NULL DEFAULT 1,
    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now'))
);

-- =============================================
-- INVENTORY
-- =============================================
CREATE TABLE IF NOT EXISTS Ingredients (
    Id              INTEGER PRIMARY KEY,
    LegacyId        INTEGER,
    Name            TEXT NOT NULL,
    Brand           TEXT,
    Unit            TEXT NOT NULL,
    PricePerUnit    INTEGER NOT NULL DEFAULT 0,
    CurrentStock    REAL NOT NULL DEFAULT 0,
    MinStockAlert   REAL NOT NULL DEFAULT 0,
    IsActive        INTEGER NOT NULL DEFAULT 1,
    UpdatedAt       TEXT
);

CREATE TABLE IF NOT EXISTS StockMovements (
    Id                  INTEGER PRIMARY KEY,
    IngredientId        INTEGER NOT NULL REFERENCES Ingredients(Id),
    MovementType        TEXT NOT NULL CHECK(MovementType IN ('Deduction','Restock','Adjustment','CancelRestore')),
    Quantity            REAL NOT NULL,
    SourceType          TEXT,
    SourceId            INTEGER,
    Notes               TEXT,
    CreatedAt           TEXT NOT NULL DEFAULT (datetime('now')),
    CreatedByUserId     INTEGER REFERENCES Users(Id)
);

-- =============================================
-- PRODUCTS & RECIPES
-- =============================================
CREATE TABLE IF NOT EXISTS ProductTemplates (
    Id              INTEGER PRIMARY KEY,
    LegacyId        INTEGER,
    Name            TEXT NOT NULL,
    Category        TEXT,
    SellingPrice    INTEGER NOT NULL DEFAULT 0,
    IsActive        INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS RecipeLines (
    Id                  INTEGER PRIMARY KEY,
    ProductTemplateId   INTEGER NOT NULL REFERENCES ProductTemplates(Id) ON DELETE CASCADE,
    IngredientId        INTEGER NOT NULL REFERENCES Ingredients(Id) ON DELETE RESTRICT,
    Quantity            REAL NOT NULL
);

-- =============================================
-- SALES
-- =============================================
CREATE TABLE IF NOT EXISTS SalesOrders (
    Id                      INTEGER PRIMARY KEY,
    LegacyId                INTEGER,
    OrderNumber             TEXT UNIQUE,
    CustomerId              INTEGER REFERENCES Customers(Id) ON DELETE SET NULL,
    ProductTemplateId       INTEGER REFERENCES ProductTemplates(Id) ON DELETE SET NULL,
    CustomerNameSnapshot    TEXT NOT NULL,
    PhoneSnapshot           TEXT,
    Description             TEXT NOT NULL,
    PickupDate              TEXT NOT NULL,
    TotalAmount             INTEGER NOT NULL,
    DownPayment             INTEGER NOT NULL DEFAULT 0,
    Status                  TEXT NOT NULL DEFAULT 'Pending'
                                CHECK(Status IN ('Pending','Processing','Ready','Paid','Cancelled')),
    StockDeducted           INTEGER NOT NULL DEFAULT 0,
    Notes                   TEXT,
    CancelledAt             TEXT,
    CancelReason            TEXT,
    CreatedAt               TEXT NOT NULL DEFAULT (datetime('now')),
    CreatedByUserId         INTEGER REFERENCES Users(Id)
);

CREATE TABLE IF NOT EXISTS SalesPayments (
    Id                  INTEGER PRIMARY KEY,
    SalesOrderId        INTEGER NOT NULL REFERENCES SalesOrders(Id) ON DELETE RESTRICT,
    Amount              INTEGER NOT NULL,
    PaymentDate         TEXT NOT NULL,
    Method              TEXT NOT NULL DEFAULT 'Cash',
    Notes               TEXT,
    JournalEntryId      INTEGER,
    CreatedAt           TEXT NOT NULL DEFAULT (datetime('now')),
    CreatedByUserId     INTEGER REFERENCES Users(Id),
    IdempotencyKey      TEXT UNIQUE
);

-- =============================================
-- ACCOUNTING (DOUBLE-ENTRY)
-- =============================================
CREATE TABLE IF NOT EXISTS Accounts (
    Id          INTEGER PRIMARY KEY,
    LegacyId    INTEGER,
    Code        TEXT NOT NULL UNIQUE,
    Name        TEXT NOT NULL,
    Type        TEXT NOT NULL CHECK(Type IN ('Asset','Liability','Equity','Revenue','Expense')),
    ParentId    INTEGER REFERENCES Accounts(Id),
    IsSystem    INTEGER NOT NULL DEFAULT 0,
    IsActive    INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS JournalEntries (
    Id                  INTEGER PRIMARY KEY,
    Description         TEXT,
    TransactionDate     TEXT NOT NULL,
    SourceDocumentType  TEXT,
    SourceDocumentId    INTEGER,
    Status              TEXT NOT NULL DEFAULT 'Posted'
                            CHECK(Status IN ('Draft','Posted','Reversed')),
    ReversalOfId        INTEGER REFERENCES JournalEntries(Id),
    PostedAt            TEXT,
    PostedByUserId      INTEGER REFERENCES Users(Id),
    IdempotencyKey      TEXT UNIQUE,
    CreatedAt           TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS JournalLines (
    Id              INTEGER PRIMARY KEY,
    JournalEntryId  INTEGER NOT NULL REFERENCES JournalEntries(Id) ON DELETE CASCADE,
    AccountId       INTEGER NOT NULL REFERENCES Accounts(Id),
    DebitAmount     INTEGER NOT NULL DEFAULT 0,
    CreditAmount    INTEGER NOT NULL DEFAULT 0,
    Notes           TEXT,
    CHECK (DebitAmount >= 0 AND CreditAmount >= 0),
    CHECK (DebitAmount = 0 OR CreditAmount = 0)
);

-- =============================================
-- INDEXES
-- =============================================
CREATE INDEX IF NOT EXISTS idx_salesorders_pickupdate   ON SalesOrders(PickupDate);
CREATE INDEX IF NOT EXISTS idx_salesorders_status       ON SalesOrders(Status);
CREATE INDEX IF NOT EXISTS idx_salesorders_customerid   ON SalesOrders(CustomerId);
CREATE INDEX IF NOT EXISTS idx_journallines_accountid   ON JournalLines(AccountId);
CREATE INDEX IF NOT EXISTS idx_journallines_entryid     ON JournalLines(JournalEntryId);
CREATE INDEX IF NOT EXISTS idx_journalentries_date      ON JournalEntries(TransactionDate);
CREATE INDEX IF NOT EXISTS idx_stockmovements_ingredient ON StockMovements(IngredientId);
CREATE INDEX IF NOT EXISTS idx_auditlog_entity          ON AuditLog(EntityType, EntityId);
CREATE INDEX IF NOT EXISTS idx_customers_phone          ON Customers(Phone);
