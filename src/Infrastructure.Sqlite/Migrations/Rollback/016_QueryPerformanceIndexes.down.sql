-- Rollback migrasi 016 (indeks performa).
--
-- PERINGATAN: hanya indeks tambahan yang dicabut; tidak ada satu pun baris data yang dihapus.
-- Statistik ANALYZE yang sudah tertulis di sqlite_stat1 dibiarkan: isinya catatan rencana
-- query, bukan data pengguna, dan akan diperbarui sendiri pada ANALYZE berikutnya.

DROP INDEX IF EXISTS idx_journallines_account_amounts;
DROP INDEX IF EXISTS idx_journalentries_status_date;
DROP INDEX IF EXISTS idx_salesorders_status_pickup;
DROP INDEX IF EXISTS idx_salesorders_created;
DROP INDEX IF EXISTS idx_purchaseorders_status_expected;
DROP INDEX IF EXISTS idx_locationstock_ingredient;
DROP INDEX IF EXISTS idx_stockmovements_created;

DELETE FROM SchemaVersion WHERE Version = 16;
