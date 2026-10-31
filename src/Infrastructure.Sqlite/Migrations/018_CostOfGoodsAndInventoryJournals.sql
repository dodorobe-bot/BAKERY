-- Migration 018: penutupan akuntansi persediaan
--
-- 1. Harga pokok penjualan (COGS). Sebelumnya stok produk jadi berkurang tanpa jurnal,
--    sehingga akun 1-006 terus membengkak dan laba kotor selalu sama dengan pendapatan.
--    Kolom AverageCostMinorUnits menyimpan biaya rata-rata tertimbang per lokasi + produk;
--    mutasi penjualan memakai nilai itu untuk mendebit 5-005.
-- 2. Beban selisih persediaan (5-006) untuk hasil stok opname. Transfer gudang->dapur
--    memindahkan nilai antara 1-003 dan 1-004, bukan hanya jumlah fisiknya.
--
-- Saldo lama: stok yang sudah ada sebelum migrasi ini belum punya biaya rata-rata. Nilainya
-- diambil dari mutasi Production terakhir produk itu. Ini perkiraan terbaik dari data yang
-- tersimpan; tidak ada baris stok yang dihapus atau diubah jumlahnya.

INSERT OR IGNORE INTO Accounts (Code, Name, Type, IsSystem, IsActive) VALUES
    ('5-005', 'Beban Pokok Penjualan',   'Expense', 1, 1),
    ('5-006', 'Beban Selisih Persediaan','Expense', 1, 1);

ALTER TABLE FinishedGoodsStock
    ADD COLUMN AverageCostMinorUnits INTEGER NOT NULL DEFAULT 0;

UPDATE FinishedGoodsStock
   SET AverageCostMinorUnits = COALESCE((
        SELECT m.UnitCostMinorUnits
          FROM FinishedGoodsMovements m
         WHERE m.ProductTemplateId = FinishedGoodsStock.ProductTemplateId
           AND m.MovementType = 'Production'
           AND m.UnitCostMinorUnits > 0
         ORDER BY m.Id DESC
         LIMIT 1), 0);

CREATE INDEX IF NOT EXISTS idx_finishedgoods_movements_source
    ON FinishedGoodsMovements(SourceType, SourceId, MovementType);
