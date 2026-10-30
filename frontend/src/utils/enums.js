// Enum di sisi server diserialisasi sebagai angka, jadi label harus dipetakan di sini.
// Nilai yang sudah berupa teks (mis. status pesanan di dashboard) ikut diterima.

const ORDER_STATUS = {
  0: 'Menunggu', 1: 'Diproses', 2: 'Siap Diambil', 3: 'Lunas', 4: 'Dibatalkan',
  Pending: 'Menunggu', Processing: 'Diproses', Ready: 'Siap Diambil', Paid: 'Lunas', Cancelled: 'Dibatalkan',
};

const PURCHASE_STATUS = {
  0: 'Draf', 1: 'Terkirim', 2: 'Diterima Sebagian', 3: 'Diterima', 4: 'Dibatalkan',
  Draft: 'Draf', Sent: 'Terkirim', PartialReceived: 'Diterima Sebagian', Received: 'Diterima', Cancelled: 'Dibatalkan',
};

const ACCOUNT_TYPE = {
  0: 'Aset', 1: 'Kewajiban', 2: 'Modal', 3: 'Pendapatan', 4: 'Beban',
  Asset: 'Aset', Liability: 'Kewajiban', Equity: 'Modal', Revenue: 'Pendapatan', Expense: 'Beban',
};

export const orderStatusLabel = (v) => ORDER_STATUS[v] ?? `Status ${v}`;
export const purchaseStatusLabel = (v) => PURCHASE_STATUS[v] ?? `Status ${v}`;
export const accountTypeLabel = (v) => ACCOUNT_TYPE[v] ?? `Tipe ${v}`;

// Peran dikirim sebagai angka (urutan enum UserRole), sebagian jalur lama mengirim teks.
// Dinormalisasi ke nama supaya layar hak akses bisa menampilkan dan mengirim ulang perannya.
const USER_ROLE_NAMA = { 0: 'Owner', 1: 'Admin', 2: 'Dapur', 3: 'Pegawai' };
export const roleName = (v) => (typeof v === 'number' ? USER_ROLE_NAMA[v] : v) ?? '';

const ORDER_STATUS_NAMA = { 0: 'Pending', 1: 'Processing', 2: 'Ready', 3: 'Paid', 4: 'Cancelled' };

const PURCHASE_STATUS_NAMA = {
  0: 'Draft', 1: 'Sent', 2: 'PartialReceived', 3: 'Received', 4: 'Cancelled',
};

// Server mengirim enum sebagai angka; sebagian jalur lama mengirim teks.
// Dinormalisasi ke nama enum server supaya perbandingan tidak bergantung bentuk datanya.
export const orderStatusName = (v) =>
  (typeof v === 'number' ? ORDER_STATUS_NAMA[v] : v) ?? '';

export const purchaseStatusName = (v) =>
  (typeof v === 'number' ? PURCHASE_STATUS_NAMA[v] : v) ?? '';

const MOVEMENT_TYPE = {
  Deduction: 'Pemakaian', Restock: 'Penerimaan', Adjustment: 'Penyesuaian', CancelRestore: 'Pengembalian',
  Transfer: 'Transfer antarlokasi',
};
export const movementTypeLabel = (v) => MOVEMENT_TYPE[v] ?? String(v ?? '-');
