-- Rollback migrasi 012: kembalikan skema ke kondisi versi 11.
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator (namanya tidak diawali angka versi),
-- dipakai manual saat penurunan versi dan diuji oleh MigrationRollbackTests.
--
-- Peringatan data: menurunkan skema menghapus seluruh catatan lot & tanggal kedaluwarsa, serta
-- nomor lot yang diketik pada struk penerimaan. Jalankan "SELECT COUNT(*) FROM ItemBatches;"
-- lebih dulu; kalau isinya bukan nol, catat tanggal kedaluwarsanya di luar aplikasi sebelum rollback.

DROP TRIGGER IF EXISTS trg_itembatches_expiry_insert;
DROP TRIGGER IF EXISTS trg_itembatches_expiry_update;

DROP INDEX IF EXISTS idx_itembatches_ingredient_batch;
DROP INDEX IF EXISTS idx_itembatches_expiry_ordered;
DROP INDEX IF EXISTS idx_auditlog_occurred;
DROP INDEX IF EXISTS idx_auditlog_entity;

ALTER TABLE ItemBatches DROP COLUMN GoodsReceiptLineId;
ALTER TABLE GoodsReceiptLines DROP COLUMN BatchNumber;
ALTER TABLE GoodsReceiptLines DROP COLUMN ExpiryDate;

DELETE FROM AppSettings WHERE Key = 'outlet_name';

UPDATE SchemaVersion SET AppliedAt = datetime('now') WHERE Version = 11;
DELETE FROM SchemaVersion WHERE Version = 12;
