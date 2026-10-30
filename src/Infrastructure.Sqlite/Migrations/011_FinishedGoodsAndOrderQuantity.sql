-- Migration 011: Jumlah pesanan & stok produk jadi
-- Keputusan bisnis: produk jadi punya stok sendiri. SPK selesai menambah stok produk jadi,
-- penjualan menguranginya; bahan mentah tidak lagi dikurangi saat pesanan dijual.
-- Lokasi penyimpanan produk jadi adalah lokasi bertipe Display (etalase toko), bahan mentah tetap
-- di Warehouse/Kitchen, jadi satu baris LocationStock tidak pernah dipakai untuk dua hal berbeda.

ALTER TABLE SalesOrders ADD COLUMN Quantity REAL NOT NULL DEFAULT 1;

-- Quantity ditambahkan sebagai kolom baru, dan CHECK pada ALTER TABLE ADD COLUMN tidak dijamin
-- tersedia di semua versi SQLite, jadi aturannya ditegakkan lewat pemicu seperti migrasi 008.
CREATE TRIGGER IF NOT EXISTS trg_salesorders_quantity_insert
BEFORE INSERT ON SalesOrders
BEGIN
    SELECT RAISE(ABORT, 'Jumlah pesanan harus lebih dari nol') WHERE NEW.Quantity <= 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_salesorders_quantity_update
BEFORE UPDATE OF Quantity ON SalesOrders
BEGIN
    SELECT RAISE(ABORT, 'Jumlah pesanan harus lebih dari nol') WHERE NEW.Quantity <= 0;
END;

-- Stok fisik produk jadi per lokasi. Sama seperti LocationStock: pemicu tulis menulis saldo dan
-- bukunya dalam satu transaksi, jadi saldo selalu bisa direkonstruksi dari mutasinya.
CREATE TABLE IF NOT EXISTS FinishedGoodsStock (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    LocationId        INTEGER NOT NULL REFERENCES StorageLocations(Id),
    ProductTemplateId INTEGER NOT NULL REFERENCES ProductTemplates(Id),
    Quantity          REAL NOT NULL DEFAULT 0 CHECK (Quantity >= 0),
    UpdatedAt         TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE(LocationId, ProductTemplateId)
);

CREATE TABLE IF NOT EXISTS FinishedGoodsMovements (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductTemplateId  INTEGER NOT NULL REFERENCES ProductTemplates(Id),
    LocationId         INTEGER NOT NULL REFERENCES StorageLocations(Id),
    MovementType       TEXT NOT NULL
                           CHECK(MovementType IN ('Production','Sale','CancelRestore','Adjustment')),
    Quantity           REAL NOT NULL,
    UnitCostMinorUnits INTEGER NOT NULL DEFAULT 0,
    SourceType         TEXT,
    SourceId           INTEGER,
    Notes              TEXT,
    CreatedAt          TEXT NOT NULL DEFAULT (datetime('now')),
    CreatedByUserId    INTEGER REFERENCES Users(Id)
);

CREATE INDEX IF NOT EXISTS idx_fgstock_loc_prod ON FinishedGoodsStock(LocationId, ProductTemplateId);
CREATE INDEX IF NOT EXISTS idx_fgmovement_loc_prod ON FinishedGoodsMovements(LocationId, ProductTemplateId, CreatedAt);

-- Baris pesanan lama sebelum ada jumlah: nilainya 1 (butir) agar saldo produk jadi tidak berubah
-- diam-diam ketika pesanan lama nanti dipindahkan statusnya.
UPDATE SalesOrders SET Quantity = 1 WHERE Quantity IS NULL OR Quantity <= 0;
