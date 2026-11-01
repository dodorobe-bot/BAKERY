# Bukti uji cetak 1.2.10 pada pencetak virtual — 2026-11-01

Dokumen ini mencatat apa yang **benar-benar terjadi** ketika jalur cetak aplikasi dijalankan
pada mesin ini, dan apa yang **tidak** bisa disimpulkan dari itu.

## Alat dan lingkungan

Harness yang sudah ada di repo, dijalankan tanpa perubahan:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\pos-physical-check.ps1 `
  -PrinterName "Microsoft Print to PDF" -PaperWidthMm 58 -AllowPrint -Configuration Release
```

- Windows 11 Pro 10.0.26300 64-bit, .NET 8.0.32, sesi non-administrator.
- Host berjalan pada basis data sementara di folder `temp` — basis data pemilik tidak dibuka.
- Printer yang dipakai adalah pencetak virtual bawaan Windows, **bukan** printer thermal.

## Yang lulus

| Pemeriksaan | Hasil |
|---|---|
| `POST /api/devices/test-print` | HTTP 200, `jobId=1`, `Struk terkirim ke printer.` |
| `POST /api/orders/1/receipt` | HTTP 200, `jobId=2` |
| Status pekerjaan | `job #1 Test status=Printed attempts=1`, `job #2 Receipt status=Printed attempts=1` |
| Keluaran fisik | dua PDF nyata dibuat: 04:36:14 dan 04:36:27 (berkas baru, bukan menimpa berkas lain) |
| Isi struk pesanan | merek, `ORD-20261101-0001`, tanggal cetak/pesanan/pickup, pelanggan, `2 x Rp 12.500 → Rp 25.000`, `Total`, `DP / Uang Muka`, `Sisa Tagihan`, sampai baris penutup |
| Isi struk uji | `UJI CETAK PERANGKAT`, nama printer, `58 mm / 32 kolom`, waktu, baris contoh, `SELESAI` |
| Pesanan tidak berubah | total `2500000 / 2500000`, status `0 / 0`, hanya satu pesanan uji tercatat |

Ini menutup celah bukti yang selama ini ada: sebelumnya jalur cetak hanya terbukti lewat
adaptor palsu di tes unit. Di sini `PrintDocument` + spooler Windows benar-benar dipakai dan
menghasilkan dokumen.

## Yang TIDAK terbukti

- Kertas thermal 58/80 mm sungguhan, hasil potong, kualitas huruf, dan perilaku saat kertas
  habis. Semua itu tetap POS-01…06 di `docs\pos-uji-fisik.md`.
- "Printed" berarti spooler menerima pekerjaan, bukan bukti kertas keluar.

## Dua temuan

### 1. Driver mengabaikan lebar kertas yang diminta

Permintaan aplikasi memakai `PaperSize("Struk 58mm", …)`
(`src\Host.Service\Devices\WindowsSpoolerPrinter.cs:47`), tetapi keluarannya ber-`MediaBox`
`[0 0 612 792]` = Letter, dan `Get-PrintConfiguration` melaporkan `PaperSize : Letter`.

Artinya: **driver boleh menolak ukuran kertas kustom tanpa memberi tahu aplikasi.** Aplikasi
tidak punya cara mendeteksi ini. Pada printer thermal yang driver-nya menghormati ukuran
kustom, hasilnya bisa berbeda dari yang diuji di sini. Teks struk memang tidak terpotong,
tetapi itu kebetulan aman karena halaman uji lebih lebar, bukan karena ada pagar.

### 2. Bug nominal pada struk uji (diperbaiki)

Baris contoh transaksi mencetak `2 x Rp 15.000 → Rp 300`. Uang disimpan sebagai unit minor,
sehingga `Money.Format(30_000)` = Rp 300, bukan Rp 30.000. Nilai yang benar 3.000.000.

- Letak bug: `src\Application\Devices\ReceiptRenderer.cs:135`.
- Kenapa lolos: tes `RenderTest_*` hanya memeriksa judul, nama printer, dan lebar kolom —
  baris contoh tidak pernah diperiksa.
- Perbaikan: `Money.Format(3_000_000)` + tes regresi
  `RenderTest_ContohTransaksiKonsisten` di `tests\Application.Tests\ReceiptRendererTests.cs`.
- Kontrol negatif: dengan kode lama tes baru itu **MERAH**
  (`Expected baris "2 x Rp 15.000 … Rp 300" to end with "Rp 30.000"`); dengan kode perbaikan
  seluruh `ReceiptRenderer` HIJAU (17/17) dan seluruh solusi HIJAU (394 tes, 0 gagal).
- Perbaikan ini **tidak ada** di paket 1.2.10 (paket dibangun 03:45/03:57, sebelum perbaikan).
  Ia masuk ke 1.2.12. Versi 1.2.11 sempat disiapkan untuk perbaikan ini tetapi **tidak pernah
  diterbitkan**; paket 1.2.12 memuat perbaikan struk dan jalur ESC/POS raw sekaligus.

## Berkas di folder ini

| Berkas | Isi |
|---|---|
| `uji-cetak-virtual-58mm.log` | transkrip lengkap harness, termasuk rekap HASIL OTOMATIS |
| `bukti.txt` | bukti yang ditulis harness ke folder sementara |
| `host.out` | log host uji |
| `keluaran-1-struk-uji.pdf`, `keluaran-2-struk-pesanan.pdf` | keluaran cetak asli |
| `ekstraksi-1-struk-uji.txt`, `ekstraksi-2-struk-pesanan.txt` | teks hasil `pdftotext -layout` |
| `periksa-antrean-dan-keluaran.ps1` | pemeriksa antrean cetak + mencari berkas keluaran |
| `periksa-media-box.ps1` | pemeriksa metadata PDF (ukuran halaman, font, perkakas ekstraksi) |

Peringatan praktis: pencetak virtual di mesin ini menyimpan hasil cetak ke
`Documents\test.pdf` / `test2.pdf` tanpa bertanya. Berkas itu keluaran uji, bukan dokumen
pemilik, dan boleh dihapus.
