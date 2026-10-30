-- Migration 013: Sesi yang bisa dicabut, pemeriksaan mutu, dan retur penjualan
-- Tiga hal yang sebelumnya hanya ada di layar atau tidak ada sama sekali:
-- 1) token keluar aplikasi masih sah sampai kedaluwarsa; 2) menu QC tidak punya tempat
-- menyimpan hasil; 3) retur belum punya dokumen, stok balik, atau jurnal.

-- Versi token per pengguna. Nilai ini ikut dicetak di JWT dan dibandingkan pada setiap
-- permintaan; logout dan ganti sandi menaikkannya sehingga token lama langsung mati.
ALTER TABLE Users ADD COLUMN TokenVersion INTEGER NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS QualityChecks (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    WorkOrderId      INTEGER NOT NULL UNIQUE REFERENCES WorkOrders(Id),
    CheckedAt        TEXT NOT NULL DEFAULT (datetime('now')),
    CheckedByUserId  INTEGER REFERENCES Users(Id),
    SampleQuantity   INTEGER NOT NULL,
    PassedQuantity   INTEGER NOT NULL,
    FailedQuantity   INTEGER NOT NULL,
    Decision         TEXT NOT NULL CHECK(Decision IN ('Passed','Failed')),
    AppearanceNotes  TEXT,
    TasteNotes       TEXT,
    Notes            TEXT,
    CreatedAt        TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_qualitychecks_checked ON QualityChecks(CheckedAt);
CREATE INDEX IF NOT EXISTS idx_qualitychecks_decision ON QualityChecks(Decision, CheckedAt);

-- Aturan hasil QC ditegakkan di basis data, bukan hanya di form: jumlah lulus dan gagal
-- tidak boleh melebihi sampel, dan keputusan tidak boleh bertentangan dengan angka gagal.
CREATE TRIGGER IF NOT EXISTS trg_qualitychecks_insert
BEFORE INSERT ON QualityChecks
BEGIN
    SELECT RAISE(ABORT, 'Jumlah sampel QC harus lebih dari nol')
        WHERE NEW.SampleQuantity <= 0;
    SELECT RAISE(ABORT, 'Jumlah lulus/gagal QC tidak boleh negatif')
        WHERE NEW.PassedQuantity < 0 OR NEW.FailedQuantity < 0;
    SELECT RAISE(ABORT, 'Jumlah lulus + gagal melebihi jumlah sampel')
        WHERE NEW.PassedQuantity + NEW.FailedQuantity > NEW.SampleQuantity;
    SELECT RAISE(ABORT, 'Keputusan QC tidak sesuai hasil pemeriksaan')
        WHERE NEW.Decision != (CASE WHEN NEW.FailedQuantity = 0 THEN 'Passed' ELSE 'Failed' END);
END;

CREATE TRIGGER IF NOT EXISTS trg_qualitychecks_update
BEFORE UPDATE OF SampleQuantity, PassedQuantity, FailedQuantity, Decision ON QualityChecks
BEGIN
    SELECT RAISE(ABORT, 'Jumlah sampel QC harus lebih dari nol')
        WHERE NEW.SampleQuantity <= 0;
    SELECT RAISE(ABORT, 'Jumlah lulus/gagal QC tidak boleh negatif')
        WHERE NEW.PassedQuantity < 0 OR NEW.FailedQuantity < 0;
    SELECT RAISE(ABORT, 'Jumlah lulus + gagal melebihi jumlah sampel')
        WHERE NEW.PassedQuantity + NEW.FailedQuantity > NEW.SampleQuantity;
    SELECT RAISE(ABORT, 'Keputusan QC tidak sesuai hasil pemeriksaan')
        WHERE NEW.Decision != (CASE WHEN NEW.FailedQuantity = 0 THEN 'Passed' ELSE 'Failed' END);
END;

-- Retur penjualan: satu baris = satu pengembalian untuk satu pesanan. Pesanan di aplikasi
-- ini memuat satu produk katalog, jadi jumlah dan nilai yang diretur cukup disimpan di sini.
CREATE TABLE IF NOT EXISTS SalesReturns (
    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnNumber      TEXT NOT NULL UNIQUE,
    SalesOrderId      INTEGER NOT NULL REFERENCES SalesOrders(Id),
    ProductTemplateId INTEGER NOT NULL REFERENCES ProductTemplates(Id),
    Quantity          REAL NOT NULL,
    Amount            INTEGER NOT NULL,
    Reason            TEXT NOT NULL,
    RefundMethod      TEXT NOT NULL DEFAULT 'Tunai',
    ReturnedAt        TEXT NOT NULL DEFAULT (datetime('now')),
    JournalEntryId    INTEGER REFERENCES JournalEntries(Id),
    IdempotencyKey    TEXT UNIQUE,
    CreatedByUserId   INTEGER REFERENCES Users(Id),
    CreatedAt         TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_salesreturns_order ON SalesReturns(SalesOrderId, ReturnedAt);
CREATE INDEX IF NOT EXISTS idx_salesreturns_returned ON SalesReturns(ReturnedAt);

-- Pemicu kumulatif: retur kedua untuk pesanan yang sama tidak boleh melebihi jumlah yang
-- dibeli maupun nilai pesanan, walaupun dua permintaan datang berbarengan dan masing-masing
-- sudah lolos pemeriksaan di layanan.
CREATE TRIGGER IF NOT EXISTS trg_salesreturns_insert
BEFORE INSERT ON SalesReturns
BEGIN
    SELECT RAISE(ABORT, 'Jumlah retur harus lebih dari nol') WHERE NEW.Quantity <= 0;
    SELECT RAISE(ABORT, 'Nilai retur harus lebih dari nol') WHERE NEW.Amount <= 0;
    SELECT RAISE(ABORT, 'Jumlah retur melebihi jumlah yang dibeli')
        WHERE (SELECT COALESCE(SUM(r.Quantity), 0) FROM SalesReturns r
               WHERE r.SalesOrderId = NEW.SalesOrderId) + NEW.Quantity
              > (SELECT so.Quantity FROM SalesOrders so WHERE so.Id = NEW.SalesOrderId);
    SELECT RAISE(ABORT, 'Nilai retur melebihi nilai pesanan')
        WHERE (SELECT COALESCE(SUM(r.Amount), 0) FROM SalesReturns r
               WHERE r.SalesOrderId = NEW.SalesOrderId) + NEW.Amount
              > (SELECT so.TotalAmount FROM SalesOrders so WHERE so.Id = NEW.SalesOrderId);
END;

-- Mutasi produk jadi mendapat tipe 'Return' supaya buku mutasi bisa membedakan barang yang
-- kembali karena retur dari pembatalan pesanan. CHECK lama tidak bisa diubah di tempat,
-- jadi tabel dibangun ulang lalu isinya disalin; tidak ada tabel lain yang merujuk ke sini.
CREATE TABLE IF NOT EXISTS FinishedGoodsMovements_baru (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ProductTemplateId  INTEGER NOT NULL REFERENCES ProductTemplates(Id),
    LocationId         INTEGER NOT NULL REFERENCES StorageLocations(Id),
    MovementType       TEXT NOT NULL
                           CHECK(MovementType IN ('Production','Sale','CancelRestore','Adjustment','Return')),
    Quantity           REAL NOT NULL,
    UnitCostMinorUnits INTEGER NOT NULL DEFAULT 0,
    SourceType         TEXT,
    SourceId           INTEGER,
    Notes              TEXT,
    CreatedAt          TEXT NOT NULL DEFAULT (datetime('now')),
    CreatedByUserId    INTEGER REFERENCES Users(Id)
);

INSERT INTO FinishedGoodsMovements_baru
    (Id, ProductTemplateId, LocationId, MovementType, Quantity, UnitCostMinorUnits,
     SourceType, SourceId, Notes, CreatedAt, CreatedByUserId)
SELECT Id, ProductTemplateId, LocationId, MovementType, Quantity, UnitCostMinorUnits,
       SourceType, SourceId, Notes, CreatedAt, CreatedByUserId
FROM FinishedGoodsMovements;

DROP TABLE FinishedGoodsMovements;
ALTER TABLE FinishedGoodsMovements_baru RENAME TO FinishedGoodsMovements;

CREATE INDEX IF NOT EXISTS idx_fgmovement_loc_prod
    ON FinishedGoodsMovements(LocationId, ProductTemplateId, CreatedAt);
