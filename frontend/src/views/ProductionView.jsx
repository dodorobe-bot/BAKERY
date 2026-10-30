import React, { useState } from 'react';
import { 
  Flame, 
  Plus, 
  CheckCircle2, 
  XCircle, 
  AlertTriangle, 
  Clock, 
  Sparkles, 
  Scale, 
  Check, 
  RotateCw,
  Ban,
  ClipboardList,
  ChefHat,
  FlaskConical
} from 'lucide-react';
import { api } from '../services/api';
import { toMinor, formatDate, formatInstantDate } from '../utils/money';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';

// Tiga menu Produksi berbagi satu layar; `view` memilih bagian yang dibuka lebih dulu.
const TAB_PRODUKSI = [
  { id: 'production_plan', label: 'Perencanaan', icon: ClipboardList },
  { id: 'production_orders', label: 'Perintah Kerja (SPK)', icon: ChefHat },
  { id: 'production_actual', label: 'Pemakaian Aktual & Hasil', icon: FlaskConical },
];

const BAGIAN_PRODUKSI = {
  production_plan: {
    judul: 'Perencanaan Jadwal Dapur',
    sub: 'SPK yang masih berstatus terjadwal dan belum mulai dikerjakan, siap diatur urutan produksinya.',
  },
  production_orders: {
    judul: 'Perintah Kerja (SPK)',
    sub: 'Seluruh SPK beserta status pengerjaannya, dari terjadwal sampai selesai atau dibatalkan.',
  },
  production_actual: {
    judul: 'Pemakaian Aktual & Hasil',
    sub: 'SPK yang sudah selesai atau dibatalkan, tempat pemakaian bahan aktual dan hasil produksi tercatat.',
  },
};

export default function ProductionView({
  workOrders, onCompleteWorkOrder, onCreateWorkOrder, onCancelWorkOrder, products, loading, onRefresh,
  canWrite = true, canCancel = true, view = 'production_orders', onViewChange,
}) {
  const section = BAGIAN_PRODUKSI[view] ? view : 'production_orders';
  const [selectedOrder, setSelectedOrder] = useState(null);
  const [completeModalOpen, setCompleteModalOpen] = useState(false);
  const [newOrderModalOpen, setNewOrderModalOpen] = useState(false);
  const [cancelModalOpen, setCancelModalOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [cancelTarget, setCancelTarget] = useState(null);
  const [loadingMaterials, setLoadingMaterials] = useState(false);

  // New Order Form State
  const [newOrderForm, setNewOrderForm] = useState({
    productTemplateId: '',
    targetQuantity: 100,
    laborCost: 0,
    overheadCost: 0
  });
  const { sedangSimpan: newOrderSubmitting, mulai: mulaiSpk, selesai: selesaiSpk } = useKunciSimpan();
  const { mulai: mulaiSelesaiSpk, selesai: selesaiSelesaiSpk } = useKunciSimpan();
  // Satu kunci per kali membuka form; klik ganda tidak boleh melahirkan dua SPK atau dua kali
  // penerbitan jurnal untuk hasil produksi yang sama.
  const [newOrderKunci, setNewOrderKunci] = useState('');
  const [completeKunci, setCompleteKunci] = useState('');

  // Form Actual Scrap & Result State
  const [scrapData, setScrapData] = useState({
    goodUnits: 96,
    scrapUnits: 3,
    scrapReason: 'Adonan gagal / bantat',
    damagedUnits: 1,
    damagedReason: 'Kerusakan mesin / cetakan',
    canRework: false,
    reworkNotes: '',
    actualIngredients: []
  });

  const handleOpenComplete = async (wo) => {
    setSelectedOrder(wo);
    const target = wo.targetQuantity || 100;

    // Always fetch full work order to get materials (GetAllAsync does not include materials)
    let materials = [];
    setLoadingMaterials(true);
    try {
      const full = await api.getWorkOrder(wo.id);
      materials = full?.materials || full?.Materials || [];
    } catch (e) {
      materials = [];
    } finally {
      setLoadingMaterials(false);
    }

    const actualIngredients = materials.length > 0
      ? materials.map(m => ({
          ingredientId: m.ingredientId || m.IngredientId,
          name: m.ingredientName || m.IngredientName || `Bahan #${m.ingredientId || m.IngredientId}`,
          initialStock: parseFloat(m.standardQty || m.StandardQty) || 0,
          actualUsed: parseFloat(m.actualQty || m.ActualQty) || parseFloat(m.standardQty || m.StandardQty) || 0,
          unit: m.unit || m.Unit || 'kg',
          unitCost: parseInt(m.unitCost || m.UnitCost) || 0,
          remaining: parseFloat(m.standardQty || m.StandardQty) || 0
        }))
      : [];

    // Compute remaining for each
    actualIngredients.forEach(ing => {
      ing.remaining = Math.max(0, ing.initialStock - ing.actualUsed);
    });

    setScrapData({
      goodUnits: Math.floor(target * 0.96),
      scrapUnits: Math.ceil(target * 0.03),
      scrapReason: 'Adonan gagal / bantat',
      damagedUnits: Math.max(0, target - Math.floor(target * 0.96) - Math.ceil(target * 0.03)),
      damagedReason: 'Cacat bentuk saat pemanggangan',
      canRework: false,
      reworkNotes: '',
      actualIngredients
    });
    setCompleteKunci(kunciBaru(`selesai-spk-${wo.id}`));
    setCompleteModalOpen(true);
  };

  const handleIngredientChange = (index, field, value) => {
    const updated = [...scrapData.actualIngredients];
    updated[index][field] = parseFloat(value) || 0;
    if (field === 'actualUsed') {
      updated[index].remaining = Math.max(0, updated[index].initialStock - updated[index].actualUsed);
    }
    setScrapData({ ...scrapData, actualIngredients: updated });
  };

  const handleSubmitComplete = (e) => {
    e.preventDefault();
    if (!selectedOrder) return;
    if (!mulaiSelesaiSpk()) return;
    
    const materials = (scrapData.actualIngredients || [])
      .filter(ing => ing.ingredientId)
      .map(ing => ({
          ingredientId: ing.ingredientId,
          actualQty: parseFloat(ing.actualUsed) || 0,
          unitCost: ing.unitCost || 0
        }));

    const scraps = [];
    if (scrapData.scrapUnits > 0) {
      scraps.push({
        quantity: parseInt(scrapData.scrapUnits) || 0,
        reasonCategory: scrapData.scrapReason.includes('gosong') ? 'Gosong' : 
                        scrapData.scrapReason.includes('resep') ? 'Salah_Resep' : 'Adonan_Gagal',
        actionTaken: scrapData.canRework ? 'Rework' : 'Scrap_Loss',
        notes: scrapData.scrapReason
      });
    }

    if (scrapData.damagedUnits > 0) {
      scraps.push({
        quantity: parseInt(scrapData.damagedUnits) || 0,
        reasonCategory: scrapData.damagedReason.includes('mixer') ? 'Mesin_Error' : 'Lainnya',
        actionTaken: 'Scrap_Loss',
        notes: scrapData.damagedReason
      });
    }

    // Kunci dilepas saat permintaan selesai: modal sudah tertutup, tapi klik kedua yang
    // datang sebelum browser menanggapi harus tetap tidak mengirim apa pun.
    Promise.resolve(onCompleteWorkOrder(selectedOrder.id, {
      actualGoodQty: Number(scrapData.goodUnits) || 0,
      actualFailedQty: (Number(scrapData.scrapUnits) || 0) + (Number(scrapData.damagedUnits) || 0),
      materials,
      scraps,
      idempotensi: completeKunci
    })).finally(selesaiSelesaiSpk);

    setCompleteModalOpen(false);
  };

  const handleOpenCancel = (wo) => {
    setCancelTarget(wo);
    setCancelReason('');
    setCancelModalOpen(true);
  };

  const handleSubmitCancel = async () => {
    if (!cancelTarget) return;
    await onCancelWorkOrder(cancelTarget.id, cancelReason);
    setCancelModalOpen(false);
    setCancelTarget(null);
    setCancelReason('');
  };

  const handleSubmitNewOrder = async (e) => {
    e.preventDefault();
    if (!newOrderForm.productTemplateId) return;
    if (!mulaiSpk()) return;
    try {
      await onCreateWorkOrder({
        productTemplateId: parseInt(newOrderForm.productTemplateId),
        targetQuantity: parseInt(newOrderForm.targetQuantity) || 100,
        laborCost: toMinor(parseInt(newOrderForm.laborCost) || 0),
        overheadCost: toMinor(parseInt(newOrderForm.overheadCost) || 0),
        idempotensi: newOrderKunci
      });
      setNewOrderModalOpen(false);
      setNewOrderForm({ productTemplateId: '', targetQuantity: 100, laborCost: 0, overheadCost: 0 });
    } finally {
      selesaiSpk();
    }
  };

  const statusBadges = {
    0: { label: 'Terjadwal', bg: 'bg-blue-500/10 text-blue-400 border-blue-500/20' },
    1: { label: 'Diproses', bg: 'bg-amber-500/10 text-amber-400 border-amber-500/20' },
    2: { label: 'Selesai', bg: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' },
    3: { label: 'Dibatalkan', bg: 'bg-rose-500/10 text-rose-400 border-rose-500/20' }
  };

  // Normalize work order status: API may return string or int
  const normalizeStatus = (wo) => {
    if (typeof wo.status === 'number') return wo.status;
    const map = { Planned: 0, In_Process: 1, Completed: 2, Cancelled: 3 };
    return map[wo.status] ?? 0;
  };

  // Bagian layar menyaring SPK sesuai menu yang dibuka; kartu ringkasan tetap menghitung semua.
  const daftarSpk = (workOrders || []).filter((wo) => {
    const status = normalizeStatus(wo);
    if (section === 'production_plan') return status === 0;
    if (section === 'production_actual') return status === 2 || status === 3;
    return true;
  });

  return (
    <div className="space-y-6">
      {/* Top Banner & Actions */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold text-white tracking-tight">{BAGIAN_PRODUKSI[section].judul}</h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20 rounded-full">
              Lantai Produksi
            </span>
          </div>
          <p className="text-sm text-slate-400 mt-1">{BAGIAN_PRODUKSI[section].sub}</p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            onClick={onRefresh}
            className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl transition-colors border border-slate-700/60"
            title="Muat Ulang Data"
          >
            <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
          {/* Tombol hanya dirender untuk peran yang memang boleh menulis SPK; server tetap
              memeriksa tokennya sendiri, ini mencegah klik yang pasti berakhir 403. */}
          {canWrite ? (
            <button
              onClick={() => {
                setNewOrderKunci(kunciBaru('spk-baru'));
                setNewOrderModalOpen(true);
              }}
              className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-semibold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all"
            >
              <Plus className="w-4 h-4" />
              <span>Buat SPK Produksi Baru</span>
            </button>
          ) : (
            <span className="text-[11px] text-slate-400 max-w-[220px] text-right">
              Peran Anda dapat melihat SPK, tetapi tidak dapat membuat atau menyelesaikannya.
            </span>
          )}
        </div>
      </div>

      {/* Bagian layar: setiap menu samping membuka bagian yang sesuai. */}
      <div className="flex items-center gap-2 border-b border-slate-800 pb-2 flex-wrap">
        {TAB_PRODUKSI.map((tab) => (
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

      {/* Metric Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-slate-900/80 border border-slate-800/80 p-4 rounded-xl">
          <div className="text-xs font-medium text-slate-400 flex items-center gap-1.5">
            <Clock className="w-4 h-4 text-amber-400" />
            <span>SPK Dalam Antrean Dapur</span>
          </div>
          <div className="text-2xl font-bold text-white mt-2">
            {workOrders.filter(w => normalizeStatus(w) === 0 || normalizeStatus(w) === 1).length} SPK
          </div>
          <div className="text-[11px] text-amber-400/80 mt-1">Siap diaduk & dicetak</div>
        </div>

        <div className="bg-slate-900/80 border border-slate-800/80 p-4 rounded-xl">
          <div className="text-xs font-medium text-slate-400 flex items-center gap-1.5">
            <Flame className="w-4 h-4 text-orange-400" />
            <span>Sedang Masuk Oven</span>
          </div>
          <div className="text-2xl font-bold text-white mt-2">
            {workOrders.filter(w => normalizeStatus(w) === 1).length} Loyang
          </div>
          <div className="text-[11px] text-slate-400 mt-1">Oven deck utama ruang panggang</div>
        </div>

        <div className="bg-slate-900/80 border border-slate-800/80 p-4 rounded-xl">
          <div className="text-xs font-medium text-slate-400 flex items-center gap-1.5">
            <CheckCircle2 className="w-4 h-4 text-emerald-400" />
            <span>SPK Selesai</span>
          </div>
          <div className="text-2xl font-bold text-emerald-400 mt-2">
            {workOrders.filter(w => normalizeStatus(w) === 2).length} SPK
          </div>
          <div className="text-[11px] text-emerald-500/80 mt-1">Produksi telah diselesaikan</div>
        </div>

        <div className="bg-slate-900/80 border border-slate-800/80 p-4 rounded-xl">
          <div className="text-xs font-medium text-slate-400 flex items-center gap-1.5">
            <AlertTriangle className="w-4 h-4 text-rose-400" />
            <span>SPK Dibatalkan</span>
          </div>
          <div className="text-2xl font-bold text-rose-400 mt-2">
            {workOrders.filter(w => normalizeStatus(w) === 3).length} SPK
          </div>
          <div className="text-[11px] text-slate-400 mt-1">Alasan dicatat lengkap di pembukuan</div>
        </div>
      </div>

      {/* Work Orders Table */}
      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
        <div className="p-4 border-b border-slate-800/80 flex items-center justify-between">
          <h2 className="text-base font-semibold text-white flex items-center gap-2">
            <Flame className="w-4 h-4 text-amber-400" />
            <span>
              {section === 'production_plan'
                ? 'Daftar SPK Terjadwal'
                : section === 'production_actual'
                  ? 'Daftar SPK Selesai & Dibatalkan'
                  : 'Daftar Perintah Kerja Dapur (SPK)'}
            </span>
          </h2>
          <span className="text-xs text-slate-400">{daftarSpk.length} SPK Terdaftar</span>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
              <tr>
                <th className="py-3.5 px-4 font-semibold">Nomor SPK & Batch</th>
                <th className="py-3.5 px-4 font-semibold">Nama Produk Roti</th>
                <th className="py-3.5 px-4 font-semibold text-center">Jumlah Target</th>
                <th className="py-3.5 px-4 font-semibold text-center">Status Pengerjaan</th>
                <th className="py-3.5 px-4 font-semibold">Tanggal Mulai</th>
                <th className="py-3.5 px-4 font-semibold text-right">Tindakan</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60 font-medium">
              {daftarSpk.length === 0 ? (
                <tr>
                  <td colSpan="6" className="py-12 text-center text-slate-500">
                    {section === 'production_plan'
                      ? 'Tidak ada SPK yang masih berstatus terjadwal.'
                      : section === 'production_actual'
                        ? 'Belum ada SPK selesai atau dibatalkan pada rentang data ini.'
                        : `Belum ada antrean Perintah Kerja (SPK).${canWrite ? ' Silakan klik tombol "Buat SPK Produksi Baru" di atas.' : ''}`}
                  </td>
                </tr>
              ) : (
                daftarSpk.map((wo) => {
                  const statusNum = normalizeStatus(wo);
                  const status = statusBadges[statusNum] || { label: 'Konsep', bg: 'bg-slate-700 text-slate-300' };
                  const canAct = statusNum !== 2 && statusNum !== 3;

                  return (
                    <tr key={wo.id} className="hover:bg-slate-800/40 transition-colors">
                      <td className="py-3.5 px-4 font-mono font-bold text-amber-400">
                        {wo.orderNumber || `SPK-${wo.id.toString().padStart(4, '0')}`}
                      </td>
                      <td className="py-3.5 px-4">
                        <div className="text-white font-semibold">{wo.productName || wo.ProductName || `Produk #${wo.productTemplateId || wo.ProductTemplateId}`}</div>
                        <div className="text-[11px] text-slate-400">ID Produk: #{wo.productTemplateId || wo.ProductTemplateId}</div>
                      </td>
                      <td className="py-3.5 px-4 text-center font-bold text-white">
                        {wo.targetQuantity || wo.TargetQuantity || 0} buah
                      </td>
                      <td className="py-3.5 px-4 text-center">
                        <span className={`inline-flex px-2.5 py-1 rounded-full text-[10px] font-semibold border ${status.bg}`}>
                          {status.label}
                        </span>
                      </td>
                      <td className="py-3.5 px-4 text-slate-400">
                        {wo.startDate || wo.StartDate
                          ? formatDate(wo.startDate || wo.StartDate)
                          : wo.createdAt || wo.CreatedAt
                            ? formatInstantDate(wo.createdAt || wo.CreatedAt)
                            : '-'}
                      </td>
                      <td className="py-3.5 px-4 text-right">
                        {canAct ? (
                          <div className="flex items-center justify-end gap-2">
                            {canWrite ? (
                              <button
                                onClick={() => handleOpenComplete(wo)}
                                className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-emerald-500/10 hover:bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 rounded-lg text-xs font-semibold transition-colors"
                              >
                                <CheckCircle2 className="w-3.5 h-3.5" />
                                <span>Selesaikan</span>
                              </button>
                            ) : null}
                            {canCancel ? (
                              <button
                                onClick={() => handleOpenCancel(wo)}
                                className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-rose-500/10 hover:bg-rose-500/20 text-rose-400 border border-rose-500/30 rounded-lg text-xs font-semibold transition-colors"
                              >
                                <Ban className="w-3.5 h-3.5" />
                                <span>Batalkan</span>
                              </button>
                            ) : null}
                            {!canWrite && !canCancel ? (
                              <span className="text-[11px] text-slate-500 italic">Hanya lihat</span>
                            ) : null}
                          </div>
                        ) : (
                          <span className="text-[11px] text-slate-500 italic">
                            {statusNum === 2 ? 'Telah Selesai & Tercatat' : 'Dibatalkan'}
                          </span>
                        )}
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Modal: Buat SPK Produksi Baru */}
      {newOrderModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg overflow-hidden shadow-2xl">
            {/* Header */}
            <div className="p-4 border-b border-slate-800 flex items-center justify-between bg-slate-950/50">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-lg bg-amber-500/10 border border-amber-500/20 flex items-center justify-center text-amber-400">
                  <Plus className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="text-base font-bold text-white">Buat SPK Produksi Baru</h3>
                  <p className="text-xs text-slate-400">Isi data perintah kerja dapur di bawah ini</p>
                </div>
              </div>
              <button
                onClick={() => setNewOrderModalOpen(false)}
                className="text-slate-400 hover:text-white p-1 rounded-lg"
              >
                <XCircle className="w-5 h-5" />
              </button>
            </div>

            {/* Body */}
            <form onSubmit={handleSubmitNewOrder} className="p-5 space-y-4">
              {/* Produk */}
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Produk <span className="text-rose-400">*</span>
                </label>
                <select
                  value={newOrderForm.productTemplateId}
                  onChange={(e) => setNewOrderForm({ ...newOrderForm, productTemplateId: e.target.value })}
                  required
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                >
                  <option value="">-- Pilih produk --</option>
                  {(products || []).map(p => (
                    <option key={p.id || p.Id} value={p.id || p.Id}>
                      {p.name || p.Name}
                    </option>
                  ))}
                </select>
                {(!products || products.length === 0) && (
                  <p className="text-[11px] text-amber-400 mt-1">
                    Data produk belum dimuat. Pastikan master produk sudah tersedia.
                  </p>
                )}
              </div>

              {/* Target Qty */}
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Target Jumlah (buah) <span className="text-rose-400">*</span>
                </label>
                <input
                  type="number"
                  min="1"
                  value={newOrderForm.targetQuantity}
                  onChange={(e) => setNewOrderForm({ ...newOrderForm, targetQuantity: e.target.value })}
                  required
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  placeholder="Contoh: 100"
                />
              </div>

              {/* Biaya Labor & Overhead */}
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                    Biaya Tenaga Kerja (Rp)
                  </label>
                  <input
                    type="number"
                    min="0"
                    value={newOrderForm.laborCost}
                    onChange={(e) => setNewOrderForm({ ...newOrderForm, laborCost: e.target.value })}
                    className="w-full px-3 py-2.5 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                    placeholder="0"
                  />
                  <p className="text-[10px] text-slate-500 mt-1">Nilai dalam rupiah penuh</p>
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                    Biaya Overhead (Rp)
                  </label>
                  <input
                    type="number"
                    min="0"
                    value={newOrderForm.overheadCost}
                    onChange={(e) => setNewOrderForm({ ...newOrderForm, overheadCost: e.target.value })}
                    className="w-full px-3 py-2.5 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                    placeholder="0"
                  />
                  <p className="text-[10px] text-slate-500 mt-1">Gas, listrik, dll.</p>
                </div>
              </div>

              {/* Footer */}
              <div className="flex items-center justify-end gap-3 pt-2">
                <button
                  type="button"
                  onClick={() => setNewOrderModalOpen(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-400 hover:text-white"
                >
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={newOrderSubmitting}
                  className="flex items-center gap-2 px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-xs shadow-lg shadow-amber-500/20 disabled:opacity-50"
                >
                  <Plus className="w-4 h-4" />
                  <span>{newOrderSubmitting ? 'Menyimpan...' : 'Buat SPK'}</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Modal: Batalkan SPK */}
      {cancelModalOpen && cancelTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md overflow-hidden shadow-2xl">
            <div className="p-4 border-b border-slate-800 flex items-center justify-between bg-slate-950/50">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-lg bg-rose-500/10 border border-rose-500/20 flex items-center justify-center text-rose-400">
                  <Ban className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="text-base font-bold text-white">
                    Batalkan SPK: {cancelTarget.orderNumber || `SPK-${cancelTarget.id}`}
                  </h3>
                  <p className="text-xs text-slate-400">Pembatalan SPK tidak dapat diurungkan</p>
                </div>
              </div>
              <button
                onClick={() => setCancelModalOpen(false)}
                className="text-slate-400 hover:text-white p-1 rounded-lg"
              >
                <XCircle className="w-5 h-5" />
              </button>
            </div>
            <div className="p-5 space-y-4">
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Alasan Pembatalan (opsional)
                </label>
                <textarea
                  value={cancelReason}
                  onChange={(e) => setCancelReason(e.target.value)}
                  rows={3}
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-700 rounded-xl text-sm text-white focus:outline-none focus:border-rose-500/60 transition-colors resize-none"
                  placeholder="Contoh: Bahan baku habis, permintaan dibatalkan pelanggan, dll."
                />
              </div>
              <div className="flex items-center justify-end gap-3">
                <button
                  type="button"
                  onClick={() => setCancelModalOpen(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-400 hover:text-white"
                >
                  Kembali
                </button>
                <button
                  type="button"
                  onClick={handleSubmitCancel}
                  className="flex items-center gap-2 px-5 py-2.5 bg-gradient-to-r from-rose-500 to-rose-600 hover:from-rose-400 hover:to-rose-500 text-white font-bold rounded-xl text-xs shadow-lg shadow-rose-500/20"
                >
                  <Ban className="w-4 h-4" />
                  <span>Konfirmasi Batalkan SPK</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Modal: Penyelesaian Produksi & Pencatatan Scrap/Aktual */}
      {completeModalOpen && selectedOrder && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-3xl overflow-hidden shadow-2xl flex flex-col max-h-[90vh]">
            {/* Modal Header */}
            <div className="p-4 border-b border-slate-800 flex items-center justify-between bg-slate-950/50">
              <div className="flex items-center gap-2.5">
                <div className="w-8 h-8 rounded-lg bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-400">
                  <CheckCircle2 className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="text-base font-bold text-white">
                    Penyelesaian SPK: {selectedOrder.orderNumber || `SPK-${selectedOrder.id}`}
                  </h3>
                  <p className="text-xs text-slate-400">
                    Masukkan hasil aktual produksi dan periksa sisa bahan baku di dapur
                  </p>
                </div>
              </div>
              <button
                onClick={() => setCompleteModalOpen(false)}
                className="text-slate-400 hover:text-white p-1 rounded-lg"
              >
                <XCircle className="w-5 h-5" />
              </button>
            </div>

            {/* Modal Body */}
            <div className="p-5 overflow-y-auto space-y-6">
              {/* Seksi 1: Perhitungan Stok Dapur Riil */}
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <h4 className="text-xs font-bold uppercase tracking-wider text-amber-400 flex items-center gap-1.5">
                    <Scale className="w-4 h-4" />
                    <span>Perhitungan Pemakaian Bahan Baku di Dapur</span>
                  </h4>
                  <span className="text-[11px] text-slate-400">Sistem otomatis menghitung sisa stok dapur</span>
                </div>

                {loadingMaterials ? (
                  <div className="text-center py-4 text-slate-400 text-xs">Memuat data bahan baku...</div>
                ) : scrapData.actualIngredients.length === 0 ? (
                  <div className="text-center py-4 text-slate-500 text-xs italic">
                    Tidak ada data bahan baku dari resep. Bahan baku akan diperbarui dari stok.
                  </div>
                ) : (
                  <div className="border border-slate-800 rounded-xl overflow-hidden">
                    <table className="w-full text-xs text-left">
                      <thead className="bg-slate-950/70 text-[10px] uppercase text-slate-400 border-b border-slate-800">
                        <tr>
                          <th className="py-2.5 px-3">Nama Bahan Baku</th>
                          <th className="py-2.5 px-3 text-center">Standar Resep</th>
                          <th className="py-2.5 px-3 text-center">Dipakai Aktual</th>
                          <th className="py-2.5 px-3 text-center font-bold text-emerald-400">Sisa Stok Dapur</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-slate-800/60">
                        {scrapData.actualIngredients.map((item, idx) => (
                          <tr key={idx} className="hover:bg-slate-800/30">
                            <td className="py-2.5 px-3 font-medium text-slate-200">{item.name}</td>
                            <td className="py-2.5 px-3 text-center text-slate-400">
                              {item.initialStock} {item.unit}
                            </td>
                            <td className="py-2.5 px-3 text-center">
                              <input
                                type="number"
                                step="0.1"
                                value={item.actualUsed}
                                onChange={(e) => handleIngredientChange(idx, 'actualUsed', e.target.value)}
                                className="w-20 px-2 py-1 bg-slate-950 text-center font-bold text-amber-400 border border-slate-700 rounded focus:outline-none focus:border-amber-500"
                              />
                              <span className="ml-1 text-[11px] text-slate-500">{item.unit}</span>
                            </td>
                            <td className="py-2.5 px-3 text-center font-bold text-emerald-400">
                              {item.remaining} {item.unit}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>

              {/* Seksi 2: Hasil Produksi & Scrap */}
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <h4 className="text-xs font-bold uppercase tracking-wider text-amber-400 flex items-center gap-1.5">
                    <Sparkles className="w-4 h-4" />
                    <span>Hasil Produksi & Catatan Kerusakan Roti</span>
                  </h4>
                  <span className="text-[11px] text-slate-400">
                    Target Rencana: <strong className="text-white">{selectedOrder.targetQuantity || selectedOrder.TargetQuantity || 100} Roti</strong>
                  </span>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                  {/* Roti Bagus */}
                  <div className="p-3 bg-emerald-500/5 border border-emerald-500/20 rounded-xl space-y-1.5">
                    <label className="text-xs font-bold text-emerald-400 flex items-center gap-1">
                      <CheckCircle2 className="w-3.5 h-3.5" />
                      <span>Roti Bagus (Lolos Uji)</span>
                    </label>
                    <input
                      type="number"
                      value={scrapData.goodUnits}
                      onChange={(e) => setScrapData({ ...scrapData, goodUnits: parseInt(e.target.value) || 0 })}
                      className="w-full px-3 py-2 bg-slate-950 text-emerald-400 font-black text-lg rounded-lg border border-emerald-500/30 focus:outline-none"
                    />
                    <p className="text-[10px] text-slate-400">Siap dipindahkan ke etalase toko</p>
                  </div>

                  {/* Roti Gagal */}
                  <div className="p-3 bg-amber-500/5 border border-amber-500/20 rounded-xl space-y-1.5">
                    <label className="text-xs font-bold text-amber-400 flex items-center gap-1">
                      <AlertTriangle className="w-3.5 h-3.5" />
                      <span>Roti Gagal / Bantat</span>
                    </label>
                    <input
                      type="number"
                      value={scrapData.scrapUnits}
                      onChange={(e) => setScrapData({ ...scrapData, scrapUnits: parseInt(e.target.value) || 0 })}
                      className="w-full px-3 py-2 bg-slate-950 text-amber-400 font-black text-lg rounded-lg border border-amber-500/30 focus:outline-none"
                    />
                    <select
                      value={scrapData.scrapReason}
                      onChange={(e) => setScrapData({ ...scrapData, scrapReason: e.target.value })}
                      className="w-full text-[11px] bg-slate-950 text-slate-300 p-1.5 rounded border border-slate-800"
                    >
                      <option value="Adonan gagal / bantat">Adonan gagal / bantat</option>
                      <option value="Gosong / suhu oven tidak stabil">Gosong / oven tidak stabil</option>
                      <option value="Salah takaran resep">Salah takaran resep</option>
                      <option value="Ragi tidak aktif">Ragi tidak aktif / mati</option>
                    </select>
                  </div>

                  {/* Roti Rusak Proses */}
                  <div className="p-3 bg-rose-500/5 border border-rose-500/20 rounded-xl space-y-1.5">
                    <label className="text-xs font-bold text-rose-400 flex items-center gap-1">
                      <XCircle className="w-3.5 h-3.5" />
                      <span>Rusak Saat Proses</span>
                    </label>
                    <input
                      type="number"
                      value={scrapData.damagedUnits}
                      onChange={(e) => setScrapData({ ...scrapData, damagedUnits: parseInt(e.target.value) || 0 })}
                      className="w-full px-3 py-2 bg-slate-950 text-rose-400 font-black text-lg rounded-lg border border-rose-500/30 focus:outline-none"
                    />
                    <select
                      value={scrapData.damagedReason}
                      onChange={(e) => setScrapData({ ...scrapData, damagedReason: e.target.value })}
                      className="w-full text-[11px] bg-slate-950 text-slate-300 p-1.5 rounded border border-slate-800"
                    >
                      <option value="Kerusakan mesin / mixer">Kerusakan mesin mixer / adukan</option>
                      <option value="Jatuh saat pemindahan loyang">Jatuh saat pemindahan loyang</option>
                      <option value="Bentuk loyang cacat">Bentuk loyang cacat</option>
                      <option value="Lainnya">Alasan lainnya</option>
                    </select>
                  </div>
                </div>

                {/* Catatan Rework */}
                <div className="p-3 bg-slate-950/60 border border-slate-800/80 rounded-xl flex items-center gap-3">
                  <input
                    type="checkbox"
                    id="reworkCheck"
                    checked={scrapData.canRework}
                    onChange={(e) => setScrapData({ ...scrapData, canRework: e.target.checked })}
                    className="w-4 h-4 rounded text-amber-500 focus:ring-0 bg-slate-900 border-slate-700"
                  />
                  <label htmlFor="reworkCheck" className="text-xs text-slate-300 select-none">
                    Roti gagal / bantat di atas bisa diolah kembali (<strong>Pengerjaan Ulang / Rework</strong>) menjadi tepung roti / olahan sekunder yang tercatat.
                  </label>
                </div>
              </div>
            </div>

            {/* Modal Footer */}
            <div className="p-4 border-t border-slate-800 bg-slate-950/70 flex items-center justify-end gap-3">
              <button
                type="button"
                onClick={() => setCompleteModalOpen(false)}
                className="px-4 py-2 text-xs font-semibold text-slate-400 hover:text-white"
              >
                Batal
              </button>
              <button
                type="button"
                onClick={handleSubmitComplete}
                className="flex items-center gap-2 px-5 py-2.5 bg-gradient-to-r from-emerald-500 to-emerald-600 hover:from-emerald-400 hover:to-emerald-500 text-slate-950 font-bold rounded-xl text-xs shadow-lg shadow-emerald-500/20"
              >
                <Check className="w-4 h-4" />
                <span>Simpan Hasil & Perbarui Stok Gudang</span>
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
