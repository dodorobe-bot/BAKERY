using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Pagar peran diuji dari sisi backend, bukan dari tampilan: akun kasir dibuat sungguhan, lalu
/// memanggil endpoint yang menu-nya tidak ia lihat. Kalau hanya menunya yang disembunyikan,
/// pengujian ini gagal — dan itu memang tujuannya.
/// </summary>
[Collection("UI Tests")]
public sealed class RbacApiTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public RbacApiTests(PlaywrightFixture fixture) => _fixture = fixture;

    private const string SandiAwal = "SandiSementara1";
    private const string SandiBaru = "SandiHarianKasir1";

    private Task<HttpClient> OwnerAsync() => AppLogin.AuthorizedClientAsync(_fixture.BaseUrl);

    private static HttpClient Klien(HttpClient induk, string token)
    {
        var http = new HttpClient { BaseAddress = induk.BaseAddress!, Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    /// <summary>Membuat akun lewat API produksi lalu melewati gerbang sandi bawaan seperti pegawai sungguhan.</summary>
    private static async Task<(int Id, string Token)> BuatPenggunaAsync(HttpClient owner, string role)
    {
        var username = $"uji{role.ToLowerInvariant()}{Guid.NewGuid():N}"[..24];
        using var buat = await owner.PostAsJsonAsync("/api/users", new { username, password = SandiAwal, role });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        using var dok = JsonDocument.Parse(teks);
        var id = dok.RootElement.GetProperty("id").GetInt32();

        var (tokenBawaan, wajibGanti) = await LoginAsync(owner, username, SandiAwal);
        wajibGanti.Should().BeTrue("akun baru selalu wajib mengganti sandi lebih dulu");

        using (var ganti = Klien(owner, tokenBawaan))
        {
            var resp = await ganti.PostAsJsonAsync("/api/auth/change-password",
                new { currentPassword = SandiAwal, newPassword = SandiBaru });
            var isi = await resp.Content.ReadAsStringAsync();
            resp.StatusCode.Should().Be(HttpStatusCode.OK, isi);
        }

        var (token, wajibGantiLagi) = await LoginAsync(owner, username, SandiBaru);
        wajibGantiLagi.Should().BeFalse();
        return (id, token);
    }

    private static async Task<(string Token, bool WajibGanti)> LoginAsync(HttpClient induk, string username, string password)
    {
        using var http = new HttpClient { BaseAddress = induk.BaseAddress! };
        var resp = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
        var teks = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, teks);
        using var dok = JsonDocument.Parse(teks);
        return (dok.RootElement.GetProperty("token").GetString()!,
            dok.RootElement.GetProperty("mustChangePassword").GetBoolean());
    }

    private static async Task<int> BuatProdukAsync(HttpClient owner, string nama, long hargaMinor)
    {
        using var buat = await owner.PostAsJsonAsync("/api/products", new
        {
            name = nama,
            category = "Uji RBAC",
            sellingPriceMinorUnits = hargaMinor,
            lines = Array.Empty<object>(),
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.IsSuccessStatusCode.Should().BeTrue(teks);

        using var dok = JsonDocument.Parse(await owner.GetStringAsync("/api/products"));
        return dok.RootElement.EnumerateArray()
            .First(p => p.GetProperty("name").GetString() == nama)
            .GetProperty("id").GetInt32();
    }

    /// <summary>Menambah stok etalase lewat SPK sungguhan: pelunasan memotong stok produk jadi.</summary>
    private static async Task SiapkanStokEtalaseAsync(HttpClient owner, int produkId, int unit)
    {
        using var buat = await owner.PostAsJsonAsync("/api/production/orders", new
        {
            productTemplateId = produkId,
            targetQuantity = unit,
            laborCost = 0,
            overheadCost = 0,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        var id = JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();

        using var selesai = await owner.PostAsJsonAsync($"/api/production/orders/{id}/complete", new
        {
            actualGoodQty = unit,
            actualFailedQty = 0,
            materials = Array.Empty<object>(),
            scraps = Array.Empty<object>(),
        });
        selesai.StatusCode.Should().Be(HttpStatusCode.OK, await selesai.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Kasir_HanyaBisaMembacaDataPenjualan()
    {
        var owner = await OwnerAsync();
        var (_, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        string[] terlarang =
        [
            "/api/dashboard", "/api/analytics/sales",
            "/api/purchases", "/api/suppliers",
            "/api/ingredients", "/api/inventory/locations", "/api/inventory/movements",
            "/api/inventory/finished-goods", "/api/inventory/batches",
            "/api/production/orders", "/api/journal", "/api/journal/balances",
            "/api/users", "/api/audit", "/api/system/self-check",
            "/api/export/orders", "/api/export/stock", "/api/export/purchases",
        ];
        foreach (var rute in terlarang)
        {
            var resp = await kasir.GetAsync(rute);
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"kasir tidak boleh membaca {rute}");
        }

        string[] boleh =
        [
            "/api/orders", "/api/customers", "/api/products", "/api/tasks",
        ];
        foreach (var rute in boleh)
        {
            var resp = await kasir.GetAsync(rute);
            resp.StatusCode.Should().Be(HttpStatusCode.OK, $"kasir membutuhkan {rute}");
        }

        using var tugas = JsonDocument.Parse(await kasir.GetStringAsync("/api/tasks"));
        var jenis = tugas.RootElement.GetProperty("item").EnumerateArray()
            .Select(i => i.GetProperty("jenis").GetString())
            .ToHashSet();
        jenis.Should().BeSubsetOf(["pengambilan", "piutang"],
            "kotak tugas kasir tidak boleh membocorkan produksi, pembelian, atau stok");
    }

    [Fact]
    public async Task Kasir_TidakBisaMenembakEndpointTulisTerlarang()
    {
        var owner = await OwnerAsync();
        var (idKasir, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        var produkId = await BuatProdukAsync(owner, $"Roti Uji {Guid.NewGuid():N}"[..26], 1_500_000);
        var hargaSemula = await HargaProdukAsync(owner, produkId);

        (HttpMethod metode, string rute, object? isi)[] percobaan =
        [
            (HttpMethod.Put, $"/api/products/{produkId}", new { name = "Diubah Kasir", sellingPriceMinorUnits = 100L, lines = Array.Empty<object>() }),
            (HttpMethod.Post, "/api/inventory/adjust", new { locationId = 1, ingredientId = 1, newQuantity = 0, reason = "uji" }),
            (HttpMethod.Post, "/api/purchases", new { supplierId = 1, lines = Array.Empty<object>() }),
            (HttpMethod.Post, "/api/suppliers", new { name = "Pemasok Uji" }),
            (HttpMethod.Post, "/api/production/orders", new { productTemplateId = produkId, targetQuantity = 1, laborCost = 0, overheadCost = 0 }),
            (HttpMethod.Post, "/api/expenses", new { category = "Uji", description = "Uji", amount = 1000L }),
            (HttpMethod.Put, $"/api/users/{idKasir}", new { role = "Owner", isActive = true }),
        ];

        foreach (var (metode, rute, isi) in percobaan)
        {
            using var permintaan = new HttpRequestMessage(metode, rute)
            {
                Content = isi is null ? null : JsonContent.Create(isi),
            };
            var resp = await kasir.SendAsync(permintaan);
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"kasir tidak boleh berhasil di {metode} {rute}");
        }

        (await HargaProdukAsync(owner, produkId)).Should().Be(hargaSemula,
            "permintaan yang ditolak tidak boleh mengubah harga di basis data");
    }

    [Fact]
    public async Task Kasir_TidakBisaMenawarHargaSendiri()
    {
        var owner = await OwnerAsync();
        var (_, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        var harga = 1_500_000L;   // Rp15.000 dalam unit minor
        var produkId = await BuatProdukAsync(owner, $"Kue Uji {Guid.NewGuid():N}"[..26], harga);
        var nama = $"Pelanggan {Guid.NewGuid():N}"[..24];

        object Pesanan(long total, decimal qty, int? produk) => new
        {
            customerName = nama,
            phone = (string?)null,
            description = "Uji harga katalog",
            pickupDate = "2030-04-04T09:00:00",
            totalAmount = total,
            downPayment = 0,
            productTemplateId = produk,
            quantity = qty,
        };

        var tanpaProduk = await kasir.PostAsJsonAsync("/api/orders", Pesanan(harga, 1, null));
        tanpaProduk.StatusCode.Should().Be(HttpStatusCode.BadRequest, "kasir wajib memilih produk dari katalog");

        var hargaMurah = await kasir.PostAsJsonAsync("/api/orders", Pesanan(100_000, 1, produkId));
        hargaMurah.StatusCode.Should().Be(HttpStatusCode.BadRequest, "total di bawah harga katalog harus ditolak");

        var jumlahNegatif = await kasir.PostAsJsonAsync("/api/orders", Pesanan(harga, -1, produkId));
        jumlahNegatif.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var sah = await kasir.PostAsJsonAsync("/api/orders", Pesanan(harga * 2, 2, produkId));
        sah.StatusCode.Should().Be(HttpStatusCode.Created, await sah.Content.ReadAsStringAsync());

        // Pemilik tetap boleh menetapkan harga khusus (mis. pesanan katering nego).
        using var pemilik = Klien(owner, await LoginTokenAsync(owner, "admin"));
        var khusus = await pemilik.PostAsJsonAsync("/api/orders", new
        {
            customerName = $"Katering {Guid.NewGuid():N}"[..24],
            description = "Harga khusus pemilik",
            pickupDate = "2030-04-04T09:00:00",
            totalAmount = 7_777_00,
            downPayment = 0,
        });
        khusus.StatusCode.Should().Be(HttpStatusCode.Created, await khusus.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Kasir_TidakBisaMenandaiLunasTanpaPembayaran()
    {
        var owner = await OwnerAsync();
        var (_, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        // Pesanan dibuat kasir itu sendiri: endpoint status dan pembayaran hanya melayani
        // pesanan milik kasir, jadi pesanan buatan pemilik akan dijawab 404 sebelum pagar
        // pelunasan sempat diuji. Kasir wajib memilih produk katalog, jadi produk uji dibuat
        // lebih dulu dan stok etalase tidak dipakai sama sekali.
        var harga = 2_000_000L; // Rp20.000
        var produk = await BuatProdukAsync(owner, $"Roti Uji {Guid.NewGuid():N}"[..26], harga);
        await SiapkanStokEtalaseAsync(owner, produk, 1);
        var buat = await kasir.PostAsJsonAsync("/api/orders", new
        {
            customerName = $"Pelanggan {Guid.NewGuid():N}"[..24],
            description = "Uji pelunasan",
            pickupDate = "2030-05-05T09:00:00",
            totalAmount = harga,
            downPayment = 0,
            productTemplateId = produk,
            quantity = 1,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        var id = JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();

        var lunasPalsu = await kasir.PutAsJsonAsync($"/api/orders/{id}/status", new { status = "Paid" });
        lunasPalsu.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "status Lunas tanpa uang masuk harus ditolak");

        var bayarSebagian = await kasir.PostAsJsonAsync($"/api/orders/{id}/payment",
            new { amount = harga / 2, method = "Tunai", notes = "Uji DP" });
        bayarSebagian.StatusCode.Should().Be(HttpStatusCode.OK, await bayarSebagian.Content.ReadAsStringAsync());

        var masihPending = await kasir.PutAsJsonAsync($"/api/orders/{id}/status", new { status = "Paid" });
        masihPending.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "sisa piutang masih ada, jadi status Lunas tetap tidak sah");

        var pelunasan = await kasir.PostAsJsonAsync($"/api/orders/{id}/payment",
            new { amount = harga / 2, method = "Tunai", notes = "Uji pelunasan" });
        pelunasan.StatusCode.Should().Be(HttpStatusCode.OK, await pelunasan.Content.ReadAsStringAsync());

        using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/orders/{id}"));
        dok.RootElement.GetProperty("status").GetInt32().Should().Be(3, "OrderStatus.Paid");

        var bayarDuaKali = await kasir.PostAsJsonAsync($"/api/orders/{id}/payment",
            new { amount = harga, method = "Tunai" });
        bayarDuaKali.StatusCode.Should().Be(HttpStatusCode.BadRequest, "pesanan lunas tidak menerima pembayaran kedua");

        var batal = await kasir.PutAsJsonAsync($"/api/orders/{id}/status",
            new { status = "Cancelled", cancelReason = "Uji" });
        batal.StatusCode.Should().Be(HttpStatusCode.Forbidden, "pembatalan hanya untuk Owner/Admin");
    }

    [Fact]
    public async Task Kasir_TidakBisaMengubahPesananKasirLain()
    {
        var owner = await OwnerAsync();
        var (_, tokenA) = await BuatPenggunaAsync(owner, "Pegawai");
        var (_, tokenB) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasirA = Klien(owner, tokenA);
        using var kasirB = Klien(owner, tokenB);

        var harga = 1_000_000L;
        var produk = await BuatProdukAsync(owner, $"Donat Uji {Guid.NewGuid():N}"[..26], harga);
        var buat = await kasirA.PostAsJsonAsync("/api/orders", new
        {
            customerName = $"Pelanggan {Guid.NewGuid():N}"[..24],
            description = "Uji cakupan tulis",
            pickupDate = "2030-06-06T09:00:00",
            totalAmount = harga,
            downPayment = 0,
            productTemplateId = produk,
            quantity = 1,
        });
        var teks = await buat.Content.ReadAsStringAsync();
        buat.StatusCode.Should().Be(HttpStatusCode.Created, teks);
        var id = JsonDocument.Parse(teks).RootElement.GetProperty("id").GetInt32();

        // Pembayaran dan ubah status harus memakai pagar cakupan yang sama dengan GET: 404,
        // bukan 403, supaya keberadaan pesanan kasir lain pun tidak bocor.
        var bayar = await kasirB.PostAsJsonAsync($"/api/orders/{id}/payment",
            new { amount = 100_000, method = "Tunai" });
        bayar.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "kasir lain tidak boleh menyentuh pembayaran pesanan ini");

        var ubahStatus = await kasirB.PutAsJsonAsync($"/api/orders/{id}/status", new { status = "Ready" });
        ubahStatus.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "kasir lain tidak boleh mengubah status pesanan ini");

        // Pembatalan diperiksa perannya lebih dulu: penolakannya 403 untuk pesanan siapa pun,
        // bukan 404 yang membuat jawaban bergantung pada siapa pemilik pesanan.
        var batal = await kasirB.PutAsJsonAsync($"/api/orders/{id}/status",
            new { status = "Cancelled", cancelReason = "Uji cakupan" });
        batal.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "pegawai tidak pernah boleh membatalkan pesanan");

        using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/orders/{id}"));
        dok.RootElement.GetProperty("status").GetInt32().Should().Be(0, "OrderStatus.Pending");
    }

    [Fact]
    public async Task TokenLama_KehilanganHakSaatPeranTurunAtauAkunNonaktif()
    {
        var owner = await OwnerAsync();
        var pemilikToken = await LoginTokenAsync(owner, "admin");

        var (idTurun, tokenAdmin) = await BuatPenggunaAsync(owner, "Admin");
        using var klienTurun = Klien(owner, tokenAdmin);
        (await klienTurun.GetAsync("/api/journal")).StatusCode.Should().Be(HttpStatusCode.OK,
            "Admin operasional memang boleh membaca buku besar");

        using var pemilik = Klien(owner, pemilikToken);
        var turunkan = await pemilik.PutAsJsonAsync($"/api/users/{idTurun}", new { role = "Pegawai", isActive = true });
        turunkan.StatusCode.Should().Be(HttpStatusCode.OK, await turunkan.Content.ReadAsStringAsync());

        (await klienTurun.GetAsync("/api/journal")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "hak keuangan harus hilang seketika, bukan menunggu token kedaluwarsa");
        var produkId = await BuatProdukAsync(owner, $"Bolu Uji {Guid.NewGuid():N}"[..26], 500_000);
        var ubahHarga = await klienTurun.PutAsJsonAsync($"/api/products/{produkId}",
            new { name = "Bolu Diubah", sellingPriceMinorUnits = 100L, lines = Array.Empty<object>() });
        ubahHarga.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (idNonaktif, tokenKasir) = await BuatPenggunaAsync(owner, "Pegawai");
        using var klienNonaktif = Klien(owner, tokenKasir);
        (await klienNonaktif.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.OK);

        var nonaktifkan = await pemilik.PutAsJsonAsync($"/api/users/{idNonaktif}", new { role = "Pegawai", isActive = false });
        nonaktifkan.StatusCode.Should().Be(HttpStatusCode.OK, await nonaktifkan.Content.ReadAsStringAsync());

        (await klienNonaktif.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "token milik akun nonaktif tidak boleh dipakai lagi");
    }

    [Fact]
    public async Task Penolakan_TercatatDiJejakAudit()
    {
        var owner = await OwnerAsync();
        var (idKasir, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        (await kasir.GetAsync("/api/purchases")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/audit?userId={idKasir}&limit=50"));
        var catatan = dok.RootElement.EnumerateArray()
            .Where(a => a.GetProperty("entityType").GetString() == "Akses")
            .Select(a => a.GetProperty("action").GetString() ?? "")
            .ToList();

        catatan.Should().Contain(a => a.Contains("DITOLAK") && a.Contains("/api/purchases"),
            "percobaan akses terlarang harus meninggalkan jejak yang bisa dibaca pemilik");
    }

    [Fact]
    public async Task DaftarPengguna_DikirimSebagaiDtoTanpaHashSandi()
    {
        var owner = await OwnerAsync();

        var teks = await owner.GetStringAsync("/api/users");
        teks.Should().NotContainEquivalentOf("passwordHash",
            "hash sandi tidak pernah boleh keluar lewat JSON, meskipun ditandai [JsonIgnore]");

        using var dok = JsonDocument.Parse(teks);
        var pertama = dok.RootElement.EnumerateArray().First();
        pertama.GetProperty("role").ValueKind.Should().Be(JsonValueKind.String,
            "layar hak akses membaca nama peran, bukan angka enum");
        pertama.TryGetProperty("username", out _).Should().BeTrue();
        pertama.TryGetProperty("isActive", out _).Should().BeTrue();
        pertama.TryGetProperty("mustChangePassword", out _).Should().BeTrue();
        pertama.TryGetProperty("lastLoginAt", out _).Should().BeTrue();
        pertama.TryGetProperty("lockedUntil", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Kasir_MelihatKatalogTanpaResepDanHargaBahan()
    {
        var owner = await OwnerAsync();
        var (_, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        var penanda = Guid.NewGuid().ToString("N")[..8];
        using var bahan = await owner.PostAsJsonAsync("/api/ingredients", new
        {
            name = $"Bahan Rahasia {penanda}",
            unit = "kg",
            pricePerUnit = 1_200_000,
            currentStock = 0,
            minStockAlert = 0,
            isActive = true,
        });
        var teksBahan = await bahan.Content.ReadAsStringAsync();
        bahan.StatusCode.Should().Be(HttpStatusCode.Created, teksBahan);
        var bahanId = JsonDocument.Parse(teksBahan).RootElement.GetProperty("id").GetInt32();

        var namaProduk = $"Roti Resep {penanda}";
        using var produk = await owner.PostAsJsonAsync("/api/products", new
        {
            name = namaProduk,
            category = "Uji RBAC",
            sellingPriceMinorUnits = 1_500_000,
            lines = new[] { new { ingredientId = bahanId, quantity = 0.5m } },
        });
        produk.IsSuccessStatusCode.Should().BeTrue(await produk.Content.ReadAsStringAsync());

        int produkId;
        using (var daftar = JsonDocument.Parse(await owner.GetStringAsync("/api/products")))
        {
            produkId = daftar.RootElement.EnumerateArray()
                .First(p => p.GetProperty("name").GetString() == namaProduk)
                .GetProperty("id").GetInt32();
        }

        using (var daftarKasir = JsonDocument.Parse(await kasir.GetStringAsync("/api/products")))
        {
            var baris = daftarKasir.RootElement.EnumerateArray()
                .First(p => p.GetProperty("id").GetInt32() == produkId);
            baris.TryGetProperty("recipeLines", out _).Should().BeFalse(
                "kasir tidak boleh melihat komposisi resep beserta harga bahan");
            baris.GetProperty("sellingPrice").GetInt64().Should().Be(1_500_000,
                "harga jual tetap dibutuhkan kasir untuk mencatat pesanan");
        }

        using (var detailKasir = JsonDocument.Parse(await kasir.GetStringAsync($"/api/products/{produkId}")))
        {
            detailKasir.RootElement.TryGetProperty("recipeLines", out _).Should().BeFalse();
            detailKasir.RootElement.TryGetProperty("hpp", out _).Should().BeFalse();
            detailKasir.RootElement.GetProperty("sellingPriceMinorUnits").GetInt64().Should().Be(1_500_000);
        }

        using var detailOwner = JsonDocument.Parse(await owner.GetStringAsync($"/api/products/{produkId}"));
        detailOwner.RootElement.GetProperty("recipeLines").GetArrayLength().Should().Be(1,
            "dapur/pemilik tetap membutuhkan resep untuk perhitungan HPP");
        detailOwner.RootElement.TryGetProperty("hpp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Kasir_HanyaMelihatPelangganYangDilayani()
    {
        var owner = await OwnerAsync();
        var (_, token) = await BuatPenggunaAsync(owner, "Pegawai");
        using var kasir = Klien(owner, token);

        var penanda = Guid.NewGuid().ToString("N")[..8];
        var namaOwner = $"Pelanggan Pemilik {penanda}";
        var namaKasir = $"Pelanggan Kasir {penanda}";
        var harga = 1_000_000L;
        var produk = await BuatProdukAsync(owner, $"Kue Cakupan {penanda}", harga);

        var buatOwner = await owner.PostAsJsonAsync("/api/orders", new
        {
            customerName = namaOwner,
            phone = $"0811{penanda[..6]}",
            description = "Uji cakupan pelanggan",
            pickupDate = "2030-07-07T09:00:00",
            totalAmount = 200_000L,
            downPayment = 0,
        });
        buatOwner.StatusCode.Should().Be(HttpStatusCode.Created, await buatOwner.Content.ReadAsStringAsync());

        var buatKasir = await kasir.PostAsJsonAsync("/api/orders", new
        {
            customerName = namaKasir,
            phone = $"0822{penanda[..6]}",
            description = "Uji cakupan pelanggan",
            pickupDate = "2030-07-07T09:00:00",
            totalAmount = harga,
            downPayment = 0,
            productTemplateId = produk,
            quantity = 1,
        });
        buatKasir.StatusCode.Should().Be(HttpStatusCode.Created, await buatKasir.Content.ReadAsStringAsync());

        using var daftarKasir = JsonDocument.Parse(await kasir.GetStringAsync("/api/customers?limit=2000"));
        var namaTerlihat = daftarKasir.RootElement.EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()).ToHashSet();
        namaTerlihat.Should().Contain(namaKasir);
        namaTerlihat.Should().NotContain(namaOwner,
            "buku pelanggan seluruh toko bukan milik akun kasir");

        using var daftarOwner = JsonDocument.Parse(await owner.GetStringAsync("/api/customers?limit=2000"));
        var namaOwnerTerlihat = daftarOwner.RootElement.EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()).ToHashSet();
        namaOwnerTerlihat.Should().Contain(namaOwner).And.Contain(namaKasir,
            "pemilik tetap melihat seluruh pelanggan toko");
    }

    [Fact]
    public async Task Pengaturan_LengkapHanyaUntukOwner_MerekTetapPublik()
    {
        var owner = await OwnerAsync();
        var (_, tokenKasir) = await BuatPenggunaAsync(owner, "Pegawai");
        var (_, tokenAdmin) = await BuatPenggunaAsync(owner, "Admin");
        using var kasir = Klien(owner, tokenKasir);
        using var admin = Klien(owner, tokenAdmin);

        (await owner.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await kasir.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "pengaturan sistem hanya dibuka Owner, bukan Admin operasional");

        using var publik = new HttpClient { BaseAddress = owner.BaseAddress! };
        var merek = await publik.GetAsync("/api/brand");
        merek.StatusCode.Should().Be(HttpStatusCode.OK, "layar masuk membaca merek sebelum ada sesi");
        using var dok = JsonDocument.Parse(await merek.Content.ReadAsStringAsync());
        dok.RootElement.TryGetProperty("outlet", out _).Should().BeTrue(
            "nama outlet di kepala layar dibaca semua peran lewat merek publik");
    }

    [Fact]
    public async Task Dapur_MenerimaBarang_TetapiTidakMembuatPesananPembelian()
    {
        var owner = await OwnerAsync();
        var (_, tokenDapur) = await BuatPenggunaAsync(owner, "Dapur");
        using var dapur = Klien(owner, tokenDapur);

        var penanda = Guid.NewGuid().ToString("N")[..8];
        using var pemasok = await owner.PostAsJsonAsync("/api/suppliers", new { name = $"Pemasok {penanda}" });
        pemasok.StatusCode.Should().Be(HttpStatusCode.OK);
        var pemasokId = JsonDocument.Parse(await pemasok.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        using var bahan = await owner.PostAsJsonAsync("/api/ingredients", new
        {
            name = $"Bahan Beli {penanda}",
            unit = "kg",
            pricePerUnit = 1_200_000,
            currentStock = 0,
            minStockAlert = 0,
            isActive = true,
        });
        bahan.StatusCode.Should().Be(HttpStatusCode.Created);
        var bahanId = JsonDocument.Parse(await bahan.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetInt32();

        var buatDapur = await dapur.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = pemasokId,
            expectedDate = (string?)null,
            notes = "Dapur tidak boleh memesan sendiri",
            lines = new[] { new { ingredientId = bahanId, orderedQty = 5m, unitCostMinorUnits = 1_200_000, unit = "kg" } },
        });
        buatDapur.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "memesan barang mengikat uang toko; itu keputusan Owner/Admin");

        (await dapur.GetAsync("/api/purchases")).StatusCode.Should().Be(HttpStatusCode.OK,
            "dapur perlu melihat PO yang harus diterima");

        using var buatOwner = await owner.PostAsJsonAsync("/api/purchases", new
        {
            supplierId = pemasokId,
            expectedDate = (string?)null,
            notes = "PO untuk diterima dapur",
            lines = new[] { new { ingredientId = bahanId, orderedQty = 5m, unitCostMinorUnits = 1_200_000, unit = "kg" } },
        });
        var teksPo = await buatOwner.Content.ReadAsStringAsync();
        buatOwner.StatusCode.Should().Be(HttpStatusCode.OK, teksPo);
        var poId = JsonDocument.Parse(teksPo).RootElement.GetProperty("id").GetInt32();

        using var detail = JsonDocument.Parse(await owner.GetStringAsync($"/api/purchases/{poId}"));
        var baris = detail.RootElement.GetProperty("lines").EnumerateArray().Single();
        var barisId = baris.GetProperty("id").GetInt32();

        using var lokasi = JsonDocument.Parse(await owner.GetStringAsync("/api/inventory/locations"));
        var gudangId = lokasi.RootElement.EnumerateArray()
            .First(l => l.GetProperty("type").GetString() == "Warehouse")
            .GetProperty("id").GetInt32();

        var terima = await dapur.PostAsJsonAsync($"/api/purchases/{poId}/receive", new
        {
            notes = "Penerimaan oleh dapur",
            lines = new[]
            {
                new { purchaseOrderLineId = barisId, ingredientId = bahanId, receivedQty = 2m, unitCostMinorUnits = 1_200_000, locationId = gudangId },
            },
        });
        terima.StatusCode.Should().Be(HttpStatusCode.OK, await terima.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EndpointTanpaToken_DitolakKecualiJalurPublik()
    {
        using var anonim = new HttpClient { BaseAddress = new Uri(_fixture.BaseUrl), Timeout = TimeSpan.FromSeconds(15) };

        string[] butuhLogin =
        [
            "/api/dashboard", "/api/orders", "/api/customers", "/api/products",
            "/api/purchases", "/api/suppliers", "/api/settings", "/api/users",
            "/api/journal", "/api/audit", "/api/tidak-ada",
        ];
        foreach (var rute in butuhLogin)
        {
            var resp = await anonim.GetAsync(rute);
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                $"tanpa token, {rute} harus ditolak sebelum isinya dibaca");
        }

        (await anonim.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonim.GetAsync("/api/brand")).StatusCode.Should().Be(HttpStatusCode.OK);

        var sandi = await AppLogin.EnsureUsablePasswordAsync(_fixture.BaseUrl);
        using var masuk = await anonim.PostAsJsonAsync("/api/auth/login",
            new { username = AppLogin.Username, password = sandi });
        masuk.StatusCode.Should().Be(HttpStatusCode.OK, "layar masuk adalah jalur publik yang sah");
    }

    private static async Task<string> LoginTokenAsync(HttpClient induk, string username)
    {
        var (token, _) = await LoginAsync(induk, username, AppLogin.TestPassword);
        return token;
    }

    private static async Task<long> HargaProdukAsync(HttpClient owner, int produkId)
    {
        using var dok = JsonDocument.Parse(await owner.GetStringAsync($"/api/products/{produkId}"));
        return dok.RootElement.GetProperty("sellingPriceMinorUnits").GetInt64();
    }
}
