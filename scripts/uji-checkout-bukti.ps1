<#
  Pagar keter-reproduksian berkas bukti.

  Tujuan: menjawab "kalau orang lain meng-clone repositori ini, apakah berkas bukti yang
  mereka pegang sama byte dengan yang dicatat saat pengujian?" Pertanyaan ini sempat tidak
  bisa dijawab: dengan core.autocrlf=true, git menormalkan LF menjadi CRLF saat checkout,
  sehingga skrip kit yang SHA-256-nya tercatat di KIT-SHA256SUMS.txt berubah menjadi berkas
  lain (run-sandbox.ps1 kit 1.2.12: tercatat 9fe9e633.../6930 bita, hasil clone 7120 bita)
  dan "sha256sum -c" bahkan menolak nama berkasnya karena manifest itu sendiri ikut
  berpindah baris.

  Yang diperiksa (pada hasil clone, bukan pada pohon kerja):
    - byte setiap berkas bukti yang dikomit sama dengan blob di index — memakai
      "hash-object --no-filters", sengaja bukan "git status": status membandingkan setelah
      filter konversi baris, jadi justru buta terhadap cacat yang dicari pagar ini
    - aturan -text memang berlaku (check-attr), bukan kebetulan isi berkasnya sudah LF
    - setiap entri KIT-SHA256SUMS.txt yang bukan artefak .exe/.msi cocok SHA-256-nya

  Tidak melakukan: pemasangan, penulisan ke folder data, atau perubahan apa pun pada repo
  asal. Clone dibuat sekali-pakai di folder temporer dan dihapus lagi. Artefak .exe/.msi
  tidak dikomit, jadi entri manifest untuk keduanya dicatat LEWAT, bukan lulus.

  Pemakaian:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\uji-checkout-bukti.ps1
    ... -Repo D:\bakery-desktop        -SimpanClone

  Keluar 1 bila ada FAIL. Keluar 2 (LEWAT) bila pohon bukan repositori git — itu belum
  diperiksa, bukan lulus. Karena yang diuji adalah hasil clone, aturan .gitattributes harus
  sudah ikut dikomit; pada pohon yang perubahannya masih di working tree, pagar ini benar
  melaporkan gagal karena HEAD memang belum memuat aturannya.
#>
[CmdletBinding()]
param(
    [string]$Repo = "",
    [switch]$SimpanClone
)

$ErrorActionPreference = "Stop"

function Tulis($marka, $pesan) { Write-Host ("[{0}] {1}" -f $marka, $pesan) }

$root = if ($Repo) { (Resolve-Path $Repo).Path } else { Split-Path -Parent $PSScriptRoot }
if (-not (Test-Path (Join-Path $root ".git"))) {
    # Bukan kegagalan bukti, tapi tidak bisa dipakai mengklaim keter-reproduksian: keluar 2,
    # dan pemanggil mencatatnya sebagai belum diperiksa, bukan lulus.
    Tulis "LEWAT" "$root bukan repositori git - checkout tidak bisa diuji"
    exit 2
}

$clone = Join-Path $env:TEMP ("seruni-checkout-bukti-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$gagal = @()
$lulus = 0
$lewat = 0

try
{
    Tulis "MULAI" "Clone sekali-pakai dengan core.autocrlf=true (konfigurasi yang merusak bukti): $clone"
    git -c core.autocrlf=true clone --quiet --no-hardlinks --depth 1 --no-tags `
        ("file:///" + ($root -replace '\\', '/')) $clone
    if ($LASTEXITCODE -ne 0) { throw "git clone gagal (exit $LASTEXITCODE)" }

    # 1. Setiap berkas bukti yang dikomit harus keluar dari checkout tanpa diubah sedikit pun.
    $oidIndex = @{}
    foreach ($baris in (git -C $clone ls-files -s)) {
        if ($baris -notmatch "`tdocs/paket-bukti-") { continue }
        $pecah = $baris -split "`t", 2
        $oidIndex[$pecah[1]] = ($pecah[0] -split '\s+')[1]
    }
    $jalur = @($oidIndex.Keys)
    Tulis "MULAI" "$($jalur.Count) berkas bukti dibandingkan byte-per-byte dengan blob"
    $hashKerja = @($jalur | git -C $clone hash-object --no-filters --stdin-paths)
    for ($i = 0; $i -lt $jalur.Count; $i++) {
        if ($hashKerja[$i] -eq $oidIndex[$jalur[$i]]) { $lulus++ }
        else { $gagal += ("checkout mengubah byte: {0} (blob {1}, hasil {2})" -f $jalur[$i], $oidIndex[$jalur[$i]], $hashKerja[$i]) }
    }

    # 2. Penjaga aturan: -text harus benar-benar berlaku, bukan kebetulan berkasnya LF.
    $telunjuk = $jalur | Where-Object { $_ -match '\.ps1$' } | Select-Object -First 1
    if (-not $telunjuk) { $gagal += "tidak ada skrip kit yang tracked untuk memeriksa check-attr" }
    else {
        $attr = git -C $clone check-attr text -- $telunjuk
        if ($attr -match ":\s*text:\s*unset") {
            $lulus++
            Tulis "OK" "-text berlaku (contoh $telunjuk)"
        } else { $gagal += "-text tidak berlaku pada $telunjuk : $attr" }
    }

    # 3. Digest yang tercatat manifest harus cocok dengan hasil clone.
    $manifests = @($jalur | Where-Object { $_ -match 'KIT-SHA256SUMS\.txt$' })
    if ($manifests.Count -eq 0) { $gagal += "tidak ada KIT-SHA256SUMS.txt yang tracked" }
    foreach ($m in $manifests) {
        $dir = Split-Path -Parent (Join-Path $clone $m)
        foreach ($baris in (Get-Content -LiteralPath (Join-Path $clone $m))) {
            if ($baris -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') { continue }
            $dicatat = $Matches[1].ToLowerInvariant()
            $nama = $Matches[2].Trim()
            if ($nama -match '\.(exe|msi)$') { $lewat++; continue }   # artefak memang tidak dikomit
            $berkas = Join-Path $dir $nama
            if (-not (Test-Path -LiteralPath $berkas)) { $gagal += "$m : berkas $nama tidak ada di clone"; continue }
            $nyata = (Get-FileHash -Algorithm SHA256 -LiteralPath $berkas).Hash.ToLowerInvariant()
            if ($nyata -eq $dicatat) { $lulus++ }
            else { $gagal += "$m : $nama tercatat $dicatat, hasil clone $nyata" }
        }
    }

    foreach ($g in $gagal) { Tulis "GAGAL" $g }
    Tulis "RINGKAS" "$lulus lulus, $($gagal.Count) gagal, $lewat lewati (artefak .exe/.msi tidak dikomit) dari $($manifests.Count) manifest"
    if ($gagal.Count -gt 0) { exit 1 }
    if ($SimpanClone) { Tulis "PILIH" "Clone ditinggalkan di $clone" }
    exit 0
}
finally
{
    if (-not $SimpanClone -and (Test-Path $clone)) {
        try { Remove-Item -LiteralPath $clone -Recurse -Force }
        catch { Tulis "PILIH" "Clone di $clone tidak terhapus: $($_.Exception.Message)" }
    }
}
