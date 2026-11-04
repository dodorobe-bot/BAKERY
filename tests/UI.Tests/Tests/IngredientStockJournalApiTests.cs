using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Stok yang diketik di layar Bahan Baku pernah menggerakkan LocationStock tanpa satu baris pun
/// di buku besar. Di basis data nyata yang sedang dipakai, delapan bahan menumpuk Rp 5,7 juta
/// persediaan sementara saldo 1-003 justru negatif dan seluruh pemeriksaan mandiri tetap hijau —
/// jurnal yang ada semuanya seimbang, hanya nilainya tidak pernah dicatat.
///
/// Tes ini memanggil endpoint sungguhan supaya jalur tambah (stok awal) dan jalur ubah (koreksi
/// total stok) sama-sama terbukti meninggalkan jurnal.
/// </summary>
[Collection("UI Tests")]
public sealed class IngredientStockJournalApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public IngredientStockJournalApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static string NamaBaru() => $"Bahan Jurnal {Guid.NewGuid():N}"[..26];

    private static bool MilikBahan(JsonElement entri, int idBahan) =>
        entri.GetProperty("sourceDocumentType").GetString() == "IngredientMaster" &&
        entri.TryGetProperty("sourceDocumentId", out var sumber) &&
        sumber.ValueKind == JsonValueKind.Number && sumber.GetInt32() == idBahan;

    private static async Task<JsonElement> JurnalAsync(HttpClient klien)
    {
        using var parsed = JsonDocument.Parse(await klien.GetStringAsync("/api/journal?limit=400"));
        return parsed.RootElement.Clone();
    }

    /// <summary>Debit/kredit per kode akun pada seluruh jurnal IngredientMaster milik satu bahan.</summary>
    private static async Task<Dictionary<string, (long Debit, long Kredit)>> NilaiPerAkunAsync(
        HttpClient klien, int idBahan)
    {
        var dok = await JurnalAsync(klien);
        var hasil = new Dictionary<string, (long, long)>();
        foreach (var entri in dok.EnumerateArray())
        {
            if (!MilikBahan(entri, idBahan)) continue;
            foreach (var baris in entri.GetProperty("lines").EnumerateArray())
            {
                var kode = baris.GetProperty("account").GetProperty("code").GetString()!;
                var (d, k) = hasil.TryGetValue(kode, out var lama) ? lama : (0L, 0L);
                hasil[kode] = (d + baris.GetProperty("debitAmount").GetInt64(),
                               k + baris.GetProperty("creditAmount").GetInt64());
            }
        }
        return hasil;
    }

    private static async Task<int> JumlahEntriAsync(HttpClient klien, int idBahan)
    {
        var dok = await JurnalAsync(klien);
        return dok.EnumerateArray().Count(e => MilikBahan(e, idBahan));
    }

    private static async Task<Jawaban> TambahAsync(HttpClient klien, object isi)
    {
        using var respons = await klien.PostAsJsonAsync("/api/ingredients", isi);
        var teks = await respons.Content.ReadAsStringAsync();
        var id = respons.StatusCode == HttpStatusCode.Created
            ? JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32() : 0;
        return new Jawaban(respons.StatusCode, id, teks);
    }

    private static async Task<Jawaban> UbahAsync(HttpClient klien, int id, object isi)
    {
        using var respons = await klien.PutAsJsonAsync($"/api/ingredients/{id}", isi);
        return new Jawaban(respons.StatusCode, id, await respons.Content.ReadAsStringAsync());
    }

    private sealed record Jawaban(HttpStatusCode Status, int Id, string Isi);

    [Fact]
    public async Task TambahBahanDenganStokAwal_MembukukanPersediaanLawanModal()
    {
        using var owner = await OwnerAsync();

        // 10 kg × Rp 1.000 = Rp 10.000 → 1.000.000 unit minor.
        var buat = await TambahAsync(owner,
            new { name = NamaBaru(), unit = "kg", pricePerUnit = 100_000, currentStock = 10 });
        buat.Status.Should().Be(HttpStatusCode.Created, buat.Isi);

        var nilai = await NilaiPerAkunAsync(owner, buat.Id);
        nilai.Should().ContainKey("1-003", "persediaan gudang harus mencatat barang yang baru masuk");
        nilai["1-003"].Should().Be((1_000_000L, 0L));
        nilai["3-001"].Should().Be((0L, 1_000_000L),
            "stok awal adalah barang yang sudah ada, jadi pasangannya modal bukan beban");
        nilai.Should().NotContainKey("5-006",
            "mencatat stok awal sebagai selisih akan mengurangi beban dan menaikkan laba tanpa penjualan");
        (await JumlahEntriAsync(owner, buat.Id)).Should().Be(1);
    }

    [Fact]
    public async Task UbahStokBahan_MembukukanSelisihSepertiOpname()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();

        var buat = await TambahAsync(owner,
            new { name = nama, unit = "kg", pricePerUnit = 100_000, currentStock = 10 });
        buat.Status.Should().Be(HttpStatusCode.Created, buat.Isi);

        var naik = await UbahAsync(owner, buat.Id,
            new { name = nama, unit = "kg", pricePerUnit = 100_000, currentStock = 12 });
        naik.Status.Should().Be(HttpStatusCode.OK, naik.Isi);

        var nilai = await NilaiPerAkunAsync(owner, buat.Id);
        nilai["1-003"].Should().Be((1_200_000L, 0L), "stok bertambah 2 kg di atas stok awal");
        nilai["3-001"].Should().Be((0L, 1_000_000L), "koreksi berikutnya tidak boleh menambah modal");
        nilai["5-006"].Should().Be((0L, 200_000L),
            "kenaikan stok yang tidak dibeli dicatat sebagai pembebanan selisih persediaan");

        // Turunkan lagi di bawah stok awal: selisihnya berbalik arah dan tetap seimbang.
        var turun = await UbahAsync(owner, buat.Id,
            new { name = nama, unit = "kg", pricePerUnit = 100_000, currentStock = 4 });
        turun.Status.Should().Be(HttpStatusCode.OK, turun.Isi);

        var setelah = await NilaiPerAkunAsync(owner, buat.Id);
        setelah["1-003"].Should().Be((1_200_000L, 800_000L));
        setelah["5-006"].Should().Be((800_000L, 200_000L),
            "turun 8 kg dibebankan sebagai selisih, naik 2 kg tadi dikreditkan");
        setelah.Values.Sum(v => v.Debit).Should().Be(setelah.Values.Sum(v => v.Kredit),
            "setiap kaki persediaan punya penyeimbang, jadi laporan tetap seimbang");
    }

    [Fact]
    public async Task UbahStokSamaDenganYangAda_TidakMenambahJurnal()
    {
        using var owner = await OwnerAsync();
        var nama = NamaBaru();

        var buat = await TambahAsync(owner,
            new { name = nama, unit = "kg", pricePerUnit = 100_000, currentStock = 3 });
        buat.Status.Should().Be(HttpStatusCode.Created, buat.Isi);
        var sebelum = await JumlahEntriAsync(owner, buat.Id);

        var ubah = await UbahAsync(owner, buat.Id,
            new { name = nama, unit = "kg", pricePerUnit = 100_000, currentStock = 3 });
        ubah.Status.Should().Be(HttpStatusCode.OK, ubah.Isi);

        (await JumlahEntriAsync(owner, buat.Id)).Should().Be(sebelum,
            "menyimpan angka yang sama bukan perubahan stok, jadi tidak boleh melahirkan jurnal");
    }

    [Fact]
    public async Task PeriksaBasisData_MencakupPemeriksaanStokLayarBahan()
    {
        using var owner = await OwnerAsync();

        using var respons = await owner.GetAsync("/api/system/self-check");
        respons.StatusCode.Should().Be(HttpStatusCode.OK, await respons.Content.ReadAsStringAsync());
        using var dok = JsonDocument.Parse(await respons.Content.ReadAsStringAsync());
        var nama = dok.RootElement.GetProperty("checks").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()).ToList();

        nama.Should().Contain("stok layar Bahan Baku punya jurnal",
            "stok yang belum dibukukan harus terlihat di layar Periksa Basis Data");
    }
}
