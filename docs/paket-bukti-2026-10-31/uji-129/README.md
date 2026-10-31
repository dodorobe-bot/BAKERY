# Bukti uji paket 1.2.9 — 2026-10-31

Folder ini memuat dua hal yang berbeda dan harus dibaca terpisah:

1. Bukti yang **benar-benar lulus** pada mesin ini (isi paket, perilaku biner payload,
   penolakan EXE saat MSI terpasang).
2. Perangkat uji pasang/peningkatan di Windows Sandbox yang **belum berhasil
   dijalankan**, beserta alasan kegagalannya.

## Bukti yang lulus

| Berkas | Isi |
|---|---|
| `periksa-msi-129.log` | 15 pemeriksaan MSI: ukuran/hash, ekstraksi administratif, 393/393 berkas identik, versi 1.2.9, bundel, tabel `File`, `Property`, dan `LaunchCondition` |
| `payload-129.log` | 25 pemeriksaan biner payload: empat skrip verifikasi (169 pemeriksaan perilaku), `/health` 1.2.9, 18 migrasi, periksa-diri 9/9, `/api/brand` empat kunci |
| `exe-guard-msi-127.log` | Log Inno saat EXE 1.2.9 dijalankan di mesin berisi MSI 1.2.7: pagar kanal menemukan entri Program dan Fitur MSI lalu membatalkan (`InitializeSetup returned False`) |

Rincian naratif: `docs\verifikasi-paket-1.2.9-2026-10-31.md`.

## Kit siap salin

Kit untuk mesin uji ≥8 GB dirakit dengan:

```
powershell -ExecutionPolicy Bypass -File scripts\siapkan-kit-uji-129.ps1
```

Keluaran bawaan: `C:\tmp\kit-uji-129` (9 berkas, 177,5 MB) berisi dua MSI, dua EXE,
manifes payload, `uji-129.ps1`, `run-sandbox.ps1`, `KIT-SHA256SUMS.txt`, dan
`README-UJI.md` (salinan `PANDUAN-KIT.md`). Salin folder itu ke mesin uji, lalu:

```
powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1
```

`run-sandbox.ps1` memetakan folder kit sendiri ke `C:\share` (tidak perlu `C:\tmp`
atau hak administrator), memakai tamu 2 GB, dan menulis `uji-jalan.wsb` saat berjalan.
Pemeriksaan awal tanpa meluncurkan sandbox:

```
powershell -ExecutionPolicy Bypass -File .\run-sandbox.ps1 -Periksa
```

## Skrip tamu

`uji-129.ps1`, 13 bagian:

1. pastikan mesin bersih;
2. periksa keutuhan keempat berkas paket;
3. pasang MSI 1.2.7;
4. isi data lewat API 1.2.7 (sandi, produk, merek);
5. EXE 1.2.9 harus menolak berjalan selama MSI terpasang;
6. tingkatkan MSI 1.2.7 → 1.2.9 dan bandingkan 393 berkas dengan manifes;
7. buka basis data lama dengan 1.2.9: sandi/produk/merek utuh, skema 18,
   `/api/brand` empat kunci, periksa-diri 9/9;
8. MSI 1.2.7 harus ditolak di atas 1.2.9;
9. copot MSI 1.2.9: folder/pintasan/ARP hilang, data di luar folder tetap ada;
10. pasang EXE 1.2.9 per pengguna, periksa penanda HKCU dan 393 berkas;
11. jalankan host dari folder EXE pada basis data baru;
12. pagar versi EXE dan penolakan MSI saat EXE terpasang;
13. copot EXE: folder program hilang, folder data pengguna tetap ada.

Skrip memanggil `Cek` lebih dari 80 kali; rekap hanya PASS bila tidak ada FAIL **dan**
total pemeriksaan minimal 60 (`lulus=NN gagal=0 total=NN`). Rekap dengan lulus belasan
berarti run terpotong (sandbox mati di tengah jalan) dan dicatat FAIL.

Skrip ini menolak berjalan bila bukan di dalam Windows Sandbox (`WDAGUtilityAccount`),
karena ia memanggil `shutdown.exe` dan menulis penanda ke folder data pengguna.
WAL basis data sengaja tidak dihapus setelah host dihentikan paksa: transaksi yang
sudah commit bisa masih berada di WAL.

### Kegagalan 2026-10-31 11:41

Peluncuran pertama pada mesin ini gagal sebelum satu pun pemeriksaan berjalan:

- `sandbox-gagal-runner.log` — runner hanya mencatat "sandbox diluncurkan"; tidak ada
  status dari tamu selama lebih dari 14 menit.
- `sandbox-gagal-memori.log` — System event log:
  `Microsoft-Windows-Resource-Exhaustion-Detector` ID 2004 pada 11:41:43:
  "Windows successfully diagnosed a low virtual memory condition …
  vmmemWindowsSandbox consumed 1200 (MB)".

Mesin uji hanya punya 3.746 MB RAM. Sisa proses VM (`vmwp`, `vmmemWindowsSandbox`)
baru hilang setelah mesin dijalankan ulang. Peluncur baru menolak berjalan di bawah
4 GB dan memberi peringatan di bawah 6 GB.

### Hasil 2026-10-31: atestasi pemilik di mesin ≥8 GB

Setelah kit ini dijalankan oleh pemilik di mesin ≥8 GB, pemilik melaporkan secara tertulis
`lulus=65 gagal=0 total=65` — 13 bagian lulus tanpa kegagalan. Berkas `status.txt`,
`runner.log`, dan folder `bukti\` **tidak** sampai ke mesin perakit (share jaringan tidak
terjangkau, flashdisk berisi media lain, `C:\tmp\hasil-129` tidak pernah ada), dan pemilik
kemudian menerima atestasi tertulis sebagai dasar keputusan. Karena itu:

- Hasil ini berstatus **atestasi**, bukan verifikasi log.
- Bila berkas asli kelak sampai, isinya harus dicocokkan dengan atestasi sebelum dipakai
  sebagai bukti biasa.
- Jangan menaikkan status ke READY FOR PRODUCTION atas dasar atestasi ini; itu menuntut
  berkas bukti yang dapat diperiksa dan paket bertanda tangan.

