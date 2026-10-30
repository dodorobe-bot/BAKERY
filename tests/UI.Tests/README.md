# BakeryDesktop UI Automation Tests

UI automation tests untuk proyek BakeryDesktop menggunakan **Playwright** + **xUnit**.

## Prerequisites

| Tool | Versi minimum |
|------|--------------|
| .NET SDK | 8.0 |
| Node.js | 18+ (diperlukan oleh Playwright installer) |
| PowerShell | 5.1 atau PowerShell Core 7+ |

## Instalasi

### 1. Build project

```powershell
cd d:\bakery-desktop
dotnet build tests/UI.Tests/BakeryDesktop.UI.Tests.csproj
```

### 2. Install Playwright browsers

Setelah build sukses, jalankan Playwright browser installer:

```powershell
pwsh tests/UI.Tests/bin/Debug/net8.0/playwright.ps1 install chromium
```

> Bila menggunakan PowerShell 5.1, ganti `pwsh` dengan `powershell`.

## Cara Menjalankan

### Persiapan

Backend harus sudah berjalan sebelum test dijalankan:

```powershell
# Jalankan backend (dari direktori solution)
dotnet run --project src/Host.Service/BakeryDesktop.Host.Service.csproj
```

Pastikan service berjalan di `http://localhost:5000` (atau sesuai konfigurasi).

```powershell
# Contoh host dengan basis data UJI (bukan data produksi), plus aset desktop/
$env:SERUNI_ALLOW_DEFAULT_JWT = "1"
dotnet run --project src/Host.Service/BakeryDesktop.Host.Service.csproj -- `
  --Database:Path "C:/tmp/ui-tests.db" `
  --Desktop:StaticFilesPath "D:/bakery-desktop/desktop" `
  --urls http://localhost:5000
```

> Uji ini memanggil API tulis (login, penyesuaian stok) jadi jalankan terhadap basis data uji.
> Sessi login disuntik dari `POST /api/auth/login` oleh `Helpers/AppLogin.cs`: akun seed `admin`
> membawa `MustChangePassword=1`, dan overlay "Ganti Kata Sandi" menutupi sidebar sehingga setiap
> klik navigasi ditolak Playwright. Helper hanya melepas bendera itu di sisi klien — sandi tersimpan
> di basis data tidak pernah diubah. Alur form login dan penolakan kredensial tetap diuji LoginTests.

### Jalankan semua UI tests

Karena test menggunakan `Skip` attribute secara default (agar CI tidak gagal tanpa server), hapus atau override attribute tersebut saat menjalankan manual, atau gunakan filter:

```powershell
dotnet test tests/UI.Tests/BakeryDesktop.UI.Tests.csproj
```

### Environment Variables

| Variable | Default | Keterangan |
|----------|---------|-----------|
| `BAKERY_TEST_URL` | `http://localhost:5000` | Base URL aplikasi yang sedang berjalan |
| `PLAYWRIGHT_HEADLESS` | `true` | Set ke `false` untuk melihat browser saat test berjalan |

Contoh menjalankan dengan browser terlihat:

```powershell
$env:PLAYWRIGHT_HEADLESS = "false"
$env:BAKERY_TEST_URL     = "http://localhost:5000"
dotnet test tests/UI.Tests/BakeryDesktop.UI.Tests.csproj
```

## Struktur Project

```
tests/UI.Tests/
├── BakeryDesktop.UI.Tests.csproj
├── README.md
├── Helpers/
│   ├── AppLogin.cs            # Sesi login API untuk tes (tanpa mengubah sandi di DB)
│   └── PlaywrightFixture.cs   # IAsyncLifetime fixture (browser + page lifecycle)
└── Tests/
    ├── LoginTests.cs           # Login valid / invalid
    ├── NavigationTests.cs      # Klik menu sidebar ke setiap view utama
    └── DashboardTests.cs       # Dashboard menampilkan metrik dari API
```

## Test Cases

### LoginTests
- `Login_WithValidCredentials_RedirectsToDashboard` — verifikasi redirect setelah login sukses
- `Login_WithInvalidCredentials_ShowsError` — verifikasi pesan error ditampilkan

### NavigationTests
- `Sidebar_ClickSalesMenu_ShowsSalesView` — klik menu Penjualan
- `Sidebar_ClickInventoryMenu_ShowsInventoryView` — klik menu Inventori/Persediaan
- `Sidebar_ClickProductionMenu_ShowsProductionView` — klik menu Produksi/SPK
- `Sidebar_ClickFinanceMenu_ShowsFinanceView` — klik menu Keuangan/Buku Besar

### DashboardTests
- `Dashboard_Loads_ShowsKeyMetrics` — verifikasi kartu metrik utama tampil
- `Dashboard_ApiData_IsNotHardcoded` — verifikasi data berasal dari API (tidak hardcoded)

## Catatan

- Semua test menggunakan `[Fact(Skip = "...")]` secara default sehingga `dotnet test` pada CI tidak gagal bila server tidak running.
- Untuk menjalankan test secara aktif, hapus atribut `Skip` atau gunakan partial test run dengan filter nama.
- Selector yang digunakan bersifat toleran (multi-selector CSS) sehingga tetap berjalan meskipun class name berubah sedikit.
