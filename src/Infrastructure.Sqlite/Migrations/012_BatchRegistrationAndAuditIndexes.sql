-- Migration 012: Pencatatan lot bahan & indeks jejak audit
-- Layar "Nomor Batch & Kedaluwarsa" hanya berguna kalau lot benar-benar punya asal: siapa
-- yang menerimanya, dari baris PO yang mana, dan kapan tanggal kedaluwarsanya diketik.
-- Lot yang tidak bisa ditelusuri kembali ke struk penerimaan adalah daftar hiasan, jadi
-- kolom asalnya ditambahkan di sini, bukan dibiarkan di luar skema.

ALTER TABLE GoodsReceiptLines ADD COLUMN BatchNumber TEXT;
ALTER TABLE GoodsReceiptLines ADD COLUMN ExpiryDate TEXT;

-- Satu lot penerimaan bisa memuat nomor kemasan dan tanggal kedaluwarsa yang sama untuk
-- beberapa baris; penunjuk balik ini membuat "kok ada lot ini?" bisa dijawab dari data.
ALTER TABLE ItemBatches ADD COLUMN GoodsReceiptLineId INTEGER REFERENCES GoodsReceiptLines(Id) ON DELETE SET NULL;

-- Nomor lot yang sama boleh muncul lebih dari sekali: satu kemasan lot bisa dibeli pada dua
-- struk berbeda, dan keduanya punya tanggal terima sendiri. Indeks ini dipakai layar
-- kedaluwarsa untuk mencari lot per bahan, bukan untuk melarang nomor yang sama.
CREATE INDEX IF NOT EXISTS idx_itembatches_ingredient_batch
    ON ItemBatches(IngredientId, BatchNumber COLLATE NOCASE);

-- Daftar kedaluwarsa dibaca urut tanggal, dan layar audit dibaca urut waktu: tanpa indeks ini,
-- keduanya memindai seluruh tabel seiring basis data membesar.
CREATE INDEX IF NOT EXISTS idx_itembatches_expiry_ordered ON ItemBatches(ExpiryDate, IngredientId);
CREATE INDEX IF NOT EXISTS idx_auditlog_occurred ON AuditLog(OccurredAt);
CREATE INDEX IF NOT EXISTS idx_auditlog_entity ON AuditLog(EntityType, EntityId);

-- Tanggal kedaluwarsa di belakang tanggal terima membuat lot terlihat "mandek" selamanya.
-- Aturan ini ditegakkan lewat pemicu karena CHECK pada ALTER TABLE ADD COLUMN tidak dijamin
-- ada di semua versi SQLite (lihat pola migrasi 011).
CREATE TRIGGER IF NOT EXISTS trg_itembatches_expiry_insert
BEFORE INSERT ON ItemBatches
BEGIN
    SELECT RAISE(ABORT, 'Tanggal kedaluwarsa lot harus setelah tanggal terima')
    WHERE NEW.ExpiryDate <= NEW.ReceivedDate;
END;

CREATE TRIGGER IF NOT EXISTS trg_itembatches_expiry_update
BEFORE UPDATE OF ExpiryDate, ReceivedDate ON ItemBatches
BEGIN
    SELECT RAISE(ABORT, 'Tanggal kedaluwarsa lot harus setelah tanggal terima')
    WHERE NEW.ExpiryDate <= NEW.ReceivedDate;
END;

-- Nama tempat kerja dipakai bilah atas aplikasi. Selama nilainya tidak ada di pengaturan,
-- layar menampilkan teks tetap yang belum tentu benar untuk cabang lain.
INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('outlet_name', 'Kantor Sentral & Toko Utama');
