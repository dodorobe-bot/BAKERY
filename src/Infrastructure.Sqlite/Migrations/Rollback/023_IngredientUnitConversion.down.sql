-- Rollback migrasi 023: kembalikan skema ke versi 22.
--
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator; dipakai manual saat penurunan versi.
--
-- Yang hilang bersama rollback ini adalah faktor konversinya, bukan angka stok: semua stok,
-- mutasi, dan opname selalu disimpan dalam satuan stok. Setelah rollback, baris resep kembali
-- dibaca dalam satuan stok (250 "g" menjadi 250 satuan stok) — periksa resep yang memakai satuan
-- tambahan sebelum menurunkan versi:
--
--   SELECT rl.Id, pt.Name AS produk, i.Name AS bahan, i.Unit AS satuan_stok, rl.Unit, rl.Quantity
--     FROM RecipeLines rl
--     JOIN ProductTemplates pt ON pt.Id = rl.ProductTemplateId
--     JOIN Ingredients i ON i.Id = rl.IngredientId
--    WHERE rl.Unit IS NOT NULL AND trim(rl.Unit) <> ''
--      AND lower(trim(rl.Unit)) <> lower(trim(i.Unit));

DROP TRIGGER IF EXISTS trg_ingredients_unit_update;
DROP TRIGGER IF EXISTS trg_recipelines_unit_update;
DROP TRIGGER IF EXISTS trg_recipelines_unit_insert;
DROP TRIGGER IF EXISTS trg_ingredientunits_terpakai_delete;
DROP TRIGGER IF EXISTS trg_ingredientunits_format_update;
DROP TRIGGER IF EXISTS trg_ingredientunits_format_insert;

DROP INDEX IF EXISTS idx_ingredientunits_bahan_satuan;
DROP TABLE IF EXISTS IngredientUnits;

-- SQLite 3.35+ mendukung DROP COLUMN; di bawah itu kolom Unit dibiarkan kosong tanpa mengubah
-- pembacaan karena tanpa tabel IngredientUnits semua baris kembali dinilai dengan faktor 1.
ALTER TABLE RecipeLines DROP COLUMN Unit;

DELETE FROM SchemaVersion WHERE Version = 23;
