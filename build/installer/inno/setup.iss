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
  #define MyAppVersion "1.2.7"
#endif
#define MyAppVersionQuad MyAppVersion + ".0"

#define MyAppName "Seruni 22 Bakery"
#define MyAppPublisher "Seruni 22"
#define MyAppURL "http://localhost:5000"
#define MyAppExeName "Seruni22Bakery.exe"
#define RootDir "..\..\..\"

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

[Files]
; Paket produksi: host .NET self-contained, aset web hasil build, peluncur exe.
Source: "{#RootDir}dist\publish-single\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
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
