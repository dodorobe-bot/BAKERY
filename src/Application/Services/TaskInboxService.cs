using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <summary>
/// Satu pekerjaan yang menunggu tindakan pengguna. <see cref="Tab"/> adalah menu tujuan di aplikasi,
/// jadi tiap baris benar-benar bisa dikerjakan, bukan hanya dibaca.
/// </summary>
public record ItemTugas(string Jenis, string Judul, string Detail, DateTime? Tenggat, string Tab, int? TautanId);

public record KotakTugas(
    DateTime DisusunPada, int Jumlah, Dictionary<string, int> PerJenis, List<ItemTugas> Item);

/// <summary>
/// Kotak tugas disusun dari kondisi basis data saat ini — pesanan jatuh tempo, SPK yang belum
/// dibereskan, PO yang belum diterima, stok di bawah batas, dan lot yang mendekati kedaluwarsa.
/// Tidak ada angka target atau "3 tugas" yang ditulis tetap: kalau tidak ada yang menunggu,
/// isinya memang kosong.
/// </summary>
public class TaskInboxService
{
    private const int BatasTampil = 60;

    private readonly ISalesOrderRepository _orders;
    private readonly IProductionRepository _workOrders;
    private readonly IPurchaseOrderRepository _purchases;
    private readonly IIngredientRepository _ingredients;
    private readonly IBatchRepository _batches;

    public TaskInboxService(
        ISalesOrderRepository orders,
        IProductionRepository workOrders,
        IPurchaseOrderRepository purchases,
        IIngredientRepository ingredients,
        IBatchRepository batches)
    {
        _orders = orders;
        _workOrders = workOrders;
        _purchases = purchases;
        _ingredients = ingredients;
        _batches = batches;
    }

    /// <summary>
    /// Tugas yang ditampilkan mengikuti peran pemanggil: kasir hanya melihat pekerjaan
    /// penjualannya, dapur hanya produksi/persediaan/pembelian, sedangkan Owner/Admin melihat
    /// semuanya. Tanpa penyaringan ini, kotak tugas menjadi jalan pintas membaca data yang
    /// endpoint aslinya sudah ditolak — misalnya jadwal produksi atau nilai PO.
    /// </summary>
    public async Task<KotakTugas> GetAsync(UserRole role)
    {
        var bolehJual = RolePolicies.Allows(RolePolicies.Sales, role);
        var bolehProduksi = RolePolicies.Allows(RolePolicies.ProductionWrite, role);
        var bolehPersediaan = RolePolicies.Allows(RolePolicies.InventoryRead, role);
        var bolehBeli = RolePolicies.Allows(RolePolicies.PurchasesWrite, role);

        var hariIni = BusinessTime.Today();
        var item = new List<ItemTugas>();

        // Jumlah per jenis dihitung COUNT di SQL, sedangkan daftar barisnya dibatasi BatasTampil.
        // Kotak tugas karena itu tetap melaporkan angka yang tepat pada basis data besar, tanpa
        // memuat seluruh pesanan, SPK, dan PO beserta barisnya ke memori.
        var jumlah = new Dictionary<string, int>();
        void Hitung(string jenis, int n)
        {
            if (n > 0) jumlah[jenis] = jumlah.TryGetValue(jenis, out var ada) ? ada + n : n;
        }

        if (bolehJual)
        {
            var tenggat = await _orders.GetOutstandingBreakdownAsync(hariIni);
            Hitung("piutang", tenggat.Overdue);
            Hitung("pengambilan", tenggat.Upcoming);

            foreach (var o in await _orders.GetOutstandingOverdueAsync(hariIni, BatasTampil))
                item.Add(new ItemTugas(
                    "piutang", "Piutang terlewat",
                    $"{o.OrderNumber ?? $"#{o.Id}"} · {o.CustomerNameSnapshot} · sisa {Money.Format(o.RemainingAmount())}",
                    o.PickupDate.Date, "receivables", o.Id));

            foreach (var o in await _orders.GetOutstandingUpcomingAsync(hariIni, BatasTampil))
                item.Add(new ItemTugas(
                    "pengambilan", "Pesanan menunggu diselesaikan",
                    $"{o.OrderNumber ?? $"#{o.Id}"} · {o.CustomerNameSnapshot} · sisa {Money.Format(o.RemainingAmount())}",
                    o.PickupDate.Date, "receivables", o.Id));
        }

        if (bolehProduksi)
        {
            var tenggat = await _workOrders.GetOpenBreakdownAsync(hariIni);
            Hitung("produksi", tenggat.Overdue + tenggat.Upcoming);

            foreach (var wo in await _workOrders.GetOpenAsync(BatasTampil))
                item.Add(new ItemTugas(
                    "produksi",
                    wo.Status == WorkOrderStatus.Planned ? "SPK belum dimulai" : "SPK berjalan belum dibereskan",
                    $"{wo.OrderNumber} · {wo.ProductName ?? "produk"} · target {wo.TargetQuantity} unit",
                    wo.StartDate?.Date, "production_orders", wo.Id));
        }

        if (bolehBeli)
        {
            var tenggat = await _purchases.GetOpenBreakdownAsync(hariIni);
            Hitung("pembelian", tenggat.Overdue);
            Hitung("menunggu barang", tenggat.Upcoming);

            foreach (var po in await _purchases.GetOpenAsync(BatasTampil))
            {
                var lewat = po.ExpectedDate is { } tanggal && tanggal.Date < hariIni;
                item.Add(new ItemTugas(
                    lewat ? "pembelian" : "menunggu barang",
                    lewat ? "PO lewat tanggal terima" : "PO belum diterima penuh",
                    $"{po.PoNumber} · {po.SupplierName ?? "pemasok"} · status {PoStatusLabel(po.Status)}",
                    po.ExpectedDate?.Date, "purchases", po.Id));
            }
        }

        if (bolehPersediaan)
        {
            var stok = await _ingredients.GetBelowMinStockAsync();
            Hitung("stok", stok.Count);
            foreach (var i in stok)
                item.Add(new ItemTugas(
                    "stok",
                    "Stok di bawah batas",
                    $"{i.Name}: {i.CurrentStock} {i.Unit} (batas {i.MinStockAlert} {i.Unit})",
                    null, "stock_catalog", i.Id));

            var lot = await _batches.GetAsync("segera", 50);
            Hitung("kedaluwarsa", lot.Count);
            foreach (var b in lot)
                item.Add(new ItemTugas(
                    "kedaluwarsa",
                    b.DaysToExpiry < 0 ? "Lot sudah kedaluwarsa" : "Lot segera kedaluwarsa",
                    $"{b.IngredientName} · lot {b.BatchNumber} · {b.DaysToExpiry} hari lagi",
                    b.ExpiryDate.Date, "batch_expiry", b.Id));
        }

        var urut = item
            .OrderBy(i => i.Tenggat is null ? 1 : 0)
            .ThenBy(i => i.Tenggat ?? DateTime.MaxValue)
            .Take(BatasTampil)
            .ToList();

        return new KotakTugas(
            DateTime.UtcNow,
            jumlah.Values.Sum(),
            jumlah,
            urut);
    }

    private static string PoStatusLabel(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft => "draft",
        PurchaseOrderStatus.Sent => "dikirim, belum diterima",
        PurchaseOrderStatus.PartialReceived => "diterima sebagian",
        PurchaseOrderStatus.Received => "diterima penuh",
        PurchaseOrderStatus.Cancelled => "dibatalkan",
        _ => status.ToString(),
    };
}
