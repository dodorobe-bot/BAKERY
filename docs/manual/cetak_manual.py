"""Cetak docs/manual/manual.html menjadi buku PDF A4 lengkap dengan kepala/kaki halaman.

Jalankan dari akar repositori:

    python docs/manual/cetak_manual.py

Prasyarat:
  - Chromium Playwright (dicari di %LOCALAPPDATA%\\ms-playwright; atau setel SERUNI_CHROME).
  - pdftotext dari xpdf/poppler (setel SERUNI_PDFTOTEXT bila tidak ada di Git for Windows).
  - Paket Python pypdf.

Alur: cetak pertama memakai penanda daftar isi "00", nomor halaman tiap bab dibaca dari hasil
cetak, penanda diganti, lalu dicetak ulang. Kepala/kaki halaman dicetak sebagai lapisan
terpisah dan digabungkan dengan pypdf, sehingga sampul tetap bersih.

Skrip ini memverifikasi hasilnya sendiri: jumlah halaman, penanda bab, keutuhan daftar isi,
kesejajaran nomor halaman, kepala/kaki di setiap halaman, dan 41 rujukan gambar. Berkas final
hanya dipasang setelah semua pemeriksaan lulus.

Versi aplikasi yang tercetak di kepala halaman dan di metadata PDF diambil dari
<Version> pada src/Host.Service/BakeryDesktop.Host.Service.csproj — sumber yang sama dengan
penjaga versi di scripts/build-pro.ps1 — lalu dicocokkan dengan judul manual.html.

Catatan mesin: Chrome dijalankan single-process. Pada mesin dengan RAM kecil, mode multi-proses
kehabisan memori saat mengolah 41 tangkapan layar dan berhenti tanpa menulis PDF.
"""

import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
HTML = ROOT / "manual.html"
OUT = ROOT / "Seruni22-Buku-Manual-Pengguna.pdf"
OUT_TMP = ROOT / "_manual-final.pdf"

SCRATCH = Path(tempfile.gettempdir()) / "seruni-manual-cetak"
PDF1 = SCRATCH / "cetak-1.pdf"
PDF2 = SCRATCH / "cetak-2.pdf"
LAP_HTML = SCRATCH / "lapisan.html"
LAP_PDF = SCRATCH / "lapisan.pdf"


def log(pesan):
    print(f"[{time.strftime('%H:%M:%S')}] {pesan}", flush=True)


def cari_chrome():
    kandidat = []
    if os.environ.get("SERUNI_CHROME"):
        kandidat.append(Path(os.environ["SERUNI_CHROME"]))
    cache = Path(os.environ.get("LOCALAPPDATA", "")) / "ms-playwright"
    kandidat += sorted(cache.glob("chromium-*/chrome-win/chrome.exe"), reverse=True)
    for jalur in kandidat:
        if jalur.is_file():
            return jalur
    raise SystemExit("Chrome/Chromium tidak ditemukan. Setel SERUNI_CHROME ke chrome.exe.")


CHROME = cari_chrome()
PDFTOTEXT = Path(os.environ.get("SERUNI_PDFTOTEXT", r"C:\Program Files\Git\mingw64\bin\pdftotext.exe"))

# Versi dibaca dari sumber yang sama dengan penjaga versi di scripts/build-pro.ps1, supaya
# kepala halaman dan metadata PDF tidak pernah menempelkan versi yang berbeda dari paket.
CSPROJ = ROOT.parent.parent / "src" / "Host.Service" / "BakeryDesktop.Host.Service.csproj"


def baca_versi():
    cocok = re.search(r"<Version>([^<]+)</Version>", CSPROJ.read_text(encoding="utf-8"))
    if not cocok:
        raise SystemExit(f"<Version> tidak ditemukan di {CSPROJ}")
    return cocok.group(1).strip()


VERSI = baca_versi()


def cetak(html: Path, pdf: Path, label: str):
    galat = ""
    for percobaan in range(1, 4):
        pdf.unlink(missing_ok=True)
        profil = Path(tempfile.mkdtemp(prefix="profil-cetak-"))
        try:
            mulai = time.time()
            hasil = subprocess.run(
                [
                    str(CHROME),
                    "--headless=old",
                    "--single-process",
                    "--disable-gpu",
                    "--no-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-extensions",
                    "--disable-background-networking",
                    "--disable-sync",
                    "--disable-breakpad",
                    "--disable-crash-reporter",
                    "--disable-component-update",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--no-pdf-header-footer",
                    f"--user-data-dir={profil}",
                    f"--print-to-pdf={pdf}",
                    html.resolve().as_uri(),
                ],
                capture_output=True,
                text=True,
                timeout=300,
            )
            ukuran = pdf.stat().st_size if pdf.exists() else 0
            if ukuran >= 100_000:
                log(f"cetak {label}: {ukuran / 1048576:.1f} MB dalam {time.time() - mulai:.0f} dtk")
                return
            galat = f"kode {hasil.returncode}, {ukuran} byte\nstdout: {hasil.stdout[-500:]}\nstderr: {hasil.stderr[-1200:]}"
        except subprocess.TimeoutExpired:
            galat = "waktu habis 300 dtk"
        finally:
            shutil.rmtree(profil, ignore_errors=True)
        log(f"cetak {label} gagal (percobaan {percobaan}/3), ulangi")
        time.sleep(5)
    raise SystemExit(f"cetak {label} gagal tiga kali:\n{galat}")


def baca_halaman(pdf: Path):
    if not PDFTOTEXT.is_file():
        raise SystemExit(f"pdftotext tidak ditemukan di {PDFTOTEXT}. Setel SERUNI_PDFTOTEXT.")
    hasil = subprocess.run(
        [str(PDFTOTEXT), "-layout", "-enc", "UTF-8", str(pdf), "-"],
        capture_output=True,
        timeout=120,
    )
    if hasil.returncode != 0:
        raise SystemExit(f"pdftotext gagal: {hasil.stderr.decode('utf-8', 'replace')}")
    halaman = hasil.stdout.decode("utf-8").split("\f")
    if halaman and not halaman[-1].strip():
        halaman.pop()
    return halaman


def petakan(halaman):
    peta = {}
    for i, isi in enumerate(halaman):
        for baris in (b.strip() for b in isi.split("\n")):
            bab = re.match(r"^BAB (\d+)(\s|$)", baris)
            if bab:
                peta.setdefault(f"bab-{bab.group(1)}", i + 1)
            lamp = re.match(r"^LAMPIRAN ([A-E])(\s|$)", baris)
            if lamp:
                peta.setdefault(f"lamp-{lamp.group(1).lower()}", i + 1)
    for n in range(1, 40):
        if f"bab-{n}" not in peta:
            raise SystemExit(f"penanda BAB {n} tidak ditemukan")
    for huruf in "abcde":
        if f"lamp-{huruf}" not in peta:
            raise SystemExit(f"penanda LAMPIRAN {huruf.upper()} tidak ditemukan")
    urut = sorted(peta.items(), key=lambda x: x[1])
    for a, b in zip(urut, urut[1:]):
        if b[1] <= a[1]:
            raise SystemExit(f"urutan halaman tidak naik: {a} lalu {b}")
    return peta


def perbarui_daftar_isi(peta):
    html = HTML.read_text(encoding="utf-8")
    for kunci, nomor in peta.items():
        pola = rf'(<span class="pg" data-pg="{kunci}">)\d+(</span>)'
        html, jumlah = re.subn(pola, rf"\g<1>{nomor:02d}\g<2>", html)
        if jumlah != 1:
            raise SystemExit(f"penanda daftar isi {kunci} ditemukan {jumlah} kali")
    HTML.write_text(html, encoding="utf-8", newline="\n")
    log(f"daftar isi diperbarui untuk {len(peta)} entri")


def html_lapisan(total):
    gaya = (
        "@page{size:A4;margin:0}html,body{margin:0;padding:0}"
        ".lembar{position:relative;width:210mm;height:296mm;overflow:hidden;page-break-after:always;"
        "font-family:'Segoe UI',Arial,sans-serif;font-size:7.5pt;color:#7b8698}"
        ".lembar:last-child{page-break-after:auto}"
        ".kepala,.kaki{position:absolute;left:18mm;right:18mm;display:flex;justify-content:space-between}"
        ".kepala{top:6mm}.kaki{bottom:6mm}"
    )
    bagian = [
        '<!doctype html><html lang="id"><head><meta charset="utf-8"><style>',
        gaya,
        '</style></head><body><div class="lembar"></div>',
    ]
    for i in range(2, total + 1):
        bagian.append(
            '<div class="lembar">'
            '<div class="kepala"><span>Seruni 22 Bakery &middot; Buku Manual Pengguna</span>'
            f"<span>Versi aplikasi {VERSI}</span></div>"
            '<div class="kaki"><span>Panduan pengoperasian dari pemasangan hingga Pengaturan Toko &amp; Cabang</span>'
            f"<span>Halaman {i} dari {total}</span></div>"
            "</div>"
        )
    bagian.append("</body></html>")
    return "".join(bagian)


def gabung():
    skrip = """
import sys
from datetime import datetime
from pypdf import PdfReader, PdfWriter
from pypdf.generic import NameObject, TextStringObject
dasar = PdfReader(sys.argv[1])
lapis = PdfReader(sys.argv[2])
if len(dasar.pages) != len(lapis.pages):
    raise SystemExit(f"jumlah halaman berbeda: {len(dasar.pages)} vs {len(lapis.pages)}")
keluar = PdfWriter()
for i, h in enumerate(dasar.pages):
    if i > 0:
        h.merge_page(lapis.pages[i])
    keluar.add_page(h)
sekarang = datetime.now().strftime("D:%Y%m%d%H%M%S")
keluar.add_metadata({
    "/Title": "Buku Manual Pengguna Seruni 22 Bakery",
    "/Author": "Seruni 22 Bakery",
    "/Subject": f"Panduan pengoperasian aplikasi Seruni 22 Bakery versi {sys.argv[4]}, dari pemasangan hingga Pengaturan Toko & Cabang",
    "/Keywords": f"manual pengguna, Seruni 22 Bakery, {sys.argv[4]}, kasir, produksi, keuangan",
    "/Creator": "Seruni 22 Bakery",
    "/Producer": "pypdf",
    "/CreationDate": sekarang,
    "/ModDate": sekarang,
})
keluar._root_object[NameObject("/Lang")] = TextStringObject("id")
with open(sys.argv[3], "wb") as f:
    keluar.write(f)
print(len(dasar.pages), sum(len(p.images) for p in dasar.pages))
"""
    hasil = subprocess.run(
        [sys.executable, "-c", skrip, str(PDF2), str(LAP_PDF), str(OUT_TMP), VERSI],
        capture_output=True,
        text=True,
        timeout=300,
    )
    if hasil.returncode != 0:
        raise SystemExit(f"pypdf gagal: {hasil.stderr[-2000:]}")
    jumlah, gambar = (int(x) for x in hasil.stdout.split())
    return jumlah, gambar


def periksa_daftar_isi_dasar(pdf, peta):
    halaman = baca_halaman(pdf)
    mulai = next((i for i, h in enumerate(halaman) if "Daftar Isi" in h), None)
    selesai = next((i for i, h in enumerate(halaman) if re.search(r"(?m)^BAB 1(\s|$)", h)), None)
    if mulai is None or selesai is None or selesai <= mulai:
        raise SystemExit("halaman daftar isi tidak dikenali pada cetakan dasar")

    salah, terbaca = [], set()
    for baris in (b.strip() for h in halaman[mulai:selesai] for b in h.split("\n")):
        cocok = re.match(r"^(Bab (\d+)|Lampiran ([A-E]))(.*?)(\d+)$", baris)
        if not cocok:
            continue
        kunci = f"bab-{cocok.group(2)}" if cocok.group(2) else f"lamp-{cocok.group(3).lower()}"
        if kunci not in peta:
            continue
        terbaca.add(kunci)
        if int(cocok.group(5)) != peta[kunci]:
            salah.append(f"{kunci}: daftar isi {cocok.group(5)}, seharusnya {peta[kunci]}")
    hilang = sorted(set(peta) - terbaca)
    if salah or hilang:
        raise SystemExit("daftar isi tidak cocok: " + "; ".join(salah + [f"{k} tidak terbaca" for k in hilang]))
    log(f"daftar isi pada cetakan dasar cocok untuk {len(peta)} entri")


def periksa_geometri_daftar_isi(pdf, peta):
    from pypdf import PdfReader

    reader = PdfReader(str(pdf))
    halaman = baca_halaman(pdf)
    mulai = next((i for i, h in enumerate(halaman) if "Daftar Isi" in h), None)
    selesai = next((i for i, h in enumerate(halaman) if re.search(r"(?m)^BAB 1(\s|$)", h)), None)

    baris_total, tanpa_nomor = 0, []
    for idx in range(mulai, selesai):
        items = []

        def kunjungi(text, cm, tm, font_dict, font_size):
            teks = text.strip()
            if not teks:
                return
            xd = cm[0] * tm[4] + cm[2] * tm[5] + cm[4]
            yd = cm[1] * tm[4] + cm[3] * tm[5] + cm[5]
            items.append((xd, yd, teks))

        reader.pages[idx].extract_text(visitor_text=kunjungi)
        for xd, yd, teks in list(items):
            if not re.fullmatch(r"(Bab \d+|Lampiran [A-E])", teks):
                continue
            baris_total += 1
            cocok = any(
                x2 > 515 and abs(y2 - yd) <= 3.5 and len(t2) <= 3 and teks != t2
                for x2, y2, t2 in items
            )
            if not cocok:
                tanpa_nomor.append(f"hal {idx + 1}: {teks}")

    if baris_total != len(peta):
        tanpa_nomor.append(f"baris daftar isi terdeteksi {baris_total}, seharusnya {len(peta)}")
    if tanpa_nomor:
        raise SystemExit("nomor daftar isi tidak sebaris dengan judulnya: " + "; ".join(tanpa_nomor[:10]))
    log(f"geometri daftar isi: {baris_total} baris, nomor sebaris dengan judul")


def periksa(akhir, peta, jumlah, gambar):
    masalah = []
    peta2 = petakan(akhir)
    for kunci, nomor in peta.items():
        if peta2.get(kunci) != nomor:
            masalah.append(f"halaman {kunci} bergeser: {nomor} -> {peta2.get(kunci)}")

    if "Halaman 1 dari" in akhir[0]:
        masalah.append("sampul memuat nomor halaman")
    if akhir[0].strip() and "TENTANG BUKU INI" not in akhir[0] and "SERUNI 22" not in akhir[0].upper():
        masalah.append("halaman pertama bukan sampul yang diharapkan")

    tanpa_kaki = []
    for i in range(1, len(akhir)):
        if f"Halaman {i + 1} dari {jumlah}" not in akhir[i]:
            tanpa_kaki.append(i + 1)
        if "Buku Manual Pengguna" not in akhir[i]:
            masalah.append(f"kepala halaman hilang di halaman {i + 1}")
    if tanpa_kaki:
        masalah.append(f"nomor kaki hilang di halaman: {tanpa_kaki[:12]} ({len(tanpa_kaki)} halaman)")
    if "Halaman 1 dari" in akhir[1]:
        masalah.append("halaman 2 memuat nomor halaman 1")
    for i in (1, len(akhir) - 1):
        if f"Versi aplikasi {VERSI}" not in akhir[i]:
            masalah.append(f"kepala halaman {i + 1} tidak memuat versi {VERSI}")

    if gambar < 40:
        masalah.append(f"gambar di PDF hanya {gambar}, 41 tangkapan layar seharusnya ada")

    nomor = {int(m) for h in akhir for m in re.findall(r"Gambar (\d+)\.", h)}
    if nomor != set(range(1, 42)):
        masalah.append(f"rujukan gambar tidak lengkap: {sorted(nomor)}")

    if masalah:
        raise SystemExit("VERIFIKASI GAGAL:\n- " + "\n- ".join(masalah))
    log(f"verifikasi lulus: {jumlah} halaman, {gambar} gambar, penanda halaman stabil, kepala/kaki di semua halaman 2..{jumlah}")


def periksa_stempel_naskah():
    judul = re.search(r"<title>([^<]*)</title>", HTML.read_text(encoding="utf-8"))
    if not judul or VERSI not in judul.group(1):
        raise SystemExit(
            f"judul manual.html tidak memuat versi {VERSI}; stempelkan naskah ke paket yang benar dulu"
        )
    log(f"versi paket {VERSI} (dari {CSPROJ.name}); judul naskah cocok")


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    periksa_stempel_naskah()
    SCRATCH.mkdir(parents=True, exist_ok=True)
    for sisa in (OUT_TMP, PDF1, PDF2, LAP_HTML, LAP_PDF):
        sisa.unlink(missing_ok=True)

    cetak(HTML, PDF1, "lintasan 1")
    halaman1 = baca_halaman(PDF1)
    peta = petakan(halaman1)
    log(f"lintasan 1: {len(halaman1)} halaman, {len(peta)} penanda")

    perbarui_daftar_isi(peta)
    cetak(HTML, PDF2, "lintasan 2")
    halaman2 = baca_halaman(PDF2)
    if len(halaman2) != len(halaman1):
        raise SystemExit(f"jumlah halaman berubah: {len(halaman1)} -> {len(halaman2)}")
    periksa_daftar_isi_dasar(PDF2, peta)

    LAP_HTML.write_text(html_lapisan(len(halaman2)), encoding="utf-8", newline="\n")
    cetak(LAP_HTML, LAP_PDF, "lapisan kepala/kaki")

    jumlah, gambar = gabung()
    akhir = baca_halaman(OUT_TMP)
    if len(akhir) != jumlah:
        raise SystemExit(f"jumlah halaman setelah gabung: {len(akhir)} vs {jumlah}")
    periksa(akhir, peta, jumlah, gambar)
    periksa_geometri_daftar_isi(OUT_TMP, peta)

    os.replace(OUT_TMP, OUT)
    for sisa in (PDF1, PDF2, LAP_HTML, LAP_PDF):
        sisa.unlink(missing_ok=True)
    log(f"SELESAI: {OUT} ({OUT.stat().st_size / 1048576:.1f} MB)")


if __name__ == "__main__":
    main()
