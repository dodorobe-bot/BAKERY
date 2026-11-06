-- Rollback migrasi 022: kembalikan skema ke versi 21.
--
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator; dipakai manual saat penurunan versi.
--
-- Yang hilang bersama rollback ini adalah gerbangnya, bukan datanya: baris FiscalPeriods tetap ada
-- dan hanya berhenti ditanyakan oleh pemicu. Setelah rollback, jurnal bertanggal di bulan berstatus
-- Closed kembali bisa ditulis — jadi periksa dulu isinya sebelum memutuskan menurunkan versi:
--
--   SELECT Name, StartDate, EndDate, Status FROM FiscalPeriods ORDER BY StartDate;
--   SELECT COUNT(*) FROM JournalEntries je
--     JOIN FiscalPeriods fp ON fp.Status IN ('Closed','Locked')
--    WHERE je.TransactionDate BETWEEN fp.StartDate AND fp.EndDate
--      AND date(je.CreatedAt) > fp.ClosedAt;   -- harus nol selama pagarnya aktif

DROP TRIGGER IF EXISTS trg_journalentries_periode_update;
DROP TRIGGER IF EXISTS trg_journalentries_periode_insert;
DROP TRIGGER IF EXISTS trg_fiscalperiods_format;

DROP INDEX IF EXISTS idx_fiscalperiods_startdate;

DELETE FROM SchemaVersion WHERE Version = 22;
