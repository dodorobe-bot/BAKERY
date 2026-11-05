using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

public class PurchaseService
{
    private readonly IPurchaseOrderRepository _purchaseRepo;
    private readonly ILocationStockRepository _locationStock;
    private readonly AccountingService _accounting;

    public PurchaseService(
        IPurchaseOrderRepository purchaseRepo,
        ILocationStockRepository locationStock,
        AccountingService accounting)
    {
        _purchaseRepo = purchaseRepo;
        _locationStock = locationStock;
        _accounting = accounting;
    }

    public async Task<Result> ReceiveGoodsAsync(int purchaseOrderId, ReceiveGoodsDto dto)
    {
        if (dto.UserId is null)
            return Result.Fail(ErrorCodes.Unauthorized, "Penerimaan barang harus atas nama pengguna yang masuk.");
        if (dto.Lines is null || dto.Lines.Count == 0)
            return Result.Fail(ErrorCodes.ValidationFailed, "Tidak ada baris barang yang diterima.");

        var po = await _purchaseRepo.GetByIdAsync(purchaseOrderId);
        if (po is null) return Result.Fail(ErrorCodes.NotFound, "PO tidak ditemukan");
        if (po.Status == PurchaseOrderStatus.Cancelled)
            return Result.Fail(ErrorCodes.InvalidOrderStatus, "PO sudah dibatalkan");
        if (po.Status == PurchaseOrderStatus.Received)
            return Result.Fail(ErrorCodes.InvalidOrderStatus, "PO sudah diterima penuh");

        var lineError = ValidateLines(po, dto.Lines);
        if (lineError is not null)
            return Result.Fail(ErrorCodes.ValidationFailed, lineError);

        // Lokasi tujuan menentukan akun persediaan yang didebit: bahan yang langsung masuk dapur
        // bukan lagi persediaan gudang, dan lokasi yang sudah nonaktif tidak boleh menerima barang.
        var lokasiAktif = (await _locationStock.GetLocationsAsync()).ToDictionary(l => l.Id);
        foreach (var line in dto.Lines)
        {
            if (!lokasiAktif.TryGetValue(line.LocationId, out var lokasi))
                return Result.Fail(ErrorCodes.ValidationFailed,
                    "Lokasi tujuan penerimaan tidak dikenal atau sudah tidak aktif.");
            if (lokasi.Type is not ("Warehouse" or "Kitchen"))
                return Result.Fail(ErrorCodes.ValidationFailed,
                    $"Bahan baku tidak bisa diterima di {lokasi.Name}: pilih gudang atau dapur.");
        }

        var receipt = new GoodsReceipt
        {
            PurchaseOrderId = purchaseOrderId,
            ReceivedDate = BusinessTime.Now(),
            Notes = dto.Notes,
            CreatedByUserId = dto.UserId,
            Lines = dto.Lines.Select(l => new GoodsReceiptLine
            {
                PurchaseOrderLineId = l.PurchaseOrderLineId,
                IngredientId = l.IngredientId,
                ReceivedQty = l.ReceivedQty,
                UnitCostMinorUnits = l.UnitCostMinorUnits,
                LocationId = l.LocationId,
                BatchNumber = l.BatchNumber,
                ExpiryDate = l.ExpiryDate
            }).ToList()
        };

        var stockChanges = receipt.Lines
            .Select(l => new StockChange(
                l.LocationId, l.IngredientId, l.ReceivedQty, MovementType.Restock,
                "GoodsReceipt", null, dto.UserId, $"Penerimaan PO {po.PoNumber}", l.UnitCostMinorUnits))
            .ToList();

        // Nilai penerimaan dibulatkan per baris, lalu dijumlahkan: membulatkan total
        // menghasilkan selisih yang tidak cocok dengan harga di struk. Pengelompokan per jenis
        // lokasi memastikan debit masuk ke akun persediaan tempat barang benar-benar disimpan.
        var nilaiPerJenis = receipt.Lines
            .GroupBy(l => lokasiAktif[l.LocationId].Type)
            .ToDictionary(g => g.Key, g => g.Sum(LineValue));

        var journal = await _accounting.BuildPurchaseReceiptAsync(po.PoNumber, nilaiPerJenis, dto.UserId.Value);

        try
        {
            // Status PO (sebagian/penuh) ditentukan di dalam transaksi dari jumlah diterima
            // yang tersimpan, jadi tidak bisa disimpulkan dari salinan PO yang mungkin basi.
            await _purchaseRepo.ReceiveWithJournalAsync(receipt, stockChanges, journal);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(ErrorCodes.ValidationFailed, ex.Message);
        }

        return Result.Ok();
    }

    private static string? ValidateLines(PurchaseOrder po, IReadOnlyList<ReceiveGoodsLineDto> lines)
    {
        foreach (var line in lines)
        {
            if (line.ReceivedQty <= 0)
                return "Jumlah diterima harus lebih besar dari nol.";
            if (line.ReceivedQty > 100_000)
                return "Jumlah diterima melebihi batas wajar 100.000 satuan.";
            if (MoneyRules.CostError(line.UnitCostMinorUnits, "Harga per satuan") is { } costError)
                return costError;

            // Lot yang diketik harus bisa dibaca balik sebagai tanggal di daftar kedaluwarsa,
            // jadi nomor kosong dan tanggal mundur ditolak di sini, bukan di pemicu database.
            if (line.BatchNumber is { Length: > 0 } nomor && nomor.Trim().Length > 60)
                return "Nomor lot terlalu panjang (maks 60 karakter).";
            if (line.ExpiryDate is { } kedaluwarsa && kedaluwarsa.Date <= BusinessTime.Today())
                return $"Tanggal kedaluwarsa lot {line.BatchNumber} harus setelah hari ini.";
            if (line.ExpiryDate is not null && string.IsNullOrWhiteSpace(line.BatchNumber))
                return "Tanggal kedaluwarsa diisi tanpa nomor lot: beri nomor kemasan supaya lot bisa ditemukan kembali.";

            var poLine = po.Lines.FirstOrDefault(l => l.Id == line.PurchaseOrderLineId);
            if (poLine is null)
                return $"Baris PO Id {line.PurchaseOrderLineId} tidak termasuk PO {po.PoNumber}.";
            if (poLine.IngredientId != line.IngredientId)
                return $"Baris PO untuk {DisplayName(poLine)} diisi bahan lain (Id {line.IngredientId}).";

            var outstanding = poLine.OrderedQty - poLine.ReceivedQty;
            if (line.ReceivedQty > outstanding)
                return $"Jumlah {line.ReceivedQty} {poLine.Unit} melebihi sisa terima {outstanding} {poLine.Unit} " +
                       $"untuk {DisplayName(poLine)}.";
        }

        var duplicates = lines.GroupBy(l => l.PurchaseOrderLineId).FirstOrDefault(g => g.Count() > 1);
        return duplicates is null
            ? null
            : $"Baris PO Id {duplicates.Key} diterima dua kali pada satu struk; gunakan satu angka jumlah.";
    }

    private static string DisplayName(PurchaseOrderLine line) => line.IngredientName ?? $"bahan Id {line.IngredientId}";

    /// <summary>Nilai satu baris penerimaan dalam satuan minor (pembulatan per baris).</summary>
    private static long LineValue(GoodsReceiptLine line)
        => (long)Math.Round(line.ReceivedQty * line.UnitCostMinorUnits, 0, MidpointRounding.AwayFromZero);
}
