// Verifikasi Batch-3 (Keamanan & otoritas) terhadap basis data uji, bukan produksi.
// Pemakaian: SERUNI_VERIFY_BASE=http://127.0.0.1:PORT node verify-batch3.mjs <path-db-uji>
// Dipanggil dari scripts/verify.ps1; host uji wajib memakai db sementara, bukan data usaha.
import { DatabaseSync } from 'node:sqlite';

const BASE = process.env.SERUNI_VERIFY_BASE || 'http://127.0.0.1:5000';
const DB_PATH = process.argv[2];
if (!DB_PATH) { console.log('pakai: node verify-batch3.mjs <path-db-uji>'); process.exit(1); }
if (!/^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/i.test(BASE)) {
  console.log(`DITOLAK: target verifikasi ${BASE} bukan loopback; tes ini menulis data, jadi hanya boleh ke host uji lokal.`);
  process.exit(1);
}
if (/[\\/]seruni22bakery\.db$/i.test(DB_PATH)) {
  console.log('DITOLAK: jalur basis data terlihat seperti produksi.');
  process.exit(1);
}

const SANDI_BAWAAN = 'admin123';
const SANDI_BARU = 'SandiTembak2026';
const hasil = [];

function catat(nama, ok, detail) {
  hasil.push({ nama, ok, detail });
  console.log(`${ok ? 'OK  ' : 'GAGAL'} | ${nama} | ${detail ?? ''}`);
}

async function reqAs(token, method, path, body, extraHeaders) {
  const res = await fetch(BASE + path, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(extraHeaders || {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* body bukan JSON (berkas ekspor) */ }
  return { status: res.status, json, text, headers: res.headers };
}

function pesan(r) {
  const j = r.json;
  if (!j) return r.text.slice(0, 120);
  return j.errorMessage || j.error || j.detail || JSON.stringify(j).slice(0, 140);
}

async function login(username, password) {
  return reqAs(null, 'POST', '/api/auth/login', { username, password });
}

// ── 1. Gerbang must_change_password di sisi server ───────────────────────────
let sandiAdmin = SANDI_BAWAAN;
let sesi = await login('admin', SANDI_BAWAAN);
if (sesi.status === 200 && sesi.json.mustChangePassword === true) {
  catat('login sandi bawaan menandai ganti sandi wajib', true, `role=${sesi.json.role}`);
  const tokenBawaan = sesi.json.token;

  const pintuTertutup = await reqAs(tokenBawaan, 'GET', '/api/orders');
  catat('token bawaan ditolak 403 untuk endpoint data',
    pintuTertutup.status === 403 && /ganti kata sandi/i.test(pesan(pintuTertutup)),
    `http=${pintuTertutup.status} ${pesan(pintuTertutup)}`);

  const bukuTertutup = await reqAs(tokenBawaan, 'GET', '/api/journal?limit=5');
  catat('buku besar juga tertutup untuk token bawaan', bukuTertutup.status === 403,
    `http=${bukuTertutup.status}`);

  const eksporTertutup = await reqAs(tokenBawaan, 'GET', '/api/export/stock');
  catat('jalur ekspor tertutup untuk token bawaan', eksporTertutup.status === 403,
    `http=${eksporTertutup.status}`);

  const sehat = await fetch(BASE + '/health', { headers: { Authorization: `Bearer ${tokenBawaan}` } });
  catat('/health tetap terbuka (probe tanpa data)', sehat.status === 200, `http=${sehat.status}`);

  const gantiLemah = await reqAs(tokenBawaan, 'POST', '/api/auth/change-password',
    { currentPassword: SANDI_BAWAAN, newPassword: 'abc' });
  catat('sandi pengganti di bawah 8 karakter ditolak', gantiLemah.status === 400, pesan(gantiLemah));

  const gantiTanpaAngka = await reqAs(tokenBawaan, 'POST', '/api/auth/change-password',
    { currentPassword: SANDI_BAWAAN, newPassword: 'hanyahuruf' });
  catat('sandi pengganti tanpa angka ditolak', gantiTanpaAngka.status === 400, pesan(gantiTanpaAngka));

  const gantiSalah = await reqAs(tokenBawaan, 'POST', '/api/auth/change-password',
    { currentPassword: 'salah123', newPassword: SANDI_BARU });
  catat('sandi lama salah ditolak', gantiSalah.status !== 200,
    `http=${gantiSalah.status} ${pesan(gantiSalah)}`);

  const ganti = await reqAs(tokenBawaan, 'POST', '/api/auth/change-password',
    { currentPassword: SANDI_BAWAAN, newPassword: SANDI_BARU });
  catat('ganti sandi lewat saat gerbang aktif', ganti.status === 200, `http=${ganti.status} ${pesan(ganti)}`);

  const loginLagiLama = await login('admin', SANDI_BAWAAN);
  catat('sandi bawaan tidak lagi bisa masuk', loginLagiLama.status !== 200,
    `http=${loginLagiLama.status} ${pesan(loginLagiLama)}`);

  sandiAdmin = SANDI_BARU;
} else {
  const alt = await login('admin', SANDI_BARU);
  if (alt.status !== 200) { console.log('LOGIN GAGAL', alt.status, alt.text); process.exit(1); }
  catat('gerbang: DB ini sudah pernah dilepas sandi bawaannya', alt.json.mustChangePassword === false,
    `harus dijalankan pada DB uji baru untuk bukti gerbang lengkap`);
  sandiAdmin = SANDI_BARU;
}

const sesiBaru = await login('admin', sandiAdmin);
catat('setelah ganti sandi, token tidak lagi berstatus wajib ganti',
  sesiBaru.status === 200 && sesiBaru.json.mustChangePassword === false,
  `mustChangePassword=${sesiBaru.json?.mustChangePassword}`);
const buka = await reqAs(sesiBaru.json.token, 'GET', '/api/orders');
catat('endpoint data terbuka setelah sandi diganti', buka.status === 200, `http=${buka.status}`);

// ── 2. Akun uji per peran (hanya SQL, produk tidak punya endpoint pembuatan pengguna) ──
const db = new DatabaseSync(DB_PATH);
db.exec('PRAGMA busy_timeout = 8000;');
const hashAdmin = db.prepare('SELECT PasswordHash FROM Users WHERE Username = ?').get('admin').PasswordHash;
const akunUji = [['owner_b3', 'Owner'], ['admin_b3', 'Admin'], ['dapur_b3', 'Dapur'], ['pegawai_b3', 'Pegawai']];
for (const [u, role] of akunUji) {
  if (!db.prepare('SELECT 1 FROM Users WHERE Username = ?').get(u)) {
    db.prepare(`INSERT INTO Users (Username, PasswordHash, Role, IsActive, MustChangePassword, CreatedAt)
                VALUES (?, ?, ?, 1, 0, datetime('now'))`).run(u, hashAdmin, role);
  } else {
    db.prepare('UPDATE Users SET Role = ?, IsActive = 1, MustChangePassword = 0 WHERE Username = ?').run(role, u);
  }
}
db.close();

const tokens = {};
for (const [u, role] of akunUji) {
  const s = await login(u, sandiAdmin);
  if (s.status !== 200) { console.log(`login ${u} GAGAL`, s.status, s.text); process.exit(1); }
  const peranServer = { 0: 'Owner', 1: 'Admin', 2: 'Dapur', 3: 'Pegawai' }[s.json.role] ?? s.json.role;
  catat(`akun uji ${u} masuk sebagai ${peranServer}`, peranServer === role, `claim=${peranServer}`);
  tokens[u] = s.json.token;
}

const tanpaToken = await reqAs(null, 'GET', '/api/orders');
catat('tanpa token seluruh API ditolak 401', tanpaToken.status === 401, `http=${tanpaToken.status}`);

// ── 3. Hash sandi tidak ikut terkirim ────────────────────────────────────────
const daftar = await reqAs(tokens.owner_b3, 'GET', '/api/users');
const adaHash = /passwordHash/i.test(daftar.text);
catat('GET /api/users tidak membocorkan PasswordHash', daftar.status === 200 && !adaHash,
  `http=${daftar.status} body=${daftar.text.slice(0, 160)}`);

// ── 4. Matriks peran per kebijakan ───────────────────────────────────────────
// Data dasar dibuat Owner supaya penolakan datang dari otoritas, bukan dari validasi.
const owner = tokens.owner_b3;
const locations = (await reqAs(owner, 'GET', '/api/inventory/locations')).json || [];
const kitchen = locations.find((l) => (l.type || '').toLowerCase() === 'kitchen');
const warehouse = locations.find((l) => (l.type || '').toLowerCase() === 'warehouse');

const bahan = await reqAs(owner, 'POST', '/api/ingredients', {
  name: 'Tepung Uji B3', unit: 'kg', pricePerUnit: 1200000, currentStock: 50, isActive: true,
});
const ingredientId = bahan.json?.id;
const produk = await reqAs(owner, 'POST', '/api/products', {
  name: 'Roti Uji B3', category: 'Roti', sellingPriceMinorUnits: 500000,
  lines: [{ ingredientId, quantity: 2 }],
});
const productId = produk.json?.id || produk.json;
const supplier = await reqAs(owner, 'POST', '/api/suppliers', {
  name: 'Supplier Uji B3', contactName: null, phone: '0811', email: null, address: null, notes: null,
});
const supplierId = supplier.json?.id || supplier.json;
catat('data dasar untuk uji peran siap', !!ingredientId && !!productId && !!supplierId && !!kitchen && !!warehouse,
  `bahan=${ingredientId} produk=${productId} supplier=${supplierId}`);

const pesananInti = (nomor, orang, total = 100000) => ({
  customerName: `Uji ${orang} ${nomor}`, phone: '081234567890', description: 'roti uji',
  pickupDate: '2026-11-10', totalAmount: total, downPayment: 0, productTemplateId: productId,
});

async function ujiPeran(nama, token, method, path, body, boleh) {
  const r = await reqAs(token, method, path, body);
  const sesuai = boleh ? r.status < 400 : r.status === 403;
  catat(nama, sesuai, `http=${r.status} (harus ${boleh ? 'boleh' : '403'}) ${sesuai ? '' : pesan(r)}`);
  return r;
}

// Penjualan: boleh Owner/Admin/Pegawai; pembatalan hanya Owner/Admin.
// Kasir memakai harga katalog (500000 minor = Rp5.000), bukan nominal ketik sendiri.
await ujiPeran('Pegawai membuat pesanan (sales)', tokens.pegawai_b3, 'POST', '/api/orders',
  pesananInti('p', 'Pegawai', 500000), true);
const tawarPegawai = await reqAs(tokens.pegawai_b3, 'POST', '/api/orders', pesananInti('t', 'Pegawai', 100000));
catat('Pegawai tidak bisa menawar harga pesanan', tawarPegawai.status === 400 && /Rp 5\.000/.test(pesan(tawarPegawai)),
  `http=${tawarPegawai.status} ${pesan(tawarPegawai)?.slice(0, 120)}`);
await ujiPeran('Dapur membuat pesanan (sales) ditolak', tokens.dapur_b3, 'POST', '/api/orders', pesananInti('d', 'Dapur'), false);
const pesananUntukBatal = await reqAs(tokens.admin_b3, 'POST', '/api/orders', pesananInti('a', 'Admin'));
const batalId = pesananUntukBatal.json?.id;
await ujiPeran('Pegawai membatalkan pesanan (sales-cancel) ditolak', tokens.pegawai_b3,
  'PUT', `/api/orders/${batalId}/status`, { status: 'Cancelled', cancelReason: 'uji peran' }, false);
const statusPegawai = (await reqAs(tokens.pegawai_b3, 'GET', `/api/orders/${batalId}`)).json;
catat('percobaan batal oleh Pegawai tidak mengubah status',
  (statusPegawai?.status ?? '') !== 4 && String(statusPegawai?.status) !== 'Cancelled',
  `status=${statusPegawai?.status}`);
await ujiPeran('Admin membatalkan pesanan (sales-cancel) boleh', tokens.admin_b3,
  'PUT', `/api/orders/${batalId}/status`, { status: 'Cancelled', cancelReason: 'uji peran' }, true);

// Buku besar & biaya: Owner/Admin saja.
await ujiPeran('Pegawai membaca jurnal (finance-read) ditolak', tokens.pegawai_b3, 'GET', '/api/journal?limit=5', undefined, false);
await ujiPeran('Dapur membaca saldo akun (finance-read) ditolak', tokens.dapur_b3, 'GET', '/api/journal/balances', undefined, false);
await ujiPeran('Dapur mencatat biaya (finance-write) ditolak', tokens.dapur_b3, 'POST', '/api/expenses',
  { category: 'Operasional', description: 'uji peran', amount: 50000 }, false);
await ujiPeran('Admin mencatat biaya (finance-write) boleh', tokens.admin_b3, 'POST', '/api/expenses',
  { category: 'Operasional', description: 'uji peran admin', amount: 50000 }, true);

// Produksi: tulis untuk Owner/Admin/Dapur; batal hanya Owner/Admin.
const spkDapur = await ujiPeran('Dapur membuat SPK (production-write) boleh', tokens.dapur_b3,
  'POST', '/api/production/orders', { productTemplateId: productId, targetQuantity: 4, laborCost: 10000, overheadCost: 5000 }, true);
await ujiPeran('Pegawai membuat SPK (production-write) ditolak', tokens.pegawai_b3,
  'POST', '/api/production/orders', { productTemplateId: productId, targetQuantity: 4, laborCost: 10000, overheadCost: 5000 }, false);
await ujiPeran('Dapur membatalkan SPK (production-cancel) ditolak', tokens.dapur_b3,
  'PUT', `/api/production/orders/${spkDapur.json?.id}/cancel`, { reason: 'uji peran' }, false);
await ujiPeran('Owner membatalkan SPK (production-cancel) boleh', tokens.owner_b3,
  'PUT', `/api/production/orders/${spkDapur.json?.id}/cancel`, { reason: 'uji peran' }, true);

// Inventaris: Owner/Admin/Dapur.
await ujiPeran('Dapur mengoreksi stok (inventory-write) boleh', tokens.dapur_b3, 'POST', '/api/inventory/adjust',
  { locationId: kitchen.id, ingredientId, newQuantity: 30, reason: 'uji peran' }, true);
await ujiPeran('Pegawai memindahkan stok (inventory-write) ditolak', tokens.pegawai_b3, 'POST', '/api/inventory/transfer',
  { fromLocationId: kitchen.id, toLocationId: warehouse.id, ingredientId, quantity: 1, notes: null }, false);

// Pembelian: membuat PO Owner/Admin, menerima barang Owner/Admin/Dapur, batal Owner/Admin.
await ujiPeran('Dapur membuat PO (purchases-create) ditolak', tokens.dapur_b3, 'POST', '/api/purchases',
  { supplierId, expectedDate: '2026-11-11', notes: 'uji peran dapur', lines: [{ ingredientId, orderedQty: 2, unitCostMinorUnits: 1200000, unit: 'kg' }] }, false);
const poOwner = await ujiPeran('Owner membuat PO (purchases-create) boleh', owner, 'POST', '/api/purchases',
  { supplierId, expectedDate: '2026-11-11', notes: 'uji peran owner', lines: [{ ingredientId, orderedQty: 2, unitCostMinorUnits: 1200000, unit: 'kg' }] }, true);
await ujiPeran('Pegawai membuat PO (purchases-create) ditolak', tokens.pegawai_b3, 'POST', '/api/purchases',
  { supplierId, expectedDate: '2026-11-11', notes: null, lines: [{ ingredientId, orderedQty: 2, unitCostMinorUnits: 1200000, unit: 'kg' }] }, false);
await ujiPeran('Dapur membatalkan PO (purchases-cancel) ditolak', tokens.dapur_b3,
  'PUT', `/api/purchases/${poOwner.json?.id}/cancel`, undefined, false);
const detailPo = await reqAs(owner, 'GET', `/api/purchases/${poOwner.json?.id}`);
const barisPo = detailPo.json?.lines?.[0];
await ujiPeran('Dapur menerima barang (purchases-receive) boleh', tokens.dapur_b3, 'POST',
  `/api/purchases/${poOwner.json?.id}/receive`,
  { notes: 'uji terima dapur', lines: [{ purchaseOrderLineId: barisPo?.id, ingredientId, receivedQty: 1, unitCostMinorUnits: 1200000, locationId: warehouse.id, batchNumber: null, expiryDate: null }] }, true);

// Data induk & administrasi.
await ujiPeran('Pegawai membuat bahan (master-data-write) ditolak', tokens.pegawai_b3, 'POST', '/api/ingredients',
  { name: 'Bahan Larangan', unit: 'kg', pricePerUnit: 100000, currentStock: 1, isActive: true }, false);
await ujiPeran('Admin membuat bahan (master-data-write) boleh', tokens.admin_b3, 'POST', '/api/ingredients',
  { name: 'Bahan Uji Admin', unit: 'kg', pricePerUnit: 100000, currentStock: 1, isActive: true }, true);
await ujiPeran('Admin membaca daftar pengguna (administration) ditolak', tokens.admin_b3, 'GET', '/api/users', undefined, false);
await ujiPeran('Admin mengubah pengaturan (administration) ditolak', tokens.admin_b3, 'PUT', '/api/settings',
  { tokoNamaUjiB3: 'Ditolak' }, false);
await ujiPeran('Owner mengubah pengaturan (administration) boleh', owner, 'PUT', '/api/settings',
  { tokoNamaUjiB3: 'Dimiliki Owner' }, true);

// ── 5. Penomoran dokumen deterministik ───────────────────────────────────────
const nomor = [];
for (let i = 0; i < 5; i++) {
  const r = await reqAs(owner, 'POST', '/api/orders', pesananInti(i, 'Urut'));
  const detail = await reqAs(owner, 'GET', `/api/orders/${r.json?.id}`);
  nomor.push(detail.json?.orderNumber);
}
const bentukNomor = (n) => typeof n === 'string' && /^[A-Z]+-\d{8}-\d{4}$/.test(n);
catat('nomor pesanan berbentuk ORD-tanggal-urut', nomor.every((n) => n && bentukNomor(n)), nomor.join(' '));
catat('nomor pesanan unik dan menaik',
  new Set(nomor).size === nomor.length &&
  nomor.every((n, i) => i === 0 || Number(n.slice(-4)) === Number(nomor[i - 1].slice(-4)) + 1),
  nomor.join(' '));

// Lomba menulis: sebelumnya memakai Random.Shared + detik sehingga tabrakan UNIQUE jadi 500.
const lomba = await Promise.all(
  Array.from({ length: 12 }, (_, i) => reqAs(owner, 'POST', '/api/orders', pesananInti(`lomba${i}`, 'Paralel'))));
const nomorLomba = [];
for (const r of lomba) {
  const detail = await reqAs(owner, 'GET', `/api/orders/${r.json?.id}`);
  nomorLomba.push({ id: r.json?.id, nomor: detail.json?.orderNumber, http: r.status });
}
const adaGagal500 = lomba.some((r) => r.status >= 500);
catat('12 pesanan paralel tanpa galat server', !adaGagal500 && lomba.every((r) => r.status === 201 || r.status === 200),
  lomba.map((r) => r.status).join(','));
const setNomorLomba = new Set(nomorLomba.map((x) => x.nomor));
catat('nomor dari penulisan paralel tetap unik',
  setNomorLomba.size === nomorLomba.length && nomorLomba.every((x) => bentukNomor(x.nomor)),
  nomorLomba.map((x) => x.nomor).join(' '));
const urutLomba = [...setNomorLomba].map((n) => Number(n.slice(-4))).sort((a, b) => a - b);
catat('urutan nomor rapat (tanpa lompatan akibat tabrakan)',
  urutLomba.every((v, i) => i === 0 || v === urutLomba[i - 1] + 1),
  `${urutLomba[0]}..${urutLomba[urutLomba.length - 1]} (jumlah ${urutLomba.length})`);

const spkNomor = [];
for (let i = 0; i < 3; i++) {
  const r = await reqAs(owner, 'POST', '/api/production/orders',
    { productTemplateId: productId, targetQuantity: 2, laborCost: 1000, overheadCost: 500 });
  const detail = await reqAs(owner, 'GET', `/api/production/orders/${r.json?.id}`);
  spkNomor.push(detail.json?.orderNumber);
}
catat('nomor SPK memakai awalan SPK dan unik',
  spkNomor.every((n) => bentukNomor(n) && n.startsWith('SPK-')) && new Set(spkNomor).size === 3,
  spkNomor.join(' '));

const poNomor = [];
for (let i = 0; i < 2; i++) {
  const r = await reqAs(owner, 'POST', '/api/purchases', {
    supplierId, expectedDate: '2026-11-12', notes: `uji nomor ${i}`,
    lines: [{ ingredientId, orderedQty: 1, unitCostMinorUnits: 1200000, unit: 'kg' }],
  });
  const detail = await reqAs(owner, 'GET', `/api/purchases/${r.json?.id}`);
  poNomor.push(detail.json?.poNumber);
}
catat('nomor PO memakai awalan PO dan unik',
  poNomor.every((n) => bentukNomor(n) && n.startsWith('PO-')) && new Set(poNomor).size === 2,
  poNomor.join(' '));

const db2 = new DatabaseSync(DB_PATH);
const sekuens = db2.prepare('SELECT Kind, DayKey, LastValue FROM DocumentSequences ORDER BY Kind, DayKey').all();
// Tanggal dalam nomor dokumen adalah tanggal kalender toko, jadi hari pembanding dibaca dari
// DocumentSequences itu sendiri. strftime('%Y%m%d','now') memakai UTC dan keliru antara
// 00:00-06:59 WIB: dokumen hari ini ternomor hari ini tetapi dicari dengan tanggal kemarin.
const hari = (kind) => (sekuens.filter((s) => s.Kind === kind).pop()?.DayKey ?? '').replaceAll('-', '');
const jumlahDokumen = (tabel, kolom, prefix, kind) =>
  db2.prepare(`SELECT COUNT(*) AS n FROM ${tabel} WHERE ${kolom} LIKE ?`).get(`${prefix}-${hari(kind)}-%`).n;
const pesananHariIni = jumlahDokumen('SalesOrders', 'OrderNumber', 'ORD', 'Order');
const spkHariIni = jumlahDokumen('WorkOrders', 'OrderNumber', 'SPK', 'WorkOrder');
const poHariIni = jumlahDokumen('PurchaseOrders', 'PoNumber', 'PO', 'PurchaseOrder');
db2.close();
const nilai = Object.fromEntries(sekuens.map((s) => [s.Kind, s.LastValue]));
catat('penghitung hari ini = jumlah dokumen tersimpan',
  nilai.Order === pesananHariIni && nilai.WorkOrder === spkHariIni && nilai.PurchaseOrder === poHariIni,
  `Order=${nilai.Order}/${pesananHariIni} SPK=${nilai.WorkOrder}/${spkHariIni} PO=${nilai.PurchaseOrder}/${poHariIni}`);

// ── 6. CORS hanya loopback ───────────────────────────────────────────────────
const asalAsing = await reqAs(null, 'GET', '/health', undefined, { Origin: 'https://penyusup.example' });
const asaAsli = await reqAs(null, 'GET', '/health', undefined, { Origin: 'http://localhost:5173' });
const pra = await fetch(BASE + '/api/orders', {
  method: 'OPTIONS',
  headers: { Origin: 'https://penyusup.example', 'Access-Control-Request-Method': 'POST' },
});
catat('Origin asing tidak mendapat Access-Control-Allow-Origin',
  !asalAsing.headers.get('access-control-allow-origin'),
  `http=${asalAsing.status} acao=${asalAsing.headers.get('access-control-allow-origin') ?? '(kosong)'}`);
catat('Origin loopback bawaan tetap diizinkan',
  asaAsli.headers.get('access-control-allow-origin') === 'http://localhost:5173',
  `acao=${asaAsli.headers.get('access-control-allow-origin') ?? '(kosong)'}`);
catat('preflight asing ditolak tanpa izin',
  !pra.headers.get('access-control-allow-origin'),
  `http=${pra.status} acao=${pra.headers.get('access-control-allow-origin') ?? '(kosong)'}`);

const gagal = hasil.filter((h) => !h.ok);
console.log(`\nTOTAL ${hasil.length} pemeriksaan, ${gagal.length} gagal`);
if (gagal.length) { for (const g of gagal) console.log(` - ${g.nama}: ${g.detail}`); process.exit(1); }
