-- Seruni 22 Bakery - Production & Warehouse Architecture v003
-- Supports: Multi-Location (Warehouse vs Kitchen), Multi-Unit, FEFO Batches, Production Orders (SPK), and Scrap/Rework Tracking

-- 1. Lokasi Penyimpanan (Gudang vs Dapur vs Etalase Toko)
CREATE TABLE IF NOT EXISTS StorageLocations (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Code        TEXT UNIQUE NOT NULL,           -- 'WH-MAIN', 'KIT-DAPUR', 'STORE-DISPLAY'
    Name        TEXT NOT NULL,
    Type        TEXT NOT NULL CHECK(Type IN ('Warehouse', 'Kitchen', 'Display')),
    IsActive    INTEGER NOT NULL DEFAULT 1
);

INSERT OR IGNORE INTO StorageLocations (Id, Code, Name, Type) VALUES
(1, 'WH-MAIN', 'Gudang Utama Bahan Baku', 'Warehouse'),
(2, 'KIT-DAPUR', 'Dapur Produksi', 'Kitchen'),
(3, 'STORE-DISPLAY', 'Etalase Toko & Kasir', 'Display');

-- 2. Satuan & Konversi Baku
CREATE TABLE IF NOT EXISTS Units (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Code        TEXT UNIQUE NOT NULL,           -- 'g', 'kg', 'sak', 'butir', 'pcs'
    Name        TEXT NOT NULL
);

INSERT OR IGNORE INTO Units (Id, Code, Name) VALUES
(1, 'g', 'Gram'),
(2, 'kg', 'Kilogram'),
(3, 'sak', 'Sak / Karung'),
(4, 'butir', 'Butir'),
(5, 'pcs', 'Pcs / Buah');

CREATE TABLE IF NOT EXISTS UnitConversions (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    IngredientId    INTEGER REFERENCES Ingredients(Id) ON DELETE CASCADE,
    FromUnit        TEXT NOT NULL,
    ToUnit          TEXT NOT NULL,
    Multiplier      REAL NOT NULL
);

-- 3. Batch Bahan Baku & Kedaluwarsa (FEFO)
CREATE TABLE IF NOT EXISTS ItemBatches (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    IngredientId        INTEGER NOT NULL REFERENCES Ingredients(Id) ON DELETE RESTRICT,
    BatchNumber         TEXT NOT NULL,
    ExpiryDate          TEXT NOT NULL,
    ReceivedDate        TEXT NOT NULL DEFAULT (date('now')),
    UnitCostMinorUnits  INTEGER NOT NULL DEFAULT 0
);

-- 4. Stok Fisik Multi-Lokasi
CREATE TABLE IF NOT EXISTS LocationStock (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    LocationId      INTEGER NOT NULL REFERENCES StorageLocations(Id),
    IngredientId    INTEGER NOT NULL REFERENCES Ingredients(Id),
    Quantity        REAL NOT NULL DEFAULT 0,
    UNIQUE(LocationId, IngredientId)
);

-- 5. Surat Perintah Kerja Produksi (SPK / Work Order)
CREATE TABLE IF NOT EXISTS WorkOrders (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderNumber         TEXT UNIQUE NOT NULL,
    ProductTemplateId   INTEGER NOT NULL REFERENCES ProductTemplates(Id),
    TargetQuantity      INTEGER NOT NULL,
    ActualGoodQty       INTEGER NOT NULL DEFAULT 0,
    ActualFailedQty     INTEGER NOT NULL DEFAULT 0,
    Status              TEXT NOT NULL DEFAULT 'Planned' 
                            CHECK(Status IN ('Planned', 'In_Process', 'Completed', 'Cancelled')),
    TotalMaterialCost   INTEGER NOT NULL DEFAULT 0,
    LaborCost           INTEGER NOT NULL DEFAULT 0,
    OverheadCost        INTEGER NOT NULL DEFAULT 0,
    StartDate           TEXT,
    CompletedDate       TEXT,
    CreatedByUserId     INTEGER REFERENCES Users(Id),
    CreatedAt           TEXT NOT NULL DEFAULT (datetime('now'))
);

-- 6. Detail Pemakaian Aktual Bahan Baku per SPK
CREATE TABLE IF NOT EXISTS WorkOrderMaterials (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    WorkOrderId     INTEGER NOT NULL REFERENCES WorkOrders(Id) ON DELETE CASCADE,
    IngredientId    INTEGER NOT NULL REFERENCES Ingredients(Id),
    StandardQty     REAL NOT NULL,
    ActualQty       REAL NOT NULL,
    UnitCost        INTEGER NOT NULL DEFAULT 0
);

-- 7. Pencatatan Scrap / Produk Gagal & Rework
CREATE TABLE IF NOT EXISTS WorkOrderScraps (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    WorkOrderId     INTEGER NOT NULL REFERENCES WorkOrders(Id) ON DELETE CASCADE,
    Quantity        INTEGER NOT NULL,
    ReasonCategory  TEXT NOT NULL CHECK(ReasonCategory IN ('Gosong', 'Adonan_Gagal', 'Salah_Resep', 'Mesin_Error', 'Lainnya')),
    ActionTaken     TEXT NOT NULL CHECK(ActionTaken IN ('Scrap_Loss', 'Rework')),
    Notes           TEXT,
    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now')),
    CreatedByUserId INTEGER REFERENCES Users(Id)
);

-- 8. Tambahan Akun Akuntansi Pabrikasi ke Chart of Accounts
INSERT OR IGNORE INTO Accounts (Id, Code, Name, Type, IsSystem, IsActive) VALUES
(9,  '1-003', 'Persediaan Bahan Baku (Gudang)',  'Asset',   1, 1),
(10, '1-004', 'Persediaan Dapur',                'Asset',   1, 1),
(11, '1-005', 'Barang Dalam Proses (WIP)',       'Asset',   1, 1),
(12, '1-006', 'Persediaan Produk Jadi (Etalase)','Asset',   1, 1),
(13, '5-004', 'Beban Kerugian Produk Rusak',     'Expense', 1, 1);

-- Indexes untuk akselerasi query stok & produksi
CREATE INDEX IF NOT EXISTS idx_locationstock_loc_ing ON LocationStock(LocationId, IngredientId);
CREATE INDEX IF NOT EXISTS idx_workorders_status ON WorkOrders(Status);
CREATE INDEX IF NOT EXISTS idx_workorder_materials_wo ON WorkOrderMaterials(WorkOrderId);
CREATE INDEX IF NOT EXISTS idx_workorder_scraps_wo ON WorkOrderScraps(WorkOrderId);
