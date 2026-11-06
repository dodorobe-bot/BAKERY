-- Migration 021: retur pemasok dan pembayaran hutang pemasok
--
-- Kasus uji D5 (baris 45 workbook): "retur sebagian, lalu bayar hutang" menuntut saldo hutang
-- turun tepat. Sampai versi ini tidak ada satu pun jalur yang bisa menurunkan 2-002 Utang Usaha:
-- penerimaan barang mengkredit akun itu, dan pembayarannya tidak pernah dicatat. Layar Keuangan
-- menampilkan tombol pembayaran pemasok dalam keadaan disable, dan pesan pembatalan PO sudah
-- menyuruh pengguna memakai fitur retur yang belum ada.
--
-- Bentuknya mengikuti apa yang sudah dibukukan: kredit 2-002 lahir per struk penerimaan, bukan per
-- PO (PurchaseRepository menautkan SourceDocumentId jurnal ke GoodsReceiptId). Jadi retur dan
-- pembayaran juga mengikat satu struk penerimaan, supaya saldo setiap struk bisa dijelaskan baris
-- per baris tanpa alokasi.
--
-- Nilai barang yang keluar dari persediaan TIDAK disimpan di sini: ia adalah nilai lapisan FIFO
-- yang benar-benar habis saat stok dikurangi (CostLayerLedger), yang tercatat di jurnal dokumennya.
-- Header dokumen sengaja tidak menyimpan total — sama seperti GoodsReceipts — supaya tidak ada
-- angka kedua yang bisa tidak cocok dengan barisnya.

CREATE TABLE IF NOT EXISTS SupplierReturns (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    ReturnNumber    TEXT NOT NULL UNIQUE,
    GoodsReceiptId  INTEGER NOT NULL REFERENCES GoodsReceipts(Id),
    PurchaseOrderId INTEGER NOT NULL REFERENCES PurchaseOrders(Id),
    Reason          TEXT NOT NULL,
    Notes           TEXT,
    ReturnedAt      TEXT NOT NULL DEFAULT (datetime('now')),
    JournalEntryId  INTEGER REFERENCES JournalEntries(Id),
    IdempotencyKey  TEXT UNIQUE,
    CreatedByUserId INTEGER REFERENCES Users(Id),
    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS SupplierReturnLines (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    SupplierReturnId   INTEGER NOT NULL REFERENCES SupplierReturns(Id) ON DELETE CASCADE,
    GoodsReceiptLineId INTEGER NOT NULL REFERENCES GoodsReceiptLines(Id),
    IngredientId       INTEGER NOT NULL REFERENCES Ingredients(Id),
    LocationId         INTEGER NOT NULL REFERENCES StorageLocations(Id),
    Quantity           REAL NOT NULL,
    UnitCostMinorUnits INTEGER NOT NULL,
    ValueMinorUnits    INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS SupplierPayments (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    PaymentNumber   TEXT NOT NULL UNIQUE,
    GoodsReceiptId  INTEGER NOT NULL REFERENCES GoodsReceipts(Id),
    Amount          INTEGER NOT NULL,
    Method          TEXT NOT NULL DEFAULT 'Tunai',
    Notes           TEXT,
    PaidAt          TEXT NOT NULL DEFAULT (datetime('now')),
    JournalEntryId  INTEGER REFERENCES JournalEntries(Id),
    IdempotencyKey  TEXT UNIQUE,
    CreatedByUserId INTEGER REFERENCES Users(Id),
    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX IF NOT EXISTS idx_supplierreturns_receipt ON SupplierReturns(GoodsReceiptId, ReturnedAt);
CREATE INDEX IF NOT EXISTS idx_supplierreturnlines_receipt
    ON SupplierReturnLines(GoodsReceiptLineId);
CREATE INDEX IF NOT EXISTS idx_supplierpayments_receipt ON SupplierPayments(GoodsReceiptId, PaidAt);

-- Sisa hutang satu struk dihitung dari baris penerimaannya, dan pemicu di bawah menanyakan hal
-- yang sama setiap kali satu baris ditulis. GoodsReceiptLines sebelumnya hanya diindeks per baris
-- PO, bahan, dan lokasi — tanpa indeks ini, membaca daftar utang berarti memindai seluruh tabel
-- baris penerimaan.
CREATE INDEX IF NOT EXISTS idx_goodsreceiptlines_receipt
    ON GoodsReceiptLines(GoodsReceiptId);

-- Pagar tiga lapis (layanan, repository di dalam transaksi, lalu di sini) dimulai dari jumlah
-- barang: retur ke-n pada baris penerimaan yang sama tidak boleh membuat total retur melebihi
-- barang yang benar-benar diterima, walaupun dua retur datang berbarengan.
CREATE TRIGGER IF NOT EXISTS trg_supplierreturnlines_jumlah_insert
BEFORE INSERT ON SupplierReturnLines
BEGIN
    SELECT RAISE(ABORT, 'Jumlah retur pemasok harus lebih dari nol')
        WHERE NEW.Quantity <= 0;
    SELECT RAISE(ABORT, 'Nominal retur pemasok tidak boleh negatif')
        WHERE NEW.ValueMinorUnits < 0 OR NEW.UnitCostMinorUnits < 0;
    SELECT RAISE(ABORT, 'Nilai retur harus sama dengan jumlah dikali harga baris penerimaan')
        WHERE NEW.ValueMinorUnits <> CAST(ROUND(NEW.Quantity * NEW.UnitCostMinorUnits) AS INTEGER);
    SELECT RAISE(ABORT, 'Jumlah retur melebihi barang yang diterima pada baris ini')
        WHERE (SELECT COALESCE(SUM(l.Quantity), 0) FROM SupplierReturnLines l
                WHERE l.GoodsReceiptLineId = NEW.GoodsReceiptLineId) + NEW.Quantity
              > (SELECT g.ReceivedQty FROM GoodsReceiptLines g WHERE g.Id = NEW.GoodsReceiptLineId);
END;

-- Beban satu struk: seluruh retur (termasuk baris yang sedang disisipkan) ditambah seluruh
-- pembayaran tidak boleh melampaui nilai terima struk itu. Tanpa pagar ini, 2-002 bisa turun
-- di bawah nol dan neraca menampilkan piutang yang dilabeli utang.
CREATE TRIGGER IF NOT EXISTS trg_supplierreturnlines_beban_struk_insert
BEFORE INSERT ON SupplierReturnLines
BEGIN
    SELECT RAISE(ABORT, 'Retur melebihi sisa hutang struk penerimaan ini')
        WHERE (
            (SELECT COALESCE(SUM(l.ValueMinorUnits), 0) FROM SupplierReturnLines l
              JOIN GoodsReceiptLines g ON g.Id = l.GoodsReceiptLineId
             WHERE g.GoodsReceiptId = (SELECT GoodsReceiptId FROM GoodsReceiptLines WHERE Id = NEW.GoodsReceiptLineId))
          + NEW.ValueMinorUnits
          + (SELECT COALESCE(SUM(p.Amount), 0) FROM SupplierPayments p
              WHERE p.GoodsReceiptId = (SELECT GoodsReceiptId FROM GoodsReceiptLines WHERE Id = NEW.GoodsReceiptLineId))
        ) > (SELECT COALESCE(SUM(CAST(ROUND(g.ReceivedQty * g.UnitCostMinorUnits) AS INTEGER)), 0)
               FROM GoodsReceiptLines g
              WHERE g.GoodsReceiptId = (SELECT GoodsReceiptId FROM GoodsReceiptLines WHERE Id = NEW.GoodsReceiptLineId));
END;

-- Pembayaran mengikuti sisa yang sama, dihitung dari struk yang dibayar.
CREATE TRIGGER IF NOT EXISTS trg_supplierpayments_insert
BEFORE INSERT ON SupplierPayments
BEGIN
    SELECT RAISE(ABORT, 'Nominal pembayaran pemasok harus lebih dari nol')
        WHERE NEW.Amount <= 0;
    SELECT RAISE(ABORT, 'Pembayaran melebihi sisa hutang struk penerimaan ini')
        WHERE (SELECT COALESCE(SUM(p.Amount), 0) FROM SupplierPayments p
                WHERE p.GoodsReceiptId = NEW.GoodsReceiptId) + NEW.Amount
              + (SELECT COALESCE(SUM(l.ValueMinorUnits), 0) FROM SupplierReturnLines l
                  JOIN GoodsReceiptLines g ON g.Id = l.GoodsReceiptLineId
                 WHERE g.GoodsReceiptId = NEW.GoodsReceiptId)
              > (SELECT COALESCE(SUM(CAST(ROUND(g.ReceivedQty * g.UnitCostMinorUnits) AS INTEGER)), 0)
                   FROM GoodsReceiptLines g WHERE g.GoodsReceiptId = NEW.GoodsReceiptId);
END;

INSERT OR IGNORE INTO SchemaVersion (Version, AppliedAt) VALUES (21, datetime('now'));
