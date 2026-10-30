import React, { useEffect, useMemo, useState } from 'react';
import { Plus, X, ShoppingCart, Truck, Check, Ban, Edit2, PackageOpen, Download } from 'lucide-react';
import { api } from '../services/api';
import { purchaseStatusLabel, purchaseStatusName } from '../utils/enums';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';

// Kunci gaya badge memakai nama enum Domain; label Indonesia diambil dari utils/enums supaya
// satu status hanya punya satu penamaan di seluruh aplikasi.
const STATUS_BADGE = {
  Draft:           'bg-slate-500/20 text-slate-300 border-slate-500/30',
  Sent:            'bg-blue-500/20 text-blue-300 border-blue-500/30',
  PartialReceived: 'bg-yellow-500/20 text-yellow-300 border-yellow-500/30',
  Received:        'bg-green-500/20 text-green-300 border-green-500/30',
  Cancelled:       'bg-red-500/20 text-red-300 border-red-500/30',
};

// Satu pembentuk angka untuk seluruh tabel; membuatnya per baris membuat layar 300 PO tertahan.
const PEMBENTUK_RUPIAH = new Intl.NumberFormat('id-ID', {
  style: 'currency', currency: 'IDR', maximumFractionDigits: 0,
});
const fmt = (minorUnits) => PEMBENTUK_RUPIAH.format((minorUnits || 0) / 100);

// Tanggal kolom tabel: pembentuknya juga dibuat sekali, bukan per baris.
const PEMBENTUK_TANGGAL = new Intl.DateTimeFormat('id-ID', {
  day: '2-digit', month: 'short', year: 'numeric',
});
const fmtTanggal = (nilai) => (nilai ? PEMBENTUK_TANGGAL.format(new Date(nilai)) : '-');

const calcPoTotal = (lines) =>
  (lines || []).reduce((sum, l) => sum + (l.orderedQty || 0) * (l.unitCostMinorUnits || 0), 0);

export default function PurchasesView({
  purchases,
  purchaseTotal = null,
  suppliers,
  ingredients,
  locations,
  onCreatePurchase,
  onReceivePurchase,
  onCancelPurchase,
  onCreateSupplier,
  onUpdateSupplier,
  canWritePurchase = true,
  canCancelPurchase = true,
  initialTab = 'po',
  onTabChange,
}) {
  const [activeTab, setActiveTab] = useState(initialTab);

  // Menu "Pemasok" dan "Pembelian" berbagi layar ini; menu samping memilih tab awalnya.
  useEffect(() => { setActiveTab(initialTab); }, [initialTab]);
  const pilihTab = (key) => {
    setActiveTab(key);
    onTabChange?.(key);
  };

  // Halaman pertama datang dari App; "Muat lagi" menambah halaman berikutnya tanpa memuat ulang
  // seluruh daftar, dan tambahan itu dibuang begitu daftar dari server berubah (sesudah aksi).
  const [poTambahan, setPoTambahan] = useState([]);
  const [poMemuatLagi, setPoMemuatLagi] = useState(false);
  const [poGalatMuat, setPoGalatMuat] = useState('');
  useEffect(() => {
    setPoTambahan([]);
    setPoGalatMuat('');
  }, [purchases]);
  const daftarPo = poTambahan.length > 0 ? [...purchases, ...poTambahan] : purchases;
  const poTotal = purchaseTotal ?? daftarPo.length;

  // Pencarian pemasok per baris tadinya menelusuri seluruh daftar untuk setiap PO; peta ini
  // membuat biayanya tetap satu kali per perubahan daftar pemasok.
  const petaPemasok = useMemo(
    () => new Map((suppliers || []).map((s) => [s.id, s.name])),
    [suppliers]
  );

  const muatPoLagi = async () => {
    setPoMemuatLagi(true);
    setPoGalatMuat('');
    try {
      const halaman = await api.getPurchases({ limit: 100, offset: daftarPo.length });
      const sudahAda = new Set(daftarPo.map((po) => po.id));
      setPoTambahan((lama) => [...lama, ...(halaman.items ?? []).filter((po) => !sudahAda.has(po.id))]);
    } catch (err) {
      setPoGalatMuat(err.message || 'Gagal memuat halaman berikutnya.');
    } finally {
      setPoMemuatLagi(false);
    }
  };

  // PO Modal
  const [poModalOpen, setPoModalOpen] = useState(false);
  const [poForm, setPoForm] = useState({ supplierId: '', expectedDate: '', notes: '' });
  const [poLines, setPoLines] = useState([{ ingredientId: '', orderedQty: '', unitCostRupiah: '', unit: '' }]);
  const { sedangSimpan: poSubmitting, mulai: mulaiPo, selesai: selesaiPo } = useKunciSimpan();
  // Satu kunci per kali membuka form: klik ganda tidak boleh melahirkan dua PO/dua struk terima.
  const [poKunci, setPoKunci] = useState('');

  // Receive Modal
  const [receiveModalOpen, setReceiveModalOpen] = useState(false);
  const [receivingPo, setReceivingPo] = useState(null);
  const [receiveLines, setReceiveLines] = useState([]);
  const [receiveNotes, setReceiveNotes] = useState('');
  const { sedangSimpan: receiveSubmitting, mulai: mulaiTerima, selesai: selesaiTerima } = useKunciSimpan();
  const [receiveKunci, setReceiveKunci] = useState('');
  const [loadingPoDetail, setLoadingPoDetail] = useState(false);

  // Supplier Modal
  const [supplierModalOpen, setSupplierModalOpen] = useState(false);
  const [supplierForm, setSupplierForm] = useState({ name: '', contactName: '', phone: '', email: '', address: '', notes: '' });
  const [editingSupplierId, setEditingSupplierId] = useState(null);
  const { sedangSimpan: supplierSubmitting, mulai: mulaiPemasok, selesai: selesaiPemasok } = useKunciSimpan();
  const [supplierKunci, setSupplierKunci] = useState('');

  // ── PO Modal Handlers ──────────────────────────────────────────────────────
  const openPoModal = () => {
    setPoForm({ supplierId: suppliers[0]?.id ?? '', expectedDate: '', notes: '' });
    setPoLines([{ ingredientId: '', orderedQty: '', unitCostRupiah: '', unit: '' }]);
    setPoKunci(kunciBaru('po-baru'));
    setPoModalOpen(true);
  };

  const addPoLine = () =>
    setPoLines((prev) => [...prev, { ingredientId: '', orderedQty: '', unitCostRupiah: '', unit: '' }]);

  const removePoLine = (idx) =>
    setPoLines((prev) => prev.filter((_, i) => i !== idx));

  const updatePoLine = (idx, field, value) =>
    setPoLines((prev) => prev.map((l, i) => (i === idx ? { ...l, [field]: value } : l)));

  const submitPo = async (e) => {
    e.preventDefault();
    if (!mulaiPo()) return;
    try {
      const payload = {
        supplierId: parseInt(poForm.supplierId),
        expectedDate: poForm.expectedDate || null,
        notes: poForm.notes || null,
        lines: poLines
          .filter((l) => l.ingredientId && l.orderedQty)
          .map((l) => ({
            ingredientId: parseInt(l.ingredientId),
            orderedQty: parseFloat(l.orderedQty),
            unitCostMinorUnits: Math.round(parseFloat(l.unitCostRupiah || 0) * 100),
            unit: l.unit || 'kg',
          })),
        idempotensi: poKunci,
      };
      await onCreatePurchase(payload);
      setPoModalOpen(false);
    } finally {
      selesaiPo();
    }
  };

  // ── Receive Modal Handlers ─────────────────────────────────────────────────
  const openReceiveModal = async (po) => {
    setReceivingPo(po);
    setReceiveNotes('');
    setReceiveLines([]);
    setReceiveKunci(kunciBaru(`terima-po-${po.id}`));
    setLoadingPoDetail(true);
    setReceiveModalOpen(true);
    try {
      const detail = await api.getPurchase(po.id);
      const lines = (detail?.lines || detail?.Lines || po.lines || []).map((l) => ({
        purchaseOrderLineId: l.id || l.Id,
        ingredientId: l.ingredientId || l.IngredientId,
        ingredientName:
          ingredients.find((i) => i.id === (l.ingredientId || l.IngredientId))?.name ||
          `Bahan #${l.ingredientId || l.IngredientId}`,
        orderedQty: l.orderedQty || l.OrderedQty || 0,
        alreadyReceived: l.receivedQty || l.ReceivedQty || 0,
        unit: l.unit || l.Unit || 'kg',
        unitCostMinorUnits: l.unitCostMinorUnits || l.UnitCostMinorUnits || 0,
        receiveNow: '',
        locationId: locations[0]?.id ?? 1,
        batchNumber: '',
        expiryDate: '',
      }));
      setReceiveLines(lines);
    } finally {
      setLoadingPoDetail(false);
    }
  };

  const updateReceiveLine = (idx, field, value) =>
    setReceiveLines((prev) => prev.map((l, i) => (i === idx ? { ...l, [field]: value } : l)));

  const submitReceive = async (e) => {
    e.preventDefault();
    if (!mulaiTerima()) return;
    try {
      const payload = {
        notes: receiveNotes || null,
        idempotensi: receiveKunci,
        lines: receiveLines
          .filter((l) => parseFloat(l.receiveNow) > 0)
          .map((l) => ({
            purchaseOrderLineId: l.purchaseOrderLineId,
            ingredientId: l.ingredientId,
            receivedQty: parseFloat(l.receiveNow),
            unitCostMinorUnits: l.unitCostMinorUnits,
            locationId: parseInt(l.locationId),
            batchNumber: l.batchNumber ? l.batchNumber.trim() : null,
            expiryDate: l.batchNumber && l.expiryDate ? l.expiryDate : null,
          })),
      };
      await onReceivePurchase(receivingPo.id, payload);
      setReceiveModalOpen(false);
    } finally {
      selesaiTerima();
    }
  };

  // ── Supplier Modal Handlers ────────────────────────────────────────────────
  const openAddSupplier = () => {
    setEditingSupplierId(null);
    setSupplierForm({ name: '', contactName: '', phone: '', email: '', address: '', notes: '' });
    setSupplierKunci(kunciBaru('pemasok-baru'));
    setSupplierModalOpen(true);
  };

  const openEditSupplier = (s) => {
    setEditingSupplierId(s.id);
    setSupplierKunci(kunciBaru(`pemasok-${s.id}`));
    setSupplierForm({
      name: s.name || '',
      contactName: s.contactName || '',
      phone: s.phone || '',
      email: s.email || '',
      address: s.address || '',
      notes: s.notes || '',
    });
    setSupplierModalOpen(true);
  };

  const submitSupplier = async (e) => {
    e.preventDefault();
    if (!mulaiPemasok()) return;
    try {
      const payload = {
        name: supplierForm.name,
        contactName: supplierForm.contactName || null,
        phone: supplierForm.phone || null,
        email: supplierForm.email || null,
        address: supplierForm.address || null,
        notes: supplierForm.notes || null,
        idempotensi: supplierKunci,
      };
      if (editingSupplierId) {
        await onUpdateSupplier(editingSupplierId, payload);
      } else {
        await onCreateSupplier(payload);
      }
      setSupplierModalOpen(false);
    } finally {
      selesaiPemasok();
    }
  };

  const confirmCancel = async (po) => {
    if (!window.confirm(`Batalkan PO ${po.poNumber}?`)) return;
    await onCancelPurchase(po.id);
  };

  // ── Render ─────────────────────────────────────────────────────────────────
  return (
    <div className="space-y-4">
      {/* Tab Bar */}
      <div className="flex gap-1 bg-slate-900/60 border border-slate-800 rounded-xl p-1 w-fit">
        {[
          { key: 'po', label: 'Purchase Orders', icon: ShoppingCart },
          { key: 'suppliers', label: 'Pemasok', icon: Truck },
        ].map(({ key, label, icon: Icon }) => (
          <button
            key={key}
            onClick={() => pilihTab(key)}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg text-xs font-semibold transition-all ${
              activeTab === key
                ? 'bg-amber-500 text-slate-950'
                : 'text-slate-400 hover:text-white hover:bg-slate-800'
            }`}
          >
            <Icon className="w-3.5 h-3.5" />
            {label}
          </button>
        ))}
      </div>

      {/* ── Purchase Orders Tab ───────────────────────────────────────────────── */}
      {activeTab === 'po' && (
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
          <div className="p-4 border-b border-slate-800 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <ShoppingCart className="w-4 h-4 text-amber-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                Daftar Purchase Order ({daftarPo.length}
                {poTotal > daftarPo.length ? ` dari ${poTotal}` : ''})
              </h3>
            </div>
            {canWritePurchase && (
              <button
                onClick={openPoModal}
                className="flex items-center gap-1.5 px-3 py-1.5 bg-amber-500 hover:bg-amber-400 text-slate-950 rounded-lg text-xs font-bold transition-colors"
              >
                <Plus className="w-3.5 h-3.5" />
                Buat PO Baru
              </button>
            )}
            <button
              onClick={async () => {
                try { await api.exportPurchases('excel'); } catch (e) { alert('Gagal export: ' + e.message); }
              }}
              className="flex items-center gap-1.5 px-3 py-1.5 bg-emerald-700 hover:bg-emerald-600 text-white rounded-lg text-xs font-medium transition-colors"
            >
              <Download className="w-3.5 h-3.5" />
              Export Excel
            </button>
            <button
              onClick={async () => {
                try { await api.exportPurchases('pdf'); } catch (e) { alert('Gagal export: ' + e.message); }
              }}
              className="flex items-center gap-1.5 px-3 py-1.5 bg-rose-700 hover:bg-rose-600 text-white rounded-lg text-xs font-medium transition-colors"
            >
              <Download className="w-3.5 h-3.5" />
              Export PDF
            </button>
          </div>

          {daftarPo.length === 0 ? (
            <div className="p-8 text-center text-slate-500 text-sm">
              Belum ada purchase order.{canWritePurchase ? ' Klik "Buat PO Baru" untuk memulai.' : ''}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs text-slate-300">
                <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                  <tr>
                    <th className="py-3 px-4 font-semibold">No. PO</th>
                    <th className="py-3 px-4 font-semibold">Pemasok</th>
                    <th className="py-3 px-4 font-semibold">Tgl Order</th>
                    <th className="py-3 px-4 font-semibold">Expected</th>
                    <th className="py-3 px-4 font-semibold">Status</th>
                    <th className="py-3 px-4 font-semibold text-right">Total Nilai</th>
                    <th className="py-3 px-4 font-semibold text-center">Aksi</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60">
                  {daftarPo.map((po) => {
                    const supplierName =
                      petaPemasok.get(po.supplierId || po.SupplierId) ||
                      po.supplierName ||
                      po.SupplierName ||
                      '-';
                    // Status bisa berupa angka enum dari sidecar; dinormalisasi dulu supaya
                    // tombol Terima/Batal tidak hilang untuk PO "Terkirim".
                    const status = purchaseStatusName(po.status ?? po.Status ?? 'Draft');
                    const canReceive = ['Draft', 'Sent', 'PartialReceived'].includes(status);
                    const canCancel = ['Draft', 'Sent'].includes(status);
                    const total = calcPoTotal(po.lines || po.Lines || []);
                    const orderDate = po.orderDate || po.OrderDate;
                    const expectedDate = po.expectedDate || po.ExpectedDate;

                    return (
                      <tr key={po.id} className="hover:bg-slate-800/40 transition-colors">
                        <td className="py-3 px-4 font-mono text-amber-400 font-medium">
                          {po.poNumber || po.PoNumber}
                        </td>
                        <td className="py-3 px-4 font-semibold text-white">{supplierName}</td>
                        <td className="py-3 px-4 text-slate-400">
                          {fmtTanggal(orderDate)}
                        </td>
                        <td className="py-3 px-4 text-slate-400">
                          {fmtTanggal(expectedDate)}
                        </td>
                        <td className="py-3 px-4">
                          <span
                            className={`inline-flex px-2 py-0.5 rounded-full text-[10px] font-bold border ${
                              STATUS_BADGE[status] || STATUS_BADGE.Draft
                            }`}
                          >
                            {purchaseStatusLabel(status)}
                          </span>
                        </td>
                        <td className="py-3 px-4 text-right font-mono text-emerald-400">
                          {fmt(total)}
                        </td>
                        <td className="py-3 px-4">
                          <div className="flex items-center justify-center gap-1.5">
                            {canReceive && canWritePurchase && (
                              <button
                                onClick={() => openReceiveModal(po)}
                                className="flex items-center gap-1 px-2 py-1 bg-emerald-500/20 hover:bg-emerald-500/30 text-emerald-300 border border-emerald-500/30 rounded-lg text-[10px] font-semibold transition-colors"
                              >
                                <PackageOpen className="w-3 h-3" />
                                Terima
                              </button>
                            )}
                            {canCancel && canCancelPurchase && (
                              <button
                                onClick={() => confirmCancel(po)}
                                className="flex items-center gap-1 px-2 py-1 bg-red-500/20 hover:bg-red-500/30 text-red-300 border border-red-500/30 rounded-lg text-[10px] font-semibold transition-colors"
                              >
                                <Ban className="w-3 h-3" />
                                Batal
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
          )}

          {daftarPo.length > 0 && daftarPo.length < poTotal && (
            <div className="p-3 border-t border-slate-800 flex items-center justify-between gap-2">
              <span className="text-[11px] text-slate-500">
                Menampilkan {daftarPo.length} dari {poTotal} PO terbaru.
              </span>
              <button
                onClick={muatPoLagi}
                disabled={poMemuatLagi}
                className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors disabled:opacity-50"
              >
                {poMemuatLagi ? 'Memuat…' : 'Muat lagi'}
              </button>
            </div>
          )}
          {poGalatMuat && (
            <div className="px-4 py-2 text-[11px] text-rose-400 border-t border-slate-800">{poGalatMuat}</div>
          )}
        </div>
      )}

      {/* ── Suppliers Tab ─────────────────────────────────────────────────────── */}
      {activeTab === 'suppliers' && (
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
          <div className="p-4 border-b border-slate-800 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Truck className="w-4 h-4 text-amber-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                Daftar Pemasok ({suppliers.length})
              </h3>
            </div>
            <button
              onClick={openAddSupplier}
              className="flex items-center gap-1.5 px-3 py-1.5 bg-amber-500 hover:bg-amber-400 text-slate-950 rounded-lg text-xs font-bold transition-colors"
            >
              <Plus className="w-3.5 h-3.5" />
              Tambah Pemasok
            </button>
          </div>

          {suppliers.length === 0 ? (
            <div className="p-8 text-center text-slate-500 text-sm">
              Belum ada pemasok. Klik "Tambah Pemasok" untuk menambahkan.
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs text-slate-300">
                <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                  <tr>
                    <th className="py-3 px-4 font-semibold">Nama</th>
                    <th className="py-3 px-4 font-semibold">Kontak</th>
                    <th className="py-3 px-4 font-semibold">Telepon</th>
                    <th className="py-3 px-4 font-semibold">Email</th>
                    <th className="py-3 px-4 font-semibold text-center">Aksi</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800/60">
                  {suppliers.map((s) => (
                    <tr key={s.id} className="hover:bg-slate-800/40 transition-colors">
                      <td className="py-3 px-4 font-semibold text-white">{s.name}</td>
                      <td className="py-3 px-4 text-slate-400">{s.contactName || '-'}</td>
                      <td className="py-3 px-4 text-slate-400">{s.phone || '-'}</td>
                      <td className="py-3 px-4 text-slate-400">{s.email || '-'}</td>
                      <td className="py-3 px-4 text-center">
                        <button
                          onClick={() => openEditSupplier(s)}
                          className="flex items-center gap-1 px-2 py-1 bg-slate-700 hover:bg-slate-600 text-slate-300 rounded-lg text-[10px] font-semibold transition-colors mx-auto"
                        >
                          <Edit2 className="w-3 h-3" />
                          Edit
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* ── Modal: Buat PO ────────────────────────────────────────────────────── */}
      {poModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl shadow-2xl overflow-hidden max-h-[90vh] flex flex-col">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between shrink-0">
              <h3 className="text-base font-bold text-white">Buat Purchase Order Baru</h3>
              <button onClick={() => setPoModalOpen(false)} className="text-slate-400 hover:text-white transition-colors">
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={submitPo} className="flex flex-col overflow-hidden flex-1">
              <div className="p-5 space-y-4 overflow-y-auto flex-1">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="text-xs font-semibold text-slate-400 block mb-1.5">Pemasok *</label>
                    <select
                      value={poForm.supplierId}
                      onChange={(e) => setPoForm({ ...poForm, supplierId: e.target.value })}
                      className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                      required
                    >
                      <option value="">-- Pilih Pemasok --</option>
                      {suppliers.map((s) => (
                        <option key={s.id} value={s.id}>
                          {s.name}
                        </option>
                      ))}
                    </select>
                  </div>
                  <div>
                    <label className="text-xs font-semibold text-slate-400 block mb-1.5">Tanggal Expected</label>
                    <input
                      type="date"
                      value={poForm.expectedDate}
                      onChange={(e) => setPoForm({ ...poForm, expectedDate: e.target.value })}
                      className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                    />
                  </div>
                </div>

                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Catatan</label>
                  <textarea
                    value={poForm.notes}
                    onChange={(e) => setPoForm({ ...poForm, notes: e.target.value })}
                    rows={2}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors resize-none"
                    placeholder="Catatan tambahan..."
                  />
                </div>

                {/* Lines */}
                <div>
                  <div className="flex items-center justify-between mb-2">
                    <label className="text-xs font-semibold text-slate-400">Baris Bahan *</label>
                    <button
                      type="button"
                      onClick={addPoLine}
                      className="flex items-center gap-1 px-2 py-1 bg-slate-700 hover:bg-slate-600 text-slate-300 rounded-lg text-[10px] font-semibold transition-colors"
                    >
                      <Plus className="w-3 h-3" />
                      Tambah Bahan
                    </button>
                  </div>

                  <div className="space-y-2">
                    {poLines.map((line, idx) => (
                      <div key={idx} className="grid grid-cols-12 gap-2 items-center">
                        <div className="col-span-4">
                          <select
                            value={line.ingredientId}
                            onChange={(e) => updatePoLine(idx, 'ingredientId', e.target.value)}
                            className="w-full px-2 py-1.5 bg-slate-950 border border-slate-700 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                            required
                          >
                            <option value="">-- Bahan --</option>
                            {ingredients.map((i) => (
                              <option key={i.id} value={i.id}>
                                {i.name}
                              </option>
                            ))}
                          </select>
                        </div>
                        <div className="col-span-2">
                          <input
                            type="number"
                            min="0"
                            step="0.01"
                            placeholder="Qty"
                            value={line.orderedQty}
                            onChange={(e) => updatePoLine(idx, 'orderedQty', e.target.value)}
                            className="w-full px-2 py-1.5 bg-slate-950 border border-slate-700 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                            required
                          />
                        </div>
                        <div className="col-span-2">
                          <input
                            type="text"
                            placeholder="Satuan"
                            value={line.unit}
                            onChange={(e) => updatePoLine(idx, 'unit', e.target.value)}
                            className="w-full px-2 py-1.5 bg-slate-950 border border-slate-700 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                          />
                        </div>
                        <div className="col-span-3">
                          <input
                            type="number"
                            min="0"
                            step="1"
                            placeholder="Harga (Rp)"
                            value={line.unitCostRupiah}
                            onChange={(e) => updatePoLine(idx, 'unitCostRupiah', e.target.value)}
                            className="w-full px-2 py-1.5 bg-slate-950 border border-slate-700 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                          />
                        </div>
                        <div className="col-span-1 flex justify-center">
                          {poLines.length > 1 && (
                            <button
                              type="button"
                              onClick={() => removePoLine(idx)}
                              className="text-red-400 hover:text-red-300 transition-colors"
                            >
                              <X className="w-4 h-4" />
                            </button>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                  <p className="text-[10px] text-slate-500 mt-1.5">Kolom: Bahan | Qty | Satuan | Harga per Satuan (Rp)</p>
                </div>
              </div>

              <div className="p-4 border-t border-slate-800 bg-slate-950/30 flex justify-end gap-3 shrink-0">
                <button
                  type="button"
                  onClick={() => setPoModalOpen(false)}
                  className="px-4 py-2 text-slate-400 hover:text-white text-sm font-medium transition-colors"
                >
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={poSubmitting}
                  className="px-5 py-2 bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all disabled:opacity-50"
                >
                  {poSubmitting ? 'Menyimpan...' : 'Buat PO'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ── Modal: Terima Barang ──────────────────────────────────────────────── */}
      {receiveModalOpen && receivingPo && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl shadow-2xl overflow-hidden max-h-[90vh] flex flex-col">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between shrink-0">
              <div>
                <h3 className="text-base font-bold text-white">Terima Barang</h3>
                <p className="text-xs text-slate-400 mt-0.5">
                  PO: <span className="text-amber-400 font-mono">{receivingPo.poNumber || receivingPo.PoNumber}</span>
                </p>
              </div>
              <button onClick={() => setReceiveModalOpen(false)} className="text-slate-400 hover:text-white transition-colors">
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={submitReceive} className="flex flex-col overflow-hidden flex-1">
              <div className="p-5 space-y-4 overflow-y-auto flex-1">
                {loadingPoDetail ? (
                  <div className="text-center text-slate-400 text-sm py-8">Memuat detail PO...</div>
                ) : receiveLines.length === 0 ? (
                  <div className="text-center text-slate-400 text-sm py-8">Tidak ada baris untuk diterima.</div>
                ) : (
                  <div className="space-y-3">
                    {receiveLines.map((line, idx) => (
                      <div key={idx} className="bg-slate-800/40 border border-slate-700/50 rounded-xl p-3 space-y-2">
                        <div className="flex items-center justify-between">
                          <span className="text-sm font-semibold text-white">{line.ingredientName}</span>
                          <span className="text-xs text-slate-400">{line.unit}</span>
                        </div>
                        <div className="grid grid-cols-3 gap-3 text-xs text-slate-400">
                          <div>
                            <span className="block text-[10px] uppercase tracking-wider mb-0.5">Dipesan</span>
                            <span className="font-mono text-slate-200">{line.orderedQty}</span>
                          </div>
                          <div>
                            <span className="block text-[10px] uppercase tracking-wider mb-0.5">Sudah Diterima</span>
                            <span className="font-mono text-slate-200">{line.alreadyReceived}</span>
                          </div>
                          <div>
                            <span className="block text-[10px] uppercase tracking-wider mb-0.5">Sisa</span>
                            <span className="font-mono text-amber-300">
                              {Math.max(0, line.orderedQty - line.alreadyReceived)}
                            </span>
                          </div>
                        </div>
                        <div className="grid grid-cols-2 gap-3">
                          <div>
                            <label className="text-[10px] uppercase tracking-wider text-slate-400 block mb-1">
                              Terima Sekarang
                            </label>
                            <input
                              type="number"
                              min="0"
                              step="0.01"
                              value={line.receiveNow}
                              onChange={(e) => updateReceiveLine(idx, 'receiveNow', e.target.value)}
                              className="w-full px-2 py-1.5 bg-slate-950 border border-slate-700 rounded-lg text-xs text-white focus:outline-none focus:border-emerald-500/60 transition-colors"
                              placeholder="0"
                            />
                          </div>
                          <div>
                            <label className="text-[10px] uppercase tracking-wider text-slate-400 block mb-1">
                              Lokasi Simpan
                            </label>
                            <select
                              value={line.locationId}
                              onChange={(e) => updateReceiveLine(idx, 'locationId', e.target.value)}
                              className="w-full px-2 py-1.5 bg-slate-950 border border-slate-700 rounded-lg text-xs text-white focus:outline-none focus:border-emerald-500/60 transition-colors"
                            >
                              {locations.map((loc) => (
                                <option key={loc.id} value={loc.id}>
                                  {loc.name}
                                </option>
                              ))}
                            </select>
                          </div>
                        </div>
                        <div className="grid grid-cols-2 gap-3">
                          <div>
                            <label className="text-[10px] uppercase tracking-wider text-slate-400 block mb-1">
                              Nomor Batch (opsional)
                            </label>
                            <input
                              type="text"
                              maxLength={60}
                              value={line.batchNumber}
                              onChange={(e) => updateReceiveLine(idx, 'batchNumber', e.target.value)}
                              className="w-full px-2 py-1.5 bg-slate-950 border border-slate-700 rounded-lg text-xs text-white focus:outline-none focus:border-emerald-500/60 transition-colors"
                              placeholder="mis. GUL-2026-10-A"
                            />
                          </div>
                          <div>
                            <label className="text-[10px] uppercase tracking-wider text-slate-400 block mb-1">
                              Kedaluwarsa
                            </label>
                            <input
                              type="date"
                              value={line.expiryDate}
                              disabled={!line.batchNumber}
                              onChange={(e) => updateReceiveLine(idx, 'expiryDate', e.target.value)}
                              className="w-full px-2 py-1.5 bg-slate-950 border border-slate-700 rounded-lg text-xs text-white focus:outline-none focus:border-emerald-500/60 transition-colors disabled:opacity-40"
                            />
                          </div>
                        </div>
                        <p className="text-[10px] text-slate-500">
                          Isi nomor batch + kedaluwarsa bila bahan ini perlu dilacak lotnya. Lot akan muncul di menu "Nomor Batch & Kedaluwarsa".
                        </p>
                      </div>
                    ))}
                  </div>
                )}

                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Catatan Penerimaan</label>
                  <textarea
                    value={receiveNotes}
                    onChange={(e) => setReceiveNotes(e.target.value)}
                    rows={2}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-emerald-500/60 transition-colors resize-none"
                    placeholder="Catatan kondisi barang, dll..."
                  />
                </div>
              </div>

              <div className="p-4 border-t border-slate-800 bg-slate-950/30 flex justify-end gap-3 shrink-0">
                <button
                  type="button"
                  onClick={() => setReceiveModalOpen(false)}
                  className="px-4 py-2 text-slate-400 hover:text-white text-sm font-medium transition-colors"
                >
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={receiveSubmitting || loadingPoDetail}
                  className="flex items-center gap-2 px-5 py-2 bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-emerald-500/20 transition-all disabled:opacity-50"
                >
                  <Check className="w-4 h-4" />
                  {receiveSubmitting ? 'Menyimpan...' : 'Simpan Penerimaan'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ── Modal: Tambah/Edit Pemasok ────────────────────────────────────────── */}
      {supplierModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <h3 className="text-base font-bold text-white">
                {editingSupplierId ? 'Edit Pemasok' : 'Tambah Pemasok Baru'}
              </h3>
              <button onClick={() => setSupplierModalOpen(false)} className="text-slate-400 hover:text-white transition-colors">
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={submitSupplier} className="p-5 space-y-3">
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Nama Pemasok *</label>
                <input
                  type="text"
                  value={supplierForm.name}
                  onChange={(e) => setSupplierForm({ ...supplierForm, name: e.target.value })}
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                  placeholder="Nama pemasok..."
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Nama Kontak</label>
                  <input
                    type="text"
                    value={supplierForm.contactName}
                    onChange={(e) => setSupplierForm({ ...supplierForm, contactName: e.target.value })}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                    placeholder="Nama PIC..."
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Telepon</label>
                  <input
                    type="tel"
                    value={supplierForm.phone}
                    onChange={(e) => setSupplierForm({ ...supplierForm, phone: e.target.value })}
                    className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                    placeholder="08xx..."
                  />
                </div>
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Email</label>
                <input
                  type="email"
                  value={supplierForm.email}
                  onChange={(e) => setSupplierForm({ ...supplierForm, email: e.target.value })}
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  placeholder="email@pemasok.com"
                />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Alamat</label>
                <textarea
                  value={supplierForm.address}
                  onChange={(e) => setSupplierForm({ ...supplierForm, address: e.target.value })}
                  rows={2}
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors resize-none"
                  placeholder="Alamat lengkap..."
                />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Catatan</label>
                <textarea
                  value={supplierForm.notes}
                  onChange={(e) => setSupplierForm({ ...supplierForm, notes: e.target.value })}
                  rows={2}
                  className="w-full px-3 py-2 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors resize-none"
                  placeholder="Catatan khusus..."
                />
              </div>

              <div className="flex justify-end gap-3 pt-2">
                <button
                  type="button"
                  onClick={() => setSupplierModalOpen(false)}
                  className="px-4 py-2 text-slate-400 hover:text-white text-sm font-medium transition-colors"
                >
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={supplierSubmitting}
                  className="px-5 py-2 bg-amber-500 hover:bg-amber-400 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all disabled:opacity-50"
                >
                  {supplierSubmitting ? 'Menyimpan...' : editingSupplierId ? 'Simpan Perubahan' : 'Tambah Pemasok'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
