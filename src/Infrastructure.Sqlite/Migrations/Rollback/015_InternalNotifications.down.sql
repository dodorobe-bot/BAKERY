-- Rollback migrasi 015.
--
-- PERINGATAN: seluruh riwayat notifikasi internal akan hilang. Jalankan hanya pada basis data
-- uji atau setelah cadangan dibuat.

DROP INDEX IF EXISTS idx_notifications_event;
DROP INDEX IF EXISTS idx_notifications_inbox;
DROP INDEX IF EXISTS ux_notifications_dedupe;
DROP TABLE IF EXISTS Notifications;

DELETE FROM SchemaVersion WHERE Version = 15;
