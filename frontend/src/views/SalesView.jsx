import React, { useEffect, useState } from 'react';
import { 
  ShoppingBag, 
  Plus, 
  Search, 
  CreditCard, 
  Calendar, 
  UserCheck, 
  Clock, 
  CheckCircle2, 
  AlertCircle,
  Receipt,
  FileDown,
  X,
  User,
  Phone,
  FileText,
  DollarSign,
  Package,
  Download,
  ScanLine,
  Store
} from 'lucide-react';
import { api } from '../services/api';
import { useBarisBertahap } from '../utils/bertahap';
import { orderStatusName } from '../utils/enums';
import { formatMinor, fromMinor, toMinor, formatDate, formatDateTime } from '../utils/money';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';
import { bolehAksi } from '../utils/kapabilitas';

// Mendaftar printer terpasang bisa lambat (printer jaringan yang tidak menyahut). Status ini
// hanya untuk menandai tombol cetak, jadi versi singkatnya dipakai ulang selama beberapa detik
// alih-alih memeriksa ulang setiap kali layar kasir dibuka.
const UMUR_STATUS_PERANGKAT_MS = 15000;
let statusPerangkatTerakhir = { waktu: 0, nilai: null };

function statusPerangkatSingkat() {
  if (statusPerangkatTerakhir.nilai && Date.now() - statusPerangkatTerakhir.waktu < UMUR_STATUS_PERANGKAT_MS) {
    return Promise.resolve(statusPerangkatTerakhir.nilai);
  }
  return api.getDeviceStatus().then((s) => {
    statusPerangkatTerakhir = { waktu: Date.now(), nilai: s };
    return s;
  });
}

// Lima menu Penjualan berbagi satu layar; `view` memilih bagian yang dibuka lebih dulu dan
// tab bagian di bawah judul menandai bagian mana yang sedang tampil.
const TAB_SALES = [
  { id: 'pos', label: 'Kasir Cepat', icon: Store },
  { id: 'orders', label: 'Pesanan & Penjualan', icon: ShoppingBag },
  { id: 'preorders', label: 'Pesanan Terjadwal', icon: Calendar },
  { id: 'customers', label: 'Pelanggan', icon: UserCheck },
  { id: 'receivables', label: 'Piutang', icon: Receipt },
];

const BAGIAN_PENJUALAN = {
  pos: {
    judul: 'Kasir Penjualan Cepat',
    sub: 'Catat penjualan langsung, cetak struk, dan tanda terima pembayaran di meja kasir.',
  },
  orders: {
    judul: 'Daftar Pesanan & Penjualan',
    sub: 'Seluruh pesanan toko, katering, dan riwayat transaksi beserta status pembayarannya.',
  },
  preorders: {
    judul: 'Pesanan Terjadwal (Pre-Order)',
    sub: 'Pesanan dengan jadwal pengambilan hari ini atau sesudahnya, urut jadwal terdekat.',
  },
  customers: {
    judul: 'Data Pelanggan & Klien',
    sub: 'Nama, kontak, poin loyalitas, dan catatan pelanggan yang tersimpan.',
  },
  receivables: {
    judul: 'Piutang & Pembayaran',
    sub: 'Pesanan yang belum lunas beserta sisa tagihannya, untuk ditagih atau diterima pembayarannya.',
  },
};

export default function SalesView({
  orders, customers, customerTotal = null, products, user, brand, onRefresh, onCreateOrder, onAddPayment, loading,
  view = 'orders', onViewChange,
}) {
  const [search, setSearch] = useState('');
  const section = BAGIAN_PENJUALAN[view] ? view : 'orders';

  // Daftar pelanggan berhalaman: halaman pertama dari App, sisanya lewat "Muat lagi" atau
  // pencarian di server — daftar 2.000 pelanggan tidak perlu ikut di setiap pembukaan kasir.
  const [cariPelanggan, setCariPelanggan] = useState('');
  const [hasilPelanggan, setHasilPelanggan] = useState(null);
  const [totalHasilPelanggan, setTotalHasilPelanggan] = useState(null);
  const [tambahanPelanggan, setTambahanPelanggan] = useState([]);
  const [memuatPelanggan, setMemuatPelanggan] = useState(false);
  const [galatPelanggan, setGalatPelanggan] = useState('');

  useEffect(() => {
    setTambahanPelanggan([]);
  }, [customers]);

  useEffect(() => {
    const kata = cariPelanggan.trim();
    if (!kata) {
      setHasilPelanggan(null);
      setTotalHasilPelanggan(null);
      setGalatPelanggan('');
      return undefined;
    }
    let batal = false;
    // Mengetik cepat tidak boleh menembakkan satu permintaan per huruf.
    const jeda = setTimeout(async () => {
      setMemuatPelanggan(true);
      setGalatPelanggan('');
      try {
        const hasil = await api.getCustomers({ search: kata, limit: 300 });
        if (batal) return;
        setHasilPelanggan(hasil.items ?? []);
        setTotalHasilPelanggan(hasil.total);
      } catch (err) {
        if (!batal) setGalatPelanggan(err.message || 'Gagal mencari pelanggan.');
      } finally {
        if (!batal) setMemuatPelanggan(false);
      }
    }, 250);
    return () => { batal = true; clearTimeout(jeda); };
  }, [cariPelanggan]);

  const daftarPelanggan = hasilPelanggan
    ?? (tambahanPelanggan.length > 0 ? [...customers, ...tambahanPelanggan] : customers);
  const totalPelanggan = hasilPelanggan
    ? (totalHasilPelanggan ?? daftarPelanggan.length)
    : (customerTotal ?? daftarPelanggan.length);

  const muatPelangganLagi = async () => {
    setMemuatPelanggan(true);
    setGalatPelanggan('');
    try {
      const halaman = await api.getCustomers({
        search: cariPelanggan.trim(), limit: 300, offset: daftarPelanggan.length,
      });
      const sudahAda = new Set(daftarPelanggan.map((c) => c.id));
      const baru = (halaman.items ?? []).filter((c) => !sudahAda.has(c.id));
      if (hasilPelanggan) setHasilPelanggan([...hasilPelanggan, ...baru]);
      else setTambahanPelanggan((lama) => [...lama, ...baru]);
      setTotalHasilPelanggan(halaman.total);
    } catch (err) {
      setGalatPelanggan(err.message || 'Gagal memuat halaman berikutnya.');
    } finally {
      setMemuatPelanggan(false);
    }
  };

  // Total pesanan hanya boleh diketik bebas oleh yang berwenang mengatur harga (Owner/Admin).
  // Kasir memakai harga katalog, sama seperti pagar yang ditegakkan server.
  const bolehUbahTotal = bolehAksi(user, 'master-data-write');
  const bolehExport = bolehAksi(user, 'sales-export');

  const hitungTotalKatalog = (productId, qty) => {
    const produk = (products || []).find((p) => String(p.id) === String(productId));
    const jumlah = parseFloat(qty || '1');
    if (!produk || isNaN(jumlah) || jumlah <= 0) return '';
    return String(Math.round((Number(produk.sellingPrice) || 0) / 100 * jumlah));
  };

  // Pemindai USB mode keyboard wedge mengetik ke kolom yang aktif lalu mengakhiri dengan Enter.
  // Kode dicocokkan ke barcode produk yang tersimpan; nama saja tidak dipakai karena dua produk
  // bisa bernama mirip dan salah pilih berarti salah harga.
  const pindaiBarcode = () => {
    const kode = kodePindai.trim();
    if (!kode) return;
    const produk = (products || []).find(
      (p) => (p.barcode || '').trim().toLowerCase() === kode.toLowerCase()
    );
    if (!produk) {
      setOrderError(`Barcode "${kode}" tidak cocok dengan produk mana pun.`);
      setKodePindai('');
      return;
    }
    setOrderError('');
    setOrderForm((f) => ({
      ...f,
      productTemplateId: String(produk.id),
      qty: 1,
      totalAmount: hitungTotalKatalog(produk.id, 1)
    }));
    setKodePindai('');
  };

  // ── Modal: Tambah Transaksi ──────────────────────────────────────────────────
  const [showOrderModal, setShowOrderModal] = useState(false);
  const [orderForm, setOrderForm] = useState({
    customerName: '',
    phone: '',
    description: '',
    pickupDate: '',
    productTemplateId: '',
    qty: 1,
    totalAmount: '',
    downPayment: '',
    notes: ''
  });
  const {
    sedangSimpan: orderSaving,
    mulai: mulaiSimpanPesanan,
    selesai: selesaiSimpanPesanan
  } = useKunciSimpan();
  const [orderError, setOrderError] = useState('');
  // Satu kunci untuk satu kali membuka form: klik ganda hanya menyimpan satu pesanan, tetapi
  // percobaan ulang setelah galat validasi tetap boleh memakai isian yang sama.
  const [orderKunci, setOrderKunci] = useState('');

  // ── Modal: Bayar ─────────────────────────────────────────────────────────────
  const [payOrder, setPayOrder] = useState(null); // SalesOrder being paid
  const [payForm, setPayForm] = useState({ amount: '', method: 'Tunai', notes: '' });
  const {
    sedangSimpan: paySaving,
    mulai: mulaiSimpanPembayaran,
    selesai: selesaiSimpanPembayaran
  } = useKunciSimpan();
  const [payError, setPayError] = useState('');
  const [payKunci, setPayKunci] = useState('');

  // Keadaan perangkat dibaca dari server: layar tidak menganggap printer ada hanya karena
  // tombolnya ditampilkan. Bila status tidak bisa dibaca, cetak tetap dicoba dan server yang
  // menjawab; tidak ada status perangkat yang dikarang di sini.
  const [perangkat, setPerangkat] = useState(null);
  const [cetakPesan, setCetakPesan] = useState(null);
  const [sedangCetak, setSedangCetak] = useState(false);
  const [kodePindai, setKodePindai] = useState('');

  useEffect(() => {
    let hidup = true;
    statusPerangkatSingkat()
      .then((s) => { if (hidup) setPerangkat(s); })
      .catch(() => { if (hidup) setPerangkat(null); });
    return () => { hidup = false; };
  }, []);

  // Nominal dari API disimpan dalam unit minor (Rp x 100), jadi tampil maupun kirim harus
  // lewat utils/money; dulu layar ini memformat angka mentah sehingga nilainya 100x meleset.
  const formatRupiah = (val) => formatMinor(val);

  // T7 — filter by search, ditambah penyaring sesuai menu: pre-order hanya yang jadwalnya belum
  // lewat, piutang hanya yang masih bersisa. Pesanan batal tidak dihitung utang dan tidak dijadwalkan.
  const displayOrders = (orders || []).filter((ord) => {
    const status = orderStatusName(ord.status);
    if (status === 'Cancelled' && (section === 'preorders' || section === 'receivables')) return false;

    if (section === 'preorders') {
      const jadwal = new Date(ord.pickupDate);
      const hariIni = new Date();
      hariIni.setHours(0, 0, 0, 0);
      if (Number.isNaN(jadwal.getTime()) || jadwal < hariIni) return false;
    }

    if (section === 'receivables') {
      const dibayar = (ord.downPayment || 0) + (ord.payments || []).reduce((s, p) => s + p.amount, 0);
      if (dibayar >= (ord.totalAmount || 0)) return false;
    }

    if (!search.trim()) return true;
    const q = search.toLowerCase();
    return (
      (ord.customerNameSnapshot || ord.customerName || '').toLowerCase().includes(q) ||
      (ord.description || '').toLowerCase().includes(q) ||
      (ord.orderNumber || '').toLowerCase().includes(q)
    );
  });

  // Pesanan terjadwal diurutkan dari jadwal terdekat supaya yang harus disiapkan hari ini di atas.
  if (section === 'preorders') {
    displayOrders.sort((a, b) => new Date(a.pickupDate) - new Date(b.pickupDate));
  }

  // Tabel pesanan dipotong bertahap; pencarian dan hitungan total tetap memakai seluruh data.
  const barisPesanan = useBarisBertahap(displayOrders);

  // ── K1 handlers ──────────────────────────────────────────────────────────────
  const openOrderModal = () => {
    setOrderForm({
      customerName: '',
      phone: '',
      description: '',
      pickupDate: '',
      productTemplateId: '',
      qty: 1,
      totalAmount: '',
      downPayment: '',
      notes: ''
    });
    setOrderError('');
    setOrderKunci(kunciBaru('pesanan-baru'));
    setShowOrderModal(true);
  };

  const handleOrderSubmit = async (e) => {
    e.preventDefault();
    if (!orderForm.customerName.trim()) { setOrderError('Nama pelanggan wajib diisi.'); return; }
    if (!orderForm.description.trim()) { setOrderError('Deskripsi pesanan wajib diisi.'); return; }
    if (!bolehUbahTotal && !orderForm.productTemplateId) {
      setOrderError('Pilih produk dari katalog supaya harga diambil dari daftar harga.');
      return;
    }
    if (!orderForm.pickupDate) { setOrderError('Tanggal pickup wajib diisi.'); return; }
    const total = parseInt(orderForm.totalAmount, 10);
    const dp = parseInt(orderForm.downPayment || '0', 10);
    if (isNaN(total) || total <= 0) { setOrderError('Nominal total harus lebih dari 0.'); return; }
    if (isNaN(dp) || dp < 0) { setOrderError('DP tidak valid.'); return; }
    if (dp > total) { setOrderError('DP tidak boleh melebihi total.'); return; }
    const jumlah = parseFloat(orderForm.qty || '1');
    if (isNaN(jumlah) || jumlah <= 0) { setOrderError('Qty harus lebih dari 0.'); return; }

    // Kunci diambil setelah validasi agar form yang masih salah isi tidak terkunci, dan
    // ditahan di ref sinkron supaya klik kedua pada tick yang sama tidak ikut berangkat.
    if (!mulaiSimpanPesanan()) return;
    setOrderError('');
    try {
      await onCreateOrder({
        customerName: orderForm.customerName.trim(),
        phone: orderForm.phone.trim() || null,
        description: orderForm.description.trim(),
        // Tanggal dikirim apa adanya (YYYY-MM-DD). Mengubahnya ke ISO UTC menggeser pickup
        // dini hari ke hari sebelumnya, karena kolom basis data hanya menyimpan tanggal.
        pickupDate: orderForm.pickupDate,
        productTemplateId: orderForm.productTemplateId ? parseInt(orderForm.productTemplateId, 10) : null,
        quantity: jumlah,
        totalAmount: toMinor(total),
        downPayment: toMinor(dp),
        notes: orderForm.notes.trim() || null,
        idempotensi: orderKunci
      });
      setShowOrderModal(false);
    } catch (err) {
      setOrderError(err.message || 'Gagal menyimpan pesanan.');
    } finally {
      selesaiSimpanPesanan();
    }
  };

  // ── T6 handlers ──────────────────────────────────────────────────────────────
  const openPayModal = (ord) => {
    setPayOrder(ord);
    const remaining = (ord.totalAmount || 0) - (ord.downPayment || 0) - (ord.payments || []).reduce((s, p) => s + p.amount, 0);
    // Kolom nominal berisi rupiah, sisa dari API masih unit minor.
    setPayForm({ amount: remaining > 0 ? String(fromMinor(remaining)) : '', method: 'Tunai', notes: '' });
    setPayError('');
    setPayKunci(kunciBaru(`bayar-${ord.id}`));
  };

  const handlePaySubmit = async (e) => {
    e.preventDefault();
    const amount = parseInt(payForm.amount, 10);
    if (isNaN(amount) || amount <= 0) { setPayError('Nominal pembayaran harus lebih dari 0.'); return; }
    if (!mulaiSimpanPembayaran()) return;
    setPayError('');
    try {
      // Kunci yang sama dipakai dua lapis: kolom IdempotencyKey di baris pembayaran (jejak di
      // basis data) dan gerbang header di sisi server. Dulu nilainya memakai Date.now() sehingga
      // setiap klik dianggap niat baru dan pembayaran ganda tetap lolos.
      await onAddPayment(payOrder.id, {
        amount: toMinor(amount),
        method: payForm.method,
        notes: payForm.notes.trim() || null,
        idempotencyKey: payKunci,
        idempotensi: payKunci
      });
      const idPesanan = payOrder.id;
      setPayOrder(null);
      // Pembayaran sudah tersimpan di titik ini. Kegagalan cetak hanya diberitahukan dan tidak
      // mengulang pembayaran maupun mengubah status pesanan.
      if (perangkat?.autoPrint) {
        api.printReceipt(idPesanan, { idempotensi: kunciBaru(`struk-${idPesanan}`) })
          .then(() => setCetakPesan({ jenis: 'ok', orderId: idPesanan, teks: 'Pembayaran tersimpan dan struk otomatis terkirim ke printer.' }))
          .catch((err) => setCetakPesan({
            jenis: 'galat',
            orderId: idPesanan,
            teks: `Pembayaran tersimpan, tetapi struk gagal dicetak: ${err.message || 'printer tidak merespons.'}`
          }));
      }
    } catch (err) {
      setPayError(err.message || 'Gagal mencatat pembayaran.');
    } finally {
      selesaiSimpanPembayaran();
    }
  };

  // ── S8 — Cetak Struk ──────────────────────────────────────────────────────────
  // Jalur utama: server merender struk dari pesanan yang tersimpan lalu mengirimnya ke printer
  // Windows. Pesanan tidak pernah diubah oleh proses cetak.
  const kirimKePrinter = async (ord, cetakUlang = false) => {
    if (sedangCetak) return;
    setSedangCetak(true);
    setCetakPesan(null);
    try {
      await api.printReceipt(ord.id, {
        reprint: cetakUlang,
        idempotensi: kunciBaru(`struk-${ord.id}`)
      });
      setCetakPesan({
        jenis: 'ok',
        orderId: ord.id,
        teks: cetakUlang ? 'Struk ulang terkirim ke printer.' : 'Struk terkirim ke printer.'
      });
    } catch (err) {
      setCetakPesan({
        jenis: 'galat',
        orderId: ord.id,
        teks: `${err.message || 'Struk gagal dicetak.'}`
      });
    } finally {
      setSedangCetak(false);
    }
  };

  // Jalur cadangan yang disengaja: dialog cetak peramban. Dipakai hanya bila pengguna memilihnya
  // setelah jalur printer langsung gagal; ini bukan klaim bahwa printer sudah terhubung.
  const cetakLewatPeramban = (ord) => {
    if (!ord) return;
    // Nama pelanggan dan deskripsi berasal dari isian pengguna, jadi disisipkan sebagai teks:
    // tanda < atau & di nama tidak boleh merusak halaman cetak.
    const esc = (nilai) => String(nilai ?? '').replace(/[&<>"']/g, (c) => (
      { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));
    const customerName = ord.customerNameSnapshot || ord.customerName || '-';
    const description = ord.description || '-';
    const total = formatRupiah(ord.totalAmount || 0);
    const dp = formatRupiah(ord.downPayment || 0);
    const remaining = formatRupiah((ord.totalAmount || 0) - (ord.downPayment || 0) - (ord.payments || []).reduce((s, p) => s + p.amount, 0));
    const pickupDate = formatDate(ord.pickupDate);
    const createdAt = formatDateTime(ord.createdAt);
    const orderNumber = ord.orderNumber || '-';
    const phone = ord.phoneSnapshot || ord.phone || '-';

    const html = `<!DOCTYPE html>
<html lang="id">
<head>
<meta charset="UTF-8"/>
<title>Struk — ${esc(orderNumber)}</title>
<style>
  body { font-family: 'Courier New', monospace; font-size: 12px; margin: 0; padding: 16px; max-width: 320px; }
  h2 { text-align: center; font-size: 14px; margin: 0 0 4px; }
  .subtitle { text-align: center; font-size: 11px; margin: 0 0 12px; }
  hr { border: none; border-top: 1px dashed #000; margin: 8px 0; }
  .row { display: flex; justify-content: space-between; margin: 3px 0; }
  .label { color: #555; }
  .total { font-weight: bold; font-size: 13px; }
  .footer { text-align: center; margin-top: 12px; font-size: 10px; color: #777; }
  @media print { body { padding: 0; } button { display: none; } }
</style>
</head>
<body>
  <h2>${esc(brand?.name || 'Seruni 22 Bakery')}</h2>
  <p class="subtitle">${esc(brand?.tagline || '')}</p>
  <hr/>
  <div class="row"><span class="label">No. Nota</span><span>${esc(orderNumber)}</span></div>
  <div class="row"><span class="label">Tgl. Cetak</span><span>${esc(createdAt)}</span></div>
  <div class="row"><span class="label">Tgl. Pickup</span><span>${esc(pickupDate)}</span></div>
  <hr/>
  <div class="row"><span class="label">Pelanggan</span><span>${esc(customerName)}</span></div>
  <div class="row"><span class="label">Telpon</span><span>${esc(phone)}</span></div>
  <div class="row"><span class="label">Pesanan</span><span>${esc(description)}</span></div>
  <hr/>
  <div class="row total"><span>Total</span><span>${esc(total)}</span></div>
  <div class="row"><span class="label">DP / Uang Muka</span><span>${esc(dp)}</span></div>
  <div class="row"><span class="label">Sisa Tagihan</span><span>${esc(remaining)}</span></div>
  <hr/>
  <p class="footer">Terima kasih atas pesanan Anda!<br/>Simpan struk ini sebagai bukti transaksi.</p>
  <br/>
  <button onclick="window.print();" style="width:100%;padding:8px;cursor:pointer;">🖨️ Cetak</button>
</body>
</html>`;

    const win = window.open('', '_blank', 'width=400,height=600');
    if (win) {
      win.document.write(html);
      win.document.close();
    }
  };

  // ── Helpers ──────────────────────────────────────────────────────────────────
  const getStatusBadge = (ord) => {
    const status = orderStatusName(ord.status).toLowerCase();
    const totalPaid = (ord.downPayment || 0) + (ord.payments || []).reduce((s, p) => s + p.amount, 0);
    const isPaid = status === 'paid' || totalPaid >= (ord.totalAmount || 0);
    const isCancelled = status === 'cancelled';
    if (isCancelled) return { label: 'Dibatalkan', cls: 'bg-rose-500/10 text-rose-400 border-rose-500/20' };
    if (isPaid) return { label: 'Lunas', cls: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' };
    if (totalPaid > 0) return { label: 'DP / Sebagian', cls: 'bg-amber-500/10 text-amber-400 border-amber-500/20' };
    return { label: 'Belum Bayar', cls: 'bg-slate-500/10 text-slate-400 border-slate-500/20' };
  };

  const canPay = (ord) => {
    const status = orderStatusName(ord.status).toLowerCase();
    if (status === 'paid' || status === 'cancelled') return false;
    const totalPaid = (ord.downPayment || 0) + (ord.payments || []).reduce((s, p) => s + p.amount, 0);
    return totalPaid < (ord.totalAmount || 0);
  };

  const getRemainingAmount = (ord) => {
    const totalPaid = (ord.downPayment || 0) + (ord.payments || []).reduce((s, p) => s + p.amount, 0);
    return Math.max(0, (ord.totalAmount || 0) - totalPaid);
  };

  return (
    <div className="space-y-6">
      {/* Top Banner */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold text-white tracking-tight">{BAGIAN_PENJUALAN[section].judul}</h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20 rounded-full">
              Kasir &amp; Pelanggan
            </span>
          </div>
          <p className="text-sm text-slate-400 mt-1">{BAGIAN_PENJUALAN[section].sub}</p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            onClick={onRefresh}
            disabled={loading}
            className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl transition-colors border border-slate-700/60"
          >
            {loading ? 'Memuat...' : 'Muat Ulang'}
          </button>
          <button
            onClick={openOrderModal}
            className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all"
          >
            <Plus className="w-4 h-4" />
            <span>Tambah Transaksi / Pesanan</span>
          </button>
          {bolehExport && (
            <>
              <input type="date" id="export-sales-from" className="px-2 py-1.5 bg-slate-800 text-slate-300 rounded-lg text-xs border border-slate-700" />
              <input type="date" id="export-sales-to" className="px-2 py-1.5 bg-slate-800 text-slate-300 rounded-lg text-xs border border-slate-700" />
              <button
                onClick={async () => {
                  const f = document.getElementById('export-sales-from')?.value || '';
                  const t = document.getElementById('export-sales-to')?.value || '';
                  if (!f || !t) { alert('Pilih tanggal mulai dan akhir'); return; }
                  try { await api.exportOrders(f, t, 'excel'); } catch (e) { alert('Gagal export: ' + e.message); }
                }}
                className="flex items-center gap-1.5 px-3 py-1.5 bg-emerald-700 hover:bg-emerald-600 text-white rounded-lg text-xs font-medium transition-colors"
              >
                <Download className="w-3.5 h-3.5" />
                <span>Export Excel</span>
              </button>
              <button
                onClick={async () => {
                  const f = document.getElementById('export-sales-from')?.value || '';
                  const t = document.getElementById('export-sales-to')?.value || '';
                  if (!f || !t) { alert('Pilih tanggal mulai dan akhir'); return; }
                  try { await api.exportOrders(f, t, 'pdf'); } catch (e) { alert('Gagal export: ' + e.message); }
                }}
                className="flex items-center gap-1.5 px-3 py-1.5 bg-rose-700 hover:bg-rose-600 text-white rounded-lg text-xs font-medium transition-colors"
              >
                <Download className="w-3.5 h-3.5" />
                <span>Export PDF</span>
              </button>
            </>
          )}
        </div>
      </div>

      {/* Bagian layar: setiap menu samping membuka bagian yang sesuai. */}
      <div className="flex items-center gap-2 border-b border-slate-800 pb-2 flex-wrap">
        {TAB_SALES.map((tab) => (
          <button
            key={tab.id}
            onClick={() => onViewChange?.(tab.id)}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-bold transition-all ${
              section === tab.id
                ? 'bg-amber-500/20 text-amber-400 border border-amber-500/30'
                : 'text-slate-400 hover:text-white'
            }`}
          >
            <tab.icon className="w-3.5 h-3.5" />
            <span>{tab.label}</span>
          </button>
        ))}
      </div>

      {/* Hasil cetak: pesan jujur tentang apa yang terjadi, termasuk tawaran jalur peramban */}
      {cetakPesan && (
        <div
          className={`flex flex-col sm:flex-row sm:items-center gap-3 p-4 rounded-xl border text-sm ${
            cetakPesan.jenis === 'ok'
              ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-300'
              : 'bg-rose-500/10 border-rose-500/20 text-rose-300'
          }`}
        >
          <span className="flex-1">{cetakPesan.teks}</span>
          <div className="flex items-center gap-2">
            {cetakPesan.jenis === 'galat' && (
              <button
                onClick={() => cetakLewatPeramban((orders || []).find((o) => o.id === cetakPesan.orderId))}
                className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs border border-slate-700"
              >
                Cetak lewat peramban
              </button>
            )}
            {cetakPesan.jenis === 'ok' && (
              <button
                onClick={() => kirimKePrinter((orders || []).find((o) => o.id === cetakPesan.orderId), true)}
                className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs border border-slate-700"
              >
                Cetak ulang
              </button>
            )}
            <button
              onClick={() => setCetakPesan(null)}
              aria-label="Tutup pemberitahuan cetak"
              className="p-1.5 text-slate-400 hover:text-white"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>
      )}

      {/* Orders Table */}
      {section !== 'customers' && (
      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="relative">
              <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
              <input
                type="text"
                placeholder="Cari nama pelanggan atau deskripsi..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="pl-9 pr-3 py-1.5 bg-slate-950 border border-slate-800 rounded-lg text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-amber-500/50 w-72"
              />
            </div>
          </div>
          <div className="text-xs text-slate-400">
            Total: <strong className="text-white">{displayOrders.length} Pesanan</strong>
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
              <tr>
                <th className="py-3 px-4 font-semibold">Nomor Nota / Faktur</th>
                <th className="py-3 px-4 font-semibold">Nama Pembeli / Instansi</th>
                <th className="py-3 px-4 font-semibold">Deskripsi</th>
                {section === 'preorders' && <th className="py-3 px-4 font-semibold">Jadwal Ambil</th>}
                <th className="py-3 px-4 font-semibold text-right">Total</th>
                <th className="py-3 px-4 font-semibold text-right">Sisa</th>
                <th className="py-3 px-4 font-semibold text-center">Status</th>
                <th className="py-3 px-4 font-semibold text-right">Tindakan</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60">
              {displayOrders.length === 0 && (
                <tr>
                  <td colSpan={section === 'preorders' ? 8 : 7} className="py-12 text-center text-slate-500">
                    {search
                      ? 'Tidak ada pesanan yang cocok dengan pencarian.'
                      : section === 'receivables'
                        ? 'Tidak ada pesanan dengan sisa tagihan — semua pesanan sudah lunas atau dibatalkan.'
                        : section === 'preorders'
                          ? 'Belum ada pesanan terjadwal dengan jadwal ambil hari ini atau sesudahnya.'
                          : 'Belum ada pesanan. Klik "Tambah Transaksi / Pesanan" untuk memulai.'}
                  </td>
                </tr>
              )}
              {barisPesanan.tampil.map((ord) => {
                const badge = getStatusBadge(ord);
                const remaining = getRemainingAmount(ord);
                return (
                  <tr key={ord.id} className="hover:bg-slate-800/40 transition-colors">
                    <td className="py-3.5 px-4 font-mono font-bold text-amber-400">
                      {ord.orderNumber || `#${ord.id}`}
                    </td>
                    <td className="py-3.5 px-4">
                      <div className="font-semibold text-white">{ord.customerNameSnapshot || ord.customerName}</div>
                      <div className="text-[11px] text-slate-400">{ord.phoneSnapshot || ord.phone || '-'}</div>
                    </td>
                    <td className="py-3.5 px-4 text-slate-300 max-w-[200px] truncate">
                      {ord.description || '-'}
                    </td>
                    {section === 'preorders' && (
                      <td className="py-3.5 px-4 text-slate-300">{formatDate(ord.pickupDate)}</td>
                    )}
                    <td className="py-3.5 px-4 text-right font-mono font-bold text-white text-sm">
                      {formatRupiah(ord.totalAmount)}
                    </td>
                    <td className="py-3.5 px-4 text-right font-mono text-sm">
                      <span className={remaining > 0 ? 'text-amber-400' : 'text-emerald-400'}>
                        {formatRupiah(remaining)}
                      </span>
                    </td>
                    <td className="py-3.5 px-4 text-center">
                      <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${badge.cls}`}>
                        {badge.label === 'Lunas' ? <CheckCircle2 className="w-3 h-3" /> : <Clock className="w-3 h-3" />}
                        <span>{badge.label}</span>
                      </span>
                    </td>
                    <td className="py-3.5 px-4 text-right">
                      <div className="flex items-center justify-end gap-1.5">
                        {canPay(ord) && (
                          <button
                            onClick={() => openPayModal(ord)}
                            className="inline-flex items-center gap-1 px-2.5 py-1 bg-emerald-600/20 hover:bg-emerald-600/40 text-emerald-400 border border-emerald-500/30 rounded-lg text-xs font-medium transition-colors"
                          >
                            <CreditCard className="w-3.5 h-3.5" />
                            <span>Bayar</span>
                          </button>
                        )}
                        <button
                          onClick={() => kirimKePrinter(ord)}
                          disabled={sedangCetak}
                          className="inline-flex items-center gap-1 px-2.5 py-1 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg text-xs font-medium transition-colors disabled:opacity-50"
                        >
                          <Receipt className="w-3.5 h-3.5" />
                          <span>{sedangCetak ? 'Mengirim...' : 'Cetak Struk'}</span>
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        {barisPesanan.adaLagi && (
          <div className="px-4 py-3 border-t border-slate-800 text-center">
            <button
              onClick={barisPesanan.tampilkanLagi}
              className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
            >
              Tampilkan 100 pesanan berikutnya ({barisPesanan.jumlah - barisPesanan.tampil.length} tersisa)
            </button>
          </div>
        )}
      </div>
      )}

      {/* T9 — Customers Table: hanya pada bagian Pelanggan */}
      {section === 'customers' && (
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
          <div className="p-4 border-b border-slate-800 flex items-center justify-between flex-wrap gap-2">
            <h2 className="text-sm font-bold text-white flex items-center gap-2">
              <UserCheck className="w-4 h-4 text-amber-400" />
              Daftar Pelanggan ({daftarPelanggan.length}
              {totalPelanggan > daftarPelanggan.length ? ` dari ${totalPelanggan}` : ''})
            </h2>
            <div className="relative">
              <Search className="w-3.5 h-3.5 text-slate-500 absolute left-2.5 top-1/2 -translate-y-1/2" />
              <input
                value={cariPelanggan}
                onChange={(e) => setCariPelanggan(e.target.value)}
                placeholder="Cari nama atau telepon…"
                className="pl-8 pr-2.5 py-1.5 bg-slate-950 text-slate-200 rounded-lg text-xs border border-slate-800 focus:outline-none focus:border-blue-500 w-56"
              />
            </div>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-3 px-4 font-semibold">Nama</th>
                  <th className="py-3 px-4 font-semibold">Telpon</th>
                  <th className="py-3 px-4 font-semibold">Email</th>
                  <th className="py-3 px-4 font-semibold text-right">Poin Loyalitas</th>
                  <th className="py-3 px-4 font-semibold">Catatan</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {daftarPelanggan.length === 0 && (
                  <tr>
                    <td colSpan={5} className="py-12 text-center text-slate-500">
                      {cariPelanggan.trim()
                        ? 'Tidak ada pelanggan yang cocok dengan pencarian.'
                        : 'Belum ada pelanggan tersimpan; pelanggan tercatat saat pesanan pertama dibuat.'}
                    </td>
                  </tr>
                )}
                {daftarPelanggan.map((c) => (
                  <tr key={c.id} className="hover:bg-slate-800/40 transition-colors">
                    <td className="py-3 px-4 font-semibold text-white">{c.name}</td>
                    <td className="py-3 px-4 text-slate-300">{c.phone || '-'}</td>
                    <td className="py-3 px-4 text-slate-300">{c.email || '-'}</td>
                    <td className="py-3 px-4 text-right font-mono text-amber-400">{c.loyaltyPoints ?? 0}</td>
                    <td className="py-3 px-4 text-slate-400">{c.notes || '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {(memuatPelanggan || galatPelanggan || totalPelanggan > daftarPelanggan.length) && (
            <div className="px-4 py-2.5 border-t border-slate-800 flex items-center justify-between gap-2">
              <span className={`text-[11px] ${galatPelanggan ? 'text-rose-400' : 'text-slate-500'}`}>
                {galatPelanggan || (memuatPelanggan
                  ? 'Memuat pelanggan…'
                  : `Menampilkan ${daftarPelanggan.length} dari ${totalPelanggan} pelanggan.`)}
              </span>
              {!galatPelanggan && totalPelanggan > daftarPelanggan.length && (
                <button
                  onClick={muatPelangganLagi}
                  disabled={memuatPelanggan}
                  className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors disabled:opacity-50"
                >
                  Muat lagi
                </button>
              )}
            </div>
          )}
        </div>
      )}

      {/* ── K1 Modal: Tambah Transaksi / Pesanan ─────────────────────────────── */}
      {showOrderModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg shadow-2xl overflow-hidden">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <div>
                <h3 className="text-base font-bold text-white">Tambah Transaksi / Pesanan Baru</h3>
                <p className="text-xs text-slate-400 mt-0.5">Isi detail pesanan pelanggan</p>
              </div>
              <button
                onClick={() => setShowOrderModal(false)}
                className="p-1.5 text-slate-400 hover:text-white hover:bg-slate-800 rounded-lg transition-colors"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
            <form onSubmit={handleOrderSubmit} className="p-5 space-y-4 max-h-[70vh] overflow-y-auto">
              {orderError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
                  {orderError}
                </div>
              )}

              <div className="grid grid-cols-2 gap-3">
                <div className="col-span-2">
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <User className="w-3 h-3" /> Nama Pelanggan <span className="text-rose-400">*</span>
                  </label>
                  <input
                    type="text"
                    value={orderForm.customerName}
                    onChange={(e) => setOrderForm({ ...orderForm, customerName: e.target.value })}
                    placeholder="Nama pelanggan atau instansi..."
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                    required
                  />
                </div>

                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <Phone className="w-3 h-3" /> Nomor Telpon
                  </label>
                  <input
                    type="text"
                    value={orderForm.phone}
                    onChange={(e) => setOrderForm({ ...orderForm, phone: e.target.value })}
                    placeholder="08xx-xxxx-xxxx"
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  />
                </div>

                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <Calendar className="w-3 h-3" /> Tanggal Pickup <span className="text-rose-400">*</span>
                  </label>
                  <input
                    type="date"
                    value={orderForm.pickupDate}
                    onChange={(e) => setOrderForm({ ...orderForm, pickupDate: e.target.value })}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                    required
                  />
                </div>

                <div className="col-span-2">
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <FileText className="w-3 h-3" /> Deskripsi Pesanan <span className="text-rose-400">*</span>
                  </label>
                  <textarea
                    value={orderForm.description}
                    onChange={(e) => setOrderForm({ ...orderForm, description: e.target.value })}
                    placeholder="Contoh: 100 roti tawar, 50 croissant coklat..."
                    rows={2}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors resize-none"
                    required
                  />
                </div>

                <div className="col-span-2">
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <ScanLine className="w-3 h-3" /> Pindai Barcode Produk
                  </label>
                  <input
                    type="text"
                    value={kodePindai}
                    onChange={(e) => setKodePindai(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') {
                        e.preventDefault();
                        pindaiBarcode();
                      }
                    }}
                    placeholder="Fokuskan di sini, lalu tembakkan pemindai (akhiran Enter)..."
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-sky-500/60 transition-colors"
                  />
                  <p className="text-[11px] text-slate-500 mt-1">
                    Hanya produk yang punya barcode di katalog yang bisa dipilih lewat pindai.
                    Pemindai harus mengirim Enter di akhir kode.
                  </p>
                </div>

                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <Package className="w-3 h-3" /> Produk
                  </label>
                  <select
                    value={orderForm.productTemplateId}
                    onChange={(e) => {
                      const productTemplateId = e.target.value;
                      const total = hitungTotalKatalog(productTemplateId, orderForm.qty);
                      setOrderForm({
                        ...orderForm,
                        productTemplateId,
                        ...(bolehUbahTotal ? {} : { totalAmount: total })
                      });
                    }}
                    required={!bolehUbahTotal}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  >
                    <option value="">{bolehUbahTotal ? '-- Pilih Produk (opsional) --' : '-- Pilih Produk (wajib) --'}</option>
                    {(products || []).map((p) => (
                      <option key={p.id} value={p.id}>{p.name}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Qty</label>
                  <input
                    type="number"
                    min={1}
                    value={orderForm.qty}
                    onChange={(e) => {
                      const qty = e.target.value;
                      const total = hitungTotalKatalog(orderForm.productTemplateId, qty);
                      setOrderForm({
                        ...orderForm,
                        qty,
                        ...(bolehUbahTotal ? {} : { totalAmount: total })
                      });
                    }}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  />
                </div>

                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <DollarSign className="w-3 h-3" /> Total (Rp) <span className="text-rose-400">*</span>
                  </label>
                  <input
                    type="number"
                    min={1}
                    value={orderForm.totalAmount}
                    onChange={(e) => setOrderForm({ ...orderForm, totalAmount: e.target.value })}
                    placeholder="Nominal dalam Rupiah..."
                    readOnly={!bolehUbahTotal}
                    title={bolehUbahTotal ? undefined : 'Mengikuti harga katalog produk dikali jumlah unit.'}
                    className={`w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors ${bolehUbahTotal ? '' : 'opacity-80 cursor-not-allowed'}`}
                    required
                  />
                  {!bolehUbahTotal && (
                    <p className="text-[11px] text-slate-500 mt-1">
                      Total dihitung dari harga katalog × jumlah unit. Perubahan harga dilakukan Admin lewat katalog produk.
                    </p>
                  )}
                </div>

                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <DollarSign className="w-3 h-3" /> DP / Uang Muka (Rp)
                  </label>
                  <input
                    type="number"
                    min={0}
                    value={orderForm.downPayment}
                    onChange={(e) => setOrderForm({ ...orderForm, downPayment: e.target.value })}
                    placeholder="0 jika belum ada DP"
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  />
                </div>

                <div className="col-span-2">
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Catatan Tambahan</label>
                  <textarea
                    value={orderForm.notes}
                    onChange={(e) => setOrderForm({ ...orderForm, notes: e.target.value })}
                    placeholder="Catatan khusus pesanan (opsional)..."
                    rows={2}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors resize-none"
                  />
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setShowOrderModal(false)}
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-sm transition-colors"
                >
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={orderSaving}
                  className="px-5 py-2 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all"
                >
                  {orderSaving ? 'Menyimpan...' : 'Simpan Pesanan'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ── T6 Modal: Tambah Pembayaran ───────────────────────────────────────── */}
      {payOrder && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-sm shadow-2xl overflow-hidden">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <div>
                <h3 className="text-base font-bold text-white">Catat Pembayaran</h3>
                <p className="text-xs text-slate-400 mt-0.5">{payOrder.orderNumber || `#${payOrder.id}`} — {payOrder.customerNameSnapshot || payOrder.customerName}</p>
              </div>
              <button
                onClick={() => setPayOrder(null)}
                className="p-1.5 text-slate-400 hover:text-white hover:bg-slate-800 rounded-lg transition-colors"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
            <form onSubmit={handlePaySubmit} className="p-5 space-y-4">
              {payError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
                  {payError}
                </div>
              )}

              {/* Payment summary */}
              <div className="bg-slate-950/60 rounded-xl p-3 space-y-1.5 text-xs">
                <div className="flex justify-between">
                  <span className="text-slate-400">Total Pesanan</span>
                  <span className="text-white font-mono">{formatRupiah(payOrder.totalAmount)}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-slate-400">DP / Uang Muka</span>
                  <span className="text-white font-mono">{formatRupiah(payOrder.downPayment || 0)}</span>
                </div>
                {(payOrder.payments || []).length > 0 && (
                  <div className="flex justify-between">
                    <span className="text-slate-400">Pembayaran Sebelumnya</span>
                    <span className="text-white font-mono">{formatRupiah((payOrder.payments || []).reduce((s, p) => s + p.amount, 0))}</span>
                  </div>
                )}
                <div className="flex justify-between border-t border-slate-800 pt-1.5 font-semibold">
                  <span className="text-amber-400">Sisa Tagihan</span>
                  <span className="text-amber-400 font-mono">{formatRupiah(getRemainingAmount(payOrder))}</span>
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Nominal Pembayaran (Rp) <span className="text-rose-400">*</span>
                </label>
                <input
                  type="number"
                  min={1}
                  value={payForm.amount}
                  onChange={(e) => setPayForm({ ...payForm, amount: e.target.value })}
                  placeholder="Nominal yang dibayar..."
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                />
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Metode Pembayaran</label>
                <select
                  value={payForm.method}
                  onChange={(e) => setPayForm({ ...payForm, method: e.target.value })}
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                >
                  <option value="Tunai">Tunai</option>
                  <option value="Transfer">Transfer Bank</option>
                  <option value="QRIS">QRIS</option>
                  <option value="Kartu Debit">Kartu Debit</option>
                </select>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Catatan (opsional)</label>
                <input
                  type="text"
                  value={payForm.notes}
                  onChange={(e) => setPayForm({ ...payForm, notes: e.target.value })}
                  placeholder="Keterangan tambahan..."
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                />
              </div>

              <div className="flex justify-end gap-2 pt-2 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setPayOrder(null)}
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-sm transition-colors"
                >
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={paySaving}
                  className="px-5 py-2 bg-gradient-to-r from-emerald-500 to-emerald-600 hover:from-emerald-400 hover:to-emerald-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-emerald-500/20 transition-all"
                >
                  {paySaving ? 'Menyimpan...' : 'Catat Pembayaran'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
