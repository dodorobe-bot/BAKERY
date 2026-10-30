using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;

namespace BakeryDesktop.Perf;

/// <summary>
/// Pengukur perpindahan menu Seruni 22 pada peramban sungguhan.
///
/// Yang diukur: waktu dari klik menu sampai layarnya benar-benar siap dipakai, bukan hanya sampai
/// permintaan jaringan selesai. Aplikasi menandai titik itu lewat event <c>s22:layar-siap</c>
/// (dikirim App.jsx setelah data tab diterapkan dan React sempat menggambar dua kali), jadi angka
/// di sini memisahkan lama menunggu data dari lama menggambar layar.
///
/// Server yang diukur bisa dijalankan sendiri oleh perkakas ini (basis data sementara atau salinan
/// data besar) atau ditunjuk lewat <c>--url</c> untuk mengukur host lain yang sudah berjalan.
///
/// Seluruh pengukuran memakai basis data yang ditunjuk <c>--db</c>; perkakas ini tidak pernah
/// menyentuh basis data pemakaian kecuali memang diberi jalurnya sendiri.
/// </summary>
internal static class Program
{
    private sealed record Menu(string Id, string Label, string Penanda);

    // Penanda adalah teks yang hanya muncul di layar tujuan; dipakai memastikan klik benar-benar
    // membuka menu yang dimaksud, bukan sekadar mengubah sorotan menu samping.
    private static readonly Menu[] MenuDaftar =
    [
        new("dashboard", "Dasbor & Indikator Utama", "Penerimaan Kasir Hari Ini"),
        new("tasks", "Kotak Tugas & Persetujuan", "Kotak Tugas & Persetujuan"),
        new("notifications", "Pemberitahuan & Catatan Aktivitas", "Notifikasi Internal"),
        new("pos", "Kasir Penjualan Cepat", "Penjualan, Kasir Toko & Piutang"),
        new("orders", "Daftar Pesanan & Penjualan", "Penjualan, Kasir Toko & Piutang"),
        new("preorders", "Pesanan Terjadwal (Pre-Order)", "Penjualan, Kasir Toko & Piutang"),
        new("customers", "Data Pelanggan & Klien", "Penjualan, Kasir Toko & Piutang"),
        new("receivables", "Piutang & Pembayaran", "Penjualan, Kasir Toko & Piutang"),
        new("sales_returns", "Retur Penjualan", "Retur Penjualan"),
        new("production_plan", "Perencanaan Jadwal Dapur", "Surat Perintah Kerja Dapur (SPK)"),
        new("production_orders", "Perintah Kerja (SPK)", "Surat Perintah Kerja Dapur (SPK)"),
        new("production_actual", "Pemakaian Aktual & Hasil", "Surat Perintah Kerja Dapur (SPK)"),
        new("recipes_bom", "Resep & Daftar Bahan Baku", "Resep & BOM Produk"),
        new("quality_control", "Pemeriksaan Mutu (QC)", "Pemeriksaan Mutu (QC)"),
        new("cost_calc", "Harga Pokok Produksi (HPP)", "Harga Pokok Produksi (HPP)"),
        new("purchases", "Pembelian & Penerimaan Barang", "Daftar Purchase Order"),
        new("suppliers", "Pemasok & Rekanan", "Daftar Purchase Order"),
        new("inventory_locations", "Gudang & Lokasi Rak", "Rantai Pasok & Persediaan Multi-Lokasi"),
        new("kitchen_transfer", "Mutasi Gudang ke Dapur", "Rantai Pasok & Persediaan Multi-Lokasi"),
        new("stock_catalog", "Katalog Bahan & Produk Jadi", "Rantai Pasok & Persediaan Multi-Lokasi"),
        new("stock_opname", "Pemeriksaan Fisik (Stok Opname)", "Pemeriksaan Fisik (Stok Opname)"),
        new("batch_expiry", "Nomor Batch & Kedaluwarsa", "Nomor Batch & Kedaluwarsa"),
        new("general_ledger", "Buku Besar & Jurnal Umum", "Keuangan, Buku Kas & Jurnal Otomatis"),
        new("cash_bank", "Kas Toko & Rekening Bank", "Keuangan, Buku Kas & Jurnal Otomatis"),
        new("payables_receivables", "Utang & Piutang Usaha", "Keuangan, Buku Kas & Jurnal Otomatis"),
        new("financial_reports", "Laporan Keuangan & Laba Rugi", "Keuangan, Buku Kas & Jurnal Otomatis"),
        new("executive_analytics", "Analisis & Tren Penjualan", "Analisis & Tren Penjualan"),
        new("audit_log", "Jejak Audit & Riwayat Sistem", "Jejak Audit & Riwayat Sistem"),
        new("user_management", "Hak Akses & Pegawai", "Hak Akses & Pegawai"),
        new("settings", "Pengaturan Toko & Cabang", "Pengaturan Toko & Cabang"),
        new("device_settings", "Perangkat Kasir", "Perangkat Kasir"),
    ];

    private static readonly Dictionary<string, string> NamaPeran = new()
    {
        ["0"] = "Owner", ["1"] = "Admin", ["2"] = "Dapur", ["3"] = "Pegawai",
    };

    private const string SkripInit = """
        (() => {
          window.__mulaiHalaman = performance.now();
          window.__siapTerakhir = null;
          window.__navT0 = 0;
          window.addEventListener('s22:layar-siap', (e) => {
            window.__siapTerakhir = { tab: e.detail.tab, ms: e.detail.ms, t: performance.now() };
          });
          document.addEventListener('click', (e) => {
            const b = e.target && e.target.closest ? e.target.closest('aside button') : null;
            if (b) window.__navT0 = performance.now();
          }, true);
        })();
        """;

    private const string SkripTungguSiap = """
        async (tab) => {
          const t0 = window.__navT0;
          const batas = t0 + 30000;
          const duaRaf = () => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
          while (true) {
            const s = window.__siapTerakhir;
            if (s && s.tab === tab && s.t > t0) {
              await duaRaf();
              const perf = window.__s22Perf || { riwayat: [] };
              return {
                t0,
                msSiap: Math.round((performance.now() - t0) * 10) / 10,
                msData: s.ms,
                reqs: perf.riwayat.filter((r) => r.mulai >= t0).map((r) => ({
                  jalur: r.jalur, metode: r.metode, status: r.status, mulai: r.mulai, ms: r.ms,
                  durasiPeladen: r.durasiPeladen,
                })),
                heap: performance.memory ? performance.memory.usedJSHeapSize : 0,
                nodes: document.querySelectorAll('*').length,
                rows: document.querySelectorAll('tbody tr').length,
              };
            }
            if (performance.now() > batas) throw new Error('batas waktu menunggu layar siap: ' + tab);
            await new Promise((r) => requestAnimationFrame(r));
          }
        }
        """;

    private const string SkripMuatAwal = """
        async () => {
          const batas = performance.now() + 60000;
          while (!window.__siapTerakhir) {
            if (performance.now() > batas) throw new Error('batas waktu muat awal');
            await new Promise((r) => requestAnimationFrame(r));
          }
          await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
          const nav = performance.getEntriesByType('navigation')[0] || {};
          const perf = window.__s22Perf || { riwayat: [] };
          return {
            tab: window.__siapTerakhir.tab,
            msSiap: Math.round(window.__siapTerakhir.t - window.__mulaiHalaman),
            msData: window.__siapTerakhir.ms,
            dcl: Math.round(nav.domContentLoadedEventEnd || 0),
            load: Math.round(nav.loadEventEnd || 0),
            heap: performance.memory ? performance.memory.usedJSHeapSize : 0,
            reqs: perf.riwayat.map((r) => ({ jalur: r.jalur, metode: r.metode, status: r.status,
              mulai: r.mulai, ms: r.ms, durasiPeladen: r.durasiPeladen })),
          };
        }
        """;

    private const string SkripPenanda = """
        (teks) => {
          const main = document.querySelector('main');
          if (!main) return false;
          // innerText mengikuti text-transform, jadi judul berkelas "uppercase" tampil huruf besar.
          return main.innerText.toLowerCase().indexOf(teks.toLowerCase()) >= 0;
        }
        """;

    public static async Task<int> Main(string[] args)
    {
        var opsi = Opsi.Baca(args);
        Directory.CreateDirectory(opsi.FolderKeluaran);
        // Tanpa --db, host uji harus tetap memakai basis data sendiri: nilai bawaan host adalah
        // basis data pemakaian di folder pengguna, dan perkakas ukur tidak boleh menyentuhnya.
        if (string.IsNullOrEmpty(opsi.Url) && opsi.Db is null) opsi.PakaiBasisDataSementara();

        Process? host = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        try
        {
            var url = opsi.Url;
            if (string.IsNullOrEmpty(url))
            {
                (url, host) = await JalankanHostAsync(opsi);
            }

            _ambilTangkapan = opsi.AmbilTangkapan;
            var sesi = await MasukAsync(url, opsi, host is not null);

            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var konteks = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });
            var page = await konteks.NewPageAsync();
            await page.AddInitScriptAsync(SkripInit);

            var galatKonsol = new List<string>();
            page.PageError += (_, e) => galatKonsol.Add("pageerror: " + e);
            page.Console += (_, e) => { if (e.Type == "error") galatKonsol.Add("console: " + e.Text); };

            var gagalJaringan = new List<string>();
            page.Response += (_, e) => { if (e.Status >= 400) gagalJaringan.Add($"{e.Status} {e.Url}"); };

            var cdp = await konteks.NewCDPSessionAsync(page);
            await cdp.SendAsync("Performance.enable");

            // ── Muat awal ──────────────────────────────────────────────────────
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            await page.EvaluateAsync(
                "(s) => { localStorage.setItem('s22_token', s.token); localStorage.setItem('s22_user', s.user); }",
                new { token = sesi.Token, user = sesi.UserJson });
            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load });

            var muatAwal = await UkurAsync<MuatAwal>(page, SkripMuatAwal);
            await page.WaitForSelectorAsync("aside", new PageWaitForSelectorOptions { Timeout = 15000 });

            _tabSekarang = muatAwal.Tab;

            // Mode cek fungsional: tombol "tampilkan berikutnya" harus benar-benar menambah baris
            // tanpa menggeser baris pertama, dan tanpa galat konsol.
            if (opsi.CekTombol)
                return await CekTombolAsync(page, cdp, galatKonsol);

            // ── Pemanasan: satu putaran penuh tidak dicatat ────────────────────
            // Dasbor dilewati karena ia tab pertama yang sudah terbuka: klik pada menu yang sama
            // tidak memicu muat ulang, jadi tidak ada "perpindahan" yang bisa diukur.
            foreach (var menu in MenuDaftar.Reverse().Where(m => m.Id != "dashboard"))
                await KlikDanTungguAsync(page, cdp, menu);

            // ── Putaran terukur ────────────────────────────────────────────────
            var hasil = new List<HasilNav>();
            for (var putaran = 1; putaran <= opsi.Putaran; putaran++)
            {
                foreach (var menu in MenuDaftar)
                {
                    var hasilNav = await KlikDanTungguAsync(page, cdp, menu);
                    hasilNav.Putaran = putaran;
                    hasil.Add(hasilNav);
                }
            }

            // ── Soak: perpindahan berulang, memantau kebocoran memori dan galat ─
            var soak = new Soak { Jumlah = opsi.Soak };
            if (opsi.Soak > 0)
            {
                var acak = new Random(opsi.Benih);
                var sebelum = await MetrikAsync(cdp);
                soak.HeapAwal = sebelum.GetValueOrDefault("JSHeapUsedSize", 0);
                var waktu = new List<double>();
                var terakhir = string.Empty;
                for (var i = 0; i < opsi.Soak; i++)
                {
                    var menu = MenuDaftar[acak.Next(MenuDaftar.Length)];
                    if (menu.Id == terakhir) { i--; continue; }   // tab yang sama tidak memicu muat ulang
                    terakhir = menu.Id;
                    var nav = await KlikDanTungguAsync(page, cdp, menu);
                    waktu.Add(nav.MsSiap);
                    if (nav.Reqs.Any(r => r.Metode is not ("GET" or "HEAD"))) soak.PermintaanUbah++;
                    if (nav.Reqs.Any(r => r.Status >= 400)) soak.PermintaanGagal++;
                }
                var sesudah = await MetrikAsync(cdp);
                soak.HeapAkhir = sesudah.GetValueOrDefault("JSHeapUsedSize", 0);
                soak.Waktu = waktu;
                soak.P50 = Persentil(waktu, 50);
                soak.P95 = Persentil(waktu, 95);
                soak.Maks = waktu.Count == 0 ? 0 : waktu.Max();
            }

            var laporan = new Laporan
            {
                Label = opsi.Label,
                BaseUrl = url,
                BasisData = opsi.Db,
                BasisDataByte = opsi.Db is not null && File.Exists(opsi.Db) ? new FileInfo(opsi.Db).Length : 0,
                Dibuat = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                MuatAwal = muatAwal,
                Hasil = hasil,
                Soak = soak,
                GalatKonsol = galatKonsol,
                GagalJaringan = gagalJaringan,
                Ringkasan = Ringkas(hasil),
                RingkasanDingin = Ringkas(hasil.Where(h => h.Putaran == 1).ToList()),
                RingkasanUlang = Ringkas(hasil.Where(h => h.Putaran > 1).ToList()),
            };

            var jalurJson = Path.Combine(opsi.FolderKeluaran, $"perf-{opsi.Label}.json");
            await File.WriteAllTextAsync(jalurJson,
                JsonSerializer.Serialize(laporan, new JsonSerializerOptions { WriteIndented = true }));

            Cetak(muatAwal, laporan, soak, galatKonsol, gagalJaringan, jalurJson);
            return galatKonsol.Count == 0 && gagalJaringan.Count == 0 ? 0 : 1;
        }
        finally
        {
            if (browser is not null) await browser.CloseAsync();
            playwright?.Dispose();
            Hentikan(host);
        }
    }

    // Playwright .NET gagal mengubah nilai balik yang memuat null menjadi objek bertipe, jadi
    // hasil ukur dibaca sebagai JsonElement lalu diurai sendiri.
    private static readonly JsonSerializerOptions JsonOpsi = new() { PropertyNameCaseInsensitive = true };

    private static async Task<T> UkurAsync<T>(IPage page, string skrip, object? argumen = null)
    {
        var json = await page.EvaluateAsync<JsonElement>(skrip, argumen);
        return JsonSerializer.Deserialize<T>(json.GetRawText(), JsonOpsi)
            ?? throw new InvalidOperationException($"Hasil ukur kosong untuk {typeof(T).Name}.");
    }

    private static string _tabSekarang = "";
    private static bool _ambilTangkapan;

    // Klik semua tombol "… berikutnya" yang sedang tampil, lalu bandingkan jumlah baris dan baris
    // pertamanya. Baris pertama yang bergeser berarti data hilang atau urutannya berubah.
    private const string SkripCekTombol = """
        async () => {
          const duaRaf = () => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
          const tombolSemua = () => [...document.querySelectorAll('main button')]
            .filter((b) => (b.innerText || '').toLowerCase().includes('berikutnya'));
          const baris = () => [...document.querySelectorAll('main tbody tr')];
          const tombol = tombolSemua();
          const sebelum = baris().length;
          const pertama = (baris()[0]?.innerText || '').trim().slice(0, 160);
          const label = tombol.map((b) => (b.innerText || '').trim().slice(0, 90));
          for (const b of tombol) b.click();
          await duaRaf();
          return {
            tombol: tombol.length,
            sebelum,
            sesudah: baris().length,
            pertama,
            pertamaSesudah: (baris()[0]?.innerText || '').trim().slice(0, 160),
            sisaTombol: tombolSemua().length,
            label,
          };
        }
        """;

    private static async Task<int> CekTombolAsync(IPage page, ICDPSession cdp, List<string> galatKonsol)
    {
        string[] diperiksa = ["inventory_locations", "stock_catalog", "stock_opname", "payables_receivables", "orders"];
        var gagal = 0;
        var totalKlik = 0;
        foreach (var id in diperiksa)
        {
            var menu = MenuDaftar.FirstOrDefault(m => m.Id == id)
                ?? throw new InvalidOperationException($"Menu '{id}' tidak ada di daftar ukur.");
            await KlikDanTungguAsync(page, cdp, menu);
            var rondeTerpakai = 0;
            for (var ronde = 1; ronde <= 3; ronde++)
            {
                var r = await UkurAsync<CekTombol>(page, SkripCekTombol);
                if (r.Tombol == 0) break;
                rondeTerpakai = ronde;
                totalKlik += r.Tombol;
                var bertambah = r.Sesudah > r.Sebelum;
                var tetap = r.Pertama == r.PertamaSesudah;
                if (!bertambah || !tetap) gagal++;
                Console.WriteLine($"[{(bertambah && tetap ? "PASS" : "FAIL")}] {id} ronde {ronde}: " +
                                  $"tombol {r.Tombol}, baris {r.Sebelum} -> {r.Sesudah}, " +
                                  $"baris pertama {(tetap ? "tetap" : "BERGESER")}, sisa tombol {r.SisaTombol}");
            }
            if (rondeTerpakai == 0)
                Console.WriteLine($"[INFO] {id}: tidak ada tombol bertahap yang tampil.");
        }
        if (galatKonsol.Count > 0) gagal += galatKonsol.Count;
        Console.WriteLine($"Cek tombol: {diperiksa.Length} menu, {totalKlik} klik, {gagal} gagal, " +
                          $"{galatKonsol.Count} galat konsol.");
        return gagal == 0 ? 0 : 1;
    }

    private static async Task<HasilNav> KlikDanTungguAsync(IPage page, ICDPSession cdp, Menu menu)
    {
        if (menu.Id == _tabSekarang)
            throw new InvalidOperationException(
                $"Menu '{menu.Id}' sudah terbuka; klik pada menu yang sama tidak memuat ulang apa pun.");

        // Metrik peramban diambil tepat sebelum klik: selisihnya menunjukkan pekerjaan utama
        // (skrip, tata letak, gaya) yang dilakukan satu perpindahan menu.
        var sebelum = await MetrikAsync(cdp);

        var tombol = page.Locator($"aside button:has-text('{menu.Label}')").First;
        await tombol.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await tombol.ClickAsync();
        var ukuran = await UkurAsync<HasilNav>(page, SkripTungguSiap, menu.Id);
        var sesudah = await MetrikAsync(cdp);
        if (_ambilTangkapan)
        {
            var folder = Path.Combine("C:", "tmp", "perf", "tangkapan");
            Directory.CreateDirectory(folder);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(folder, menu.Id + ".png"), FullPage = false,
            });
        }

        _tabSekarang = menu.Id;
        ukuran.Menu = menu.Id;
        ukuran.Label = menu.Label;
        ukuran.Penanda = (await page.EvaluateAsync<JsonElement>(SkripPenanda, menu.Penanda)).GetBoolean();
        ukuran.Heap = sesudah.GetValueOrDefault("JSHeapUsedSize", 0);
        ukuran.Layout = (int)Selisih(sesudah, sebelum, "LayoutCount");
        ukuran.RecalcStyle = (int)Selisih(sesudah, sebelum, "RecalcStyleCount");
        ukuran.LayoutMs = Math.Round(Selisih(sesudah, sebelum, "LayoutDuration") * 1000, 1);
        ukuran.RecalcMs = Math.Round(Selisih(sesudah, sebelum, "RecalcStyleDuration") * 1000, 1);
        ukuran.ScriptMs = Math.Round(Selisih(sesudah, sebelum, "ScriptDuration") * 1000, 1);
        ukuran.TaskMs = Math.Round(Selisih(sesudah, sebelum, "TaskDuration") * 1000, 1);
        return ukuran;
    }

    private static double Selisih(Dictionary<string, double> sesudah, Dictionary<string, double> sebelum, string nama) =>
        sesudah.GetValueOrDefault(nama, 0) - sebelum.GetValueOrDefault(nama, 0);

    private static async Task<Dictionary<string, double>> MetrikAsync(ICDPSession cdp)
    {
        var kamus = new Dictionary<string, double>();
        var hasil = await cdp.SendAsync("Performance.getMetrics");
        if (hasil is not { } json || !json.TryGetProperty("metrics", out var daftar)) return kamus;
        foreach (var m in daftar.EnumerateArray())
        {
            var nama = m.GetProperty("name").GetString();
            if (nama is not null) kamus[nama] = m.GetProperty("value").GetDouble();
        }
        return kamus;
    }

    // ── Host uji ─────────────────────────────────────────────────────────────

    private static async Task<(string Url, Process Proses)> JalankanHostAsync(Opsi opsi)
    {
        var akar = AkarRepositori();
        var port = PortBebas();
        var url = $"http://127.0.0.1:{port}";
        var kunci = Path.Combine(opsi.FolderKeluaran, $"jwt-{opsi.Label}.key");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(Path.Combine(akar, "src", "Host.Service", "bin", "Release", "net8.0",
            "Seruni22Bakery.Host.dll"));
        if (opsi.Db is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(opsi.Db))!);
            psi.ArgumentList.Add($"--Database:Path={Path.GetFullPath(opsi.Db)}");
        }
        psi.ArgumentList.Add($"--Desktop:StaticFilesPath={Path.Combine(akar, "desktop")}");
        psi.ArgumentList.Add($"--Jwt:SigningKeyFile={kunci}");
        psi.ArgumentList.Add($"--urls={url}");

        var proses = Process.Start(psi) ?? throw new InvalidOperationException("Host uji gagal dijalankan.");
        var log = new System.Text.StringBuilder();
        proses.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        proses.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.AppendLine(e.Data); };
        proses.BeginOutputReadLine();
        proses.BeginErrorReadLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var tenggat = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < tenggat)
        {
            if (proses.HasExited)
                throw new InvalidOperationException($"Host uji berhenti (exit {proses.ExitCode}). Log:\n{log}");
            try
            {
                if ((await http.GetAsync(url + "/health")).IsSuccessStatusCode) return (url, proses);
            }
            catch { /* belum mendengarkan */ }
            await Task.Delay(250);
        }
        throw new InvalidOperationException($"Host uji tidak siap di {url}. Log:\n{log}");
    }

    private static void Hentikan(Process? proses)
    {
        if (proses is null) return;
        try
        {
            if (!proses.HasExited) proses.Kill(entireProcessTree: true);
            proses.WaitForExit(5000);
        }
        catch { /* sudah mati */ }
        finally { proses.Dispose(); }
    }

    private static int PortBebas()
    {
        var pendengar = new TcpListener(IPAddress.Loopback, 0);
        pendengar.Start();
        var port = ((IPEndPoint)pendengar.LocalEndpoint).Port;
        pendengar.Stop();
        return port;
    }

    private static string AkarRepositori()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BakeryDesktop.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("BakeryDesktop.slnx tidak ditemukan di atas direktori perkakas.");
    }

    // ── Login ────────────────────────────────────────────────────────────────

    private sealed record Sesi(string Token, string UserJson);

    private static async Task<Sesi> MasukAsync(string url, Opsi opsi, bool hostMilikSendiri)
    {
        using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(10) };

        var kandidat = opsi.KataSandi is not null
            ? [opsi.KataSandi]
            : new[] { "Skala2026", "SandiTesUI2026", "admin123" };

        JsonElement? sesi = null;
        string? sandiDipakai = null;
        foreach (var sandi in kandidat)
        {
            var respons = await http.PostAsJsonAsync("/api/auth/login", new { username = opsi.Pengguna, password = sandi });
            if (!respons.IsSuccessStatusCode) continue;
            using var dok = JsonDocument.Parse(await respons.Content.ReadAsStringAsync());
            sesi = dok.RootElement.Clone();
            sandiDipakai = sandi;
            break;
        }
        if (sesi is null)
            throw new InvalidOperationException(
                $"Tidak ada kata sandi yang diterima untuk '{opsi.Pengguna}' di {url}. " +
                "Sebutkan lewat --password bila memakai basis data sendiri.");

        var akun = sesi.Value;
        if (akun.GetProperty("mustChangePassword").GetBoolean())
        {
            if (!hostMilikSendiri)
                throw new InvalidOperationException(
                    "Akun ini masih wajib ganti kata sandi dan host bukan milik perkakas; ganti sandinya di luar pengukuran.");
            const string baru = "SandiPerf2026";
            using var ganti = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
            {
                Content = JsonContent.Create(new { currentPassword = sandiDipakai, newPassword = baru }),
            };
            ganti.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", akun.GetProperty("token").GetString());
            var jawab = await http.SendAsync(ganti);
            if (!jawab.IsSuccessStatusCode)
                throw new InvalidOperationException($"Ganti kata sandi awal gagal: {await jawab.Content.ReadAsStringAsync()}");

            var ulang = await http.PostAsJsonAsync("/api/auth/login", new { username = opsi.Pengguna, password = baru });
            using var dok = JsonDocument.Parse(await ulang.Content.ReadAsStringAsync());
            akun = dok.RootElement.Clone();
        }

        var peran = akun.GetProperty("role");
        var namaPeran = peran.ValueKind == JsonValueKind.Number
            ? NamaPeran[peran.GetInt32().ToString()]
            : peran.GetString() ?? "Owner";

        var userJson = JsonSerializer.Serialize(new
        {
            id = akun.GetProperty("userId").GetInt32(),
            username = akun.GetProperty("username").GetString(),
            role = namaPeran,
            capabilities = akun.TryGetProperty("capabilities", out var cap) ? cap : default,
            mustChangePassword = akun.GetProperty("mustChangePassword").GetBoolean(),
        });

        return new Sesi(akun.GetProperty("token").GetString()!, userJson);
    }

    // ── Ringkasan ────────────────────────────────────────────────────────────

    private static List<RingkasanMenu> Ringkas(List<HasilNav> hasil) =>
        hasil.GroupBy(h => h.Menu)
            .Select(g =>
            {
                var waktu = g.Select(x => x.MsSiap).OrderBy(x => x).ToList();
                var req = g.Select(x => x.Reqs.Count).OrderBy(x => x).ToList();
                var peladen = g.SelectMany(x => x.Reqs).Where(r => r.DurasiPeladen.HasValue)
                    .Select(r => r.DurasiPeladen!.Value).OrderBy(x => x).ToList();
                var skrip = g.Select(x => x.ScriptMs).OrderBy(x => x).ToList();
                var tata = g.Select(x => x.LayoutMs + x.RecalcMs).OrderBy(x => x).ToList();
                var data = g.Select(x => x.MsData).OrderBy(x => x).ToList();
                return new RingkasanMenu
                {
                    Menu = g.Key,
                    Label = g.First().Label,
                    N = waktu.Count,
                    P50 = Persentil(waktu, 50),
                    P95 = Persentil(waktu, 95),
                    Maks = waktu.Count == 0 ? 0 : waktu[^1],
                    DataP50 = Persentil(data, 50),
                    SkripP50 = Persentil(skrip, 50),
                    TataP50 = Persentil(tata, 50),
                    Permintaan = req.Count == 0 ? 0 : req[req.Count / 2],
                    PeladenP50 = peladen.Count == 0 ? null : Persentil(peladen, 50),
                    PeladenJumlah = peladen.Count,
                    PenandaCocok = g.All(x => x.Penanda),
                };
            })
            .OrderByDescending(r => r.P95)
            .ToList();

    private static double Persentil(List<double> nilai, int p)
    {
        if (nilai.Count == 0) return 0;
        var urut = nilai.OrderBy(x => x).ToList();
        var posisi = (int)Math.Ceiling(p / 100.0 * urut.Count) - 1;
        return Math.Round(urut[Math.Clamp(posisi, 0, urut.Count - 1)], 1);
    }

    private static void Cetak(MuatAwal awal, Laporan laporan, Soak soak,
        List<string> galatKonsol, List<string> gagalJaringan, string jalurJson)
    {
        Console.WriteLine();
        Console.WriteLine($"Muat awal   : siap {awal.MsSiap} ms (data {awal.MsData} ms), DOMContentLoaded {awal.Dcl} ms, load {awal.Load} ms, tab '{awal.Tab}'");
        Console.WriteLine($"Permintaan  : {awal.Reqs.Count} pada muat awal");
        CetakTabel("Kunjungan pertama (dingin)", laporan.RingkasanDingin);
        CetakTabel("Kunjungan ulang", laporan.RingkasanUlang);

        if (soak.Jumlah > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Soak {soak.Jumlah}x pindah menu: p50 {soak.P50:0.0} ms, p95 {soak.P95:0.0} ms, maks {soak.Maks:0.0} ms");
            Console.WriteLine($"Heap JS {soak.HeapAwal / 1048576.0:0.0} MB -> {soak.HeapAkhir / 1048576.0:0.0} MB " +
                              $"(selisih {(soak.HeapAkhir - soak.HeapAwal) / 1048576.0:+0.0;-0.0;0.0} MB)");
            Console.WriteLine($"Permintaan ubah data saat soak: {soak.PermintaanUbah}, jawaban gagal: {soak.PermintaanGagal}");
        }

        Console.WriteLine();
        Console.WriteLine($"Galat konsol: {galatKonsol.Count}, jawaban HTTP gagal: {gagalJaringan.Count}");
        foreach (var g in galatKonsol.Take(5)) Console.WriteLine("  " + g);
        foreach (var g in gagalJaringan.Take(5)) Console.WriteLine("  " + g);
        Console.WriteLine($"Hasil JSON  : {jalurJson}");
    }

    private static void CetakTabel(string judul, List<RingkasanMenu> ringkasan)
    {
        if (ringkasan.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine($"── {judul} ──");
        Console.WriteLine(string.Format("{0,-22} {1,3} {2,7} {3,7} {4,7} {5,7} {6,7} {7,4} {8,9}  {9}",
            "menu", "n", "p50", "p95", "maks", "data", "skrip", "req", "peladen", "penanda"));
        foreach (var r in ringkasan)
        {
            var peladen = r.PeladenP50.HasValue ? r.PeladenP50.Value.ToString("0.0") : "-";
            Console.WriteLine($"{r.Menu,-22} {r.N,3} {r.P50,7:0.0} {r.P95,7:0.0} {r.Maks,7:0.0} " +
                              $"{r.DataP50,7:0.0} {r.SkripP50,7:0.0} {r.Permintaan,4} {peladen,9}  {(r.PenandaCocok ? "ok" : "TIDAK COCOK")}");
        }
    }

    // ── DTO ──────────────────────────────────────────────────────────────────

    private sealed class HasilNav
    {
        public string Menu { get; set; } = "";
        public string Label { get; set; } = "";
        public int Putaran { get; set; }
        public double MsSiap { get; set; }
        public double MsData { get; set; }
        public bool Penanda { get; set; }
        public double Heap { get; set; }
        public int Layout { get; set; }
        public int RecalcStyle { get; set; }
        public double LayoutMs { get; set; }
        public double RecalcMs { get; set; }
        public double ScriptMs { get; set; }
        public double TaskMs { get; set; }
        public int Nodes { get; set; }
        public int Rows { get; set; }
        public List<PermintaanNav> Reqs { get; set; } = [];
        [JsonIgnore] public double T0 { get; set; }
    }

    private sealed class PermintaanNav
    {
        public string Jalur { get; set; } = "";
        public string Metode { get; set; } = "";
        public int Status { get; set; }
        public double Mulai { get; set; }
        public double Ms { get; set; }
        public double? DurasiPeladen { get; set; }
    }

    private sealed class MuatAwal
    {
        public string Tab { get; set; } = "";
        public double MsSiap { get; set; }
        public double MsData { get; set; }
        public double Dcl { get; set; }
        public double Load { get; set; }
        public double Heap { get; set; }
        public List<PermintaanNav> Reqs { get; set; } = [];
    }

    private sealed class Soak
    {
        public int Jumlah { get; set; }
        public double P50 { get; set; }
        public double P95 { get; set; }
        public double Maks { get; set; }
        public double HeapAwal { get; set; }
        public double HeapAkhir { get; set; }
        public int PermintaanUbah { get; set; }
        public int PermintaanGagal { get; set; }
        public List<double> Waktu { get; set; } = [];
    }

    private sealed class CekTombol
    {
        public int Tombol { get; set; }
        public int Sebelum { get; set; }
        public int Sesudah { get; set; }
        public string Pertama { get; set; } = "";
        public string PertamaSesudah { get; set; } = "";
        public int SisaTombol { get; set; }
        public List<string> Label { get; set; } = [];
    }

    private sealed class RingkasanMenu
    {
        public string Menu { get; set; } = "";
        public string Label { get; set; } = "";
        public int N { get; set; }
        public double P50 { get; set; }
        public double P95 { get; set; }
        public double Maks { get; set; }
        public double DataP50 { get; set; }
        public double SkripP50 { get; set; }
        public double TataP50 { get; set; }
        public int Permintaan { get; set; }
        public double? PeladenP50 { get; set; }
        public int PeladenJumlah { get; set; }
        public bool PenandaCocok { get; set; }
    }

    private sealed class Laporan
    {
        public string Label { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string? BasisData { get; set; }
        public long BasisDataByte { get; set; }
        public string Dibuat { get; set; } = "";
        public MuatAwal MuatAwal { get; set; } = new();
        public List<HasilNav> Hasil { get; set; } = [];
        public Soak Soak { get; set; } = new();
        public List<string> GalatKonsol { get; set; } = [];
        public List<string> GagalJaringan { get; set; } = [];
        public List<RingkasanMenu> Ringkasan { get; set; } = [];
        public List<RingkasanMenu> RingkasanDingin { get; set; } = [];
        public List<RingkasanMenu> RingkasanUlang { get; set; } = [];
    }

    // ── Argumen ──────────────────────────────────────────────────────────────

    private sealed class Opsi
    {
        public string Label { get; private set; } = "kecil";
        public string? Db { get; private set; }
        public string? Url { get; private set; }
        public string? KataSandi { get; private set; }
        public string Pengguna { get; private set; } = "admin";
        public int Putaran { get; private set; } = 5;
        public int Soak { get; private set; } = 100;
        public int Benih { get; private set; } = 20261029;
        public string FolderKeluaran { get; private set; } = Path.Combine("C:", "tmp", "perf", "hasil");
        public bool AmbilTangkapan { get; private set; }
        public bool CekTombol { get; private set; }

        /// <summary>
        /// Basis data kosong milik perkakas ini sendiri. Host membuat dan memigrasikannya saat
        /// mulai, jadi angkanya mewakili pemasangan baru — bukan basis data pemakaian.
        /// </summary>
        public void PakaiBasisDataSementara() =>
            Db = Path.Combine(FolderKeluaran, $"kecil-{DateTime.Now:yyyyMMdd-HHmmss}.db");

        public static Opsi Baca(string[] args)
        {
            var opsi = new Opsi();
            for (var i = 0; i < args.Length; i++)
            {
                string? Nilai() => i + 1 < args.Length ? args[++i] : null;
                switch (args[i])
                {
                    case "--label": opsi.Label = Nilai() ?? opsi.Label; break;
                    case "--db": opsi.Db = Nilai(); break;
                    case "--url": opsi.Url = Nilai(); break;
                    case "--password": opsi.KataSandi = Nilai(); break;
                    case "--user": opsi.Pengguna = Nilai() ?? opsi.Pengguna; break;
                    case "--rounds": opsi.Putaran = int.Parse(Nilai() ?? "5"); break;
                    case "--soak": opsi.Soak = int.Parse(Nilai() ?? "100"); break;
                    case "--seed": opsi.Benih = int.Parse(Nilai() ?? "20261029"); break;
                    case "--out": opsi.FolderKeluaran = Nilai() ?? opsi.FolderKeluaran; break;
                    case "--shots": opsi.AmbilTangkapan = true; break;
                    case "--cek-tombol": opsi.CekTombol = true; break;
                    default: throw new ArgumentException($"Argumen tidak dikenal: {args[i]}");
                }
            }
            return opsi;
        }
    }
}
