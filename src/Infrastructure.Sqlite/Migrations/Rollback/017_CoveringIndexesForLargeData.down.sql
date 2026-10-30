-- Rollback migrasi 017 (indeks penutup).
--
-- PERINGATAN: hanya bentuk indeks yang dikembalikan; tidak ada satu pun baris data yang
-- dihapus. Statistik ANALYZE dibiarkan karena isinya catatan rencana kueri, bukan data
-- pengguna, dan akan diperbarui sendiri pada ANALYZE berikutnya.

DROP INDEX IF EXISTS idx_salesorders_created_agg;
CREATE INDEX IF NOT EXISTS idx_salesorders_created
    ON SalesOrders(CreatedAt);

DROP INDEX IF EXISTS idx_purchaseorders_created;

DROP INDEX IF EXISTS idx_salesorders_open;

DROP INDEX IF EXISTS idx_salespayments_order_amount;
CREATE INDEX IF NOT EXISTS idx_salespayments_salesorder
    ON SalesPayments(SalesOrderId);

DELETE FROM SchemaVersion WHERE Version = 17;
