-- Pemasok
CREATE TABLE IF NOT EXISTS Suppliers (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT NOT NULL,
    ContactName TEXT,
    Phone       TEXT,
    Email       TEXT,
    Address     TEXT,
    Notes       TEXT,
    IsActive    INTEGER NOT NULL DEFAULT 1,
    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Purchase Order Header
CREATE TABLE IF NOT EXISTS PurchaseOrders (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    PoNumber        TEXT UNIQUE NOT NULL,
    SupplierId      INTEGER NOT NULL REFERENCES Suppliers(Id),
    Status          TEXT NOT NULL DEFAULT 'Draft'
                        CHECK(Status IN ('Draft','Sent','PartialReceived','Received','Cancelled')),
    OrderDate       TEXT NOT NULL DEFAULT (date('now')),
    ExpectedDate    TEXT,
    Notes           TEXT,
    CreatedByUserId INTEGER REFERENCES Users(Id),
    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Purchase Order Line Items
CREATE TABLE IF NOT EXISTS PurchaseOrderLines (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    PurchaseOrderId     INTEGER NOT NULL REFERENCES PurchaseOrders(Id) ON DELETE CASCADE,
    IngredientId        INTEGER NOT NULL REFERENCES Ingredients(Id),
    OrderedQty          REAL NOT NULL,
    ReceivedQty         REAL NOT NULL DEFAULT 0,
    UnitCostMinorUnits  INTEGER NOT NULL DEFAULT 0,
    Unit                TEXT NOT NULL
);

-- Goods Receipt (Penerimaan Barang)
CREATE TABLE IF NOT EXISTS GoodsReceipts (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    PurchaseOrderId INTEGER NOT NULL REFERENCES PurchaseOrders(Id),
    ReceivedDate    TEXT NOT NULL DEFAULT (date('now')),
    Notes           TEXT,
    CreatedByUserId INTEGER REFERENCES Users(Id),
    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS GoodsReceiptLines (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    GoodsReceiptId      INTEGER NOT NULL REFERENCES GoodsReceipts(Id) ON DELETE CASCADE,
    PurchaseOrderLineId INTEGER NOT NULL REFERENCES PurchaseOrderLines(Id),
    IngredientId        INTEGER NOT NULL REFERENCES Ingredients(Id),
    ReceivedQty         REAL NOT NULL,
    UnitCostMinorUnits  INTEGER NOT NULL DEFAULT 0,
    LocationId          INTEGER NOT NULL DEFAULT 1 REFERENCES StorageLocations(Id)
);
