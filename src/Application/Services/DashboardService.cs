using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <param name="KasBalance">Saldo Kas &amp; Bank (1-001) pada arah normalnya, unit minor.</param>
/// <param name="Piutang">Sisa bayar seluruh pesanan aktif termasuk yang belum jatuh tempo, unit minor.</param>
/// <param name="Revenue">Kas masuk hari bisnis ini, unit minor.</param>
/// <param name="Expense">Kas keluar hari bisnis ini, unit minor.</param>
/// <param name="NetProfit">Selisih kas masuk-keluar hari ini, unit minor.</param>
public record DashboardSummary(
    long KasBalance,
    long Piutang,
    long Revenue,
    long Expense,
    long NetProfit,
    int ActiveOrders,
    List<SmartAlert> Alerts,
    List<UpcomingOrder> UpcomingOrders);

public record SmartAlert(string Type, string Message);
public record UpcomingOrder(int Id, string OrderNumber, string CustomerName, string Description, DateTime PickupDate, string Status);

public class DashboardService
{
    private const string CashAccountCode = "1-001";

    private readonly ISalesOrderRepository _orders;
    private readonly IIngredientRepository _ingredients;
    private readonly IJournalRepository _journals;

    public DashboardService(ISalesOrderRepository orders, IIngredientRepository ingredients, IJournalRepository journals)
    {
        _orders = orders;
        _ingredients = ingredients;
        _journals = journals;
    }

    public async Task<DashboardSummary> GetSummaryAsync()
    {
        var today = BusinessTime.Today();

        // Semua angka KPI dihitung di SQL. Dashboard tidak lagi memuat semua pesanan belum lunas
        // beserta pembayarannya, jadi waktu buka tetap datar walau riwayat transaksi bertahun-tahun.
        var kas = await _journals.GetAccountBalanceAsync(CashAccountCode);

        // "Hari ini" di kartu KPI berarti arus kas pada tanggal bisnis, bukan saldo sepanjang masa.
        var cashFlow = await _journals.GetCashFlowAsync(today, CashAccountCode);
        var cashIn = cashFlow.Masuk;
        var cashOut = cashFlow.Keluar;

        var outstanding = await _orders.GetOutstandingSummaryAsync();
        var piutang = outstanding.Sisa;

        var upcoming = await _orders.GetUpcomingAsync(today);

        var alerts = new List<SmartAlert>();

        // H-1 alerts
        var tomorrow = today.AddDays(1);
        foreach (var o in upcoming.Where(o => o.PickupDate.Date == tomorrow))
            alerts.Add(new SmartAlert("pickup", $"H-1 Pengambilan: {o.CustomerNameSnapshot} ({o.Description})"));

        // Stock alerts
        var lowStock = await _ingredients.GetBelowMinStockAsync();
        foreach (var i in lowStock)
            alerts.Add(new SmartAlert("stock", $"Stok Menipis: {i.Name} ({i.CurrentStock} {i.Unit})"));

        // Overdue payment alerts — tiga yang paling lama menunggu, sama seperti urutan sebelumnya.
        var overdue = await _orders.GetOutstandingOverdueAsync(today, 3);
        foreach (var o in overdue)
            alerts.Add(new SmartAlert("payment",
                $"Piutang Terlewat: {o.CustomerNameSnapshot} ({Money.Format(o.RemainingAmount())})"));

        var upcomingList = upcoming.Take(10).Select(o => new UpcomingOrder(
            o.Id, o.OrderNumber ?? $"#{o.Id}", o.CustomerNameSnapshot,
            o.Description, o.PickupDate, o.Status.ToString())).ToList();

        return new DashboardSummary(kas, piutang, cashIn, cashOut, cashIn - cashOut,
            outstanding.Jumlah, alerts, upcomingList);
    }
}
