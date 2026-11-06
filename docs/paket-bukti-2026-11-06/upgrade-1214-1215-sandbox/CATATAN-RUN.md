# Catatan uji peningkatan 1.2.14 → 1.2.15 di Windows Sandbox

Dua run dijalankan pada 6 November 2026 dengan **paket yang sama** (EXE/MSI 1.2.15 hasil
`build-pro` pukul 15:15). Run 1 merah karena **dua cacat harness**, bukan cacat produk;
setelah harness diperbaiki, run 2 hijau penuh. Kedua run disimpan apa adanya.

## Run 1 — `run1/` (kit v2, 15:21–15:38)

Rekap: **105 lulus, 2 gagal, 107 total** — `| rekap | FAIL | lulus=105 gagal=2 total=107 minimal=80`.

| Pemeriksaan | Hasil | Penyebab |
|---|---|---|
| `emulator ESC/POS loopback siap di port 9100` | FAIL — `listener tidak siap dalam 10 detik` | `Start-Job` di tamu 1.536 MB butuh lebih dari 10 detik. Emulator **tetap menerima 635 bita** dan seluruh pemeriksaan byte (`ESC @`, `Rp 30.000`, `ESC d 3`, tanpa `GS V`) lulus. |
| `log penolakan EXE menyebut pagar versi terhadap 1.2.15` | FAIL — `alasan versi tidak tercatat` | Log tamu sebenarnya berbunyi `Pagar versi: HKCU Version=1.2.15 lebih baru dari paket 1.2.14.` (`exe-1214-versi-guard.log`), tetapi regex harness masih mencari `1\.2\.13` karena port hanya mengganti teks biasa, bukan pola ber-*backslash*. |

Tidak ada pemeriksaan produk yang gagal. Paket tidak dibangun ulang.

## Perbaikan harness (bukan produk)

1. Batas tunggu emulator `20 × 500 ms` (10 detik) → `120 × 500 ms` (60 detik), dengan komentar
   bahwa tamu hanya 1.536 MB.
2. Penggantian versi ber-*escape* memakai sentinel: `1\.2\.13` → `1\.2\.14`, lalu
   `1\.2\.14` → `1\.2\.15`, ditambah pagar yang berhenti bila `1\.2\.13` masih tertinggal.

Kit hasil perbaikan adalah kit v3 yang disalin di folder ini (`uji-1214-1215.ps1`,
`run-sandbox.ps1`, `PANDUAN-KIT.md`, `hash-publish-1215.txt`, `KIT-SHA256SUMS.txt`, dan
`port-kit-1215.py`). Paket `.exe`/`.msi` tidak dikomit.

## Run 2 — `run2/` (kit v3, 15:40–16:04)

Rekap: **107 lulus, 0 gagal** — `| rekap | PASS | lulus=107 gagal=0 total=107`.

Kedua pemeriksaan yang gagal di run 1 kini PASS
(`emulator ESC/POS loopback siap di port 9100 | PASS | listener aktif`;
`log penolakan EXE menyebut pagar versi terhadap 1.2.15 | PASS | alasan versi tercatat`).
Sisa hasil sama dengan run 1: skema 18 → **23**, **lima** migrasi 019–023 dijalankan,
`/api/periods` hidup, periksa-diri **18 pemeriksaan / 0 gagal**, 393 berkas payload cocok,
sandi/produk/merek bertahan, aset web lama tersapu
(`index-BmCxycbD.js` + `index-CUfTbA7B.css` → `index-B1TuJsgD.js` + `index-DOW8Zhjg.css`),
dan semua pagar turun versi/kanal serta copot-tanpa-menghapus-data lulus.

## Keutuhan salinan

`run1/status.txt` dan `run2/status.txt` disalin byte-per-byte:
`3e50798e8a9d6a60afae057fbd865d7b2613e7f5888074f398592f789e02d877` dan
`ebb8fbf1c2134a5a9703300924a4dde25cc63a00eb787f560f59bce46567b6a4`, sama dengan berkas
di folder kit `C:\tmp\kit-uji-1214-1215-v2` dan `C:\tmp\kit-uji-1214-1215-v3`.

## Batas yang tetap berlaku

- Emulator ESC/POS adalah **perangkat lunak uji di dalam tamu**, bukan printer fisik.
- Windows Sandbox adalah Windows 11; uji Windows 10 dan perangkat fisik tetap milik pemilik.
- CI tidak membangun installer; bukti ini dari mesin pengembang.
