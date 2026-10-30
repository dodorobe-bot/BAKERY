# Notifikasi Internal & MCP Lokal — Yang Benar-Benar Berjalan

Dokumen ini menjelaskan dua kemampuan yang ditambahkan pada versi 1.2.0: kotak **notifikasi
internal** yang tersimpan di SQLite, dan **server MCP lokal** lewat stdio dengan empat tool baca.
Isinya mengikuti kode yang benar-benar ada, bukan rencana. Semua yang belum diuji disebut apa
adanya di bagian akhir.

Batas yang tegas: **tidak ada WhatsApp, Telegram, email/SMTP, atau layanan pesan luar apa pun di
aplikasi ini.** Tidak ada tabel antrean keluar, provider, webhook, token API, atau tombol "kirim".
Notifikasi berhenti di basis data lokal dan layar aplikasi.

Tanggal pemeriksaan: 2026-10-28. Lingkungan: Windows 11 Pro 10.0.26300 64-bit, .NET 8.

---

## 1. Arsitektur aktual

Sama seperti komponen lain, notifikasi dan MCP memakai jalur yang sudah ada:

```
React (Vite)  ──HTTP loopback + JWT──▶  ASP.NET Core host (.NET 8)
                                              │
                                              ├─ NotificationService ─▶ SQLite (tabel Notifications)
                                              └─ McpToolbox ──────────▶ service aplikasi yang sama
                                                                       (Analytics, TaskInbox, dll.)
```

- Tidak ada Tauri. Antarmuka tetap dilayani host lokal, dan peramban tidak pernah membuka SQLite.
- MCP berjalan sebagai **proses kedua** dari executable yang sama dengan saklar `--mcp-stdio`,
  memakai berkas basis data yang sama. Ia tidak membuka jalur tulis transaksi; tool-nya hanya
  membaca lewat service aplikasi.

## 2. Skema notifikasi (migrasi 015)

Tabel `Notifications` (versi skema 15):

| Kolom | Jenis | Aturan |
|---|---|---|
| `Id` | INTEGER | primary key, autoincrement |
| `EventType` | TEXT | wajib; sengaja tanpa CHECK agar jenis baru tidak menuntut migrasi |
| `Severity` | TEXT | wajib, default `Info`; CHECK `Info` / `Warning` / `Critical` |
| `Title`, `Body` | TEXT | wajib, teks yang dibaca pengguna |
| `EntityType`, `EntityId` | TEXT / INTEGER | rujukan ke dokumen sumber (opsional) |
| `RecipientUserId` | INTEGER | wajib, FK ke `Users(Id)` `ON DELETE CASCADE` |
| `DedupeKey` | TEXT | wajib, **unik** |
| `IsRead` | INTEGER | wajib, default 0; CHECK 0 atau 1 |
| `ReadAt` | TEXT | waktu dibaca (UTC), null bila belum |
| `CreatedAt` | TEXT | wajib |
| `MetadataJson` | TEXT | ruang tambahan, belum dipakai tool mana pun |

Indeks: `ux_notifications_dedupe` (unik), `idx_notifications_inbox`
(`RecipientUserId, IsRead, CreatedAt DESC`), `idx_notifications_event` (`EventType, CreatedAt DESC`).

Satu baris ditujukan ke **satu pengguna**, sehingga status baca tidak saling mengganggu. Menghapus
pengguna ikut menghapus kotaknya; riwayat transaksi tidak hilang karena audit dan jurnal terpisah.

Rollback tersedia di `015_InternalNotifications.down.sql` dan **menghapus seluruh riwayat
notifikasi** — hanya untuk basis data uji atau setelah cadangan dibuat.

## 3. Jenis peristiwa dan penerimanya

Notifikasi transaksi ditulis oleh endpoint setelah transaksi berhasil (atau gagal) tersimpan.
Penulisan notifikasi tidak pernah menggagalkan transaksi: kegagalannya hanya dicatat ke log.

| EventType | Keparahan | Penerima | Dipicu oleh |
|---|---|---|---|
| `transaction.order.created` | Info | Owner, Admin + pembuat pesanan | `POST /api/orders` berhasil |
| `transaction.order.failed` | Warning | pembuat pesanan | `POST /api/orders` ditolak |
| `transaction.order.paid` | Info | Owner, Admin | `POST /api/orders/{id}/payment` berhasil |
| `transaction.order.payment_failed` | Warning | pembuat pembayaran | pembayaran ditolak |
| `transaction.order.cancelled` | Warning | Owner, Admin | status pesanan menjadi Batal |
| `transaction.order.status` | Info | Owner, Admin, Pegawai | perubahan status pesanan lain |
| `transaction.return.created` | Warning | Owner, Admin | `POST /api/returns` berhasil |
| `transaction.purchase.received` | Info | Owner, Admin, Dapur | penerimaan barang PO berhasil |
| `transaction.purchase.receive_failed` | Warning | penerima | penerimaan barang ditolak |
| `transaction.production.completed` | Info | Owner, Admin, Dapur, Pegawai | penyelesaian SPK berhasil |
| `transaction.production.failed` | Warning | penyelesai SPK | penyelesaian SPK ditolak |
| `transaction.production.cancelled` | Warning | Owner, Admin, Dapur | SPK dibatalkan |
| `transaction.qc.passed` | Info | Owner, Admin, Dapur | pemeriksaan mutu lulus |
| `transaction.qc.failed` | Critical | Owner, Admin, Dapur | pemeriksaan mutu gagal |
| `stock.low` | Warning | Owner, Admin, Dapur | bahan di bawah batas minimum |
| `batch.expiry` | Warning / Critical | Owner, Admin, Dapur | lot hampir / sudah kedaluwarsa |
| `task.pickup.overdue` | Warning | Owner, Admin, Pegawai | pesanan lewat jadwal ambil |
| `task.inbox` | Warning | per peran yang punya tugas | ringkasan kotak tugas harian |
| `report.daily` | Info | Owner, Admin | ringkasan hari sebelumnya |

Aturan penyusunan pengingat (`SinkronkanOperasionalAsync`):

- `stock.low` hanya untuk bahan dengan stok di bawah `MinStockAlert`.
- `batch.expiry` memakai status "segera" (Warning) dan "kedaluwarsa" (Critical).
- `task.inbox` dibuat satu per peran per hari, hanya bila kotak peran itu tidak kosong.
- `report.daily` hanya dibuat bila hari sebelumnya punya pesanan, pendapatan, atau beban; hari
  kosong tidak dibuatkan ringkasan palsu.
- Pengguna nonaktif tidak pernah menerima pengingat.

## 4. Kunci dedupe: sekali per kondisi per hari

Setiap baris berakhir dengan akhiran pengguna, misalnya
`stock.low:12:2026-10-28:u3`, `batch.expiry:45:2026-10-28:u3`,
`task.inbox:Owner:2026-10-28:u1`, `report.daily:2026-10-27:u1`.

Akibatnya:

- Penyegaran boleh berjalan sesering apa pun; kondisi yang sama tidak menumpuk.
- Besok kondisinya masih sama? Pengingat baru dibuat karena tanggal bisnis berubah — memang
  tujuannya mengingatkan setiap hari, bukan sekali seumur hidup.
- Notifikasi transaksi memakai id dokumen (atau idempotency key) sehingga pengiriman ulang
  permintaan yang sama tidak menggandakan pemberitahuan.

Penyegaran berjalan otomatis lewat `NotificationWorker` setiap **10 menit** sejak host hidup, dan
bisa dipicu manual dari layar (`POST /api/notifications/sync`).

## 5. Endpoint dan layar

| Endpoint | Guna |
|---|---|
| `GET /api/notifications?unreadOnly=&limit=` | kotak pengguna yang sedang masuk (batas 200) |
| `GET /api/notifications/summary` | total, belum dibaca, kritis belum dibaca |
| `POST /api/notifications/{id}/read` | tandai satu; 404 bila bukan miliknya |
| `POST /api/notifications/read-all` | tandai semua miliknya |
| `POST /api/notifications/sync` | susun pengingat operasional sekarang; mengembalikan jumlah baris baru |

Semua endpoint memakai `RequireAuthorization()` dan selalu memakai id pengguna dari token —
tidak ada parameter yang membuka kotak pengguna lain. `Severity` dikirim sebagai teks
(`Info`/`Warning`/`Critical`), bukan angka enum.

Layar **Integrasi & Notifikasi** menampilkan ringkasan (Total / Belum Dibaca / Kritis Belum
Dibaca), daftar notifikasi dengan lencana keparahan dan titik belum dibaca, tombol
"Segarkan Pengingat", "Tandai Semua", dan "Tandai dibaca" per baris. Tombol "Buka" berpindah ke
layar sumber sesuai `EntityType`:

| EntityType | Layar |
|---|---|
| `SalesOrder` | orders (Penjualan) |
| `PurchaseOrder` | purchases (Pembelian) |
| `WorkOrder` | production_orders (Produksi) |
| `QualityCheck` | quality_control |
| `Ingredient` | stock_catalog |
| `ItemBatch` | batch_expiry |
| `SalesReturn` | sales_returns |

Karena isinya dibaca dari SQLite, riwayat tetap ada setelah aplikasi ditutup dan dibuka kembali.
Tidak ada tombol kirim ke layanan luar, tidak ada indikator "terkirim" palsu.

## 6. MCP lokal lewat stdio

### 6.1 Menjalankan

MCP bukan layanan yang menyala sendiri saat aplikasi dipasang. Ia dijalankan oleh klien MCP
sebagai proses anak:

```
%LOCALAPPDATA%\Programs\Seruni22Bakery\Seruni22Bakery.Host.exe --mcp-stdio
```

Konfigurasi tambahan lewat argumen atau `%LOCALAPPDATA%\Seruni22Bakery\config\appsettings.json`:

| Kunci | Arti |
|---|---|
| `Mcp:UserId` | id akun internal yang menjadi identitas agen (wajib) |
| `Database:Path` | basis data yang dibaca; bawaan `%LOCALAPPDATA%\Seruni22Bakery\seruni22bakery.db` |
| `Jwt:SigningKeyFile` | kunci sesi host; bawaan `%LOCALAPPDATA%\Seruni22Bakery\jwt.key` |

Mode MCP tidak menyalakan server web dan tidak menyajikan berkas layar, jadi
`Desktop:StaticFilesPath` tidak diperlukan di sana.

Contoh entri klien MCP:

```json
{
  "mcpServers": {
    "seruni22": {
      "command": "C:\\Users\\<nama>\\AppData\\Local\\Programs\\Seruni22Bakery\\Seruni22Bakery.Host.exe",
      "args": ["--mcp-stdio"],
      "env": { "Mcp__UserId": "2" }
    }
  }
}
```

Proses MCP memakai basis data yang sama dengan aplikasi. Seperti host biasa, ia menjalankan
migrasi yang tertunda saat start — jadi jangan jalankan pada salinan basis data yang belum
dicadangkan bila versinya lebih lama.

### 6.2 Protokol

- Satu pesan JSON-RPC 2.0 per baris di stdin; satu jawaban per baris di stdout.
- Metode: `initialize`, `ping`, `tools/list`, `tools/call`. Versi protokol `2024-11-05` (juga
  menerima `2025-03-26` dan `2025-06-18`).
- Kode galat: `-32700` JSON tidak valid, `-32601` metode tidak dikenal, `-32603` galat internal.
- Di mode ini **stdout hanya untuk protokol**; log Serilog dialihkan ke berkas.
- Notifikasi tanpa `id` (mis. `notifications/initialized`) tidak dibalas.

### 6.3 Empat tool, semuanya baca

| Tool | Hak akses | Keluaran |
|---|---|---|
| `get_daily_report` | peran dengan `finance-read` (Owner, Admin) | pendapatan, beban, selisih pendapatan − beban, nilai & jumlah pesanan, unit terjual; argumen `date` opsional `YYYY-MM-DD` |
| `get_task_list` | semua peran aktif | kotak tugas milik peran agen |
| `get_notification_summary` | semua peran aktif | ringkasan kotak + 20 notifikasi terbaru |
| `get_operational_status` | semua peran aktif | versi, hasil periksa integritas basis data, jumlah pengguna aktif, pesanan belum lunas, tugas menunggu |

Batas yang ditegakkan kode:

- Tidak ada SQL bebas, akses berkas, atau tool tulis. `inputSchema` memakai
  `additionalProperties: false`; argumen asing (mis. `sql`) ditolak.
- `date` harus `YYYY-MM-DD`, tidak boleh melewati hari ini, dan paling lama 366 hari ke belakang.
- Tanpa `Mcp:UserId`, atau bila akun agen tidak ada/nonaktif, **semua** pemanggilan ditolak.
- Peran diperiksa di service, bukan di skema: Pegawai yang memanggil `get_daily_report` ditolak
  walau mengirim argumen yang sah.

### 6.4 Jejak audit

Setiap pemanggilan, termasuk penolakan dan galat, menulis `AuditLog`:

- `Action` = `MCP <nama tool>`, `EntityType` = `McpTool`, `SessionId` = `mcp-stdio`.
- `NewValues` = JSON `{ tool, args, hasil: ok|ditolak|galat, alasan }`, dipotong 500 karakter.
- Dapat dibaca lewat layar Jejak Audit atau `GET /api/audit?entity=McpTool`.

Jejak ini yang membuat klaim "agen hanya membaca dan setiap permintaannya tercatat" bisa
diperiksa, bukan sekadar dijanjikan.

### 6.5 Pemulihan saat gagal

| Gejala | Penyebab yang mungkin | Tindakan |
|---|---|---|
| Semua tool dijawab "Mcp:UserId belum diisi" | identitas agen belum disetel | isi `Mcp:UserId` dengan id akun aktif |
| "Akun agen MCP tidak ditemukan atau sudah dinonaktifkan" | akun dihapus/dinonaktifkan | pilih akun aktif lain |
| "tidak berwenang membaca laporan keuangan" | peran tidak punya `finance-read` | pakai akun Owner/Admin, atau jangan panggil tool itu |
| Jawaban `-32603` | galat internal (mis. basis data terkunci) | periksa log berkas di folder log; ulangi; laporkan bila berulang |
| Proses langsung keluar | argumen/konfigurasi salah, basis data tidak dapat dibuka | jalankan tanpa `--mcp-stdio` untuk melihat pesan host, periksa log |

## 7. Bukti uji pada build 1.2.3

Seluruh 265 tes lulus (Application 46, Domain 57, Infrastructure 91, UI 71), di Debug dan Release.
Tiga tes UI tambahan adalah validasi data induk produk (`ProductValidationTests.cs`): permintaan
tanpa daftar resep, nama kosong, harga negatif, dan jumlah bahan tidak sah dijawab 400, bukan 500.
Dua tes sebelumnya adalah regresi tanggal pickup (`SalesPickupDateTests.cs`). Yang langsung
menyentuh dokumen ini:

| Berkas | Jumlah | Isi |
|---|---|---|
| `tests/Infrastructure.Tests/Migration015Tests.cs` | 6 | tabel + 3 indeks, FK ke `Users`, CHECK keparahan & `IsRead`, dedupe unik, penerima wajib ada, rollback 015 |
| `tests/Infrastructure.Tests/NotificationTests.cs` | 10 | dedupe, isolasi kotak per pengguna, ringkasan, tandai dibaca tidak bisa lintas pengguna, persistensi setelah koneksi dibuka ulang, stok menipis hanya untuk peran gudang sekali sehari, lot kedaluwarsa Critical, ringkasan harian kemarin untuk Owner/Admin, pengguna nonaktif tidak menerima |
| `tests/UI.Tests/Tests/NotificationApiTests.cs` | 3 | tanpa sesi 401; pesanan nyata menulis notifikasi dan status baca bisa ditelusuri; penyegaran berulang tidak menggandakan |
| `tests/UI.Tests/Tests/McpStdioTests.cs` | 5 | initialize + tools/list + panggilan sah; penolakan tool asing/argumen salah/metode tak dikenal; tanpa akun agen semua ditolak; Pegawai ditolak laporan harian tetapi tugas tetap boleh dan semuanya tercatat audit; notifikasi dari transaksi host terbaca dari proses MCP kedua |

Tes MCP menjalankan **proses sistem operasi kedua** pada basis data sementara yang sama, bukan
memanggil kelas di dalam proses tes. Itu bukti bahwa batas proses dan stdio benar-benar bekerja.

## 8. Yang belum diuji / batas jujur

- **Basis data nyata sudah dimigrasikan** ke skema 15 pada 2026-10-28 atas izin pemilik, setelah
  mekanismenya dibuktikan lebih dulu pada salinan persis (13 → 15). Sebelum migrasi dibuat dua
  cadangan berskema 13 di `%LOCALAPPDATA%\Seruni22Bakery\backups`
  (`manual-20261028-214555.db` dan `pre-upgrade-v13-to-v15-20261028-214556.db`, keduanya
  `integrity_check` ok) plus salinan dingin `C:\tmp\real-pre-migrate` (SHA-256 `3d116c61…`).
  Hasil pada DB nyata: skema 15, `integrity_check` ok, 0 pelanggaran foreign key, `Users` 1 dan
  `SalesOrders` 0 tetap, tabel `Notifications` + `PrintJobs` bertambah, `AuditLog` tidak berubah.
  Aplikasi 1.2.3 terpasang di atas DB nyata; peluncur resmi kini berupa
  `Seruni22Bakery.exe` (WinExe .NET Framework, tanpa jendela konsol dan tanpa mesin VBScript),
  bukan lagi `Seruni22Bakery.vbs`. Peluncur itu menjalankan host + UI (`/health` 200, `/` 200)
  tanpa berkas `launcher-error.txt`, dan DB nyata tetap identik sebelum/sesudah upgrade
  (skema 15, SHA-256 berkas utama tidak berubah). Login dengan sandi pemilik tidak dijalankan
  oleh agen karena sandi bawaan sudah diganti; itu satu-satunya langkah yang perlu dicoba
  pemilik sendiri.
- Notifikasi diuji lewat API dan layar pada basis data sementara. Belum ada uji "tutup aplikasi
  lalu buka lagi" pada instalasi terpasang; persistensi dibuktikan pada tingkat basis data
  (koneksi dibuka ulang) dan proses kedua.
- MCP belum diuji dengan klien MCP pihak ketiga sungguhan (mis. editor/agen tertentu); yang diuji
  adalah protokol JSON-RPC pada stdio dan tool-nya.
- Performa kotak notifikasi belum diukur pada riwayat puluhan ribu baris; batas baca 200 per
  permintaan dan indeks sudah ada, tetapi belum ada angka beban.
- Belum ada pembersihan/arsip notifikasi lama. Riwayat tumbuh terus mengikuti pemakaian.
