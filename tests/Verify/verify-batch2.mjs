// Verifikasi aksi nyata Batch-2 terhadap basis data uji, bukan produksi.
// Pemakaian: SERUNI_VERIFY_BASE=http://127.0.0.1:PORT node verify-batch2.mjs
// Dipanggil dari scripts/verify.ps1; host wajib menunjuk db sementara hasil migrasi.
const BASE = process.env.SERUNI_VERIFY_BASE || 'http://127.0.0.1:5000';
if (!/^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/i.test(BASE)) {
  console.log(`DITOLAK: target verifikasi ${BASE} bukan loopback; tes ini menulis data, jadi hanya boleh ke host uji lokal.`);
  process.exit(1);
}
let token = '';
const hasil = [];
const SANDI_UJI = 'SandiTembak2026';

function catat(nama, ok, detail) {
  hasil.push({ nama, ok, detail });
  console.log(`${ok ? 'OK  ' : 'GAGAL'} | ${nama} | ${detail ?? ''}`);
}

async function api(method, path, body) {
  const res = await fetch(BASE + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* pesan plain text */ }
  return { status: res.status, json, text };
}

function pesan(r) {
  const j = r.json;
  if (!j) return r.text.slice(0, 120);
  return j.errorMessage || j.error || j.detail || JSON.stringify(j).slice(0, 140);
}

// Enum server dikirim sebagai angka; terima bentuk angka maupun nama supaya assertions jujur.
const STATUS = { Paid: 3, Pending: 0, Cancelled: 4, Received: 3, Reversed: 2, Posted: 1 };
function isStatus(v, nama) {
  if (typeof v === 'number') return v === STATUS[nama];
  return String(v) === nama;
}

async function totalJurnal() {
  const r = await api('GET', '/api/journal?limit=1000');
  const entries = r.json || [];
  let debit = 0, kredit = 0;
  for (const e of entries) for (const l of (e.lines || [])) { debit += l.debitAmount; kredit += l.creditAmount; }
  return { jumlahEntri: entries.length, debit, kredit, entries };
}

async function jumlahMutasi() {
  const r = await api('GET', '/api/inventory/movements?limit=500');
  return (r.json || []).length;
}

async function stokDiLokasi(locationId, ingredientId) {
  const r = await api('GET', `/api/inventory/locations/${locationId}/stock`);
  const baris = (r.json || []).find((x) => x.ingredientId === ingredientId);
  return baris ? Number(baris.quantity ?? baris.currentStock ?? 0) : 0;
}

// Produk jadi punya buku sendiri sejak migrasi 011: menjual roti menggeser etalase,
// bukan mengurangi bahan dapur (bahan sudah dipotong saat SPK diselesaikan).
async function stokProduk(locationId, productTemplateId) {
  const r = await api('GET', `/api/inventory/finished-goods?locationId=${locationId}`);
  const baris = (r.json || []).find((x) => x.productTemplateId === productTemplateId);
  return baris ? Number(baris.quantity ?? 0) : 0;
}

// ── Login ───────────────────────────────────────────────────────────────────
// Basis data baru selalu menandai admin wajib ganti sandi, dan server menolak endpoint data
// sebelum hal itu terjadi (Batch-3). Gerbangnya dibuka lewat API, seperti pengguna sungguhan.
const login = await api('POST', '/api/auth/login', { username: 'admin', password: 'admin123' });
if (!login.json?.token) { console.log('LOGIN GAGAL', login.status, login.text); process.exit(1); }
token = login.json.token;
catat('login admin', true, `user ${login.json.userId} ${login.json.role}`);

if (login.json.mustChangePassword) {
  const tolak = await api('GET', '/api/inventory/locations');
  catat('gerbang sandi menolak endpoint data sebelum ganti sandi', tolak.status === 403,
    `http=${tolak.status} ${pesan(tolak)}`);

  const ganti = await api('POST', '/api/auth/change-password', {
    currentPassword: 'admin123', newPassword: SANDI_UJI,
  });
  catat('ganti sandi bawaan lewat API', ganti.status === 200, pesan(ganti));

  const masukLagi = await api('POST', '/api/auth/login', { username: 'admin', password: SANDI_UJI });
  if (!masukLagi.json?.token) { console.log('LOGIN ULANG GAGAL', masukLagi.status, masukLagi.text); process.exit(1); }
  token = masukLagi.json.token;
  catat('sandi baru membuka endpoint data', !masukLagi.json.mustChangePassword,
    `mustChangePassword=${masukLagi.json.mustChangePassword}`);
}

// ── Data dasar ──────────────────────────────────────────────────────────────
const locations = (await api('GET', '/api/inventory/locations')).json || [];
const kitchen = locations.find((l) => l.type === 'Kitchen');
const warehouse = locations.find((l) => l.type === 'Warehouse');
const display = locations.find((l) => l.type === 'Display');
catat('lokasi tersediaan', !!kitchen && !!warehouse && !!display,
  `kitchen=${kitchen?.id} warehouse=${warehouse?.id} display=${display?.id}`);

const tepung = await api('POST', '/api/ingredients', {
  name: 'Tepung Terigu Uji', unit: 'kg', pricePerUnit: 1200000, currentStock: 50, isActive: true,
});
const ingredientId = tepung.json?.id;
const produk = await api('POST', '/api/products', {
  name: 'Roti Uji', category: 'Roti', sellingPriceMinorUnits: 500000,
  lines: [{ ingredientId, quantity: 2 }],
});
const productId = produk.json?.id || produk.json;
catat('bahan + produk uji dibuat', !!ingredientId && !!productId, `ingredient=${ingredientId} product=${productId}`);

// Dapur perlu stok nyata; kalau kosong, semua uji potongan stok hanya lulus secara kosong.
const isiDapur = await api('POST', '/api/inventory/adjust', {
  locationId: kitchen.id, ingredientId, newQuantity: 40, reason: 'Stok awal uji Batch-2',
});
catat('stok awal dapur diisi', isiDapur.status === 200, pesan(isiDapur));

// ── Pagar nominal pesanan ───────────────────────────────────────────────────
const rp0 = await api('POST', '/api/orders', {
  customerName: 'Uji Nol', phone: '08000000001', description: 'd', pickupDate: '2026-11-01',
  totalAmount: 0, downPayment: 0, productTemplateId: productId,
});
catat('pesanan bernilai 0 ditolak', rp0.status === 400, pesan(rp0));

const dpLebih = await api('POST', '/api/orders', {
  customerName: 'Uji DP', phone: '08000000002', description: 'd', pickupDate: '2026-11-01',
  totalAmount: 200000, downPayment: 300000, productTemplateId: productId,
});
catat('uang muka melebihi nilai pesanan ditolak', dpLebih.status === 400, pesan(dpLebih));

const nominalGila = await api('POST', '/api/orders', {
  customerName: 'Uji Besar', phone: '08000000003', description: 'd', pickupDate: '2026-11-01',
  totalAmount: 9_000_000_000_000, downPayment: 0, productTemplateId: productId,
});
catat('nominal menembus atap ditolak', nominalGila.status === 400, pesan(nominalGila));

// ── Pesanan sah + jurnal DP atomik ──────────────────────────────────────────
const sebelumDp = await totalJurnal();
const pesanan = await api('POST', '/api/orders', {
  customerName: 'Bu Sari', phone: '081234567890', description: 'Roti uji 20 pcs',
  pickupDate: '2026-11-05', totalAmount: 200000, downPayment: 50000,
  productTemplateId: productId, idempotencyKey: 'batch2-dp-001',
});
const orderId = pesanan.json?.id;
const sesudahDp = await totalJurnal();
catat('pesanan + DP tersimpan', pesanan.status === 201 && !!orderId, `order=${orderId} ${pesan(pesanan)}`);
catat('jurnal DP ikut tertulis dalam transaksi yang sama',
  sesudahDp.jumlahEntri === sebelumDp.jumlahEntri + 1,
  `entri ${sebelumDp.jumlahEntri} -> ${sesudahDp.jumlahEntri}`);
const dpEntri = sesudahDp.entries[0];
catat('entri DP menautkan SourceDocumentId pesanan',
  dpEntri.sourceDocumentId === orderId && dpEntri.sourceDocumentType === 'SalesOrder',
  `${dpEntri.sourceDocumentType}#${dpEntri.sourceDocumentId} :: ${dpEntri.description}`);

// ── SPK ──────────────────────────────────────────────────────────────────────
// Produksi dijalankan lebih dulu: etalase harus berisi barang sebelum pesanan bisa
// dilunasi atau diproses. Kalau urutannya dibalik, penjagaan stok produk jadi justru
// menolak penjualan yang belum pernah diproduksi — dan itu memang perilaku yang benar.
const spkBesar = await api('POST', '/api/production/orders', { productTemplateId: productId, targetQuantity: 0, laborCost: 0, overheadCost: 0 });
catat('SPK target 0 ditolak', spkBesar.status === 400, pesan(spkBesar));
const spkBeban = await api('POST', '/api/production/orders', { productTemplateId: productId, targetQuantity: 5, laborCost: -1, overheadCost: 0 });
catat('SPK biaya tenaga kerja negatif ditolak', spkBeban.status === 400, pesan(spkBeban));

const spk = await api('POST', '/api/production/orders', { productTemplateId: productId, targetQuantity: 5, laborCost: 100000, overheadCost: 50000 });
const spkId = spk.json?.id;
catat('SPK dibuat', spk.status === 201 && !!spkId, `spk=${spkId}`);

const jurnalSebelumSPK = await totalJurnal();
const scrapSalah = await api('POST', `/api/production/orders/${spkId}/complete`, {
  actualGoodQty: 4, actualFailedQty: 1,
  materials: [{ ingredientId, actualQty: 10, unitCost: 1200000 }],
  scraps: [{ quantity: 1, reasonCategory: 'Rusak panggang', actionTaken: 'Dibuang', notes: 'alasan bebas' }],
});
const jurnalSetelahScrapSalah = await totalJurnal();
catat('alasan scrap tak dikenal ditolak dengan pesan terbaca', scrapSalah.status === 400, pesan(scrapSalah));
catat('scrap ditolak tidak menulis jurnal',
  jurnalSetelahScrapSalah.jumlahEntri === jurnalSebelumSPK.jumlahEntri,
  `entri ${jurnalSebelumSPK.jumlahEntri} -> ${jurnalSetelahScrapSalah.jumlahEntri}`);

const scrapKurang = await api('POST', `/api/production/orders/${spkId}/complete`, {
  actualGoodQty: 4, actualFailedQty: 3,
  materials: [{ ingredientId, actualQty: 10, unitCost: 1200000 }],
  scraps: [{ quantity: 1, reasonCategory: 'Gosong', actionTaken: 'Scrap_Loss', notes: 'scrap kurang' }],
});
catat('scrap lebih kecil dari produk rusak ditolak', scrapKurang.status === 400, pesan(scrapKurang));

const stokProdukSebelumProduksi = await stokProduk(display.id, productId);
const selesai = await api('POST', `/api/production/orders/${spkId}/complete`, {
  actualGoodQty: 4, actualFailedQty: 1,
  materials: [{ ingredientId, actualQty: 10, unitCost: 1200000 }],
  scraps: [{ quantity: 1, reasonCategory: 'Gosong', actionTaken: 'Scrap_Loss', notes: 'uji' }],
});
const stokProdukSesudahProduksi = await stokProduk(display.id, productId);
const jurnalSesudahSPK = await totalJurnal();
const entriSPK = jurnalSesudahSPK.entries.find((e) => (e.description || '').includes('Penyelesaian Produksi SPK'));
catat('penyelesaian SPK mencatat jurnal', selesai.status === 200 && !!entriSPK,
  `http=${selesai.status} ${pesan(selesai)} :: ` +
  (entriSPK?.lines || []).map((l) => `${l.account?.code} D${l.debitAmount}/K${l.creditAmount}`).join(' '));
catat('hasil produksi menambah stok produk jadi di etalase',
  stokProdukSesudahProduksi === stokProdukSebelumProduksi + 4,
  `stok produk ${stokProdukSebelumProduksi} -> ${stokProdukSesudahProduksi}`);

// Skema jurnal SPK: bahan 12.000.000 dari persediaan dapur dipecah menjadi barang jadi
// 9.600.000 (4 unit baik) dan kerugian scrap 2.400.000 (1 unit), ditambah tenaga kerja
// 100.000 dan overhead 50.000 yang dibayar tunai. Jumlahnya harus seimbang.
const totalDebitSPK = (entriSPK?.lines || []).reduce((a, l) => a + l.debitAmount, 0);
const totalKreditSPK = (entriSPK?.lines || []).reduce((a, l) => a + l.creditAmount, 0);
const barisAkunSPK = (kode) => (entriSPK?.lines || []).filter((l) => l.account?.code === kode);
const debitAkun = (kode) => barisAkunSPK(kode).reduce((a, l) => a + l.debitAmount, 0);
const kreditAkun = (kode) => barisAkunSPK(kode).reduce((a, l) => a + l.creditAmount, 0);
catat('jurnal SPK seimbang dan memecah biaya bahan dengan benar',
  !!entriSPK && totalDebitSPK === totalKreditSPK && totalDebitSPK === 12150000 &&
  debitAkun('1-006') === 9600000 && debitAkun('5-004') === 2400000 &&
  kreditAkun('1-004') === 12000000 && debitAkun('5-003') === 100000 && debitAkun('5-002') === 50000 &&
  kreditAkun('1-001') === 150000,
  `debit=${totalDebitSPK} kredit=${totalKreditSPK} ` +
  `fg=${debitAkun('1-006')} scrap=${debitAkun('5-004')} dapur=${kreditAkun('1-004')}`);

// Muatan ini sengaja lengkap valid: penolakannya harus datang dari penjaga status SPK,
// bukan dari pagar scrap, supaya 'ditolak' tidak menjadi hijau palsu.
const selesaiLagi = await api('POST', `/api/production/orders/${spkId}/complete`, {
  actualGoodQty: 4, actualFailedQty: 1,
  materials: [{ ingredientId, actualQty: 10, unitCost: 1200000 }],
  scraps: [{ quantity: 1, reasonCategory: 'Gosong', actionTaken: 'Scrap_Loss', notes: 'ulang' }],
});
const jurnalSetelahSPKGanda = await totalJurnal();
const pesanSelesaiLagi = pesan(selesaiLagi);
catat('SPK selesai ulang ditolak oleh penjaga status',
  selesaiLagi.status === 400 && /tidak dapat diselesaikan ulang/.test(pesanSelesaiLagi), pesanSelesaiLagi);
catat('SPK selesai ulang tidak menambah jurnal',
  jurnalSetelahSPKGanda.jumlahEntri === jurnalSesudahSPK.jumlahEntri,
  `entri ${jurnalSesudahSPK.jumlahEntri} -> ${jurnalSetelahSPKGanda.jumlahEntri}`);

// ── Pembayaran ──────────────────────────────────────────────────────────────
// Server hanya menerima "Tunai" dan "Transfer" (sama dengan pilihan di layar kasir);
// laporan pendapatan per metode bergantung pada dua nilai itu.
const metodeAsing = await api('POST', `/api/orders/${orderId}/payment`, {
  orderId, amount: 10000, method: 'Cash', idempotencyKey: 'batch2-bayar-metode-asing',
});
catat('metode pembayaran di luar Tunai/Transfer ditolak',
  metodeAsing.status === 400 && /Tunai, Transfer/.test(pesan(metodeAsing)), pesan(metodeAsing));

const lebih = await api('POST', `/api/orders/${orderId}/payment`, {
  orderId, amount: 500000, method: 'Tunai', idempotencyKey: 'batch2-bayar-berlebih',
});
catat('pembayaran melebihi sisa bayar ditolak', lebih.status === 400, pesan(lebih));

const nol = await api('POST', `/api/orders/${orderId}/payment`, {
  orderId, amount: 0, method: 'Tunai', idempotencyKey: 'batch2-bayar-nol',
});
catat('pembayaran nol ditolak', nol.status === 400, pesan(nol));

const sebelumBayar = await totalJurnal();
const parsial = await api('POST', `/api/orders/${orderId}/payment`, {
  orderId, amount: 100000, method: 'Tunai', idempotencyKey: 'batch2-bayar-parsial',
});
const sesudahBayar = await totalJurnal();
catat('pembayaran sebagian diterima', parsial.status === 200, pesan(parsial));
catat('satu entri jurnal per pembayaran parsial',
  sesudahBayar.jumlahEntri === sebelumBayar.jumlahEntri + 1,
  `entri ${sesudahBayar.jumlahEntri}`);
const orderSetengah = (await api('GET', `/api/orders/${orderId}`)).json;
const bayarEntri = orderSetengah.payments?.[orderSetengah.payments.length - 1];
catat('pembayaran menautkan JournalEntryId', !!bayarEntri?.journalEntryId,
  `payment.journalEntryId=${bayarEntri?.journalEntryId}`);

const lunas = await api('POST', `/api/orders/${orderId}/payment`, {
  orderId, amount: 50000, method: 'Tunai', idempotencyKey: 'batch2-bayar-lunas',
});
const orderLunas = (await api('GET', `/api/orders/${orderId}`)).json;
const jurnalLunas = await totalJurnal();
const adaPendapatan = jurnalLunas.entries.some((e) => (e.description || '').startsWith('Pendapatan Pesanan'));
catat('pelunasan mengubah status jadi Paid', lunas.status === 200 && isStatus(orderLunas.status, 'Paid'),
  `status=${orderLunas.status}`);
catat('pengakuan pendapatan tercatat saat lunas', adaPendapatan, `total entri ${jurnalLunas.jumlahEntri}`);

const stokSetelahLunas = await stokProduk(display.id, productId);
catat('pelunasan memotong stok produk jadi sebanyak jumlah pesanan',
  stokSetelahLunas === stokProdukSesudahProduksi - Number(orderLunas.quantity ?? 1),
  `stok produk ${stokProdukSesudahProduksi} -> ${stokSetelahLunas} untuk ${orderLunas.quantity ?? 1} unit`);

const sesudahLunas = await api('POST', `/api/orders/${orderId}/payment`, {
  orderId, amount: 10000, method: 'Tunai', idempotencyKey: 'batch2-bayar-sesudah-lunas',
});
catat('pembayaran ganda setelah lunas ditolak', sesudahLunas.status === 400, pesan(sesudahLunas));

const replay = await api('POST', `/api/orders/${orderId}/payment`, {
  orderId, amount: 50000, method: 'Tunai', idempotencyKey: 'batch2-bayar-lunas',
});
const jurnalSetelahReplay = await totalJurnal();
catat('idempotensi: pembayaran kunci sama tidak menulis ulang',
  replay.status === 200 && jurnalSetelahReplay.jumlahEntri === jurnalLunas.jumlahEntri,
  `entri tetap ${jurnalSetelahReplay.jumlahEntri}`);

// ── Jurnal harus selalu seimbang ────────────────────────────────────────────
const saldo = await totalJurnal();
catat('total debit = total kredit di seluruh jurnal', saldo.debit === saldo.kredit,
  `debit=${saldo.debit} kredit=${saldo.kredit} entri=${saldo.jumlahEntri}`);

// ── Status: potong stok, lalu batal + pembalik ───────────────────────────────
const pesanan2 = await api('POST', '/api/orders', {
  customerName: 'Pak Devi', phone: '081234567891', description: 'Roti uji 4 pcs',
  pickupDate: '2026-11-06', totalAmount: 100000, downPayment: 20000, productTemplateId: productId,
});
const order2Id = pesanan2.json?.id;
const stokSebelum = await stokProduk(display.id, productId);
const mutasiSebelum = await jumlahMutasi();
const jurnalSebelumProses = await totalJurnal();

const proses = await api('PUT', `/api/orders/${order2Id}/status`, { orderId: order2Id, status: 'Processing' });
const stokSesudahProses = await stokProduk(display.id, productId);
catat('pemrosesan pesanan memotong stok produk jadi di etalase',
  proses.status === 200 && stokSesudahProses === stokSebelum - 1,
  `stok produk ${stokSebelum} -> ${stokSesudahProses}`);

const jurnalSebelumBatal = await totalJurnal();
const batal = await api('PUT', `/api/orders/${order2Id}/status`, {
  orderId: order2Id, status: 'Cancelled', cancelReason: 'Uji pembalikan Batch-2',
});
const stokSesudahBatal = await stokProduk(display.id, productId);
const jurnalSesudahBatal = await totalJurnal();
const pembalik = jurnalSesudahBatal.entries.filter((e) => (e.description || '').startsWith('Pembalik:'));
const asliDibalik = jurnalSesudahBatal.entries.filter((e) => isStatus(e.status, 'Reversed'));
catat('pembatalan mengembalikan stok produk jadi', batal.status === 200 && stokSesudahBatal === stokSebelum,
  `stok produk ${stokSesudahProses} -> ${stokSesudahBatal} (awal ${stokSebelum})`);
catat('pembatalan menulis entri pembalik', pembalik.length >= 1,
  `${pembalik.length} pembalik, entri asli berstatus Reversed=${asliDibalik.length}`);

const batalLagi = await api('PUT', `/api/orders/${order2Id}/status`, {
  orderId: order2Id, status: 'Cancelled', cancelReason: 'pembatalan ganda',
});
const jurnalSetelahBatalGanda = await totalJurnal();
catat('pembatalan ganda ditolak', batalLagi.status === 400, pesan(batalLagi));
catat('pembatalan ganda tidak menambah jurnal',
  jurnalSetelahBatalGanda.jumlahEntri === jurnalSesudahBatal.jumlahEntri,
  `entri ${jurnalSetelahBatalGanda.jumlahEntri}`);

// ── Stok tidak cukup: tolak TANPA menulis apa pun ────────────────────────────
const bahanMahal = await api('POST', '/api/ingredients', {
  name: 'Keju Uji', unit: 'kg', pricePerUnit: 9000000, currentStock: 0, isActive: true,
});
const kejuId = bahanMahal.json?.id;
const produkStokHabis = await api('POST', '/api/products', {
  name: 'Kue Keju Uji', category: 'Kue', sellingPriceMinorUnits: 800000,
  lines: [{ ingredientId: kejuId, quantity: 5 }],
});
const produkHabisId = produkStokHabis.json?.id || produkStokHabis.json;
const pesanan3 = await api('POST', '/api/orders', {
  customerName: 'Uji Stok Habis', phone: '081234567892', description: 'kue keju',
  pickupDate: '2026-11-07', totalAmount: 800000, downPayment: 0, productTemplateId: produkHabisId,
});
const order3Id = pesanan3.json?.id;
const jurnalSebelumGagal = await totalJurnal();
const mutasiSebelumGagal = await jumlahMutasi();
const stokKejuSebelum = await stokDiLokasi(kitchen.id, kejuId);

const gagalProses = await api('PUT', `/api/orders/${order3Id}/status`, { orderId: order3Id, status: 'Processing' });
const order3Setelah = (await api('GET', `/api/orders/${order3Id}`)).json;
const jurnalSetelahGagal = await totalJurnal();
const mutasiSetelahGagal = await jumlahMutasi();
catat('stok tidak cukup ditolak dengan pesan jelas', gagalProses.status === 400, pesan(gagalProses));
catat('status pesanan tidak berubah saat stok ditolak', isStatus(order3Setelah.status, 'Pending'),
  `status=${order3Setelah.status}, stockDeducted=${order3Setelah.stockDeducted}`);
catat('transaksi gagal tidak meninggalkan mutasi stok',
  mutasiSetelahGagal === mutasiSebelumGagal && (await stokDiLokasi(kitchen.id, kejuId)) === stokKejuSebelum,
  `mutasi ${mutasiSebelumGagal} -> ${mutasiSetelahGagal}`);
catat('transaksi gagal tidak meninggalkan jurnal',
  jurnalSetelahGagal.jumlahEntri === jurnalSebelumGagal.jumlahEntri,
  `entri ${jurnalSebelumGagal.jumlahEntri} -> ${jurnalSetelahGagal.jumlahEntri}`);

// ── Biaya ────────────────────────────────────────────────────────────────────
const biayaNegatif = await api('POST', '/api/expenses', { category: 'Operasional', description: 'uji', amount: -50000 });
catat('biaya negatif ditolak', biayaNegatif.status === 400, pesan(biayaNegatif));
const biayaKosong = await api('POST', '/api/expenses', { category: 'Operasional', description: '  ', amount: 50000 });
catat('biaya tanpa keterangan ditolak', biayaKosong.status === 400, pesan(biayaKosong));
const jurnalSebelumBiaya = await totalJurnal();
const biayaSah = await api('POST', '/api/expenses', { category: 'HPP', description: 'Belanja tepung uji', amount: 75000 });
const jurnalSetelahBiaya = await totalJurnal();
const entriBiaya = jurnalSetelahBiaya.entries.find((e) => (e.description || '').includes('Belanja tepung uji'));
const akunBiaya = (entriBiaya?.lines || []).map((l) => l.account?.code).sort().join('+');
catat('biaya sah tercatat dan menjurnal', biayaSah.status === 200 && jurnalSetelahBiaya.jumlahEntri === jurnalSebelumBiaya.jumlahEntri + 1,
  `akun ${akunBiaya}`);
catat('kategori HPP memakai akun 5-001', akunBiaya === '1-001+5-001', `akun=${akunBiaya}`);

// ── Pembelian + penerimaan ───────────────────────────────────────────────────
const supplier = await api('POST', '/api/suppliers', { name: 'Supplier Uji', phone: '0811', email: null, address: null, notes: null, contactName: null });
const supplierId = supplier.json?.id || supplier.json;
const po = await api('POST', '/api/purchases', {
  supplierId, expectedDate: '2026-11-02', notes: 'uji batch2',
  lines: [{ ingredientId, orderedQty: 2.5, unitCostMinorUnits: 1200049, unit: 'kg' }],
});
const poId = po.json?.id;
catat('PO dibuat', po.status === 200 && !!poId, `po=${poId} ${pesan(po)}`);

const poBurah = await api('POST', '/api/purchases', {
  supplierId, expectedDate: null, notes: null,
  lines: [{ ingredientId, orderedQty: 0, unitCostMinorUnits: 100, unit: 'kg' }],
});
catat('baris PO berjumlah 0 ditolak', poBurah.status === 400, pesan(poBurah));

const poSatuan = await api('POST', '/api/purchases', {
  supplierId, expectedDate: null, notes: null,
  lines: [{ ingredientId, orderedQty: 5, unitCostMinorUnits: 100, unit: '' }],
});
catat('baris PO tanpa satuan ditolak', poSatuan.status === 400, pesan(poSatuan));

const poDetail = (await api('GET', `/api/purchases/${poId}`)).json;
const poLine = poDetail.lines[0];
const stokGudangSebelum = await stokDiLokasi(warehouse.id, ingredientId);
const jurnalSebelumTerima = await totalJurnal();

const terimaLebih = await api('POST', `/api/purchases/${poId}/receive`, {
  notes: 'kelebihan', lines: [{ purchaseOrderLineId: poLine.id, ingredientId, receivedQty: 999, unitCostMinorUnits: 1200000, locationId: warehouse.id }],
});
const stokSetelahTolak = await stokDiLokasi(warehouse.id, ingredientId);
const jurnalSetelahTolak = await totalJurnal();
catat('penerimaan melebihi sisa terima ditolak', terimaLebih.status === 400, pesan(terimaLebih));
catat('penerimaan ditolak tidak mengubah stok', stokSetelahTolak === stokGudangSebelum,
  `stok ${stokGudangSebelum} -> ${stokSetelahTolak}`);
catat('penerimaan ditolak tidak meninggalkan jurnal',
  jurnalSetelahTolak.jumlahEntri === jurnalSebelumTerima.jumlahEntri,
  `entri ${jurnalSetelahTolak.jumlahEntri}`);

const salahBahan = await api('POST', `/api/purchases/${poId}/receive`, {
  notes: 'bahan salah', lines: [{ purchaseOrderLineId: poLine.id, ingredientId: kejuId, receivedQty: 1, unitCostMinorUnits: 1000, locationId: warehouse.id }],
});
catat('baris PO diisi bahan lain ditolak', salahBahan.status === 400, pesan(salahBahan));

const terima = await api('POST', `/api/purchases/${poId}/receive`, {
  notes: 'terima utuh', lines: [{ purchaseOrderLineId: poLine.id, ingredientId, receivedQty: 2.5, unitCostMinorUnits: 1200049, locationId: warehouse.id }],
});
const stokSesudahTerima = await stokDiLokasi(warehouse.id, ingredientId);
const poSesudah = (await api('GET', `/api/purchases/${poId}`)).json;
const jurnalSesudahTerima = await totalJurnal();
const entriTerima = jurnalSesudahTerima.entries.find((e) => (e.description || '').startsWith('Penerimaan Barang PO'));
catat('penerimaan barang sah diterima', terima.status === 200, pesan(terima));
catat('stok gudang bertambah sesuai jumlah terima', stokSesudahTerima === stokGudangSebelum + 2.5,
  `${stokGudangSebelum} -> ${stokSesudahTerima}`);
catat('status PO jadi Received', isStatus(poSesudah.status, 'Received'), `status=${poSesudah.status}`);
catat('jurnal Dr Persediaan / Cr Utang tercatat', !!entriTerima,
  (entriTerima?.lines || []).map((l) => `${l.account?.code}:${l.debitAmount}/${l.creditAmount}`).join(' '));

const nilaiTerima = entriTerima?.lines?.[0]?.debitAmount;
catat('nilai penerimaan membulatkan pecahan satuan (2,5 x 1200049 = 3000123)',
  nilaiTerima === 3000123, `debit=${nilaiTerima} (pembulatan ke bawah memberi 3000122)`);

// ── Rekonsiliasi akhir ───────────────────────────────────────────────────────
const akhir = await totalJurnal();
catat('saldo akhir jurnal tetap seimbang', akhir.debit === akhir.kredit,
  `debit=${akhir.debit} kredit=${akhir.kredit} entri=${akhir.jumlahEntri}`);

const balances = (await api('GET', '/api/journal/balances')).json || [];
const kas = balances.find((b) => b.code === '1-001');
const utang = balances.find((b) => b.code === '2-002');
const pendapatan = balances.find((b) => b.code === '4-001');
// Uji ini tidak memberi saldo kas awal, jadi kas harus tepat sama dengan uang yang benar-benar
// bergerak: pesanan Bu Sari lunas 200.000, biaya 75.000, upah+overhead SPK 150.000.
// Utang hanya dari penerimaan PO, dan pendapatan diakui sekali saat pesanan lunas.
const kasSeharusnya = 200000 - 75000 - 150000;
catat('saldo akun sesuai arus kas dan dokumen yang tercatat',
  kas.balance === kasSeharusnya && utang.balance === 3000123 && pendapatan.balance === 200000,
  `kas=${kas.balance} (harus ${kasSeharusnya}) utang=${utang.balance} pendapatan=${pendapatan.balance}`);

const gagal = hasil.filter((h) => !h.ok);
console.log(`\nTOTAL ${hasil.length} pemeriksaan, ${gagal.length} gagal`);
if (gagal.length) { for (const g of gagal) console.log(` - ${g.nama}: ${g.detail}`); process.exit(1); }
