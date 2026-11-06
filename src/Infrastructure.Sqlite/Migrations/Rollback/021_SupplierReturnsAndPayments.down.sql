-- Rollback migrasi 021: kembalikan skema ke versi 20.
--
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator; dipakai manual saat penurunan versi.
--
-- Peringatan data: retur dan pembayaran pemasok adalah bukti uang dan barang yang sudah bergerak.
-- Tabelnya dibuang di sini, jadi jalankan dulu tiga pertanyaan di bawah dan ekspor journannya;
-- kalau isinya bukan nol, penurunan versi akan menghilangkan dokumen sumber dari JournalEntries
-- yang sudah posted dan saldo 2-002 tidak lagi bisa dijelaskan baris per baris.
--
--   SELECT COUNT(*) FROM SupplierReturns;
--   SELECT COUNT(*) FROM SupplierPayments;
--   SELECT id, description FROM JournalEntries
--    WHERE SourceDocumentType IN ('SupplierReturn','SupplierPayment');

DROP TRIGGER IF EXISTS trg_supplierpayments_insert;
DROP TRIGGER IF EXISTS trg_supplierreturnlines_beban_struk_insert;
DROP TRIGGER IF EXISTS trg_supplierreturnlines_jumlah_insert;

DROP TABLE IF EXISTS SupplierReturnLines;
DROP TABLE IF EXISTS SupplierReturns;
DROP TABLE IF EXISTS SupplierPayments;

-- Indeks ini dibuat 021 di tabel yang sudah ada lebih dulu, jadi tidak ikut hilang bersama
-- DROP TABLE di atas.
DROP INDEX IF EXISTS idx_supplierreturns_receipt;
DROP INDEX IF EXISTS idx_supplierpayments_receipt;
DROP INDEX IF EXISTS idx_goodsreceiptlines_receipt;

DELETE FROM SchemaVersion WHERE Version = 21;
