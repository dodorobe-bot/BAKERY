# Seruni 22 Bakery

Aplikasi desktop untuk toko dan pabrik roti: penjualan, produksi, pembelian, stok, jurnal keuangan, dan laporan — berjalan **sepenuhnya offline** di satu komputer toko.

- Versi saat ini: **1.2.9**, skema basis data **18**
- Status rilis: **READY FOR LIMITED PILOT** untuk 1.2.9 (uji pasang/peningkatan berdasarkan atestasi tertulis pemilik; berkas bukti mentah belum diterima), paket **NotSigned**
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
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version 1.2.9
```

Hasil: `dist\publish-single\` dan `dist\installer\Seruni22Bakery_Setup_v1.2.9.exe`, disertai `SHA256SUMS.txt` dan `BUILD-INFO.txt`.

MSI (WiX, per-machine) — hanya membungkus folder publish yang sudah ada:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-msi.ps1 -Version 1.2.9
```

Tanda tangan Authenticode (butuh sertifikat; saat ini belum dipakai):

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\sign-paket.ps1 -Thumbprint <40 hex>
```

Artefak installer, folder publish, `node_modules`, basis data, dan kunci tidak masuk repositori (lihat `.gitignore`). Integritas paket dibuktikan lewat SHA-256 di `dist\installer\SHA256SUMS.txt` dan dokumen verifikasi.

## Peningkatan versi

- **Satu kanal per komputer.** Installer EXE (per-pengguna) dan MSI (per-mesin) memakai port 5000 dan folder data yang sama. Keduanya menolak dipasang selama kanal lain terdeteksi; copot dulu yang terpasang sebelum berpindah kanal.
- **Upgrade di tempat.** Jalankan installer versi baru dari kanal yang sama di atas versi lama. Kedua kanal menolak menimpa versi yang lebih baru (turun versi tidak didukung, karena skema basis data yang sudah dimigrasi belum tentu terbaca kode lama).
- **Cadangkan lebih dulu:** **Pengaturan → Cadangan & penyimpanan → Buat cadangan sekarang**, atau `Seruni22Bakery.exe --backup` saat aplikasi tertutup.
- Data pengguna (basis data, kunci, cadangan, log) berada di luar folder instalasi dan tidak disentuh oleh upgrade maupun uninstall.
- Peluncur memeriksa `/health` sebelum membuka aplikasi. Bila yang menjawab di port 5000 adalah server dari versi atau folder instalasi lain, peluncur berhenti dengan pesan jelas alih-alih menampilkan data dari instalasi yang salah.
- Versi aplikasi yang berjalan tampil di **Pengaturan**; versi skema terpasang dan versi skema terkemas tampil di **Periksa basis data**.

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
- Cadangan/pemulihan dari baris perintah dijalankan saat aplikasi tertutup, lewat peluncur (hasil tampil di kotak pesan) atau langsung ke host (cocok untuk skrip):

```
Seruni22Bakery.exe --backup
Seruni22Bakery.exe --restore "C:\path\cadangan.db"

"%ProgramFiles%\Seruni 22 Bakery\Seruni22Bakery.Host.exe" --backup
"%ProgramFiles%\Seruni 22 Bakery\Seruni22Bakery.Host.exe" --restore "C:\path\cadangan.db"
```

Peluncur meneruskan argumen itu apa adanya ke host, menunggu selesai, lalu keluar dengan kode keluar host (`0` berhasil, `1` gagal dengan basis data dikembalikan, `2` argumen salah/berkas tidak ada, `3` aplikasi masih berjalan). Kedua jalur menerima `--Database:Path=...` untuk menunjuk basis data lain. Peluncur adalah program tanpa konsol, jadi skrip sebaiknya memakai `Seruni22Bakery.Host.exe`.

Installer tidak memuat basis data maupun kunci, dan uninstall tidak menghapus folder data pengguna.

## Keamanan

- API hanya mendengarkan loopback; CORS dibatasi ke alamat lokal aplikasi.
- Kunci JWT dibuat acak per instalasi dengan izin berkas ketat.
- Gerbang wajib-ganti-sandi ditegakkan server sebelum endpoint lain bisa dipakai.
- Otorisasi per peran (RBAC) dan cakupan kasir diperiksa di server, bukan hanya disembunyikan di menu.
- Aksi sensitif meninggalkan jejak audit; foto/logo disimpan sebagai penanda, bukan isi biner.
- Endpoint publik hanya `/health` dan `/api/brand` (nama toko, slogan, logo, dan nama outlet).

## Status rilis dan batasan yang diketahui

Status 1.2.9: **READY FOR LIMITED PILOT**, bukan READY FOR PRODUCTION. Perbaikan kode,
tes otomatis, dan isi paket sudah lulus. Uji pasang/peningkatan/copot 1.2.9 dilaporkan
lulus oleh pemilik secara tertulis (`lulus=65 gagal=0 total=65`) setelah menjalankan kit
di mesin ≥8 GB; berkas bukti mentah (`status.txt`, `runner.log`, folder `bukti\`) belum
sampai ke mesin ini, dan pemilik menerima atestasi tertulis sebagai dasar keputusan.
Naik ke READY FOR PRODUCTION masih menuntut berkas bukti yang dapat diperiksa, paket
bertanda tangan, dan uji perangkat kasir fisik.

Bukti terakhir (2026-10-31):

- 392 tes otomatis hijau (Domain 83, Application 51, Infrastructure 142, UI 116).
- Biner yang benar-benar dikemas lulus 169 pemeriksaan perilaku dan 25 pemeriksaan
  payload pada basis data sementara: `/health` 1.2.9, 18 migrasi, periksa-diri 9/9,
  skema 18, `/api/brand` empat kunci.
- Isi MSI 1.2.9 utuh: 393/393 berkas identik SHA-256/ukuran dengan `dist\publish-single`;
  tabel `LaunchCondition` memuat pagar turun versi dan pagar kanal EXE.
- Installer EXE 1.2.9 di mesin ini menolak berjalan karena mendeteksi MSI 1.2.7:
  kode keluar 1, tidak ada berkas terpasang, alasan tercatat di log Inno.
- Uji pasang/peningkatan/copot 1.2.9: atestasi pemilik `lulus=65 gagal=0 total=65`
  (13 bagian, termasuk MSI 1.2.7 → 1.2.9 dengan data lama, penolakan silang EXE/MSI,
  penolakan turun versi, dan copot yang menyisakan data pengguna). Berkas bukti mentah
  tidak diterima di mesin ini; lihat `docs\verifikasi-paket-1.2.9-2026-10-31.md`.
- Uji pasang/peningkatan 1.2.9 di Windows Sandbox **pada mesin ini** gagal dijalankan:
  tamu 1.536 MB tidak selesai boot karena mesin ini hanya 3.746 MB RAM (System event
  ID 2004, `vmmemWindowsSandbox` 1.200 MB). Kegagalan ini soal memori host, bukan paket.
- Kit uji siap salin untuk mesin ≥8 GB sudah dirakit:
  `scripts\siapkan-kit-uji-129.ps1` menghasilkan `C:\tmp\kit-uji-129` (9 berkas,
  177,5 MB). Preflight `run-sandbox.ps1 -Periksa` di mesin ini lulus 8 checksum dan
  mendeteksi prasyarat; tanpa `-Periksa` peluncur menolak jalan (RAM 3.746 MB, keluar
  3, tidak membuat `uji-jalan.wsb`).
- Bukti pasang/copot 1.2.7 (mesin pemilik dan sandbox) tetap berlaku untuk 1.2.7 saja,
  bukan untuk 1.2.9.

Yang **belum** dibuktikan dan tidak boleh diklaim:

- Berkas bukti mentah uji 1.2.9 (status.txt, runner.log, folder bukti) belum diterima;
  hasil uji 1.2.9 bersandar pada atestasi tertulis pemilik.
- Paket **belum ditandatangani** (`NotSigned`); sertifikat code signing belum dibeli.
- Perangkat kasir fisik — printer thermal 80 mm, pemindai barcode USB, laci uang — belum diuji.
- Jendela peluncur belum pernah dibuka di mesin berisi data nyata; uji perilaku memakai
  host yang dijalankan langsung pada basis data uji.
- Copot MSI 1.2.7 di mesin pemilik belum dijalankan (copot MSI 1.2.7 sudah lulus di sandbox).
- Installer EXE 1.2.7/1.2.8 tidak memiliki pagar versi, jadi installer lama itu sendiri
  masih bisa menurunkan instalasi; paket 1.2.9 sendiri menolak menurunkan versi.
- Kompatibilitas belum diuji di semua versi Windows.

## Dokumentasi

- [Verifikasi paket 1.2.9](docs/verifikasi-paket-1.2.9-2026-10-31.md) — isi paket, uji payload, pagar installer, dan uji pasang yang belum terbukti
- [Verifikasi paket 1.2.7](docs/verifikasi-paket-1.2.7-2026-10-30.md) — isi paket, uji payload, peningkatan MSI, pasang/copot EXE
- [Laporan GO/NO-GO](docs/laporan-go-no-go-2026-10-29.md) — keputusan kesiapan dan syarat pilot
- [Cadangan & pemulihan bencana](docs/pemulihan-bencana.md) — panduan pemilik dan teknisi
- [Perangkat kasir](docs/pos-hardware.md) dan [uji fisik](docs/pos-uji-fisik.md)
- [Performa navigasi](docs/performa-navigasi-2026-10-29.md)
- [Notifikasi internal & MCP](docs/internal-notifications-and-mcp.md)
- [Registri kasus uji](docs/test-case-registry-2026-10-28.md)
- Bukti mentah uji paket: `docs/paket-bukti-2026-10-29/`, `docs/paket-bukti-2026-10-30/`, `docs/paket-bukti-2026-10-31/`

## Riwayat versi singkat

| Versi | Perubahan inti |
|---|---|
| 1.2.9 | Perbaikan 31 temuan audit: jurnal HPP/persediaan, validasi SPK, status pesanan atomik, RBAC dan kebocoran data, pagar versi/kanal installer |
| 1.2.7 | Nama toko, slogan, dan logo dapat diganti kapan saja |
| 1.2.6 | Kemasan audit final; sembilan cacat pra-pilot tertutup di 1.2.5 |
| 1.2.5 | Perbaikan BUG-02 s.d. BUG-11 (retur, transfer stok, zona waktu, label keuangan, dll.) |
| 1.2.4 | Skema 17: indeks penutup untuk data besar, peningkatan performa navigasi |
