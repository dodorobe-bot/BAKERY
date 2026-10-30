-- Migration 007: Satu sumber kebenaran stok
-- LocationStock adalah penyimpan stok fisik, StockMovements menjadi buku mutasi yang bisa direkonstruksi per lokasi.

CREATE TABLE LocationStock_v007 (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    LocationId      INTEGER NOT NULL REFERENCES StorageLocations(Id),
    IngredientId    INTEGER NOT NULL REFERENCES Ingredients(Id),
    Quantity        REAL NOT NULL DEFAULT 0 CHECK (Quantity >= 0),
    UpdatedAt       TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE(LocationId, IngredientId)
);

-- Baris bermasalah akibat bug lama dinolkan lebih dulu agar tidak menabrak CHECK setelah tabel dibangun ulang
UPDATE LocationStock SET Quantity = 0 WHERE Quantity < 0;

INSERT INTO LocationStock_v007 (Id, LocationId, IngredientId, Quantity)
    SELECT Id, LocationId, IngredientId, Quantity FROM LocationStock;

DROP TABLE LocationStock;

ALTER TABLE LocationStock_v007 RENAME TO LocationStock;

CREATE INDEX IF NOT EXISTS idx_locationstock_loc_ing ON LocationStock(LocationId, IngredientId);

-- Ledger dicatat sebelum lokasi mutasi diketahui, kolomnya diisi agar saldo bisa direkonsiliasi per lokasi
ALTER TABLE StockMovements ADD COLUMN LocationId INTEGER REFERENCES StorageLocations(Id);

UPDATE StockMovements SET LocationId = 2 WHERE LocationId IS NULL AND MovementType IN ('Deduction','CancelRestore');

UPDATE StockMovements SET LocationId = 1 WHERE LocationId IS NULL;

-- Quantity kini selalu berkonvensi bertanda (pengurangan bernilai negatif), catatan lama dibenarkan
UPDATE StockMovements SET Quantity = -ABS(Quantity) WHERE MovementType = 'Deduction';

CREATE INDEX IF NOT EXISTS idx_stockmovements_loc_ing ON StockMovements(LocationId, IngredientId);

-- Saldo fisik lama belum tercermin di ledger, jadi satu catatan rekonsiliasi per lokasi dibuat
-- agar sejak versi 7 ledger selalu dapat merekonstruksi LocationStock tanpa mengubah stok fisik
INSERT INTO StockMovements (IngredientId, MovementType, Quantity, LocationId, SourceType, Notes, CreatedAt)
SELECT ls.IngredientId,
       'Adjustment',
       ls.Quantity - COALESCE((SELECT SUM(m.Quantity) FROM StockMovements m
                                WHERE m.LocationId = ls.LocationId
                                  AND m.IngredientId = ls.IngredientId), 0),
       ls.LocationId,
       'Baseline007',
       'Saldo awal hasil normalisasi migrasi 007',
       datetime('now')
FROM LocationStock ls
WHERE ls.Quantity <> COALESCE((SELECT SUM(m.Quantity) FROM StockMovements m
                                WHERE m.LocationId = ls.LocationId
                                  AND m.IngredientId = ls.IngredientId), 0);
