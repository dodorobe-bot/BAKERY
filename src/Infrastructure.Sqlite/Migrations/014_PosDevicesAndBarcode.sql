-- Migration 014: Perangkat kasir (antrean cetak) dan barcode produk
--
-- Struk harus bisa dicetak tanpa kehilangan transaksi. Karena printer bisa mati, kehabisan
-- kertas, atau hanya lambat, hasil cetak dicatat sebagai pekerjaan tersendiri: pesanan sudah
-- tersimpan sebelum pencetakan dimulai, dan kegagalan cetak hanya menandai pekerjaan itu,
-- bukan membatalkan atau mengulang penjualan. Tanpa tabel ini "sudah dicetak atau belum"
-- hanya ada di kepala kasir.

CREATE TABLE IF NOT EXISTS PrintJobs (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderId             INTEGER REFERENCES SalesOrders(Id) ON DELETE SET NULL,
    Kind                TEXT NOT NULL CHECK (Kind IN ('Receipt','Test')),
    PrinterName         TEXT,
    PaperWidthMm        INTEGER NOT NULL CHECK (PaperWidthMm IN (58,80)),
    -- Pending ditulis sebelum perangkat disentuh. Kalau aplikasi berhenti di tengah jalan,
    -- baris Pending yang menggantung ditandai Failed saat start berikutnya, sehingga antrean
    -- tidak pernah terlihat "sedang dicetak" selamanya.
    Status              TEXT NOT NULL DEFAULT 'Pending'
                            CHECK (Status IN ('Pending','Printed','Failed')),
    Attempts            INTEGER NOT NULL DEFAULT 0,
    LastError           TEXT,
    -- Isi struk disimpan apa adanya: bukti apa yang diminta untuk dicetak, bukan hanya
    -- "sudah dicetak", dan bisa dicetak ulang tanpa membaca ulang data yang mungkin berubah.
    Payload             TEXT NOT NULL,
    CreatedAt           TEXT NOT NULL,
    CreatedByUserId     INTEGER REFERENCES Users(Id),
    FinishedAt          TEXT
);

CREATE INDEX IF NOT EXISTS idx_printjobs_status ON PrintJobs(Status, CreatedAt);
CREATE INDEX IF NOT EXISTS idx_printjobs_order ON PrintJobs(OrderId, CreatedAt);

-- Barcode hanya dipakai untuk mencari produk saat dipindai; produk lama tetap sah tanpa
-- barcode. Indeks unik parsial menolak dua produk dengan barcode sama tanpa memaksa kolom
-- itu terisi, karena tidak semua produk bakery diberi label barcode.
ALTER TABLE ProductTemplates ADD COLUMN Barcode TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS ux_producttemplates_barcode
    ON ProductTemplates(Barcode COLLATE NOCASE)
    WHERE Barcode IS NOT NULL AND Barcode <> '';

INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES
    ('device.printer.name', ''),
    ('device.printer.mode', 'windows-driver'),
    ('device.paper.width_mm', '80'),
    ('device.printer.auto_print', '0'),
    ('device.scanner.suffix', 'enter');
