# Verifikasi paket 1.2.12 — 2026-11-01

Putusan: **NOT READY.** Paket dibangun dan diperiksa, semua tes otomatis dan gerbang lulus,
perbaikan ketahanan sambungan dibuktikan hidup di peramban, dan pasang/peningkatan/copot sudah
diuji di Windows Sandbox bersih (105 pemeriksaan lulus, 0 gagal). Yang belum ada: pencetakan ke
printer fisik, pemindai barcode, laci uang, pemasangan oleh pemilik di PC kasir, uji di Windows
10, dan tanda tangan Authenticode. Tidak ada satu pun dari hal itu yang boleh dianggap lulus
dari dokumen ini.

Versi **1.2.11 tidak pernah diterbitkan**; perbaikan struk yang semula direncanakan untuk 1.2.11
digabung ke 1.2.12.

## Artefak

| Berkas | Ukuran (bita) | SHA-256 |
|---|---|---|
| `dist\installer\Seruni22Bakery_Setup_v1.2.12.exe` | 42.675.344 | `f43f4e8583cc1df84354039925f45046aafe93a6f3996b112c80501229a506c1` |
| `dist\installer\Seruni22Bakery_v1.2.12_x64.msi` | 50.399.748 | `5ee109f9ece7cd24b4125adb5f999aedd8cd90d701a77bb9b2d8e9a8bd8fc859` |

Isi paket (dari `dist\publish-single`, lalu dibandingkan dengan ekstraksi administratif MSI):

| Berkas | SHA-256 |
|---|---|
| `Seruni22Bakery.Host.dll` | `ccfdc91b193a87b9acb9ab9418cbbb6cf4304f3de604101b4c331f8305f9ad7d` |
| `Seruni22Bakery.exe` (peluncur) | `44290eb7a43cc662e607f097b7bb670cf8fdbf4d1fdf82baf58308c814ea5e27` |
| `wwwroot\index.html` (466 bita) | `954c5d65e5e15270d40d4c9b9c435da260172be631bc820f4f84012dc96b4617` |
| `wwwroot\assets\index-dCrlHoiC.js` (596.507 bita) | `602b04bf13dbcc1bd04ce0a56801824a78c8139e366f0f89870d61034cf77135` |
| `wwwroot\assets\index-RvdFb41s.css` (58.177 bita) | `f3f9c629d71f73ca11b8fff08d8daafe5ffe5b9ad8ef2efae5d911b08ea06180` |

- Versi yang dilaporkan host dan peluncur: `1.2.12` (dibaca dari `VersionInfo`, bukan dari nama berkas).
- MSI: ProductCode `{1D0B5C27-C2B9-49AD-A675-BA390B33E3E3}`, UpgradeCode
  `{B975F792-6F80-4D56-8A64-D274A044DCF1}` (dipertahankan), ProductVersion `1.2.12`.
- Tanda tangan Authenticode kedua paket: **NotSigned** (dibaca `Get-AuthenticodeSignature`).
- Payload MSI = payload publish: 393 berkas, 135.883.947 bita, daftar berkas identik (`diff` kosong).
- `dist\installer\SHA256SUMS.txt` kini memuat **kedua** artefak (EXE + MSI) dan lolos
  `sha256sum -c`; berkas ini berakhiran LF. Sebelumnya `build-installer.ps1` hanya menulis baris
  EXE sehingga daftar paket 1.2.10 tidak memuat MSI; `build-msi.ps1` sekarang menambahkan
  barisnya sendiri (penggantian baris lama untuk berkas yang sama, jadi menjalankan ulang tidak
  menggandakan entri). Logika itu diuji terpisah di `C:\tmp\uji-append-sums.ps1`: dua kali
  dijalankan tetap 2 baris, tanpa CR.
- Bundel yang disajikan memuat perbaikan: `grep -c "Menunggu server hidup lagi"` pada
  `wwwroot\assets\index-dCrlHoiC.js` = 1, dan tidak ada berkas JS ber-hash lama di `desktop\assets`.

## Perintah build

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-msi.ps1
```

Log: `C:\tmp\build-1212.log`, `C:\tmp\build-msi-1212.log`, `dist\build-logs\{test,frontend-build,publish,msi-build}.log`.

## Tes dan gerbang

| Pemeriksaan | Hasil |
|---|---|
| `dotnet test BakeryDesktop.slnx -c Release` (di dalam build-installer) | 489 lulus, 0 gagal (129 + 83 + 143 + 134) |
| `scripts\verify.ps1 -SkipTests -Configuration Release` | 28 langkah lulus; 169 pemeriksaan API (63 + 56 + 21 + 29), 0 gagal |

Sesudah `build-msi.ps1` diubah (agar SHA256SUMS memuat MSI), pagar dijalankan ulang dengan
`-SkipTests -SkipVerify`: **20 langkah lulus**, keluar 0 (`C:\tmp\gate-1212-final.log`). Tes dan
verifikasi API tidak diulang karena biner dan isi paket tidak berubah.

Empat pagar baru §2d ikut dinilai: `KeepAliveTimeout` di host, probe `/health` di klien, penantian
server di layar, dan bundel `desktop/` memuat teks penantian. Kontrol negatif pagar bundel
dijalankan, bukan diasumsikan: berkas `desktop\assets\index-dCrlHoiC.js` dipindahkan sementara,
pagar berubah **FAIL** dengan pesan `desktop\assets\index-*.js tidak ada` (run keluar 1), lalu
berkas dikembalikan — SHA-256 tetap
`602b04bf13dbcc1bd04ce0a56801824a78c8139e366f0f89870d61034cf77135` dan pagar kembali
**LULUS 20 langkah** (`C:\tmp\kontrol-bundel.log`, `C:\tmp\pulih-bundel.log`). Kedua jalan
kontrol itu memakai `-SkipTests -SkipVerify` (build Debug + pagar), jadi angka 20 tidak
menggantikan gerbang 28 langkah di tabel atas.

## Bukti hidup ketahanan sambungan (2026-11-01)

Lingkungan: host scratch `--Database:Path C:/tmp/uji-banner.db` (basis data sementara, data usaha
tidak disentuh) pada `http://localhost:5000`, aplikasi disajikan host itu sendiri, dibuka di
peramban sungguhan.

1. Dasbor termuat, tanpa banner.
2. Host dimatikan (`taskkill` pada PID pemilik port 5000). Tombol **Perbarui Data** ditekan.
   Hasil: banner `Data gagal dimuat: Server lokal belum berjalan atau sudah tertutup. Buka kembali
   aplikasi Seruni 22 Bakery — layar ini memuat sendiri begitu server hidup lagi.` dengan label
   `Menunggu server hidup lagi…`; tombol **Coba lagi** tidak ditampilkan.
3. Host dijalankan lagi; **tidak ada klik apa pun**. Dalam ±10 detik banner hilang dan data
   dasbor terisi lagi.
4. Log host baru: 4 permintaan `GET /health` dan 2 `GET /api/dashboard` — bukti bahwa layar
   memulihkan diri sendiri dan tidak membanjiri server.

Siklus di atas dijalankan dua kali (sebelum dan sesudah penyederhanaan penjadwalan penantian),
hasilnya sama. Berkas log: `C:\tmp\uji-banner-host*.log`.

## Uji pasang & peningkatan di Windows Sandbox (2026-11-01)

Lingkungan: tamu Windows 11 Enterprise build 26100, RAM 1.536 MB, jaringan dimatikan, pengguna
`WDAGUtilityAccount` elevated. Run terakhir: **105 pemeriksaan lulus, 0 gagal**
(`lulus=105 gagal=0 total=105`, 17:44–17:56). Bukti, skrip, dan kedua run ada di
`docs\paket-bukti-2026-11-01\upgrade-1212-sandbox\`.

- MSI 1.2.9 dipasang dan diisi data, lalu ditingkatkan ke 1.2.12: exit 0, `WIX_UPGRADE_DETECTED`
  untuk ProductCode 1.2.9, `RemoveExistingProducts` jalan, ARP berganti tunggal.
- 393 berkas payload identik dengan manifes (`cocok=393 beda=0 hanya-terpasang=0 total=393`);
  sandi, produk, merek, dan logo dari 1.2.9 tetap utuh; skema 18 tanpa migrasi baru.
- ESC/POS raw dari biner hasil peningkatan: emulator loopback menerima 635 bita, diawali `ESC @`,
  diakhiri `ESC d 3`, memuat `Rp 30.000` (bukan `Rp 300`), tanpa `GS V` saat potong mati;
  pengaturan bertahan setelah host di-restart.
- Pagar kanal dan versi bekerja (EXE⇄MSI, EXE 1.2.12 menolak penanda 9.9.9, MSI 1.2.9 ditolak di
  atas 1.2.12). Copot MSI dan EXE tidak menghapus folder data pengguna.
- Run pertama (100 lulus / 5 gagal) disimpan juga; kelima kegagalan terbukti cacat skrip uji —
  regex pesan pagar, path pintasan Start Menu EXE, dan pembacaan log sebelum host berhenti —
  bukan cacat produk. Perbaikannya tercatat di README folder bukti.

Yang **tidak** dibuktikan oleh uji ini: printer thermal fisik, printer jaringan fisik, pemindai
barcode, laci uang, Windows 10, dan pemasangan di PC pemilik. Emulator printer adalah perangkat
lunak di dalam tamu.

## Uji perangkat dengan sarana yang ada (2026-11-01)

Mesin pengembangan tidak punya printer thermal, pemindai, port COM, laci uang, maupun sertifikat
code signing (`docs\paket-bukti-2026-11-01\uji-fisik-1212\perangkat-2026-11-01.txt`). Yang bisa
dijalankan:

- **Pemindai barcode (simulasi):** aplikasi 1.2.12 yang dikemas, basis data sementara, Chromium
  mengetik per karakter + Enter ke kolom pindai. **14/14 lulus**, termasuk 10 pindai cepat tanpa
  karakter hilang (4189 ms), pesan `Barcode "0000000000000" tidak cocok dengan produk mana pun.`,
  penolakan tanpa Enter, dan fokus di luar kolom. Ini simulasi wedge, bukan pemindai fisik.
- **Cetak driver Windows:** percobaan ke `Microsoft Print to PDF` **gagal** —
  `POST /api/devices/test-print` melewati batas 40 detik, antrean cetak 0 pekerjaan, tidak ada PDF
  baru. Port `PORTPROMPT:` menuntut dialog simpan sehingga cetak senyap memblokir. Bukan lulus;
  jalur driver ke printer thermal tetap belum diuji.
- **Laci uang:** fitur belum ada. Jaminan negatif diperiksa byte demi byte pada 635 bita struk
  ESC/POS dari biner hasil peningkatan: tidak ada `ESC p`, `ESC a`, `ESC E`, `ESC t`, `GS V`,
  `GS k`. Tes Release terkait dijalankan ulang: 96 lulus (Application) + 31 lulus (UI), 0 gagal.
- **Authenticode:** 0 sertifikat code signing; EXE dan MSI `NotSigned`; `signtool verify /pa`
  menolak keduanya; uji `sign-paket.ps1` dengan thumbprint palsu ditolak sebelum ada byte ditulis
  (SHA-256 kedua paket tidak berubah).

Bukti, log, dan panduan PC kasir: `docs\paket-bukti-2026-11-01\uji-fisik-1212\`.

## Yang belum dibuktikan (tetap terbuka)

- Cetak ke printer thermal 80 mm dan printer jaringan 9100 fisik; laci uang.
- Pemindai barcode USB fisik (alur aplikasinya sudah lulus simulasi).
- Pemasangan dan peningkatan oleh pemilik di PC kasir, termasuk uji di Windows 10.
- Tanda tangan Authenticode (pembelian sertifikat ditunda oleh pemilik 2026-10-29).

Panduan uji perangkat fisik yang tinggal dijalankan: `docs\pos-uji-fisik.md` (langkah A1–A7,
kedua jalur cetak).
