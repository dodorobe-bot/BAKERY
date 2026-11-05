-- Migration 019: lapisan biaya (FIFO) untuk bahan baku dan produk jadi
--
-- Sebelum migrasi ini nilai persediaan dihitung ulang dari harga yang sedang terpasang, bukan
-- dari harga barang yang benar-benar bergerak. Akibatnya (BUG-032) buku dan fisik bisa cocok
-- sementara nilainya tidak: bahan yang dibeli Rp 500 lalu dijual memakai harga master Rp 600
-- menyisakan selisih nilai yang tidak pernah tercatat, dan produk jadi memakai rata-rata
-- bergerak sehingga HPP penjualan (BUG-034/E3a) tidak pernah sama dengan harga per batch.
--
-- StockCostLayers menyimpan tiap potongan barang masuk beserta biaya per satuannya. Keluar
-- selalu menghabiskan lapisan tertua lebih dulu (FIFO berdasar Id penyisipan), jadi nominal
-- yang masuk ke 5-005 dan 5-006 adalah harga barang yang memang berpindah.
--
-- Saldo lama tidak diubah: satu lapisan dibuat per baris stok yang ada, memakai harga yang
-- sudah dipakai pembukuan selama ini (Ingredients.PricePerUnit untuk bahan,
-- FinishedGoodsStock.AverageCostMinorUnits untuk produk jadi). Nilai buku total tetap sama,
-- tidak ada jurnal historis yang disunting, dan tidak ada baris stok yang dikurangi.
--
-- FinishedGoodsStock.AverageCostMinorUnits tetap dipertahankan sebagai rata-rata tertimbang
-- dari lapisan yang tersisa supaya layar Katalog/HPP punya satu angka per produk.

CREATE TABLE IF NOT EXISTS StockCostLayers (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemKind           TEXT NOT NULL CHECK (ItemKind IN ('Ingredient','FinishedGoods')),
    ItemId             INTEGER NOT NULL,
    LocationId         INTEGER NOT NULL REFERENCES StorageLocations(Id),
    Quantity           REAL NOT NULL CHECK (Quantity >= 0),
    UnitCostMinorUnits INTEGER NOT NULL CHECK (UnitCostMinorUnits >= 0),
    SourceType         TEXT NOT NULL,
    SourceId           INTEGER,
    CreatedAt          TEXT NOT NULL DEFAULT (datetime('now')),
    UpdatedAt          TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_stockcostlayers_fifo
    ON StockCostLayers (ItemKind, ItemId, LocationId, Id);

CREATE INDEX IF NOT EXISTS idx_stockcostlayers_location
    ON StockCostLayers (LocationId, ItemKind);

-- Lapisan awal bahan: satu baris per lokasi + bahan, pada harga yang selama ini dipakai
-- jurnal transfer/opname. Stok nol tidak diberi lapisan supaya tabel hanya berisi barang
-- yang benar-benar ada.
INSERT INTO StockCostLayers
    (ItemKind, ItemId, LocationId, Quantity, UnitCostMinorUnits, SourceType, SourceId)
SELECT 'Ingredient', ls.IngredientId, ls.LocationId, ls.Quantity,
       COALESCE(i.PricePerUnit, 0), 'Migration019', ls.Id
  FROM LocationStock ls
  JOIN Ingredients i ON i.Id = ls.IngredientId
 WHERE ls.Quantity > 0;

-- Lapisan awal produk jadi: memakai biaya rata-rata yang sudah dihitung migration 018,
-- sehingga saldo akun 1-006 tidak berubah sesaat setelah upgrade.
INSERT INTO StockCostLayers
    (ItemKind, ItemId, LocationId, Quantity, UnitCostMinorUnits, SourceType, SourceId)
SELECT 'FinishedGoods', fgs.ProductTemplateId, fgs.LocationId, fgs.Quantity,
       fgs.AverageCostMinorUnits, 'Migration019', fgs.Id
  FROM FinishedGoodsStock fgs
 WHERE fgs.Quantity > 0;

-- Buku mutasi produk jadi selama ini hanya menyimpan biaya per unit. Keluarnya barang lewat
-- FIFO bisa menyapu beberapa lapisan sekaligus, sehingga nilai total yang berpindah bukan
-- selalu sama dengan quantity × satu harga. Kolom ini menyimpan nilai pastinya, jadi retur
-- dan pemeriksaan bisa menunjuk angka yang benar-benar dibebankan, bukan hasil kali ulang.
ALTER TABLE FinishedGoodsMovements ADD COLUMN ValueMinorUnits INTEGER NOT NULL DEFAULT 0;

-- Baris lama direkonstruksi dari rumus yang berlaku waktu itu (quantity × biaya per unit),
-- sehingga riwayat tidak ditulis ulang dengan aturan baru.
UPDATE FinishedGoodsMovements
   SET ValueMinorUnits = CAST(ROUND(ABS(Quantity) * UnitCostMinorUnits) AS INTEGER)
 WHERE ValueMinorUnits = 0;
