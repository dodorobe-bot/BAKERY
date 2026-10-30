using System.Text.RegularExpressions;
using Seruni22Bakery.Domain.Interfaces;

/// <summary>
/// Gerbang anti-duplikat untuk semua aksi perubahan. Klien mengirim header "Idempotency-Key"
/// berisi nilai tetap untuk satu niat (satu kali buka form), bukan per klik, sehingga "Simpan"
/// yang ditekan dua kali — atau permintaan ulang setelah koneksi putus — hanya mengerjakan satu
/// aksi dan klik kedua mendapat jawaban yang sama.
///
/// Ditempatkan sebagai middleware supaya satu aturan berlaku untuk seluruh POST/PUT/PATCH API,
/// termasuk endpoint yang ditambahkan nanti.
/// </summary>
public static class Idempotensi
{
    public const string HeaderName = "Idempotency-Key";

    private static readonly Regex BentukSah = new(@"^[A-Za-z0-9._-]{8,100}$", RegexOptions.Compiled);

    // Login dan ganti sandi boleh diulang dengan kunci yang sama: keduanya tidak menulis dokumen
    // usaha, dan menolak ulang login akan mengunci pengguna dari aplikasinya sendiri.
    private static readonly string[] Dikecualikan = ["/api/auth/login", "/api/auth/change-password"];

    public static async Task TengahkanAsync(HttpContext ctx, Func<Task> lanjut)
    {
        var metode = ctx.Request.Method;
        var path = ctx.Request.Path;
        if (metode is not ("POST" or "PUT" or "PATCH")
            || !path.StartsWithSegments("/api")
            || Dikecualikan.Any(d => path.StartsWithSegments(d)))
        {
            await lanjut();
            return;
        }

        var kunci = ctx.Request.Headers[HeaderName].ToString().Trim();
        if (kunci.Length == 0)
        {
            await lanjut();
            return;
        }

        if (!BentukSah.IsMatch(kunci))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new
            {
                errorCode = Seruni22Bakery.Domain.Common.ErrorCodes.ValidationFailed,
                errorMessage = "Kunci idempotensi harus 8-100 karakter: huruf, angka, titik, garis bawah, atau tanda hubung."
            });
            return;
        }

        var repo = (IIdempotencyRepository?)ctx.RequestServices.GetService(typeof(IIdempotencyRepository));
        if (repo is null)
        {
            await lanjut();
            return;
        }

        var rute = $"{metode} {path}";
        var ada = await repo.GetAsync(kunci);
        if (ada is not null)
        {
            if (!ada.Selesai)
            {
                await BentrokAsync(ctx, "Permintaan dengan kunci yang sama masih diproses, tunggu sebentar.");
                return;
            }
            if (!string.Equals(ada.Route, rute, StringComparison.Ordinal))
            {
                await BentrokAsync(ctx, "Kunci idempotensi ini sudah dipakai untuk aksi lain.");
                return;
            }
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            // Permintaan ulang atas niat yang sama tidak boleh tercatat sebagai aksi kedua:
            // datanya tidak berubah, dan jejak audit sudah ditulis oleh permintaan pertama.
            ctx.Items[Jejak.Duplikat] = true;
            await ctx.Response.WriteAsJsonAsync(new { id = ada.ResourceId, duplikat = true });
            return;
        }

        var milikmu = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        int? userId = int.TryParse(milikmu, out var parsed) ? parsed : null;

        // Pesan dulu sebelum mengerjakan: dua permintaan yang datang berbarengan tidak boleh
        // sama-sama melihat "belum ada" lalu sama-sama menulis dokumen.
        if (!await repo.ReserveAsync(kunci, rute, userId))
        {
            await BentrokAsync(ctx, "Permintaan dengan kunci yang sama masih diproses, tunggu sebentar.");
            return;
        }

        int? resourceId = null;
        try
        {
            await lanjut();
            resourceId = AmbilIdDariLokasi(ctx);
        }
        catch
        {
            await repo.ReleaseAsync(kunci);
            throw;
        }

        // Aksi yang ditolak (400/403/404) tidak boleh memakan kunci: pengguna masih sedang
        // memperbaiki isian yang sama dan harus boleh menyimpannya lagi.
        if (ctx.Response.StatusCode >= 400)
            await repo.ReleaseAsync(kunci);
        else
            await repo.CompleteAsync(kunci, resourceId);
    }

    /// <summary>
    /// Tautan hasil (Location) adalah satu-satunya tempat id dokumen bisa dibaca tanpa
    /// menyentuh isi respons yang sudah dikirim ke klien.
    /// </summary>
    private static int? AmbilIdDariLokasi(HttpContext ctx)
    {
        var lokasi = ctx.Response.Headers.Location.ToString();
        if (lokasi.Length == 0) return null;
        var akhir = lokasi.LastIndexOf('/');
        return akhir >= 0 && int.TryParse(lokasi[(akhir + 1)..], out var id) ? id : null;
    }

    private static Task BentrokAsync(HttpContext ctx, string pesan)
    {
        ctx.Response.StatusCode = StatusCodes.Status409Conflict;
        return ctx.Response.WriteAsJsonAsync(new
        {
            errorCode = Seruni22Bakery.Domain.Common.ErrorCodes.IdempotencyConflict,
            errorMessage = pesan
        });
    }
}
