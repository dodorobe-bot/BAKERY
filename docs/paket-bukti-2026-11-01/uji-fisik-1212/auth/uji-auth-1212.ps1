$ErrorActionPreference = 'Continue'
$exe = 'D:\bakery-desktop\dist\installer\Seruni22Bakery_Setup_v1.2.12.exe'
$msi = 'D:\bakery-desktop\dist\installer\Seruni22Bakery_v1.2.12_x64.msi'
$sama = '0000000000000000000000000000000000000000'

Write-Host '== HASH SEBELUM =='
$sebelum = @{}
foreach ($f in @($exe, $msi)) {
    $h = (Get-FileHash $f -Algorithm SHA256).Hash
    $sebelum[$f] = $h
    Write-Host ((Split-Path -Leaf $f) + ' ' + $h)
}

Write-Host ''
Write-Host '== SERTIFIKAT CODE SIGNING TERPASANG =='
$jumlah = 0
foreach ($toko in @('Cert:\CurrentUser\My', 'Cert:\LocalMachine\My')) {
    $c = @(Get-ChildItem $toko -ErrorAction SilentlyContinue | Where-Object {
        ($_.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })
    })
    $jumlah += $c.Count
    Write-Host ($toko + ': ' + $c.Count + ' sertifikat code signing')
    foreach ($x in $c) { Write-Host ('  ' + $x.Subject + ' s/d ' + $x.NotAfter + ' kunci=' + $x.HasPrivateKey) }
}
Write-Host ('TOTAL sertifikat code signing: ' + $jumlah)

Write-Host ''
Write-Host '== PREFLIGHT sign-paket.ps1 DENGAN THUMBPRINT PALSU =='
$pesan = ''
try {
    & 'D:\bakery-desktop\scripts\sign-paket.ps1' -Thumbprint $sama | Out-Host
    $pesan = '(skrip tidak melempar galat)'
} catch {
    $pesan = $_.Exception.Message
}
Write-Host ('PESAN PENOLAKAN: ' + $pesan)
Write-Host ('KODE KELUAR: ' + $LASTEXITCODE)

Write-Host ''
Write-Host '== HASH SESUDAH =='
$berubah = 0
foreach ($f in @($exe, $msi)) {
    $h = (Get-FileHash $f -Algorithm SHA256).Hash
    $status = if ($h -eq $sebelum[$f]) { 'TIDAK BERUBAH' } else { 'BERUBAH'; }
    if ($h -ne $sebelum[$f]) { $berubah++ }
    Write-Host ((Split-Path -Leaf $f) + ' ' + $h + ' -> ' + $status)
}
Write-Host ('BERKAS BERUBAH: ' + $berubah)

Write-Host ''
Write-Host '== GET-AUTHENTICODESIGNATURE =='
foreach ($f in @($exe, $msi)) {
    $s = Get-AuthenticodeSignature $f
    Write-Host ((Split-Path -Leaf $f) + ' | Status=' + $s.Status + ' | Signer=' + $(if ($s.SignerCertificate) { $s.SignerCertificate.Subject } else { '(tidak ada)' }))
}

Write-Host ''
Write-Host '== SIGNTOOL VERIFY /pa =='
$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
Write-Host ('signtool: ' + $signtool)
foreach ($f in @($exe, $msi)) {
    $keluar = & $signtool verify /pa /v $f 2>&1 | Select-Object -Last 6
    Write-Host ((Split-Path -Leaf $f) + ' -> keluar ' + $LASTEXITCODE)
    $keluar | ForEach-Object { Write-Host ('  ' + $_) }
}
