using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

public record ReturnSupplierLineRequest(int GoodsReceiptLineId, decimal Quantity, int? LocationId);

public record ReturnSupplierRequest(
    int? UserId, string Reason, string? Notes,
    IReadOnlyList<ReturnSupplierLineRequest>? Lines, string? IdempotencyKey = null);

public record PaySupplierRequest(
    int? UserId, long Amount, string? Method, string? Notes, string? IdempotencyKey = null);

/// <summary>
/// Menurunkan hutang pemasok lewat dua jalur: barang yang dikembalikan (retur) dan uang yang
/// dibayarkan. Keduanya mengikat satu struk penerimaan — sama seperti kredit 2-002 lahir — dan
/// dibatasi sisa hutang struk itu, sehingga tidak ada saldo negatif yang bisa dijelaskan di neraca.
/// </summary>
public class SupplierSettlementService
{
    private static readonly string[] MetodeSah = ["Tunai", "Transfer"];

    private readonly ISupplierSettlementRepository _settlement;
    private readonly ILocationStockRepository _stock;
    private readonly IDocumentSequenceRepository _documents;
    private readonly AccountingService _accounting;

    public SupplierSettlementService(
        ISupplierSettlementRepository settlement, ILocationStockRepository stock,
        IDocumentSequenceRepository documents, AccountingService accounting)
    {
        _settlement = settlement;
        _stock = stock;
        _documents = documents;
        _accounting = accounting;
    }

    public async Task<Result<int>> ReturnAsync(int goodsReceiptId, ReturnSupplierRequest req)
    {
        if (req.UserId is null)
            return Result<int>.Fail(ErrorCodes.Unauthorized, "Retur pemasok harus atas nama pengguna yang masuk.");
        if (string.IsNullOrWhiteSpace(req.Reason))
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Alasan retur wajib diisi.");
        if (req.Reason.Trim().Length > 300)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Alasan retur terlalu panjang (maks 300 karakter).");
        if (req.Lines is null || req.Lines.Count == 0)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Tidak ada baris barang yang diretur.");

        var ganda = req.Lines.GroupBy(l => l.GoodsReceiptLineId).FirstOrDefault(g => g.Count() > 1);
        if (ganda is not null)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Baris penerimaan Id {ganda.Key} diretur dua kali pada satu dokumen; gunakan satu angka jumlah.");
        foreach (var line in req.Lines)
        {
            if (line.Quantity <= 0)
                return Result<int>.Fail(ErrorCodes.ValidationFailed, "Jumlah yang diretur harus lebih besar dari nol.");
            if (line.Quantity > 100_000)
                return Result<int>.Fail(ErrorCodes.ValidationFailed,
                    "Jumlah retur melebihi batas wajar 100.000 satuan.");
        }

        var struk = await _settlement.GetPayableAsync(goodsReceiptId);
        if (struk is null)
            return Result<int>.Fail(ErrorCodes.NotFound, "Struk penerimaan tidak ditemukan.");
        if (struk.BalanceMinorUnits <= 0)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                struk.PaidValueMinorUnits > 0
                    ? "Hutang struk ini sudah lunas; retur tidak bisa mengurangi saldo di bawah nol."
                    : "Seluruh hutang struk ini sudah diretur; tidak ada sisa yang bisa dikurangi.");

        // Lokasi menentukan akun persediaan yang dikredit dan tempat barang benar-benar diambil,
        // jadi bahan tidak boleh "keluar" dari etalase produk jadi atau lokasi yang sudah nonaktif.
        var lokasiAktif = (await _stock.GetLocationsAsync()).ToDictionary(l => l.Id);
        var baris = new List<SupplierReturnLine>();
        var nilaiPerJenis = new Dictionary<string, long>();
        foreach (var line in req.Lines)
        {
            var asal = struk.Lines.FirstOrDefault(l => l.GoodsReceiptLineId == line.GoodsReceiptLineId);
            if (asal is null)
                return Result<int>.Fail(ErrorCodes.ValidationFailed,
                    $"Baris penerimaan Id {line.GoodsReceiptLineId} tidak termasuk struk {struk.PoNumber}.");

            var sisaJumlah = asal.ReceivedQuantity - asal.ReturnedQuantity;
            if (line.Quantity > sisaJumlah)
                return Result<int>.Fail(ErrorCodes.ValidationFailed,
                    $"Jumlah retur {line.Quantity:0.###} {asal.Unit} melebihi sisa yang bisa diretur " +
                    $"({sisaJumlah:0.###} {asal.Unit}) untuk {asal.IngredientName}.");

            var lokasiId = line.LocationId ?? asal.LocationId;
            if (!lokasiAktif.TryGetValue(lokasiId, out var lokasi))
                return Result<int>.Fail(ErrorCodes.ValidationFailed,
                    "Lokasi asal retur tidak dikenal atau sudah tidak aktif.");
            if (lokasi.Type is not ("Warehouse" or "Kitchen"))
                return Result<int>.Fail(ErrorCodes.ValidationFailed,
                    $"Bahan baku tidak bisa diretur dari {lokasi.Name}: pilih gudang atau dapur.");

            var nilai = NilaiBaris(line.Quantity, asal.UnitCostMinorUnits);
            nilaiPerJenis[lokasi.Type] = nilaiPerJenis.GetValueOrDefault(lokasi.Type) + nilai;
            baris.Add(new SupplierReturnLine
            {
                GoodsReceiptLineId = asal.GoodsReceiptLineId,
                IngredientId = asal.IngredientId,
                IngredientName = asal.IngredientName,
                LocationId = lokasiId,
                Quantity = line.Quantity,
                UnitCostMinorUnits = asal.UnitCostMinorUnits,
                ValueMinorUnits = nilai
            });
        }

        var nilaiTotal = baris.Sum(l => l.ValueMinorUnits);
        if (nilaiTotal > struk.BalanceMinorUnits)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Nilai retur {Money.Format(nilaiTotal)} melebihi sisa hutang struk ini " +
                $"({Money.Format(struk.BalanceMinorUnits)}).");

        var nomor = await _documents.NextNumberAsync(DocumentKind.SupplierReturn, BusinessTime.Now());
        var ret = new SupplierReturn
        {
            ReturnNumber = nomor,
            GoodsReceiptId = goodsReceiptId,
            PurchaseOrderId = struk.PurchaseOrderId,
            Reason = req.Reason.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            // Waktu toko ditulis apa adanya dan dibaca balik sebagai waktu toko, sama seperti
            // tanggal penerimaan yang menjadi dasar struk ini.
            ReturnedAt = BusinessTime.Now(),
            IdempotencyKey = req.IdempotencyKey,
            CreatedByUserId = req.UserId,
            Lines = baris
        };

        var journal = await _accounting.BuildSupplierReturnAsync(nomor, nilaiPerJenis, req.UserId.Value);

        try
        {
            var id = await _settlement.CreateReturnWithJournalAsync(ret, journal);
            return Result<int>.Ok(id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<int>.Fail(ErrorCodes.ValidationFailed, ex.Message);
        }
    }

    public async Task<Result<int>> PayAsync(int goodsReceiptId, PaySupplierRequest req)
    {
        if (req.UserId is null)
            return Result<int>.Fail(ErrorCodes.Unauthorized, "Pembayaran harus atas nama pengguna yang masuk.");
        if (MoneyRules.AmountError(req.Amount, "Nilai pembayaran") is { } amountError)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, amountError);

        var metode = string.IsNullOrWhiteSpace(req.Method) ? "Tunai" : req.Method.Trim();
        if (!MetodeSah.Contains(metode, StringComparer.OrdinalIgnoreCase))
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Metode pembayaran harus salah satu dari: {string.Join(", ", MetodeSah)}.");
        metode = MetodeSah.First(m => m.Equals(metode, StringComparison.OrdinalIgnoreCase));

        var struk = await _settlement.GetPayableAsync(goodsReceiptId);
        if (struk is null)
            return Result<int>.Fail(ErrorCodes.NotFound, "Struk penerimaan tidak ditemukan.");
        if (struk.BalanceMinorUnits <= 0)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                "Hutang struk ini sudah lunas; tidak ada yang perlu dibayar lagi.");
        if (req.Amount > struk.BalanceMinorUnits)
            return Result<int>.Fail(ErrorCodes.ValidationFailed,
                $"Pembayaran {Money.Format(req.Amount)} melebihi sisa hutang struk ini " +
                $"({Money.Format(struk.BalanceMinorUnits)}).");

        var nomor = await _documents.NextNumberAsync(DocumentKind.SupplierPayment, BusinessTime.Now());
        var payment = new SupplierPayment
        {
            PaymentNumber = nomor,
            GoodsReceiptId = goodsReceiptId,
            AmountMinorUnits = req.Amount,
            Method = metode,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            PaidAt = BusinessTime.Now(),
            IdempotencyKey = req.IdempotencyKey,
            CreatedByUserId = req.UserId
        };

        var journal = await _accounting.BuildSupplierPaymentAsync(
            nomor, struk.SupplierName, req.Amount, req.UserId.Value);
        if (journal is null)
            return Result<int>.Fail(ErrorCodes.ValidationFailed, "Jurnal pembayaran tidak ikut dibentuk.");

        try
        {
            var id = await _settlement.CreatePaymentWithJournalAsync(payment, journal);
            return Result<int>.Ok(id);
        }
        catch (InvalidOperationException ex)
        {
            return Result<int>.Fail(ErrorCodes.ValidationFailed, ex.Message);
        }
    }

    /// <summary>Sama seperti perhitungan struk penerimaan: dibulatkan per baris, lalu dijumlahkan.</summary>
    private static long NilaiBaris(decimal quantity, long unitCostMinorUnits)
        => (long)Math.Round(quantity * unitCostMinorUnits, 0, MidpointRounding.AwayFromZero);
}
