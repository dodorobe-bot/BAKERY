"""Cetak docs/manual/petunjuk.html menjadi buku petunjuk ringkas PDF (gaya buku petunjuk instansi).

Jalankan dari akar repositori:

    python docs/manual/cetak_petunjuk.py

Prasyarat sama dengan cetak_manual.py: Chromium Playwright (atau SERUNI_CHROME), pdftotext
(atau SERUNI_PDFTOTEXT), dan paket Python pypdf.

Alur: cetakan pertama membaca halaman awal tiap bagian dari teks hasil ekstraksi, nomor daftar
isi diisi ulang, lalu dicetak kedua kalinya. Kaki halaman dicetak sebagai lapisan terpisah dan
digabung dengan pypdf supaya sampul tetap bersih.

Verifikasi mandiri: seluruh rujukan gambar ada di disk, setiap entri daftar isi menemukan
halamannya, nomor halaman naik sesuai urutan daftar isi, penanda tidak bergeser pada cetakan
kedua, kaki halaman ada di semua halaman kecuali sampul, dan seluruh caption 1..N terbaca di PDF.

Versi aplikasi diambil dari <Version> pada src/Host.Service/BakeryDesktop.Host.Service.csproj —
sumber yang sama dengan penjaga versi di scripts/build-pro.ps1 — lalu dicocokkan dengan judul naskah.

Catatan mesin: Chrome dijalankan single-process; pada RAM kecil mode multi-proses kehabisan memori.
"""

import html
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
HTML = ROOT / "petunjuk.html"
OUT = ROOT / "Seruni22-Buku-Petunjuk-Pengguna.pdf"
OUT_TMP = ROOT / "_petunjuk-final.pdf"
GAMBAR = ROOT / "gambar"

SCRATCH = Path(tempfile.gettempdir()) / "seruni-petunjuk-cetak"
PDF1 = SCRATCH / "cetak-1.pdf"
PDF2 = SCRATCH / "cetak-2.pdf"
LAP_HTML = SCRATCH / "lapisan.html"
LAP_PDF = SCRATCH / "lapisan.pdf"

CSPROJ = ROOT.parent.parent / "src" / "Host.Service" / "BakeryDesktop.Host.Service.csproj"


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


def baca_versi():
    cocok = re.search(r"<Version>([^<]+)</Version>", CSPROJ.read_text(encoding="utf-8"))
    if not cocok:
        raise SystemExit(f"<Version> tidak ditemukan di {CSPROJ}")
    return cocok.group(1).strip()


VERSI = baca_versi()


def cetak(sumber: Path, pdf: Path, label: str):
    galat = ""
    for percobaan in range(1, 4):
        pdf.unlink(missing_ok=True)
        profil = Path(tempfile.mkdtemp(prefix="profil-petunjuk-"))
        try:
            mulai = time.time()
            hasil = subprocess.run(
                [
                    str(CHROME), "--headless=old", "--single-process", "--disable-gpu", "--no-sandbox",
                    "--disable-dev-shm-usage", "--disable-extensions", "--disable-background-networking",
                    "--disable-sync", "--disable-breakpad", "--disable-crash-reporter",
                    "--disable-component-update", "--no-first-run", "--no-default-browser-check",
                    "--no-pdf-header-footer",
                    f"--user-data-dir={profil}",
                    f"--print-to-pdf={pdf}",
                    sumber.resolve().as_uri(),
                ],
                capture_output=True, text=True, timeout=300,
            )
            ukuran = pdf.stat().st_size if pdf.exists() else 0
            if ukuran >= 100_000:
                log(f"cetak {label}: {ukuran / 1048576:.1f} MB dalam {time.time() - mulai:.0f} dtk")
                return
            galat = f"kode {hasil.returncode}, {ukuran} byte\nstdout: {hasil.stdout[-400:]}\nstderr: {hasil.stderr[-1200:]}"
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
    hasil = subprocess.run([str(PDFTOTEXT), "-layout", "-enc", "UTF-8", str(pdf), "-"],
                           capture_output=True, timeout=120)
    if hasil.returncode != 0:
        raise SystemExit(f"pdftotext gagal: {hasil.stderr.decode('utf-8', 'replace')}")
    halaman = hasil.stdout.decode("utf-8").split("\f")
    if halaman and not halaman[-1].strip():
        halaman.pop()
    return halaman


def baca_halaman_raw(pdf: Path):
    """Ekstraksi menurut urutan konten, bukan menurut kolom.

    Nomor halaman daftar isi berada jauh di kanan judulnya, dipisahkan titik-titik pemandu,
    sehingga -layout kerap memindahkan angka ke baris tetangga. Urutan konten selalu
    judul lalu nomornya, jadi pemeriksaan daftar isi memakai mode ini."""
    hasil = subprocess.run([str(PDFTOTEXT), "-raw", "-enc", "UTF-8", str(pdf), "-"],
                           capture_output=True, timeout=120)
    if hasil.returncode != 0:
        raise SystemExit(f"pdftotext -raw gagal: {hasil.stderr.decode('utf-8', 'replace')}")
    halaman = hasil.stdout.decode("utf-8").split("\f")
    if halaman and not halaman[-1].strip():
        halaman.pop()
    return halaman


def baris(halaman):
    for teks in halaman:
        for b in teks.split("\n"):
            yield re.sub(r"\s+", " ", b.replace(" ", " ")).strip()


def kena(line, penanda):
    """Judul yang berdiri tepat di atas tabel ikut terserap ke baris tabel oleh pdftotext
    -layout, jadi sub-bab boleh cocok sebagai awalan baris. Penanda bab tetap harus persis,
    sebab "BAB I" adalah awalan sah dari "BAB II" dan "BAB III"."""
    if penanda.startswith("BAB "):
        return line == penanda
    return line == penanda or line.startswith(penanda + " ")


def target_daftar_isi():
    """Ambil (kunci, teks penanda) dari baris daftar isi naskah, sesuai urutan tampil."""
    naskah = HTML.read_text(encoding="utf-8")
    keluar = []
    for baris_toc in re.finditer(
        r'<span class="no(?: ind)?">(.*?)</span><span class="t(?: ind)?">(.*?)</span>'
        r'.*?<span class="pg" data-pg="([a-z0-9-]+)">', naskah, re.S):
        no, judul, kunci = (html.unescape(x).strip() for x in baris_toc.groups())
        label = f"{no} {judul}".strip()
        # Judul bab tercetak di dua baris (penanda "BAB I" lalu judulnya), sedangkan baris sub-bab
        # tercetak satu baris penuh. Penanda pencarian disesuaikan dengan bentuk di badan buku.
        penanda = " ".join(label.split()[:2]) if label.startswith("BAB ") else label
        keluar.append((kunci, penanda, label))
    if len(keluar) != len({k for k, *_ in keluar}):
        raise SystemExit("kunci data-pg pada daftar isi ada yang ganda")
    return keluar


def batas_toc(halaman):
    """Indeks halaman daftar isi: dari lembar 'DAFTAR ISI' sampai tepat sebelum badan buku."""
    mulai = next((i for i, t in enumerate(halaman) if "DAFTAR ISI" in t), None)
    if mulai is None:
        raise SystemExit("halaman daftar isi tidak ditemukan")
    for i in range(mulai + 1, len(halaman)):
        if any(kena(b, "BAB I") for b in baris([halaman[i]])):
            return mulai, i
    raise SystemExit("penanda BAB I pada badan buku tidak ditemukan")


def nomor_daftar_isi(halaman_raw, mulai, tubuh, targets):
    """Nomor yang tercetak di samping tiap judul daftar isi, dibaca dari aliran teks mentah."""
    teks = re.sub(r"\s+", " ", " ".join(halaman_raw[mulai:tubuh]))
    keluar = {}
    for kunci, _, label in targets:
        cocok = re.search(re.escape(label) + r" (\d{1,3})(?= |\Z)", teks)
        keluar[kunci] = int(cocok.group(1)) if cocok else None
    return keluar


def petakan(halaman, targets):
    mulai_toc, tubuh = batas_toc(halaman)
    peta = {}

    di_luar_toc = set(range(0, mulai_toc)) | set(range(tubuh, len(halaman)))
    for kunci, penanda, label in targets:
        calon = sorted(i for i in di_luar_toc if any(kena(b, penanda) for b in baris([halaman[i]])))
        if not calon:
            raise SystemExit(f"penanda {kunci!r} ({penanda}) tidak ditemukan di badan buku")
        peta[kunci] = calon[0] + 1
    return peta, tubuh


def urut_check(targets, peta):
    buruk = []
    sebelum = 0
    for kunci, _, label in targets:
        sekarang = peta[kunci]
        if sekarang < sebelum:
            buruk.append(f"{label}: halaman {sekarang} mendahului sebelumnya ({sebelum})")
        sebelum = sekarang
    if buruk:
        raise SystemExit("urutan daftar isi tidak naik: " + "; ".join(buruk))
    log(f"urutan halaman naik untuk {len(targets)} entri")


def perbarui_daftar_isi(peta):
    naskah = HTML.read_text(encoding="utf-8")
    for kunci, nomor in peta.items():
        pola = rf'(<span class="pg" data-pg="{kunci}">)\d+(</span>)'
        naskah, jumlah = re.subn(pola, rf"\g<1>{nomor}\g<2>", naskah)
        if jumlah != 1:
            raise SystemExit(f"penanda daftar isi {kunci} ditemukan {jumlah} kali")
    HTML.write_text(naskah, encoding="utf-8", newline="\n")
    log(f"daftar isi diperbarui untuk {len(peta)} entri")


def rujukan_gambar():
    naskah = HTML.read_text(encoding="utf-8")
    sumber = re.findall(r'<img src="gambar/([^"]+)"', naskah)
    hilang = sorted({s for s in sumber if not (GAMBAR / s).is_file()})
    if hilang:
        raise SystemExit(f"gambar rujukan tidak ada di disk: {hilang}")
    caption = [int(m) for m in re.findall(r"<b>Gambar (\d+)\.</b>", naskah)]
    if caption != list(range(1, len(caption) + 1)):
        raise SystemExit(f"caption gambar tidak berurut 1..{len(caption)}: {caption}")
    log(f"rujukan gambar: {len(sumber)} penempatan, caption berurut 1..{len(caption)}")
    return len(caption)


def html_lapisan(total):
    gaya = (
        "@page{size:A4;margin:0}html,body{margin:0;padding:0}"
        ".lembar{position:relative;width:210mm;height:296mm;overflow:hidden;page-break-after:always;"
        "font-family:'Segoe UI',Arial,sans-serif;font-size:7.8pt;color:#7b8698}"
        ".lembar:last-child{page-break-after:auto}"
        ".kaki{position:absolute;bottom:8mm;left:18mm;right:18mm;display:flex;justify-content:space-between;"
        "border-top:1px solid #dfe4ec;padding-top:2mm}"
    )
    bagian = ['<!doctype html><html lang="id"><head><meta charset="utf-8"><style>', gaya,
              '</style></head><body><div class="lembar"></div>']
    for i in range(2, total + 1):
        bagian.append(
            '<div class="lembar"><div class="kaki">'
            "<span>Buku Petunjuk Penggunaan Aplikasi (User Manual)</span>"
            f"<span>Halaman {i} dari {total}</span>"
            f"<span>Seruni 22 Bakery &middot; Versi {VERSI}</span>"
            "</div></div>"
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
    "/Title": "Buku Petunjuk Penggunaan Aplikasi (User Manual) Seruni 22 Bakery",
    "/Author": "Seruni 22 Bakery",
    "/Subject": f"Petunjuk ringkas pengoperasian aplikasi Seruni 22 Bakery versi {sys.argv[4]}",
    "/Keywords": f"user manual, Seruni 22 Bakery, {sys.argv[4]}, kasir, produksi, persediaan, keuangan",
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
    hasil = subprocess.run([sys.executable, "-c", skrip, str(PDF2), str(LAP_PDF), str(OUT_TMP), VERSI],
                           capture_output=True, text=True, timeout=300)
    if hasil.returncode != 0:
        raise SystemExit(f"pypdf gagal: {hasil.stderr[-2000:]}")
    jumlah, gambar = (int(x) for x in hasil.stdout.split())
    return jumlah, gambar


def gambar_per_lembar(pdf):
    """Jumlah objek gambar yang benar-benar tertanam di tiap halaman PDF."""
    from pypdf import PdfReader

    keluar = []
    for h in PdfReader(str(pdf)).pages:
        sumber = h.get("/Resources")
        objek = sumber.get("/XObject", {}) if sumber else {}
        keluar.append(sum(1 for x in objek.values()
                          if str(x.get_object().get("/Subtype")) == "/Image"))
    return keluar


def periksa(pdf, akhir, peta, targets, jumlah, gambar, jumlah_caption):
    masalah = []
    peta2, _ = petakan(akhir, targets)
    for kunci, nomor in peta.items():
        if peta2.get(kunci) != nomor:
            masalah.append(f"halaman {kunci} bergeser: {nomor} -> {peta2.get(kunci)}")

    halaman_raw = baca_halaman_raw(pdf)
    if len(halaman_raw) != len(akhir):
        raise SystemExit(f"jumlah halaman teks mentah berbeda: {len(halaman_raw)} vs {len(akhir)}")
    mulai_toc, tubuh_toc = batas_toc(akhir)
    tercetak = nomor_daftar_isi(halaman_raw, mulai_toc, tubuh_toc, targets)
    for kunci, _, label in targets:
        if tercetak[kunci] != peta[kunci]:
            masalah.append(f"daftar isi memuat nomor {tercetak[kunci]} untuk {label}, "
                           f"seharusnya {peta[kunci]}")

    if f"Halaman 1 dari" in akhir[0]:
        masalah.append("sampul memuat nomor halaman")
    if "Seruni 22 Bakery" not in akhir[0]:
        masalah.append("halaman pertama bukan sampul yang diharapkan")

    tanpa_kaki = [i + 1 for i in range(1, len(akhir)) if f"Halaman {i + 1} dari {jumlah}" not in akhir[i]]
    if tanpa_kaki:
        masalah.append(f"kaki halaman hilang di halaman: {tanpa_kaki[:12]} ({len(tanpa_kaki)} halaman)")
    for i in (1, len(akhir) - 1):
        if f"Versi {VERSI}" not in akhir[i]:
            masalah.append(f"kaki halaman {i + 1} tidak memuat versi {VERSI}")

    if gambar < jumlah_caption:
        masalah.append(f"gambar di PDF hanya {gambar}, naskah memuat {jumlah_caption} caption")
    terbaca = {int(m) for t in akhir for m in re.findall(r"Gambar (\d+)\.", t)}
    kurang = sorted(set(range(1, jumlah_caption + 1)) - terbaca)
    if kurang:
        masalah.append(f"caption tidak terbaca di PDF: {kurang}")

    # Gambar tanpa caption di halamannya = gambar yang terdorong ke halaman sendiri
    # (sampul pernah begitu), jadi setiap halaman bergambar harus memuat captionnya.
    yatim = [i + 1 for i, n in enumerate(gambar_per_lembar(pdf))
             if n > len(re.findall(r"Gambar \d+\.", akhir[i]))]
    if yatim:
        masalah.append(f"gambar tanpa caption di halaman: {yatim}")

    if masalah:
        raise SystemExit("VERIFIKASI GAGAL:\n- " + "\n- ".join(masalah))
    log(f"verifikasi lulus: {jumlah} halaman, {gambar} gambar, {jumlah_caption} caption, "
        f"{len(peta)} entri daftar isi, kaki halaman di semua halaman 2..{jumlah}")


def periksa_stempel_naskah():
    judul = re.search(r"<title>([^<]*)</title>", HTML.read_text(encoding="utf-8"))
    if not judul or VERSI not in html.unescape(judul.group(1)):
        raise SystemExit(f"judul petunjuk.html tidak memuat versi {VERSI}; stempelkan naskah ke paket yang benar dulu")
    log(f"versi paket {VERSI} (dari {CSPROJ.name}); judul naskah cocok")


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    periksa_stempel_naskah()
    jumlah_caption = rujukan_gambar()
    targets = target_daftar_isi()
    log(f"daftar isi menargetkan {len(targets)} entri")

    SCRATCH.mkdir(parents=True, exist_ok=True)
    for sisa in (OUT_TMP, PDF1, PDF2, LAP_HTML, LAP_PDF):
        sisa.unlink(missing_ok=True)

    cetak(HTML, PDF1, "lintasan 1")
    halaman1 = baca_halaman(PDF1)
    peta, tubuh = petakan(halaman1, targets)
    urut_check(targets, peta)
    log(f"lintasan 1: {len(halaman1)} halaman, BAB I mulai halaman {tubuh + 1}")

    perbarui_daftar_isi(peta)
    cetak(HTML, PDF2, "lintasan 2")
    halaman2 = baca_halaman(PDF2)
    if len(halaman2) != len(halaman1):
        raise SystemExit(f"jumlah halaman berubah: {len(halaman1)} -> {len(halaman2)}")

    LAP_HTML.write_text(html_lapisan(len(halaman2)), encoding="utf-8", newline="\n")
    cetak(LAP_HTML, LAP_PDF, "lapisan kaki halaman")

    jumlah, gambar = gabung()
    akhir = baca_halaman(OUT_TMP)
    if len(akhir) != jumlah:
        raise SystemExit(f"jumlah halaman setelah gabung: {len(akhir)} vs {jumlah}")
    periksa(OUT_TMP, akhir, peta, targets, jumlah, gambar, jumlah_caption)

    os.replace(OUT_TMP, OUT)
    for sisa in (PDF1, PDF2, LAP_HTML, LAP_PDF):
        sisa.unlink(missing_ok=True)
    log(f"SELESAI: {OUT} ({OUT.stat().st_size / 1048576:.1f} MB)")


if __name__ == "__main__":
    main()
