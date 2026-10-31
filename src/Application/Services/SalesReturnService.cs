using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

public record CreateSalesReturnRequest(
    int SalesOrderId, decimal Quantity, long Amount, string Reason,
    string? RefundMethod, int UserId, string? IdempotencyKey = null);

/// <summary>
/// Retur penjualan adalah pengeluaran uang dan pengembalian stok sekaligus. Karena itu hanya
/// pesanan yang sudah lunas yang boleh diretur (pendapatannya sudah diakui), nilainya dibatasi
/// sisa pesanan, dan jurnal + stok ditulis dalam satu transaksi bersama dokumen returnya.
/// </summary>
public class SalesReturnService
{
    private static readonly string[] MetodeSah = ["Tunai", "Transfer"];

    private readonly ISalesOrderRepository _orders;
    private readonly ISalesReturnRepository _returns;
    private readonly ILocationStockRepository _stock;
    private readonly IDocumentSequenceRepository _documents;
    private readonly AccountingService _accounting;

    public SalesReturnService(
        ISalesOrderRepository orders, ISalesReturnRepository returns,
        ILocationStockRepository stock,
        IDocumentSequenceRepository documents, AccountingService accounting)
    {
        _orders = orders;
        _returns = returns;
        _stock = stock;
        _documents = documents;
        _accounting = accounting;
    }

    public async Task<Result<int>> CreateAsync(CreateSalesReturnRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Alasan retur wajib diisi.");
        if (req.Reason.Trim().Length > 300)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Alasan retur terlalu panjang (maks 300 karakter).");
        if (req.Quantity <= 0)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Jumlah yang diretur harus lebih dari nol.");
        if (MoneyRules.AmountError(req.Amount, "Nilai retur") is { } amountError)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, amountError);

        var metode = string.IsNullOrWhiteSpace(req.RefundMethod) ? "Tunai" : req.RefundMethod.Trim();
        if (!MetodeSah.Contains(metode, StringComparer.OrdinalIgnoreCase))
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Metode pengembalian dana harus salah satu dari: {string.Join(", ", MetodeSah)}.");
        metode = MetodeSah.First(m => m.Equals(metode, StringComparison.OrdinalIgnoreCase));

        var order = await _orders.GetByIdAsync(req.SalesOrderId);
        if (order is null)
            return Result<int>.Fail(ErrorCodes.NotFound, "Pesanan tidak ditemukan.");

        // Pesanan yang belum lunas belum punya pendapatan yang bisa dikembalikan; pembatalan
        // pesanan adalah jalur yang benar untuk itu, bukan retur.
        if (order.Status != OrderStatus.Paid)
            return Result<int>.Fail(ErrorCodes.InvalidOrderStatus,
                "Retur hanya untuk pesanan yang sudah lunas. Untuk pesanan berjalan, gunakan pembatalan pesanan.");
        if (order.ProductTemplateId is null)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                "Pesanan ini tidak merujuk produk katalog, sehingga stok yang dikembalikan tidak bisa ditentukan.");

        var sudah = await _returns.GetReturnedTotalsAsync(order.Id);
        var sisaJumlah = order.Quantity - sudah.Quantity;
        var sisaNilai = order.TotalAmount - sudah.Amount;
        if (req.Quantity > sisaJumlah)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Jumlah retur melebihi sisa yang bisa diretur ({sisaJumlah:0.###} unit).");

        // Nilai uang yang dikembalikan ditentukan oleh jumlah unit yang diretur, bukan oleh
        // isian bebas: retur 1 dari 2 unit hanya boleh mengembalikan separuh nilai pesanan.
        // Retur terakhir memakai sisa nilai apa adanya supaya total retur menutup pesanan tepat.
        var nilaiSeharusnya = req.Quantity == sisaJumlah
            ? sisaNilai
            : (long)Math.Round(req.Quantity * order.TotalAmount / order.Quantity, MidpointRounding.AwayFromZero);
        if (nilaiSeharusnya <= 0 || nilaiSeharusnya > sisaNilai)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Nilai retur melebihi sisa nilai pesanan ({Money.Format(sisaNilai)}).");
        if (req.Amount != nilaiSeharusnya)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Nilai retur harus mengikuti jumlah yang diretur: {req.Quantity:0.###} unit = {Money.Format(nilaiSeharusnya)}, bukan {Money.Format(req.Amount)}.");

        // Stok kembali ke etalase dengan nilai HPP yang benar-benar dibebankan saat pesanan
        // terjual, bukan HPP resep hari ini: harga bahan bisa berubah setelah penjualan, dan
        // memakai harga baru membuat laba retur berbeda dari laba penjualannya.
        var hppPerUnit = await _stock.GetFinishedGoodsSaleUnitCostAsync(order.Id);

        var nomor = await _documents.NextNumberAsync(DocumentKind.Return, DateTime.UtcNow);
        var ret = new SalesReturn
        {
            ReturnNumber = nomor,
            SalesOrderId = order.Id,
            ProductTemplateId = order.ProductTemplateId.Value,
            Quantity = req.Quantity,
            Amount = nilaiSeharusnya,
            Reason = req.Reason.Trim(),
            RefundMethod = metode,
            ReturnedAt = DateTime.UtcNow,
            IdempotencyKey = req.IdempotencyKey,
            CreatedByUserId = req.UserId,
        };

        var journal = await _accounting.BuildSalesReturnAsync(nomor, order.CustomerNameSnapshot, nilaiSeharusnya, req.UserId);
        int displayId = await _stock.GetLocationIdByTypeAsync("Display");
        var changes = new List<FinishedGoodsChange>
        {
            new(displayId, order.ProductTemplateId.Value, req.Quantity,
                FinishedGoodsMovementType.Return, "SalesReturn", null, req.UserId,
                $"Retur {nomor} · {order.OrderNumber} ({req.Quantity:0.###} unit)", hppPerUnit)
        };

        try
        {
            var id = await _returns.CreateWithJournalAsync(ret, journal, changes);
            return Result<int>.Ok(id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<int>.Fail(ErrorCodes.ValidationFailed, ex.Message);
        }
    }
}
