# Verifikasi Paket 1.2.6 — Audit Prapilot, 29 Oktober 2026

Lingkup: dua cacat yang tersisa dari audit prapilot (urutan otorisasi pembatalan dan agregasi jurnal yang memuat entri pembalik), pengerasan dua tes UI yang rapuh, tes regresi baru, lalu kemasan ulang 1.2.6 serta verifikasi isi paket.
Keputusan: **READY FOR LIMITED PILOT** — perbaikan terbukti lewat tes dan lewat binari yang benar-benar dikemas. Pemasangan MSI 1.2.6 di mesin ini dan di mesin bersih **belum dijalankan**, paket tetap `NotSigned`, dan perangkat kasir fisik di luar lingkup.

## 1. Cacat yang ditutup pada 1.2.6

| Cacat | Gejala | Perbaikan | Bukti |
|---|---|---|---|
| Urutan otorisasi pembatalan | Pegawai yang mencoba membatalkan pesanan menerima 404 (pesanan dianggap tidak ada) alih-alih 403; jawaban bergantung pada pemilik pesanan | Pemeriksaan peran `sales-cancel` dipindah **sebelum** pagar cakupan kasir di `PUT /api/orders/{id}/status` | `Kasir_TidakBisaMengubahPesananKasirLain` (403) dan `verify-batch3` baris "Pegawai membatalkan pesanan (sales-cancel) ditolak" → 403 |
| Agregasi jurnal mengabaikan entri pembalik | Ringkasan rentang hanya menjumlah `Posted`, sehingga dokumen yang dibatalkan hilang dari jurnal dan rekap; angka tidak saling meniadakan | Semua penjumlahan memakai `Status IN ('Posted','Reversed')`; daftar tetap `Posted` saja | `ReportingAggregateTests` menulis pasangan asli `Reversed` + pembalik `Posted` dan menuntut saldo bersih (Debit 138.010 − Kredit 88.000 = 50.010) |

Dua tes UI juga diperkeras: `Katalog_MenampilkanKolomStokProdukJadi` dan `MenuPemasok_MembukaTabPemasok` kini menunggu **isi** layar (`TungguTeksAsync`), bukan hanya munculnya elemen `main`; sebelumnya asersi bisa membaca teks "memuat layar…" saat bundel masih dimuat.

## 2. Regresi otomatis

`dotnet test BakeryDesktop.slnx -c Release` pada sumber yang dipaketkan:

| Proyek | Lulus | Gagal |
|---|---:|---:|
| Application | 48 | 0 |
| Domain | 57 | 0 |
| Infrastructure | 118 | 0 |
| UI (Playwright) | 91 | 0 |
| **Total** | **314** | **0** |

Suite dijalankan tiga kali terpisah (pipeline build akhir, dua pengulangan stabilitas) dengan hasil sama. Tes UI membaca bundel dari `desktop/`, yang disinkronkan dari `frontend/dist` sebelum dijalankan.

Kontrol negatif dijalankan untuk dua perbaikan: mengembalikan sumber lama membuat `ReportingAggregateTests` gagal (jumlah entri 6 ≠ 7) dan tes cakupan kasir gagal (200 ≠ 404). Setelah pemulihan, hash sumber diperiksa dan suite kembali hijau.

## 3. Artefak 1.2.6

| Berkas | Ukuran | SHA-256 | Dibuat |
| --- | ---: | --- | --- |
| `Seruni22Bakery_Setup_v1.2.6.exe` | 42.631.595 bita | `4ebb5962ad2f0379959f2c044359d39d353d4db1c10e2b7f4204809f68726512` | 2026-10-29 19:21 |
| `Seruni22Bakery_v1.2.6_x64.msi` | 50.357.095 bita | `461d1c771a5ba58b0dc944fc9eb3e9768b8640abd437fc72ca52d65744e353c1` | 2026-10-29 19:29 |

Keduanya `NotSigned`. Peluncur melaporkan versi berkas 1.2.6, host 1.2.6.0. MSI ProductCode build ini `{EDCBBD96-732D-4854-A31B-0A36E6C43E77}`; UpgradeCode tetap `B975F792-6F80-4D56-8A64-D274A044DCF1`. Identitas ini dibaca langsung dari tabel Property MSI, bukan dari berkas definisi.

## 4. Isi paket (MSI, ekstraksi administratif)

| Pemeriksaan | Hasil |
| --- | --- |
| Ekstraksi administratif | exit 0, 393 berkas aplikasi |
| Nama berkas vs `dist/publish-single` | 393 vs 393, **tidak ada selisih nama** |
| Byte-per-byte | total 135.742.559 bita di kedua sisi |
| `Seruni22Bakery.Host.dll` | `c9a5b020386536e2092bf2f7dfa9e0e0e7c611fff3b8519f158887a83b90020e` — sama |
| `Seruni22Bakery.exe` | `bc51aa7b01f425e8fae379beb8ff46d19be863ee63f3718f4e9417350986d981` — sama |
| `wwwroot/index.html` | `298108b0bae6ba8bc550a3f3adcd41ff65e210bf86c22f4931ef38b46ee946f8` — sama dengan `frontend/dist` dan `desktop/` |
| Bundel frontend | `index-BPNiweyi.js` 579.183 bita, `index-Bz7F3T1f.css` 55.997 bita |
| Versi dari payload | host 1.2.6.0 (ProductVersion 1.2.6), peluncur 1.2.6 |

## 5. Uji perilaku pada binari payload

Host dari hasil ekstraksi MSI dijalankan langsung (bukan salinan build) dengan basis data sementara dan kunci JWT sementara; empat skrip verifikasi API dijalankan berurutan:

| Skenario | Pemeriksaan | Gagal |
|---|---:|---:|
| `verify-batch2` | 62 | 0 |
| `verify-batch3` | 54 | 0 |
| `verify-batch6` | 21 | 0 |
| `verify-bug09` | 29 | 0 |
| **Total** | **166** | **0** |

`/health` menjawab `version 1.2.6`; baris "Pegawai membatalkan pesanan (sales-cancel) ditolak" menghasilkan `http=403`, yaitu perilaku yang menjadi inti perbaikan 1.2.6.

Basis data nyata tidak tersentuh: ukuran dan waktu ubah `seruni22bakery.db` (446.464 bita, 2026-10-28 21:46), `-wal` (2026-10-29 10:10), dan `-shm` (2026-10-29 10:19) identik sebelum dan sesudah uji. Uji asap hanya menambah baris log di `%LOCALAPPDATA%\Seruni22Bakery\logs`.

## 6. Yang belum diuji pada 1.2.6

- Pasang/copot MSI 1.2.6 di mesin ini: **NOT RUN** (perMachine, butuh shell administrator).
- Pasang/copot EXE 1.2.6: **NOT RUN**. Mekanisme Inno tidak berubah dari 1.2.5 yang lulus pasang/copot senyap; yang berubah hanya versi dan isi host.
- Peningkatan 1.2.5 → 1.2.6 di mesin bersih: **NOT RUN**.
- Menjalankan aplikasi hasil paket di mesin pemilik: dilarang (data pemilik).
- Penandatanganan Authenticode: **BLOCKED** (sertifikat belum ada; keputusan pemilik menunda).
- Perangkat kasir fisik: **BLOCKED** (printer thermal, pemindai barcode, laci uang ditunda atas permintaan pemilik).

## 7. Kesimpulan

Dua cacat sisa audit tertutup dan dijaga tes regresi yang terbukti merah pada sumber lama. Paket 1.2.6 memuat perbaikannya secara byte-per-byte, dan binari yang dikemas lulus 166 pemeriksaan API termasuk perilaku 403 pembatalan. Keputusan tetap **READY FOR LIMITED PILOT** dengan syarat: uji perangkat fisik di PC kasir, pasang di mesin pemilik, uji peningkatan di mesin bersih, dan cadangan off-machine sebelum dipakai. Verdict **READY FOR PRODUCTION** belum bisa diberikan selama paket `NotSigned` dan pemasangan MSI 1.2.6 belum diuji.

Log mentah: `C:\tmp\verify-payload-126b.log` (166 pemeriksaan), `C:\tmp\build-msi-126.log`, `C:\tmp\audit-fulltests3.log`, `C:\tmp\stab-1.log`, `C:\tmp\stab-2.log`; metadata paket di `dist\installer\BUILD-INFO-MSI.txt`.
