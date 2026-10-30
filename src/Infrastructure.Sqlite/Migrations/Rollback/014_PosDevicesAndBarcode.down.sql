-- Rollback migrasi 014.
--
-- PERINGATAN: riwayat cetak dan barcode produk akan hilang. Jalankan hanya pada basis data
-- uji atau setelah cadangan dibuat; nomor barcode yang sudah ditempel di kemasan tidak bisa
-- dipulihkan dari skrip ini.

DROP INDEX IF EXISTS idx_printjobs_status;
DROP INDEX IF EXISTS idx_printjobs_order;
DROP TABLE IF EXISTS PrintJobs;

DROP INDEX IF EXISTS ux_producttemplates_barcode;
ALTER TABLE ProductTemplates DROP COLUMN Barcode;

DELETE FROM AppSettings WHERE Key IN (
    'device.printer.name',
    'device.printer.mode',
    'device.paper.width_mm',
    'device.printer.auto_print',
    'device.scanner.suffix'
);

DELETE FROM SchemaVersion WHERE Version = 14;
