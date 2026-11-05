; =====================================================================
; Seruni 22 Bakery Desktop - Professional Inno Setup 6 Script
; Fitur:
; - Tanpa jendela console: backend berjalan tersembunyi via peluncur exe native
;   (WinExe .NET Framework, tanpa ketergantungan VBScript)
; - Offline penuh: hasil publish self-contained sudah membawa runtime .NET 8
; - Data pengguna di %LOCALAPPDATA%\Seruni22Bakery, tidak pernah dihapus
;   oleh upgrade maupun uninstall (tidak ada entri UninstallDelete untuk data)
; =====================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.2.15"
#endif
#define MyAppVersionQuad MyAppVersion + ".0"

#define MyAppName "Seruni 22 Bakery"
#define MyAppPublisher "Seruni 22"
#define MyAppURL "http://localhost:5000"
#define MyAppExeName "Seruni22Bakery.exe"
#define RootDir "..\..\..\"
; Payload bisa diarahkan ke folder publish lain (build percobaan) lewat
; /DPayloadDir=... dari scripts\build-installer.ps1. Bawaannya dist\publish-single.
#ifndef PayloadDir
  #define PayloadDir RootDir + "dist\publish-single"
#endif

[Setup]
AppId={{5D9D507C-B7C6-4C4B-A6EE-6786A01F909A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersionQuad}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} - Sistem Toko & Pabrik Roti
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
DefaultDirName={autopf}\Seruni22Bakery
DisableDirPage=no
AllowNoIcons=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#RootDir}dist\installer
OutputBaseFilename=Seruni22Bakery_Setup_v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
SetupIconFile=app.ico
UninstallDisplayName={#MyAppName} (Sistem Toko & Pabrik Roti)
UninstallDisplayIcon={app}\app.ico
CreateUninstallRegKey=yes
; Saat upgrade, proses host perlu ditutup agar berkas tidak terkunci. Data pengguna
; tidak ikut disentuh karena berada di luar folder instalasi.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "default"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Registry]
; Penanda kanal dan versi untuk pagar lintas-installer. MSI membaca Channel=exe di
; HKCU dan berhenti; installer ini membaca Channel=msi di HKLM dan berhenti. Keduanya
; memakai folder data dan port yang sama, jadi dua salinan tidak boleh terpasang.
; Nilai Version dipakai memasang pagar turun versi (lihat InitializeSetup).
Root: "HKCU"; Subkey: "Software\Seruni22Bakery"; ValueType: string; ValueName: "Channel"; ValueData: "exe"; Flags: uninsdeletevalue
Root: "HKCU"; Subkey: "Software\Seruni22Bakery"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletevalue
Root: "HKCU"; Subkey: "Software\Seruni22Bakery"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletevalue

[Files]
; Paket produksi: host .NET self-contained, aset web hasil build, peluncur exe.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "app.ico"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Aset frontend memakai nama ber-hash, sehingga build baru meninggalkan aset lama
; yang tidak lagi dirujuk index.html. Folder aset dibersihkan dulu agar upgrade
; tidak menumpuk bundel usang.
Type: filesandordirs; Name: "{app}\wwwroot\assets"
; Versi 1.2.2 dan sebelumnya memakai peluncur .vbs. Mesin skrip VBScript tidak
; selalu tersedia di Windows 11, jadi berkas lama itu dibersihkan saat upgrade.
Type: files; Name: "{app}\Seruni22Bakery.vbs"

[Icons]
; Shortcut menunjuk peluncur exe di folder instalasi. Peluncur menyalakan host
; tanpa jendela konsol, menunggu /health, lalu membuka peramban.
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; IconFilename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; IconFilename: "{app}\app.ico"
; Shortcut uninstall di dalam folder instalasi agar persis seperti gambar yang diinginkan
Name: "{app}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; IconFilename: "{uninstallexe}"

[Run]
; Pemasangan senyap (mis. skrip penyebaran atau uji mesin bersih) tidak boleh menyalakan
; aplikasi: di mesin pemilik, peluncur langsung membaca database asli. `skipifsilent`
; menangani /SILENT maupun /VERYSILENT; sudah dibuktikan dengan Inno Setup 6.7.3 pada
; 2026-10-29 (pemasangan senyap: tidak ada satu pun entri [Run] yang dijalankan).
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: postinstall skipifsilent nowait

[UninstallRun]
; Matikan proses backend dan peluncur saat uninstall agar file tidak terkunci.
; Hanya proses program yang dihentikan; tidak ada perintah yang menghapus folder
; data pengguna.
Filename: "taskkill"; Parameters: "/f /im Seruni22Bakery.Host.exe"; Flags: runhidden; RunOnceId: "KillBakeryHost"
Filename: "taskkill"; Parameters: "/f /im Seruni22Bakery.exe"; Flags: runhidden; RunOnceId: "KillBakeryLauncher"

[Code]
// ── Pagar kanal dan turun versi ─────────────────────────────────────────────
// Dua kanal installer (EXE per-pengguna dan MSI per-mesin) memakai folder data dan port
// yang sama. Kalau keduanya terpasang, pengguna punya dua salinan yang berebut satu basis
// data, jadi pemasangan kanal kedua dihentikan dengan pesan yang bisa ditindaklanjuti.
//
// Versi lama juga tidak boleh dipasang di atas versi baru: basis data yang sudah dimigrasi
// oleh versi baru tidak selalu terbaca oleh kode lama.
function BandingkanVersi(A, B: String): Integer;
var
  pa, pb, va, vb: Integer;
begin
  Result := 0;
  while (A <> '') or (B <> '') do
  begin
    pa := Pos('.', A);
    if pa > 0 then
    begin
      va := StrToIntDef(Copy(A, 1, pa - 1), 0);
      A := Copy(A, pa + 1, Length(A));
    end
    else
    begin
      va := StrToIntDef(A, 0);
      A := '';
    end;

    pb := Pos('.', B);
    if pb > 0 then
    begin
      vb := StrToIntDef(Copy(B, 1, pb - 1), 0);
      B := Copy(B, pb + 1, Length(B));
    end
    else
    begin
      vb := StrToIntDef(B, 0);
      B := '';
    end;

    if va <> vb then
    begin
      if va > vb then Result := 1 else Result := -1;
      Exit;
    end;
  end;
end;

function MsiTerpasang(): Boolean;
var
  kanal: String;
  akar: Integer;
  daftar: TArrayOfString;
  i, j: Integer;
  tampil: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'Software\Seruni22Bakery', 'Channel', kanal)
     and (CompareText(kanal, 'msi') = 0) then
  begin
    Log('Pagar kanal: penanda HKLM Software\Seruni22Bakery\Channel = msi ditemukan.');
    Result := True;
    Exit;
  end;
  // MSI 1.2.7 dan lebih lama tidak menulis penanda Channel, jadi penanda saja tidak
  // cukup: entri "Program dan Fitur" milik MSI tetap ada dan dicari dari namanya.
  for i := 0 to 1 do
  begin
    if i = 0 then akar := HKLM64 else akar := HKLM32;
    if RegGetSubkeyNames(akar, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', daftar) then
    begin
      for j := 0 to GetArrayLength(daftar) - 1 do
      begin
        if RegQueryStringValue(akar,
             'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + daftar[j],
             'DisplayName', tampil)
           and (CompareText(tampil, 'Seruni 22 Bakery') = 0) then
        begin
          Log('Pagar kanal: entri Program dan Fitur MSI ditemukan di Uninstall\' + daftar[j] + '.');
          Result := True;
          Exit;
        end;
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  versiTerpasang: String;
begin
  Result := True;

  if MsiTerpasang() then
  begin
    // /SUPPRESSMSGBOXES tidak menahan MsgBox buatan skrip; tanpa penjaga ini,
    // pemasangan senyap akan menunggu klik selamanya. Dalam mode senyap cukup gagal.
    if not WizardSilent() then
      MsgBox('Seruni 22 Bakery versi MSI sudah terpasang di komputer ini.' + #13#10 + #13#10 +
             'Copot dulu lewat "Aplikasi terpasang" (Program dan Fitur), baru pasang versi EXE ini. ' +
             'Dua kanal instalasi tidak boleh terpasang bersamaan karena memakai basis data dan port yang sama.',
             mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if RegQueryStringValue(HKCU, 'Software\Seruni22Bakery', 'Version', versiTerpasang)
     and (BandingkanVersi(versiTerpasang, '{#MyAppVersion}') > 0) then
  begin
    Log('Pagar versi: HKCU Version=' + versiTerpasang + ' lebih baru dari paket {#MyAppVersion}.');
    if not WizardSilent() then
      MsgBox('Versi ' + versiTerpasang + ' sudah terpasang, sedangkan paket ini versi {#MyAppVersion}.' + #13#10 + #13#10 +
             'Versi lama tidak bisa dipasang di atas versi baru. Pakai paket versi ' + versiTerpasang + ' atau lebih baru, ' +
             'atau copot dulu versi yang terpasang (data pengguna tidak ikut terhapus).',
             mbError, MB_OK);
    Result := False;
  end;
end;
