// Ukur kinerja jalur bahan baku + beberapa halaman baca pada host Release.
// Menulis 500 bahan dan 300 pesanan ke basis data uji, jadi hanya boleh menunjuk host loopback
// yang memakai berkas sementara — jalankan lewat scripts/verify.ps1 atau host uji buatan sendiri.
//
// Pemakaian: PERF_BASE=http://127.0.0.1:PORT node perf-bahan.mjs
const BASE = process.env.PERF_BASE;
if (!BASE) { console.log('pakai: PERF_BASE=http://127.0.0.1:PORT node perf-bahan.mjs'); process.exit(1); }
if (!/^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/i.test(BASE)) {
  console.log(`DITOLAK: target kinerja ${BASE} bukan loopback; skrip ini menulis data.`);
  process.exit(1);
}

const headers = { 'Content-Type': 'application/json' };
let token = null;

async function req(method, path, body) {
  const res = await fetch(BASE + path, {
    method,
    headers: { ...headers, ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* bukan JSON */ }
  return { status: res.status, json, text };
}

async function login(u, p) {
  const r = await req('POST', '/api/auth/login', { username: u, password: p });
  if (r.status !== 200) throw new Error(`login ${r.status}`);
  return r.json;
}

const sesi = await login('admin', 'admin123');
token = sesi.token;
if (sesi.mustChangePassword) {
  await req('POST', '/api/auth/change-password', { currentPassword: 'admin123', newPassword: 'PerfUji2026' });
  token = (await login('admin', 'PerfUji2026')).token;
}

const stempel = Date.now().toString(36);

// ── Pengisian data (tidak diukur) ────────────────────────────────────────────
let gagalSeed = 0;
const t0 = performance.now();
for (let i = 0; i < 500; i++) {
  const r = await req('POST', '/api/ingredients', {
    name: `Bahan Perf ${stempel} ${String(i).padStart(3, '0')}`,
    unit: 'kg',
    pricePerUnit: 1_000_000 + i * 1000,
    currentStock: 0,
    minStockAlert: 5,
  });
  if (r.status !== 201) gagalSeed++;
}
const seedBahan = performance.now() - t0;

const t1 = performance.now();
for (let i = 0; i < 300; i++) {
  const r = await req('POST', '/api/orders', {
    customerName: `Pelanggan Perf ${stempel} ${i}`,
    description: 'Beban uji kinerja',
    pickupDate: '2030-04-04T09:00:00',
    totalAmount: 250_000,
    downPayment: 0,
    quantity: 1,
  });
  if (r.status !== 201) gagalSeed++;
}
const seedPesanan = performance.now() - t1;
console.log(`seed: 500 bahan ${seedBahan.toFixed(0)} ms, 300 pesanan ${seedPesanan.toFixed(0)} ms, gagal=${gagalSeed}`);

// ── Pengukuran ───────────────────────────────────────────────────────────────
const stat = (arr) => {
  const s = [...arr].sort((a, b) => a - b);
  const at = (p) => s[Math.min(s.length - 1, Math.floor(p * s.length))];
  return {
    n: s.length,
    min: s[0].toFixed(1),
    median: at(0.5).toFixed(1),
    p95: at(0.95).toFixed(1),
    max: s[s.length - 1].toFixed(1),
  };
};

async function ukur(label, n, fn) {
  await fn(0); // pemanasan: pemuatan pustaka/JIT tidak masuk hitungan
  const waktu = [];
  let statusTerakhir = 0;
  for (let i = 0; i < n; i++) {
    const mulai = performance.now();
    const r = await fn(i);
    waktu.push(performance.now() - mulai);
    statusTerakhir = r.status;
  }
  const s = stat(waktu);
  console.log(`${label.padEnd(34)} n=${s.n} median=${s.median} ms p95=${s.p95} ms min=${s.min} max=${s.max} (http ${statusTerakhir})`);
  return waktu;
}

const daftar = await req('GET', '/api/ingredients');
const bahan = Array.isArray(daftar.json) ? daftar.json : [];
console.log(`jumlah bahan aktif=${bahan.length}, contoh id=${bahan[0]?.id}`);
const idUji = bahan[0].id;

await ukur('GET /api/ingredients', 21, () => req('GET', '/api/ingredients'));
await ukur('POST /api/ingredients (baru)', 21, (i) => req('POST', '/api/ingredients', {
  name: `Bahan Ukur ${stempel} ${i}`, unit: 'kg', pricePerUnit: 1_200_000, currentStock: 0, minStockAlert: 0,
}));
await ukur('POST /api/ingredients (kembar→409)', 21, () => req('POST', '/api/ingredients', {
  name: `bahan ukur ${stempel} 0`, unit: 'kg', pricePerUnit: 1, currentStock: 0,
}));
await ukur('PUT /api/ingredients/{id}', 21, () => req('PUT', `/api/ingredients/${idUji}`, {
  name: bahan[0].name, unit: 'kg', pricePerUnit: 1_300_000,
}));
await ukur('GET /api/orders', 21, () => req('GET', '/api/orders'));
await ukur('GET /api/dashboard', 21, () => req('GET', '/api/dashboard'));
await ukur('GET /api/journal', 21, () => req('GET', '/api/journal'));
await ukur('GET /api/products', 21, () => req('GET', '/api/products'));
await ukur('GET /api/export/stock?format=excel', 7, () => req('GET', '/api/export/stock?format=excel'));

// ── Konkurensi ───────────────────────────────────────────────────────────────
let mulai = performance.now();
const tulis = await Promise.all(Array.from({ length: 10 }, (_, i) =>
  req('POST', '/api/ingredients', {
    name: `Bahan Paralel ${stempel} ${i}`, unit: 'kg', pricePerUnit: 500_000, currentStock: 0, minStockAlert: 0,
  })));
console.log(`10 POST paralel: ${(performance.now() - mulai).toFixed(0)} ms, status=${[...new Set(tulis.map((r) => r.status))].join(',')}`);

mulai = performance.now();
const baca = await Promise.all(Array.from({ length: 20 }, () => req('GET', '/api/ingredients')));
console.log(`20 GET paralel: ${(performance.now() - mulai).toFixed(0)} ms, status=${[...new Set(baca.map((r) => r.status))].join(',')}`);

// Sepuluh kiriman nama sama sekaligus: cek nama dan penyisipan harus satu transaksi tulis,
// jadi hasil yang benar adalah tepat satu 201 dan sembilan 409.
const kembar = await Promise.all(Array.from({ length: 10 }, () =>
  req('POST', '/api/ingredients', {
    name: `Bahan Balapan ${stempel}`, unit: 'kg', pricePerUnit: 100, currentStock: 0,
  })));
const dibuat = kembar.filter((r) => r.status === 201).length;
console.log(`10 POST nama sama paralel: 201=${dibuat}, 409=${kembar.filter((r) => r.status === 409).length}`);
if (dibuat !== 1) {
  console.log('GAGAL: nama kembar tersimpan lebih dari sekali di bawah beban paralel.');
  process.exit(1);
}
