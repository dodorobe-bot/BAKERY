using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <summary>Satu hari pada grafik: uang yang dibukukan sebagai pendapatan dan pesanan yang dibuat.</summary>
public record HarianPenjualan(DateTime Tanggal, long Pendapatan, long NilaiPesanan, int JumlahPesanan);

/// <summary>Produk terlaris dalam rentang; nama sudah dibaca dari master data, bukan dari snapshot pesanan.</summary>
public record ProdukLaris(string Nama, int JumlahPesanan, decimal UnitTerjual, long Nilai);

/// <summary>Beban dikelompokkan per akun beban, supaya angkanya bisa dicocokkan ke buku besar.</summary>
public record BebanPerAkun(string Kode, string Nama, long Nominal);

/// <summary>
/// Angka analisis dibangun dari jurnal posted dan pesanan yang tidak dibatalkan. Tidak ada
/// proyeksi, target, atau "pertumbuhan %" yang dikarang: yang tampil hanya yang memang tercatat,
/// dan kalau sewaan bulan ini sepi, grafiknya memang datar.
/// </summary>
public record AnalitikPenjualan(
    DateTime Dari, DateTime Sampai,
    long TotalPendapatan, long TotalBeban, long LabaKotor,
    long TotalNilaiPesanan, int JumlahPesanan, int JumlahPesananBatal, decimal UnitTerjual,
    long RataRataNilaiPesanan,
    List<HarianPenjualan> Harian, List<ProdukLaris> Produk, List<BebanPerAkun> Beban);

/// <summary>Satu hari buku: pendapatan/beban dari jurnal, pesanan dari catatan pesanan hari itu.</summary>
public record LaporanHarian(
    DateTime Tanggal, long Pendapatan, long Beban, long LabaKotor,
    long NilaiPesanan, int JumlahPesanan, int JumlahPesananBatal,
    decimal UnitTerjual, long RataRataNilaiPesanan);

public class AnalyticsService
{
    private const string PendapatanKode = "4-001";

    private readonly IJournalRepository _journals;
    private readonly ISalesOrderRepository _orders;

    public AnalyticsService(IJournalRepository journals, ISalesOrderRepository orders)
    {
        _journals = journals;
        _orders = orders;
    }

    /// <summary>
    /// Seluruh angka disusun di SQL: jumlah per hari, total beban per akun, dan produk terlaris.
    /// Tidak ada lagi pemuatan lima ribu pesanan ke memori lalu pengelompokan O(hari × pesanan),
    /// jadi rentang 24 bulan tetap cepat walaupun riwayat bertambah.
    /// </summary>
    public async Task<AnalitikPenjualan> GetSalesTrendAsync(int days)
    {
        var sampai = BusinessTime.Today();
        var dari = sampai.AddDays(-(Math.Clamp(days, 7, 730) - 1));
        var batas = sampai.AddDays(1);

        var jurnalHarian = await _journals.GetDailyTotalsAsync(dari, batas);
        var penjualanHarian = await _orders.GetDailySalesAsync(dari, batas);
        var bebanPerAkun = await _journals.GetExpenseTotalsAsync(dari, batas);
        var produkLaris = await _orders.GetTopProductSalesAsync(dari, batas, 10);

        // Pendapatan dan beban diambil dari jurnal, bukan dari kolom nominal pesanan: jurnal adalah
        // tempat angka uang benar-benar diakui, termasuk pembalikannya saat pesanan dibatalkan.
        var pendapatanHarian = jurnalHarian.ToDictionary(h => h.Tanggal.Date, h => h.Pendapatan);
        var bebanHarian = jurnalHarian.ToDictionary(h => h.Tanggal.Date, h => h.Beban);
        var pesananHarian = penjualanHarian.ToDictionary(h => h.Tanggal.Date);

        var harian = new List<HarianPenjualan>();
        for (var tanggal = dari; tanggal <= sampai; tanggal = tanggal.AddDays(1))
        {
            var adaPesanan = pesananHarian.TryGetValue(tanggal, out var hari);
            var adaUang = pendapatanHarian.ContainsKey(tanggal) || bebanHarian.ContainsKey(tanggal) || adaPesanan;
            // Hari tanpa catatan apa pun dilewati: grafik yang penuh titik nol membuat deret panjang
            // dan pembaca tidak bisa tahu mana hari benar-benar tidak ada transaksi.
            if (!adaUang) continue;
            harian.Add(new HarianPenjualan(tanggal,
                Nilai(pendapatanHarian, tanggal), hari?.Nilai ?? 0, hari?.JumlahPesanan ?? 0));
        }

        var totalPendapatan = jurnalHarian.Sum(h => h.Pendapatan);
        var totalBeban = jurnalHarian.Sum(h => h.Beban);
        var nilaiPesanan = penjualanHarian.Sum(h => h.Nilai);
        var jumlahPesanan = penjualanHarian.Sum(h => h.JumlahPesanan);

        return new AnalitikPenjualan(
            dari, sampai,
            totalPendapatan, totalBeban, totalPendapatan - totalBeban,
            nilaiPesanan, jumlahPesanan,
            penjualanHarian.Sum(h => h.JumlahBatal),
            penjualanHarian.Sum(h => h.Kuantitas),
            jumlahPesanan > 0 ? nilaiPesanan / jumlahPesanan : 0,
            harian,
            produkLaris.Select(p => new ProdukLaris(p.Nama, p.JumlahPesanan, p.Kuantitas, p.Nilai)).ToList(),
            bebanPerAkun.Select(b => new BebanPerAkun(b.Code, b.Name, b.Total)).ToList());
    }

    /// <summary>
    /// Laporan satu hari dari jurnal posted dan pesanan yang tercatat pada hari itu. Dipakai
    /// ringkasan harian internal dan tool MCP; angkanya sama dengan layar analisis.
    /// </summary>
    public async Task<LaporanHarian> GetDailyReportAsync(DateTime tanggal)
    {
        var hari = tanggal.Date;
        var jurnal = await _journals.GetDailyTotalsAsync(hari, hari.AddDays(1));
        var penjualan = await _orders.GetDailySalesAsync(hari, hari.AddDays(1));

        long pendapatan = jurnal.Sum(h => h.Pendapatan);
        long beban = jurnal.Sum(h => h.Beban);
        var nilai = penjualan.Sum(h => h.Nilai);
        var aktif = penjualan.Sum(h => h.JumlahPesanan);
        return new LaporanHarian(
            hari, pendapatan, beban, pendapatan - beban, nilai,
            aktif, penjualan.Sum(h => h.JumlahBatal), penjualan.Sum(h => h.Kuantitas),
            aktif > 0 ? nilai / aktif : 0);
    }

    private static long Nilai(Dictionary<DateTime, long> tabel, DateTime kunci) =>
        tabel.TryGetValue(kunci, out var nilai) ? nilai : 0;
}
