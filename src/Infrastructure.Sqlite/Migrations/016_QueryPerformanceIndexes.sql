-- Migration 016: indeks dan statistik untuk layar dengan data besar
--
-- Tidak ada data yang dihapus atau diringkas di sini. Semua perbaikan performa berasal dari
-- indeks yang mengikuti pola kueri nyata dan statistik perencana kueri (ANALYZE). Riwayat
-- transaksi tetap utuh; aplikasi hanya berhenti memuat seluruh tabel besar ke memori.
--
-- Indeks dipilih dari EXPLAIN QUERY PLAN pada basis data uji 5.000 pesanan / 800 pembelian /
-- 5.800 baris jurnal: tanpa statistik, SQLite memilih urutan gabung yang salah untuk laporan
-- arus kas (1,4 detik), sedangkan dengan indeks + ANALYZE turun ke beberapa milidetik.

-- Saldo akun: GROUP BY AccountId dengan penjumlahan Debit/Credit; indeks penutup membuat
-- penjumlahan tidak perlu membaca tabel JournalLines baris per baris.
CREATE INDEX IF NOT EXISTS idx_journallines_account_amounts
    ON JournalLines(AccountId, DebitAmount, CreditAmount);

-- Jurnal per tanggal + status (dashboard, laporan keuangan, analitik).
CREATE INDEX IF NOT EXISTS idx_journalentries_status_date
    ON JournalEntries(Status, TransactionDate);

-- Pesanan aktif per tanggal ambil (dashboard, kotak tugas, piutang).
CREATE INDEX IF NOT EXISTS idx_salesorders_status_pickup
    ON SalesOrders(Status, PickupDate);

-- Analitik penjualan mengelompokkan pesanan menurut tanggal dibuat.
CREATE INDEX IF NOT EXISTS idx_salesorders_created
    ON SalesOrders(CreatedAt);

-- Pembelian terbuka (belum diterima penuh) diurutkan menurut tanggal perkiraan.
CREATE INDEX IF NOT EXISTS idx_purchaseorders_status_expected
    ON PurchaseOrders(Status, ExpectedDate);

-- Stok bahan: subkueri SUM(LocationStock.Quantity) per bahan butuh indeks yang dimulai
-- dari IngredientId; indeks lama hanya dimulai dari LocationId.
CREATE INDEX IF NOT EXISTS idx_locationstock_ingredient
    ON LocationStock(IngredientId);

-- Mutasi stok terbaru per lokasi (layar inventaris).
CREATE INDEX IF NOT EXISTS idx_stockmovements_created
    ON StockMovements(CreatedAt);

-- Statistik perencana kueri. Tanpa langkah ini, indeks baru di atas justru bisa membuat
-- SQLite memilih rencana yang lebih lambat (terbukti pada kueri arus kas).
ANALYZE;
