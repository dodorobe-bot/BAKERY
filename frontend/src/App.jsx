import React, { useState, useEffect, useRef } from 'react';
import { 
  Sparkles, 
  Clock, 
  CheckCircle, 
  RefreshCw,
  Warehouse,
  ShoppingBag,
  ChefHat,
  Store,
  Wallet
} from 'lucide-react';
import { api } from './services/api';
import { formatMinor, formatDate } from './utils/money';
import { orderStatusLabel, roleName } from './utils/enums';

import RecipesBomView from './views/RecipesBomView';
import PurchasesView from './views/PurchasesView';
import Sidebar from './components/Sidebar';
import BrandMark from './components/BrandMark';
import ProductionView from './views/ProductionView';
import InventoryView from './views/InventoryView';
import SalesView from './views/SalesView';
import FinanceView from './views/FinanceView';
import SystemView from './views/SystemView';
import WorkspaceView from './views/WorkspaceView';
import AnalyticsView from './views/AnalyticsView';
import OpnameView from './views/OpnameView';
import BatchesView from './views/BatchesView';
import QualityView from './views/QualityView';
import CostView from './views/CostView';
import ReturnsView from './views/ReturnsView';
import DeviceView from './views/DeviceView';
import { bolehAksi } from './utils/kapabilitas';
import { DEFAULT_BRAND, brandFromSettings } from './utils/brand';

// Layar pertama mengikuti kapabilitas: kasir mulai dari layar kasir dan dapur dari SPK,
// bukan dasbor keuangan yang memang ditolak server untuk mereka.
function tabAwal(user) {
  const caps = user?.capabilities || [];
  if (caps.includes('finance-read')) return 'dashboard';
  if (caps.includes('sales')) return 'pos';
  if (caps.includes('production-write')) return 'production_orders';
  return 'tasks';
}

// Kerangka ringan selama data tab yang baru dibuka belum tiba. Tanpa ini layar tujuan sempat
// dirender penuh memakai data tab sebelumnya, lalu dirender ulang dengan data yang benar.
function LayarMemuat() {
  return (
    <div className="flex items-center justify-center gap-3 h-64 text-slate-500 text-sm">
      <span className="w-4 h-4 border-2 border-slate-700 border-t-amber-400 rounded-full animate-spin" />
      Memuat layar…
    </div>
  );
}

export default function App() {
  const [user, setUser] = useState(api.getUser());
  const [activeTab, setActiveTab] = useState(() => tabAwal(api.getUser()));
  
  // Auth Form State
  // Sandi pemilik tidak boleh ikut terkirim di dalam bundel: form selalu kosong.
  const [loginForm, setLoginForm] = useState({ username: '', password: '' });
  const [loginLoading, setLoginLoading] = useState(false);
  const [loginError, setLoginError] = useState('');

  // Data State
  const [dashboardData, setDashboardData] = useState(null);
  const [orders, setOrders] = useState([]);
  const [customers, setCustomers] = useState([]);
  const [products, setProducts] = useState([]);
  const [workOrders, setWorkOrders] = useState([]);
  const [locations, setLocations] = useState([]);
  const [ingredients, setIngredients] = useState([]);
  const [stockByLocation, setStockByLocation] = useState({});
  const [movements, setMovements] = useState([]);
  const [finishedGoods, setFinishedGoods] = useState([]);
  const [balances, setBalances] = useState([]);
  const [settings, setSettings] = useState({});
  // Merek toko dibaca terpisah dari pengaturan: layar masuk belum punya token, sedangkan
  // /api/brand boleh dibuka tanpa masuk dan hanya memuat nama, slogan, dan logo.
  const [brand, setBrand] = useState(DEFAULT_BRAND);
  const [users, setUsers] = useState([]);
  const [tasks, setTasks] = useState(null);
  const [notifications, setNotifications] = useState(null);
  const [analytics, setAnalytics] = useState(null);
  const [batches, setBatches] = useState([]);
  const [qualityChecks, setQualityChecks] = useState([]);
  const [costSummary, setCostSummary] = useState(null);
  const [returns, setReturns] = useState([]);
  const [deviceSnapshot, setDeviceSnapshot] = useState(null);
  const [printJobs, setPrintJobs] = useState([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState('');
  // Server yang mati tidak bisa dihidupkan oleh tombol "Coba lagi": menekannya hanya memunculkan
  // banner merah yang sama. Selama bendera ini menyala, layar menanyakan /health berkala dan
  // memuat sendiri begitu server menjawab lagi.
  const [serverMati, setServerMati] = useState(false);
  // Tab yang datanya sudah diterapkan; layar tujuan baru dirender sesudah ini sama dengan
  // activeTab, sehingga tabel besar tidak digambar dua kali (sekali dengan data lama).
  const [tabSiap, setTabSiap] = useState(null);

  // Purchases & Suppliers state (reserved for future phases)
  const [purchases, setPurchases] = useState([]);
  const [suppliers, setSuppliers] = useState([]);
  // Jumlah seluruh baris di server; daftar di layar hanya sebagian halaman pertama.
  const [customerTotal, setCustomerTotal] = useState(null);
  const [purchaseTotal, setPurchaseTotal] = useState(null);
  const [orderTotal, setOrderTotal] = useState(null);

  // Change Password Modal State
  const [changePwModal, setChangePwModal] = useState(false);
  const [changePwForm, setChangePwForm] = useState({ current: '', next: '', confirm: '' });
  const [changePwLoading, setChangePwLoading] = useState(false);
  const [changePwError, setChangePwError] = useState('');

  // Nomor urut pemuatan data: hanya jawaban dari permintaan terbaru yang boleh masuk ke layar.
  const urutanMuat = useRef(0);

  // Data layar disimpan sebentar supaya kembali ke menu yang baru saja dibuka tidak perlu
  // menunggu jaringan lagi. Umurnya pendek dan setiap aksi pengubahan data memaksa muat ulang,
  // jadi angka di layar tidak tertinggal lama.
  const cacheLayar = useRef(new Map());

  useEffect(() => {
    if (!user) return;
    if (user.mustChangePassword) {
      // Server menolak semua endpoint data selama sandi masih bawaan, jadi memuat data
      // sekarang hanya menghasilkan galat di belakang layar ganti sandi.
      setChangePwModal(true);
      return;
    }
    loadData(false);
  }, [user, activeTab]);

  // Merek dibaca sekali saat aplikasi dibuka — sebelum maupun sesudah masuk — lalu diperbarui
  // setiap kali Pengaturan disimpan. Judul jendela ikut nama toko supaya bilah tugas Windows
  // tidak lagi menampilkan nama lama setelah pemilik menggantinya.
  useEffect(() => {
    api.getBrand()
      .then((b) => setBrand({ ...DEFAULT_BRAND, ...(b || {}) }))
      .catch(() => {});
  }, []);

  useEffect(() => {
    document.title = brand.name;
  }, [brand.name]);

  // Pengaturan lengkap sengaja tidak dimuat di latar belakang: hanya Owner yang boleh membaca
  // /api/settings. Identitas toko di kepala layar sudah datang dari merek publik (/api/brand).

  const setData = {
    dashboardData: setDashboardData,
    orders: setOrders,
    customers: setCustomers,
    products: setProducts,
    workOrders: setWorkOrders,
    locations: setLocations,
    ingredients: setIngredients,
    stockByLocation: setStockByLocation,
    movements: setMovements,
    finishedGoods: setFinishedGoods,
    balances: setBalances,
    settings: setSettings,
    users: setUsers,
    tasks: setTasks,
    notifications: setNotifications,
    analytics: setAnalytics,
    batches: setBatches,
    qualityChecks: setQualityChecks,
    costSummary: setCostSummary,
    returns: setReturns,
    deviceSnapshot: setDeviceSnapshot,
    printJobs: setPrintJobs,
    purchases: setPurchases,
    suppliers: setSuppliers,
    customerTotal: setCustomerTotal,
    purchaseTotal: setPurchaseTotal,
    orderTotal: setOrderTotal,
  };

  // Stok setiap lokasi diminta per lokasi: satu sumber kebenaran, tanpa menebak ID dapur/gudang.
  const stokPerLokasi = async (locs) => {
    const rows = await Promise.all((locs || []).map(async (loc) => {
      const stock = await api.getLocationStock(loc.id);
      return [loc.id, stock || []];
    }));
    return Object.fromEntries(rows);
  };

  // Daftar yang tidak saling bergantung diambil bersamaan. Sebelumnya permintaan dijalankan satu
  // per satu, sehingga lama menunggu pindah menu adalah jumlah seluruh permintaan layar itu.
  const ambilDataLayar = async (tab) => {
    switch (tab) {
      case 'dashboard':
        return { dashboardData: await api.getDashboard() };

      case 'orders':
      case 'pos':
      case 'preorders':
      case 'receivables': {
        // Daftar pelanggan berhalaman: layar hanya perlu sebagian, sisanya lewat pencarian.
        const [o, c, p] = await Promise.all([
          api.getOrders({ limit: 100 }), api.getCustomers({ limit: 50 }), api.getProducts(),
        ]);
        return {
          orders: o?.items ?? [], orderTotal: o?.total ?? null,
          customers: c?.items ?? [], customerTotal: c?.total ?? null, products: p || [],
        };
      }

      case 'customers': {
        const [c, p] = await Promise.all([api.getCustomers({ limit: 50 }), api.getProducts()]);
        return { customers: c?.items ?? [], customerTotal: c?.total ?? null, products: p || [] };
      }

      case 'production_orders':
      case 'production_actual':
      case 'production_plan': {
        const [wo, p] = await Promise.all([api.getWorkOrders(), api.getProducts()]);
        return { workOrders: wo || [], products: p || [] };
      }

      case 'quality_control': {
        // QC membutuhkan dua daftar: SPK selesai yang belum diperiksa dan hasil yang tersimpan.
        const [wo, qc] = await Promise.all([api.getWorkOrders(), api.getQualityChecks()]);
        return { workOrders: wo || [], qualityChecks: qc || [] };
      }

      case 'cost_calc':
        return { costSummary: await api.getCostSummary() };

      case 'sales_returns': {
        const [o, r] = await Promise.all([api.getOrders({ limit: 100 }), api.getReturns()]);
        return { orders: o?.items ?? [], orderTotal: o?.total ?? null, returns: r || [] };
      }

      case 'general_ledger':
      case 'cash_bank':
      case 'payables_receivables':
      case 'financial_reports': {
        // Jurnal per rentang dimuat oleh FinanceView; di sini saldo akun, pesanan, pembelian
        // terbuka (yang benar-benar menjadi utang), dan ringkasan dasbor.
        const [bal, o, po, d] = await Promise.all([
          api.getJournalBalances(), api.getOrders({ outstanding: true, limit: 500 }),
          api.getPurchases({ status: 'open', limit: 300 }), api.getDashboard(),
        ]);
        return {
          balances: bal || [], orders: o?.items ?? [], orderTotal: o?.total ?? null,
          purchases: po?.items ?? [], purchaseTotal: po?.total ?? null, dashboardData: d,
        };
      }

      case 'recipes_bom': {
        const [p, ings] = await Promise.all([api.getProducts(), api.getIngredients()]);
        return { products: p || [], ingredients: ings || [] };
      }

      case 'purchases':
      case 'suppliers': {
        // Halaman pertama 100 PO: tabel 300 baris menahan tata letak layar. Sisa halaman
        // dimuat lewat tombol "Muat lagi" tanpa memuat ulang seluruh daftar.
        const [po, sup, ings, locs] = await Promise.all([
          api.getPurchases({ limit: 100 }), api.getSuppliers(), api.getIngredients(), api.getLocations(),
        ]);
        return {
          purchases: po?.items ?? [], purchaseTotal: po?.total ?? null,
          suppliers: sup || [], ingredients: ings || [], locations: locs || [],
        };
      }

      case 'inventory_locations':
      case 'kitchen_transfer':
      case 'stock_catalog': {
        const [locs, ings, mv, p, fg] = await Promise.all([
          api.getLocations(),
          api.getIngredients(),
          api.getInventoryMovements(),
          tab === 'stock_catalog' ? api.getProducts() : Promise.resolve(null),
          tab === 'stock_catalog' ? api.getFinishedGoods() : Promise.resolve(null),
        ]);
        const data = { locations: locs || [], ingredients: ings || [], movements: mv || [] };
        if (p) data.products = p;
        if (fg) data.finishedGoods = fg;
        data.stockByLocation = await stokPerLokasi(locs);
        return data;
      }

      case 'stock_opname': {
        const [locs, ings] = await Promise.all([api.getLocations(), api.getIngredients()]);
        return {
          locations: locs || [],
          ingredients: ings || [],
          stockByLocation: await stokPerLokasi(locs),
        };
      }

      case 'batch_expiry': {
        const [ings, b] = await Promise.all([api.getIngredients(), api.getInventoryBatches()]);
        return { ingredients: ings || [], batches: b || [] };
      }

      case 'tasks':
        return { tasks: await api.getTasks() };

      case 'notifications':
        return { notifications: await api.getNotifications() };

      case 'executive_analytics':
        return { analytics: await api.getAnalytics() };

      case 'settings':
        return { settings: (await api.getSettings()) || {} };

      case 'device_settings': {
        const [snap, pekerjaan] = await Promise.all([api.getDevices(), api.getPrintJobs(50)]);
        return { deviceSnapshot: snap, printJobs: pekerjaan || [] };
      }

      case 'user_management':
        return { users: (await api.getUsers()) || [] };

      default:
        return {};
    }
  };

  const terapkanData = (data) => {
    Object.entries(data).forEach(([kunci, nilai]) => setData[kunci]?.(nilai));
  };

  // Umur simpanan layar. Satu putaran menjelajah seluruh menu pada data besar makan belasan
  // detik, jadi umur 10 detik membuat hampir setiap kunjungan ulang memuat ulang dari server.
  // Setiap aksi pengubahan tetap membuang seluruh simpanan, sehingga angka di layar tidak
  // tertinggal sesudah transaksi, stok, atau produksi berubah.
  const UMUR_CACHE_LAYAR_MS = 30000;

  // Penanda "layar siap dipakai" untuk perkakas ukur (tests/Perf): dikirim sesudah data tab ini
  // diterapkan dan React sempat menggambar dua kali, sehingga lama menunggu data tidak tercampur
  // lama menggambar. Aplikasi sendiri tidak mendengarkan event ini.
  const tandaiSiap = (tab, mulai) => {
    requestAnimationFrame(() => requestAnimationFrame(() => {
      window.dispatchEvent(new CustomEvent('s22:layar-siap', {
        detail: { tab, ms: Math.round(performance.now() - mulai) }
      }));
    }));
  };

  const loadData = async (paksa = true) => {
    const nomor = ++urutanMuat.current;
    const tab = activeTab;
    const mulai = performance.now();
    if (!paksa) {
      const simpanan = cacheLayar.current.get(tab);
      if (simpanan && Date.now() - simpanan.waktu < UMUR_CACHE_LAYAR_MS) {
        terapkanData(simpanan.data);
        setTabSiap(tab);
        setLoading(false);
        setLoadError('');
        tandaiSiap(tab, mulai);
        return;
      }
    }
    setLoading(true);
    setLoadError('');
    try {
      const data = await ambilDataLayar(tab);
      // Pindah menu saat permintaan sebelumnya masih berjalan tidak boleh membuat jawaban lama
      // menimpa layar yang sekarang, atau memunculkan galatnya di layar yang tidak dibuka.
      if (nomor !== urutanMuat.current) return;
      terapkanData(data);
      setTabSiap(tab);
      setServerMati(false);
      // Muat ulang paksa (tombol perbarui atau sesudah aksi pengubahan) membuang seluruh simpanan:
      // layar lain yang dibuka berikutnya harus membaca angka terbaru, bukan salinan lama.
      if (paksa) cacheLayar.current.clear();
      cacheLayar.current.set(tab, { waktu: Date.now(), data });
      tandaiSiap(tab, mulai);
    } catch (err) {
      if (nomor !== urutanMuat.current) return;
      console.error(err);
      setServerMati(err?.name === 'ServerTidakTerhubung');
      setLoadError(`Data gagal dimuat: ${err.message || 'kesalahan tidak diketahui'}`);
    } finally {
      if (nomor === urutanMuat.current) setLoading(false);
    }
  };

  // Penantian server: /health dibuka tanpa token dan sangat ringan, jadi aman ditanya dari layar
  // mana pun. Setelah server menjawab, data dimuat ulang sendiri — pengguna tidak perlu menekan
  // apa pun, apalagi menutup jendela yang terbuka.
  useEffect(() => {
    if (!serverMati || !user) return undefined;
    let berhenti = false;
    let pewaktu = null;
    const tunggu = () => {
      pewaktu = setTimeout(async () => {
        if (berhenti) return;
        // Server yang hidup belum tentu bisa melayani data (mis. basis data sedang sibuk), jadi
        // pemuatan tetap dicoba. Bila berhasil, serverMati padam dan penantian ini berhenti
        // sendiri; bila gagal karena server, penantian dijadwalkan lagi.
        if (await api.serverHidup()) await loadData(true);
        if (!berhenti) tunggu();
      }, 4000);
    };
    tunggu();
    return () => {
      berhenti = true;
      if (pewaktu) clearTimeout(pewaktu);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [serverMati, user, activeTab]);

  const handleLogin = async (e) => {
    e.preventDefault();
    setLoginLoading(true);
    setLoginError('');
    try {
      const res = await api.login(loginForm.username, loginForm.password);
      api.setAuth(res.token, {
        id: res.userId,
        username: res.username,
        role: roleName(res.role),
        capabilities: res.capabilities || [],
        mustChangePassword: res.mustChangePassword
      });
      // Sesi baru selalu mulai dari dasbor dengan data kosong: tampilan dan data milik
      // pengguna sebelumnya tidak boleh terlihat oleh pengguna berikutnya di terminal yang sama.
      kosongkanTampilan();
      const masuk = api.getUser();
      setActiveTab(tabAwal(masuk));
      setUser(masuk);
    } catch (err) {
      setLoginError(err.message || 'Nama pengguna atau kata sandi tidak cocok.');
    } finally {
      setLoginLoading(false);
    }
  };

  const kosongkanTampilan = () => {
    // Permintaan yang masih berjalan milik sesi sebelumnya tidak boleh mengisi layar sesi baru.
    urutanMuat.current += 1;
    // Simpanan data milik pengguna sebelumnya tidak boleh dipakai pengguna berikutnya.
    cacheLayar.current.clear();
    setActiveTab('dashboard');
    setDashboardData(null);
    setOrders([]);
    setOrderTotal(null);
    setCustomers([]);
    setProducts([]);
    setWorkOrders([]);
    setLocations([]);
    setIngredients([]);
    setStockByLocation({});
    setMovements([]);
    setBalances([]);
    setSettings({});
    setUsers([]);
    setTasks(null);
    setAnalytics(null);
    setBatches([]);
    setQualityChecks([]);
    setCostSummary(null);
    setReturns([]);
    setPurchases([]);
    setSuppliers([]);
    setLoading(false);
    setLoadError('');
  };

  const handleLogout = async () => {
    // Tujuan logout adalah token di server berhenti berlaku, bukan sekadar membersihkan layar.
    // Bila server tidak terjangkau, sesi lokal tetap dibersihkan; token di server baru tercabut
    // pada logout berikutnya yang berhasil, selama masa berlakunya belum habis.
    try { await api.logout(); } catch { /* diabaikan: pembersihan lokal di bawah tetap jalan */ }
    api.clearAuth();
    setUser(null);
    kosongkanTampilan();
    // Sandi pengguna sebelumnya tidak boleh tertinggal di kolom login.
    setLoginForm({ username: '', password: '' });
  };

  const handleChangePassword = async (e) => {
    e.preventDefault();
    if (changePwForm.next !== changePwForm.confirm) {
      setChangePwError('Kata sandi baru dan konfirmasi tidak cocok.');
      return;
    }
    setChangePwLoading(true);
    setChangePwError('');
    try {
      await api.changePassword(changePwForm.current, changePwForm.next);
      // Token lama masih membawa tanda mustChangePassword, jadi server tetap menolaknya.
      // Masuk ulang dengan sandi baru memberi token yang sah tanpa memaksa pengguna keluar.
      const res = await api.login(user.username, changePwForm.next);
      api.setAuth(res.token, {
        id: res.userId,
        username: res.username,
        role: roleName(res.role),
        capabilities: res.capabilities || [],
        mustChangePassword: res.mustChangePassword
      });
      setUser(api.getUser());
      setChangePwModal(false);
      setChangePwForm({ current: '', next: '', confirm: '' });
    } catch (err) {
      setChangePwError(err.message || 'Gagal mengganti kata sandi.');
    } finally {
      setChangePwLoading(false);
    }
  };

  // Galat aksi dibiarkan naik ke layar: modal tetap terbuka dengan isian utuh dan pesan
  // ditampilkan di dalam form. Menangkapnya di sini membuat pengguna kehilangan isian
  // produksi/pembelian yang panjang hanya karena satu penolakan server.
  const handleCompleteWorkOrder = async (id, payload) => {
    await api.completeWorkOrder(id, payload);
    await loadData();
  };

  const handleCreateQualityCheck = async (payload) => {
    await api.createQualityCheck(payload);
    await loadData();
  };

  const handleCreateReturn = async (payload) => {
    await api.createReturn(payload);
    await loadData();
  };

  const handleTransferStock = async (payload) => {
    // Kesalahan dibiarkan naik agar layar yang menampilkan pesan, bukan popup global.
    await api.transferStock(payload);
    await loadData();
  };

  const handleCreateOrder = async (payload) => {
    await api.createOrder(payload);
    await loadData();
  };

  const handleCreateWorkOrder = async (payload) => {
    await api.createWorkOrder(payload);
    await loadData();
  };

  const handleCancelWorkOrder = async (id, reason) => {
    await api.cancelWorkOrder(id, reason);
    await loadData();
  };

  const handleAddPayment = async (orderId, payload) => {
    await api.addPayment(orderId, payload);
    await loadData();
  };

  // Galat dibiarkan naik ke layar: modal tetap terbuka dengan isian yang sudah diketik dan
  // pesannya tampil di dalam form. Ditangkap di sini hanya memunculkan alert lalu menutup modal,
  // sehingga pengguna kehilangan seluruh isian saat penyimpanan gagal.
  const handleCreateProduct = async (payload) => {
    await api.createProduct(payload);
    await loadData();
  };

  const handleUpdateProduct = async (id, payload) => {
    await api.updateProduct(id, payload);
    await loadData();
  };

  const handleDeleteProduct = async (id) => {
    await api.deleteProduct(id);
    await loadData();
  };

  const handleCreateIngredient = async (payload) => {
    await api.createIngredient(payload);
    await loadData();
  };

  const handleUpdateIngredient = async (id, payload) => {
    await api.updateIngredient(id, payload);
    await loadData();
  };

  const handleDeleteIngredient = async (id) => {
    await api.deleteIngredient(id);
    await loadData();
  };

  const handleCreatePurchase = async (payload) => {
    await api.createPurchase(payload); await loadData();
  };
  const handleReceivePurchase = async (id, payload) => {
    await api.receivePurchase(id, payload); await loadData();
  };
  const handleCancelPurchase = async (id) => {
    await api.cancelPurchase(id); await loadData();
  };
  const handleCreateSupplier = async (payload) => {
    await api.createSupplier(payload); await loadData();
  };
  const handleUpdateSupplier = async (id, payload) => {
    await api.updateSupplier(id, payload); await loadData();
  };

  // Layar keuangan menghitung piutang dan utang dari daftar yang dimuat. Tanpa halaman
  // berikutnya, angka di kartu berhenti di 100/300 baris pertama dan tampak lebih kecil dari
  // kenyataan. Kedua pemuat ini melanjutkan dari jumlah baris yang sudah tampil.
  const handleLoadMoreReceivables = async () => {
    const halaman = await api.getOrders({ outstanding: true, limit: 500, offset: orders.length });
    const baru = halaman?.items ?? [];
    setOrders((lama) => {
      const ada = new Set((lama ?? []).map((o) => o.id));
      return [...(lama ?? []), ...baru.filter((o) => !ada.has(o.id))];
    });
    setOrderTotal(halaman?.total ?? null);
  };
  const handleLoadMorePayables = async () => {
    const halaman = await api.getPurchases({ status: 'open', limit: 300, offset: purchases.length });
    const baru = halaman?.items ?? [];
    setPurchases((lama) => {
      const ada = new Set((lama ?? []).map((p) => p.id));
      return [...(lama ?? []), ...baru.filter((p) => !ada.has(p.id))];
    });
    setPurchaseTotal(halaman?.total ?? null);
  };

  // Pengaturan & hak akses: hasil dikembalikan ke layar (bukan alert) supaya modal/form
  // bisa menampilkan pesan sukses atau peringatan zona waktu apa adanya.
  const handleSaveSettings = async (values) => {
    const hasil = await api.saveSettings(values);
    const segar = await api.getSettings();
    setSettings(segar || {});
    // Merek langsung mengikuti isian yang baru disimpan, tanpa menunggu aplikasi dibuka ulang.
    setBrand(brandFromSettings(segar));
    return hasil;
  };
  const handleSaveDeviceSettings = async (values) => {
    const hasil = await api.saveDeviceSettings(values);
    setDeviceSnapshot(await api.getDevices());
    setPrintJobs((await api.getPrintJobs(50)) || []);
    return hasil;
  };
  const handleTestPrint = async () => {
    try {
      return await api.testPrint();
    } finally {
      api.getPrintJobs(50).then((j) => setPrintJobs(j || [])).catch(() => {});
    }
  };
  const handleCreateUser = async (payload) => {
    await api.createUser(payload);
    setUsers(await api.getUsers());
  };
  const handleUpdateUser = async (id, payload) => {
    await api.updateUser(id, payload);
    setUsers(await api.getUsers());
  };
  const handleAdjustStock = async (payload) => {
    await api.adjustStock(payload);
    await loadData();
  };

  // Login Screen dalam Bahasa Indonesia
  if (!user) {
    return (
      <div className="min-h-screen bg-slate-950 flex items-center justify-center p-4 relative overflow-hidden">
        {/* Glow ambient background */}
        <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-amber-500/10 rounded-full blur-3xl pointer-events-none" />
        <div className="absolute bottom-1/4 right-1/4 w-96 h-96 bg-emerald-500/10 rounded-full blur-3xl pointer-events-none" />

        <div className="w-full max-w-md bg-slate-900/90 border border-slate-800 rounded-3xl p-8 shadow-2xl relative z-10 backdrop-blur-xl">
          <div className="text-center space-y-2 mb-8">
            <div className="mx-auto w-fit">
              <BrandMark brand={brand} besar />
            </div>
            <h1 className="text-2xl font-bold text-white tracking-tight break-words">{brand.name}</h1>
            <p className="text-xs text-slate-400 break-words">{brand.tagline}</p>
          </div>

          {loginError && (
            <div className="mb-4 p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
              {loginError}
            </div>
          )}

          <form onSubmit={handleLogin} className="space-y-4">
            <div>
              <label className="text-xs font-semibold text-slate-400 block mb-1.5">Nama Pengguna / ID Pegawai</label>
              <input
                type="text"
                value={loginForm.username}
                onChange={(e) => setLoginForm({ ...loginForm, username: e.target.value })}
                className="w-full px-4 py-3 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                placeholder="Masukkan nama pengguna..."
                required
              />
            </div>

            <div>
              <label className="text-xs font-semibold text-slate-400 block mb-1.5">Kata Sandi (PIN)</label>
              <input
                type="password"
                value={loginForm.password}
                onChange={(e) => setLoginForm({ ...loginForm, password: e.target.value })}
                className="w-full px-4 py-3 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                placeholder="Masukkan kata sandi..."
                required
              />
            </div>

            <button
              type="submit"
              disabled={loginLoading}
              className="w-full py-3.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-xl shadow-amber-500/20 transition-all duration-200 mt-2"
            >
              {loginLoading ? 'Memeriksa Hak Akses...' : 'Masuk ke Sistem'}
            </button>
          </form>

          <div className="mt-8 pt-4 border-t border-slate-800/80 text-center text-[11px] text-slate-500">
            Aplikasi Desktop Toko Roti • Edisi Bisnis Terintegrasi
          </div>
        </div>
      </div>
    );
  }

  // Active View Rendering
  const renderCurrentView = () => {
    switch (activeTab) {
      case 'production_orders':
      case 'production_actual':
      case 'production_plan':
        return (
          <ProductionView
            workOrders={workOrders}
            onCompleteWorkOrder={handleCompleteWorkOrder}
            onCreateWorkOrder={handleCreateWorkOrder}
            onCancelWorkOrder={handleCancelWorkOrder}
            products={products}
            loading={loading}
            onRefresh={loadData}
            canWrite={bolehAksi(user, 'production-write')}
            canCancel={bolehAksi(user, 'production-cancel')}
            view={activeTab}
            onViewChange={setActiveTab}
          />
        );

      case 'quality_control':
        return (
          <QualityView
            qualityChecks={qualityChecks}
            workOrders={workOrders}
            loading={loading}
            onRefresh={loadData}
            onCreateQualityCheck={handleCreateQualityCheck}
            canWrite={bolehAksi(user, 'production-write')}
          />
        );

      case 'cost_calc':
        return (
          <CostView summary={costSummary} loading={loading} onRefresh={loadData} />
        );

      case 'inventory_locations':
      case 'kitchen_transfer':
      case 'stock_catalog':
        return (
          <InventoryView
            view={activeTab}
            onViewChange={setActiveTab}
            locations={locations}
            ingredients={ingredients}
            stockByLocation={stockByLocation}
            movements={movements}
            products={products}
            finishedGoods={finishedGoods}
            loading={loading}
            onRefresh={loadData}
            onTransferStock={handleTransferStock}
            canTransfer={bolehAksi(user, 'inventory-write')}
          />
        );

      case 'pos':
      case 'orders':
      case 'preorders':
      case 'customers':
      case 'receivables':
        return (
          <SalesView
            orders={orders}
            orderTotal={orderTotal}
            customers={customers}
            customerTotal={customerTotal}
            products={products}
            user={user}
            brand={brand}
            onCreateOrder={handleCreateOrder}
            onAddPayment={handleAddPayment}
            onRefresh={loadData}
            loading={loading}
            view={activeTab}
            onViewChange={setActiveTab}
          />
        );

      case 'sales_returns':
        return (
          <ReturnsView
            returns={returns}
            orders={orders}
            loading={loading}
            onRefresh={loadData}
            onCreateReturn={handleCreateReturn}
            canWrite={bolehAksi(user, 'sales-return')}
          />
        );

      case 'general_ledger':
      case 'cash_bank':
      case 'payables_receivables':
      case 'financial_reports':
        return (
          <FinanceView
            view={activeTab}
            onViewChange={setActiveTab}
            balances={balances}
            orders={orders}
            orderTotal={orderTotal}
            purchases={purchases}
            purchaseTotal={purchaseTotal}
            summary={dashboardData}
            loading={loading}
            onRefresh={loadData}
            onLoadMoreOrders={handleLoadMoreReceivables}
            onLoadMorePurchases={handleLoadMorePayables}
          />
        );

      case 'tasks':
      case 'notifications':
        return (
          <WorkspaceView
            view={activeTab}
            tasks={tasks}
            notifications={notifications}
            loading={loading}
            onRefresh={loadData}
            onSyncNotifications={async () => { await api.syncNotifications(); await loadData(); }}
            onMarkRead={async (id) => { await api.markNotificationRead(id); await loadData(); }}
            onMarkAllRead={async () => { await api.markAllNotificationsRead(); await loadData(); }}
            onOpenTab={setActiveTab}
          />
        );

      case 'recipes_bom':
        return (
          <RecipesBomView
            products={products}
            ingredients={ingredients}
            onCreateProduct={handleCreateProduct}
            onUpdateProduct={handleUpdateProduct}
            onDeleteProduct={handleDeleteProduct}
            onCreateIngredient={handleCreateIngredient}
            onUpdateIngredient={handleUpdateIngredient}
            onDeleteIngredient={handleDeleteIngredient}
            canManage={bolehAksi(user, 'master-data-write')}
          />
        );

      case 'purchases':
      case 'suppliers':
        return (
          <PurchasesView
            purchases={purchases}
            purchaseTotal={purchaseTotal}
            suppliers={suppliers}
            ingredients={ingredients}
            locations={locations}
            onCreatePurchase={handleCreatePurchase}
            onReceivePurchase={handleReceivePurchase}
            onCancelPurchase={handleCancelPurchase}
            onCreateSupplier={handleCreateSupplier}
            onUpdateSupplier={handleUpdateSupplier}
            canWritePurchase={bolehAksi(user, 'purchases-create')}
            canReceivePurchase={bolehAksi(user, 'purchases-receive')}
            canCancelPurchase={bolehAksi(user, 'purchases-cancel')}
            canExportPurchase={bolehAksi(user, 'finance-read')}
            canManageSuppliers={bolehAksi(user, 'master-data-write')}
            initialTab={activeTab === 'suppliers' ? 'suppliers' : 'po'}
            onTabChange={(tab) => setActiveTab(tab === 'suppliers' ? 'suppliers' : 'purchases')}
          />
        );

      case 'stock_opname':
        return (
          <OpnameView
            locations={locations}
            ingredients={ingredients}
            stockByLocation={stockByLocation}
            loading={loading}
            onRefresh={loadData}
            onAdjust={handleAdjustStock}
            canWrite={bolehAksi(user, 'inventory-write')}
          />
        );

      case 'batch_expiry':
        return (
          <BatchesView
            batches={batches}
            ingredients={ingredients}
            loading={loading}
            onRefresh={loadData}
            canWrite={bolehAksi(user, 'inventory-write')}
          />
        );

      case 'executive_analytics':
        return (
          <AnalyticsView analytics={analytics} loading={loading} onRefresh={loadData} />
        );

      case 'device_settings':
        return (
          <DeviceView
            snapshot={deviceSnapshot}
            jobs={printJobs}
            loading={loading}
            onRefresh={loadData}
            onSaveSettings={handleSaveDeviceSettings}
            onTestPrint={handleTestPrint}
          />
        );

      case 'audit_log':
      case 'user_management':
      case 'settings':
        return (
          <SystemView
            view={activeTab}
            settings={settings}
            users={users}
            loading={loading}
            onRefresh={loadData}
            onSaveSettings={handleSaveSettings}
            onCreateUser={handleCreateUser}
            onUpdateUser={handleUpdateUser}
          />
        );

      case 'dashboard':
      default:
        return (
          <div className="space-y-6">
            {/* KPI Cards */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
              <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl shadow-lg">
                <div className="flex items-center justify-between text-slate-400 text-xs">
                  <span>Penerimaan Kasir Hari Ini</span>
                  <ShoppingBag className="w-4 h-4 text-amber-400" />
                </div>
                <div className="text-2xl font-black text-white mt-2 font-mono">
                  {dashboardData != null
                    ? formatMinor(dashboardData.revenue)
                    : '-'}
                </div>
                <div className="text-xs text-slate-400 mt-1">
                  {dashboardData != null
                    ? `Kas: ${formatMinor(dashboardData.kasBalance)}`
                    : 'Memuat data...'}
                </div>
              </div>

              <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl shadow-lg">
                <div className="flex items-center justify-between text-slate-400 text-xs">
                  <span>Pesanan Aktif</span>
                  <ChefHat className="w-4 h-4 text-orange-400" />
                </div>
                <div className="text-2xl font-black text-white mt-2">
                  {dashboardData != null
                    ? dashboardData.activeOrders
                    : '-'} Pesanan
                </div>
                <div className="text-xs text-amber-400 mt-1">
                  {dashboardData != null
                    ? (dashboardData.activeOrders > 0 ? 'pesanan belum lunas' : 'Tidak ada pesanan aktif')
                    : 'Memuat data...'}
                </div>
              </div>

              <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl shadow-lg">
                <div className="flex items-center justify-between text-slate-400 text-xs">
                  <span>Bahan Baku Kritis</span>
                  <Warehouse className="w-4 h-4 text-rose-400" />
                </div>
                {(() => {
                  const stockAlerts = dashboardData?.alerts?.filter(a => a.type === 'stock') ?? [];
                  return (
                    <>
                      <div className={`text-2xl font-black mt-2 ${stockAlerts.length > 0 ? 'text-rose-400' : dashboardData != null ? 'text-emerald-400' : 'text-white'}`}>
                        {dashboardData != null ? `${stockAlerts.length} Bahan` : '-'}
                      </div>
                      <div className="text-xs text-slate-400 mt-1">
                        {stockAlerts.length > 0
                          ? stockAlerts[0].message
                          : dashboardData != null ? 'Semua stok aman' : 'Memuat data...'}
                      </div>
                    </>
                  );
                })()}
              </div>

              <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl shadow-lg">
                <div className="flex items-center justify-between text-slate-400 text-xs">
                  <span>Piutang Belum Lunas</span>
                  <Sparkles className="w-4 h-4 text-emerald-400" />
                </div>
                <div className="text-2xl font-black text-emerald-400 mt-2 font-mono">
                  {dashboardData != null
                    ? formatMinor(dashboardData.piutang)
                    : '-'}
                </div>
                <div className="text-xs text-slate-400 mt-1">
                  {dashboardData?.alerts?.filter(a => a.type === 'payment').length > 0
                    ? `${dashboardData.alerts.filter(a => a.type === 'payment').length} tagihan terlewat`
                    : dashboardData != null ? 'Tidak ada piutang jatuh tempo' : 'Memuat data...'}
                </div>
              </div>
            </div>

            {/* Smart Alerts */}
            {dashboardData?.alerts?.length > 0 && (
              <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
                <div className="p-4 border-b border-slate-800 flex items-center gap-2">
                  <Clock className="w-4 h-4 text-amber-400" />
                  <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                    Notifikasi & Peringatan ({dashboardData.alerts.length})
                  </h3>
                </div>
                <div className="divide-y divide-slate-800/60">
                  {dashboardData.alerts.map((alert, idx) => {
                    const colorMap = {
                      stock: 'text-rose-400 bg-rose-500/10 border-rose-500/20',
                      pickup: 'text-amber-400 bg-amber-500/10 border-amber-500/20',
                      payment: 'text-blue-400 bg-blue-500/10 border-blue-500/20',
                    };
                    const colors = colorMap[alert.type] ?? 'text-slate-300 bg-slate-800/40 border-slate-700/40';
                    return (
                      <div key={idx} className="px-4 py-3 flex items-center gap-3">
                        <span className={`inline-flex px-2 py-0.5 rounded-full text-[10px] font-bold border ${colors} shrink-0`}>
                          {alert.type === 'stock' ? 'STOK' : alert.type === 'pickup' ? 'PICKUP' : alert.type === 'payment' ? 'PIUTANG' : alert.type.toUpperCase()}
                        </span>
                        <span className="text-xs text-slate-300">{alert.message}</span>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {/* Upcoming Orders */}
            {dashboardData?.upcomingOrders?.length > 0 && (
              <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
                <div className="p-4 border-b border-slate-800 flex items-center gap-2">
                  <CheckCircle className="w-4 h-4 text-emerald-400" />
                  <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                    Pesanan Mendatang ({dashboardData.upcomingOrders.length})
                  </h3>
                </div>
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-xs text-slate-300">
                    <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                      <tr>
                        <th className="py-3 px-4 font-semibold">No. Pesanan</th>
                        <th className="py-3 px-4 font-semibold">Pelanggan</th>
                        <th className="py-3 px-4 font-semibold">Deskripsi</th>
                        <th className="py-3 px-4 font-semibold text-center">Tanggal Ambil</th>
                        <th className="py-3 px-4 font-semibold text-right">Status</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-800/60">
                      {dashboardData.upcomingOrders.map((order) => (
                        <tr key={order.id} className="hover:bg-slate-800/40 transition-colors">
                          <td className="py-3 px-4 font-mono text-amber-400 font-medium">{order.orderNumber}</td>
                          <td className="py-3 px-4 font-semibold text-white">{order.customerName}</td>
                          <td className="py-3 px-4 text-slate-400">{order.description}</td>
                          <td className="py-3 px-4 text-center">
                            {formatDate(order.pickupDate)}
                          </td>
                          <td className="py-3 px-4 text-right">
                            <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20">
                              {orderStatusLabel(order.status)}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {/* Quick Actions Shortcuts */}
            <div className="bg-slate-900/60 border border-slate-800/80 p-5 rounded-2xl">
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400 mb-3">
                Jalan Pintas Menu Cepat
              </h3>
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                {bolehAksi(user, 'sales') && (
                  <button
                    onClick={() => setActiveTab('pos')}
                    className="p-3 bg-slate-950/80 hover:bg-amber-500/10 border border-slate-800 hover:border-amber-500/30 rounded-xl text-left transition-all group"
                  >
                    <Store className="w-5 h-5 text-amber-400 mb-2 group-hover:scale-110 transition-transform" />
                    <div className="text-xs font-bold text-white">Kasir Toko Cepat</div>
                    <div className="text-[10px] text-slate-400 mt-0.5">Penjualan langsung roti</div>
                  </button>
                )}

                {/* Pintasan mengikuti kapabilitas yang sama dengan menu samping: tombol yang
                    pasti ditolak server tidak dirender, supaya klik tidak berakhir jadi galat. */}
                {bolehAksi(user, 'production-write') && (
                  <button
                    onClick={() => setActiveTab('production_orders')}
                    className="p-3 bg-slate-950/80 hover:bg-amber-500/10 border border-slate-800 hover:border-amber-500/30 rounded-xl text-left transition-all group"
                  >
                    <ChefHat className="w-5 h-5 text-amber-400 mb-2 group-hover:scale-110 transition-transform" />
                    <div className="text-xs font-bold text-white">Perintah Kerja Dapur</div>
                    <div className="text-[10px] text-slate-400 mt-0.5">Pantau resep & oven</div>
                  </button>
                )}

                {bolehAksi(user, 'inventory-write') && (
                  <button
                    onClick={() => setActiveTab('kitchen_transfer')}
                    className="p-3 bg-slate-950/80 hover:bg-emerald-500/10 border border-slate-800 hover:border-emerald-500/30 rounded-xl text-left transition-all group"
                  >
                    <Warehouse className="w-5 h-5 text-emerald-400 mb-2 group-hover:scale-110 transition-transform" />
                    <div className="text-xs font-bold text-white">Mutasi Bahan Baku</div>
                    <div className="text-[10px] text-slate-400 mt-0.5">Kirim gudang ke dapur</div>
                  </button>
                )}

                {bolehAksi(user, 'finance-read') && (
                  <button
                    onClick={() => setActiveTab('general_ledger')}
                    className="p-3 bg-slate-950/80 hover:bg-blue-500/10 border border-slate-800 hover:border-blue-500/30 rounded-xl text-left transition-all group"
                  >
                    <Wallet className="w-5 h-5 text-blue-400 mb-2 group-hover:scale-110 transition-transform" />
                    <div className="text-xs font-bold text-white">Buku Kas & Jurnal</div>
                    <div className="text-[10px] text-slate-400 mt-0.5">Pembukuan otomatis</div>
                  </button>
                )}
              </div>
            </div>
          </div>
        );
    }
  };

  return (
    <div className="flex h-screen bg-slate-950 overflow-hidden font-sans">
      {/* Change Password Modal — shown when mustChangePassword is true */}
      {changePwModal && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-slate-950/90 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50">
              <h3 className="text-base font-bold text-white">Ganti Kata Sandi</h3>
              <p className="text-xs text-amber-400 mt-1">
                Kata sandi Anda harus diganti sebelum melanjutkan.
              </p>
            </div>
            <form onSubmit={handleChangePassword} className="p-5 space-y-4">
              {changePwError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
                  {changePwError}
                </div>
              )}
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Kata Sandi Saat Ini</label>
                <input
                  type="password"
                  value={changePwForm.current}
                  onChange={(e) => setChangePwForm({ ...changePwForm, current: e.target.value })}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Kata Sandi Baru</label>
                <input
                  type="password"
                  value={changePwForm.next}
                  onChange={(e) => setChangePwForm({ ...changePwForm, next: e.target.value })}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                />
                <p className="text-[10px] text-slate-500 mt-1">Minimal 8 karakter serta memuat huruf dan angka.</p>
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Konfirmasi Kata Sandi Baru</label>
                <input
                  type="password"
                  value={changePwForm.confirm}
                  onChange={(e) => setChangePwForm({ ...changePwForm, confirm: e.target.value })}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                />
              </div>
              <div className="flex justify-end">
                <button
                  type="submit"
                  disabled={changePwLoading}
                  className="px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all"
                >
                  {changePwLoading ? 'Menyimpan...' : 'Simpan Kata Sandi Baru'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
      {/* Enterprise Sidebar */}
      <Sidebar
        activeTab={activeTab}
        onSelectTab={setActiveTab}
        user={user}
        brand={brand}
        onLogout={handleLogout}
      />

      {/* Main Content Area */}
      <main className="flex-1 flex flex-col h-screen overflow-hidden">
        {/* Top Navbar */}
        <header className="h-14 border-b border-slate-800/80 bg-slate-950/60 backdrop-blur-xl px-6 flex items-center justify-between shrink-0 z-20">
          <div className="flex items-center gap-3">
            <span className="text-xs font-semibold text-slate-400">Lokasi Kerja:</span>
            <div className="px-3 py-1 bg-slate-900 border border-slate-800 rounded-lg text-xs font-bold text-white flex items-center gap-1.5">
              <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
              <span>{brand.outlet || 'Kantor Sentral & Toko Utama'}</span>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <button
              onClick={loadData}
              className="p-2 text-slate-400 hover:text-white hover:bg-slate-900 rounded-lg transition-colors"
              title="Perbarui Data"
            >
              <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
            </button>
          </div>
        </header>

        {/* Dynamic View Scrollable Container */}
        <div className="flex-1 overflow-y-auto p-6 custom-scrollbar bg-slate-950">
          <div className="max-w-7xl mx-auto">
            {loadError && (
              <div className="mb-4 p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs flex items-center justify-between gap-3">
                <span>{loadError}</span>
                {/* Selama server mati tombol "Coba lagi" hanya mengulang kegagalan yang sama;
                    layar sedang menanyakan /health sendiri dan akan memuat ulang saat server hidup. */}
                {serverMati ? (
                  <span className="text-rose-300/80 shrink-0">Menunggu server hidup lagi…</span>
                ) : (
                  <button onClick={loadData} className="text-rose-300 font-bold hover:text-white shrink-0">Coba lagi</button>
                )}
              </div>
            )}
            {(tabSiap === activeTab || loadError) ? renderCurrentView() : <LayarMemuat />}
          </div>
        </div>
      </main>
    </div>
  );
}
