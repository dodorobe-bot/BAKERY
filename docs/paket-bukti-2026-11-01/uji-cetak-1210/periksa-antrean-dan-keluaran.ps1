$ErrorActionPreference = 'Continue'
$jobs = @(Get-PrintJob -PrinterName 'Microsoft Print to PDF' -ErrorAction SilentlyContinue)
Write-Host ('Antrean aktif: ' + $jobs.Count)
foreach ($j in $jobs) {
  Write-Host ('  #{0} "{1}" status={2} diajukan={3}' -f $j.Id, $j.Document, $j.JobStatus, $j.SubmittedTime)
}
Write-Host '--- PDF baru (20 menit terakhir) ---'
$dir = @("$env:USERPROFILE\Documents", "$env:USERPROFILE\Desktop", [System.IO.Path]::GetTempPath())
foreach ($d in $dir) {
  Get-ChildItem -Path $d -Filter *.pdf -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -gt (Get-Date).AddMinutes(-20) } |
    ForEach-Object { Write-Host ('  ' + $_.FullName + ' | ' + $_.Length + ' bita | ' + $_.LastWriteTime.ToString('HH:mm:ss')) }
}
Write-Host '--- berkas di folder bukti uji fisik ---'
$bukti = Get-ChildItem -Path ([System.IO.Path]::GetTempPath()) -Directory -Filter 'seruni-pos-fisik-*' -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($null -ne $bukti) {
  Write-Host ('  ' + $bukti.FullName)
  Get-ChildItem $bukti.FullName -File | ForEach-Object { Write-Host ('    ' + $_.Name + ' ' + $_.Length) }
}
