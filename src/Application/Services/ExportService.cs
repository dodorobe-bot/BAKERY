using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <summary>
/// Export laporan ke PDF (QuestPDF) atau Excel (ClosedXML).
/// Semua amount dalam minor units; ditampilkan dengan FormatRupiah.
/// </summary>
public class ExportService
{
    private readonly ISalesOrderRepository _orders;
    private readonly IJournalRepository _journals;
    private readonly IIngredientRepository _ingredients;
    private readonly IPurchaseOrderRepository _purchases;
    private readonly ISettingRepository? _settings;

    public ExportService(
        ISalesOrderRepository orders,
        IJournalRepository journals,
        IIngredientRepository ingredients,
        IPurchaseOrderRepository purchases,
        ISettingRepository? settings = null)
    {
        _orders = orders;
        _journals = journals;
        _ingredients = ingredients;
        _purchases = purchases;
        _settings = settings;
    }

    // Nama di kepala laporan mengikuti Pengaturan, sama seperti struk: berkas yang dikirim ke
    // pembeli tidak boleh masih mencantumkan merek lama setelah pemilik menggantinya.
    private async Task<string> NamaTokoAsync() =>
        _settings is null
            ? StoreBrand.DefaultName
            : StoreBrand.Name(await _settings.GetAsync(StoreBrand.NameKey));

    public static string FormatRupiah(long minorUnits) => Money.Format(minorUnits);

    // ───────────────────────────────────────────────────────────────────────
    //  JURNAL UMUM
    // ───────────────────────────────────────────────────────────────────────

    public async Task<(byte[] data, string contentType, string filename)> ExportJournalAsync(
        DateTime from, DateTime to, string format)
    {
        var entries = await _journals.GetByDateRangeAsync(from, to);
        var rows = FlattenJournal(entries);
        var namaToko = await NamaTokoAsync();

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = BuildJournalPdf(from, to, rows, namaToko);
            return (bytes, "application/pdf", $"jurnal-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
        }
        var xls = BuildJournalExcel(rows, namaToko);
        return (xls, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"jurnal-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx");
    }

    private record JournalRow(DateTime Date, int EntryId, string Description, long Debit, long Credit, string AccountLabel);

    private static List<JournalRow> FlattenJournal(IReadOnlyList<JournalEntry> entries)
    {
        var rows = new List<JournalRow>();
        foreach (var e in entries)
        {
            foreach (var l in e.Lines)
            {
                rows.Add(new JournalRow(
                    e.TransactionDate,
                    e.Id,
                    e.Description ?? "",
                    l.DebitAmount,
                    l.CreditAmount,
                    l.Account is null ? $"Acct {l.AccountId}" : $"{l.Account.Code} - {l.Account.Name}"));
            }
        }
        return rows;
    }

    private byte[] BuildJournalPdf(DateTime from, DateTime to, List<JournalRow> rows, string namaToko)
    {
        return Document.Create(container => container.Page(page =>
        {
            page.Margin(28);
            page.Header().Column(col =>
            {
                col.Item().Text(namaToko).FontSize(18).SemiBold();
                col.Item().Text($"Laporan Jurnal Umum").FontSize(13);
                col.Item().Text($"Periode: {from:dd/MM/yyyy} - {to:dd/MM/yyyy}").FontSize(10);
                col.Item().LineHorizontal(1);
            });
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(80);
                    c.ConstantColumn(90);
                    c.RelativeColumn();
                    c.ConstantColumn(110);
                    c.ConstantColumn(110);
                    c.RelativeColumn();
                });
                table.Header(header =>
                {
                    table.Cell().Element(h => CellStyle(h)).Text("Tanggal");
                    header.Cell().Element(h => CellStyle(h)).Text("Kode");
                    header.Cell().Element(h => CellStyle(h)).Text("Deskripsi");
                    header.Cell().Element(h => CellStyle(h)).AlignRight().Text("Debit");
                    header.Cell().Element(h => CellStyle(h)).AlignRight().Text("Kredit");
                    header.Cell().Element(h => CellStyle(h)).Text("Akun");
                });

                foreach (var r in rows)
                {
                    table.Cell().Element(c => CellStyle(c)).Text(r.Date.ToString("dd/MM/yy"));
                    table.Cell().Element(c => CellStyle(c)).Text($"JE-{r.EntryId}");
                    table.Cell().Element(c => CellStyle(c)).Text(r.Description);
                    table.Cell().Element(c => CellStyle(c)).AlignRight().Text(FormatRupiah(r.Debit));
                    table.Cell().Element(c => CellStyle(c)).AlignRight().Text(FormatRupiah(r.Credit));
                    table.Cell().Element(c => CellStyle(c)).Text(r.AccountLabel);
                }
            });
            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Dicetak: ");
                t.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            });
        })).GeneratePdf();
    }

    private byte[] BuildJournalExcel(List<JournalRow> rows, string namaToko)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Jurnal");
        ws.Cell(1, 1).Value = $"{namaToko} - Laporan Jurnal Umum";
        ws.Range(1, 1, 1, 6).Merge().Style.Font.Bold = true;
        var headers = new[] { "Tanggal", "Kode", "Deskripsi", "Debit", "Kredit", "Akun" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(3, i + 1).Value = headers[i];
        ws.Range(3, 1, 3, 6).Style.Font.Bold = true;

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int row = i + 4;
            ws.Cell(row, 1).Value = r.Date.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = $"JE-{r.EntryId}";
            ws.Cell(row, 3).Value = r.Description;
            ws.Cell(row, 4).Value = r.Debit / 100m;
            ws.Cell(row, 5).Value = r.Credit / 100m;
            ws.Cell(row, 6).Value = r.AccountLabel;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  PENJUALAN
    // ───────────────────────────────────────────────────────────────────────

    public async Task<(byte[] data, string contentType, string filename)> ExportOrdersAsync(
        DateTime from, DateTime to, string format)
    {
        // Filter tanggal dilakukan di SQL. Versi sebelumnya mengambil 500 pesanan terbaru lalu
        // menyaring di memori, sehingga periode lama bisa keluar kosong atau terpotong tanpa
        // tanda apa pun pada berkas ekspornya.
        var filtered = (await _orders.GetByPickupDateRangeAsync(from, to)).ToList();
        var namaToko = await NamaTokoAsync();

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = BuildOrdersPdf(from, to, filtered, namaToko);
            return (bytes, "application/pdf", $"penjualan-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
        }
        var xls = BuildOrdersExcel(filtered, namaToko);
        return (xls, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"penjualan-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx");
    }

    private byte[] BuildOrdersPdf(DateTime from, DateTime to, List<SalesOrder> orders, string namaToko)
    {
        return Document.Create(container => container.Page(page =>
        {
            // Tujuh kolom dengan tiga kolom uang tidak muat di A4 tegak (540 pt kolom tetap
            // melampaui 539,28 pt isi halaman); lanskap memberi ruang untuk nama pelanggan.
            page.Size(PageSizes.A4.Landscape());
            page.Margin(28);
            page.Header().Column(col =>
            {
                col.Item().Text(namaToko).FontSize(18).SemiBold();
                col.Item().Text("Laporan Penjualan").FontSize(13);
                col.Item().Text($"Periode: {from:dd/MM/yyyy} - {to:dd/MM/yyyy}").FontSize(10);
                col.Item().LineHorizontal(1);
            });
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(90);
                    c.RelativeColumn();
                    c.ConstantColumn(70);
                    c.ConstantColumn(100);
                    c.ConstantColumn(100);
                    c.ConstantColumn(100);
                    c.ConstantColumn(80);
                });
                table.Header(header =>
                {
                    header.Cell().Element(h => CellStyle(h)).Text("No Order");
                    header.Cell().Element(h => CellStyle(h)).Text("Pelanggan");
                    header.Cell().Element(h => CellStyle(h)).Text("Tgl Pickup");
                    header.Cell().Element(h => CellStyle(h)).AlignRight().Text("Total");
                    header.Cell().Element(h => CellStyle(h)).AlignRight().Text("DP");
                    header.Cell().Element(h => CellStyle(h)).AlignRight().Text("Sisa");
                    header.Cell().Element(h => CellStyle(h)).Text("Status");
                });
                foreach (var o in orders)
                {
                    table.Cell().Element(c => CellStyle(c)).Text(o.OrderNumber ?? $"#{o.Id}");
                    table.Cell().Element(c => CellStyle(c)).Text(o.CustomerNameSnapshot);
                    table.Cell().Element(c => CellStyle(c)).Text(o.PickupDate.ToString("dd/MM/yy"));
                    table.Cell().Element(c => CellStyle(c)).AlignRight().Text(FormatRupiah(o.TotalAmount));
                    table.Cell().Element(c => CellStyle(c)).AlignRight().Text(FormatRupiah(o.DownPayment));
                    table.Cell().Element(c => CellStyle(c)).AlignRight().Text(FormatRupiah(o.RemainingAmount()));
                    table.Cell().Element(c => CellStyle(c)).Text(o.Status.ToString());
                }
            });
            page.Footer().AlignCenter().Text($"Total: {orders.Count} transaksi | {FormatRupiah(orders.Sum(o => o.TotalAmount))}");
        })).GeneratePdf();
    }

    private byte[] BuildOrdersExcel(List<SalesOrder> orders, string namaToko)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Penjualan");
        ws.Cell(1, 1).Value = $"{namaToko} - Laporan Penjualan";
        ws.Range(1, 1, 1, 7).Merge().Style.Font.Bold = true;
        var headers = new[] { "No Order", "Pelanggan", "Telpon", "Deskripsi", "Tgl Pickup", "Total (Rp)", "DP (Rp)", "Sisa (Rp)", "Status" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(3, i + 1).Value = headers[i];
        ws.Range(3, 1, 3, headers.Length).Style.Font.Bold = true;
        for (int i = 0; i < orders.Count; i++)
        {
            var o = orders[i];
            int row = i + 4;
            ws.Cell(row, 1).Value = o.OrderNumber ?? $"#{o.Id}";
            ws.Cell(row, 2).Value = o.CustomerNameSnapshot;
            ws.Cell(row, 3).Value = o.PhoneSnapshot ?? "";
            ws.Cell(row, 4).Value = o.Description;
            ws.Cell(row, 5).Value = o.PickupDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 6).Value = o.TotalAmount / 100m;
            ws.Cell(row, 7).Value = o.DownPayment / 100m;
            ws.Cell(row, 8).Value = o.RemainingAmount() / 100m;
            ws.Cell(row, 9).Value = o.Status.ToString();
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  STOK
    // ───────────────────────────────────────────────────────────────────────

    public async Task<(byte[] data, string contentType, string filename)> ExportStockAsync(string format)
    {
        var ingredients = await _ingredients.GetAllActiveAsync();
        var namaToko = await NamaTokoAsync();

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = BuildStockPdf(ingredients, namaToko);
            return (bytes, "application/pdf", $"stok-{DateTime.Now:yyyy-MM-dd}.pdf");
        }
        var xls = BuildStockExcel(ingredients, namaToko);
        return (xls, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"stok-{DateTime.Now:yyyy-MM-dd}.xlsx");
    }

    private byte[] BuildStockPdf(IEnumerable<Ingredient> ingredients, string namaToko)
    {
        return Document.Create(container => container.Page(page =>
        {
            page.Margin(28);
            page.Header().Column(col =>
            {
                col.Item().Text(namaToko).FontSize(18).SemiBold();
                col.Item().Text("Laporan Stok Bahan Baku").FontSize(13);
                col.Item().Text($"Tanggal: {DateTime.Now:dd/MM/yyyy}").FontSize(10);
                col.Item().LineHorizontal(1);
            });
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.ConstantColumn(80);
                    c.ConstantColumn(100);
                    c.ConstantColumn(100);
                    c.ConstantColumn(80);
                });
                table.Header(header =>
                {
                    header.Cell().Element(h => CellStyle(h)).Text("Nama Bahan");
                    header.Cell().Element(h => CellStyle(h)).Text("Satuan");
                    header.Cell().Element(h => CellStyle(h)).AlignRight().Text("Stok");
                    header.Cell().Element(h => CellStyle(h)).AlignRight().Text("Min Stok");
                    header.Cell().Element(h => CellStyle(h)).Text("Status");
                });
                foreach (var i in ingredients)
                {
                    string status = i.CurrentStock <= i.MinStockAlert ? "KRITIS" : "OK";
                    table.Cell().Element(c => CellStyle(c)).Text(i.Name);
                    table.Cell().Element(c => CellStyle(c)).Text(i.Unit);
                    table.Cell().Element(c => CellStyle(c)).AlignRight().Text(i.CurrentStock.ToString("N2"));
                    table.Cell().Element(c => CellStyle(c)).AlignRight().Text(i.MinStockAlert.ToString("N2"));
                    table.Cell().Element(c => CellStyle(c)).Text(status);
                }
            });
        })).GeneratePdf();
    }

    private byte[] BuildStockExcel(IEnumerable<Ingredient> ingredients, string namaToko)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Stok");
        ws.Cell(1, 1).Value = $"{namaToko} - Laporan Stok";
        ws.Range(1, 1, 1, 5).Merge().Style.Font.Bold = true;
        var headers = new[] { "Nama Bahan", "Merek", "Satuan", "Stok", "Min Stok", "Status" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(3, i + 1).Value = headers[i];
        ws.Range(3, 1, 3, headers.Length).Style.Font.Bold = true;
        int row = 4;
        foreach (var i in ingredients)
        {
            ws.Cell(row, 1).Value = i.Name;
            ws.Cell(row, 2).Value = i.Brand ?? "";
            ws.Cell(row, 3).Value = i.Unit;
            ws.Cell(row, 4).Value = i.CurrentStock;
            ws.Cell(row, 5).Value = i.MinStockAlert;
            ws.Cell(row, 6).Value = i.CurrentStock <= i.MinStockAlert ? "KRITIS" : "OK";
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  PEMBELIAN
    // ───────────────────────────────────────────────────────────────────────

    public async Task<(byte[] data, string contentType, string filename)> ExportPurchasesAsync(string format)
    {
        var orders = await _purchases.GetAllAsync();
        var list = orders.ToList();
        var namaToko = await NamaTokoAsync();

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = BuildPurchasesPdf(list, namaToko);
            return (bytes, "application/pdf", $"pembelian-{DateTime.Now:yyyy-MM-dd}.pdf");
        }
        var xls = BuildPurchasesExcel(list, namaToko);
        return (xls, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"pembelian-{DateTime.Now:yyyy-MM-dd}.xlsx");
    }

    private byte[] BuildPurchasesPdf(List<PurchaseOrder> orders, string namaToko)
    {
        return Document.Create(container => container.Page(page =>
        {
            page.Margin(28);
            page.Header().Column(col =>
            {
                col.Item().Text(namaToko).FontSize(18).SemiBold();
                col.Item().Text("Laporan Pembelian").FontSize(13);
                col.Item().Text($"Tanggal: {DateTime.Now:dd/MM/yyyy}").FontSize(10);
                col.Item().LineHorizontal(1);
            });
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(100);
                    c.RelativeColumn();
                    c.ConstantColumn(90);
                    c.ConstantColumn(90);
                    c.ConstantColumn(110);
                    c.ConstantColumn(100);
                });
                table.Header(header =>
                {
                    header.Cell().Element(h => CellStyle(h)).Text("No PO");
                    header.Cell().Element(h => CellStyle(h)).Text("Supplier");
                    header.Cell().Element(h => CellStyle(h)).Text("Tgl Order");
                    header.Cell().Element(h => CellStyle(h)).Text("Expected");
                    header.Cell().Element(h => CellStyle(h)).AlignRight().Text("Total Nilai");
                    header.Cell().Element(h => CellStyle(h)).Text("Status");
                });
                foreach (var o in orders)
                {
                    long total = (o.Lines ?? new List<PurchaseOrderLine>()).Sum(l => (long)l.OrderedQty * l.UnitCostMinorUnits);
                    table.Cell().Element(c => CellStyle(c)).Text(o.PoNumber ?? $"#{o.Id}");
                    table.Cell().Element(c => CellStyle(c)).Text(o.SupplierName ?? "-");
                    table.Cell().Element(c => CellStyle(c)).Text(o.OrderDate.ToString("dd/MM/yy"));
                    table.Cell().Element(c => CellStyle(c)).Text(o.ExpectedDate?.ToString("dd/MM/yy") ?? "-");
                    table.Cell().Element(c => CellStyle(c)).AlignRight().Text(FormatRupiah(total));
                    table.Cell().Element(c => CellStyle(c)).Text(o.Status.ToString());
                }
            });
        })).GeneratePdf();
    }

    private byte[] BuildPurchasesExcel(List<PurchaseOrder> orders, string namaToko)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Pembelian");
        ws.Cell(1, 1).Value = $"{namaToko} - Laporan Pembelian";
        ws.Range(1, 1, 1, 6).Merge().Style.Font.Bold = true;
        var headers = new[] { "No PO", "Supplier", "Tgl Order", "Expected", "Total Nilai (Rp)", "Status" };
        for (int i = 0; i < headers.Length; i++)
            ws.Cell(3, i + 1).Value = headers[i];
        ws.Range(3, 1, 3, headers.Length).Style.Font.Bold = true;
        int row = 4;
        foreach (var o in orders)
        {
            long total = (o.Lines ?? new List<PurchaseOrderLine>()).Sum(l => (long)l.OrderedQty * l.UnitCostMinorUnits);
            ws.Cell(row, 1).Value = o.PoNumber ?? $"#{o.Id}";
            ws.Cell(row, 2).Value = o.SupplierName ?? "-";
            ws.Cell(row, 3).Value = o.OrderDate.ToString("dd/MM/yyyy");
            ws.Cell(row, 4).Value = o.ExpectedDate?.ToString("dd/MM/yyyy") ?? "-";
            ws.Cell(row, 5).Value = total / 100m;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
            ws.Cell(row, 6).Value = o.Status.ToString();
            row++;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Helper untuk QuestPDF styling
    // ───────────────────────────────────────────────────────────────────────

    private static IContainer CellStyle(IContainer container)
    {
        return container
            .BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(4)
            .PaddingHorizontal(3);
    }
}
