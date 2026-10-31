using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Daftar pesanan kini dipotong per halaman 100 baris, bukan lagi diam-diam berhenti di 100
/// tanpa memberi tahu layar. Jumlah seluruh baris dikirim lewat header X-Total-Count supaya
/// tombol "Muat lagi" tahu kapan berhenti, dan offset memuat halaman berikutnya tanpa tumpang
/// tindih.
/// </summary>
[Collection("UI Tests")]
public sealed class OrderPaginationTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public OrderPaginationTests(PlaywrightFixture fixture) => _fixture = fixture;

    private static object Pesanan(string nama) => new
    {
        customerName = nama,
        description = "Uji pagination pesanan",
        pickupDate = "2030-09-09",
        totalAmount = 100_000,
        downPayment = 0,
        productTemplateId = (int?)null,
        quantity = 1,
    };

    [Fact]
    public async Task LimitOffset_DanHeaderTotal_MemuatHalamanTanpaTumpangTindih()
    {
        using var owner = await AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

        var nama = $"Paginasi {Guid.NewGuid():N}"[..24];
        for (var i = 1; i <= 3; i++)
        {
            using var buat = await owner.PostAsJsonAsync("/api/orders", Pesanan($"{nama} #{i}"));
            buat.StatusCode.Should().Be(HttpStatusCode.Created, await buat.Content.ReadAsStringAsync());
        }

        using var halamanSatu = await owner.GetAsync("/api/orders?limit=2");
        halamanSatu.StatusCode.Should().Be(HttpStatusCode.OK);
        var total = int.Parse(halamanSatu.Headers.GetValues("X-Total-Count").Single());
        total.Should().BeGreaterThanOrEqualTo(3, "header memuat jumlah seluruh pesanan, bukan jumlah halaman");

        using var dokSatu = JsonDocument.Parse(await halamanSatu.Content.ReadAsStringAsync());
        var barisSatu = dokSatu.RootElement.EnumerateArray().ToList();
        barisSatu.Should().HaveCount(2, "limit membatasi jumlah baris yang dikirim");

        using var halamanDua = await owner.GetAsync("/api/orders?limit=2&offset=2");
        using var dokDua = JsonDocument.Parse(await halamanDua.Content.ReadAsStringAsync());
        var barisDua = dokDua.RootElement.EnumerateArray().ToList();

        var idSatu = barisSatu.Select(o => o.GetProperty("id").GetInt32()).ToHashSet();
        barisDua.Select(o => o.GetProperty("id").GetInt32()).Should().NotIntersectWith(idSatu,
            "offset melompati halaman pertama, bukan mengulanginya");
    }
}
