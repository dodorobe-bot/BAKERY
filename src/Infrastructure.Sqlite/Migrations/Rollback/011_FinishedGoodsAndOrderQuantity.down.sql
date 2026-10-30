-- Rollback migrasi 011: kembalikan skema ke kondisi versi 10.
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator (namanya tidak diawali angka versi),
-- dipakai manual saat penurunan versi dan diuji oleh MigrationRollbackTests.
--
-- Peringatan data: menurunkan skema membuang seluruh saldo dan mutasi produk jadi. Jalankan
-- "SELECT COUNT(*) FROM FinishedGoodsStock WHERE Quantity > 0;" lebih dulu; kalau isinya bukan
-- nol, ekspor atau nolkan stoknya sebelum rollback.

DROP TABLE IF EXISTS FinishedGoodsMovements;
DROP TABLE IF EXISTS FinishedGoodsStock;
DROP INDEX IF EXISTS idx_fgstock_loc_prod;
DROP INDEX IF EXISTS idx_fgmovement_loc_prod;

DROP TRIGGER IF EXISTS trg_salesorders_quantity_insert;
DROP TRIGGER IF EXISTS trg_salesorders_quantity_update;

-- SQLite >= 3.35 mendukung DROP COLUMN; di bawah itu kolom Quantity dibiarkan kosong tanpa
-- dibaca aplikasi versi 10, yang tetap sah karena punya DEFAULT 1.
ALTER TABLE SalesOrders DROP COLUMN Quantity;

UPDATE SchemaVersion SET AppliedAt = datetime('now') WHERE Version = 10;
DELETE FROM SchemaVersion WHERE Version = 11;
