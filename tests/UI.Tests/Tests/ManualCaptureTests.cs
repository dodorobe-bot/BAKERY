using System.Net.Http.Json;
using System.Text.Json;
using BakeryDesktop.UI.Tests.Helpers;
using Microsoft.Playwright;

namespace BakeryDesktop.UI.Tests.Tests;

/// <summary>
/// Menghasilkan gambar untuk buku manual pemakaian.
///
/// Tes ini bukan pagar mutu: ia tidak memeriksa perilaku, hanya merekam layar yang sudah
/// diisi contoh data. Karena itu ia hanya berjalan bila diminta lewat
/// SERUNI_CAPTURE_MANUAL=1, dan menolak menyentuh host yang bukan milik TestHost — data usaha
/// asli tidak boleh berubah hanya karena buku manual dibuat ulang.
/// </summary>
[Collection("UI Tests")]
public sealed class ManualCaptureTests : IClassFixture<PlaywrightFixture>
{
    private readonly PlaywrightFixture _fixture;

    public ManualCaptureTests(PlaywrightFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tangkap_Layar_Manual()
    {
        if (Environment.GetEnvironmentVariable("SERUNI_CAPTURE_MANUAL") != "1") return;

        var baseUrl = _fixture.BaseUrl;
        if (!TestHost.Manages(baseUrl))
            throw new InvalidOperationException(
                "Tangkapan manual hanya boleh memakai basis data sementara milik TestHost. " +
                "Jalankan tanpa BAKERY_TEST_URL.");

        var page = _fixture.Page;
        await page.SetViewportSizeAsync(1440, 900);

        var folder = Path.Combine(TestHost.RepositoryRoot, "docs", "manual", "gambar");
        Directory.CreateDirectory(folder);

        // Layar masuk direkam sebelum ada sesi di localStorage.
        await page.GotoAsync(baseUrl);
        await page.EvaluateAsync("() => localStorage.clear()");
        await page.ReloadAsync();
        await page.GetByText("Nama Pengguna / ID Pegawai", new() { Exact = true }).WaitForAsync();
        await Tangkap(page, folder, "login");

        using var http = await AppLogin.AuthorizedClientAsync(baseUrl);
        var data = await IsiContohAsync(http, page, baseUrl);

        // Gerbang ganti sandi wajib: akun baru dari layar Hak Akses selalu berangkat dengan
        // MustChangePassword=1, jadi overlay-nya bisa direkam tanpa mengubah akun pemilik.
        await page.EvaluateAsync("() => localStorage.clear()");
        await page.GotoAsync(baseUrl);
        await page.Locator("input").Nth(0).FillAsync(data.KasirUsername);
        await page.Locator("input[type='password']").FillAsync(data.KasirPassword);
        await page.GetByText("Masuk ke Sistem", new() { Exact = true }).ClickAsync();
        await page.GetByText("Ganti Kata Sandi", new() { Exact = true }).WaitForAsync(
            new() { Timeout = 15_000 });
        await Tangkap(page, folder, "login-ganti-sandi");

        await page.EvaluateAsync("() => localStorage.clear()");
        await AppLogin.SignInAsync(page, baseUrl);
        await page.SetViewportSizeAsync(1440, 900);

        // ── Ruang kerja ────────────────────────────────────────────────────
        await Buka(page, "Dasbor & Indikator Utama", "Penerimaan Kasir Hari Ini");
        await Tangkap(page, folder, "dashboard");

        await Buka(page, "Kotak Tugas & Persetujuan", "Kotak Tugas & Persetujuan");
        await Tangkap(page, folder, "tasks");

        await Buka(page, "Pemberitahuan & Catatan Aktivitas", "Notifikasi Internal");
        await Tangkap(page, folder, "notifications");

        // ── Penjualan & pelanggan ──────────────────────────────────────────
        await Buka(page, "Kasir Penjualan Cepat", "Kasir Penjualan Cepat");
        await Tangkap(page, folder, "pos");

        // Formulir ini lebih tinggi dari jendela 900 px, jadi jendela ditinggikan dulu
        // supaya tombol simpan ikut terlihat di gambar manual.
        await page.SetViewportSizeAsync(1440, 1200);
        await page.GetByText("Tambah Transaksi / Pesanan", new() { Exact = true }).ClickAsync();
        await page.Locator("h3:has-text('Tambah Transaksi / Pesanan Baru')").WaitForAsync();
        await Tangkap(page, folder, "pos-form");
        await page.SetViewportSizeAsync(1440, 900);
        await TutupDialogAsync(page);

        await Buka(page, "Daftar Pesanan & Penjualan", "Daftar Pesanan & Penjualan");
        await Tangkap(page, folder, "orders");

        await Buka(page, "Pesanan Terjadwal (Pre-Order)", "Pesanan Terjadwal (Pre-Order)");
        await Tangkap(page, folder, "preorders");

        await Buka(page, "Data Pelanggan & Klien", "Data Pelanggan & Klien");
        await Tangkap(page, folder, "customers");

        await Buka(page, "Piutang & Pembayaran", "Piutang & Pembayaran");
        await Tangkap(page, folder, "receivables");

        // Dialog pelunasan dibuka dari baris yang masih punya sisa bayar.
        await page.Locator("main button:has-text('Bayar')").First.ClickAsync();
        await page.Locator("h3:has-text('Catat Pembayaran')").WaitForAsync();
        await Tangkap(page, folder, "receivables-form");
        await TutupDialogAsync(page);

        await Buka(page, "Retur Penjualan", "Retur Penjualan");
        await Tangkap(page, folder, "sales_returns");

        // ── Operasional & produksi ─────────────────────────────────────────
        await Buka(page, "Perencanaan Jadwal Dapur", "Perencanaan Jadwal Dapur");
        await Tangkap(page, folder, "production_plan");

        await Buka(page, "Perintah Kerja (SPK)", "Perintah Kerja (SPK)");
        await Tangkap(page, folder, "production_orders");

        await page.GetByText("Buat SPK Produksi Baru", new() { Exact = true }).ClickAsync();
        await page.Locator("h3:has-text('Buat SPK Produksi Baru')").WaitForAsync();
        await Tangkap(page, folder, "production_orders-form");
        await TutupDialogAsync(page);

        await Buka(page, "Perintah Kerja (SPK)", "Perintah Kerja (SPK)");
        await page.Locator("main button:has-text('Selesaikan')").First.ClickAsync();
        await page.Locator("h3:has-text('Penyelesaian SPK')").WaitForAsync();
        await Tangkap(page, folder, "production_orders-complete");
        await TutupDialogAsync(page);

        await Buka(page, "Resep & Daftar Bahan Baku", "Resep & BOM Produk");
        await Tangkap(page, folder, "recipes_bom");

        await Buka(page, "Pemakaian Aktual & Hasil", "Pemakaian Aktual & Hasil");
        await Tangkap(page, folder, "production_actual");

        await Buka(page, "Pemeriksaan Mutu (QC)", "Pemeriksaan Mutu (QC)");
        await Tangkap(page, folder, "quality_control");

        await Buka(page, "Harga Pokok Produksi (HPP)", "Harga Pokok Produksi (HPP)");
        await Tangkap(page, folder, "cost_calc");

        // ── Rantai pasok & persediaan ──────────────────────────────────────
        await Buka(page, "Pembelian & Penerimaan Barang", "Daftar Purchase Order");
        await Tangkap(page, folder, "purchases");

        await page.GetByText("Buat PO Baru", new() { Exact = true }).ClickAsync();
        await page.Locator("h3:has-text('Buat Purchase Order Baru')").WaitForAsync();
        await Tangkap(page, folder, "purchases-form");
        await TutupDialogAsync(page);

        await Buka(page, "Pembelian & Penerimaan Barang", "Daftar Purchase Order");
        await page.Locator("main button:has-text('Terima')").First.ClickAsync();
        await page.Locator("h3:has-text('Terima Barang')").WaitForAsync();
        await Tangkap(page, folder, "purchases-receive");
        await TutupDialogAsync(page);

        await Buka(page, "Pemasok & Rekanan", "Daftar Pemasok");
        await Tangkap(page, folder, "suppliers");

        await Buka(page, "Gudang & Lokasi Rak", "Stok per Lokasi");
        await Tangkap(page, folder, "inventory_locations");

        await Buka(page, "Mutasi Gudang ke Dapur", "Mutasi Gudang → Dapur");
        await Tangkap(page, folder, "kitchen_transfer");

        await page.GetByText("Kirim Bahan", new() { Exact = true }).ClickAsync();
        await page.Locator("h3:has-text('Formulir Mutasi Bahan')").WaitForAsync();
        await Tangkap(page, folder, "kitchen_transfer-form");
        await TutupDialogAsync(page);

        await Buka(page, "Katalog Bahan & Produk Jadi", "Katalog Bahan & Produk Jadi");
        await Tangkap(page, folder, "stock_catalog");

        await Buka(page, "Pemeriksaan Fisik (Stok Opname)", "Pemeriksaan Fisik (Stok Opname)");
        await Tangkap(page, folder, "stock_opname");

        await Buka(page, "Nomor Batch & Kedaluwarsa", "Nomor Batch & Kedaluwarsa");
        await Tangkap(page, folder, "batch_expiry");

        // ── Keuangan & akuntansi ───────────────────────────────────────────
        await Buka(page, "Buku Besar & Jurnal Umum", "Buku Besar & Jurnal Umum");
        await Tangkap(page, folder, "general_ledger");

        await Buka(page, "Kas Toko & Rekening Bank", "Kas Toko & Rekening Bank");
        await Tangkap(page, folder, "cash_bank");

        await Buka(page, "Utang & Piutang Usaha", "Utang & Piutang Usaha");
        await Tangkap(page, folder, "payables_receivables");

        await Buka(page, "Laporan Keuangan & Laba Rugi", "Laporan Keuangan & Laba Rugi");
        await Tangkap(page, folder, "financial_reports");

        // ── Analisis & pengaturan ──────────────────────────────────────────
        await Buka(page, "Analisis & Tren Penjualan", "Analisis & Tren Penjualan");
        await Tangkap(page, folder, "executive_analytics");

        await Buka(page, "Jejak Audit & Riwayat Sistem", "Jejak Audit & Riwayat Sistem");
        await Tangkap(page, folder, "audit_log");

        await Buka(page, "Hak Akses & Pegawai", "Hak Akses & Pegawai");
        await Tangkap(page, folder, "user_management");

        await page.GetByText("Tambah Pegawai", new() { Exact = true }).First.ClickAsync();
        await page.Locator("h3:has-text('Tambah Pegawai')").WaitForAsync();
        await Tangkap(page, folder, "user_management-form");
        await TutupDialogAsync(page);

        await Buka(page, "Perangkat Kasir", "Perangkat Kasir");
        await Tangkap(page, folder, "device_settings");

        await Buka(page, "Pengaturan Toko & Cabang", "Pengaturan Toko & Cabang");
        await Tangkap(page, folder, "settings");

        var jumlah = Directory.GetFiles(folder, "*.png").Length;
        Console.WriteLine($"[manual] {jumlah} gambar tersimpan di {folder}");
    }

    /// <summary>
    /// Contoh data untuk buku: satu hari kerja toko roti — bahan, resep, produksi, pembelian,
    /// pesanan, retur, biaya, dan pegawai. Semuanya ditulis lewat endpoint produksi, bukan SQL,
    /// supaya gambar yang direkam memperlihatkan aplikasi yang benar-benar berjalan.
    /// </summary>
    private static async Task<DataContoh> IsiContohAsync(HttpClient http, IPage page, string baseUrl)
    {
        await Put(http, "/api/settings", new Dictionary<string, string>
        {
            ["brand_name"] = "Seruni 22 Bakery",
            ["brand_tagline"] = "Roti & kue segar setiap hari",
            ["outlet_name"] = "Toko Utama — Jl. Seruni No. 22",
            ["timezone"] = "Asia/Jakarta",
        });

        var bahan = new Dictionary<string, (int Id, string Unit)>();
        foreach (var b in new[]
        {
            ("Tepung Terigu Cakra", "Bogasari", "kg", 1_400_000L, 100.0, 15.0),
            ("Gula Pasir", "Gulaku", "kg", 1_600_000L, 30.0, 5.0),
            ("Mentega Tawar", "Anchor", "kg", 3_200_000L, 25.0, 4.0),
            ("Ragi Instan", "Fermipan", "kg", 9_000_000L, 5.0, 0.5),
            ("Telur Ayam", "Cisarua", "kg", 2_800_000L, 20.0, 3.0),
            ("Susu Bubuk", "Indomilk", "kg", 8_500_000L, 8.0, 1.0),
            ("Cokelat Compound", "Beryls", "kg", 6_500_000L, 10.0, 2.0),
            ("Keju Cheddar", "Prochiz", "kg", 12_000_000L, 6.0, 1.0),
        })
        {
            var id = (await Post(http, "/api/ingredients", new
            {
                name = b.Item1,
                brand = b.Item2,
                unit = b.Item3,
                pricePerUnit = b.Item4,
                currentStock = b.Item5,
                minStockAlert = b.Item6,
            })).GetProperty("id").GetInt32();
            bahan[b.Item1] = (id, b.Item3);
        }

        var produk = new Dictionary<string, int>();
        foreach (var p in new[]
        {
            ("Roti Tawar Susu", "Roti", 2_500_000L, new[]
            {
                ("Tepung Terigu Cakra", 0.30), ("Gula Pasir", 0.03), ("Mentega Tawar", 0.02),
                ("Ragi Instan", 0.004), ("Susu Bubuk", 0.01), ("Telur Ayam", 0.02),
            }),
            ("Roti Cokelat", "Roti", 1_200_000L, new[]
            {
                ("Tepung Terigu Cakra", 0.12), ("Gula Pasir", 0.02), ("Mentega Tawar", 0.01),
                ("Ragi Instan", 0.002), ("Cokelat Compound", 0.015),
            }),
            ("Roti Keju", "Roti", 1_300_000L, new[]
            {
                ("Tepung Terigu Cakra", 0.12), ("Gula Pasir", 0.015), ("Mentega Tawar", 0.012),
                ("Ragi Instan", 0.002), ("Keju Cheddar", 0.02),
            }),
            ("Kroisan Mentega", "Kue", 1_500_000L, new[]
            {
                ("Tepung Terigu Cakra", 0.10), ("Mentega Tawar", 0.05), ("Gula Pasir", 0.01),
                ("Ragi Instan", 0.002),
            }),
            ("Kue Sus", "Kue", 800_000L, new[]
            {
                ("Tepung Terigu Cakra", 0.05), ("Mentega Tawar", 0.02), ("Telur Ayam", 0.03),
                ("Gula Pasir", 0.01),
            }),
            ("Donat Gula", "Kue", 700_000L, new[]
            {
                ("Tepung Terigu Cakra", 0.08), ("Gula Pasir", 0.02), ("Mentega Tawar", 0.01),
                ("Ragi Instan", 0.003),
            }),
        })
        {
            var id = (await Post(http, "/api/products", new
            {
                name = p.Item1,
                category = p.Item2,
                sellingPriceMinorUnits = p.Item3,
                barcode = "899" + Math.Abs(p.Item1.GetHashCode()) % 10_000_000,
                lines = p.Item4.Select(l => new { ingredientId = bahan[l.Item1].Id, quantity = l.Item2 }).ToArray(),
            })).GetProperty("id").GetInt32();
            produk[p.Item1] = id;
        }

        // Bahan untuk dua SPK yang akan diselesaikan + satu SPK rencana.
        foreach (var (nama, jumlah) in new[]
        {
            ("Tepung Terigu Cakra", 40.0), ("Gula Pasir", 12.0), ("Mentega Tawar", 10.0),
            ("Ragi Instan", 2.5), ("Telur Ayam", 8.0), ("Susu Bubuk", 4.0),
            ("Cokelat Compound", 5.0), ("Keju Cheddar", 4.0),
        })
        {
            await Post(http, "/api/inventory/transfer", new
            {
                fromLocationId = 1,
                toLocationId = 2,
                ingredientId = bahan[nama].Id,
                quantity = jumlah,
                notes = "Persiapan produksi pagi",
            });
        }

        var pemasok = new Dictionary<string, int>();
        foreach (var s in new[]
        {
            ("CV Sumber Bahan Nusantara", "Pak Hendra", "0812-1111-2200", "order@sumberbahan.co.id", "Jl. Raya Bekasi No. 45, Jakarta Timur"),
            ("PT Dairy Prima", "Bu Lestari", "0813-2222-3300", "sales@dairyprima.co.id", "Kawasan Industri Pulogadung Blok C7"),
            ("Toko Kemasan Sejahtera", "Ko Aan", "0857-8888-9900", null, "Jl. Pasar Baru No. 12, Jakarta Pusat"),
        })
        {
            var id = (await Post(http, "/api/suppliers", new
            {
                name = s.Item1,
                contactName = s.Item2,
                phone = s.Item3,
                email = s.Item4,
                address = s.Item5,
                notes = (string?)null,
            })).GetProperty("id").GetInt32();
            pemasok[s.Item1] = id;
        }

        var hariIni = DateTime.Today;
        var tepung = bahan["Tepung Terigu Cakra"];
        var mentega = bahan["Mentega Tawar"];
        var cokelat = bahan["Cokelat Compound"];

        // PO 1 — diterima sebagian, menghasilkan lot yang masih jauh dari kedaluwarsa.
        var po1 = (await Post(http, "/api/purchases", new
        {
            supplierId = pemasok["CV Sumber Bahan Nusantara"],
            expectedDate = hariIni.AddDays(-2).ToString("yyyy-MM-dd"),
            notes = "Pasokan mingguan tepung",
            lines = new[] { new { ingredientId = tepung.Id, orderedQty = 50.0, unitCostMinorUnits = 1_350_000L, unit = tepung.Unit } },
        })).GetProperty("id").GetInt32();
        var po1Detail = await Get(http, $"/api/purchases/{po1}");
        var po1Line = po1Detail.GetProperty("lines")[0];
        await Post(http, $"/api/purchases/{po1}/receive", new
        {
            notes = "Datang 30 kg, sisa menyusul",
            lines = new[]
            {
                new
                {
                    purchaseOrderLineId = po1Line.GetProperty("id").GetInt32(),
                    ingredientId = tepung.Id,
                    receivedQty = 30.0,
                    unitCostMinorUnits = 1_350_000L,
                    locationId = 1,
                    batchNumber = "TEP-2601",
                    expiryDate = hariIni.AddDays(180).ToString("yyyy-MM-dd"),
                },
            },
        });

        // PO 2 — diterima penuh.
        var po2 = (await Post(http, "/api/purchases", new
        {
            supplierId = pemasok["PT Dairy Prima"],
            expectedDate = hariIni.AddDays(-1).ToString("yyyy-MM-dd"),
            notes = "Mentega tawar untuk produksi kroisan",
            lines = new[] { new { ingredientId = mentega.Id, orderedQty = 30.0, unitCostMinorUnits = 3_000_000L, unit = mentega.Unit } },
        })).GetProperty("id").GetInt32();
        var po2Detail = await Get(http, $"/api/purchases/{po2}");
        var po2Line = po2Detail.GetProperty("lines")[0];
        await Post(http, $"/api/purchases/{po2}/receive", new
        {
            notes = "Diterima lengkap dan sesuai suhu",
            lines = new[]
            {
                new
                {
                    purchaseOrderLineId = po2Line.GetProperty("id").GetInt32(),
                    ingredientId = mentega.Id,
                    receivedQty = 30.0,
                    unitCostMinorUnits = 3_000_000L,
                    locationId = 1,
                    batchNumber = "MTA-2602",
                    expiryDate = hariIni.AddDays(30).ToString("yyyy-MM-dd"),
                },
            },
        });

        // PO 3 — masih draft, menunggu persetujuan pemilik.
        await Post(http, "/api/purchases", new
        {
            supplierId = pemasok["CV Sumber Bahan Nusantara"],
            expectedDate = hariIni.AddDays(3).ToString("yyyy-MM-dd"),
            notes = "Stok cokelat menipis, pesan sebelum akhir pekan",
            lines = new[] { new { ingredientId = cokelat.Id, orderedQty = 20.0, unitCostMinorUnits = 6_000_000L, unit = cokelat.Unit } },
        });

        // Lot lama yang sudah lewat kedaluwarsa, untuk memperlihatkan peringatan di layar batch.
        await Post(http, "/api/inventory/batches", new
        {
            ingredientId = bahan["Keju Cheddar"].Id,
            batchNumber = "KJU-2512",
            receivedDate = hariIni.AddDays(-40).ToString("yyyy-MM-dd"),
            expiryDate = hariIni.AddDays(-2).ToString("yyyy-MM-dd"),
            unitCostMinorUnits = 11_500_000L,
        });

        var spk = new Dictionary<string, int>();
        foreach (var w in new[]
        {
            ("Roti Tawar Susu", 50, 350_000L, 180_000L),
            ("Roti Cokelat", 60, 320_000L, 150_000L),
            ("Kroisan Mentega", 40, 300_000L, 140_000L),
            ("Donat Gula", 40, 260_000L, 120_000L),
        })
        {
            var id = (await Post(http, "/api/production/orders", new
            {
                productTemplateId = produk[w.Item1],
                targetQuantity = w.Item2,
                laborCost = w.Item3,
                overheadCost = w.Item4,
            })).GetProperty("id").GetInt32();
            spk[w.Item1] = id;
        }

        await SelesaikanSpkAsync(http, spk["Roti Tawar Susu"], 48, 2,
            "Adonan_Gagal", "Scrap_Loss", "Dua loyang bantat karena ragi kurang aktif.");
        await SelesaikanSpkAsync(http, spk["Roti Cokelat"], 57, 3,
            "Gosong", "Scrap_Loss", "Suhu oven tidak stabil pada loyang terakhir.");

        await Post(http, "/api/production/quality-checks", new
        {
            workOrderId = spk["Roti Tawar Susu"],
            sampleQuantity = 10,
            passedQuantity = 10,
            failedQuantity = 0,
            appearanceNotes = "Warna kulit roti merata, remah lembut.",
            tasteNotes = "Rasa susu pas, tidak terlalu manis.",
            notes = "Layak dijual.",
        });
        await Post(http, "/api/production/quality-checks", new
        {
            workOrderId = spk["Roti Cokelat"],
            sampleQuantity = 10,
            passedQuantity = 9,
            failedQuantity = 1,
            appearanceNotes = "Satu roti berwarna terlalu gelap di bagian atas.",
            tasteNotes = "Rasa cokelat konsisten.",
            notes = "Satu unit disisihkan, tidak dijual.",
        });

        await Put(http, $"/api/production/orders/{spk["Donat Gula"]}/cancel", new
        {
            reason = "Pesanan pasar batal; bahan dialihkan ke roti tawar.",
        });

        // Pesanan: satu lunas, satu DP, satu belum bayar, satu batal, satu pre-order, satu lewat waktu.
        var pesananLunas = await BuatPesananAsync(http, "Ibu Ratna — Arisan Melati", "0812-3456-7801",
            "Roti tawar susu 10 bungkus untuk arisan", hariIni.AddDays(1),
            produk["Roti Tawar Susu"], 10, 2_500_000L, 25_000_000L, "Tunai");
        await BuatPesananAsync(http, "Pak Joko — Koperasi Sekolah", "0813-4567-8902",
            "Roti cokelat 20 bungkus untuk kantin", hariIni.AddDays(2),
            produk["Roti Cokelat"], 20, 1_200_000L, 10_000_000L, "Transfer");
        await BuatPesananAsync(http, "Dewi — Pesanan Hajatan", "0857-5678-9013",
            "Roti tawar susu 5 bungkus", hariIni.AddDays(3),
            produk["Roti Tawar Susu"], 5, 2_500_000L, 0L, null);
        var pesananBatal = await BuatPesananAsync(http, "Andi — Kantor Kelurahan", "0821-6789-0124",
            "Roti cokelat 3 bungkus", hariIni.AddDays(2),
            produk["Roti Cokelat"], 3, 1_200_000L, 0L, null);
        await Put(http, $"/api/orders/{pesananBatal}/status", new
        {
            status = "Cancelled",
            cancelReason = "Pelanggan membatalkan karena acara diundur.",
        });
        await BuatPesananAsync(http, "Sekolah Bina Ceria", "021-7788-9900",
            "Roti keju 12 bungkus untuk rapat guru", hariIni.AddDays(5),
            produk["Roti Keju"], 12, 1_300_000L, 0L, null);
        await BuatPesananAsync(http, "Warung Bu Tini", "0819-7890-1235",
            "Donat gula 6 bungkus untuk titip jual", hariIni.AddDays(-1),
            produk["Donat Gula"], 6, 700_000L, 0L, null);

        await Post(http, "/api/returns", new
        {
            salesOrderId = pesananLunas,
            quantity = 2.0,
            amount = 5_000_000L,
            reason = "Dua bungkus kemasan penyok saat pengantaran.",
            refundMethod = "Tunai",
            idempotencyKey = "manual-retur-1",
        });

        await Post(http, "/api/expenses", new
        {
            category = "Biaya Operasional",
            description = "Listrik, air, dan kebersihan toko bulan ini",
            amount = 85_000_000L,
        });
        await Post(http, "/api/expenses", new
        {
            category = "Kemasan",
            description = "Kantong kertas dan stiker label 1.000 lembar",
            amount = 27_500_000L,
        });

        await Post(http, "/api/inventory/adjust", new
        {
            locationId = 1,
            ingredientId = cokelat.Id,
            newQuantity = 9.75,
            reason = "Selisih hitung fisik opname mingguan (0,25 kg).",
        });

        var akun = new[]
        {
            ("rani.admin", "RaniAdmin2026", "Admin"),
            ("budi.dapur", "BudiDapur2026", "Dapur"),
            ("sari.kasir", "SariKasir2026", "Pegawai"),
        };
        foreach (var a in akun)
            await Post(http, "/api/users", new { username = a.Item1, password = a.Item2, role = a.Item3 });

        await Post(http, "/api/notifications/sync", new { });

        return new DataContoh("sari.kasir", "SariKasir2026");
    }

    private static async Task SelesaikanSpkAsync(
        HttpClient http, int workOrderId, int baik, int gagal, string alasan, string tindakan, string catatan)
    {
        var detail = await Get(http, $"/api/production/orders/{workOrderId}");
        var materials = detail.GetProperty("materials").EnumerateArray().Select(m => new
        {
            ingredientId = m.GetProperty("ingredientId").GetInt32(),
            actualQty = Math.Round(m.GetProperty("standardQty").GetDecimal() * 0.98m, 3),
            unitCost = 0L,
        }).ToArray();

        await Post(http, $"/api/production/orders/{workOrderId}/complete", new
        {
            actualGoodQty = baik,
            actualFailedQty = gagal,
            materials,
            scraps = gagal == 0 ? Array.Empty<object>() : new object[]
            {
                new { quantity = gagal, reasonCategory = alasan, actionTaken = tindakan, notes = catatan },
            },
        });
    }

    private static async Task<int> BuatPesananAsync(
        HttpClient http, string nama, string telepon, string deskripsi, DateTime ambil,
        int produkId, int jumlah, long hargaSatuanMinor, long bayarMinor, string? metode)
    {
        var total = hargaSatuanMinor * jumlah;
        var id = (await Post(http, "/api/orders", new
        {
            customerName = nama,
            phone = telepon,
            description = deskripsi,
            pickupDate = ambil.ToString("yyyy-MM-dd"),
            totalAmount = total,
            downPayment = bayarMinor,
            productTemplateId = produkId,
            notes = metode is null ? null : $"Pembayaran awal via {metode}",
            createdByUserId = 0,
            idempotencyKey = $"manual-{Guid.NewGuid():N}",
            quantity = jumlah,
        })).GetProperty("id").GetInt32();
        return id;
    }

    private static async Task<JsonElement> Post(HttpClient http, string path, object body)
    {
        using var res = await http.PostAsJsonAsync(path, body);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST {path} gagal ({(int)res.StatusCode}): {text}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<JsonElement>(text);
    }

    private static async Task Put(HttpClient http, string path, object body)
    {
        using var res = await http.PutAsJsonAsync(path, body);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"PUT {path} gagal ({(int)res.StatusCode}): {text}");
    }

    private static async Task<JsonElement> Get(HttpClient http, string path)
    {
        using var res = await http.GetAsync(path);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET {path} gagal ({(int)res.StatusCode}): {text}");
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    private static async Task Buka(IPage page, string label, string teksTunggu)
    {
        await page.Locator($"aside button:has-text('{label}')").First.ClickAsync();
        // Sebagian judul tabel memuat jumlah baris ("Daftar Purchase Order (3)"), jadi pencocokan
        // dibuat "mengandung" dan dibatasi ke area isi supaya label menu di sidebar tidak ikut
        // dianggap sebagai bukti layar sudah berganti.
        await page.Locator("main").GetByText(teksTunggu).First.WaitForAsync(
            new() { Timeout = 15_000 });
        await TungguTenangAsync(page);
    }

    /// <summary>
    /// Modal aplikasi tidak punya tombol tutup yang seragam (sebagian hanya "Batal" di dalam
    /// formulir), jadi dialog ditutup dengan memuat ulang layar. Sesi tersimpan di localStorage,
    /// sehingga pengguna tetap masuk dan menu berikutnya bisa dibuka.
    /// </summary>
    private static async Task TutupDialogAsync(IPage page)
    {
        await page.ReloadAsync();
        await page.Locator("aside").WaitForAsync(new() { Timeout = 15_000 });
        await TungguTenangAsync(page);
    }

    private static async Task TungguTenangAsync(IPage page)
    {
        for (var i = 0; i < 40; i++)
        {
            var tertunda = await page.EvaluateAsync<int?>("() => window.__s22Perf ? window.__s22Perf.tertunda : 0");
            if ((tertunda ?? 0) == 0) break;
            await page.WaitForTimeoutAsync(150);
        }
        // Satu jeda pendek untuk transisi dan animasi kartu; tanpa ini gambar bisa tertangkap
        // saat tabel masih setengah muncul.
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task Tangkap(IPage page, string folder, string nama)
    {
        await TungguTenangAsync(page);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(folder, nama + ".png"),
            FullPage = false,
        });
    }

    private sealed record DataContoh(string KasirUsername, string KasirPassword);
}
