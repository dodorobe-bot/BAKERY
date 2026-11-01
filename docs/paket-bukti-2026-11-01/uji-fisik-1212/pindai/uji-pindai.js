// Uji pemindai barcode mode keyboard-wedge pada UI 1.2.12.
// Peramban Chromium sungguhan (Playwright) mengetik per karakter lalu menekan Enter,
// persis seperti pemindai USB yang diprogram dengan akhiran Enter. Ini BUKAN pemindai fisik.
const fs = require('fs');
const path = require('path');
const pw = require('D:/bakery-desktop/tests/Perf/NavPerf/bin/Release/net8.0/.playwright/package');

const kerja = 'C:/tmp/uji-pindai-1212';
const bukti = path.join(kerja, 'bukti');
fs.mkdirSync(bukti, { recursive: true });
const info = JSON.parse(fs.readFileSync(path.join(kerja, 'info.json'), 'utf8').replace(/^\uFEFF/, ''));

const hasil = { waktu: new Date().toISOString(), base: info.base, langkah: [], galatKonsol: [] };
function catat(nama, lulus, rincian) {
  hasil.langkah.push({ nama, lulus, rincian });
  console.log((lulus ? 'PASS ' : 'FAIL ') + nama + ' | ' + rincian);
}

(async () => {
  const browser = await pw.chromium.launch({ headless: true });
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.on('console', (m) => { if (m.type() === 'error') hasil.galatKonsol.push(m.text()); });
  page.on('pageerror', (e) => hasil.galatKonsol.push('pageerror: ' + e.message));

  try {
    await page.goto(info.base, { waitUntil: 'domcontentloaded' });
    await page.getByPlaceholder('Masukkan nama pengguna...').fill('admin');
    await page.getByPlaceholder('Masukkan kata sandi...').fill(info.sandi);
    await page.getByRole('button', { name: /Masuk ke Sistem/ }).click();
    await page.getByRole('button', { name: /Daftar Pesanan & Penjualan/ }).first().waitFor({ timeout: 20000 });
    catat('login UI dengan sandi baru', true, 'menu Penjualan terlihat');

    await page.getByRole('button', { name: /Daftar Pesanan & Penjualan/ }).first().click();
    await page.getByRole('button', { name: /Tambah Transaksi \/ Pesanan/ }).first().click();

    const pindai = page.getByPlaceholder(/Fokuskan di sini/);
    const pilihProduk = page.locator('select').filter({ hasText: 'Pilih Produk' }).first();
    const total = page.getByPlaceholder('Nominal dalam Rupiah...');
    await pindai.waitFor({ timeout: 10000 });

    async function ketikPindai(kode, delay) {
      await pindai.click();
      await page.keyboard.type(kode, { delay });
      const sebelumEnter = await pindai.inputValue();
      await page.keyboard.press('Enter');
      await page.waitForTimeout(80);
      return sebelumEnter;
    }

    // 1. Barcode dikenal (produk 1).
    const sebelum1 = await ketikPindai(info.produk1.barcode, 5);
    const nilaiSelect1 = await pilihProduk.inputValue();
    const nilaiTotal1 = await total.inputValue();
    catat('karakter sampai utuh sebelum Enter', sebelum1 === info.produk1.barcode, 'kolom berisi "' + sebelum1 + '"');
    catat('barcode dikenal memilih produk yang tepat', nilaiSelect1 === String(info.produk1.id), 'select=' + nilaiSelect1 + ' harap=' + info.produk1.id);
    catat('total terisi dari harga katalog', String(nilaiTotal1) === info.produk1.totalHarapan, 'total=' + nilaiTotal1 + ' harap=' + info.produk1.totalHarapan);
    catat('kolom pindai kosong lagi setelah Enter', (await pindai.inputValue()) === '', 'kolom="' + (await pindai.inputValue()) + '"');
    await page.screenshot({ path: path.join(bukti, '01-pindai-dikenal.png') });

    // 2. Barcode produk 2 harus memilih produk 2, bukan produk 1.
    await ketikPindai(info.produk2.barcode, 5);
    const nilaiSelect2 = await pilihProduk.inputValue();
    const nilaiTotal2 = await total.inputValue();
    catat('barcode kedua memilih produk kedua', nilaiSelect2 === String(info.produk2.id), 'select=' + nilaiSelect2 + ' harap=' + info.produk2.id);
    catat('total produk kedua benar', String(nilaiTotal2) === info.produk2.totalHarapan, 'total=' + nilaiTotal2 + ' harap=' + info.produk2.totalHarapan);

    // 3. Barcode tak dikenal: pesan jelas, tidak memilih produk sembarangan.
    const kodeAsing = '0000000000000';
    await ketikPindai(kodeAsing, 5);
    const pesan = page.getByText(/tidak cocok dengan produk mana pun/);
    const adaPesan = await pesan.count() > 0;
    const teksPesan = adaPesan ? (await pesan.first().innerText()).trim() : '';
    const selectSetelahAsing = await pilihProduk.inputValue();
    catat('barcode asing menampilkan pesan', adaPesan && teksPesan.includes(kodeAsing), teksPesan || '(tidak ada)');
    catat('barcode asing tidak mengubah produk terpilih', selectSetelahAsing === String(info.produk2.id), 'select=' + selectSetelahAsing);
    catat('kolom pindai dikosongkan setelah barcode asing', (await pindai.inputValue()) === '', 'kolom="' + (await pindai.inputValue()) + '"');
    await page.screenshot({ path: path.join(bukti, '02-pindai-asing.png') });

    // 4. Tanpa Enter: aplikasi memang hanya memproses saat Enter.
    await pindai.click();
    await page.keyboard.type(info.produk1.barcode, { delay: 3 });
    await page.waitForTimeout(150);
    const nilaiTanpaEnter = await pilihProduk.inputValue();
    catat('tanpa Enter tidak memilih produk', nilaiTanpaEnter === String(info.produk2.id), 'select tetap=' + nilaiTanpaEnter);
    await pindai.fill('');

    // 5. Sepuluh pindai cepat berturut-turut.
    const mulai = Date.now();
    let semuaBenar = true;
    const rincianCepat = [];
    for (let i = 0; i < 10; i++) {
      const isi = await ketikPindai(info.produk1.barcode, 2);
      const sel = await pilihProduk.inputValue();
      const tot = await total.inputValue();
      const kolom = await pindai.inputValue();
      const ok = isi === info.produk1.barcode && sel === String(info.produk1.id) && String(tot) === info.produk1.totalHarapan && kolom === '';
      if (!ok) semuaBenar = false;
      rincianCepat.push((ok ? 'ok' : 'SALAH') + '#' + (i + 1) + ' isi=' + isi + ' sel=' + sel + ' tot=' + tot);
    }
    const durasi = Date.now() - mulai;
    catat('10 pindai cepat: tidak ada karakter hilang', semuaBenar, rincianCepat.join('; '));
    catat('10 pindai cepat selesai < 10 detik', durasi < 10000, durasi + ' ms');

    // 6. Fokus di luar kolom: hasil pindai masuk ke elemen yang sedang aktif (sifat keyboard wedge).
    await page.getByRole('heading', { name: /Tambah Transaksi/ }).click();
    await page.keyboard.type('8991234567890', { delay: 2 });
    await page.keyboard.press('Enter');
    await page.waitForTimeout(100);
    const kolomSetelahFokusLuar = await pindai.inputValue();
    catat('fokus di luar kolom: kolom pindai tidak terisi', kolomSetelahFokusLuar === '', 'kolom="' + kolomSetelahFokusLuar + '"');

    hasil.ringkasan = hasil.langkah.every((l) => l.lulus) ? 'PASS' : 'FAIL';
    hasil.jumlahLulus = hasil.langkah.filter((l) => l.lulus).length;
    hasil.jumlahGagal = hasil.langkah.filter((l) => !l.lulus).length;
  } catch (e) {
    hasil.galatFatal = e.message;
    try { await page.screenshot({ path: path.join(bukti, '99-galat.png') }); } catch {}
  } finally {
    fs.writeFileSync(path.join(kerja, 'hasil-pindai.json'), JSON.stringify(hasil, null, 2));
    console.log('RINGKASAN: ' + (hasil.ringkasan || 'FAIL') + ' lulus=' + (hasil.jumlahLulus || 0) + ' gagal=' + (hasil.jumlahGagal || 0) + (hasil.galatFatal ? ' fatal=' + hasil.galatFatal : ''));
    await browser.close();
  }
})();
