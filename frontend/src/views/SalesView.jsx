import React, { useEffect, useRef, useState } from 'react';
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
import { orderStatusName } from '../utils/enums';
import { formatMinor, fromMinor, toMinor, formatDate, formatDateTime, tanggalTokoISO } from '../utils/money';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';
import { bolehAksi } from '../utils/kapabilitas';
import { statusPerangkatSingkat } from '../utils/statusPerangkat';

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

// ── Pratinjau tagihan ────────────────────────────────────────────────────────────
// Cerminan aturan pembukuan server (diskon, PPN, pembulatan) dalam satuan minor (sen). Angka yang
// dilihat kasir sebelum menekan Simpan adalah angka yang akan dibukukan, jadi pembulatannya harus
// sama persis: server menghitung dengan desimal dan membulatkan setengah Rupiah ke atas, sedangkan
// peramban hanya punya angka biner. Karena itu setiap perkalian persen diubah lebih dulu ke bilangan
// bulat (permil) lalu dibagi dengan pembulatan eksplisit, bukan dengan Math.round biasa.
const SATU_RUPIAH = 100;
// Pembagi setiap perkalian persen: persen disimpan sebagai permil (× 1000) dan dibagi seratus
// (× 100) sehingga keduanya digabung menjadi 100000 dan perhitungannya tidak pernah menyentuh
// angka pecahan.
const SKALA_PERSSEN = 100000;
const TARIF_PPN_BAWAAN = 11; // tarif yang dipakai server bila PPN aktif sementara tarifnya kosong

// a dan b bilangan bulat non-negatif: a/b dibulatkan setengah ke atas, sama seperti
// MidpointRounding.AwayFromZero yang dipakai server.
const bagiBulat = (a, b) => {
  const q = Math.floor(a / b);
  return (a - q * b) * 2 >= b ? q + 1 : q;
};

const keRupiahPenuh = (sen) => bagiBulat(Math.round(sen), SATU_RUPIAH) * SATU_RUPIAH;

const kePermil = (persen) => Math.round((Number(persen) || 0) * 1000);

// Bendera pengaturan dikirim sebagai teks ("0"/"1", sebagian jalur lama "true"/"false").
const flagHidup = (nilai) => nilai === '1' || String(nilai ?? '').toLowerCase() === 'true';

// null berarti peran ini tidak bisa membaca pengaturan toko: layar tidak tahu apakah toko memungut
// PPN, dan pratinjau tidak boleh mengarang baris yang mungkin tidak terjadi.
const pajakDariPengaturan = (settings) => {
  if (!settings || settings.tax_enabled == null) return null;
  if (!flagHidup(settings.tax_enabled)) return { aktif: false, tarifPersen: 0, inklusif: false };
  // Tarif 0 persen sah dan berarti tidak ada PPN; hanya nilai kosong/rusak yang jatuh ke tarif
  // bawaan server, jadi pemisahannya dibaca dari teksnya, bukan dari angka 0 hasil konversi.
  const mentah = String(settings.tax_rate_percent ?? '').trim();
  const tarif = Number(mentah);
  return {
    aktif: true,
    tarifPersen: mentah === '' || !Number.isFinite(tarif) ? TARIF_PPN_BAWAAN : Math.max(0, tarif),
    inklusif: flagHidup(settings.tax_inclusive),
  };
};

const pratinjauTagihan = ({ nilaiBarangRupiah, diskonMode, diskonNilai, pajak }) => {
  const subtotal = Math.max(0, toMinor(nilaiBarangRupiah));
  const persen = diskonMode === 'persen'
    ? Math.min(100, Math.max(0, parseFloat(diskonNilai) || 0))
    : 0;
  const diskon = keRupiahPenuh(persen > 0
    ? bagiBulat(subtotal * kePermil(persen), SKALA_PERSSEN)
    : toMinor(diskonMode === 'nominal' ? parseFloat(diskonNilai) || 0 : 0));
  const diskonTerpakai = Math.min(diskon, subtotal);
  const tagihanSebelumPpn = subtotal - diskonTerpakai;

  if (!pajak || !pajak.aktif || pajak.tarifPersen <= 0) {
    return {
      subtotal,
      diskon: diskonTerpakai,
      ppn: pajak ? 0 : null,
      total: tagihanSebelumPpn,
    };
  }

  const tarif = kePermil(pajak.tarifPersen);
  // Harga katalog sudah memuat PPN: DPP = tagihan / (1 + tarif), PPN selisihnya, sehingga total
  // tagihan tidak berubah — hanya sebagian berpindah dari pendapatan ke kewajiban pajak.
  if (pajak.inklusif) {
    const dpp = keRupiahPenuh(bagiBulat(tagihanSebelumPpn * SKALA_PERSSEN, SKALA_PERSSEN + tarif));
    return { subtotal, diskon: diskonTerpakai, ppn: tagihanSebelumPpn - dpp, total: tagihanSebelumPpn };
  }

  const ppn = keRupiahPenuh(bagiBulat(tagihanSebelumPpn * tarif, SKALA_PERSSEN));
  return { subtotal, diskon: diskonTerpakai, ppn, total: tagihanSebelumPpn + ppn };
};

// Rincian uang sebuah pesanan dari server. Pesanan yang dibuat sebelum fitur diskon/PPN berjalan
// tidak punya kolomnya, jadi rincian hanya ditampilkan bila angkanya benar-benar ada.
const rincianPesanan = (ord) => (
  ord && ord.subtotalAmount != null && ord.taxAmount != null
    ? {
      subtotal: ord.subtotalAmount,
      diskon: ord.discountAmount || 0,
      diskonPersen: ord.discountPercent,
      ppn: ord.taxAmount,
      tarif: ord.taxRatePercent,
      inklusif: !!ord.taxInclusive,
    }
    : null
);

// Label PPN memakai tarif yang tersimpan di pesanan, bukan tarif yang sedang aktif, dan menandai
// mode inklusif karena pajaknya tidak menambah tagihan. Layar dan struk memakai label yang sama.
const labelPpn = (rinci) =>
  `PPN${rinci.tarif != null ? ` ${rinci.tarif}%` : ''}${rinci.inklusif ? ' (termasuk)' : ''}`;

// Isian awal form pesanan; satu tempat supaya membuka ulang form tidak pernah meninggalkan
// diskon atau nilai barang dari pesanan sebelumnya.
const ISI_PESANAN_BAWAAN = {
  customerName: '',
  phone: '',
  description: '',
  pickupDate: '',
  productTemplateId: '',
  qty: 1,
  totalAmount: '',
  discountMode: 'nominal',
  discountValue: '',
  downPayment: '',
  notes: ''
};

export default function SalesView({
  orders, orderTotal = null, customers, customerTotal = null, products, user, brand, settings,
  onRefresh, onCreateOrder, onAddPayment, loading,
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
    // Ketikan berikutnya membatalkan permintaan yang masih berjalan, bukan hanya mengabaikan
    // jawabannya: server tidak perlu menyelesaikan pencarian yang sudah tidak dipakai.
    const pengendali = new AbortController();
    // Mengetik cepat tidak boleh menembakkan satu permintaan per huruf.
    const jeda = setTimeout(async () => {
      setMemuatPelanggan(true);
      setGalatPelanggan('');
      try {
        const hasil = await api.getCustomers({ search: kata, limit: 300, signal: pengendali.signal });
        if (batal) return;
        setHasilPelanggan(hasil.items ?? []);
        setTotalHasilPelanggan(hasil.total);
      } catch (err) {
        if (!batal && err.name !== 'AbortError') setGalatPelanggan(err.message || 'Gagal mencari pelanggan.');
      } finally {
        if (!batal) setMemuatPelanggan(false);
      }
    }, 250);
    return () => { batal = true; pengendali.abort(); clearTimeout(jeda); };
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

  // Daftar pesanan berhalaman: halaman pertama dari App, sisanya lewat "Muat lagi". Server
  // membatasi 100 baris per halaman, jadi tanpa tombol ini pesanan ke-101 dan seterusnya tidak
  // pernah terlihat — dan pengguna tidak tahu ada yang hilang.
  const [pesananTambahan, setPesananTambahan] = useState([]);
  const [memuatPesanan, setMemuatPesanan] = useState(false);
  const [galatPesanan, setGalatPesanan] = useState('');
  const batalMuatPesanan = useRef(null);
  useEffect(() => {
    setPesananTambahan([]);
    setMemuatPesanan(false);
    setGalatPesanan('');
    // Permintaan halaman lama milik daftar sebelumnya tidak boleh menyusup sesudah muat ulang.
    return () => batalMuatPesanan.current?.abort();
  }, [orders]);
  const daftarPesanan = pesananTambahan.length > 0 ? [...orders, ...pesananTambahan] : (orders || []);
  const totalPesanan = orderTotal ?? daftarPesanan.length;

  const muatPesananLagi = async () => {
    if (memuatPesanan) return;
    setMemuatPesanan(true);
    setGalatPesanan('');
    const pengendali = new AbortController();
    batalMuatPesanan.current = pengendali;
    try {
      const halaman = await api.getOrders({
        limit: 100, offset: daftarPesanan.length, signal: pengendali.signal,
      });
      const sudahAda = new Set(daftarPesanan.map((o) => o.id));
      const baru = (halaman.items ?? []).filter((o) => !sudahAda.has(o.id));
      setPesananTambahan((lama) => [...lama, ...baru]);
    } catch (err) {
      if (err.name !== 'AbortError') setGalatPesanan(err.message || 'Gagal memuat halaman berikutnya.');
    } finally {
      if (!pengendali.signal.aborted) setMemuatPesanan(false);
    }
  };

  // Total pesanan hanya boleh diketik bebas oleh yang berwenang mengatur harga (Owner/Admin).
  // Kasir memakai harga katalog, sama seperti pagar yang ditegakkan server.
  const bolehUbahTotal = bolehAksi(user, 'master-data-write');
  const bolehExport = bolehAksi(user, 'sales-export');
  // Diskon mengubah pendapatan yang dibukukan, jadi kapabilitas khusus dari server yang
  // menentukan boleh tidaknya; layar hanya menyembunyikan kolomnya dan menjelaskan kenapa,
  // tidak memindahkan aturannya ke sisi klien.
  const bolehDiskon = bolehAksi(user, 'sales-discount');
  const pajak = pajakDariPengaturan(settings);

  const hitungTotalKatalog = (productId, qty) => {
    const produk = (products || []).find((p) => String(p.id) === String(productId));
    const jumlah = parseFloat(qty || '1');
    if (!produk || isNaN(jumlah) || jumlah <= 0) return '';
    return String(Math.round(fromMinor(produk.sellingPrice) * jumlah));
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
  const [orderForm, setOrderForm] = useState({ ...ISI_PESANAN_BAWAAN });
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
  const [payForm, setPayForm] = useState({ amount: '', tender: '', method: 'Tunai', notes: '' });
  const {
    sedangSimpan: paySaving,
    mulai: mulaiSimpanPembayaran,
    selesai: selesaiSimpanPembayaran
  } = useKunciSimpan();
  const [payError, setPayError] = useState('');
  const [payKunci, setPayKunci] = useState('');

  // ── Serahkan pesanan ─────────────────────────────────────────────────────────
  const {
    sedangSimpan: serahkanSaving,
    mulai: mulaiSerahkan,
    selesai: selesaiSerahkan
  } = useKunciSimpan();
  const [serahkanPesan, setSerahkanPesan] = useState(null);

  // ── Kembalian dari server ────────────────────────────────────────────────────
  const [kembalianPesan, setKembalianPesan] = useState(null);

  // ── Modal: Batalkan Pesanan ──────────────────────────────────────────────────
  // Pembatalan mengembalikan stok dan membalik jurnal, jadi alasannya wajib diisi dan tombolnya
  // hanya tampil bagi peran yang memang diizinkan server (Owner/Admin).
  const [cancelOrder, setCancelOrder] = useState(null);
  const [cancelReason, setCancelReason] = useState('');
  const {
    sedangSimpan: cancelSaving,
    mulai: mulaiSimpanPembatalan,
    selesai: selesaiSimpanPembatalan
  } = useKunciSimpan();
  const [cancelError, setCancelError] = useState('');
  const [cancelKunci, setCancelKunci] = useState('');

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
  const displayOrders = daftarPesanan.filter((ord) => {
    const status = orderStatusName(ord.status);
    if (status === 'Cancelled' && (section === 'preorders' || section === 'receivables')) return false;

    if (section === 'preorders') {
      // Jadwal ambil adalah tanggal bisnis kalender toko; dibandingkan sebagai teks ISO supaya
      // zona perangkat tidak bisa menyembunyikan jadwal hari ini.
      const jadwal = String(ord.pickupDate ?? '').slice(0, 10);
      if (!jadwal || jadwal < tanggalTokoISO()) return false;
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

  // Seluruh baris yang sudah dimuat ditampilkan; sisa halaman diambil dari server lewat
  // "Muat lagi", jadi tidak ada baris yang tersembunyi tanpa cara membukanya.

  // ── K1 handlers ──────────────────────────────────────────────────────────────
  const openOrderModal = () => {
    setOrderForm({ ...ISI_PESANAN_BAWAAN });
    setOrderError('');
    setOrderKunci(kunciBaru('pesanan-baru'));
    setShowOrderModal(true);
  };

  // Pratinjau dihitung ulang setiap kali form berubah; angka inilah yang dipakai membandingkan DP,
  // supaya uang muka tidak diizinkan melewati tagihan yang benar-benar akan dibukukan server.
  const pratinjau = pratinjauTagihan({
    nilaiBarangRupiah: parseFloat(orderForm.totalAmount) || 0,
    diskonMode: bolehDiskon ? orderForm.discountMode : 'nominal',
    diskonNilai: bolehDiskon ? orderForm.discountValue : '',
    pajak,
  });

  const handleOrderSubmit = async (e) => {
    e.preventDefault();
    if (!orderForm.customerName.trim()) { setOrderError('Nama pelanggan wajib diisi.'); return; }
    if (!orderForm.description.trim()) { setOrderError('Deskripsi pesanan wajib diisi.'); return; }
    if (!bolehUbahTotal && !orderForm.productTemplateId) {
      setOrderError('Pilih produk dari katalog supaya harga diambil dari daftar harga.');
      return;
    }
    if (!orderForm.pickupDate) { setOrderError('Tanggal pickup wajib diisi.'); return; }
    const nilaiBarang = parseFloat(orderForm.totalAmount);
    const dp = parseInt(orderForm.downPayment || '0', 10);
    if (isNaN(nilaiBarang) || nilaiBarang <= 0) { setOrderError('Nilai barang harus lebih dari 0.'); return; }
    const diskon = parseFloat(orderForm.discountValue || '0');
    if (bolehDiskon && !isNaN(diskon) && diskon > 0) {
      if (orderForm.discountMode === 'persen' && (diskon < 0 || diskon > 100)) {
        setOrderError('Diskon persen harus antara 0 dan 100.');
        return;
      }
      if (orderForm.discountMode === 'nominal' && diskon > nilaiBarang) {
        setOrderError('Diskon tidak boleh melebihi nilai barang.');
        return;
      }
    }
    if (isNaN(dp) || dp < 0) { setOrderError('DP tidak valid.'); return; }
    if (dp > fromMinor(pratinjau.total)) { setOrderError('DP tidak boleh melebihi total tagihan.'); return; }
    const jumlah = parseFloat(orderForm.qty || '1');
    if (isNaN(jumlah) || jumlah <= 0) { setOrderError('Qty harus lebih dari 0.'); return; }

    // Kunci diambil setelah validasi agar form yang masih salah isi tidak terkunci, dan
    // ditahan di ref sinkron supaya klik kedua pada tick yang sama tidak ikut berangkat.
    if (!mulaiSimpanPesanan()) return;
    setOrderError('');
    const kirimDiskon = bolehDiskon && !isNaN(diskon) && diskon > 0;
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
        // Nilai barang sebelum diskon dan sebelum PPN; server yang menghitung tagihan akhirnya.
        totalAmount: toMinor(nilaiBarang),
        // Hanya mode aktif yang dikirim: mengirim keduanya memberi server dua niat yang berbeda.
        ...(kirimDiskon && orderForm.discountMode === 'persen' ? { discountPercent: diskon } : {}),
        ...(kirimDiskon && orderForm.discountMode === 'nominal' ? { discountAmount: toMinor(diskon) } : {}),
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
    setPayForm({
      amount: remaining > 0 ? String(fromMinor(remaining)) : '',
      tender: '',
      method: 'Tunai',
      notes: ''
    });
    setPayError('');
    setPayKunci(kunciBaru(`bayar-${ord.id}`));
  };

  const bayarSen = toMinor(parseFloat(payForm.amount) || 0);
  const adaTunai = payForm.method === 'Tunai' && String(payForm.tender).trim() !== '';
  const tunaiSen = adaTunai ? toMinor(parseFloat(payForm.tender) || 0) : null;
  const kembalian = tunaiSen !== null ? tunaiSen - bayarSen : null;
  const rincianBayar = payOrder ? rincianPesanan(payOrder) : null;

  const handlePaySubmit = async (e) => {
    e.preventDefault();
    const amount = parseInt(payForm.amount, 10);
    if (isNaN(amount) || amount <= 0) { setPayError('Nominal pembayaran harus lebih dari 0.'); return; }
    // Uang yang diterima tidak pernah boleh lebih kecil dari tagihannya: server menolaknya, dan
    // pembayaran yang ditolak di server tetap meninggalkan kesan "gagal mencatat" di layar.
    if (tunaiSen !== null && tunaiSen < bayarSen) {
      setPayError('Uang Diterima tidak boleh kurang dari nominal pembayaran.');
      return;
    }
    if (!mulaiSimpanPembayaran()) return;
    setPayError('');
    try {
      // Kunci yang sama dipakai dua lapis: kolom IdempotencyKey di baris pembayaran (jejak di
      // basis data) dan gerbang header di sisi server. Dulu nilainya memakai Date.now() sehingga
      // setiap klik dianggap niat baru dan pembayaran ganda tetap lolos.
      const hasil = await onAddPayment(payOrder.id, {
        amount: toMinor(amount),
        method: payForm.method,
        // Tunai saja yang membawa uang diterima; transfer dan kartu dibayar pas sehingga
        // mengirim nominal ekstra akan menyembunyikan pembayaran berlebih.
        ...(tunaiSen !== null ? { tenderAmount: tunaiSen } : {}),
        notes: payForm.notes.trim() || null,
        idempotencyKey: payKunci,
        idempotensi: payKunci
      });
      const idPesanan = payOrder.id;
      const orderNumber = payOrder.orderNumber || `#${idPesanan}`;
      setPayOrder(null);
      // Kembalian diambil dari jawaban server, bukan dari hitungan peramban, supaya uang yang
      // keluar dari laci sama dengan angka yang terbukukan.
      const kembaliServer = Number(hasil?.changeAmount);
      if (Number.isFinite(kembaliServer) && kembaliServer > 0) {
        setKembalianPesan({
          orderId: idPesanan,
          teks: `Pembayaran ${orderNumber} tercatat. Kembalian ${formatRupiah(kembaliServer)} diserahkan ke pelanggan.`
        });
      }
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

  // ── Serahkan pesanan ─────────────────────────────────────────────────────────
  // Penyerahan barang adalah titik pengakuan pendapatan sekaligus pemicu piutang untuk sisanya, jadi
  // aksinya sekali-arah dan server yang menolak bila pesanan sudah diserahkan, dibatalkan, atau
  // dikembalikan. Nomor pesanan dan sisa bayar dibaca dari data yang sudah ada di layar.
  const bolehSerahkan = (ord) =>
    !ord.deliveredAt && orderStatusName(ord.status).toLowerCase() !== 'cancelled';

  const handleSerahkan = async (ord) => {
    if (!mulaiSerahkan()) return;
    setSerahkanPesan(null);
    try {
      await api.deliverOrder(ord.id, kunciBaru(`serahkan-${ord.id}`));
      const sisa = getRemainingAmount(ord);
      const nomor = ord.orderNumber || `#${ord.id}`;
      setSerahkanPesan({
        jenis: 'ok',
        teks: sisa > 0
          // Sisa bayar pesanan yang sudah diserahkan ditagih sebagai piutang, istilah yang sama
          // dengan daftar piutang di layar Keuangan.
          ? `Pesanan ${nomor} diserahkan dan pendapatannya diakui. Sisa bayar ${formatRupiah(sisa)} tercatat sebagai piutang.`
          : `Pesanan ${nomor} diserahkan dan pendapatannya diakui.`,
      });
      await onRefresh?.();
    } catch (err) {
      setSerahkanPesan({ jenis: 'galat', teks: err.message || 'Pesanan gagal diserahkan.' });
    } finally {
      selesaiSerahkan();
    }
  };

  const openCancelModal = (ord) => {
    setCancelOrder(ord);
    setCancelReason('');
    setCancelError('');
    // Kunci tetap per pesanan: klik ganda tidak menjalankan pembalikan stok dan jurnal dua kali,
    // sementara percobaan ulang setelah galat validasi tetap memakai kunci yang sama.
    setCancelKunci(kunciBaru(`batal-pesanan-${ord.id}`));
  };

  const handleCancelSubmit = async (e) => {
    e.preventDefault();
    const alasan = cancelReason.trim();
    if (!alasan) { setCancelError('Alasan pembatalan wajib diisi.'); return; }
    if (!mulaiSimpanPembatalan()) return;
    setCancelError('');
    try {
      await api.updateOrderStatus(cancelOrder.id, 'Cancelled', alasan, cancelKunci);
      setCancelOrder(null);
      await onRefresh?.();
    } catch (err) {
      setCancelError(err.message || 'Gagal membatalkan pesanan.');
    } finally {
      selesaiSimpanPembatalan();
    }
  };

  // ── S8 — Cetak Struk ──────────────────────────────────────────────────────────  // Jalur utama: server merender struk dari pesanan yang tersimpan lalu mengirimnya ke printer
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
    const pickupDate = formatDate(ord.pickupDate);
    const createdAt = formatDateTime(ord.createdAt);
    const orderNumber = ord.orderNumber || '-';
    const phone = ord.phoneSnapshot || ord.phone || '-';

    // Rincian uang yang dicetak dialog peramban sama dengan isi struk thermal dari server:
    // nilai barang, diskon, PPN, lalu uang yang diserahkan dan kembaliannya. Pesanan lama yang
    // tidak punya angka barangnya memakai tagihan, supaya tidak tercetak "Nilai Barang Rp 0".
    const rinci = rincianPesanan(ord);
    const tagihan = ord.totalAmount || 0;
    const uangMuka = ord.downPayment || 0;
    const nilaiBarang = rinci && rinci.subtotal > 0 ? rinci.subtotal : tagihan;
    const diskon = rinci ? rinci.diskon : 0;
    const ppn = rinci ? rinci.ppn : 0;
    const pembayaranList = ord.payments || [];
    const dibayar = pembayaranList.reduce((s, p) => s + (p.amount || 0), 0);
    const kembalian = pembayaranList.reduce((s, p) => s + (p.changeAmount || 0), 0);
    const diserahkan = pembayaranList.reduce((s, p) => s + (p.tenderAmount ?? p.amount ?? 0), 0);
    const sisa = Math.max(0, tagihan - uangMuka - dibayar);
    const baris = (label, nilai, tebal = false) =>
      `<div class="row${tebal ? ' total' : ''}"><span class="label">${esc(label)}</span><span>${esc(formatRupiah(nilai))}</span></div>`;
    const barisDiskon =
      `<div class="row"><span class="label">Diskon</span><span>- ${esc(formatRupiah(diskon))}</span></div>`;
    const barisPpn =
      `<div class="row"><span class="label">${esc(labelPpn(rinci))}</span><span>${esc(formatRupiah(ppn))}</span></div>`;
    const rincianStruk = [
      baris('Nilai Barang', nilaiBarang),
      diskon > 0 ? barisDiskon : '',
      ppn > 0 ? barisPpn : '',
      baris('Total', tagihan, true),
      uangMuka > 0 ? baris('DP / Uang Muka', uangMuka) : '',
      dibayar > 0
        ? kembalian > 0
          ? baris('Tunai Diterima', diserahkan)
          : baris('Pembayaran', dibayar)
        : '',
      kembalian > 0 ? baris('Kembalian', kembalian) : '',
      baris('Sisa Tagihan', sisa),
      sisa === 0 ? '<p class="lunas">LUNAS</p>' : '',
    ].join('\n');

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
  .lunas { text-align: center; font-weight: bold; margin: 6px 0 0; }
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
  ${rincianStruk}
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

      {/* Kembalian yang dibukukan server: angka laci uang harus berasal dari jawaban yang sama */}
      {kembalianPesan && (
        <div
          className="flex flex-col sm:flex-row sm:items-center gap-3 p-4 rounded-xl border text-sm bg-emerald-500/10 border-emerald-500/20 text-emerald-300"
        >
          <span className="flex-1">{kembalianPesan.teks}</span>
          <button
            onClick={() => setKembalianPesan(null)}
            aria-label="Tutup pemberitahuan kembalian"
            className="p-1.5 text-slate-400 hover:text-white"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* Hasil penyerahan pesanan: pendapatan diakui dan sisa bayar menjadi piutang */}
      {serahkanPesan && (
        <div
          className={`flex flex-col sm:flex-row sm:items-center gap-3 p-4 rounded-xl border text-sm ${
            serahkanPesan.jenis === 'ok'
              ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-300'
              : 'bg-rose-500/10 border-rose-500/20 text-rose-300'
          }`}
        >
          <span className="flex-1">{serahkanPesan.teks}</span>
          <button
            onClick={() => setSerahkanPesan(null)}
            aria-label="Tutup pemberitahuan penyerahan"
            className="p-1.5 text-slate-400 hover:text-white"
          >
            <X className="w-4 h-4" />
          </button>
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
            {totalPesanan > daftarPesanan.length && (
              <span className="ml-1 text-slate-500">
                (baru {daftarPesanan.length} dari {totalPesanan} dimuat)
              </span>
            )}
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
              {displayOrders.map((ord) => {
                const badge = getStatusBadge(ord);
                const remaining = getRemainingAmount(ord);
                const rinci = rincianPesanan(ord);
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
                      {/* Rincian uang pesanan: kolom lama tetap menampilkan tagihan, angka tambahan
                          di bawahnya hanya muncul bila server mengirim rinciannya. */}
                      {rinci && (rinci.diskon > 0 || rinci.ppn > 0) && (
                        <div className="text-[10px] font-sans font-normal text-slate-400 space-y-0.5">
                          {rinci.diskon > 0 && (
                            <div>
                              Diskon {formatRupiah(rinci.diskon)}
                              {rinci.diskonPersen ? ` (${rinci.diskonPersen}%)` : ''}
                            </div>
                          )}
                          {rinci.ppn > 0 && (
                            <div>{labelPpn(rinci)} {formatRupiah(rinci.ppn)}</div>
                          )}
                        </div>
                      )}
                    </td>
                    <td className="py-3.5 px-4 text-right font-mono text-sm">
                      <span className={remaining > 0 ? 'text-amber-400' : 'text-emerald-400'}>
                        {formatRupiah(remaining)}
                      </span>
                      {/* Barang sudah diserahkan tetapi bayarnya belum: sisanya sudah menjadi
                          piutang, istilah yang dipakai juga di layar Keuangan. */}
                      {remaining > 0 && ord.deliveredAt && (
                        <div className="text-[10px] font-sans text-slate-400">piutang</div>
                      )}
                    </td>
                    <td className="py-3.5 px-4 text-center">
                      <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${badge.cls}`}>
                        {badge.label === 'Lunas' ? <CheckCircle2 className="w-3 h-3" /> : <Clock className="w-3 h-3" />}
                        <span>{badge.label}</span>
                      </span>
                      {ord.deliveredAt && (
                        <div className="text-[10px] font-sans text-slate-400 mt-1">
                          Diserahkan {formatDateTime(ord.deliveredAt)}
                        </div>
                      )}
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
                        {bolehSerahkan(ord) && (
                          <button
                            onClick={() => handleSerahkan(ord)}
                            disabled={serahkanSaving}
                            title="Barang diterima pelanggan: pendapatan diakui dan sisa bayar menjadi piutang."
                            className="inline-flex items-center gap-1 px-2.5 py-1 bg-sky-600/20 hover:bg-sky-600/40 text-sky-400 border border-sky-500/30 rounded-lg text-xs font-medium transition-colors disabled:opacity-50"
                          >
                            <CheckCircle2 className="w-3.5 h-3.5" />
                            <span>{serahkanSaving ? 'Menyerahkan...' : 'Serahkan'}</span>
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
                        {bolehAksi(user, 'sales-cancel') && badge.label !== 'Dibatalkan' && (
                          <button
                            onClick={() => openCancelModal(ord)}
                            className="inline-flex items-center gap-1 px-2.5 py-1 bg-rose-600/20 hover:bg-rose-600/40 text-rose-400 border border-rose-500/30 rounded-lg text-xs font-medium transition-colors"
                          >
                            <X className="w-3.5 h-3.5" />
                            <span>Batalkan</span>
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        {(daftarPesanan.length < totalPesanan || galatPesanan) && (
          <div className="px-4 py-3 border-t border-slate-800 text-center space-y-1.5">
            {galatPesanan && <div className="text-[11px] text-rose-400">{galatPesanan}</div>}
            {daftarPesanan.length < totalPesanan && (
              <button
                onClick={muatPesananLagi}
                disabled={memuatPesanan}
                className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors disabled:opacity-50"
              >
                {memuatPesanan
                  ? 'Memuat…'
                  : `Muat lagi (${totalPesanan - daftarPesanan.length} pesanan lagi)`}
              </button>
            )}
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
                    <DollarSign className="w-3 h-3" /> Nilai Barang (Rp) <span className="text-rose-400">*</span>
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
                      Nilai barang dihitung dari harga katalog × jumlah unit. Perubahan harga dilakukan Admin lewat katalog produk.
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
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5 flex items-center gap-1">
                    <DollarSign className="w-3 h-3" /> Diskon
                  </label>
                  {bolehDiskon ? (
                    <>
                      <div className="flex items-center gap-2">
                        {/* Satu kolom, dua mode — mengikuti cara layar ini memilih bagian lewat
                            tombol kecil, bukan dua kolom yang membuat kasir mengisi keduanya. */}
                        <div className="flex rounded-xl border border-slate-800 overflow-hidden shrink-0">
                          {[{ id: 'nominal', label: 'Nominal' }, { id: 'persen', label: 'Persen' }].map((mode) => (
                            <button
                              key={mode.id}
                              type="button"
                              onClick={() => setOrderForm({ ...orderForm, discountMode: mode.id })}
                              className={`px-3 py-2 text-xs font-bold transition-colors ${
                                orderForm.discountMode === mode.id
                                  ? 'bg-amber-500/20 text-amber-400'
                                  : 'text-slate-400 hover:text-white'
                              }`}
                            >
                              {mode.label}
                            </button>
                          ))}
                        </div>
                        <input
                          type="number"
                          min={0}
                          max={orderForm.discountMode === 'persen' ? 100 : undefined}
                          step={orderForm.discountMode === 'persen' ? 'any' : undefined}
                          value={orderForm.discountValue}
                          onChange={(e) => setOrderForm({ ...orderForm, discountValue: e.target.value })}
                          placeholder="0"
                          className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                        />
                      </div>
                      <p className="text-[11px] text-slate-500 mt-1">
                        {orderForm.discountMode === 'persen'
                          ? 'Persen dihitung dari nilai barang; hasilnya dibulatkan ke Rupiah penuh.'
                          : 'Diskon dalam Rupiah, dibulatkan ke Rupiah penuh.'}
                      </p>
                    </>
                  ) : (
                    <p className="text-[11px] text-slate-400">
                      Diskon hanya untuk Admin — pesanan kasir memakai harga penuh.
                    </p>
                  )}
                </div>

                {/* Pratinjau tagihan: angka yang akan dibukukan server bila pesanan disimpan. */}
                <div className="col-span-2 bg-slate-950/60 rounded-xl p-3 space-y-1.5 text-xs">
                  <div className="flex justify-between">
                    <span className="text-slate-400">Nilai Barang</span>
                    <span className="text-white font-mono">{formatRupiah(pratinjau.subtotal)}</span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-slate-400">Diskon</span>
                    <span className={pratinjau.diskon > 0 ? 'text-rose-400 font-mono' : 'text-white font-mono'}>
                      {pratinjau.diskon > 0 ? `- ${formatRupiah(pratinjau.diskon)}` : formatRupiah(0)}
                    </span>
                  </div>
                  {pratinjau.ppn !== null && (
                    <div className="flex justify-between">
                      <span className="text-slate-400">
                        PPN{pajak?.aktif ? ` ${pajak.tarifPersen}%` : ''}
                        {pajak?.inklusif ? ' (termasuk)' : ''}
                      </span>
                      <span className="text-white font-mono">{formatRupiah(pratinjau.ppn)}</span>
                    </div>
                  )}
                  <div className="flex justify-between border-t border-slate-800 pt-1.5 font-semibold">
                    <span className="text-amber-400">Total Tagihan</span>
                    <span className="text-amber-400 font-mono">{formatRupiah(pratinjau.total)}</span>
                  </div>
                  {pratinjau.ppn === null && (
                    <p className="text-[11px] text-slate-500">
                      Peran ini tidak bisa membaca pengaturan PPN toko, jadi total di atas belum tentu
                      termasuk PPN — server yang menghitung tagihan akhirnya.
                    </p>
                  )}
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

      {/* ── Modal: Batalkan Pesanan ───────────────────────────────────────────── */}
      {cancelOrder && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <div>
                <h3 className="text-base font-bold text-white">Batalkan Pesanan</h3>
                <p className="text-xs text-slate-400 mt-0.5">
                  {cancelOrder.orderNumber || `#${cancelOrder.id}`} — {cancelOrder.customerNameSnapshot || cancelOrder.customerName}
                </p>
              </div>
              <button
                onClick={() => setCancelOrder(null)}
                aria-label="Tutup"
                className="p-1.5 text-slate-400 hover:text-white hover:bg-slate-800 rounded-lg transition-colors"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
            <form onSubmit={handleCancelSubmit} className="p-5 space-y-4">
              <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-300 text-xs leading-relaxed">
                Pembatalan mengembalikan stok produk jadi ke etalase dan membalik jurnal penjualan
                pesanan ini. Tindakan ini tidak bisa dibatalkan dari layar.
              </div>
              {cancelError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
                  {cancelError}
                </div>
              )}
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Alasan Pembatalan <span className="text-rose-400">*</span>
                </label>
                <textarea
                  value={cancelReason}
                  onChange={(e) => setCancelReason(e.target.value)}
                  rows={3}
                  placeholder="Contoh: pelanggan membatalkan pesanan lewat telepon…"
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-rose-500/60 transition-colors resize-none"
                  required
                />
              </div>
              <div className="flex justify-end gap-2 pt-2 border-t border-slate-800">
                <button
                  type="button"
                  onClick={() => setCancelOrder(null)}
                  className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-xl text-sm transition-colors"
                >
                  Kembali
                </button>
                <button
                  type="submit"
                  disabled={cancelSaving}
                  className="px-5 py-2 bg-gradient-to-r from-rose-500 to-rose-600 hover:from-rose-400 hover:to-rose-500 text-white font-bold rounded-xl text-sm shadow-lg shadow-rose-500/20 transition-all disabled:opacity-50"
                >
                  {cancelSaving ? 'Membatalkan...' : 'Batalkan Pesanan'}
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
                {rincianBayar && (
                  <>
                    <div className="flex justify-between">
                      <span className="text-slate-400">Nilai Barang</span>
                      <span className="text-white font-mono">{formatRupiah(rincianBayar.subtotal)}</span>
                    </div>
                    {rincianBayar.diskon > 0 && (
                      <div className="flex justify-between">
                        <span className="text-slate-400">
                          Diskon{rincianBayar.diskonPersen ? ` (${rincianBayar.diskonPersen}%)` : ''}
                        </span>
                        <span className="text-rose-400 font-mono">- {formatRupiah(rincianBayar.diskon)}</span>
                      </div>
                    )}
                    {rincianBayar.ppn > 0 && (
                      <div className="flex justify-between">
                        <span className="text-slate-400">PPN{rincianBayar.tarif != null ? ` ${rincianBayar.tarif}%` : ''}</span>
                        <span className="text-white font-mono">{formatRupiah(rincianBayar.ppn)}</span>
                      </div>
                    )}
                  </>
                )}
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
                  onChange={(e) => setPayForm({
                    ...payForm,
                    method: e.target.value,
                    // Uang diterima hanya ada pada pembayaran tunai; dipindah ke metode lain harus
                    // kosong supaya nominal ekstra tidak ikut terkirim ke server.
                    tender: e.target.value === 'Tunai' ? payForm.tender : '',
                  })}
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                >
                  <option value="Tunai">Tunai</option>
                  <option value="Transfer">Transfer Bank</option>
                  <option value="QRIS">QRIS</option>
                  <option value="Kartu Debit">Kartu Debit</option>
                </select>
              </div>

              {payForm.method === 'Tunai' && (
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Uang Diterima (Rp)</label>
                  <input
                    type="number"
                    min={0}
                    value={payForm.tender}
                    onChange={(e) => setPayForm({ ...payForm, tender: e.target.value })}
                    placeholder="Uang yang diserahkan pelanggan..."
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  />
                  {kembalian !== null && kembalian >= 0 && (
                    <div className="flex justify-between mt-2 text-xs font-semibold">
                      <span className="text-emerald-400">Kembalian</span>
                      <span className="text-emerald-400 font-mono">{formatRupiah(kembalian)}</span>
                    </div>
                  )}
                  {kembalian !== null && kembalian < 0 && (
                    <p className="text-[11px] text-rose-400 mt-1">
                      Uang Diterima kurang {formatRupiah(-kembalian)} dari nominal pembayaran.
                    </p>
                  )}
                </div>
              )}

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
