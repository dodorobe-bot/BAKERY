const API_BASE = '';

// Parameter kosong tidak dikirim: server memakai nilai bawaannya sendiri.
const denganParam = (jalur, params) => {
  const isi = Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== '');
  if (isi.length === 0) return jalur;
  const query = isi.map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`).join('&');
  return `${jalur}?${query}`;
};

// Pengukuran waktu permintaan untuk diagnosis kinerja (mis. perpindahan menu). Riwayat dibatasi
// 300 entri supaya tidak tumbuh selama aplikasi dipakai berhari-hari, dan hanya memuat jalur,
// durasi, serta kode status — bukan isi data.
const riwayatPerf = [];
export const perf = { riwayat: riwayatPerf, tertunda: 0 };
if (typeof window !== 'undefined') window.__s22Perf = perf;

const idPermintaan = () =>
  (typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`);

// Tanpa batas waktu, satu permintaan yang macet membuat layar berputar selamanya dan tombol
// nondaktif — pengguna tidak tahu apakah datanya tersimpan. Batas normal cukup longgar untuk
// query terberat pada data besar; cadangan dan unduhan ekspor diberi batas jauh lebih lama
// karena memang menyalin seluruh basis data atau membuat berkas besar.
const BATAS_NORMAL_MS = 15000;
const BATAS_LAMA_MS = 120000;

// Sinyal milik pemanggil (mis. batal saat layar ditinggal) diteruskan ke pengendali lokal,
// sehingga fetch ikut berhenti tanpa memutus kaitan pengendali dengan pewaktu batas.
const sambungSinyal = (sinyalLuar, pengendali) => {
  if (!sinyalLuar) return () => {};
  if (sinyalLuar.aborted) {
    pengendali.abort(sinyalLuar.reason);
    return () => {};
  }
  const teruskan = () => pengendali.abort(sinyalLuar.reason);
  sinyalLuar.addEventListener('abort', teruskan, { once: true });
  return () => sinyalLuar.removeEventListener('abort', teruskan);
};

// Server mengirim "app;dur=12.3" pada header Server-Timing; angkanya dipisah supaya lama
// permintaan di server bisa dibedakan dari lama di peramban.
const durasiServer = (nilai) => {
  if (!nilai) return null;
  const cocok = /dur=([\d.]+)/.exec(nilai);
  return cocok ? Number(cocok[1]) : null;
};

export const api = {
  getToken: () => localStorage.getItem('s22_token'),
  getUser: () => {
    try {
      const user = JSON.parse(localStorage.getItem('s22_user') || 'null');
      // Sesi yang tersimpan sebelum server ikut mengirim daftar kapabilitas tidak bisa dipakai
      // menyaring menu. Lebih baik diminta masuk ulang daripada menampilkan tombol yang pasti
      // ditolak 403, karena penolakan itu akan terbaca sebagai kegagalan menyimpan data.
      if (user && !Array.isArray(user.capabilities)) {
        api.clearAuth();
        return null;
      }
      return user;
    } catch {
      return null;
    }
  },
  setAuth: (token, user) => {
    localStorage.setItem('s22_token', token);
    localStorage.setItem('s22_user', JSON.stringify(user));
  },
  clearAuth: () => {
    localStorage.removeItem('s22_token');
    localStorage.removeItem('s22_user');
  },

  async request(endpoint, options = {}) {
    const token = this.getToken();
    const headers = {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(options.headers || {})
    };

    // Kunci dikirim sebagai header supaya gerbang sisi server bisa mengenalinya untuk SEMUA
    // aksi perubahan, termasuk tombol tanpa formulir (batalkan, terima, hapus). Layar boleh
    // menuliskannya sebagai `options.idempotensi` atau property `idempotensi` pada payload; di
    // sini angkanya dipindah ke header lalu dibuang dari badan, jadi DTO server tidak ikut berubah.
    let body = options.body;
    let kunci = typeof options.idempotensi === 'string' && options.idempotensi ? options.idempotensi : null;
    if (typeof body === 'string' && body.startsWith('{')) {
      try {
        const isian = JSON.parse(body);
        if (isian && typeof isian.idempotensi === 'string' && isian.idempotensi.length > 0) {
          kunci = isian.idempotensi;
          delete isian.idempotensi;
          body = JSON.stringify(isian);
        }
      } catch {
        // Badan permintaan bukan JSON yang berasal dari gerbang ini; biarkan apa adanya.
      }
    }
    if (kunci) headers['Idempotency-Key'] = kunci;
    // Kunci tidak boleh ikut masuk ke RequestInit fetch. Sinyal pemanggil juga dipegang di sini,
    // bukan diteruskan apa adanya, supaya batas waktu bisa dipasang per percobaan.
    const { idempotensi: _kunciLokal, signal: sinyalPemanggil, timeoutMs, ...sisaOptions } = options;
    const batasMs = Number.isFinite(timeoutMs) && timeoutMs > 0 ? timeoutMs : BATAS_NORMAL_MS;

    // Satu id untuk satu permintaan: nilainya muncul di log server bila lambat, sehingga satu
    // permintaan bisa ditelusuri dari peramban ke server tanpa menebak urutannya.
    const idPerf = idPermintaan();
    headers['X-Request-Id'] = idPerf;
    const metode = (sisaOptions.method || 'GET').toUpperCase();
    const mulaiPerf = performance.now();
    perf.tertunda += 1;
    let sudahDicatat = false;
    const catat = (status, durasiPeladen = null) => {
      if (sudahDicatat) return;
      sudahDicatat = true;
      perf.tertunda -= 1;
      riwayatPerf.push({
        jalur: endpoint,
        metode,
        status,
        mulai: Math.round(mulaiPerf),
        ms: Math.round(performance.now() - mulaiPerf),
        durasiPeladen,
        id: idPerf,
      });
      if (riwayatPerf.length > 300) riwayatPerf.shift();
    };
    // Hanya permintaan baca yang diulang otomatis. Koneksi ke layanan lokal bisa terputus
    // sekejap — komputer baru bangun dari tidur, layanan baru mulai, atau soket keep-alive
    // yang sudah ditutup server dipakai lagi. Peramban melaporkannya sebagai
    // "NetworkError when attempting to fetch resource" tanpa kode status; mengulang sekali
    // atau dua kali menghapus gangguan sekejap itu. Permintaan yang mengubah data tidak
    // diulang di sini: pengulangan yang tidak diminta bisa mencatat transaksi dua kali.
    const batasPercobaan = metode === 'GET' || metode === 'HEAD' ? 3 : 1;
    let res = null;
    let galatJaringan = null;
    let lewatBatas = false;
    for (let percobaan = 1; percobaan <= batasPercobaan; percobaan += 1) {
      // Satu pengendali dan satu pewaktu per percobaan: percobaan ulang mendapat jatah waktu
      // penuh, bukan sisa waktu percobaan sebelumnya.
      const pengendali = new AbortController();
      const lepasSinyal = sambungSinyal(sinyalPemanggil, pengendali);
      const pewaktu = setTimeout(() => {
        lewatBatas = true;
        pengendali.abort();
      }, batasMs);
      try {
        res = await fetch(`${API_BASE}${endpoint}`, {
          ...sisaOptions,
          body,
          headers,
          signal: pengendali.signal
        });
        galatJaringan = null;
        break;
      } catch (err) {
        galatJaringan = err;
        // Batas waktu bukan gangguan sekejap: mengulang hanya menambah lama menunggu, dan untuk
        // perintah yang mengubah data pengulangan itu berbahaya. Batal dari pemanggil juga tidak
        // boleh diulang.
        if (lewatBatas || sinyalPemanggil?.aborted) break;
        if (percobaan < batasPercobaan) {
          await new Promise((selesai) => setTimeout(selesai, 200 * percobaan));
        }
      } finally {
        clearTimeout(pewaktu);
        lepasSinyal();
      }
    }
    if (!res) {
      catat(0);
      const detik = Math.round(batasMs / 1000);
      if (sinyalPemanggil?.aborted) {
        const batal = new Error('Permintaan dibatalkan.');
        batal.name = 'AbortError';
        throw batal;
      }
      if (lewatBatas) {
        console.error(`Permintaan ${endpoint} melewati batas ${detik} detik.`);
        throw new Error(
          `Server tidak menjawab dalam ${detik} detik. Aplikasi mungkin sedang sibuk; coba lagi, ` +
          'dan bila berulang tutup lalu buka kembali aplikasi.'
        );
      }
      console.error(`Permintaan ${endpoint} gagal di jaringan:`, galatJaringan);
      throw new Error('Server lokal tidak menjawab. Pastikan aplikasi masih berjalan, lalu coba lagi.');
    }

    const durasiPeladen = durasiServer(res.headers.get('Server-Timing'));
    try {
      // Login dan logout boleh dijawab 401 tanpa memuat ulang halaman: pada login 401 berarti
      // sandi salah (pesannya ditampilkan formulir), dan pada logout token yang sudah mati justru
      // tujuan akhirnya — memuat ulang di tengah logout akan menampilkan pesan galat yang salah.
      if (res.status === 401 && !endpoint.startsWith('/api/auth/')) {
        this.clearAuth();
        window.location.reload();
        throw new Error('Sesi berakhir, silakan login kembali.');
      }

      const data = await res.json().catch(() => null);

      if (!res.ok) {
        // Sebagian endpoint mengirim { error }, sebagian lain { errorMessage }.
        const msg = data?.errorMessage || data?.ErrorMessage || data?.error || data?.Error || `Error ${res.status}`;
        throw new Error(msg);
      }

      // Daftar berhalaman tetap menjawab array; jumlah seluruh baris datang lewat header supaya
      // layar bisa berkata "menampilkan N dari M" dan tahu kapan tombol "Muat lagi" berhenti.
      if (options.withTotal) {
        const total = Number(res.headers.get('X-Total-Count'));
        return { items: Array.isArray(data) ? data : [], total: Number.isFinite(total) ? total : null };
      }

      return data;
    } finally {
      // Dicatat sekali di setiap jalan keluar, termasuk galat dan 401: permintaan yang gagal
      // tetap harus muncul di riwayat agar jumlahnya bisa dibandingkan dengan jumlah klik.
      catat(res.status, durasiPeladen);
    }
  },

  login: (username, password) =>
    api.request('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password })
    }),

  changePassword: (currentPassword, newPassword) =>
    api.request('/api/auth/change-password', {
      method: 'POST',
      body: JSON.stringify({ currentPassword, newPassword })
    }),

  // Menaikkan versi token pengguna di server, sehingga token yang tersimpan di perangkat ini
  // (dan salinan mana pun) langsung ditolak. Daftar data lokal dibersihkan sesudahnya.
  logout: () => api.request('/api/auth/logout', { method: 'POST' }),

  getDashboard: () => api.request('/api/dashboard'),
  // Daftar pesanan berhalaman: halaman pertama saja yang dimuat, jumlah seluruh baris datang
  // lewat header untuk tombol "Muat lagi". Tanpa ini layar kehilangan pesanan ke-101 dan
  // seterusnya tanpa memberi tahu pengguna.
  getOrders: (opsi = {}) =>
    api.request(denganParam('/api/orders', { limit: opsi.limit, offset: opsi.offset }), {
      withTotal: true,
      signal: opsi.signal
    }),
  getOrder: (id) => api.request(`/api/orders/${id}`),
  createOrder: (payload) =>
    api.request('/api/orders', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  addPayment: (orderId, payload) =>
    api.request(`/api/orders/${orderId}/payment`, {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  updateOrderStatus: (orderId, status, cancelReason = null, kunci = null) =>
    api.request(`/api/orders/${orderId}/status`, {
      method: 'PUT',
      idempotensi: kunci,
      body: JSON.stringify({ status, cancelReason })
    }),
  getCustomers: (opsi = {}) =>
    api.request(denganParam('/api/customers', { search: opsi.search, limit: opsi.limit, offset: opsi.offset }),
      { withTotal: true, signal: opsi.signal }),
  getIngredients: () => api.request('/api/ingredients'),
  createIngredient: (payload) =>
    api.request('/api/ingredients', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  updateIngredient: (id, payload) =>
    api.request(`/api/ingredients/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload)
    }),
  deleteIngredient: (id) =>
    api.request(`/api/ingredients/${id}`, { method: 'DELETE' }),
  getProducts: () => api.request('/api/products'),
  getProduct: (id) => api.request(`/api/products/${id}`),
  createProduct: (payload) =>
    api.request('/api/products', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  updateProduct: (id, payload) =>
    api.request(`/api/products/${id}`, {
      method: 'PUT',
      body: JSON.stringify(payload)
    }),
  deleteProduct: (id) =>
    api.request(`/api/products/${id}`, { method: 'DELETE' }),
  getJournal: (from = null, to = null, opsi = {}) =>
    api.request(denganParam('/api/journal', {
      from, to, limit: opsi.limit, beforeDate: opsi.beforeDate, beforeId: opsi.beforeId
    })),
  // Ringkasan seluruh rentang dihitung server: laporan tetap tepat walau entri hanya dimuat sebagian.
  getJournalSummary: (from, to, source = '') =>
    api.request(denganParam('/api/journal/summary', { from, to, source })),
  getJournalBalances: (asOf = '') => api.request(denganParam('/api/journal/balances', { asOf })),
  recordExpense: (payload) =>
    api.request('/api/expenses', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  // Merek toko harus bisa dibaca sebelum masuk, jadi endpoint ini tidak menuntut token dan
  // hanya mengembalikan nama, slogan, serta logo.
  getBrand: () => api.request('/api/brand'),
  getSettings: () => api.request('/api/settings'),
  saveSettings: (values) =>
    api.request('/api/settings', { method: 'PUT', body: JSON.stringify(values) }),
  getSelfCheck: () => api.request('/api/system/self-check'),

  // Cadangan & penyimpanan. Pemulihan tidak lewat layar ini: basis data sedang dibuka host,
  // jadi pemulihan dilakukan saat aplikasi tertutup (Seruni22Bakery.exe --restore).
  getBackups: () => api.request('/api/backup'),
  // Membuat cadangan menyalin seluruh basis data; pada data besar ini bisa lewat dari batas
  // normal, jadi diberi batas panjang yang sama dengan unduhan.
  createBackup: () => api.request('/api/backup', { method: 'POST', timeoutMs: BATAS_LAMA_MS }),
  copyBackup: (tujuan) =>
    api.request('/api/backup/copy', {
      method: 'POST',
      body: JSON.stringify({ tujuan }),
      timeoutMs: BATAS_LAMA_MS
    }),
  getStorage: () => api.request('/api/system/storage'),

  // Perangkat kasir: printer Windows dan antrean cetak.
  getDevices: () => api.request('/api/devices'),
  getDeviceStatus: () => api.request('/api/devices/status'),
  saveDeviceSettings: (values) =>
    api.request('/api/devices/settings', { method: 'PUT', body: JSON.stringify(values) }),
  testPrint: () => api.request('/api/devices/test-print', { method: 'POST' }),
  getPrintJobs: (limit = 50) => api.request(`/api/devices/print-jobs?limit=${limit}`),
  printReceipt: (orderId, { reprint = false, idempotensi = null } = {}) =>
    api.request(`/api/orders/${orderId}/receipt`, {
      method: 'POST',
      idempotensi,
      body: JSON.stringify({ cetakUlang: reprint })
    }),

  // Kotak tugas, analisis, jejak audit, dan notifikasi internal
  getTasks: () => api.request('/api/tasks'),
  getNotifications: (unreadOnly = false, limit = 100) =>
    api.request(`/api/notifications?unreadOnly=${unreadOnly ? 'true' : 'false'}&limit=${limit}`),
  getNotificationSummary: () => api.request('/api/notifications/summary'),
  markNotificationRead: (id) =>
    api.request(`/api/notifications/${id}/read`, { method: 'POST' }),
  markAllNotificationsRead: () =>
    api.request('/api/notifications/read-all', { method: 'POST' }),
  syncNotifications: () => api.request('/api/notifications/sync', { method: 'POST' }),
  getAnalytics: (days = 90) => api.request(`/api/analytics/sales?days=${days}`),
  getAudit: ({ entity = null, entityId = null, userId = null, limit = 100 } = {}) => {
    const q = new URLSearchParams();
    if (entity) q.set('entity', entity);
    if (entityId) q.set('entityId', entityId);
    if (userId) q.set('userId', userId);
    q.set('limit', String(limit));
    const teks = q.toString();
    return api.request(`/api/audit${teks ? `?${teks}` : ''}`);
  },

  // Hak akses & pegawai
  getUsers: () => api.request('/api/users'),
  createUser: (payload) =>
    api.request('/api/users', { method: 'POST', body: JSON.stringify(payload) }),
  updateUser: (id, payload) =>
    api.request(`/api/users/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),

  // Production (SPK) & Scrap
  getWorkOrders: () => api.request('/api/production/orders'),
  getWorkOrder: (id) => api.request(`/api/production/orders/${id}`),
  createWorkOrder: (payload) =>
    api.request('/api/production/orders', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  completeWorkOrder: (id, payload) =>
    api.request(`/api/production/orders/${id}/complete`, {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  cancelWorkOrder: (id, reason) =>
    api.request(`/api/production/orders/${id}/cancel`, {
      method: 'PUT',
      // Kunci tetap untuk transisi sekali-jadi: klik ganda tidak boleh mengerjakan pembatalan
      // dua kali, sementara percobaan yang benar-benar gagal (4xx) tetap boleh diulang karena
      // sisi server melepas kunci itu. Aksi yang bisa terjadi berulang kali (edit, hapus)
      // justru TIDAK memakai kunci tetap.
      idempotensi: `batal-spk-${id}`,
      body: JSON.stringify({ reason: reason || null })
    }),

  // Pemeriksaan mutu: hasil disimpan per SPK, keputusan dihitung server.
  getQualityChecks: () => api.request('/api/production/quality-checks'),
  createQualityCheck: (payload) =>
    api.request('/api/production/quality-checks', {
      method: 'POST',
      idempotensi: payload.idempotensi,
      body: JSON.stringify(payload)
    }),
  getCostSummary: () => api.request('/api/production/cost-summary'),

  // Retur penjualan: hanya pesanan lunas; nilai dibatasi sisa pesanan.
  getReturns: () => api.request('/api/returns'),
  createReturn: (payload) =>
    api.request('/api/returns', {
      method: 'POST',
      idempotensi: payload.idempotensi,
      body: JSON.stringify(payload)
    }),

  // Multi-Location Inventory & Stock Transfer
  getLocations: () => api.request('/api/inventory/locations'),
  getLocationStock: (locationId) => api.request(`/api/inventory/locations/${locationId}/stock`),
  getInventoryMovements: (locationId = null, limit = 100) =>
    api.request(`/api/inventory/movements?limit=${limit}${locationId ? `&locationId=${locationId}` : ''}`),
  getFinishedGoods: () => api.request('/api/inventory/finished-goods'),
  getInventoryBatches: (status = null) =>
    api.request(`/api/inventory/batches${status ? `?status=${status}` : ''}`),
  createInventoryBatch: (payload) =>
    api.request('/api/inventory/batches', {
      method: 'POST',
      // Aksi ini boleh diulang setelah diperbaiki, jadi kunci dibuat per percobaan lewat
      // pemanggil, bukan kunci tetap satu nilai.
      idempotensi: payload.idempotensi,
      body: JSON.stringify(payload),
    }),
  transferStock: (payload) =>
    api.request('/api/inventory/transfer', {
      method: 'POST',
      body: JSON.stringify(payload)
    }),
  adjustStock: (payload) =>
    api.request('/api/inventory/adjust', {
      method: 'POST',
      idempotensi: payload.idempotensi,
      body: JSON.stringify(payload)
    }),

  // Suppliers
  getSuppliers: () => api.request('/api/suppliers'),
  createSupplier: (payload) => api.request('/api/suppliers', { method: 'POST', body: JSON.stringify(payload) }),
  updateSupplier: (id, payload) => api.request(`/api/suppliers/${id}`, { method: 'PUT', body: JSON.stringify(payload) }),

  // Purchase Orders
  getPurchases: (opsi = {}) =>
    api.request(denganParam('/api/purchases', { status: opsi.status, limit: opsi.limit, offset: opsi.offset }),
      { withTotal: true }),
  createPurchase: (payload) => api.request('/api/purchases', { method: 'POST', body: JSON.stringify(payload) }),
  getPurchase: (id) => api.request(`/api/purchases/${id}`),
  receivePurchase: (id, payload) => api.request(`/api/purchases/${id}/receive`, { method: 'POST', body: JSON.stringify(payload) }),
  cancelPurchase: (id) => api.request(`/api/purchases/${id}/cancel`, {
    method: 'PUT',
    idempotensi: `batal-po-${id}`
  }),

  // ── Export PDF/Excel ──────────────────────────────────────────────────
  downloadFile: async (endpoint, filename) => {
    const token = api.getToken();
    // URL relatif: aplikasi disajikan dari origin yang sama, jadi alamat port tidak boleh ditulis tetap.
    // Unduhan tidak mengubah data, jadi gangguan jaringan sekejap boleh dicoba ulang. Batas
    // waktunya panjang karena ekspor besar memang lama, tetapi tetap ada supaya layar tidak
    // menunggu tanpa akhir ketika server berhenti menjawab.
    let res = null;
    let galatJaringan = null;
    let lewatBatas = false;
    for (let percobaan = 1; percobaan <= 3; percobaan += 1) {
      const pengendali = new AbortController();
      const pewaktu = setTimeout(() => {
        lewatBatas = true;
        pengendali.abort();
      }, BATAS_LAMA_MS);
      try {
        res = await fetch(`${API_BASE}${endpoint}`, {
          headers: token ? { 'Authorization': `Bearer ${token}` } : {},
          signal: pengendali.signal
        });
        galatJaringan = null;
        break;
      } catch (err) {
        galatJaringan = err;
        if (lewatBatas) break;
        if (percobaan < 3) await new Promise((selesai) => setTimeout(selesai, 200 * percobaan));
      } finally {
        clearTimeout(pewaktu);
      }
    }
    if (!res) {
      if (lewatBatas) {
        throw new Error(
          `Server tidak menjawab dalam ${Math.round(BATAS_LAMA_MS / 1000)} detik. ` +
          'Coba lagi; bila berulang, tutup lalu buka kembali aplikasi.'
        );
      }
      console.error(`Unduhan ${endpoint} gagal di jaringan:`, galatJaringan);
      throw new Error('Server lokal tidak menjawab. Pastikan aplikasi masih berjalan, lalu coba lagi.');
    }
    if (!res.ok) throw new Error(`Export gagal (${res.status})`);
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  },
  exportJournal: (from, to, format = 'excel') => api.downloadFile(`/api/export/journal?from=${from}&to=${to}&format=${format}`, `jurnal-${from}-${to}.${format === 'pdf' ? 'pdf' : 'xlsx'}`),
  exportOrders: (from, to, format = 'excel') => api.downloadFile(`/api/export/orders?from=${from}&to=${to}&format=${format}`, `penjualan-${from}-${to}.${format === 'pdf' ? 'pdf' : 'xlsx'}`),
  exportStock: (format = 'excel') => api.downloadFile(`/api/export/stock?format=${format}`, `stok.${format === 'pdf' ? 'pdf' : 'xlsx'}`),
  exportPurchases: (format = 'excel') => api.downloadFile(`/api/export/purchases?format=${format}`, `pembelian.${format === 'pdf' ? 'pdf' : 'xlsx'}`)
};
