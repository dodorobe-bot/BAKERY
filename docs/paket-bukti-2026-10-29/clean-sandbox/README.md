# Bukti uji MSI 1.2.4 di lingkungan Windows bersih

Dijalankan 2026-10-29 15:42–15:45 (+07:00) di Windows Sandbox: Windows 11 Enterprise
build 26100, tanpa jaringan (`<Networking>Disable</Networking>`), RAM VM 1536 MB,
pengguna `WDAGUtilityAccount` (elevated). Sandbox dipakai karena tidak ada mesin
fisik/VM terpisah yang tersambung ke mesin ini.

## Berkas

| Berkas | Isi |
|---|---|
| `status.txt` | 30 baris hasil per pemeriksaan (`PASS`/`FAIL` + rincian), diakhiri `rekap | PASS | lulus=30 gagal=0` |
| `validasi.log` | transkrip PowerShell lengkap dari skrip yang sama |
| `msi-install.log` | log `msiexec /i ... /qn /l*v`; "Installation completed successfully", status 0 |
| `msi-uninstall.log` | log `msiexec /x ... /qn /l*v`; "Removal completed successfully", status 0 |
| `host-uji.log` | keluaran host dari folder hasil pasang; migrasi 1..17, `/health`, SPA, aset |
| `validasi.ps1` | skrip yang dijalankan (identik dengan `C:\tmp\clean-msi-124\share\validasi.ps1`) |
| `Seruni22-clean2.wsb` | konfigurasi sandbox yang dipakai |

## Ringkasan hasil

- Lingkungan terbukti bersih: `C:\Program Files\dotnet` tidak ada, tidak ada folder
  program maupun entri ARP Seruni sebelum pemasangan.
- Salinan MSI di dalam sandbox diverifikasi utuh: 50.357.095 bita,
  SHA-256 `2972b145e3fd77df2500b654d9af290421d03ce52e1fbb1416333c8ca7f23114`.
- Pasang `/qn`: exit 0; 393 berkas; `hostfxr.dll` + `coreclr.dll` ada; peluncur 1.2.4;
  host 1.2.4.0; `wwwroot\index.html` SHA-256 `5364f0b3…`;
  `wwwroot\assets\index-CKgTNzb_.js` 573.778 bita, SHA-256 `665bb9bd…`;
  tanpa `appsettings.Development.json`, PDB, basis data, atau `jwt.key`.
- ARP "Seruni 22 Bakery" 1.2.4; pintasan Start Menu dan Public Desktop menunjuk
  `C:\Program Files\Seruni 22 Bakery\Seruni22Bakery.exe`; status tanda tangan NotSigned.
- Host dijalankan dari folder hasil pasang pada basis data scratch `C:\data\uji.db`
  port 5292: `/health` 200 `version=1.2.4`, SPA 200 (466 bita), aset 200 (573.778 bita),
  17 migrasi diterapkan (skema 17).
- Copot `/qn`: exit 0; folder program, kedua pintasan, dan entri ARP hilang;
  `uji.db` dan `uji.key` di luar folder program tetap ada; tidak ada proses tersisa.

## Catatan kejujuran

Percobaan pertama (15:33–15:36, folder `bukti-run1` di mesin, tidak diarsipkan di sini)
gagal pada dua pemeriksaan: entri ARP dan copot (`msiexec` exit 1605). Penyebabnya
konstanta ProductCode di skrip uji masih memakai nilai build 10:34
(`{DE2E612D-…}`), sedangkan WiX membuat ProductCode baru untuk setiap build. Paket
tidak diubah. Nilai yang benar untuk paket ini `{B1734125-F339-4527-AD56-CC888D911FC0}`,
dan `dist\installer\BUILD-INFO-MSI.txt` sudah dikoreksi. Percobaan kedua dengan nilai
yang benar lulus 30/30.
