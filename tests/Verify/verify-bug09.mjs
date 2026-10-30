// Verifikasi BUG-09 (layar bahan baku): validasi masukan, pagar peran, jejak audit, dan teks
// berbahaya disimpan sebagai data — terhadap basis data uji, BUKAN data usaha.
// Dipanggil dari scripts/verify.ps1 dengan SERUNI_VERIFY_BASE + argumen path db sementara.
import { DatabaseSync } from 'node:sqlite';

const BASE = process.env.SERUNI_VERIFY_BASE || 'http://127.0.0.1:5000';
const DB_PATH = process.argv[2];
if (!DB_PATH) { console.log('pakai: node verify-bug09.mjs <path-db-uji>'); process.exit(1); }
if (!/^https?:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/i.test(BASE)) {
  console.log(`DITOLAK: target verifikasi ${BASE} bukan loopback; tes ini menulis data.`);
  process.exit(1);
}
if (/[\\/]seruni22bakery\.db$/i.test(DB_PATH)) {
  console.log('DITOLAK: jalur basis data terlihat seperti produksi.');
  process.exit(1);
}

const SANDI_BAWAAN = 'admin123';
const SANDI_BARU = 'PemilikUjiB9';
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
const pesan = (r) => (r.json ? (r.json.errorMessage || r.json.error || JSON.stringify(r.json)).slice(0, 140) : r.text.slice(0, 120));
const login = (u, p) => req(null, 'POST', '/api/auth/login', { username: u, password: p });

async function masukDanGanti(username, sandiLama, sandiBaru) {
  const masuk = await login(username, sandiLama);
  if (masuk.status !== 200) return { error: `login ${username} gagal http=${masuk.status}` };
  if (!masuk.json.mustChangePassword) return { token: masuk.json.token };
  const ganti = await req(masuk.json.token, 'POST', '/api/auth/change-password',
    { currentPassword: sandiLama, newPassword: sandiBaru });
  if (ganti.status !== 200) return { error: `ganti sandi ${username} gagal http=${ganti.status}` };
  const ulang = await login(username, sandiBaru);
  return ulang.status === 200 ? { token: ulang.json.token } : { error: `login ulang ${username} gagal` };
}

// Akun peran dibuat lewat API produksi; gerbang sandi bawaan dilewati seperti pegawai sungguhan.
async function buatPeran(owner, role) {
  const username = `uji${role.toLowerCase()}b9${Math.random().toString(36).slice(2, 8)}`.slice(0, 24);
  const buat = await req(owner, 'POST', '/api/users', { username, password: 'SandiAwalB9', role });
  if (buat.status !== 201) return { error: `buat ${role} http=${buat.status}` };
  const masuk = await masukDanGanti(username, 'SandiAwalB9', 'SandiHarianB9');
  return masuk.error ? masuk : { token: masuk.token, id: buat.json.id };
}

const bahanBaru = (nama, tambahan) => ({
  name: nama, unit: 'kg', pricePerUnit: 1_200_000, currentStock: 0, minStockAlert: 0, ...(tambahan || {}),
});

const daftarBahan = async (token) => {
  const r = await req(token, 'GET', '/api/ingredients');
  return { status: r.status, items: Array.isArray(r.json) ? r.json : [] };
};

// ── 1. Tanpa token: semuanya 401, bukan 404/200 ──────────────────────────────
for (const [metode, jalur] of [
  ['POST', '/api/ingredients'],
  ['PUT', '/api/ingredients/1'],
  ['DELETE', '/api/ingredients/1'],
]) {
  const r = await req(null, metode, jalur, metode === 'DELETE' ? undefined : bahanBaru('Tanpa Token'));
  catat(`${metode} ${jalur} tanpa token ditolak 401`, r.status === 401, `http=${r.status}`);
}

// ── 2. Pemilik masuk, lalu akun Pegawai & Dapur ──────────────────────────────
const pemilik = await masukDanGanti('admin', SANDI_BAWAAN, SANDI_BARU);
if (pemilik.error) { catat('pemilik bisa masuk', false, pemilik.error); }
else catat('pemilik bisa masuk', true, '');
const T = pemilik.token;

const pegawai = await buatPeran(T, 'Pegawai');
const dapur = await buatPeran(T, 'Dapur');
catat('akun Pegawai & Dapur terbentuk', !pegawai.error && !dapur.error, `${pegawai.error || ''} ${dapur.error || ''}`);

// ── 3. Bahan sah oleh pemilik ────────────────────────────────────────────────
const nama = `Tepung Uji B9 ${Math.random().toString(36).slice(2, 7)}`;
const dibuat = await req(T, 'POST', '/api/ingredients', bahanBaru(nama, { currentStock: 9 }));
catat('pemilik menambah bahan (201)', dibuat.status === 201 && dibuat.json?.id > 0,
  `http=${dibuat.status} ${pesan(dibuat)}`);
const id = dibuat.json?.id;

// ── 4. Pagar peran ──────────────────────────────────────────────────────────
if (pegawai.token) {
  const p = await req(pegawai.token, 'POST', '/api/ingredients', bahanBaru('Bahan Pegawai'));
  catat('Pegawai menambah bahan ditolak 403', p.status === 403, `http=${p.status}`);
  const u = await req(pegawai.token, 'PUT', `/api/ingredients/${id}`, bahanBaru(nama, { pricePerUnit: 1 }));
  catat('Pegawai mengubah bahan ditolak 403', u.status === 403, `http=${u.status}`);
  const d = await req(pegawai.token, 'DELETE', `/api/ingredients/${id}`);
  catat('Pegawai menonaktifkan bahan ditolak 403', d.status === 403, `http=${d.status}`);
}
if (dapur.token) {
  const p = await req(dapur.token, 'POST', '/api/ingredients', bahanBaru('Bahan Dapur'));
  catat('Dapur menambah bahan ditolak 403', p.status === 403, `http=${p.status}`);
}

// ── 5. Validasi masukan: 400 dengan kode dan kolom yang jelas ────────────────
const kasus = [
  ['nama kosong', { name: '   ' }, 'Nama'],
  ['satuan kosong', { unit: '  ' }, 'Satuan'],
  ['harga negatif', { pricePerUnit: -1 }, 'Harga'],
  ['stok negatif', { currentStock: -2 }, 'Stok'],
  ['minimum stok negatif', { minStockAlert: -1 }, 'minimum'],
  ['satuan terlalu panjang', { unit: 'x'.repeat(25) }, 'Satuan'],
  ['nama terlalu panjang', { name: 'n'.repeat(130) }, 'Nama'],
];
for (const [label, ubah, potongan] of kasus) {
  const r = await req(T, 'POST', '/api/ingredients', bahanBaru(`Kasus ${label}`, ubah));
  catat(`${label} ditolak 400 (bukan 500)`, r.status === 400 && r.json?.errorCode === 'VALIDATION_FAILED'
    && String(r.json?.errorMessage || '').includes(potongan),
    `http=${r.status} kode=${r.json?.errorCode} pesan=${pesan(r)}`);
}

// ── 6. Nama kembar: 409, tidak menambah baris ────────────────────────────────
const kembar = await req(T, 'POST', '/api/ingredients', bahanBaru(`  ${nama.toUpperCase()}  `, { unit: 'gram' }));
catat('nama kembar (beda huruf/spasi) ditolak 409', kembar.status === 409 && kembar.json?.errorCode === 'DUPLICATE_KEY',
  `http=${kembar.status} kode=${kembar.json?.errorCode}`);
const semua = await daftarBahan(T);
catat('penolakan tidak menambah baris kedua',
  semua.items.filter((b) => b.name.toLowerCase() === nama.toLowerCase()).length === 1,
  `jumlah=${semua.items.filter((b) => b.name.toLowerCase() === nama.toLowerCase()).length}`);

// ── 7. Ubah tanpa currentStock tidak menghapus stok ─────────────────────────
const ubah = await req(T, 'PUT', `/api/ingredients/${id}`,
  { name: nama, unit: 'kg', pricePerUnit: 1_400_000, minStockAlert: 3 });
catat('ubah harga tanpa currentStock diterima', ubah.status === 200, `http=${ubah.status} ${pesan(ubah)}`);
const sesudah = (await daftarBahan(T)).items.find((b) => b.id === id);
catat('stok tetap 9 setelah ubah harga', sesudah?.currentStock === 9,
  `stok=${sesudah?.currentStock} harga=${sesudah?.pricePerUnit}`);
catat('harga tersimpan dalam unit minor', sesudah?.pricePerUnit === 1_400_000, `harga=${sesudah?.pricePerUnit}`);
catat('batas minimum tersimpan', sesudah?.minStockAlert === 3, `min=${sesudah?.minStockAlert}`);

const takAda = await req(T, 'PUT', '/api/ingredients/999999', bahanBaru('Tidak Ada'));
catat('ubah Id tak dikenal menjawab 404', takAda.status === 404, `http=${takAda.status}`);

// ── 8. Jejak audit memuat nilai lama dan baru ───────────────────────────────
const audit = await req(T, 'GET', `/api/audit?entity=Ingredient&entityId=${id}&limit=20`);
const entri = (Array.isArray(audit.json) ? audit.json : [])
  .find((a) => a.action === 'ubah bahan baku');
catat('ubah bahan tercatat di jejak audit', !!entri, `http=${audit.status} n=${(audit.json || []).length}`);
catat('jejak audit memuat harga lama & baru',
  !!entri && String(entri.oldValues || '').includes('1200000') && String(entri.newValues || '').includes('1400000'),
  `lama=${entri?.oldValues} baru=${entri?.newValues}`);

// ── 9. Teks berbahaya disimpan sebagai data, bukan dijalankan ───────────────
const jahat = `<script>alert(1)</script>'; DROP TABLE Ingredients;--`;
const injeksi = await req(T, 'POST', '/api/ingredients', bahanBaru(jahat));
catat('nama berisi HTML/SQL tersimpan sebagai teks biasa', injeksi.status === 201, `http=${injeksi.status} ${pesan(injeksi)}`);
const cek = (await daftarBahan(T)).items.find((b) => b.name === jahat);
catat('isi tersimpan persis apa adanya (parameter, bukan rangkaian SQL)', !!cek, `ditemukan=${!!cek}`);
try {
  const db = new DatabaseSync(DB_PATH, { readOnly: true });
  const n = db.prepare('SELECT COUNT(*) AS c FROM Ingredients').get().c;
  db.close();
  catat('tabel Ingredients masih utuh setelah nama berisi SQL', Number(n) >= 2, `baris=${n}`);
} catch (e) {
  catat('tabel Ingredients masih utuh setelah nama berisi SQL', false, String(e.message).slice(0, 120));
}

const gagal = hasil.filter((h) => !h.ok).length;
console.log(`\nTOTAL ${hasil.length} pemeriksaan, ${gagal} gagal`);
process.exit(gagal === 0 ? 0 : 1);
