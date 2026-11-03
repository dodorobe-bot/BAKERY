# Verifikasi paket 1.2.13 — 2026-11-03

Dokumen ini mencatat apa yang **sudah dijalankan** dan apa yang **belum** untuk paket
Seruni 22 Bakery 1.2.13. Angka di bawah dibaca ulang dari berkas laporan, bukan dari ingatan.

## Putusan

**NOT READY.** Gerbang dan automasi lulus penuh pada konfigurasi Release, tetapi pasang /
peningkatan / copot di Windows Sandbox belum dijalankan untuk versi ini, perangkat fisik
(printer thermal 80 mm, printer jaringan, pemindai barcode, laci uang) belum diuji, Windows 10
belum diuji, dan paket belum bertanda tangan.

## Artefak

| Berkas | Ukuran | SHA-256 | Tanda tangan |
|---|---|---|---|
| `Seruni22Bakery_Setup_v1.2.13.exe` | 42.672.292 | `c1a0af29bd8f5bf9c439cadef3c1d8c891da0c49a3271384192fd682c444ed9d` | NotSigned |
| `Seruni22Bakery_v1.2.13_x64.msi` | 50.416.132 | `eb3f40fb0fa2fe1fb7e09bee733db524a8b29fb55af367b1bd8d0df1f5afd132` | NotSigned |

Sumber: `dist/installer/SHA256SUMS.txt` dan `dist/installer/BUILD-INFO.json`.

Build dibungkus dari commit **`5993072`** dengan **pohon sumber bersih** (`pohonKotor=0`;
`kotorArtefakBuild=5` adalah laporan yang baru saja ditulis build itu sendiri). UpgradeCode
`B975F792-6F80-4D56-8A64-D274A044DCF1` sama dengan paket 1.2.4–1.2.12, jadi peningkatan
menggantikan pemasangan lama, bukan menumpuk.

Cara membangun ulang:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-pro.ps1 -SinkronkanBundelDesktop
```

## Yang dijalankan hari ini

| Pagar | Hasil | Bukti |
|---|---|---|
| `verify.ps1 -Configuration Release` | **32 langkah PASS, 0 gagal, 0 SKIP**, 239,9 s | `dist/verify/verify-20261103-172525.json` |
| xUnit unit + integrasi + UI/Playwright | **523 lulus, 0 gagal, 0 dilewati** (dijumlah dari log, bukan dari exit code) | `dist/verify/verify-20261103-172525.test.log` |
| Verifikasi perilaku lewat API nyata | **169 pemeriksaan, 0 gagal** — batch2 63, batch3 56, batch6 21, bug09 29 — host loopback + DB sementara | tabel `[PASS]` di laporan gerbang |
| `build-pro.ps1` | **12 langkah, 0 gagal, 0 dilewati, 1 peringatan** (penimpaan atas `-IzinkanTulisUlang`) | `dist/installer/BUILD-INFO.json` |
| `validate-paket.ps1` | **19 pemeriksaan, 0 gagal, 0 lewati, 0 peringatan** | `dist/installer/VERIFY-REPORT.json` |
| Isi payload MSI vs manifest publish | **393/393 berkas** cocok hash dan ukuran | `dist/publish-single.manifest.txt` |
| Publish bebas berkas terlarang | lulus — tidak ada basis data, kunci JWT, kredensial, atau berkas debug | `VERIFY-REPORT.json` |
| Kontrol negatif automasi (`uji-build-pro.ps1`) | **14/14 lulus, 0 SKIP** — baseline tetap hijau sebelum dan sesudah sabotase | [`docs/paket-bukti-2026-11-03/kontrol-1213/uji-build-pro-rilis-1213.json`](paket-bukti-2026-11-03/kontrol-1213/uji-build-pro-rilis-1213.json) |
| Kontrol negatif diulang terhadap skrip yang sudah di-commit | **14/14 lulus, 0 SKIP** — hash EXE/MSI setelah run tetap `c1a0af29…`/`eb3f40fb…`, jadi artefak rilis tidak tersentuh sabotase | [`docs/paket-bukti-2026-11-03/kontrol-1213/uji-build-pro-rilis-1213-ulang.json`](paket-bukti-2026-11-03/kontrol-1213/uji-build-pro-rilis-1213-ulang.json) |

Kontrol negatif membuktikan validator menolak: hash EXE salah, MSI tak tercantum, manifest payload
salah, MSI hilang, versi EXE tertukar, kunci di folder publish, laporan menyebut artefak hantu,
nama ber-`..` di SHA256SUMS, EXE hilang (tanpa crash), manifest hilang (tidak boleh jadi SKIP), dan
`JsonPath` di luar repo/temp. Penjaga versi terbit menolak build ulang dan **tidak** menghapus
laporan paket yang sudah ada.

## Perbaikan yang masuk 1.2.13 (tidak ada di paket 1.2.12)

1. **Keamanan jaringan** — host menolak alamat dengar di luar loopback, dari `--urls` maupun
   `Kestrel:Endpoints`, sebelum kunci JWT dan basis data dibuat; LAN hanya atas
   `SERUNI_ALLOW_LAN=1`. 20 tes unit + 1 tes proses yang juga memastikan tidak ada DB/kunci yang
   tertinggal. Tidak ada skrip atau instalator yang membuka port firewall; diperiksa sebagai pagar
   statis di gerbang.
2. **Konsistensi data** — nilai yang menempel pada stok produk jadi sama dengan nilai yang dipakai
   HPP; SPK tanpa unit baik membebani gaji/operasional; sisa pembulatan harga rata-rata masuk beban
   selisih persediaan.
3. **Backup** — retensi mengutamakan salinan yang sudah terverifikasi.
4. **Idempotensi** — kunci dari proses mati di tengah jalan kedaluwarsa 30 jam (UTC dihitung di
   SQL); baris selesai dibersihkan setelah 30 hari.
5. **Antar muka** — status perangkat dipakai ulang 15 s dan disegarkan saat pengaturan berubah;
   daftar piutang/utang dimuat bertahap dengan penanda "hanya baris yang termuat".
6. **Automasi** — laporan JSON tertelusur per run, jumlah tes dibaca dari log, bukti kegagalan
   disimpan di `dist/verify/bukti`, tiga pagar statis, `sign-paket` memilih versi dari laporan
   build (bukan abjad), `dotnet test` tanpa satu pun tes tercatat menggagalkan build, jalur
   `OutRoot` bertanda junction/symlink ditolak, dan hitungan "pohon kotor" memisahkan kode sumber
   dari artefak yang ditulis build.

Skema basis data: **18** — tidak ada migrasi baru, jadi pemasangan di atas data lama tidak
menyentuh struktur.

## Belum diuji untuk 1.2.13

- Pasang EXE, pasang MSI, **peningkatan 1.2.12 → 1.2.13**, penolakan turun versi, dan copot tanpa
  menghapus data pengguna, di Windows Sandbox bersih.
- Cetak ke printer thermal 80 mm dan ke printer jaringan fisik; jalur ESC/POS raw baru terbukti
  sampai emulator loopback.
- Potong kertas, pemindai barcode fisik (hanya lulus simulasi wedge 14/14 pada 1.2.12), laci uang.
- Pemasangan oleh pemilik di PC kasir sungguhan.
- Windows 10 x64 (semua uji hari ini di Windows 11 26300).
- Tanda tangan Authenticode: 0 sertifikat terpasang, `signtool verify /pa` akan menolak.

## Batas bukti

Peralatan uji berada pada PC dengan RAM 3,7 GB; hasil "lulus" di atas adalah hasil satu mesin.
Angka 1.2.12 (`lulus=105` di Sandbox) adalah bukti untuk paket itu, **bukan** untuk 1.2.13.
