# Seruni 22 Bakery

Aplikasi desktop untuk toko dan pabrik roti: penjualan, produksi, pembelian, stok, jurnal keuangan, dan laporan — berjalan **sepenuhnya offline** di satu komputer toko.

- Versi sumber dan paket terbaru: **1.2.14**, skema basis data **18**
- Status 1.2.14: **NOT READY** — yang sudah terbukti di mesin ini: gerbang verifikasi konfigurasi Release **33 langkah lulus, 0 gagal, 0 SKIP**, **544 tes otomatis lulus / 0 gagal / 0 dilewati**, **169 pemeriksaan API** tanpa kegagalan, `build-pro` **12 langkah, 0 gagal, 1 peringatan** dengan **393/393** berkas payload terverifikasi pada EXE dan MSI, dan pasang/peningkatan/copot **1.2.13 → 1.2.14 di Windows Sandbox bersih lulus 106/106** (data utuh, tanpa migrasi baru, pagar turun versi dan kanal terbukti). Yang menahan: cetak ke printer fisik, pemindai fisik, laci uang, dan uji di Windows 10 belum dilakukan, paket masih `NotSigned`, dan bukti hijau CI belum ada karena commit CI belum di-push. Rincian: [docs/verifikasi-paket-1.2.14-2026-11-05.md](docs/verifikasi-paket-1.2.14-2026-11-05.md).
- Status 1.2.13: **NOT READY** — gerbang verifikasi konfigurasi Release lulus penuh (32 langkah, 0 gagal, 0 SKIP; 523 tes otomatis; 169 pemeriksaan API), `build-pro` menghasilkan EXE+MSI dengan 393/393 berkas payload terverifikasi, dan pasang/peningkatan/copot di **Windows Sandbox bersih lulus 105/105** (1.2.12 → 1.2.13, data utuh, tanpa migrasi baru, pagar turun versi dan kanal terbukti), tetapi cetak ke printer fisik, pemindai fisik, laci uang, dan Windows 10 belum diuji, serta paket belum bertanda tangan.
- Status 1.2.12: **NOT READY** — paket dibangun 2026-11-01 dan saat itu 489 tes lulus plus pasang/peningkatan/copot di Windows Sandbox bersih (1.2.9 → 1.2.12, 105/105), tetapi paket itu **tidak memuat** perbaikan 2–3 November (pagar loopback, nilai stok jadi, retensi backup, kunci idempotensi, muat bertahap daftar piutang/utang). Versi 1.2.11 tidak pernah diterbitkan.
- Status 1.2.9: **READY FOR LIMITED PILOT** (uji pasang/peningkatan berdasarkan atestasi tertulis pemilik; berkas bukti mentah belum diterima)
- Paket **NotSigned** (belum ada sertifikat tanda tangan)
- Platform: Windows 10/11 x64
- Riwayat perubahan: [CHANGELOG.md](CHANGELOG.md)

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
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1 -Version 1.2.12
```

Hasil: `dist\publish-single\` dan `dist\installer\Seruni22Bakery_Setup_v1.2.12.exe`, disertai `SHA256SUMS.txt` dan `BUILD-INFO.txt`.

MSI (WiX, per-machine) — hanya membungkus folder publish yang sudah ada:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-msi.ps1 -Version 1.2.12
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

- Stok bahan warisan (diketik di **Bahan Baku** sebelum jalur itu ikut membukukan jurnal) ditambal dengan `--rekonsiliasi-stok-bahan`. Tanpa `--terapkan` perintah ini hanya mencetak daftar bahan, jumlah, dan nilainya; dengan `--terapkan` ia membuat cadangan `pra-rekonsiliasi` lebih dulu lalu menulis jurnal stok awal melawan **3-001 Modal**. Rinciannya di `docs/pemulihan-bencana.md`.

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

Status 1.2.12: **NOT READY**. Build Release, 489 tes otomatis, gerbang verifikasi 28 langkah
(169 pemeriksaan API), isi paket, dan pemulihan layar setelah server mati sudah terbukti.
Pasang/peningkatan/copot di Windows Sandbox bersih juga sudah lulus: MSI 1.2.9 dinaikkan ke
MSI 1.2.12 dengan data lama utuh, penolakan turun versi dan kanal silang EXE↔MSI terbukti,
dan ESC/POS dari biner hasil peningkatan diterima emulator (`lulus=105 gagal=0 total=105`).
Belum dijalankan untuk paket ini: cetak ke printer fisik, pemindai fisik, dan laci uang,
pemasangan oleh pemilik di PC kasir, serta uji di Windows 10. Alur pemindai sudah lulus
simulasi 14/14, dan percobaan cetak driver ke pencetak virtual gagal karena port
`PORTPROMPT:` (bukan bukti printer thermal).
Paket 1.2.12 dipakai untuk uji perangkat dan uji pasang oleh pemilik sebelum status naik.

Status 1.2.9: **READY FOR LIMITED PILOT**, bukan READY FOR PRODUCTION. Perbaikan kode,
tes otomatis, dan isi paket sudah lulus. Uji pasang/peningkatan/copot 1.2.9 dilaporkan
lulus oleh pemilik secara tertulis (`lulus=65 gagal=0 total=65`) setelah menjalankan kit
di mesin ≥8 GB; berkas bukti mentah (`status.txt`, `runner.log`, folder `bukti\`) belum
sampai ke mesin ini, dan pemilik menerima atestasi tertulis sebagai dasar keputusan.
Naik ke READY FOR PRODUCTION masih menuntut berkas bukti yang dapat diperiksa, paket
bertanda tangan, dan uji perangkat kasir fisik.

Bukti 1.2.12 (2026-11-01):

- 489 tes otomatis Release hijau (Application 129, Domain 83, Infrastructure 143, UI 134).
- `scripts/verify.ps1 -SkipTests -Configuration Release`: 28 langkah lulus; verifikasi API hidup
  batch2 63, batch3 56, batch6 21, bug09 29 — masing-masing 0 gagal; uji argumen peluncur 5/5
  dengan kontrol negatif merah pada peluncur lama.
- Empat pagar ketahanan sambungan baru (`KeepAliveTimeout`, probe `/health`, penantian di layar,
  bundel `desktop/` memuat perbaikannya). Kontrol negatif pagar bundel dijalankan: berkas aset
  dipindahkan → FAIL, dikembalikan → LULUS.
- Bukti hidup: host scratch dimatikan saat aplikasi terbuka → banner "Menunggu server hidup
  lagi…" (tanpa tombol Coba lagi); host dijalankan lagi tanpa klik apa pun → banner hilang dan
  data termuat (log host: 4 `GET /health`, 2 `GET /api/dashboard`). Dijalankan dua kali.
- Struk uji mencetak `2 x Rp 15.000 … Rp 30.000`; `ReceiptRenderer` 17/17.
- Jalur ESC/POS raw terbukti sampai emulator loopback (1.572 bita = 636 + 936, diperiksa byte
  demi byte), dan sekali lagi dari biner hasil peningkatan di Sandbox (635 bita, nominal
  `Rp 30.000`, pengaturan bertahan setelah host di-restart). **Belum** diuji pada printer fisik.
- Isi paket: 393 berkas, 135.883.947 bita, host `1.2.12`, peluncur `1.2.12`, bebas
  `.pdb`/DB/`jwt.key`/`.env`; payload MSI identik dengan `dist\publish-single`; `NotSigned`
  pada EXE maupun MSI. Rincian: `docs\verifikasi-paket-1.2.12-2026-11-01.md`.
- Uji pasang/peningkatan/copot 1.2.12 di Windows Sandbox bersih: `lulus=105 gagal=0 total=105`
  (seluruh 105 pemeriksaan dijalankan ulang dari boot sandbox baru). Lulus: MSI 1.2.9 dipasang
  lalu ditingkatkan ke 1.2.12 dengan 393/393 berkas payload identik dan data lama utuh; MSI
  1.2.9 menolak dipasang di atas 1.2.12; EXE 1.2.12 menolak berjalan selama MSI terpasang dan
  sebaliknya; copot MSI/EXE menyisakan folder data pengguna. Bukti mentah (status,
  `validasi.log`, log installer/host, byte ESC/POS): `docs\paket-bukti-2026-11-01\upgrade-1212-sandbox\`.
  Run pertama kit ini melaporkan 5 FAIL; kelimanya cacat harness (pola log, berkas log dibaca
  saat host masih menulis, jalur pintasan), diperbaiki di skrip uji tanpa melonggarkan
  pemeriksaan produk, lalu seluruh 105 pemeriksaan diulang.
- Belum dijalankan untuk 1.2.12: cetak ke printer thermal/jaringan fisik, pemindai barcode fisik,
  laci uang, pemasangan oleh pemilik di PC kasir, dan Windows 10.
- Uji perangkat dengan sarana yang ada (2026-11-01): alur pemindai disimulasikan dengan ketukan
  per karakter + Enter pada aplikasi 1.2.12 yang dikemas — 14/14 lulus (10 pindai cepat tanpa
  karakter hilang, kode asing ditolak, tanpa Enter tidak memilih produk); itu simulasi, bukan
  perangkat fisik. Percobaan cetak driver ke `Microsoft Print to PDF` **gagal** karena port
  `PORTPROMPT:` menuntut dialog simpan (endpoint `test-print` melewati 40 detik, antrean 0),
  jadi jalur driver thermal tetap belum diuji. Laci uang belum ada; 635 bita struk ESC/POS
  diperiksa dan tidak memuat `ESC p` maupun perintah terlarang lain. Authenticode: 0 sertifikat,
  kedua paket `NotSigned`, `signtool verify /pa` menolak. Bukti:
  `docs\paket-bukti-2026-11-01\uji-fisik-1212\`.

Bukti terakhir 1.2.9 (2026-10-31):

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

- [Changelog](CHANGELOG.md) — rincian perubahan tiap versi, termasuk seluruh BUG 1.2.9
- [Verifikasi paket 1.2.12](docs/verifikasi-paket-1.2.12-2026-11-01.md) — artefak, isi paket, tes, gerbang, dan bukti pemulihan sambungan; juga daftar yang belum diuji
- [Verifikasi paket 1.2.9](docs/verifikasi-paket-1.2.9-2026-10-31.md) — isi paket, uji payload, pagar installer, dan atestasi uji pasang pemilik
- [Verifikasi paket 1.2.7](docs/verifikasi-paket-1.2.7-2026-10-30.md) — isi paket, uji payload, peningkatan MSI, pasang/copot EXE
- [Laporan GO/NO-GO](docs/laporan-go-no-go-2026-10-29.md) — keputusan kesiapan dan syarat pilot
- [Cadangan & pemulihan bencana](docs/pemulihan-bencana.md) — panduan pemilik dan teknisi
- [Perangkat kasir](docs/pos-hardware.md) dan [uji fisik](docs/pos-uji-fisik.md)
- [Performa navigasi](docs/performa-navigasi-2026-10-29.md)
- [Notifikasi internal & MCP](docs/internal-notifications-and-mcp.md)
- [Registri kasus uji](docs/test-case-registry-2026-10-28.md)
- Bukti mentah uji paket: `docs/paket-bukti-2026-10-29/`, `docs/paket-bukti-2026-10-30/`, `docs/paket-bukti-2026-10-31/`, `docs/paket-bukti-2026-11-01/`

## Riwayat versi singkat

Rincian lengkap ada di [CHANGELOG.md](CHANGELOG.md).

| Versi | Perubahan inti |
|---|---|
| 1.2.12 | Jalur cetak ESC/POS raw, perbaikan nominal struk uji, ketahanan sambungan (banner "Server lokal tidak menjawab" memulihkan diri) |
| 1.2.11 | Tidak dirilis; perubahan digabung ke 1.2.12 |
| 1.2.10 | Zona waktu toko untuk ekspor, penomoran dokumen, dan rekap harian; peluncur meneruskan perintah pemeliharaan |
| 1.2.9 | Perbaikan 31 temuan audit: jurnal HPP/persediaan, validasi SPK, status pesanan atomik, RBAC dan kebocoran data, pagar versi/kanal installer |
| 1.2.8 | Tidak dirilis; perubahan peluncur ikut termuat di 1.2.9 |
| 1.2.7 | Nama toko, slogan, dan logo dapat diganti kapan saja |
| 1.2.6 | Kemasan audit final; sembilan cacat pra-pilot tertutup di 1.2.5 |
| 1.2.5 | Perbaikan BUG-02 s.d. BUG-11 (retur, transfer stok, zona waktu, label keuangan, dll.) |
| 1.2.4 | Skema 17: indeks penutup untuk data besar, peningkatan performa navigasi |
