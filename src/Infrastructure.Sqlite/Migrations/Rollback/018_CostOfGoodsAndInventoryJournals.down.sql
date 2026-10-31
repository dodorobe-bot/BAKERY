-- Rollback migrasi 018: kembalikan skema ke versi 17.
--
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator; dipakai manual saat penurunan
-- versi. Peringatan data: kolom biaya rata-rata dan akun 5-005/5-006 dibuang. Jurnal COGS
-- yang sudah dibuat tetap ada (JournalEntries tidak diubah), sehingga saldo 5-005/5-006
-- menggantung tanpa akun. Ekspor buku besar sebelum rollback bila angka itu masih dibutuhkan.

DROP INDEX IF EXISTS idx_finishedgoods_movements_source;

-- SQLite 3.35+ mendukung DROP COLUMN, tetapi tabel dibangun ulang supaya rollback juga
-- berjalan pada pustaka SQLite lama yang mungkin dipakai mesin kasir.
CREATE TABLE IF NOT EXISTS FinishedGoodsStock_lama (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    LocationId         INTEGER NOT NULL REFERENCES StorageLocations(Id),
    ProductTemplateId  INTEGER NOT NULL REFERENCES ProductTemplates(Id),
    Quantity           REAL NOT NULL CHECK(Quantity >= 0),
    UpdatedAt          TEXT NOT NULL,
    UNIQUE(LocationId, ProductTemplateId)
);

INSERT OR IGNORE INTO FinishedGoodsStock_lama
    (Id, LocationId, ProductTemplateId, Quantity, UpdatedAt)
SELECT Id, LocationId, ProductTemplateId, Quantity, UpdatedAt FROM FinishedGoodsStock;

DROP TABLE FinishedGoodsStock;
ALTER TABLE FinishedGoodsStock_lama RENAME TO FinishedGoodsStock;

DELETE FROM Accounts WHERE Code IN ('5-005', '5-006');

DELETE FROM SchemaVersion WHERE Version = 18;
