using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using FluentAssertions;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// UI tests covering sidebar navigation.
/// Nav item labels are derived from d:\bakery-desktop\frontend\src\config\navigation.js.
/// The fixture starts a throwaway host and database unless BAKERY_TEST_URL points elsewhere.
/// </summary>
[Collection("UI Tests")]
public sealed class NavigationTests : IClassFixture<PlaywrightFixture>
{
    // Nav group labels from navigation.js (used to locate sidebar sections)
    private const string SalesGroupLabel      = "PENJUALAN & PELANGGAN";
    private const string ProductionGroupLabel = "OPERASIONAL & PRODUKSI";
    private const string InventoryGroupLabel  = "RANTAI PASOK & PERSEDIAAN";
    private const string FinanceGroupLabel    = "KEUANGAN & AKUNTANSI";

    private readonly PlaywrightFixture _fixture;

    public NavigationTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Logs in through the real form and clears the mandatory password-change gate,
    /// which otherwise covers the sidebar (see <see cref="AppLogin"/>).
    /// </summary>
    private async Task EnsureLoggedInAsync()
    {
        await AppLogin.SignInAsync(_fixture.Page, _fixture.BaseUrl);
    }

    [Fact]
    public async Task Sidebar_ClickSalesMenu_ShowsSalesView()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        // Click a known sales nav item — "Daftar Pesanan" is unique to the sales nav item
        await ClickNavItemByTextAsync(page, "Daftar Pesanan");

        // Verify the app is still rendered after navigation — <main> and <aside> should be visible
        await page.WaitForSelectorAsync("main", new PageWaitForSelectorOptions { Timeout = 8_000 });

        var visible = await page.IsVisibleAsync("main");
        visible.Should().BeTrue("clicking the Penjualan menu item should show the SalesView inside main");
    }

    [Fact]
    public async Task Sidebar_ClickInventoryMenu_ShowsInventoryView()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        // "RANTAI PASOK & PERSEDIAAN" group; click "Katalog Bahan & Produk Jadi" as the inventory entry
        await ClickNavItemByTextAsync(page, "Katalog Bahan");

        await page.WaitForSelectorAsync("main", new PageWaitForSelectorOptions { Timeout = 8_000 });

        var visible = await page.IsVisibleAsync("main");
        visible.Should().BeTrue("clicking the Inventori menu item should show the InventoryView inside main");
    }

    [Fact]
    public async Task Sidebar_ClickProductionMenu_ShowsProductionView()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        // "OPERASIONAL & PRODUKSI" group; click "Perintah Kerja (SPK)"
        await ClickNavItemByTextAsync(page, "Perintah Kerja");

        await page.WaitForSelectorAsync("main", new PageWaitForSelectorOptions { Timeout = 8_000 });

        var visible = await page.IsVisibleAsync("main");
        visible.Should().BeTrue("clicking the Produksi menu item should show the ProductionView inside main");
    }

    [Fact]
    public async Task Sidebar_ClickFinanceMenu_ShowsFinanceView()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        // "KEUANGAN & AKUNTANSI" group; click "Buku Besar & Jurnal Umum"
        await ClickNavItemByTextAsync(page, "Buku Besar");

        await page.WaitForSelectorAsync("main", new PageWaitForSelectorOptions { Timeout = 8_000 });

        var visible = await page.IsVisibleAsync("main");
        visible.Should().BeTrue("clicking the Keuangan menu item should show the FinanceView inside main");
    }

    // ---------------------------------------------------------------------------
    // Menu yang berbagi satu layar: menu samping harus membuka bagian yang sesuai
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Dulu menu Pemasok membuka tab Purchase Orders. Kini tab awal mengikuti menu yang diklik,
    /// jadi daftar pemasok langsung terlihat tanpa satu klik tambahan.
    /// </summary>
    [Fact]
    public async Task MenuPemasok_MembukaTabPemasok()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        await ClickNavItemByTextAsync(page, "Pemasok");
        // Isi layar yang ditunggu, bukan hanya kerangka <main>, supaya tidak membaca "memuat layar…".
        await TungguTeksAsync(page, "daftar pemasok");

        var teks = await page.Locator("main").InnerTextAsync();
        var hurufKecil = teks.ToLowerInvariant();
        hurufKecil.Should().Contain("daftar pemasok", "menu Pemasok harus mendarat di tab Pemasok");
        hurufKecil.Should().NotContain("daftar purchase order", "tab Purchase Orders tidak boleh menjadi tab awal menu Pemasok");
    }

    /// <summary>
    /// Lima menu Penjualan berbagi satu layar. Menu Pelanggan harus mendarat di bagian pelanggan,
    /// bukan menampilkan tabel pesanan lebih dulu.
    /// </summary>
    [Fact]
    public async Task MenuPelanggan_MembukaBagianPelangganTanpaTabelPesanan()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        await ClickNavItemByTextAsync(page, "Data Pelanggan");
        await page.WaitForSelectorAsync("main h1", new PageWaitForSelectorOptions { Timeout = 8_000 });

        (await page.Locator("main h1").First.InnerTextAsync()).Should().Contain("Data Pelanggan");
        var teks = await page.Locator("main").InnerTextAsync();
        var hurufKecil = teks.ToLowerInvariant();
        hurufKecil.Should().Contain("daftar pelanggan");
        hurufKecil.Should().NotContain("nomor nota / faktur", "bagian pelanggan tidak menampilkan tabel pesanan");
    }

    /// <summary>Menu Pre-Order harus menampilkan jadwal ambil, bukan daftar pesanan biasa.</summary>
    [Fact]
    public async Task MenuPreOrder_MenampilkanKolomJadwalAmbil()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        await ClickNavItemByTextAsync(page, "Pesanan Terjadwal");
        await page.WaitForSelectorAsync("main h1", new PageWaitForSelectorOptions { Timeout = 8_000 });

        (await page.Locator("main h1").First.InnerTextAsync()).Should().Contain("Pesanan Terjadwal");
        (await page.Locator("main").InnerTextAsync()).ToLowerInvariant().Should().Contain("jadwal ambil",
            "bagian pre-order menyaring jadwal yang belum lewat dan menampilkan tanggalnya");
    }

    /// <summary>Tiga menu Produksi berbagi satu layar; Perencanaan hanya menampilkan SPK terjadwal.</summary>
    [Fact]
    public async Task MenuPerencanaan_MembukaDaftarSpkTerjadwal()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        await ClickNavItemByTextAsync(page, "Perencanaan Jadwal Dapur");
        await page.WaitForSelectorAsync("main h1", new PageWaitForSelectorOptions { Timeout = 8_000 });

        (await page.Locator("main h1").First.InnerTextAsync()).Should().Contain("Perencanaan Jadwal Dapur");
        (await page.Locator("main").InnerTextAsync()).ToLowerInvariant().Should().Contain("daftar spk terjadwal");
    }

    /// <summary>
    /// Pintasan dasbor mengikuti kapabilitas yang sama dengan menu samping. Akun tanpa
    /// production-write tidak boleh melihat pintasan yang ujungnya ditolak server.
    /// </summary>
    [Fact]
    public async Task PintasanDashboard_TanpaHakProduksi_TidakDirender()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        await page.EvaluateAsync(
            "() => { const u = JSON.parse(localStorage.getItem('s22_user')); " +
            "u.capabilities = (u.capabilities || []).filter((c) => c !== 'production-write'); " +
            "localStorage.setItem('s22_user', JSON.stringify(u)); }");
        await page.ReloadAsync();

        await ClickNavItemByTextAsync(page, "Dasbor");
        await page.WaitForSelectorAsync("main", new PageWaitForSelectorOptions { Timeout = 10000 });

        var teks = (await page.Locator("main").InnerTextAsync()).ToLowerInvariant();
        teks.Should().Contain("jalan pintas", "dasbor harus termuat agar pemeriksaan pintasan bermakna");
        teks.Should().NotContain("perintah kerja dapur", "tanpa production-write pintasan tidak dirender");
    }

    /// <summary>
    /// Katalog dulu menulis bahwa stok produk jadi tidak terlacak, padahal endpoint finished-goods
    /// menyediakannya. Kolom baru harus benar-benar dirender dan catatan usang itu hilang.
    /// </summary>
    [Fact]
    public async Task Katalog_MenampilkanKolomStokProdukJadi()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        await ClickNavItemByTextAsync(page, "Katalog Bahan");
        await page.WaitForSelectorAsync("main", new PageWaitForSelectorOptions { Timeout = 8_000 });

        // <main> sudah ada sejak kerangka layar, jadi menunggunya saja bisa membaca teks
        // "memuat layar…" dan gagal secara acak. Tunggu sampai isi katalog benar-benar termuat;
        // kalau tidak pernah muncul, asersi di bawah tetap gagal dengan pesan yang sama.
        await TungguTeksAsync(page, "stok jadi per lokasi");

        var teks = (await page.Locator("main").InnerTextAsync()).ToLowerInvariant();
        teks.Should().Contain("stok jadi per lokasi", "stok etalase produk jadi harus terlihat di katalog");
        teks.Should().NotContain("belum punya baris locationstock sendiri",
            "catatan usang yang menyangkal buku stok produk jadi tidak boleh tersisa");
    }

    /// <summary>Label analisis harus menyebut selisih pendapatan − beban, bukan laba kotor akuntansi.</summary>
    [Fact]
    public async Task Analisis_MemakaiLabelSelisihBukanLabaKotor()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        await ClickNavItemByTextAsync(page, "Analisis & Tren");
        await page.WaitForSelectorAsync("main h1", new PageWaitForSelectorOptions { Timeout = 8_000 });

        // Label KPI yang dilihat pengguna; kotak catatan di bawahnya sengaja menyebut "laba kotor"
        // untuk menjelaskan kenapa angka ini bukan laba kotor akuntansi, jadi pemeriksaan negatif
        // diarahkan ke kisi KPI saja.
        var kpi = (await page.Locator("main .grid").First.InnerTextAsync()).ToLowerInvariant();
        kpi.Should().Contain("selisih pendapatan", "angka analisis adalah pendapatan jurnal dikurangi beban jurnal");
        kpi.Should().NotContain("laba kotor", "label KPI tidak boleh memakai istilah laba kotor karena penjualan belum dibebani HPP");

        var teks = (await page.Locator("main").InnerTextAsync()).ToLowerInvariant();
        teks.Should().Contain("bukan laba kotor akuntansi", "catatan menjelaskan kenapa angka ini bukan laba kotor akuntansi");
    }

    // ---------------------------------------------------------------------------
    // Peran & penyaringan menu
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Label peran dulu dipetakan dengan kunci angka sementara sesi menyimpan nama peran, sehingga
    /// pemilik toko pun tampil sebagai "Pegawai Toko". Tes ini mengunci label dan menu sistemnya.
    /// </summary>
    [Fact]
    public async Task Sidebar_PeranPemilik_MenampilkanLabelDanMenuSistem()
    {
        await EnsureLoggedInAsync();
        var page = _fixture.Page;

        // Aplikasi menyimpan peran sebagai nama (App.jsx memakai roleName saat login), jadi tes
        // harus memakai bentuk yang sama; sesi bernomor hanya menutupi bug label.
        await UbahSesiKeBentukAplikasiAsync(page, "Owner");

        var sidebar = page.Locator("aside").First;
        await sidebar.WaitForAsync(new LocatorWaitForOptions { Timeout = 8_000 });

        (await sidebar.InnerTextAsync()).Should().Contain("Pemilik Toko",
            "akun seed 'admin' berperan Owner, jadi label peran harus 'Pemilik Toko', bukan label pegawai");

        await ClickNavItemByTextAsync(page, "Jejak Audit");
        await page.WaitForSelectorAsync("main h1", new PageWaitForSelectorOptions { Timeout = 8_000 });

        (await page.Locator("main h1").First.InnerTextAsync()).Should().Contain("Jejak Audit",
            "pemilik toko harus bisa membuka layar jejak audit");
    }

    /// <summary>
    /// Menu disaring oleh kapabilitas yang dikirim server saat login. Tes membuat pegawai lewat
    /// endpoint produksi, lalu memastikan menu administrasi tidak dirender dan server tetap
    /// menolak endpoint-nya — bukan hanya tombolnya yang disembunyikan.
    /// </summary>
    [Fact]
    public async Task Sidebar_PeranPegawai_MenyembunyikanMenuAdministrasiDanDitolakServer()
    {
        var baseUrl = _fixture.BaseUrl;
        var page = _fixture.Page;

        using var owner = await AppLogin.AuthorizedClientAsync(baseUrl);
        var username = "kasir-uji-" + Guid.NewGuid().ToString("N")[..6];
        const string passwordAwal = "KasirUji2026";
        const string passwordBaru = "KasirUjiBaru2026";

        using var buat = await owner.PostAsJsonAsync("/api/users",
            new { username, password = passwordAwal, role = "Pegawai" });
        buat.IsSuccessStatusCode.Should().BeTrue(
            $"endpoint /api/users harus menerima pegawai baru dari pemilik (HTTP {(int)buat.StatusCode}): " +
            await buat.Content.ReadAsStringAsync());

        var (token, sesi) = await MasukSebagaiPegawaiAsync(baseUrl, username, passwordAwal, passwordBaru);
        sesi.GetProperty("capabilities").EnumerateArray()
            .Select(c => c.GetString()).Should().NotContain("administration",
                "peran Pegawai tidak berhak atas administrasi; kapabilitas inilah yang menyaring menu");

        using var pegawai = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
        pegawai.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        (await pegawai.GetAsync("/api/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "jejak audit hanya untuk administrasi; penolakan harus datang dari server, bukan dari menu");
        (await pegawai.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "daftar pegawai tidak boleh terbaca oleh pegawai biasa");
        (await pegawai.GetAsync("/api/analytics/sales")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "analisis penjualan termasuk wewenang keuangan");
        (await pegawai.GetAsync("/api/tasks")).StatusCode.Should().Be(HttpStatusCode.OK,
            "kotak tugas memang tersedia untuk semua peran yang bekerja");

        // Bentuk sesi sama dengan yang disimpan api.setAuth(): getUser() menolak sesi tanpa
        // daftar kapabilitas, dan App.jsx menyimpan peran sebagai nama hasil roleName().
        var userJson = JsonSerializer.Serialize(new
        {
            id = sesi.GetProperty("userId").GetInt32(),
            username = sesi.GetProperty("username").GetString(),
            role = "Pegawai",
            capabilities = sesi.GetProperty("capabilities"),
            mustChangePassword = sesi.GetProperty("mustChangePassword").GetBoolean(),
        });

        await page.GotoAsync(baseUrl);
        await page.EvaluateAsync(
            "(sesi) => { localStorage.setItem('s22_token', sesi.token); localStorage.setItem('s22_user', sesi.user); }",
            new { token, user = userJson });
        await page.ReloadAsync();

        var sidebar = page.Locator("aside").First;
        await sidebar.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        var teks = await sidebar.InnerTextAsync();

        teks.Should().Contain("Staf Toko / Kasir", "label peran harus mengikuti nama peran dari sesi");
        teks.Should().NotContain("Jejak Audit", "menu administrasi tidak boleh dirender untuk pegawai");
        teks.Should().NotContain("Hak Akses", "pegawai tidak mengelola akun");
        teks.Should().NotContain("Pengaturan Toko", "pengaturan milik pemilik/manajer");
        teks.Should().NotContain("Analisis & Tren", "analisis butuh wewenang keuangan");
        teks.Should().NotContain(FinanceGroupLabel, "grup keuangan tidak relevan bagi kasir");
        teks.Should().Contain("Kotak Tugas", "ruang kerja tetap tersedia untuk pegawai");
    }

    /// <summary>
    /// Pegawai baru berangkat dengan sandi sementara dan wajib menggantinya. Gerbang itu dilewati
    /// lewat API produksi pada basis data sementara milik fixture, sama seperti <see cref="AppLogin"/>.
    /// </summary>
    private static async Task<(string Token, JsonElement Sesi)> MasukSebagaiPegawaiAsync(
        string baseUrl, string username, string passwordAwal, string passwordBaru)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };

        var sesi = await LoginAsync(http, username, passwordAwal);
        if (sesi.GetProperty("mustChangePassword").GetBoolean())
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
            {
                Content = JsonContent.Create(new { currentPassword = passwordAwal, newPassword = passwordBaru }),
            };
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sesi.GetProperty("token").GetString());
            using var ganti = await http.SendAsync(request);
            var badan = await ganti.Content.ReadAsStringAsync();
            ganti.IsSuccessStatusCode.Should().BeTrue(
                $"pegawai harus bisa mengganti sandi sementara lewat API (HTTP {(int)ganti.StatusCode}): {badan}");

            sesi = await LoginAsync(http, username, passwordBaru);
        }

        return (sesi.GetProperty("token").GetString()!, sesi);
    }

    private static async Task<JsonElement> LoginAsync(HttpClient http, string username, string password)
    {
        var response = await http.PostAsJsonAsync("/api/auth/login", new { username, password });
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(
            $"login '{username}' harus berhasil di host uji (HTTP {(int)response.StatusCode}): {body}");
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// App.jsx menyimpan peran sebagai nama hasil roleName(), bukan angka dari server. Tes yang
    /// memakai bentuk sesi produksi inilah yang benar-benar menjaga label peran.
    /// </summary>
    private static async Task UbahSesiKeBentukAplikasiAsync(IPage page, string namaPeran)
    {
        await page.EvaluateAsync(
            "(nama) => { const u = JSON.parse(localStorage.getItem('s22_user')); u.role = nama; localStorage.setItem('s22_user', JSON.stringify(u)); }",
            namaPeran);
        await page.ReloadAsync();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Clicks the first nav/sidebar element whose visible text contains <paramref name="partialText"/>.
    /// Uses XPath to match partial text, which is more reliable across React component trees.
    /// </summary>
    private static async Task ClickNavItemByTextAsync(IPage page, string partialText)
    {
        // Try button or anchor elements in the sidebar that contain the target text
        var locator = page.Locator($"nav button:has-text('{partialText}'), nav a:has-text('{partialText}'), aside button:has-text('{partialText}'), aside a:has-text('{partialText}'), [role='navigation'] button:has-text('{partialText}')");
        await locator.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 8_000 });
        await locator.First.ClickAsync();
    }

    /// <summary>
    /// Menunggu isi layar, bukan hanya kerangkanya. <c>main</c> sudah ada sejak layar pertama
    /// dirender, jadi asersi teks yang langsung membacanya bisa menangkap "memuat layar…".
    /// </summary>
    private static async Task TungguTeksAsync(IPage page, string teks, int timeoutMs = 10_000)
    {
        var json = JsonSerializer.Serialize(teks.ToLowerInvariant());
        await page.WaitForFunctionAsync(
            $"() => (document.querySelector('main')?.innerText || '').toLowerCase().includes({json})",
            null, new PageWaitForFunctionOptions { Timeout = timeoutMs });
    }
}
