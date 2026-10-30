// Verifikasi Batch-6 (Jejak audit, hak akses & pegawai, kapabilitas peran, lot & kedaluwarsa,
// kotak tugas, analisis, periksa-diri) terhadap basis data uji — BUKAN data usaha.
// Dipanggil dari scripts/verify.ps1 dengan SERUNI_VERIFY_BASE + argumen path db sementara.
import { DatabaseSync } from 'node:sqlite';

const BASE = process.env.SERUNI_VERIFY_BASE || 'http://127.0.0.1:5000';
const DB_PATH = process.argv[2];
if (!DB_PATH) { console.log('pakai: node verify-batch6.mjs <path-db-uji>'); process.exit(1); }
if (!/^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/i.test(BASE)) {
  console.log(`DITOLAK: target verifikasi ${BASE} bukan loopback; tes ini menulis data.`);
  process.exit(1);
}
if (/[\\/]seruni22bakery\.db$/i.test(DB_PATH)) {
  console.log('DITOLAK: jalur basis data terlihat seperti produksi.');
  process.exit(1);
}

const SANDI_BAWAAN = 'admin123';
const SANDI_BARU = 'PemilikUji2026';
const SANDI_KASIR = 'KasirUji2026';
const hasil = [];
function catat(nama, ok, detail) {
  hasil.push({ nama, ok, detail });
  console.log(`${ok ? 'OK  ' : 'GAGAL'} | ${nama} | ${detail ?? ''}`);
}

async function req(token, method, path, body, extraHeaders) {
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
  try { json = text ? JSON.parse(text) : null; } catch { /* bukan JSON */ }
  return { status: res.status, json, text };
}
function pesan(r) {
  const j = r.json;
  if (!j) return r.text.slice(0, 120);
  return j.errorMessage || j.error || j.detail || JSON.stringify(j).slice(0, 140);
}
const login = (u, p) => req(null, 'POST', '/api/auth/login', { username: u, password: p });

// Ganti sandi wajib lalu masuk ulang: token bawaan dikunci oleh gerbang ganti-sandi.
async function masukDanGanti(username, sandiLama, sandiBaru) {
  const masuk = await login(username, sandiLama);
  if (masuk.status !== 200) return { error: `login ${username} gagal http=${masuk.status}` };
  if (masuk.json.mustChangePassword) {
    const ganti = await req(masuk.json.token, 'POST', '/api/auth/change-password',
      { currentPassword: sandiLama, newPassword: sandiBaru });
    if (ganti.status !== 200) return { error: `ganti sandi ${username} gagal http=${ganti.status}` };
    const ulang = await login(username, sandiBaru);
    if (ulang.status !== 200) return { error: `login ulang ${username} gagal` };
    return { token: ulang.json.token, user: ulang.json };
  }
  return { token: masuk.json.token, user: masuk.json };
}

const hariDepan = (n) => new Date(Date.now() + n * 86400000).toISOString().slice(0, 10);

// ── 1. Pemilik & kapabilitas ────────────────────────────────────────────────
const pemilik = await masukDanGanti('admin', SANDI_BAWAAN, SANDI_BARU);
if (pemilik.error) { catat('pemilik bisa masuk & ganti sandi', false, pemilik.error); }
else {
  const cap = pemilik.user.capabilities;
  catat('login mengirim daftar kapabilitas', Array.isArray(cap) && cap.length > 0,
    `kapabilitas=${JSON.stringify(cap)}`);
  catat('pemilik memegang administration & finance-read',
    Array.isArray(cap) && cap.includes('administration') && cap.includes('finance-read'),
    `role=${pemilik.user.role}`);
}
const T = pemilik.token;

// ── 2. Kotak tugas & analisis dari data nyata ───────────────────────────────
const tugas = await req(T, 'GET', '/api/tasks');
catat('GET /api tasks menjawab struktur', tugas.status === 200
  && Array.isArray(tugas.json?.item) && typeof tugas.json?.jumlah === 'number',
  `http=${tugas.status} jumlah=${tugas.json?.jumlah}`);

const analitik = await req(T, 'GET', '/api/analytics/sales?days=90');
catat('GET analytics punya campo pendapatan & laba', analitik.status === 200
  && 'totalPendapatan' in (analitik.json || {}) && 'labaKotor' in (analitik.json || {}),
  `http=${analitik.status}`);

// ── 3. Periksa-diri basis data (Owner only) ─────────────────────────────────
const cek = await req(T, 'GET', '/api/system/self-check');
catat('self-check mengembalikan daftar pemeriksaan', cek.status === 200
  && Array.isArray(cek.json?.checks), `http=${cek.status} n=${cek.json?.checks?.length}`);

// ── 4. Tulis jejak audit lewat perubahan nyata ──────────────────────────────
const ubahSetting = await req(T, 'PUT', '/api/settings', { outlet_name: 'Toko Uji Batch-6' });
catat('PUT settings diterapkan', ubahSetting.status === 200 && ubahSetting.json?.diterapkan >= 1,
  `http=${ubahSetting.status} diterapkan=${ubahSetting.json?.diterapkan}`);

const auditSetting = await req(T, 'GET', '/api/audit?entity=AppSetting&limit=50');
const adaJejakSetting = (auditSetting.json || []).some((e) =>
  e.entityType === 'AppSetting' && (e.newValues || '').includes('outlet_name'));
catat('jejak audit mencatat perubahan pengaturan + nilai baru',
  auditSetting.status === 200 && adaJejakSetting,
  `http=${auditSetting.status} baris=${(auditSetting.json || []).length}`);

// ── 5. Hak akses: buat pegawai, daftar tidak membocorkan sandi ───────────────
const buatKasir = await req(T, 'POST', '/api/users',
  { username: 'kasir-uji', password: SANDI_KASIR, role: 'Pegawai' });
catat('POST /api/users membuat pegawai (201)', buatKasir.status === 201 && !!buatKasir.json?.id,
  `http=${buatKasir.status} ${pesan(buatKasir)}`);

const daftarUser = await req(T, 'GET', '/api/users');
const bocor = JSON.stringify(daftarUser.json || {}).toLowerCase().includes('passwordhash');
catat('daftar user TIDAK mengirim hash sandi', daftarUser.status === 200 && !bocor,
  `http=${daftarUser.status} n=${(daftarUser.json || []).length}`);

const turunSelf = await req(T, 'PUT', `/api/users/${pemilik.user.userId ?? 1}`,
  { role: 'Owner', isActive: false });
catat('Owner terakhir tidak bisa menonaktifkan dirinya', turunSelf.status === 400,
  `http=${turunSelf.status} ${pesan(turunSelf)}`);

// ── 6. Peran rendah: menu & gerbang yang benar-benar tertutup ────────────────
const kasir = await masukDanGanti('kasir-uji', SANDI_KASIR, 'KasirGanti2026');
if (kasir.error) { catat('pegawai bisa masuk', false, kasir.error); }
else {
  const capK = kasir.user.capabilities;
  catat('pegawai TIDAK memegang administration', Array.isArray(capK) && !capK.includes('administration'),
    `kapabilitas=${JSON.stringify(capK)}`);
  const K = kasir.token;
  const auditK = await req(K, 'GET', '/api/audit');
  catat('audit ditolak 403 untuk pegawai', auditK.status === 403, `http=${auditK.status}`);
  const analitikK = await req(K, 'GET', '/api/analytics/sales');
  catat('analytics ditolak 403 untuk pegawai', analitikK.status === 403, `http=${analitikK.status}`);
  const usersK = await req(K, 'GET', '/api/users');
  catat('daftar pegawai ditolak 403 untuk pegawai', usersK.status === 403, `http=${usersK.status}`);
  const selfK = await req(K, 'GET', '/api/system/self-check');
  catat('periksa-diri ditolak 403 untuk pegawai', selfK.status === 403, `http=${selfK.status}`);
  const tugasK = await req(K, 'GET', '/api/tasks');
  catat('pegawai tetap boleh membuka kotak tugas', tugasK.status === 200, `http=${tugasK.status}`);
}

// ── 7. Lot bahan & kedaluwarsa ───────────────────────────────────────────────
const bahan = await req(T, 'POST', '/api/ingredients',
  { name: 'Tepung Terigu Uji', unit: 'kg', pricePerUnit: 1250000, currentStock: 0, minStockAlert: 0, isActive: true });
catat('bahan uji tercipta', bahan.status === 201 && !!bahan.json?.id, `http=${bahan.status} ${pesan(bahan)}`);
const bahanId = bahan.json?.id;

const lot = await req(T, 'POST', '/api/inventory/batches', {
  ingredientId: bahanId, batchNumber: 'UJI-2026-LOK1',
  receivedDate: hariDepan(-5), expiryDate: hariDepan(30), unitCostMinorUnits: 1250000,
});
catat('lot tercatat', lot.status === 201 && !!lot.json?.id, `http=${lot.status} ${pesan(lot)}`);

const daftarLot = await req(T, 'GET', '/api/inventory/batches');
const lotNampak = (daftarLot.json || []).some((b) => b.batchNumber === 'UJI-2026-LOK1');
catat('lot muncul di daftar batch & kedaluwarsa', daftarLot.status === 200 && lotNampak,
  `http=${daftarLot.status} n=${(daftarLot.json || []).length}`);

// Tanpa kedaluwarsa di masa depan, server harus menolak (bukan menyimpan tanggal basi).
const lotSalah = await req(T, 'POST', '/api/inventory/batches', {
  ingredientId: bahanId, batchNumber: 'UJI-BASI',
  expiryDate: hariDepan(-1), unitCostMinorUnits: 0,
});
catat('lot dengan kedaluwarsa lampau ditolak', lotSalah.status === 400, `http=${lotSalah.status}`);

// ── 8. Konsistensi jumlah baris audit di SQLite langsung ─────────────────────
try {
  const db = new DatabaseSync(DB_PATH);
  const n = db.prepare('SELECT COUNT(*) AS c FROM AuditLog').get().c;
  db.close();
  catat('AuditLogs benar-benar berisi baris', Number(n) >= 3, `baris=${n}`);
} catch (e) {
  catat('AuditLogs terbaca dari SQLite', false, String(e.message).slice(0, 120));
}

const gagal = hasil.filter((h) => !h.ok).length;
console.log(`\nTOTAL ${hasil.length} pemeriksaan, ${gagal} gagal`);
process.exit(gagal === 0 ? 0 : 1);
