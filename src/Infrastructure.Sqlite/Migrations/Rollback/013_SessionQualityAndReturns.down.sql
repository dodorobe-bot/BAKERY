-- Rollback migrasi 013: kembalikan skema ke kondisi versi 12.
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator (namanya tidak diawali angka versi),
-- dipakai manual saat penurunan versi dan diuji oleh MigrationRollbackTests.
--
-- Peringatan data: menurunkan skema menghapus seluruh hasil pemeriksaan mutu dan dokumen retur,
-- serta baris mutu produk jadi bertipe 'Return'. Jalankan
-- "SELECT COUNT(*) FROM QualityChecks; SELECT COUNT(*) FROM SalesReturns;
--  SELECT COUNT(*) FROM FinishedGoodsMovements WHERE MovementType='Return';"
-- lebih dulu; kalau isinya bukan nol, ekspor catatannya di luar aplikasi sebelum rollback.
-- Versi token pengguna juga hilang, sehingga semua sesi yang sudah dicabut kembali sah sampai
-- tokennya kedaluwarsa.

DROP TRIGGER IF EXISTS trg_salesreturns_insert;
DROP TRIGGER IF EXISTS trg_qualitychecks_update;
DROP TRIGGER IF EXISTS trg_qualitychecks_insert;

DROP TABLE IF EXISTS SalesReturns;
DROP TABLE IF EXISTS QualityChecks;

-- Kembalikan CHECK mutasi produk jadi ke versi 012 (tanpa 'Return'). CHECK tidak bisa diubah
-- di tempat, jadi tabel dibangun ulang; baris 'Return' dibuang supaya tabel baru sah.
CREATE TABLE IF NOT EXISTS FinishedGoodsMovements_lama (
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

INSERT INTO FinishedGoodsMovements_lama
    (Id, ProductTemplateId, LocationId, MovementType, Quantity, UnitCostMinorUnits,
     SourceType, SourceId, Notes, CreatedAt, CreatedByUserId)
SELECT Id, ProductTemplateId, LocationId, MovementType, Quantity, UnitCostMinorUnits,
       SourceType, SourceId, Notes, CreatedAt, CreatedByUserId
FROM FinishedGoodsMovements
WHERE MovementType != 'Return';

DROP TABLE FinishedGoodsMovements;
ALTER TABLE FinishedGoodsMovements_lama RENAME TO FinishedGoodsMovements;

CREATE INDEX IF NOT EXISTS idx_fgmovement_loc_prod
    ON FinishedGoodsMovements(LocationId, ProductTemplateId, CreatedAt);

ALTER TABLE Users DROP COLUMN TokenVersion;

UPDATE SchemaVersion SET AppliedAt = datetime('now') WHERE Version = 12;
DELETE FROM SchemaVersion WHERE Version = 13;
