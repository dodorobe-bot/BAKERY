# Seruni 22 Bakery

Aplikasi desktop untuk toko dan pabrik roti: penjualan, produksi, pembelian, stok, jurnal keuangan, dan laporan — berjalan **sepenuhnya offline** di satu komputer toko.

- Versi saat ini: **1.2.7**, skema basis data **17**
- Status rilis: **READY FOR LIMITED PILOT** (belum production), paket **NotSigned**
- Platform: Windows 10/11 x64

## Arsitektur

```
Peluncur (Seruni22Bakery.exe, WinExe native)
   └─ menyalakan host tanpa jendela konsol, menunggu /health, membuka peramban
        └─ Host ASP.NET Core (.NET 8, self-contained)
             ├─ API HTTP loopback http://localhost:5000
             ├─ SPA React (dibundel ke wwwroot)
             └─ SQLite (WAL) di folder data pengguna
```

Tidak ada server pusat dan tidak ada koneksi internet. Paket instalasi sudah membawa runtime .NET 8 sehingga mesin kasir tidak perlu memasang .NET terpisah.

## Fitur utama

- **Penjualan**: pesanan, pembayaran, pelunasan, retur, cetak struk, pindai barcode
- **Produksi**: SPK, QC, pemakaian bahan, stok produk jadi, HPP per unit
- **Pembelian**: pesanan pembelian, penerimaan, pembatalan parsial
- **Inventaris**: bahan baku, resep (BOM), stok per lokasi, stok opname, batch & kedaluwarsa
- **Keuangan**: jurnal berpasangan otomatis, laba kotor, ekspor XLSX
- **Sistem**: pengguna & peran (RBAC), jejak audit, periksa-diri basis data, cadangan otomatis
- **Merek toko**: nama, slogan, dan logo bisa diganti kapan saja dari Pengaturan

## Struktur repositori

```
BakeryDesktop.slnx
src/
  Domain/               aturan bisnis, entitas, validasi
  Application/          use case dan layanan
  Infrastructure.Sqlite/repositori SQLite dan migrasi skema
  Host.Service/         host ASP.NET Core, endpoint API, penyajian SPA
  Desktop.Contracts/    kontrak bersama host dan peluncur
  Migration.Tool/       alat bantu migrasi basis data
frontend/               React 19 + Vite + Tailwind
build/installer/        skrip Inno (EXE), WiX (MSI), peluncur C#, uji mesin bersih
tests/                  Domain, Application, Infrastructure, UI, Perf, Verify
scripts/                build-installer, build-msi, verify, sign-paket, pos-physical-check
docs/                   laporan verifikasi, GO/NO-GO, pemulihan, perangkat kasir
```

## Kebutuhan pengembangan

| Alat | Kegunaan |
|---|---|
| Windows 10/11 x64 | target aplikasi (pencetakan memakai driver Windows) |
| .NET 8 SDK | build dan tes host |
| Node.js 22+ dan npm | frontend |
| PowerShell 5.1 | skrip build/verifikasi |
| Inno Setup 6 | hanya untuk membangun installer EXE |
| WiX 5 (`dotnet tool install --global wix`) | hanya untuk membangun MSI |

## Menjalankan saat pengembangan

Terminal 1 — host API:

```
dotnet run --project src/Host.Service/BakeryDesktop.Host.Service.csproj
```

Terminal 2 — frontend dengan proxy ke host:

```
cd frontend
npm install
npm run dev
```

Buka http://localhost:5173. Akun bawaan `admin` / `admin123`; aplikasi menolak endpoint lain sampai sandi diganti pada login pertama.

Agar data nyata tidak tersentuh, jalankan host dengan basis data dan kunci JWT terpisah:

```
dotnet run --project src/Host.Service/BakeryDesktop.Host.Service.csproj -- ^
  --Database:Path=C:\tmp\dev\uji.db --Jwt:SigningKeyFile=C:\tmp\dev\uji.key ^
  --urls http://127.0.0.1:5312
```

## Tes

```
dotnet test BakeryDesktop.slnx
```

351 kasus: Application 51, Domain 80, Infrastructure 119, UI 101 (UI memakai Playwright dan host uji dengan basis data sementara).

Pagar satu perintah — build, seluruh tes, lalu verifikasi perilaku lewat API pada basis data sementara:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify.ps1
```

Skrip verifikasi perilaku lain ada di `tests/Verify/*.mjs` (dijalankan terhadap host uji, bukan data toko).

## Membangun paket instalasi

EXE (Inno Setup) — tes, frontend produksi, publish self-contained, peluncur, lalu Inno:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version 1.2.7
```

Hasil: `dist\publish-single\` dan `dist\installer\Seruni22Bakery_Setup_v1.2.7.exe`, disertai `SHA256SUMS.txt` dan `BUILD-INFO-MSI.txt`.

MSI (WiX, per-machine) — hanya membungkus folder publish yang sudah ada:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-msi.ps1 -Version 1.2.7
```

Tanda tangan Authenticode (butuh sertifikat; saat ini belum dipakai):

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\sign-paket.ps1 -Thumbprint <40 hex>
```

Artefak installer, folder publish, `node_modules`, basis data, dan kunci tidak masuk repositori (lihat `.gitignore`). Integritas paket dibuktikan lewat SHA-256 di `dist\installer\SHA256SUMS.txt` dan dokumen verifikasi.

## Data pengguna, cadangan, dan pemulihan

| Isi | Letak bawaan |
|---|---|
| Basis data aktif | `%LOCALAPPDATA%\Seruni22Bakery\seruni22bakery.db` |
| Folder cadangan | `%LOCALAPPDATA%\Seruni22Bakery\backups\` |
| Kunci JWT per instalasi | `%LOCALAPPDATA%\Seruni22Bakery\jwt.key` |
| Konfigurasi pengguna | `%LOCALAPPDATA%\Seruni22Bakery\config\appsettings.json` |
| Catatan aplikasi | `%LOCALAPPDATA%\Seruni22Bakery\logs\` |

- Cadangan otomatis berjalan sekali sehari selama aplikasi dipakai, dan setiap cadangan langsung diperiksa (integritas SQLite, foreign key, versi skema) lalu dicatat di manifes `.json` berisi SHA-256.
- Cadangan manual: **Pengaturan → Cadangan & penyimpanan → Buat cadangan sekarang**.
- Cadangan/pemulihan dari baris perintah dijalankan lewat host, bukan peluncur, dan aplikasi harus tertutup:

```
"%ProgramFiles%\Seruni 22 Bakery\Seruni22Bakery.Host.exe" --backup
"%ProgramFiles%\Seruni 22 Bakery\Seruni22Bakery.Host.exe" --restore "C:\path\cadangan.db"
```

`--restore` menolak berjalan bila masih ada proses `Seruni22Bakery.Host` lain (kode keluar 3). Kedua perintah menerima `--Database:Path=...` untuk menunjuk basis data lain.

> **Batasan 1.2.7:** peluncur `Seruni22Bakery.exe` hanya mengenali `--tanpa-browser` dan **tidak meneruskan** `--backup`/`--restore` ke host, jadi perintah pemulihan harus memakai `Seruni22Bakery.Host.exe` seperti di atas. Ini belum diperbaiki di 1.2.7.

Installer tidak memuat basis data maupun kunci, dan uninstall tidak menghapus folder data pengguna.

## Keamanan

- API hanya mendengarkan loopback; CORS dibatasi ke alamat lokal aplikasi.
- Kunci JWT dibuat acak per instalasi dengan izin berkas ketat.
- Gerbang wajib-ganti-sandi ditegakkan server sebelum endpoint lain bisa dipakai.
- Otorisasi per peran (RBAC) dan cakupan kasir diperiksa di server, bukan hanya disembunyikan di menu.
- Aksi sensitif meninggalkan jejak audit; foto/logo disimpan sebagai penanda, bukan isi biner.
- Endpoint publik hanya `/health` dan `/api/brand` (tiga kunci merek).

## Status rilis dan batasan yang diketahui

Bukti terakhir (2026-10-29 s.d. 2026-10-31):

- 351 tes otomatis hijau; binari yang benar-benar dikemas lulus 180 pemeriksaan perilaku.
- Peningkatan MSI 1.2.6 → 1.2.7 lulus di mesin pemilik (basis data utuh) dan di Windows Sandbox bersih 65/65, termasuk copot tanpa kehilangan data.
- Pasang/copot EXE 1.2.7 lulus 42 pemeriksaan di mesin pemilik tanpa mengganggu instalasi MSI.

Yang **belum** dibuktikan dan tidak boleh diklaim:

- Paket **belum ditandatangani** (`NotSigned`); sertifikat code signing belum dibeli.
- Perangkat kasir fisik — printer thermal 80 mm, pemindai barcode USB, laci uang — belum diuji.
- Jendela peluncur belum pernah dibuka di mesin berisi data nyata; uji perilaku memakai host yang dijalankan langsung pada basis data uji.
- Copot MSI 1.2.7 di mesin pemilik belum dijalankan (copot MSI sudah lulus di sandbox).
- Kompatibilitas belum diuji di semua versi Windows.

Karena itu verdict tetap **READY FOR LIMITED PILOT**, bukan **READY FOR PRODUCTION**.

## Dokumentasi

- [Verifikasi paket 1.2.7](docs/verifikasi-paket-1.2.7-2026-10-30.md) — isi paket, uji payload, peningkatan MSI, pasang/copot EXE
- [Laporan GO/NO-GO](docs/laporan-go-no-go-2026-10-29.md) — keputusan kesiapan dan syarat pilot
- [Cadangan & pemulihan bencana](docs/pemulihan-bencana.md) — panduan pemilik dan teknisi
- [Perangkat kasir](docs/pos-hardware.md) dan [uji fisik](docs/pos-uji-fisik.md)
- [Performa navigasi](docs/performa-navigasi-2026-10-29.md)
- [Notifikasi internal & MCP](docs/internal-notifications-and-mcp.md)
- [Registri kasus uji](docs/test-case-registry-2026-10-28.md)
- Bukti mentah uji paket: `docs/paket-bukti-2026-10-29/`, `docs/paket-bukti-2026-10-30/`

## Riwayat versi singkat

| Versi | Perubahan inti |
|---|---|
| 1.2.7 | Nama toko, slogan, dan logo dapat diganti kapan saja |
| 1.2.6 | Kemasan audit final; sembilan cacat pra-pilot tertutup di 1.2.5 |
| 1.2.5 | Perbaikan BUG-02 s.d. BUG-11 (retur, transfer stok, zona waktu, label keuangan, dll.) |
| 1.2.4 | Skema 17: indeks penutup untuk data besar, peningkatan performa navigasi |
