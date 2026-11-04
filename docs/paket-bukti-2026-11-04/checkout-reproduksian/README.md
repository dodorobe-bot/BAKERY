# Keter-reproduksian berkas bukti setelah `git clone`

Dokumen ini mencatat cacat dan perbaikannya: sampai 2026-11-04, tidak ada yang bisa menjawab
pertanyaan **"kalau orang lain meng-clone repositori ini, apakah berkas bukti yang dia pegang
sama byte dengan yang dicatat saat pengujian?"** Jawabannya ternyata tidak.

## 1. Cacat yang ditemukan

Mesin ini memakai `core.autocrlf=true`. Saat checkout, git menormalkan akhir baris LF menjadi
CRLF. Untuk berkas naskah itu tidak berbahaya, tapi folder `docs/paket-bukti-*` berisi dua hal
yang justru dihitung byte-per-byte:

1. **Skrip kit sandbox** yang SHA-256-nya tercatat di `KIT-SHA256SUMS.txt`. Contoh konkret:
   `docs/paket-bukti-2026-11-01/uji-1212/run-sandbox.ps1` tercatat
   `9fe9e633edb4434c46cdd1ed56202cc7d544c6cf64fcccd0d823a773275627bb` dengan panjang **6930
   bita**; hasil clone **7120 bita** dengan digest `dbee6bb7eb2be606c05b92461e2f4923269441405da750de132c81d9eb4922ce`.
   Yang diperiksa orang lain bukan lagi berkas yang dibuktikan.
2. **Manifestnya sendiri.** Karena `KIT-SHA256SUMS.txt` ikut berpindah akhir baris, `sha256sum -c`
   menolak nama berkas yang dibaca (baris `\r` menempel di nama). Jadi bukti itu tidak bisa
   diverifikasi bahkan terhadap dirinya sendiri.

Penyebab kedua muncul dari pengecekan pertama: sebuah berkas bukti yang berubah byte tidak bisa
lagi dipercaya sebagai bukti, dan sebagian laporan JSON gerbang di dalamnya memuat digest.

## 2. Perbaikan

Satu aturan di `.gitattributes`, di samping `*.bin binary` yang sudah ada lebih dulu:

```
docs/paket-bukti-*/** -text
```

`-text` mematikan seluruh konversi akhir baris untuk isi folder bukti, sehingga blob dan hasil
checkout adalah bita yang sama, di mesin mana pun dan pada konfigurasi git apa pun.

Pagar baru `scripts/uji-checkout-bukti.ps1` menjaganya tetap hijau. Pagar itu **meng-clone
pohon ke folder temporer dengan `core.autocrlf=true`** — sengaja mengulangi konfigurasi yang
merusak, bukan mengelaknya — lalu memeriksa tiga hal pada hasil clone:

| Pemeriksaan | Cara | Kenapa begini |
| --- | --- | --- |
| Byte tiap berkas bukti sama dengan blob | `git ls-files -s` vs `git hash-object --no-filters --stdin-paths` | `git status --porcelain` **tidak** bisa dipakai: status membandingkan setelah filter konversi baris, jadi buta terhadap cacat yang dicari. Versi pertama pagar ini memakai `git status` dan hasilnya hijau palsu. |
| Aturan `-text` benar-benar berlaku | `git check-attr text -- <skrip kit>` harus `text: unset` | Membedakan "aturannya jalan" dari "kebetulan isi berkasnya sudah LF" |
| Digest tercatat di manifest cocok dengan hasil clone | tiap baris `KIT-SHA256SUMS.txt` dihitung ulang | Ini pertanyaan asli pengguna: bukti yang dikit harus bisa diverifikasi penerima |

Artefak `.exe`/`.msi` tidak dikomit, jadi entri manifest untuk keduanya dicatat **lewati**, bukan
lulus. Keluar 0 = tidak ada FAIL; keluar 1 = ada berkas bukti yang berubah; keluar 2 = pohon yang
ditunjuk bukan repositori git, yang dicatat pemanggil sebagai **BELUM DIPERIKSA**, bukan lulus.

Pagar ini dipasang sebagai langkah 4 `scripts/verify.ps1` (`Catat 'keter-reproduksian berkas
bukti'`), sehingga gerbang rilis tidak bisa lagi hijau tanpa bukti yang bisa direproduksi.

## 3. Tiga kali jalan, dan apa artinya

Perintah pemakaiannya:

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\uji-checkout-bukti.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\uji-checkout-bukti.ps1 -Repo <pohon-lain>
```

| Berkas log | Kondisi pohon | Hasil |
| --- | --- | --- |
| `kontrol-negatif-head-tanpa-aturan.txt` | HEAD yang belum memuat aturan `docs/paket-bukti-*/** -text` (kontrol negatif) | `69 lulus, 195 gagal, 16 lewati` — **exit 1** |
| `lulus-head-dengan-aturan.txt` | HEAD yang **sudah** memuat aturan (commit `2b8fa45`) | `251 berkas` dibandingkan, `-text berlaku`, `268 lulus, 0 gagal, 16 lewati` — **exit 0** |
| `lewat-pohon-tanpa-git.txt` | folder tanpa `.git` | `[LEWAT] ... bukan repositori git` — **exit 2**, dicatat sebagai belum diperiksa |

Angka kontrol negatif adalah cacat terukurnya: **178 dari 247** berkas bukti yang tracked di
folder `docs/paket-bukti-*` pada saat itu ditulis ulang oleh `git clone` sebelum aturan ada. 195
kegagalan itu terbagi atas 178 berkas berubah byte, 16 digest kit yang tidak cocok lagi dengan
manifestnya, dan 1 penjaga `check-attr`. Setelah aturan berlaku, ketiganya nol dan 16/16 digest
kit yang tercatat di manifest cocok lagi. Run hijau memegang 251 berkas, bukan 247, karena empat
berkas bukti di folder ini sendiri ikut dihitung setelah aturannya dikomit.

## 4. Cara membaca hasil pada pohon ini sekarang

Menjalankan pagar di `D:\bakery-desktop` **saat aturannya masih di working tree** akan
melaporkan gagal seperti baris pertama tabel di atas — dan itu benar, bukan regresi. Yang diuji
adalah hasil clone dari HEAD, jadi aturan harus ikut dikomit lebih dulu supaya hijau. Jangan
"memperbaiki" warna itu dengan menghapus pemeriksaan byte atau membandingkannya lewat
`git status`: keduanya mengembalikan hijau palsu.

Berkas log di folder ini disalin apa adanya dari keluaran skrip, dengan akhir baris LF, dan
termasuk pola `docs/paket-bukti-*/** -text` sehingga ia sendiri terverifikasi oleh pagar ini.
