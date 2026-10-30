using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Notifikasi internal diuji dari API yang benar-benar dipakai layar: transaksi nyata menulis
/// baris, penyegaran pengingat idempoten, dan status baca hanya berlaku untuk pemiliknya.
/// </summary>
[Collection("UI Tests")]
public sealed class NotificationApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public NotificationApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static async Task<int> BuatPesananAsync(HttpClient owner)
    {
        using var buat = await owner.PostAsJsonAsync("/api/orders", new
        {
            customerName = $"Pelanggan Notifikasi {Guid.NewGuid():N}"[..32],
            description = "Uji notifikasi internal",
            pickupDate = "2030-01-05T10:00:00",
            totalAmount = 7_500_000,
            downPayment = 0,
            quantity = 1,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        return dok.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<JsonDocument> KotakAsync(HttpClient owner, int limit = 200) =>
        JsonDocument.Parse(await owner.GetStringAsync($"/api/notifications?limit={limit}"));

    [Fact]
    public async Task TanpaSesi_Ditolak()
    {
        using var anonim = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl) };

        using var kotak = await anonim.GetAsync("/api/notifications");
        using var ringkas = await anonim.GetAsync("/api/notifications/summary");
        using var segarkan = await anonim.PostAsync("/api/notifications/sync", null);

        kotak.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ringkas.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        segarkan.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TransaksiNyata_MenulisNotifikasi_DanStatusBacaBisaDitelusuri()
    {
        using var owner = await OwnerAsync();
        var pesanan = await BuatPesananAsync(owner);

        using var kotak = await KotakAsync(owner);
        var item = kotak.RootElement.GetProperty("item").EnumerateArray().ToList();
        var cocok = item.Where(n =>
            n.GetProperty("eventType").GetString() == "transaction.order.created" &&
            n.GetProperty("entityId").GetInt32() == pesanan).ToList();
        cocok.Should().ContainSingle();
        var baris = cocok[0];
        var id = baris.GetProperty("id").GetInt32();
        baris.GetProperty("severity").GetString().Should().Be("Info",
            "severity dikirim sebagai teks supaya layar tidak menebak urutan angka enum");
        baris.GetProperty("isRead").GetBoolean().Should().BeFalse();
        baris.GetProperty("body").GetString().Should().Contain("75.000");

        var ringkas = kotak.RootElement.GetProperty("ringkasan");
        var belumDibaca = item.Count(n => !n.GetProperty("isRead").GetBoolean());
        ringkas.GetProperty("total").GetInt32().Should().Be(item.Count);
        ringkas.GetProperty("unread").GetInt32().Should().Be(belumDibaca);

        (await owner.PostAsync($"/api/notifications/{id}/read", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        using var sesudah = await KotakAsync(owner);
        var dibaca = sesudah.RootElement.GetProperty("item").EnumerateArray()
            .Single(n => n.GetProperty("id").GetInt32() == id);
        dibaca.GetProperty("isRead").GetBoolean().Should().BeTrue();
        dibaca.GetProperty("readAt").GetString().Should().NotBeNullOrWhiteSpace();

        (await owner.PostAsync("/api/notifications/read-all", null)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        using var semua = JsonDocument.Parse(await owner.GetStringAsync("/api/notifications/summary"));
        semua.RootElement.GetProperty("unread").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task PenyegaranPengingat_BerulangTidakMenggandakanPengingat()
    {
        using var owner = await OwnerAsync();
        // Bahan baku tanpa stok dan dengan batas minimum: memicu pengingat stok menipis.
        using var bahan = await owner.PostAsJsonAsync("/api/ingredients", new
        {
            name = $"Tepung Uji {Guid.NewGuid():N}"[..20],
            unit = "kg",
            pricePerUnit = 12000,
            minStockAlert = 5,
        });
        bahan.StatusCode.Should().Be(HttpStatusCode.Created, await bahan.Content.ReadAsStringAsync());

        using var pertama = await owner.PostAsync("/api/notifications/sync", null);
        pertama.StatusCode.Should().Be(HttpStatusCode.OK);
        var ditulisPertama = JsonDocument.Parse(await pertama.Content.ReadAsStringAsync())
            .RootElement.GetProperty("ditulis").GetInt32();
        ditulisPertama.Should().BeGreaterThan(0, "stok di bawah batas harus menghasilkan pengingat");

        using var kedua = await owner.PostAsync("/api/notifications/sync", null);
        var ditulisKedua = JsonDocument.Parse(await kedua.Content.ReadAsStringAsync())
            .RootElement.GetProperty("ditulis").GetInt32();
        ditulisKedua.Should().Be(0, "kunci dedupe memuat tanggal bisnis dan penerima");

        using var kotak = await KotakAsync(owner);
        var pengingatStok = kotak.RootElement.GetProperty("item").EnumerateArray()
            .Count(n => n.GetProperty("eventType").GetString() == "stock.low" &&
                        n.GetProperty("title").GetString()!.Contains("Tepung Uji"));
        pengingatStok.Should().Be(1);
    }
}
