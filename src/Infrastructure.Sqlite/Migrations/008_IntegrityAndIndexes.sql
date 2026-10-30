-- Seruni 22 Bakery - Integritas data & indeks v008
--
-- SQLite tidak bisa menambah CHECK pada tabel yang sudah ada tanpa menyusun ulang tabel,
-- dan menyusun ulang tabel induk akan menabrak foreign key yang aktif. Pemicu BEFORE
-- menulis mencapai hasil yang sama: nilai buruk ditolak sebelum masuk ke tabel.

-- =============================================
-- INDEKS UNTUK HUBUNGAN YANG SERING DIBACA
-- =============================================
CREATE INDEX IF NOT EXISTS idx_salespayments_salesorder ON SalesPayments(SalesOrderId);
CREATE INDEX IF NOT EXISTS idx_salesorders_product ON SalesOrders(ProductTemplateId);
CREATE INDEX IF NOT EXISTS idx_purchaseorderlines_po ON PurchaseOrderLines(PurchaseOrderId);
CREATE INDEX IF NOT EXISTS idx_purchaseorders_supplier ON PurchaseOrders(SupplierId);
CREATE INDEX IF NOT EXISTS idx_goodsreceipts_po ON GoodsReceipts(PurchaseOrderId);
CREATE INDEX IF NOT EXISTS idx_goodsreceiptlines_pol ON GoodsReceiptLines(PurchaseOrderLineId);
CREATE INDEX IF NOT EXISTS idx_goodsreceiptlines_ing ON GoodsReceiptLines(IngredientId);
CREATE INDEX IF NOT EXISTS idx_goodsreceiptlines_loc ON GoodsReceiptLines(LocationId);
CREATE INDEX IF NOT EXISTS idx_recipelines_ingredient ON RecipeLines(IngredientId);
CREATE INDEX IF NOT EXISTS idx_itembatches_ingredient ON ItemBatches(IngredientId);
CREATE INDEX IF NOT EXISTS idx_itembatches_expiry ON ItemBatches(ExpiryDate);
CREATE INDEX IF NOT EXISTS idx_auditlog_userid ON AuditLog(UserId);
CREATE INDEX IF NOT EXISTS idx_stockmovements_location ON StockMovements(LocationId);
CREATE INDEX IF NOT EXISTS idx_workorders_product ON WorkOrders(ProductTemplateId);

-- =============================================
-- NILAI UANG DAN KUANTITAS TIDAK BOLEH MINUS
-- =============================================
CREATE TRIGGER IF NOT EXISTS trg_ingredients_insert
BEFORE INSERT ON Ingredients
BEGIN
    SELECT RAISE(ABORT, 'Harga satuan bahan baku tidak boleh negatif') WHERE NEW.PricePerUnit < 0;
    SELECT RAISE(ABORT, 'Batas minimum stok bahan baku tidak boleh negatif') WHERE NEW.MinStockAlert < 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_ingredients_update
BEFORE UPDATE ON Ingredients
BEGIN
    SELECT RAISE(ABORT, 'Harga satuan bahan baku tidak boleh negatif') WHERE NEW.PricePerUnit < 0;
    SELECT RAISE(ABORT, 'Batas minimum stok bahan baku tidak boleh negatif') WHERE NEW.MinStockAlert < 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_producttemplates_insert
BEFORE INSERT ON ProductTemplates
BEGIN
    SELECT RAISE(ABORT, 'Harga jual produk tidak boleh negatif') WHERE NEW.SellingPrice < 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_producttemplates_update
BEFORE UPDATE ON ProductTemplates
BEGIN
    SELECT RAISE(ABORT, 'Harga jual produk tidak boleh negatif') WHERE NEW.SellingPrice < 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_recipelines_insert
BEFORE INSERT ON RecipeLines
BEGIN
    SELECT RAISE(ABORT, 'Jumlah bahan pada resep harus lebih dari nol') WHERE NEW.Quantity <= 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_salesorders_insert
BEFORE INSERT ON SalesOrders
BEGIN
    SELECT RAISE(ABORT, 'Nilai pesanan harus lebih dari nol') WHERE NEW.TotalAmount <= 0;
    SELECT RAISE(ABORT, 'Uang muka tidak boleh negatif') WHERE NEW.DownPayment < 0;
    SELECT RAISE(ABORT, 'Uang muka melebihi nilai pesanan') WHERE NEW.DownPayment > NEW.TotalAmount;
END;

CREATE TRIGGER IF NOT EXISTS trg_workorders_insert
BEFORE INSERT ON WorkOrders
BEGIN
    SELECT RAISE(ABORT, 'Target produksi harus lebih dari nol') WHERE NEW.TargetQuantity <= 0;
    SELECT RAISE(ABORT, 'Biaya tenaga kerja tidak boleh negatif') WHERE NEW.LaborCost < 0;
    SELECT RAISE(ABORT, 'Biaya overhead tidak boleh negatif') WHERE NEW.OverheadCost < 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_workordermaterials_insert
BEFORE INSERT ON WorkOrderMaterials
BEGIN
    SELECT RAISE(ABORT, 'Jumlah bahan standar pada SPK tidak boleh negatif') WHERE NEW.StandardQty < 0;
    SELECT RAISE(ABORT, 'Jumlah bahan aktual pada SPK tidak boleh negatif') WHERE NEW.ActualQty < 0;
    SELECT RAISE(ABORT, 'Harga satuan bahan pada SPK tidak boleh negatif') WHERE NEW.UnitCost < 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_workordermaterials_update
BEFORE UPDATE ON WorkOrderMaterials
BEGIN
    SELECT RAISE(ABORT, 'Jumlah bahan aktual pada SPK tidak boleh negatif') WHERE NEW.ActualQty < 0;
    SELECT RAISE(ABORT, 'Harga satuan bahan pada SPK tidak boleh negatif') WHERE NEW.UnitCost < 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_workorderscraps_insert
BEFORE INSERT ON WorkOrderScraps
BEGIN
    SELECT RAISE(ABORT, 'Jumlah produk rusak harus lebih dari nol') WHERE NEW.Quantity <= 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_purchaseorderlines_insert
BEFORE INSERT ON PurchaseOrderLines
BEGIN
    SELECT RAISE(ABORT, 'Jumlah yang dipesan harus lebih dari nol') WHERE NEW.OrderedQty <= 0;
    SELECT RAISE(ABORT, 'Harga satuan pembelian tidak boleh negatif') WHERE NEW.UnitCostMinorUnits < 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_purchaseorderlines_update
BEFORE UPDATE ON PurchaseOrderLines
BEGIN
    SELECT RAISE(ABORT, 'Jumlah diterima tidak boleh negatif') WHERE NEW.ReceivedQty < 0;
    SELECT RAISE(ABORT, 'Jumlah diterima melebihi jumlah yang dipesan') WHERE NEW.ReceivedQty > NEW.OrderedQty;
    SELECT RAISE(ABORT, 'Jumlah yang dipesan harus lebih dari nol') WHERE NEW.OrderedQty <= 0;
END;

CREATE TRIGGER IF NOT EXISTS trg_goodsreceiptlines_insert
BEFORE INSERT ON GoodsReceiptLines
BEGIN
    SELECT RAISE(ABORT, 'Jumlah barang diterima harus lebih dari nol') WHERE NEW.ReceivedQty <= 0;
    SELECT RAISE(ABORT, 'Harga satuan barang diterima tidak boleh negatif') WHERE NEW.UnitCostMinorUnits < 0;
END;

-- =============================================
-- PEMBAYARAN TIDAK BOLEH MELEBIHI NILAI PESANAN
-- =============================================
CREATE TRIGGER IF NOT EXISTS trg_salespayments_insert
BEFORE INSERT ON SalesPayments
BEGIN
    SELECT RAISE(ABORT, 'Nominal pembayaran harus lebih dari nol') WHERE NEW.Amount <= 0;
    -- Baris yang sedang disisipkan belum masuk SUM, jadi total lama + NEW dibandingkan ke nilai pesanan.
    SELECT RAISE(ABORT, 'Total pembayaran melebihi nilai pesanan')
    WHERE NEW.Amount > (
        SELECT so.TotalAmount - so.DownPayment -
               COALESCE((SELECT SUM(p.Amount) FROM SalesPayments p WHERE p.SalesOrderId = NEW.SalesOrderId), 0)
        FROM SalesOrders so WHERE so.Id = NEW.SalesOrderId);
END;
