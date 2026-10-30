-- 010: Kunci idempotensi untuk aksi perubahan.
-- Tombol "Simpan" yang ditekan dua kali, koneksi yang terputus di tengah permintaan, atau
-- permintaan ulang dari layar yang sama sebelumnya selalu menghasilkan dua dokumen: dua pesanan,
-- dua penerimaan barang, dua jurnal. Pembayaran sudah punya kolom kunci sendiri, tapi aksi lain
-- tidak punya tempat untuk mencatat "sudah pernah dikerjakan".
-- Baris dibuat saat permintaan diterima (ReservedAt) dan ditutup saat selesai (CompletedAt).
-- Selama CompletedAt masih kosong, permintaan dengan kunci yang sama ditolak sebagai "sedang
-- diproses"; kalau aksinya gagal, barisnya dihapus supaya pengguna boleh mencoba lagi.
CREATE TABLE IF NOT EXISTS IdempotencyKeys (
    Key         TEXT PRIMARY KEY,
    Route       TEXT NOT NULL,
    UserId      INTEGER REFERENCES Users(Id),
    ResourceId  INTEGER,
    ReservedAt  TEXT NOT NULL DEFAULT (datetime('now')),
    CompletedAt TEXT
);

-- Jejak audit tumbuh secepat transaksi; pembersihan harian mencari baris berdasarkan tanggal.
CREATE INDEX IF NOT EXISTS idx_idempotencykeys_reserved ON IdempotencyKeys(ReservedAt);
