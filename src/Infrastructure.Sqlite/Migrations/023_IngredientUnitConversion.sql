-- Migration 023: konversi satuan bahan untuk baris resep
--
-- Kasus uji C7 (baris 38 workbook): bahan bersatuan kg dengan harga Rp 12.000/kg dipakai resep
-- sebanyak "250 g". Baris resep tidak punya satuan, jadi 250 dibaca sebagai 250 kg dan HPP produk
-- menjadi Rp 3.000.000 — seratus kali lipat dari yang seharusnya (Rp 3.000).
--
-- Faktor konversi disimpan per bahan, bukan per baris resep: satu bahan yang sama dipakai banyak
-- produk, dan faktor yang diketik ulang di tiap baris cepat berbeda. Satuan stok (Ingredients.Unit)
-- adalah dasar semua angka — stok, mutasi, opname, pembelian — sehingga lapisan ini hanya
-- menerjemahkan satuan yang diketik di resep ke satuan stok sebelum HPP dan pemakaian dihitung.
--
-- Resep lama tidak berubah: kolom Unit kosong berarti "satuan stok" dengan faktor 1.

CREATE TABLE IF NOT EXISTS IngredientUnits (
    Id              INTEGER PRIMARY KEY,
    IngredientId    INTEGER NOT NULL REFERENCES Ingredients(Id) ON DELETE CASCADE,
    Unit            TEXT NOT NULL,
    FactorToBase    REAL NOT NULL CHECK (FactorToBase > 0)
);

-- "g" dan "G" adalah satuan yang sama; dua baris untuk keduanya membuat faktor yang dipakai
-- bergantung pada baris mana yang kebetulan dibaca lebih dulu.
CREATE UNIQUE INDEX IF NOT EXISTS idx_ingredientunits_bahan_satuan
    ON IngredientUnits(IngredientId, Unit COLLATE NOCASE);

ALTER TABLE RecipeLines ADD COLUMN Unit TEXT;

-- Satuan tambahan tidak boleh kosong, tidak boleh sama dengan satuan stok, dan faktornya harus
-- positif. Pemeriksaan di layar/server memberi pesan yang jelas; pemicu ini menahan jalur yang
-- melewati aplikasi, termasuk impor langsung ke basis data.
CREATE TRIGGER IF NOT EXISTS trg_ingredientunits_format_insert
BEFORE INSERT ON IngredientUnits
BEGIN
    SELECT RAISE(ABORT, 'Nama satuan tambahan wajib diisi')
        WHERE trim(COALESCE(NEW.Unit, '')) = '';
    SELECT RAISE(ABORT, 'Faktor satuan tambahan harus lebih dari nol')
        WHERE NEW.FactorToBase IS NULL OR NEW.FactorToBase <= 0;
    SELECT RAISE(ABORT, 'Satuan tambahan tidak boleh sama dengan satuan stok bahan')
        WHERE EXISTS (SELECT 1 FROM Ingredients i
                       WHERE i.Id = NEW.IngredientId
                         AND lower(trim(i.Unit)) = lower(trim(NEW.Unit)));
END;

CREATE TRIGGER IF NOT EXISTS trg_ingredientunits_format_update
BEFORE UPDATE OF IngredientId, Unit, FactorToBase ON IngredientUnits
BEGIN
    SELECT RAISE(ABORT, 'Nama satuan tambahan wajib diisi')
        WHERE trim(COALESCE(NEW.Unit, '')) = '';
    SELECT RAISE(ABORT, 'Faktor satuan tambahan harus lebih dari nol')
        WHERE NEW.FactorToBase IS NULL OR NEW.FactorToBase <= 0;
    SELECT RAISE(ABORT, 'Satuan tambahan tidak boleh sama dengan satuan stok bahan')
        WHERE EXISTS (SELECT 1 FROM Ingredients i
                       WHERE i.Id = NEW.IngredientId
                         AND lower(trim(i.Unit)) = lower(trim(NEW.Unit)));
END;

-- Menghapus satuan yang masih dipakai baris resep akan membuat resep itu tidak bisa dihitung.
-- Pesannya menyebut jumlah pemakainya supaya pengguna tahu resep mana yang harus diubah dulu.
CREATE TRIGGER IF NOT EXISTS trg_ingredientunits_terpakai_delete
BEFORE DELETE ON IngredientUnits
BEGIN
    SELECT RAISE(ABORT, 'Satuan ini masih dipakai baris resep; ubah resepnya lebih dulu')
        WHERE EXISTS (SELECT 1 FROM RecipeLines rl
                       WHERE rl.IngredientId = OLD.IngredientId
                         AND rl.Unit IS NOT NULL
                         AND lower(trim(rl.Unit)) = lower(trim(OLD.Unit)));
END;

-- Baris resep hanya boleh memakai satuan stok atau satuan tambahan yang terdaftar. Tanpa pemicu
-- ini, salah ketik "grm" akan jatuh ke faktor 1 dan dinilai seperti kilogram.
CREATE TRIGGER IF NOT EXISTS trg_recipelines_unit_insert
BEFORE INSERT ON RecipeLines
BEGIN
    SELECT RAISE(ABORT, 'Satuan baris resep belum terdaftar pada bahan')
        WHERE NEW.Unit IS NOT NULL
          AND trim(NEW.Unit) <> ''
          AND NOT EXISTS (
              SELECT 1 FROM Ingredients i
               WHERE i.Id = NEW.IngredientId
                 AND (lower(trim(i.Unit)) = lower(trim(NEW.Unit))
                      OR EXISTS (SELECT 1 FROM IngredientUnits iu
                                  WHERE iu.IngredientId = i.Id
                                    AND lower(trim(iu.Unit)) = lower(trim(NEW.Unit)))));
END;

CREATE TRIGGER IF NOT EXISTS trg_recipelines_unit_update
BEFORE UPDATE OF IngredientId, Unit ON RecipeLines
BEGIN
    SELECT RAISE(ABORT, 'Satuan baris resep belum terdaftar pada bahan')
        WHERE NEW.Unit IS NOT NULL
          AND trim(NEW.Unit) <> ''
          AND NOT EXISTS (
              SELECT 1 FROM Ingredients i
               WHERE i.Id = NEW.IngredientId
                 AND (lower(trim(i.Unit)) = lower(trim(NEW.Unit))
                      OR EXISTS (SELECT 1 FROM IngredientUnits iu
                                  WHERE iu.IngredientId = i.Id
                                    AND lower(trim(iu.Unit)) = lower(trim(NEW.Unit)))));
END;

-- Mengganti nama satuan stok membuat baris resep yang menyebut satuan lama menjadi yatim. Bahan
-- yang barisnya kosong tetap ikut satuan stok baru (faktor 1), jadi hanya satuan eksplisit yang
-- diperiksa.
CREATE TRIGGER IF NOT EXISTS trg_ingredients_unit_update
BEFORE UPDATE OF Unit ON Ingredients
BEGIN
    SELECT RAISE(ABORT, 'Satuan stok ini masih disebut baris resep; ubah resepnya lebih dulu')
        WHERE lower(trim(NEW.Unit)) <> lower(trim(OLD.Unit))
          AND EXISTS (SELECT 1 FROM RecipeLines rl
                       WHERE rl.IngredientId = OLD.Id
                         AND rl.Unit IS NOT NULL
                         AND lower(trim(rl.Unit)) = lower(trim(OLD.Unit)));
END;

INSERT OR IGNORE INTO SchemaVersion (Version, AppliedAt) VALUES (23, datetime('now'));
