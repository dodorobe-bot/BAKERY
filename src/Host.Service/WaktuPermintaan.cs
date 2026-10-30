using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

/// <summary>
/// Waktu proses permintaan di sisi server. Angkanya dikirim lewat header <c>Server-Timing:
/// app;dur=…</c> supaya lama di server bisa dipisahkan dari lama di peramban saat mengukur
/// perpindahan menu, dan <c>X-Request-Id</c> yang dikirim peramban dikembalikan apa adanya
/// agar satu permintaan bisa ditelusuri dari klik sampai query basis data.
///
/// Nilainya diukur sampai respons mulai dikirim (byte pertama), karena setelah itu header tidak
/// boleh disusun lagi. Untuk jawaban JSON kecil selisihnya dengan waktu total tidak berarti;
/// waktu total tetap dicatat di log untuk permintaan yang lambat.
///
/// Yang masuk log hanya metode, jalur tanpa query, dan kode status: kata sandi, nama pengguna,
/// dan kata kunci pencarian tidak boleh ikut tercatat.
/// </summary>
public static class WaktuPermintaan
{
    private const double AmbangLambatMs = 500;

    public static async Task TengahkanAsync(HttpContext ctx, RequestDelegate lanjut)
    {
        var jalur = ctx.Request.Path;
        // Berkas statis tidak perlu diukur: yang dicari adalah lama jawaban API, dan setiap
        // aset halaman akan membanjiri log dengan angka yang tidak dibandingkan siapa pun.
        if (!jalur.StartsWithSegments("/api"))
        {
            await lanjut(ctx);
            return;
        }

        var id = ctx.Request.Headers["X-Request-Id"].ToString();
        if (id.Length > 0) ctx.Response.Headers["X-Request-Id"] = id;

        var jam = Stopwatch.StartNew();
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers["Server-Timing"] =
                $"app;dur={jam.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}";
            return Task.CompletedTask;
        });

        await lanjut(ctx);

        jam.Stop();
        var ms = jam.Elapsed.TotalMilliseconds;
        if (ms < AmbangLambatMs) return;

        var logger = (ctx.RequestServices.GetService(typeof(ILoggerFactory)) as ILoggerFactory)
            ?.CreateLogger("WaktuPermintaan");
        logger?.LogWarning(
            "[Lambat] {Metode} {Jalur} {Status} {Ms:0} ms (id {Id})",
            ctx.Request.Method, jalur.Value, ctx.Response.StatusCode, ms,
            id.Length > 0 ? id : "-");
    }
}
