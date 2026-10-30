-- Migration 015: Notifikasi internal
--
-- Notifikasi disimpan lebih dulu di SQLite lalu dibaca ulang oleh aplikasi. Isinya berasal dari
-- transaksi dan kondisi operasional nyata, bukan pesan eksternal: tidak ada provider, antrean
-- keluar, atau webhook di sini.
--
-- Setiap baris ditujukan ke satu pengguna internal supaya status baca bisa berbeda per orang.
-- DedupeKey unik mencegah pengingat yang sama (stok menipis, lot kedaluwarsa) ditulis berulang
-- setiap kali penyegaran berjalan. Penghapusan pengguna ikut menghapus notifikasinya.
--
-- EventType sengaja tidak dibatasi CHECK: jenis peristiwa tumbuh bersama fitur, dan menambah
-- jenis baru tidak boleh menuntut migrasi tabel. Nilai yang dipakai saat ini:
--   transaction.order.created / transaction.order.paid / transaction.order.cancelled
--   transaction.purchase.received / transaction.production.completed
--   transaction.production.cancelled / transaction.qc.passed / transaction.qc.failed
--   transaction.return.created / stock.low / batch.expiry / task.pickup.overdue
--   report.daily

CREATE TABLE IF NOT EXISTS Notifications (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    EventType       TEXT NOT NULL,
    Severity        TEXT NOT NULL DEFAULT 'Info'
                        CHECK (Severity IN ('Info','Warning','Critical')),
    Title           TEXT NOT NULL,
    Body            TEXT NOT NULL,
    EntityType      TEXT,
    EntityId        INTEGER,
    RecipientUserId INTEGER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    DedupeKey       TEXT NOT NULL,
    IsRead          INTEGER NOT NULL DEFAULT 0 CHECK (IsRead IN (0,1)),
    ReadAt          TEXT,
    CreatedAt       TEXT NOT NULL,
    MetadataJson    TEXT
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_notifications_dedupe ON Notifications(DedupeKey);
CREATE INDEX IF NOT EXISTS idx_notifications_inbox
    ON Notifications(RecipientUserId, IsRead, CreatedAt DESC);
CREATE INDEX IF NOT EXISTS idx_notifications_event
    ON Notifications(EventType, CreatedAt DESC);
