-- Penomoran dokumen deterministik.
-- generator lama (Random.Next untuk SPK, stempel detik untuk PO, GUID[6] untuk ORD) bisa
-- menghasilkan nomor yang sama pada dua dokumen di hari yang sama, dan kolom nomornya UNIQUE,
-- sehingga penyimpanan kedua gagal menjadi 500. Satu baris urutan per jenis per hari membuat
-- nomor selalu naik dan tidak pernah bentrok.
CREATE TABLE IF NOT EXISTS DocumentSequences (
    Kind      TEXT    NOT NULL,   -- 'Order' | 'WorkOrder' | 'PurchaseOrder'
    DayKey    TEXT    NOT NULL,   -- 'yyyy-MM-dd' (UTC)
    LastValue INTEGER NOT NULL,
    PRIMARY KEY (Kind, DayKey)
);
