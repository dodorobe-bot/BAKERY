import React, { useMemo, useState } from 'react';
import {
  Warehouse,
  ArrowLeftRight,
  Boxes,
  RotateCw,
  Download,
  AlertTriangle,
  CheckCircle,
  Search,
  X,
  Send,
  Coins,
} from 'lucide-react';
import { api } from '../services/api';
import { formatMinor, formatQty, formatDateTime } from '../utils/money';
import { movementTypeLabel } from '../utils/enums';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';
import { useBarisBertahap } from '../utils/bertahap';

// Tiga menu pada grup Rantai Pasok tampil sebagai tiga bagian pada satu layar.
const SECTION_BY_MENU = {
  inventory_locations: 'stock',
  kitchen_transfer: 'movements',
  stock_catalog: 'catalog',
};
const MENU_BY_SECTION = Object.fromEntries(
  Object.entries(SECTION_BY_MENU).map(([menu, section]) => [section, menu])
);

// Kelas Tailwind harus tertulis penuh agar ikut terdeteksi saat build.
const LOCATION_TYPES = {
  Warehouse: { label: 'Gudang', chip: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' },
  Kitchen: { label: 'Dapur', chip: 'bg-amber-500/10 text-amber-400 border-amber-500/20' },
  Display: { label: 'Etalase', chip: 'bg-blue-500/10 text-blue-400 border-blue-500/20' },
  Default: { label: 'Lokasi', chip: 'bg-slate-500/10 text-slate-400 border-slate-500/20' },
};

const MOVEMENT_TONE = {
  Deduction: 'text-rose-400',
  Restock: 'text-emerald-400',
  Adjustment: 'text-slate-300',
  CancelRestore: 'text-amber-400',
  Transfer: 'text-sky-400',
};

export default function InventoryView({
  view,
  onViewChange,
  locations,
  ingredients,
  stockByLocation,
  movements,
  products,
  finishedGoods,
  loading,
  onRefresh,
  onTransferStock,
  canTransfer = true,
}) {
  const section = SECTION_BY_MENU[view] ?? 'stock';
  const [locationId, setLocationId] = useState(null);
  const [search, setSearch] = useState('');
  const [movementLocation, setMovementLocation] = useState('');
  const [transferOpen, setTransferOpen] = useState(false);
  const [transferError, setTransferError] = useState('');
  const { sedangSimpan: transferSaving, mulai: mulaiMutasi, selesai: selesaiMutasi } = useKunciSimpan();
  const [transferKunci, setTransferKunci] = useState('');
  const [transferForm, setTransferForm] = useState({
    fromLocationId: '',
    toLocationId: '',
    ingredientId: '',
    quantity: '',
    notes: '',
  });

  const activeLocations = locations ?? [];
  const currentLocationId = locationId ?? activeLocations[0]?.id ?? null;
  const currentLocation = activeLocations.find((l) => l.id === currentLocationId) ?? null;

  // Stok per lokasi datang dari LocationStock; harga & satuan minimum ikut dibawa baris stok,
  // bukan hanya dari master bahan, karena bahan nonaktif masih menyimpan stok yang nyata.
  const locationRows = stockByLocation?.[currentLocationId] ?? [];

  const totalByIngredient = useMemo(() => {
    const totals = {};
    Object.entries(stockByLocation ?? {}).forEach(([, rows]) => {
      (rows ?? []).forEach((row) => {
        totals[row.ingredientId] = (totals[row.ingredientId] ?? 0) + Number(row.quantity ?? 0);
      });
    });
    return totals;
  }, [stockByLocation]);

  const activeIngredients = (ingredients ?? []).filter((ing) => ing.isActive !== false);

  // Bahan yang sudah nonaktif di master tidak ikut daftar bahan, padahal stoknya masih ada di
  // lokasi. Barisnya tetap ditawarkan pada mutasi; tanpa ini, sisa stok itu terjebak selamanya.
  const bahanYatim = useMemo(() => {
    const dikenal = new Set((ingredients ?? []).map((ing) => ing.id));
    const map = new Map();
    Object.values(stockByLocation ?? {}).forEach((rows) => {
      (rows ?? []).forEach((r) => {
        if (!dikenal.has(r.ingredientId) && !map.has(r.ingredientId)) {
          map.set(r.ingredientId, {
            id: r.ingredientId,
            name: r.ingredientName ?? `Bahan ${r.ingredientId}`,
            unit: r.unit ?? '-',
            inactive: true,
          });
        }
      });
    });
    return [...map.values()];
  }, [ingredients, stockByLocation]);
  const bahanMutasi = activeIngredients.concat(bahanYatim);

  // Satu baris per bahan aktif agar stok nol juga terlihat, bukan hanya baris yang punya record.
  const stockRows = useMemo(() => {
    const byIngredient = new Map(locationRows.map((row) => [row.ingredientId, row]));
    const rows = activeIngredients.map((ing) => {
      const held = byIngredient.get(ing.id);
      const quantity = Number(held?.quantity ?? 0);
      const minStock = Number(ing.minStockAlert ?? held?.minStockAlert ?? 0);
      return {
        ingredientId: ing.id,
        key: held?.id ?? `absent-${ing.id}`,
        name: ing.name,
        unit: ing.unit ?? held?.unit ?? '-',
        quantity,
        minStock,
        priceMinor: Number(ing.pricePerUnit ?? 0),
        hasRecord: Boolean(held),
      };
    });
    activeIngredients.forEach((ing) => byIngredient.delete(ing.id));
    // Baris sisa: lokasi menyimpan bahan yang sudah nonaktif di master.
    byIngredient.forEach((held) => {
      rows.push({
        ingredientId: held.ingredientId,
        key: `orphan-${held.id}`,
        name: held.ingredientName ?? `Bahan ${held.ingredientId}`,
        unit: held.unit ?? '-',
        quantity: Number(held.quantity ?? 0),
        minStock: Number(held.minStockAlert ?? 0),
        priceMinor: Number(held.ingredientPricePerUnit ?? 0),
        hasRecord: true,
        inactive: true,
      });
    });
    return rows;
  }, [locationRows, activeIngredients]);

  const query = search.trim().toLowerCase();
  const filteredRows = query
    ? stockRows.filter((r) => r.name.toLowerCase().includes(query) || r.unit.toLowerCase().includes(query))
    : stockRows;

  // Tabel 400+ baris ditampilkan bertahap; pencarian dan nilai total tetap dihitung dari seluruh data.
  const barisStok = useBarisBertahap(filteredRows);

  const locationValue = stockRows.reduce((sum, r) => sum + r.quantity * r.priceMinor, 0);
  const recordedRows = stockRows.filter((r) => r.hasRecord).length;
  const belowMin = stockRows.filter((r) => r.quantity <= r.minStock && r.minStock > 0);
  const zeroEverywhere = activeIngredients.filter(
    (ing) => (totalByIngredient[ing.id] ?? 0) <= 0
  );

  const filteredMovements = useMemo(() => {
    const all = movements ?? [];
    if (!movementLocation) return all;
    return all.filter((m) => String(m.locationId) === String(movementLocation));
  }, [movements, movementLocation]);

  const barisProduk = useBarisBertahap(products ?? []);

  // Buku stok produk jadi terpisah dari bahan: barisnya per produk per lokasi.
  const stokProdukPerLokasi = useMemo(() => {
    const map = new Map();
    (finishedGoods ?? []).forEach((row) => {
      const isi = map.get(row.productTemplateId) ?? [];
      isi.push(row);
      map.set(row.productTemplateId, isi);
    });
    return map;
  }, [finishedGoods]);
  const barisMutasi = useBarisBertahap(filteredMovements);
  const barisBahan = useBarisBertahap(activeIngredients);

  const openTransfer = (prefill) => {
    setTransferError('');
    // Kunci per kali membuka form: dua klik "Simpan" hanya menghasilkan satu mutasi stok.
    setTransferKunci(kunciBaru('pindah-stok'));
    setTransferForm({
      fromLocationId: String(prefill?.fromLocationId ?? currentLocationId ?? activeLocations[0]?.id ?? ''),
      toLocationId: String(prefill?.toLocationId ?? activeLocations.find((l) => l.id !== prefill?.fromLocationId && l.id !== currentLocationId)?.id ?? ''),
      ingredientId: prefill?.ingredientId ? String(prefill.ingredientId) : '',
      quantity: '',
      notes: '',
    });
    setTransferOpen(true);
  };

  const selectedIngredient = activeIngredients.find((i) => String(i.id) === String(transferForm.ingredientId));
  const availableAtSource = selectedIngredient
    ? Number(stockByLocation?.[Number(transferForm.fromLocationId)]?.find(
        (row) => String(row.ingredientId) === String(selectedIngredient.id)
      )?.quantity ?? 0)
    : 0;

  const validateTransfer = () => {
    if (!transferForm.fromLocationId) return 'Pilih lokasi asal.';
    if (!transferForm.toLocationId) return 'Pilih lokasi tujuan.';
    if (transferForm.fromLocationId === transferForm.toLocationId)
      return 'Lokasi asal dan tujuan harus berbeda.';
    if (!selectedIngredient) return 'Pilih bahan yang akan dikirim.';
    const qty = Number(transferForm.quantity);
    if (!Number.isFinite(qty) || qty <= 0) return 'Jumlah harus angka lebih besar dari nol.';
    if (qty > availableAtSource)
      return `Jumlah melebihi stok tersedia di lokasi asal (${formatQty(availableAtSource)} ${selectedIngredient.unit}).`;
    return '';
  };

  const submitTransfer = async (event) => {
    event.preventDefault();
    const problem = validateTransfer();
    if (problem) { setTransferError(problem); return; }
    if (!mulaiMutasi()) return;
    setTransferError('');
    try {
      await onTransferStock({
        fromLocationId: Number(transferForm.fromLocationId),
        toLocationId: Number(transferForm.toLocationId),
        ingredientId: Number(selectedIngredient.id),
        quantity: Number(transferForm.quantity),
        notes: transferForm.notes || null,
        idempotensi: transferKunci,
      });
      setTransferOpen(false);
    } catch (err) {
      setTransferError(err.message || 'Gagal mencatat mutasi stok.');
    } finally {
      selesaiMutasi();
    }
  };

  const tabs = [
    { id: 'stock', label: 'Stok per Lokasi', icon: Warehouse },
    { id: 'movements', label: 'Mutasi Gudang → Dapur', icon: ArrowLeftRight },
    { id: 'catalog', label: 'Katalog Bahan & Produk Jadi', icon: Boxes },
  ];

  return (
    <div className="space-y-6">
      {/* Top banner */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold text-white tracking-tight">Rantai Pasok & Persediaan Multi-Lokasi</h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 rounded-full">
              {activeLocations.length} lokasi aktif
            </span>
          </div>
          <p className="text-sm text-slate-400 mt-1">
            Angka diambil dari LocationStock per lokasi. Tidak ada contoh data: tabel kosong berarti memang belum ada pencatatan.
          </p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            onClick={onRefresh}
            className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl transition-colors border border-slate-700/60"
            title="Muat Ulang Data"
          >
            <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
          {canTransfer && (
            <button
              onClick={() => openTransfer({ fromLocationId: currentLocationId })}
              disabled={activeLocations.length < 2 || activeIngredients.length === 0}
              className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-emerald-500 to-emerald-600 hover:from-emerald-400 hover:to-emerald-500 disabled:opacity-40 disabled:cursor-not-allowed text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-emerald-500/20 transition-all"
            >
              <ArrowLeftRight className="w-4 h-4" />
              <span>Kirim Bahan</span>
            </button>
          )}
        </div>
      </div>

      {/* Bagian */}
      <div className="flex items-center gap-2 border-b border-slate-800 pb-2 flex-wrap">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => onViewChange?.(MENU_BY_SECTION[tab.id])}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-bold transition-all ${
              section === tab.id
                ? 'bg-emerald-500/20 text-emerald-400 border border-emerald-500/30'
                : 'text-slate-400 hover:text-white'
            }`}
          >
            <tab.icon className="w-3.5 h-3.5" />
            <span>{tab.label}</span>
          </button>
        ))}
      </div>

      {activeLocations.length === 0 && (
        <div className="bg-amber-500/10 border border-amber-500/20 rounded-2xl p-4 text-xs text-amber-300">
          Belum ada lokasi penyimpanan aktif di basis data, jadi stok tidak dapat ditampilkan.
        </div>
      )}

      {section === 'stock' && currentLocation && (
        <>
          {/* Pemilih lokasi */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            {activeLocations.map((loc) => {
              const rows = stockByLocation?.[loc.id] ?? [];
              const tone = LOCATION_TYPES[loc.type] ?? LOCATION_TYPES.Default;
              const selected = loc.id === currentLocationId;
              return (
                <button
                  key={loc.id}
                  onClick={() => setLocationId(loc.id)}
                  className={`text-left p-4 rounded-2xl border transition-all ${
                    selected
                      ? 'bg-slate-900 border-emerald-500/40 shadow-lg shadow-emerald-500/10'
                      : 'bg-slate-900/60 border-slate-800 hover:border-slate-700'
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{loc.code}</span>
                    <span className={`text-[10px] font-semibold px-2 py-0.5 rounded-full border ${tone.chip}`}>
                      {tone.label}
                    </span>
                  </div>
                  <div className="text-sm font-bold text-white mt-1.5">{loc.name}</div>
                  <div className="text-[11px] text-slate-400 mt-1">
                    {rows.length} baris stok tercatat
                  </div>
                </button>
              );
            })}
          </div>

          {/* KPI lokasi */}
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Nilai persediaan lokasi ini</div>
              <div className="text-xl font-bold text-white mt-2 font-mono">{formatMinor(locationValue)}</div>
              <div className="text-[11px] text-slate-400 mt-1">
                dari {recordedRows} bahan dengan stok, harga master bahan
              </div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Baris stok</div>
              <div className="text-xl font-bold text-white mt-2 font-mono">{recordedRows}</div>
              <div className="text-[11px] text-slate-400 mt-1">dari {activeIngredients.length} bahan aktif</div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Di bawah minimum</div>
              <div className={`text-xl font-bold mt-2 font-mono ${belowMin.length ? 'text-rose-400' : 'text-emerald-400'}`}>
                {belowMin.length} bahan
              </div>
              <div className="text-[11px] text-slate-400 mt-1 truncate">
                {belowMin.length ? belowMin.map((r) => r.name).join(', ') : 'Semua di atas batas'}
              </div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Stok nol di semua lokasi</div>
              <div className={`text-xl font-bold mt-2 font-mono ${zeroEverywhere.length ? 'text-amber-400' : 'text-emerald-400'}`}>
                {zeroEverywhere.length} bahan
              </div>
              <div className="text-[11px] text-slate-400 mt-1 truncate">
                {zeroEverywhere.length ? zeroEverywhere.map((i) => i.name).join(', ') : 'Setiap bahan punya stok'}
              </div>
            </div>
          </div>

          {/* Tabel stok */}
          <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
            <div className="p-4 border-b border-slate-800 flex items-center justify-between flex-wrap gap-2">
              <h2 className="text-base font-semibold text-white flex items-center gap-2">
                <Warehouse className="w-4 h-4 text-emerald-400" />
                <span>Stok di {currentLocation.name}</span>
              </h2>
              <div className="flex items-center gap-2 flex-wrap">
                <div className="relative">
                  <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
                  <input
                    type="text"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Cari nama bahan..."
                    className="pl-9 pr-3 py-1.5 bg-slate-950 border border-slate-800 rounded-lg text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-emerald-500/50 w-56"
                  />
                </div>
                <button
                  onClick={() => api.exportStock('excel').catch((e) => alert('Gagal export: ' + e.message))}
                  className="flex items-center gap-1.5 px-3 py-1.5 bg-emerald-700 hover:bg-emerald-600 text-white rounded-lg text-xs font-medium transition-colors"
                >
                  <Download className="w-3.5 h-3.5" />
                  <span>Excel</span>
                </button>
                <button
                  onClick={() => api.exportStock('pdf').catch((e) => alert('Gagal export: ' + e.message))}
                  className="flex items-center gap-1.5 px-3 py-1.5 bg-rose-700 hover:bg-rose-600 text-white rounded-lg text-xs font-medium transition-colors"
                >
                  <Download className="w-3.5 h-3.5" />
                  <span>PDF</span>
                </button>
              </div>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs text-slate-300">
                <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                  <tr>
                    <th className="py-3 px-4 font-semibold">Bahan</th>
                    <th className="py-3 px-4 font-semibold">Satuan</th>
                    <th className="py-3 px-4 font-semibold text-right">Stok lokasi</th>
                    <th className="py-3 px-4 font-semibold text-right">Minimum</th>
                    <th className="py-3 px-4 font-semibold text-center">Cukup?</th>
                    <th className="py-3 px-4 font-semibold text-right">Harga satuan</th>
                    <th className="py-3 px-4 font-semibold text-right">Nilai stok</th>
                    <th className="py-3 px-4 font-semibold text-right">Aksi</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60">
                  {filteredRows.length === 0 && (
                    <tr>
                      <td colSpan={8} className="py-6 px-4 text-center text-slate-500">
                        {activeIngredients.length === 0
                          ? 'Belum ada bahan baku aktif. Tambahkan lewat menu Resep & Daftar Bahan Baku.'
                          : 'Tidak ada bahan yang cocok dengan pencarian.'}
                      </td>
                    </tr>
                  )}
                  {barisStok.tampil.map((row) => {
                    const low = row.minStock > 0 && row.quantity <= row.minStock;
                    return (
                      <tr key={row.key} className="hover:bg-slate-800/40 transition-colors">
                        <td className="py-3 px-4">
                          <div className="font-semibold text-white">{row.name}</div>
                          <div className="text-[11px] text-slate-500">
                            ID {row.ingredientId}{row.inactive ? ' • nonaktif di master' : ''}
                          </div>
                        </td>
                        <td className="py-3 px-4 font-mono">{row.unit}</td>
                        <td className="py-3 px-4 text-right font-mono font-bold text-white">
                          {formatQty(row.quantity)}
                          {!row.hasRecord && <span className="text-[10px] text-slate-500 font-sans"> (belum dicatat)</span>}
                        </td>
                        <td className="py-3 px-4 text-right font-mono text-slate-400">
                          {row.minStock > 0 ? formatQty(row.minStock) : '-'}
                        </td>
                        <td className="py-3 px-4 text-center">
                          {low ? (
                            <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold bg-rose-500/10 text-rose-400 border border-rose-500/20">
                              <AlertTriangle className="w-3 h-3" />
                              <span>Kurang</span>
                            </span>
                          ) : (
                            <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                              <CheckCircle className="w-3 h-3" />
                              <span>Cukup</span>
                            </span>
                          )}
                        </td>
                        <td className="py-3 px-4 text-right font-mono">
                          {row.priceMinor > 0 ? formatMinor(row.priceMinor) : <span className="text-slate-500">belum diisi</span>}
                        </td>
                        <td className="py-3 px-4 text-right font-mono text-white">
                          {formatMinor(row.quantity * row.priceMinor)}
                        </td>
                        <td className="py-3 px-4 text-right">
                          {canTransfer && (
                            <button
                              onClick={() => openTransfer({ fromLocationId: currentLocationId, ingredientId: row.ingredientId })}
                              disabled={row.quantity <= 0}
                              className="text-[11px] font-semibold text-emerald-400 hover:text-emerald-300 disabled:text-slate-600 disabled:cursor-not-allowed"
                            >
                              Kirim
                            </button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {barisStok.adaLagi && (
              <div className="px-4 py-3 border-t border-slate-800 text-center">
                <button
                  onClick={barisStok.tampilkanLagi}
                  className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                >
                  Tampilkan 100 baris berikutnya ({barisStok.jumlah - barisStok.tampil.length} tersisa)
                </button>
              </div>
            )}

            <div className="px-4 py-3 border-t border-slate-800 text-[11px] text-slate-500 leading-relaxed">
              Nilai stok memakai harga master bahan (<span className="text-slate-400">Ingredients.PricePerUnit</span>), sedangkan
              penerimaan barang memakai harga pada baris PO. Dua dasar penilaian ini belum disatukan, jadi angka di sini
              bisa berbeda dari nilai perolehan sebenarnya. Satuan bahan juga masih teks bebas pada master, belum daftar satuan baku.
            </div>
          </div>
        </>
      )}

      {section === 'movements' && (
        <>
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            {/* Formulir mutasi */}
            <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 space-y-4">
              <h2 className="text-base font-semibold text-white flex items-center gap-2">
                <ArrowLeftRight className="w-4 h-4 text-emerald-400" />
                <span>Kirim bahan antar lokasi</span>
              </h2>
              {canTransfer ? (
                <button
                  onClick={() => openTransfer({ fromLocationId: currentLocationId })}
                  className="w-full flex items-center justify-center gap-2 px-4 py-2.5 bg-gradient-to-r from-emerald-500 to-emerald-600 hover:from-emerald-400 hover:to-emerald-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-emerald-500/20 transition-all"
                >
                  <Send className="w-4 h-4" />
                  <span>Buka Formulir Mutasi</span>
                </button>
              ) : (
                <p className="text-[11px] text-slate-400">
                  Peran Anda dapat melihat mutasi, tetapi tidak dapat mencatat perpindahan stok.
                </p>
              )}
              <p className="text-[11px] text-slate-500">
                Setiap pengiriman menulis dua kaki mutasi dalam satu transaksi: pengurang di lokasi asal dan
                penambah di lokasi tujuan.
              </p>
            </div>

            {/* Pergerakan terakhir */}
            <div className="lg:col-span-2 bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
              <div className="p-4 border-b border-slate-800 flex items-center justify-between gap-2 flex-wrap">
                <h2 className="text-base font-semibold text-white">Pergerakan stok terakhir</h2>
                <select
                  value={movementLocation}
                  onChange={(e) => setMovementLocation(e.target.value)}
                  className="bg-slate-950 text-slate-200 border border-slate-800 rounded-lg px-2 py-1.5 text-xs focus:outline-none focus:border-emerald-500"
                >
                  <option value="">Semua lokasi</option>
                  {activeLocations.map((loc) => (
                    <option key={loc.id} value={loc.id}>{loc.name}</option>
                  ))}
                </select>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs text-slate-300">
                  <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="py-3 px-4 font-semibold">Waktu</th>
                      <th className="py-3 px-4 font-semibold">Jenis</th>
                      <th className="py-3 px-4 font-semibold">Bahan</th>
                      <th className="py-3 px-4 font-semibold text-right">Jumlah</th>
                      <th className="py-3 px-4 font-semibold">Sumber dokumen</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60">
                    {(filteredMovements ?? []).length === 0 && (
                      <tr>
                        <td colSpan={5} className="py-6 px-4 text-center text-slate-500">
                          Belum ada mutasi tercatat untuk filter ini.
                        </td>
                      </tr>
                    )}
                    {barisMutasi.tampil.map((m) => {
                      const positive = Number(m.quantity) > 0;
                      return (
                        <tr key={m.id} className="hover:bg-slate-800/40 transition-colors">
                          <td className="py-3 px-4 font-mono text-slate-400">{formatDateTime(m.createdAt)}</td>
                          <td className={`py-3 px-4 font-semibold ${MOVEMENT_TONE[m.movementType] ?? 'text-slate-300'}`}>
                            {movementTypeLabel(m.movementType)}
                          </td>
                          <td className="py-3 px-4">
                            <div className="font-semibold text-white">{m.ingredientName}</div>
                            <div className="text-[11px] text-slate-500">{m.locationName ?? 'lokasi tanpa catatan'}</div>
                          </td>
                          <td className="py-3 px-4 text-right font-mono font-bold">
                            {positive ? '+' : ''}{formatQty(m.quantity)} <span className="text-[11px] font-sans text-slate-400">{m.unit}</span>
                          </td>
                          <td className="py-3 px-4 font-mono text-[11px] text-slate-400">
                            {m.sourceType ? `${m.sourceType}${m.sourceId ? ` #${m.sourceId}` : ''}` : '-'}
                            {m.notes ? <div className="text-slate-500 font-sans">{m.notes}</div> : null}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
              {barisMutasi.adaLagi && (
                <div className="px-4 py-3 border-t border-slate-800 text-center">
                  <button
                    onClick={barisMutasi.tampilkanLagi}
                    className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                  >
                    Tampilkan 100 mutasi berikutnya ({barisMutasi.jumlah - barisMutasi.tampil.length} tersisa)
                  </button>
                </div>
              )}
            </div>
          </div>
        </>
      )}

      {section === 'catalog' && (
        <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
          {/* Master bahan */}
          <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
            <div className="p-4 border-b border-slate-800 flex items-center gap-2">
              <Coins className="w-4 h-4 text-emerald-400" />
              <h2 className="text-base font-semibold text-white">Katalog bahan baku</h2>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs text-slate-300">
                <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                  <tr>
                    <th className="py-3 px-4 font-semibold">Bahan</th>
                    <th className="py-3 px-4 font-semibold">Satuan</th>
                    <th className="py-3 px-4 font-semibold text-right">Harga satuan</th>
                    <th className="py-3 px-4 font-semibold text-right">Total semua lokasi</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60">
                  {activeIngredients.length === 0 && (
                    <tr><td colSpan={4} className="py-6 px-4 text-center text-slate-500">Belum ada bahan baku.</td></tr>
                  )}
                  {barisBahan.tampil.map((ing) => (
                    <tr key={ing.id} className="hover:bg-slate-800/40">
                      <td className="py-3 px-4 font-semibold text-white">{ing.name}<div className="text-[11px] text-slate-500 font-normal">ID {ing.id}</div></td>
                      <td className="py-3 px-4 font-mono">{ing.unit}</td>
                      <td className="py-3 px-4 text-right font-mono">{ing.pricePerUnit > 0 ? formatMinor(ing.pricePerUnit) : <span className="text-slate-500">belum diisi</span>}</td>
                      <td className="py-3 px-4 text-right font-mono text-white">{formatQty(totalByIngredient[ing.id] ?? 0)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {barisBahan.adaLagi && (
              <div className="px-4 py-3 border-t border-slate-800 text-center">
                <button
                  onClick={barisBahan.tampilkanLagi}
                  className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                >
                  Tampilkan 100 bahan berikutnya ({barisBahan.jumlah - barisBahan.tampil.length} tersisa)
                </button>
              </div>
            )}
          </div>

          {/* Produk jadi */}
          <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
            <div className="p-4 border-b border-slate-800 flex items-center gap-2">
              <Boxes className="w-4 h-4 text-amber-400" />
              <h2 className="text-base font-semibold text-white">Katalog produk jadi</h2>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs text-slate-300">
                <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                  <tr>
                    <th className="py-3 px-4 font-semibold">Produk</th>
                    <th className="py-3 px-4 font-semibold">Kategori</th>
                    <th className="py-3 px-4 font-semibold text-right">Harga jual</th>
                    <th className="py-3 px-4 font-semibold text-right">Bahan resep</th>
                    <th className="py-3 px-4 font-semibold text-right">Stok jadi per lokasi</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60">
                  {(products ?? []).length === 0 && (
                    <tr><td colSpan={5} className="py-6 px-4 text-center text-slate-500">Belum ada produk terdaftar.</td></tr>
                  )}
                  {barisProduk.tampil.map((p) => {
                    const saldo = stokProdukPerLokasi.get(p.id) ?? [];
                    const total = saldo.reduce((s, row) => s + Number(row.quantity || 0), 0);
                    return (
                      <tr key={p.id} className="hover:bg-slate-800/40">
                        <td className="py-3 px-4">
                          <div className="font-semibold text-white">{p.name}</div>
                          <div className="text-[11px] text-slate-500">{p.isActive === false ? 'nonaktif' : 'aktif'}</div>
                        </td>
                        <td className="py-3 px-4 text-slate-400">{p.category || '-'}</td>
                        <td className="py-3 px-4 text-right font-mono">{formatMinor(p.sellingPrice)}</td>
                        <td className="py-3 px-4 text-right font-mono">{(p.recipeLines ?? p.lines ?? []).length} baris</td>
                        <td className="py-3 px-4 text-right">
                          <div className="font-mono font-semibold text-white">{formatQty(total)}</div>
                          <div className="text-[11px] text-slate-500">
                            {saldo.length === 0
                              ? 'belum tercatat'
                              : saldo.map((row) => `${row.locationName} ${formatQty(row.quantity)}`).join(' · ')}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {barisProduk.adaLagi && (
              <div className="px-4 py-3 border-t border-slate-800 text-center">
                <button
                  onClick={barisProduk.tampilkanLagi}
                  className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                >
                  Tampilkan 100 produk berikutnya ({barisProduk.jumlah - barisProduk.tampil.length} tersisa)
                </button>
              </div>
            )}
            <div className="px-4 py-3 border-t border-slate-800 text-[11px] text-slate-500">
              Stok produk jadi dicatat di buku stok produk jadi per lokasi dan diperbarui oleh produksi,
              penjualan, pembatalan, serta retur. Bahan baku punya buku stok sendiri di tab Gudang.
            </div>
          </div>
        </div>
      )}

      {/* Formulir mutasi */}
      {transferOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg overflow-hidden shadow-2xl">
            <div className="p-4 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <ArrowLeftRight className="w-4 h-4 text-emerald-400" />
                <span>Formulir Mutasi Bahan</span>
              </h3>
              <button onClick={() => setTransferOpen(false)} className="text-slate-400 hover:text-white">
                <X className="w-4 h-4" />
              </button>
            </div>

            <form onSubmit={submitTransfer} className="p-5 space-y-4">
              {transferError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
                  {transferError}
                </div>
              )}

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1">Dari lokasi</label>
                  <select
                    value={transferForm.fromLocationId}
                    onChange={(e) => setTransferForm({ ...transferForm, fromLocationId: e.target.value })}
                    className="w-full bg-slate-950 text-slate-200 border border-slate-800 rounded-xl p-2.5 text-xs focus:outline-none focus:border-emerald-500"
                  >
                    <option value="">— Pilih —</option>
                    {activeLocations.map((loc) => (
                      <option key={loc.id} value={loc.id}>{loc.code} · {loc.name}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1">Ke lokasi</label>
                  <select
                    value={transferForm.toLocationId}
                    onChange={(e) => setTransferForm({ ...transferForm, toLocationId: e.target.value })}
                    className="w-full bg-slate-950 text-slate-200 border border-slate-800 rounded-xl p-2.5 text-xs focus:outline-none focus:border-emerald-500"
                  >
                    <option value="">— Pilih —</option>
                    {activeLocations.map((loc) => (
                      <option key={loc.id} value={loc.id}>{loc.code} · {loc.name}</option>
                    ))}
                  </select>
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1">Bahan</label>
                <select
                  value={transferForm.ingredientId}
                  onChange={(e) => setTransferForm({ ...transferForm, ingredientId: e.target.value })}
                  className="w-full bg-slate-950 text-slate-200 border border-slate-800 rounded-xl p-2.5 text-xs focus:outline-none focus:border-emerald-500"
                >
                  <option value="">— Pilih bahan —</option>
                  {bahanMutasi.map((ing) => (
                    <option key={ing.id} value={ing.id}>
                      {ing.name} ({ing.unit}){ing.inactive ? ' • nonaktif di master' : ''}
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1">Jumlah</label>
                <div className="flex items-center gap-2">
                  <input
                    type="number"
                    step="0.01"
                    min="0"
                    value={transferForm.quantity}
                    onChange={(e) => setTransferForm({ ...transferForm, quantity: e.target.value })}
                    className="flex-1 bg-slate-950 text-white font-bold text-base border border-slate-800 rounded-xl p-2.5 focus:outline-none focus:border-emerald-500"
                    placeholder="0"
                  />
                  <span className="text-xs font-semibold text-slate-400 bg-slate-950 border border-slate-800 rounded-xl px-3 py-2.5">
                    {selectedIngredient?.unit ?? '—'}
                  </span>
                </div>
                <div className="text-[11px] text-slate-500 mt-1">
                  Satuan terkunci dari master bahan. Tersedia di lokasi asal:{' '}
                  <span className="text-slate-300 font-mono">{formatQty(availableAtSource)} {selectedIngredient?.unit ?? ''}</span>
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1">Catatan</label>
                <input
                  type="text"
                  value={transferForm.notes}
                  onChange={(e) => setTransferForm({ ...transferForm, notes: e.target.value })}
                  className="w-full bg-slate-950 text-slate-200 border border-slate-800 rounded-xl p-2.5 text-xs focus:outline-none focus:border-emerald-500"
                  placeholder="Misal: kebutuhan SPK shift pagi"
                />
              </div>

              <div className="pt-1 flex justify-end gap-2.5">
                <button
                  type="button"
                  onClick={() => setTransferOpen(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-400 hover:text-white"
                >
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={transferSaving}
                  className="flex items-center gap-1.5 px-4 py-2 bg-gradient-to-r from-emerald-500 to-emerald-600 text-slate-950 font-bold rounded-xl text-xs shadow-lg shadow-emerald-500/20 disabled:opacity-50"
                >
                  <Send className="w-3.5 h-3.5" />
                  <span>{transferSaving ? 'Menyimpan...' : 'Catat Mutasi'}</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
