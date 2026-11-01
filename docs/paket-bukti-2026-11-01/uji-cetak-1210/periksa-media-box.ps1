$ErrorActionPreference = 'Continue'
foreach ($n in 'test.pdf','test2.pdf') {
  $p = Join-Path "$env:USERPROFILE\Documents" $n
  if (-not (Test-Path $p)) { Write-Host "$n : tidak ada"; continue }
  $i = Get-Item $p
  Write-Host ('=== ' + $p)
  Write-Host ('  dibuat : ' + $i.CreationTime.ToString('yyyy-MM-dd HH:mm:ss'))
  Write-Host ('  diubah : ' + $i.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
  Write-Host ('  ukuran : ' + $i.Length)
  $b = [System.IO.File]::ReadAllBytes($p)
  $head = [System.Text.Encoding]::ASCII.GetString($b[0..([Math]::Min(400,$b.Length-1))])
  if ($head -match '/CreationDate \(([^)]*)\)') { Write-Host ('  CreationDate PDF : ' + $matches[1]) }
  if ($head -match '/ModDate \(([^)]*)\)') { Write-Host ('  ModDate PDF      : ' + $matches[1]) }
  $ascii = [System.Text.Encoding]::ASCII.GetString($b)
  foreach ($cek in 'Seruni','Nastar','Bolu','Rp','Total') {
    Write-Host ('  mengandung "' + $cek + '" (teks mentah): ' + $ascii.Contains($cek))
  }
  Write-Host ('  font tertanam : ' + (($ascii | Select-String -Pattern '/BaseFont\s*/(\w+)' -AllMatches).Matches.Count))
}
Write-Host '--- alat ekstraksi PDF yang tersedia ---'
foreach ($t in 'pdftotext','pdftk','gs','mutool','python','python3') {
  $g = Get-Command $t -ErrorAction SilentlyContinue
  Write-Host ('  ' + $t + ' : ' + $(if ($g) { $g.Source } else { 'TIDAK ADA' }))
}
