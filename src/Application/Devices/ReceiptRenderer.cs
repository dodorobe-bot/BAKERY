using System.Globalization;
using System.Text;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Application.Devices;

/// <summary>
/// Isi struk dirender di server dari pesanan yang sudah tersimpan, bukan dari angka yang
/// dikirim layar. Yang tercetak karenanya selalu sama dengan yang tercatat di basis data.
/// Lebar 58 mm memakai 32 karakter dan 80 mm memakai 48 karakter (font A printer thermal).
/// </summary>
public static class ReceiptRenderer
{
    private static readonly string[] NamaBulan =
    [
        "Januari", "Februari", "Maret", "April", "Mei", "Juni",
        "Juli", "Agustus", "September", "Oktober", "November", "Desember"
    ];

    public static int Kolom(int paperWidthMm) => paperWidthMm == 58 ? 32 : 48;

    public static string Render(
        SalesOrder order,
        string? outletName,
        int paperWidthMm,
        bool cetakUlang = false,
        string? brandName = null,
        string? brandTagline = null)
    {
        var lebar = Kolom(paperWidthMm);
        var sb = new StringBuilder();

        void Baris(string teks = "") => sb.AppendLine(teks);
        void Tengah(string teks)
        {
            foreach (var potong in Bungkus(teks, lebar))
                Baris(new string(' ', Math.Max(0, (lebar - potong.Length) / 2)) + potong);
        }
        void Pasangan(string label, string nilai)
        {
            var spasi = lebar - label.Length - nilai.Length;
            if (spasi >= 1) Baris(label + new string(' ', spasi) + nilai);
            else
            {
                Baris(label);
                foreach (var potong in Bungkus(nilai, lebar)) Baris(potong.PadLeft(lebar));
            }
        }
        void Garis() => Baris(new string('-', lebar));

        // Kepala struk mengikuti nama dan slogan toko di Pengaturan, bukan teks tetap di kode:
        // pemilik yang mengganti merek tidak perlu membangun ulang aplikasi. Nama tempat kerja
        // tetap dicetak sebagai baris ketiga bila berbeda dari nama toko.
        var namaToko = StoreBrand.Name(brandName);
        Tengah(namaToko);
        Tengah(StoreBrand.Tagline(brandTagline));
        if (!string.IsNullOrWhiteSpace(outletName) && !string.Equals(outletName.Trim(), namaToko, StringComparison.OrdinalIgnoreCase))
            Tengah(outletName.Trim());
        if (cetakUlang)
        {
            Garis();
            Tengah("*** CETAK ULANG ***");
        }
        Garis();
        Pasangan("No. Nota", order.OrderNumber ?? "-");
        Pasangan("Tgl. Cetak", FormatWaktu(DateTime.UtcNow));
        Pasangan("Tgl. Pesanan", FormatWaktu(order.CreatedAt));
        Pasangan("Tgl. Pickup", FormatTanggal(order.PickupDate));
        Garis();
        Pasangan("Pelanggan", string.IsNullOrWhiteSpace(order.CustomerNameSnapshot) ? "-" : order.CustomerNameSnapshot);
        if (!string.IsNullOrWhiteSpace(order.PhoneSnapshot)) Pasangan("Telepon", order.PhoneSnapshot);
        Garis();

        Baris("Pesanan:");
        foreach (var potong in Bungkus(string.IsNullOrWhiteSpace(order.Description) ? "-" : order.Description, lebar))
            Baris(potong);

        var jumlah = order.Quantity <= 0 ? 1 : order.Quantity;
        var satuan = (long)Math.Round(order.TotalAmount / jumlah, MidpointRounding.AwayFromZero);
        Pasangan($"{FormatJumlah(jumlah)} x {Money.Format(satuan)}", Money.Format(order.TotalAmount));

        Garis();
        Pasangan("Total", Money.Format(order.TotalAmount));
        if (order.DownPayment > 0) Pasangan("DP / Uang Muka", Money.Format(order.DownPayment));
        var dibayar = order.Payments.Sum(p => p.Amount);
        if (dibayar > 0) Pasangan("Pembayaran", Money.Format(dibayar));
        Pasangan("Sisa Tagihan", Money.Format(Math.Max(0, order.RemainingAmount())));
        if (order.IsFullyPaid()) Tengah("LUNAS");
        Garis();
        Tengah("Terima kasih atas pesanan Anda!");
        Tengah("Simpan struk ini sebagai bukti transaksi.");
        return sb.ToString();
    }

    public static string RenderTest(
        string? outletName,
        string printerName,
        int paperWidthMm,
        DateTime waktu,
        string? brandName = null,
        string? brandTagline = null)
    {
        var lebar = Kolom(paperWidthMm);
        var sb = new StringBuilder();
        void Baris(string teks = "") => sb.AppendLine(teks);
        void Tengah(string teks)
        {
            foreach (var potong in Bungkus(teks, lebar))
                Baris(new string(' ', Math.Max(0, (lebar - potong.Length) / 2)) + potong);
        }
        void Pasangan(string label, string nilai)
        {
            var spasi = lebar - label.Length - nilai.Length;
            if (spasi >= 1) Baris(label + new string(' ', spasi) + nilai);
            else
            {
                Baris(label);
                Baris(nilai.PadLeft(lebar));
            }
        }

        var namaToko = StoreBrand.Name(brandName);
        Tengah(namaToko);
        Tengah(StoreBrand.Tagline(brandTagline));
        if (!string.IsNullOrWhiteSpace(outletName) && !string.Equals(outletName.Trim(), namaToko, StringComparison.OrdinalIgnoreCase))
            Tengah(outletName.Trim());
        Tengah("UJI CETAK PERANGKAT");
        Baris(new string('=', lebar));
        Pasangan("Printer", printerName);
        Pasangan("Lebar kertas", $"{paperWidthMm} mm / {lebar} kolom");
        Pasangan("Waktu", FormatWaktu(waktu));
        Baris(new string('-', lebar));
        Baris("Contoh baris transaksi:");
        Pasangan("2 x Rp 15.000", Money.Format(3_000_000));
        Baris(new string('-', lebar));
        foreach (var potong in Bungkus(
            "Jika tulisan ini terbaca utuh tanpa terpotong, lebar kertas sudah sesuai dengan printer.",
            lebar))
            Baris(potong);
        Baris();
        Tengah("SELESAI");
        return sb.ToString();
    }

    private static string FormatJumlah(decimal jumlah) =>
        jumlah % 1 == 0
            ? ((long)jumlah).ToString(CultureInfo.InvariantCulture)
            : jumlah.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatWaktu(DateTime waktu)
    {
        var lokal = waktu.Kind == DateTimeKind.Utc
            ? TimeZoneInfo.ConvertTimeFromUtc(waktu, BusinessTime.Zone)
            : waktu;
        return $"{lokal:dd} {NamaBulan[lokal.Month - 1]} {lokal:yyyy} {lokal:HH:mm}";
    }

    private static string FormatTanggal(DateTime tanggal) =>
        $"{tanggal:dd} {NamaBulan[tanggal.Month - 1]} {tanggal:yyyy}";

    /// <summary>Membungkus teks pada batas kata; kata yang lebih panjang dari kolom dipotong paksa.</summary>
    internal static IReadOnlyList<string> Bungkus(string teks, int lebar)
    {
        var hasil = new List<string>();
        foreach (var paragraf in teks.Replace("\r", "").Split('\n'))
        {
            var baris = new StringBuilder();
            foreach (var kata in paragraf.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (baris.Length > 0 && baris.Length + 1 + kata.Length > lebar)
                {
                    hasil.Add(baris.ToString());
                    baris.Clear();
                }
                var sisaKata = kata;
                while (sisaKata.Length > lebar)
                {
                    if (baris.Length > 0) { hasil.Add(baris.ToString()); baris.Clear(); }
                    hasil.Add(sisaKata[..lebar]);
                    sisaKata = sisaKata[lebar..];
                }
                if (sisaKata.Length == 0) continue;
                if (baris.Length > 0) baris.Append(' ');
                baris.Append(sisaKata);
            }
            hasil.Add(baris.Length > 0 ? baris.ToString() : string.Empty);
        }
        return hasil;
    }
}
