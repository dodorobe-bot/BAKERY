-- Rollback migrasi 020: kembalikan skema ke versi 19.
--
-- File ini TIDAK dijalankan otomatis oleh DatabaseMigrator; dipakai manual saat penurunan versi.
-- Yang dibuang hanya kolom perhitungan, pemicunya, dan dua akun yang lahir di migrasi ini.
--
-- Peringatan: kalau sudah ada pesanan ber-PPN atau berdiskon, penurunan versi membuat angka itu
-- tidak lagi bisa dijelaskan. Jurnal yang terlanjur memakai 2-003 dan 4-002 dibiarkan utuh —
-- menghapusnya berarti menghilangkan bukti, dan neraca akan kehilangan lawannya. Ekspor
-- SalesOrders, SalesPayments, dan JournalLines lebih dulu bila pembukuan masih dibutuhkan.

DROP TRIGGER IF EXISTS trg_salesorders_pricing_insert;
DROP TRIGGER IF EXISTS trg_salesorders_pricing_update;
DROP TRIGGER IF EXISTS trg_salespayments_tender_insert;
DROP TRIGGER IF EXISTS trg_salespayments_tender_update;
DROP TRIGGER IF EXISTS trg_salesreturns_pajak_insert;

ALTER TABLE SalesReturns DROP COLUMN TaxAmount;
ALTER TABLE SalesReturns DROP COLUMN DiscountAmount;

ALTER TABLE SalesPayments DROP COLUMN ChangeAmount;
ALTER TABLE SalesPayments DROP COLUMN TenderAmount;

ALTER TABLE SalesOrders DROP COLUMN RevenueRecognizedAt;
ALTER TABLE SalesOrders DROP COLUMN DeliveredAt;
ALTER TABLE SalesOrders DROP COLUMN TaxAmount;
ALTER TABLE SalesOrders DROP COLUMN TaxInclusive;
ALTER TABLE SalesOrders DROP COLUMN TaxRatePercent;
ALTER TABLE SalesOrders DROP COLUMN DiscountPercent;
ALTER TABLE SalesOrders DROP COLUMN DiscountAmount;
ALTER TABLE SalesOrders DROP COLUMN SubtotalAmount;

DELETE FROM Accounts WHERE Code IN ('2-003', '4-002');

DELETE FROM AppSettings WHERE Key IN ('tax_enabled', 'tax_rate_percent', 'tax_inclusive');

DELETE FROM SchemaVersion WHERE Version = 20;
