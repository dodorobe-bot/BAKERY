-- Migration 022: tutup periode sebagai pagar tulis
--
-- Kasus uji E6 dan E7 (baris 55-56 workbook): layar beban tidak punya kolom tanggal sehingga semua
-- biaya jatuh di hari bisnis berjalan, dan tabel FiscalPeriods dibiarkan kosong tanpa satu pun
-- pembaca. Akibat gabungan keduanya: transaksi mundur tidak dikontrol sama sekali. Siapapun dengan
-- hak keuangan bisa menulis jurnal bertanggal bulan apa pun, termasuk bulan yang laporannya sudah
-- dianggap final oleh pemilik.
--
-- Pagar dipasang di JournalEntries, bukan di tiap layar. Semua aksi — pesanan, pembayaran pelanggan,
-- penerimaan barang, retur dan pembayaran pemasok, SPK, mutasi gudang, opname, biaya — bermuara ke
-- satu penulis jurnal, jadi satu pemicu di sini menahan seluruh jalur sekaligus, termasuk sisipan
-- langsung ke basis data yang melewati aplikasi.
--
-- Menutup periode tidak menghapus atau memindah satu baris pun. Yang dikunci hanya gerbang tulis:
-- riwayat yang sudah ada tetap terbaca di laporan, dan periode yang dibuka kembali langsung bisa
-- dipakai lagi tanpa pemulihan apa pun.

-- Rentang periode dibandingkan sebagai teks yyyy-MM-dd, sama seperti kolom TransactionDate. Format
-- itu adalah satu-satunya hal yang membuat perbandingan tanggal di pemicu di bawah benar, jadi
-- baris dengan format lain ditolak di sini, bukan dibiarkan lolos menjadi pagar yang bocor.
CREATE TRIGGER IF NOT EXISTS trg_fiscalperiods_format
BEFORE INSERT ON FiscalPeriods
BEGIN
    SELECT RAISE(ABORT, 'Tanggal awal periode harus berformat yyyy-MM-dd')
        WHERE NEW.StartDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]';
    SELECT RAISE(ABORT, 'Tanggal akhir periode harus berformat yyyy-MM-dd')
        WHERE NEW.EndDate NOT GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]';
    SELECT RAISE(ABORT, 'Rentang periode buku tidak sah')
        WHERE NEW.StartDate > NEW.EndDate;
END;

-- Satu rentang hanya boleh punya satu baris. Tanpa indeks ini, dua baris untuk bulan yang sama bisa
-- membuat status periode tergantung baris mana yang kebetulan dibaca lebih dulu.
CREATE UNIQUE INDEX IF NOT EXISTS idx_fiscalperiods_startdate ON FiscalPeriods(StartDate);

CREATE TRIGGER IF NOT EXISTS trg_journalentries_periode_insert
BEFORE INSERT ON JournalEntries
BEGIN
    SELECT RAISE(ABORT, 'Periode buku ini sudah ditutup; jurnal tidak boleh ditulis ke dalamnya')
        WHERE EXISTS (SELECT 1 FROM FiscalPeriods fp
                       WHERE fp.Status IN ('Closed', 'Locked')
                         AND fp.StartDate <= NEW.TransactionDate
                         AND fp.EndDate >= NEW.TransactionDate);
END;

-- Membalik entri lama mengubah Status, bukan tanggal, jadi pemicunya hanya ikut bekerja ketika
-- TransactionDate yang disentuh: memindah jurnal ke bulan tertutup adalah cara termudah untuk
-- diam-diam mengubah laba rugi periode yang sudah final.
CREATE TRIGGER IF NOT EXISTS trg_journalentries_periode_update
BEFORE UPDATE OF TransactionDate ON JournalEntries
BEGIN
    SELECT RAISE(ABORT, 'Tanggal jurnal tidak boleh dipindah ke periode buku yang sudah ditutup')
        WHERE EXISTS (SELECT 1 FROM FiscalPeriods fp
                       WHERE fp.Status IN ('Closed', 'Locked')
                         AND fp.StartDate <= NEW.TransactionDate
                         AND fp.EndDate >= NEW.TransactionDate);
END;

INSERT OR IGNORE INTO SchemaVersion (Version, AppliedAt) VALUES (22, datetime('now'));
