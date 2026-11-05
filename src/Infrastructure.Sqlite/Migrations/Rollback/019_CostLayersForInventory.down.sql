-- Rollback migrasi 019: kembalikan skema ke versi 18.
--
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator; dipakai manual saat penurunan
-- versi. Yang dibuang hanya tabel lapisan biaya. Stok fisik, buku mutasi, dan jurnal tidak
-- disentuh, jadi saldo lama tetap terbaca — hanya saja perhitungan biaya kembali memakai
-- harga yang sedang terpasang, yang merupakan sumber BUG-032.
--
-- Ekspor StockCostLayers lebih dulu bila rincian lapisan masih dibutuhkan untuk audit.

DROP INDEX IF EXISTS idx_stockcostlayers_fifo;
DROP INDEX IF EXISTS idx_stockcostlayers_location;
DROP TABLE IF EXISTS StockCostLayers;

-- Kolom nilai hanya menambah informasi; jurnal yang sudah memakai angka FIFO-nya tetap ada.
ALTER TABLE FinishedGoodsMovements DROP COLUMN ValueMinorUnits;

DELETE FROM SchemaVersion WHERE Version = 19;
