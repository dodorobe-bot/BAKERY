-- Migration 020: diskon, PPN, kembalian, dan piutang usaha
--
-- Sampai versi ini pesanan hanya menyimpan satu angka (TotalAmount) sehingga tiga hal tidak bisa
-- dibukukan sama sekali:
--  1. Diskon (kasus B2a/B2b): tidak ada kolomnya dan tidak ada kuncinya di Pengaturan, jadi
--     harga yang dikurangkan di kasir hanya bisa diketik langsung ke dalam total.
--  2. PPN (kasus B3a-B3d): tidak ada akun Utang PPN, sehingga pajak yang dipungut tidak pernah
--     tercatat sebagai kewajiban — uangnya ada di kas tapi tidak pernah jadi hutang ke negara.
--  3. Piutang (B7a): pesanan yang diserahkan sebelum lunas tidak punya akun lawan, sehingga
--     sisanya tidak tercatat sama sekali dan neraca selalu menampilkan piutang nol.
--
-- Aturan pembulatan mengikuti praktik toko: diskon dan PPN dibulatkan ke Rupiah penuh (100 sen),
-- karena struk dan laci uang tidak mengenal pecahan Rupiah. Nilai yang tersimpan tetap dalam sen.
--
-- Barang yang sudah ada tidak diubah nilainya: SubtotalAmount diisi sama dengan TotalAmount dan
-- diskon/pajak bernilai nol, jadi pesanan lama tetap terbaca persis seperti sebelumnya.

INSERT OR IGNORE INTO Accounts (Code, Name, Type, IsSystem, IsActive) VALUES
    ('2-003', 'Utang PPN',        'Liability', 1, 1),
    ('4-002', 'Diskon Penjualan', 'Revenue',   1, 1);
-- 4-002 ikut bertipe Revenue supaya layar analisis yang menjumlahkan (kredit - debit) akun
-- pendapatan otomatis menampilkan penjualan BERSIH, bukan penjualan kotor sebelum diskon.

ALTER TABLE SalesOrders ADD COLUMN SubtotalAmount   INTEGER NOT NULL DEFAULT 0;
ALTER TABLE SalesOrders ADD COLUMN DiscountAmount   INTEGER NOT NULL DEFAULT 0;
ALTER TABLE SalesOrders ADD COLUMN DiscountPercent  REAL;
ALTER TABLE SalesOrders ADD COLUMN TaxRatePercent   REAL;
ALTER TABLE SalesOrders ADD COLUMN TaxInclusive     INTEGER NOT NULL DEFAULT 0;
ALTER TABLE SalesOrders ADD COLUMN TaxAmount        INTEGER NOT NULL DEFAULT 0;
ALTER TABLE SalesOrders ADD COLUMN DeliveredAt      TEXT;
ALTER TABLE SalesOrders ADD COLUMN RevenueRecognizedAt TEXT;

ALTER TABLE SalesPayments ADD COLUMN TenderAmount   INTEGER;
ALTER TABLE SalesPayments ADD COLUMN ChangeAmount   INTEGER NOT NULL DEFAULT 0;

-- Setiap baris retur menyimpan potongan dan pajak yang ikut dibalik, supaya pajak yang sudah
-- dihapus dari kewajiban bisa dibuktikan per transaksi — bukan hanya selisih total.
ALTER TABLE SalesReturns ADD COLUMN DiscountAmount INTEGER NOT NULL DEFAULT 0;
ALTER TABLE SalesReturns ADD COLUMN TaxAmount      INTEGER NOT NULL DEFAULT 0;

-- Pesanan lama: subtotal = total tagihan, tanpa diskon dan tanpa pajak.
UPDATE SalesOrders SET SubtotalAmount = TotalAmount WHERE SubtotalAmount = 0;

-- Penjaga rumus. Pemicu, bukan CHECK: SQLite tidak bisa menambah CHECK pada tabel yang sudah ada,
-- dan rumah kode ini sudah memakai pemicu untuk aturan jumlah pesanan (migrasi 011).
-- Baris lama yang tidak diisi oleh jalur baru (SubtotalAmount = 0) tidak dinilai, supaya pembuat
-- data manual tidak tiba-tiba ditolak; semua pesanan yang dihitung ulang selalu cocok.
CREATE TRIGGER IF NOT EXISTS trg_salesorders_pricing_insert
BEFORE INSERT ON SalesOrders
BEGIN
    SELECT RAISE(ABORT, 'Nilai diskon pesanan di luar nilai yang bisa didiskontokan')
        WHERE NEW.SubtotalAmount > 0
          AND (NEW.DiscountAmount < 0 OR NEW.DiscountAmount > NEW.SubtotalAmount);
    SELECT RAISE(ABORT, 'Nilai PPN pesanan tidak boleh negatif')
        WHERE NEW.SubtotalAmount > 0 AND NEW.TaxAmount < 0;
    SELECT RAISE(ABORT, 'PPN inklusif tidak boleh lebih besar daripada tagihan')
        WHERE NEW.SubtotalAmount > 0 AND NEW.TaxInclusive = 1
          AND NEW.TaxAmount > NEW.SubtotalAmount - NEW.DiscountAmount;
    SELECT RAISE(ABORT, 'Total pesanan harus sama dengan subtotal dikurangi diskon ditambah PPN')
        WHERE NEW.SubtotalAmount > 0
          AND NEW.TotalAmount <> NEW.SubtotalAmount - NEW.DiscountAmount
                + (CASE WHEN NEW.TaxInclusive = 1 THEN 0 ELSE NEW.TaxAmount END);
END;

CREATE TRIGGER IF NOT EXISTS trg_salesorders_pricing_update
BEFORE UPDATE ON SalesOrders
BEGIN
    SELECT RAISE(ABORT, 'Nilai diskon pesanan di luar nilai yang bisa didiskontokan')
        WHERE NEW.SubtotalAmount > 0
          AND (NEW.DiscountAmount < 0 OR NEW.DiscountAmount > NEW.SubtotalAmount);
    SELECT RAISE(ABORT, 'Nilai PPN pesanan tidak boleh negatif')
        WHERE NEW.SubtotalAmount > 0 AND NEW.TaxAmount < 0;
    SELECT RAISE(ABORT, 'PPN inklusif tidak boleh lebih besar daripada tagihan')
        WHERE NEW.SubtotalAmount > 0 AND NEW.TaxInclusive = 1
          AND NEW.TaxAmount > NEW.SubtotalAmount - NEW.DiscountAmount;
    SELECT RAISE(ABORT, 'Total pesanan harus sama dengan subtotal dikurangi diskon ditambah PPN')
        WHERE NEW.SubtotalAmount > 0
          AND NEW.TotalAmount <> NEW.SubtotalAmount - NEW.DiscountAmount
                + (CASE WHEN NEW.TaxInclusive = 1 THEN 0 ELSE NEW.TaxAmount END);
END;

-- Uang yang diserahkan pelanggan tidak pernah lebih kecil daripada bagian yang dibukukan, dan
-- kembalian adalah selisihnya — angka yang sama yang dilihat kasir di laci uang.
CREATE TRIGGER IF NOT EXISTS trg_salespayments_tender_insert
BEFORE INSERT ON SalesPayments
BEGIN
    SELECT RAISE(ABORT, 'Uang diterima tidak boleh kurang daripada pembayaran yang dibukukan')
        WHERE NEW.TenderAmount IS NOT NULL AND NEW.TenderAmount < NEW.Amount;
    SELECT RAISE(ABORT, 'Kembalian harus sama dengan uang diterima dikurangi pembayaran')
        WHERE NEW.TenderAmount IS NOT NULL AND NEW.ChangeAmount <> NEW.TenderAmount - NEW.Amount;
END;

CREATE TRIGGER IF NOT EXISTS trg_salespayments_tender_update
BEFORE UPDATE ON SalesPayments
BEGIN
    SELECT RAISE(ABORT, 'Uang diterima tidak boleh kurang daripada pembayaran yang dibukukan')
        WHERE NEW.TenderAmount IS NOT NULL AND NEW.TenderAmount < NEW.Amount;
    SELECT RAISE(ABORT, 'Kembalian harus sama dengan uang diterima dikurangi pembayaran')
        WHERE NEW.TenderAmount IS NOT NULL AND NEW.ChangeAmount <> NEW.TenderAmount - NEW.Amount;
END;

-- Retur bertahap tidak boleh mengurangi pajak atau membalik diskon lebih besar daripada yang
-- pernah dibukukan pada pesanannya, walaupun dua retur datang berbarengan.
CREATE TRIGGER IF NOT EXISTS trg_salesreturns_pajak_insert
BEFORE INSERT ON SalesReturns
BEGIN
    SELECT RAISE(ABORT, 'PPN yang diretur melebihi PPN pesanan')
        WHERE (SELECT COALESCE(SUM(r.TaxAmount), 0) FROM SalesReturns r
                WHERE r.SalesOrderId = NEW.SalesOrderId) + NEW.TaxAmount
              > (SELECT o.TaxAmount FROM SalesOrders o WHERE o.Id = NEW.SalesOrderId);
    SELECT RAISE(ABORT, 'Diskon yang dibalik melebihi diskon pesanan')
        WHERE (SELECT COALESCE(SUM(r.DiscountAmount), 0) FROM SalesReturns r
                WHERE r.SalesOrderId = NEW.SalesOrderId) + NEW.DiscountAmount
              > (SELECT o.DiscountAmount FROM SalesOrders o WHERE o.Id = NEW.SalesOrderId);
END;

INSERT OR IGNORE INTO SchemaVersion (Version, AppliedAt) VALUES (20, datetime('now'));
