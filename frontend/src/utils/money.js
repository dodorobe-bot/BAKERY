// Semua nominal di basis data dan API disimpan dalam unit minor (Rp x 100).
// Layar harus membagi 100 saat membaca dan mengali 100 saat mengirim.
//
// Pembentuk angka dipakai per baris per sel pada tabel ratusan baris; membuat Intl.NumberFormat
// di setiap panggilan memakan puluhan milidetik per layar, jadi dibentuk sekali di tingkat modul.
const PEMBENTUK_RUPIAH = new Intl.NumberFormat('id-ID', {
  style: 'currency', currency: 'IDR', maximumFractionDigits: 0,
});
const PEMBENTUK_QTY = new Intl.NumberFormat('id-ID', { maximumFractionDigits: 2 });

export const formatMinor = (value) =>
  PEMBENTUK_RUPIAH.format(Math.round((Number(value) || 0) / 100));

export const toMinor = (value) => Math.round((Number(value) || 0) * 100);

// Balikan toMinor: dipakai untuk mengisi kolom rupiah dari nilai API, misalnya sisa pembayaran.
export const fromMinor = (value) => Math.round((Number(value) || 0) / 100);

export const formatQty = (value) => PEMBENTUK_QTY.format(Number(value) || 0);

// Tanggal bisnis (tanggal ambil, tanggal jurnal) sudah ditulis dalam kalender toko,
// jadi ditampilkan apa adanya tanpa konversi zona.
export const formatDate = (value) => {
  if (!value) return '-';
  return String(value).replace('T', ' ').slice(0, 10);
};

// Cap waktu kejadian (mutasi stok, jejak audit, retur) disimpan sebagai UTC dan harus
// ditampilkan dalam satu zona: zona operasi toko. Perangkat kasir bisa saja disetel ke
// zona lain, jadi zona perangkat tidak dipakai.
const ZONA_TOKO = 'Asia/Jakarta';
const BAGIAN_WAKTU_TOKO = new Intl.DateTimeFormat('en-CA', {
  timeZone: ZONA_TOKO,
  year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
});

const keInstan = (value) => {
  const teks = String(value ?? '').trim();
  if (!teks) return null;
  // Tanpa penanda zona berarti kolom basis data menyimpan UTC (mis. datetime('now')).
  const iso = /[zZ]$|[+-]\d{2}:?\d{2}$/.test(teks) ? teks : `${teks.replace(' ', 'T')}Z`;
  const waktu = new Date(iso);
  return Number.isNaN(waktu.getTime()) ? null : waktu;
};

const bagianWaktu = (value) => {
  const waktu = keInstan(value);
  if (!waktu) return null;
  const bagian = {};
  for (const { type, value: isi } of BAGIAN_WAKTU_TOKO.formatToParts(waktu)) bagian[type] = isi;
  return bagian;
};

export const formatDateTime = (value) => {
  const b = bagianWaktu(value);
  return b ? `${b.year}-${b.month}-${b.day} ${b.hour}:${b.minute}` : '-';
};

// Tanggal kalender toko dari sebuah cap waktu; beda dengan formatDate yang menerima tanggal bisnis.
export const formatInstantDate = (value) => {
  const b = bagianWaktu(value);
  return b ? `${b.year}-${b.month}-${b.day}` : '-';
};
