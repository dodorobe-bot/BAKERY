-- Migration 017: indeks penutup untuk analitik dan daftar pembelian pada volume besar
--
-- Diukur pada basis data uji 500.000 pesanan / 100.000 PO (salinan skema, data sintetis):
-- rentang satu tahun untuk analitik penjualan butuh 1,6 detik per kueri karena setiap baris
-- diambil dari tabel utama lewat indeks CreatedAt. Dengan indeks penutup, seluruh kolom yang
-- dibaca tersedia di indeks dan kueri yang sama selesai ~0,16 detik. Tidak ada satu pun baris
-- data yang diubah, diringkas, atau dihapus; hanya bentuk indeks yang berubah.

-- Rentang CreatedAt untuk analitik penjualan dan produk terlaris: Status, TotalAmount,
-- Quantity, dan ProductTemplateId ikut di indeks supaya pemindaian tidak menyentuh tabel.
DROP INDEX IF EXISTS idx_salesorders_created;
CREATE INDEX IF NOT EXISTS idx_salesorders_created_agg
    ON SalesOrders(CreatedAt, Status, TotalAmount, Quantity, ProductTemplateId);

-- Daftar pembelian diurutkan CreatedAt; tanpa indeks ini 100 ribu PO diurutkan di memori
-- setiap kali layar Pembelian dibuka.
CREATE INDEX IF NOT EXISTS idx_purchaseorders_created
    ON PurchaseOrders(CreatedAt);

-- Sisa bayar setiap pesanan dihitung dengan menjumlahkan pembayarannya. Indeks lama hanya
-- memuat SalesOrderId, sehingga setiap penjumlahan masih harus membuka tabel pembayaran satu
-- per satu. Versi penutup (SalesOrderId, Amount) membuat ringkasan piutang dashboard pada
-- 1 juta pesanan turun dari 1,34 detik menjadi 0,36 detik. Indeks parsial "hanya pesanan
-- belum lunas" sempat dicoba dan justru lebih lambat (1,1 detik): memindai indeks lalu
-- mencari setiap baris ke tabel acak lebih mahal daripada memindai tabel berurutan.
DROP INDEX IF EXISTS idx_salespayments_salesorder;
CREATE INDEX IF NOT EXISTS idx_salespayments_order_amount
    ON SalesPayments(SalesOrderId, Amount);

-- Piutang dan kotak tugas hanya peduli pesanan yang belum lunas. Tanpa indeks parsial,
-- setiap buka dashboard memindai seluruh riwayat pesanan: pada 1 juta pesanan tabelnya
-- ~160 MB dan kueri ringkasan piutang butuh ~1,4 detik saat cache dingin. Indeks parsial
-- berurut Id hanya memuat pesanan belum lunas dengan kolom yang dibutuhkan kueri, jadi
-- pemindaiannya berurutan dan tidak menyentuh tabel. Syarat indeks ditulis sama persis
-- dengan syarat kueri supaya perencana SQLite mau memakainya.
CREATE INDEX IF NOT EXISTS idx_salesorders_open
    ON SalesOrders(Id, TotalAmount, DownPayment)
    WHERE Status NOT IN ('Paid','Cancelled');

-- Indeks JournalLines sengaja tidak diubah. Percobaan indeks penutup (JournalEntryId,
-- AccountId, DebitAmount, CreditAmount) diukur pada basis data 500 ribu pesanan: rekap harian
-- membaik, tetapi saldo akun dan saldo kas dashboard justru melambat 4-7x karena perencana
-- kueri memilih memindai seluruh indeks lalu mencari setiap entri. Pola baca jurnal justru
-- paling cepat bila baris jurnal dibaca berurutan (lihat CROSS JOIN di JournalRepository),
-- jadi bentuk indeks lama dipertahankan.

ANALYZE;
