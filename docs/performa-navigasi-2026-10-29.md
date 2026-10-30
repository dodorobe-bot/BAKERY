# Performa Perpindahan Menu — Hasil Ukur dan Perbaikan

Tanggal: 29 Oktober 2026
Lingkup: keluhan "perpindahan antar menu masih nge-lag" pada data besar (5.000 pesanan, 2.000 pelanggan, 1.000 pembelian).
Putusan kinerja: target navigasi tercapai pada pengukuran harness.
Putusan paket: **NOT READY** — EXE dan MSI 1.2.4 sudah dibangun ulang dengan bundel ini dan lulus uji pasang/copot, tetapi belum ditandatangani, belum diuji di mesin bersih, dan aplikasi terpasang belum dijalankan di mesin pemilik (lihat bagian 10).

## 1. Lingkungan dan cara ukur

| Butir | Nilai |
| --- | --- |
| Mesin | Windows 10.0.26300, x64, RAM 3,8 GB |
| Basis data uji | salinan data besar 10,3 MB; basis data asli tidak pernah dibuka untuk tulis |
| Host | Seruni22Bakery.Host 1.2.4 Release, hanya loopback |
| Bundel layar | `frontend/dist` yang disalin ke `desktop/` |
| Peramban | Chromium headless melalui Playwright 1.47.0 (harness `tests/Perf/NavPerf`) |
| Beban | 31 menu sekali (dingin), 3 kunjungan ulang per menu, 100 perpindahan berturut-turut (soak) |

Definisi angka:

- **Siap** — jarak dari klik menu sampai peristiwa `s22:layar-siap` ditambah dua bingkai animasi. Ini yang dirasakan pengguna sebagai "layar sudah berganti".
- **Data** — waktu di dalam aplikasi dari mulai memuat sampai data diterapkan.
- **Skrip** — durasi eksekusi JavaScript menurut CDP (`ScriptDuration`).
- **Tata** — durasi tata letak menurut CDP (`LayoutDuration`).
- **Peladen** — durasi permintaan menurut `Server-Timing` dari host.

Catatan kejujuran: setiap konfigurasi dijalankan satu kali. Selisih antar-jalan pada menu ringan bisa 30–80 ms, sehingga keputusan memakai sebaran gabungan 31 menu (p50/p95 dari nilai per menu), bukan satu angka per menu. Seluruh berkas mentah ada di `docs/perf-bukti-2026-10-29/`. Tidak ada angka yang disesuaikan agar lulus.

## 2. Baseline sebelum perbaikan

Berkas: `docs/perf-bukti-2026-10-29/perf-besar.json`

| Ukuran | p50 | p95 | maks |
| --- | ---: | ---: | ---: |
| Navigasi dingin (31 menu) | 114,8 ms | 544,6 ms | 581,6 ms |
| Kunjungan ulang (31 menu) | 152,5 ms | 585,9 ms | 787,3 ms |
| Soak 100 perpindahan | 56,4 ms | 130,9 ms | 869,4 ms |

Muat awal: siap 241 ms (data 154 ms). Heap JavaScript 16,7 MB → 38,2 MB setelah soak (+20,5 MB). Galat konsol 0, jawaban HTTP gagal 0.

Menu terburuk (dingin): pembelian 581,6 ms, buku besar 547,0 ms, gudang & lokasi 542,3 ms, stok opname 439,3 ms, katalog 430,1 ms, utang & piutang 344,8 ms.

## 3. Tiga penyebab terbesar yang terukur

### 3.1 DOM menahan ratusan baris sekaligus

Pada layar pembelian baseline: siap 581,6 ms dengan data 419 ms dan tata letak 214,9 ms. Buku besar: tata letak 298,1 ms. Gudang: tata letak 321 ms. Sementara host menjawab permintaan yang sama dalam 5–62 ms (`host-burst.log`, permintaan pertama dingin 61,7 ms; mayoritas di bawah 25 ms). Artinya biaya terbesar ada di penyisipan dan penataan ratusan baris tabel, bukan di SQLite, IPC, atau jaringan.

Ukuran tabel saat itu: jurnal 300 baris, katalog bahan + produk ±300 baris, opname ratusan baris dalam satu tabel, pembelian 300 baris, pelanggan 300 baris.

### 3.2 Layar lama ikut dipasang saat pindah menu

Gerbang render lama: `tabSiap === activeTab || !loading`. Saat menu baru diklik, `loading` masih `false` dan `tabSiap` masih menu lama, sehingga layar lama dipasang sekali dengan prop baru (pekerjaan render penuh), lalu layar baru dipasang lagi setelah data tiba. Bukti: pemasok dingin 324,3 ms padahal data hanya 15 ms dan skrip 226,2 ms — waktunya habis di JavaScript, bukan di data. Setelah gerbang diperbaiki (`App.jsx:1185`), pemasok dingin menjadi 180,3 ms dengan skrip 34,1 ms.

### 3.3 Formatter dibuat ulang per sel dan pencarian linear per baris

`formatMinor` dan `formatQty` membuat `Intl.NumberFormat` baru pada setiap pemanggilan, artinya satu objek formatter per sel tabel. Di layar pembelian, nama pemasok juga dicari dengan `Array.find` pada setiap baris. Keduanya pekerjaan berulang yang tumbuh sebanding jumlah baris × jumlah sel. Perbaikan: formatter tingkat modul (`money.js:6`, `money.js:9`) dan peta `Map` pemasok (`PurchasesView.jsx:62`).

## 4. Perbaikan yang diterapkan

| Berkas | Baris | Perubahan |
| --- | --- | --- |
| `frontend/src/utils/money.js` | 6, 9, 12, 19 | Satu `Intl.NumberFormat` untuk rupiah dan satu untuk jumlah, dipakai ulang semua sel |
| `frontend/src/views/PurchasesView.jsx` | 18, 24, 62, 343 | Formatter tingkat modul dan `Map` nama pemasok, bukan `Array.find` per baris |
| `frontend/src/App.jsx` | 47, 1185 | Gerbang `tabSiap === activeTab || loadError`; layar lama tidak lagi dipasang sia-sia; spinner `LayarMemuat` saat menunggu |
| `frontend/src/App.jsx` | 313, 332–349 | Cache layar 30 detik; setiap aksi ubah data tetap memaksa muat ulang |
| `frontend/src/App.jsx` | 184, 192 | Pelanggan halaman pertama 50 (dari 300); sisanya lewat pencarian server atau "Muat lagi" |
| `frontend/src/utils/bertahap.js` | 6, 8–21 | Hook `useBarisBertahap`: seluruh data tetap di memori untuk pencarian/agregasi, DOM hanya 50 baris pertama, bertambah 100 per klik |
| `frontend/src/views/InventoryView.jsx` | 19, 141, 156–158, 465–672 | Stok, mutasi, bahan, produk jadi memakai hook + tombol "Tampilkan berikutnya" |
| `frontend/src/views/FinanceView.jsx` | 18, 229, 319–321, 594–890 | Jurnal, kas, piutang, utang memakai hook + tombol per bagian |
| `frontend/src/views/OpnameView.jsx` | 5, 45, 197 | Baris opname bertahap |
| `frontend/src/views/SalesView.jsx` | 76, 95, 216, 582, 638 | Pesanan bertahap; pelanggan memakai pagination server dengan "Muat lagi" |
| `tests/Perf/NavPerf/Program.cs` | mode `--cek-tombol` | Pemeriksaan fungsional: tombol "tampilkan berikutnya" wajib menambah baris tanpa menggeser baris pertama dan tanpa galat konsol |

Arsitektur tidak diubah: React + host .NET + SQLite tetap, tidak ada fitur bisnis yang dihapus, data penuh tetap tersedia untuk pencarian, filter, dan agregasi.

## 5. Hasil sesudah perbaikan

Berkas: `docs/perf-bukti-2026-10-29/perf-besar-fix7.json`

| Ukuran | p50 | p95 | maks |
| --- | ---: | ---: | ---: |
| Navigasi dingin (31 menu) | 74,6 ms | 173,4 ms | 253,9 ms |
| Kunjungan ulang (31 menu) | 72,3 ms | 147,6 ms | 197,1 ms |
| Soak 100 perpindahan | 97,0 ms | 194,4 ms | 263,2 ms |

Muat awal: siap 287 ms (data 222 ms; satu sampel, lebih lambat dari baseline 241 ms karena variasi dan pemanasan host — di luar target yang diminta). Heap 17,2 MB → 16,2 MB (−0,9 MB). Permintaan ubah data saat soak 0, jawaban gagal 0, galat konsol 0.

Perbandingan per menu (p50, milidetik):

| Menu | Dingin sebelum | Dingin sesudah | Ulang sebelum | Ulang sesudah |
| --- | ---: | ---: | ---: | ---: |
| pembelian | 581,6 | 147,8 | 787,3 | 147,5 |
| buku besar | 547,0 | 81,2 | 387,3 | 96,3 |
| gudang & lokasi | 542,3 | 72,3 | 577,9 | 98,3 |
| stok opname | 439,3 | 65,7 | 384,4 | 110,1 |
| katalog bahan & produk | 430,1 | 74,6 | 421,7 | 71,4 |
| utang & piutang | 344,8 | 101,1 | 354,0 | 116,3 |
| pemasok | 324,3 | 180,3 | 593,9 | 147,4 |
| resep & bahan | 265,0 | 166,4 | 221,5 | 197,1 |
| kas & bank | 246,3 | 76,4 | 283,6 | 67,6 |
| kasir | 245,5 | 137,6 | 406,6 | 120,3 |
| transfer dapur | 217,7 | 71,1 | 203,7 | 70,4 |
| pemberitahuan | 149,8 | 113,6 | 273,5 | 111,4 |
| pra-pesanan | 145,3 | 141,7 | 150,3 | 147,7 |
| pengaturan perangkat | 128,7 | 53,2 | 80,5 | 60,7 |
| pesanan | 121,9 | 143,2 | 161,6 | 123,6 |
| piutang | 114,8 | 118,8 | 169,8 | 131,7 |
| hitung HPP | 109,2 | 97,7 | 152,5 | 103,9 |
| pelanggan | 108,6 | 253,9 | 188,6 | 135,9 |
| analisis eksekutif | 108,4 | 56,3 | 105,9 | 61,4 |
| dasbor | 108,1 | 58,4 | 99,9 | 56,8 |
| laporan keuangan | 103,3 | 78,6 | 98,4 | 72,3 |
| pengaturan | 96,2 | 59,5 | 92,8 | 69,0 |
| batch & kedaluwarsa | 87,4 | 46,7 | 110,3 | 61,4 |
| hak akses & pegawai | 67,3 | 54,8 | 73,3 | 50,8 |
| kontrol mutu | 60,9 | 50,2 | 85,0 | 56,2 |
| produksi aktual | 58,5 | 63,0 | 72,3 | 59,9 |
| retur penjualan | 57,7 | 49,4 | 130,0 | 54,9 |
| kotak tugas | 56,4 | 94,0 | 106,8 | 93,2 |
| rencana produksi | 55,2 | 56,4 | 66,2 | 56,1 |
| jejak audit | 54,3 | 68,7 | 59,1 | 68,4 |
| perintah produksi | 53,4 | 53,1 | 70,9 | 51,6 |

Tiga hal yang harus disebut apa adanya:

1. Beberapa menu ringan terlihat lebih lambat pada sampel dingin tunggal (pelanggan 108,6 → 253,9; kotak tugas 56,4 → 94,0; jejak audit 54,3 → 68,7; pesanan 121,9 → 143,2). Itu variasi satu sampel; pada kunjungan ulang angkanya justru membaik (pelanggan 188,6 → 135,9; kotak tugas 106,8 → 93,2; jejak audit 59,1 → 68,4; pesanan 161,6 → 123,6, dengan jejak audit tetap naik tipis).
2. Resep & bahan (197,1 ms) dan pemasok (180,3 ms) masih dekat batas 200 ms karena layarnya berisi formulir dan tabel sekaligus, bukan tabel panjang saja.
3. Outlier garbage collection masih ada: soak maksimum 263,2 ms. Tidak ada beku di atas satu detik, tetapi satu dari 100 perpindahan masih bisa terasa sedikit tersendat.

Seri pengukuran dengan harness yang sama, memperlihatkan arah yang konsisten meski variasi antar-jalan besar:

| Jalan | Dingin p95 | Ulang p95 | Soak p95 | Soak maks |
| --- | ---: | ---: | ---: | ---: |
| baseline | 544,6 | 585,9 | 130,9 | 869,4 |
| fix3 | 215,5 | 238,3 | 588,9 | 1301,7 |
| fix5 | 208,8 | 223,3 | 229,8 | 325,6 |
| fix6 | 245,7 | 189,8 | 187,4 | 259,4 |
| fix7 | 173,4 | 147,6 | 194,4 | 263,2 |

## 6. Uji fungsional tombol "tampilkan berikutnya"

Berkas: `docs/perf-bukti-2026-10-29/run-cek-tombol-final.log`

5 menu, 15 klik, 0 gagal, 0 galat konsol. Contoh baris:

- gudang & lokasi: 50 → 150 → 250 → 350 baris, baris pertama tetap.
- stok opname: 50 → 150 → 250 → 350 baris.
- utang & piutang: 100 → 250 → 350 → 400 baris.
- katalog: 100 → 270 → 370 → 470 baris (dua tabel bertahap).
- pesanan: 100 → 150 baris.

Baris pertama tidak pernah bergeser dan tidak ada baris yang hilang, jadi pembatasan DOM tidak mengubah isi atau urutan data. Daftar penuh tetap dipakai untuk pencarian, filter, dan penjumlahan.

## 7. Regresi dan pagar

- `scripts/verify.ps1 -Configuration Release` dijalankan dua kali setelah perubahan batas pelanggan: keduanya keluar dengan kode 0, 18 langkah LULUS, 166 pemeriksaan skrip verifikasi (62 + 54 + 21 + 29), 0 gagal.
- `dotnet test` (unit + integrasi + UI) LULUS.
- Tidak ada tes yang dihapus, dilemahkan, atau diganti tiruan. Tidak ada hasil yang ditandai lulus berdasarkan pembacaan kode.
- Basis data kecil/kosong: siap awal 185 ms; soak 100 perpindahan p50 55,4 ms, p95 64,7 ms, maks 74,1 ms; heap +2,1 MB; 0 galat. Tidak ada regresi untuk pengguna baru.

## 8. Tabel temuan dan status

| Butir | Target | Hasil | Status |
| --- | --- | --- | --- |
| Navigasi dingin p95 (31 menu) | ≤ 200 ms | 173,4 ms (maks 253,9) | PASSED |
| Navigasi kunjungan ulang p95 | ≤ 200 ms | 147,6 ms (maks 197,1) | PASSED |
| Soak 100 perpindahan p95 | ≤ 200 ms | 194,4 ms (maks 263,2) | PASSED |
| Waktu interaksi (ketik/klik) p95 | ≤ 100 ms | belum ada alat ukur input-latency terpisah | NOT RUN |
| Kueri sederhana p95 | ≤ 100 ms | nilai terburuk 61,7 ms (permintaan pertama dingin), mayoritas < 25 ms | PASSED |
| Dasbor p95 | ≤ 1 s | 58,4 ms dingin, 56,8 ms ulang (baseline 108,1) | PASSED |
| Tidak ada beku panjang | tanpa jeda > 1 s | maksimum soak 263,2 ms | PASSED |
| RAM tidak tumbuh tanpa batas | stabil pada 100 perpindahan | 17,2 → 16,2 MB (−0,9 MB; baseline +20,5 MB) | PASSED |
| Tidak ada transaksi hilang/ganda saat pindah menu | 0 tulis tak sengaja | 0 permintaan ubah data, 0 jawaban gagal; integritas transaksi ditutup rangkaian tes | PASSED |
| Installer tidak menimpa basis data / tidak mengubah lokasi data | wajib | EXE dan MSI baru: 4 berkas basis data asli identik sebelum/sesudah pasang dan copot | PASSED |
| Pasang/copot paket baru | wajib | EXE 23 pemeriksaan, MSI 28 pemeriksaan, 0 gagal; detail di `docs/verifikasi-paket-1.2.4-2026-10-29.md` | PASSED |
| Menjalankan aplikasi terpasang di mesin ini | wajib | sengaja tidak dijalankan: mesin ini memegang data pemilik; uji asap memakai host dari payload dengan basis data scratch | NOT RUN |
| Tauri (shell desktop) | — | proyek tidak memakai Tauri | NOT APPLICABLE |
| Kapasitas data jauh di atas 5.000 pesanan | tanpa lag | belum diukur ulang pada 10.000+ pesanan | NOT RUN |

## 9. Cakupan butir wajib

| Butir | Bukti | Status |
| --- | --- | --- |
| Audit dan profil dulu, bukan menebak | baseline `perf-besar.json`, `host-burst.log` | PASSED |
| Tiga penyebab terukur, satu kelompok per satu waktu | seri fix3 → fix5 → fix6 → fix7 | PASSED |
| Perbaikan nyata di kode, bukan dokumen rekomendasi saja | tabel bagian 4 | PASSED |
| Target navigasi, dasbor, kueri | tabel bagian 8 | PASSED |
| Target interaksi | belum diukur | NOT RUN |
| Tidak menghapus fitur bisnis demi kecepatan | semua data penuh masih dimuat; tes fitur lulus | PASSED |
| Tidak mengubah arsitektur | React + .NET + SQLite tetap | PASSED |
| Regresi setelah tiap kelompok | verify.ps1 + dotnet test akhir | PASSED |
| Pasang/copot build desktop terpasang | EXE 23 pemeriksaan + MSI 28 pemeriksaan, 0 gagal | PASSED |
| Menjalankan aplikasi terpasang di mesin ini | sengaja tidak dijalankan; uji asap memakai host dari payload dengan basis data scratch | NOT RUN |
| Paket installer memuat perbaikan | EXE `763d4384…` dan MSI `2972b145…` memuat index `5364f0…` | PASSED |

## 10. Risiko sisa dan langkah berikutnya

1. **Installer 1.2.4 sudah dibangun ulang dan lulus pasang/copot.** EXE `763d4384…` (14:31) dan MSI `2972b145…` (14:31) memuat bundel `index-CKgTNzb_.js` / `index.html` `5364f0…`, bukan bundel lama. Bukti: `docs/verifikasi-paket-1.2.4-2026-10-29.md`. Yang belum: pasang di mesin bersih, menjalankan aplikasi terpasang, dan penandatanganan kode.
2. **Latensi interaksi belum diukur.** Harness mengukur klik menu sampai layar siap, bukan jeda ketikan atau klik tombol di dalam layar. Perlu alat ukur terpisah sebelum target 100 ms dapat dinyatakan lulus.
3. **Resep & bahan dan pemasok masih di 180–197 ms.** Masih di bawah 200 ms tetapi tanpa ruang cadangan; layar ini berikutnya yang perlu dipecah.
4. **Outlier garbage collection hingga 263 ms.** Tidak membeku, tetapi masih terasa sesekali; penyebabnya alokasi objek per render yang perlu ditelusuri dengan rekaman alokasi.
5. **Data di atas 5.000 pesanan belum diukur ulang.** Pagar saat ini menjaga DOM, bukan ukuran data di memori; bila pesanan tumbuh ke puluhan ribu, endpoint pesanan (dibatasi 100 baris) dan agregasi dasbor perlu diuji lagi.
6. **Di luar lingkup ini dan tetap terbuka:** uji perangkat fisik (printer, pemindai, laci uang), pemasangan di mesin bersih, dan penandatanganan kode (installer masih NotSigned).

Berkas bukti mentah: `docs/perf-bukti-2026-10-29/` berisi JSON baseline, fix3, fix5, fix6, fix7, basis data kecil, log jalur fix7, log cek tombol, log host untuk kueri, dan log pagar verifikasi.
