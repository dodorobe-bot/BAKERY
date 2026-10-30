import React, { useMemo, useState } from 'react';
import { QrCode, RotateCw, Plus, X } from 'lucide-react';
import { formatMinor, toMinor, formatDate } from '../utils/money';
import { api } from '../services/api';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';

// Nomor batch & kedaluwarsa. Daftarnya dibaca langsung dari ItemBatches — lot yang lahir dari
// penerimaan barang dan lot yang dicatat tangan di sini menyatu dalam satu tabel, jadi tidak ada
// daftar "kedaluwarsa" hiasan yang berdiri sendiri di luar basis data.
const STATUS_TONE = {
  aman: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
  segera: 'bg-amber-500/10 text-amber-400 border-amber-500/20',
  kedaluwarsa: 'bg-rose-500/10 text-rose-400 border-rose-500/20',
};

export default function BatchesView({ batches, ingredients, loading, onRefresh, canWrite }) {
  const [filter, setFilter] = useState('');
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ ingredientId: '', batchNumber: '', receivedDate: '', expiryDate: '', unitCost: '' });
  const [galat, setGalat] = useState('');
  const [pesan, setPesan] = useState('');
  const { sedangSimpan, mulai, selesai } = useKunciSimpan();
  const [kunci, setKunci] = useState(null);

  const daftar = useMemo(() => {
    const rows = batches ?? [];
    return filter ? rows.filter((b) => b.status === filter) : rows;
  }, [batches, filter]);

  const buka = () => {
    setForm({ ingredientId: '', batchNumber: '', receivedDate: '', expiryDate: '', unitCost: '' });
    setGalat(''); setPesan('');
    setKunci(kunciBaru('catat-lot'));
    setOpen(true);
  };

  const simpan = async (e) => {
    e.preventDefault();
    if (!mulai()) return;
    setGalat('');
    try {
      await api.createInventoryBatch({
        ingredientId: Number(form.ingredientId),
        batchNumber: form.batchNumber.trim(),
        receivedDate: form.receivedDate || null,
        expiryDate: form.expiryDate,
        unitCostMinorUnits: toMinor(form.unitCost || 0),
        idempotensi: kunci,
      });
      setOpen(false);
      setPesan('Lot tercatat. Daftar dimuat ulang.');
      await onRefresh();
    } catch (err) {
      setGalat(err.message || 'Gagal mencatat lot.');
    } finally {
      selesai();
    }
  };

  const bahanAktif = (ingredients ?? []).filter((i) => i.isActive !== false);

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Nomor Batch & Kedaluwarsa</h1>
          <p className="text-sm text-slate-400 mt-1">
            Lot bahan yang tercatat dari penerimaan maupun pencatatan tangan. Status dihitung dari tanggal kedaluwarsa terhadap hari bisnis.
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button onClick={onRefresh} className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl border border-slate-700/60" title="Muat Ulang">
            <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button onClick={buka} disabled={!canWrite || bahanAktif.length === 0}
            className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 disabled:opacity-40 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20">
            <Plus className="w-4 h-4" /><span>Catat Lot</span>
          </button>
        </div>
      </div>

      {!canWrite && (
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-4 text-xs text-slate-400">
          Peran Anda dapat melihat lot, tetapi tidak dapat mencatatnya. Pencatatan dilakukan Kepala Dapur atau ke atas.
        </div>
      )}

      {pesan && <div className="p-3 bg-emerald-500/10 border border-emerald-500/20 rounded-xl text-emerald-400 text-xs">{pesan}</div>}

      <div className="flex items-center gap-2">
        {['', 'aman', 'segera', 'kedaluwarsa'].map((s) => (
          <button key={s || 'all'} onClick={() => setFilter(s)}
            className={`px-3 py-1.5 rounded-lg text-xs font-bold border transition-all ${filter === s ? 'bg-amber-500/20 text-amber-400 border-amber-500/30' : 'text-slate-400 hover:text-white border-transparent'}`}>
            {s === '' ? 'Semua' : s}
          </button>
        ))}
        <span className="ml-auto text-[11px] text-slate-500">{daftar.length} lot</span>
      </div>

      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
        {daftar.length === 0 ? (
          <div className="p-6 text-xs text-slate-400">
            Belum ada lot {filter ? `berstatus ${filter}` : 'tercatat'}. Lot muncul otomatis saat barang diterima dengan nomor batch, atau lewat "Catat Lot".
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-3 px-4 font-semibold">Bahan</th>
                  <th className="py-3 px-4 font-semibold">Nomor Batch</th>
                  <th className="py-3 px-4 font-semibold">Diterima</th>
                  <th className="py-3 px-4 font-semibold">Kedaluwarsa</th>
                  <th className="py-3 px-4 font-semibold text-right">Sisa hari</th>
                  <th className="py-3 px-4 font-semibold text-right">Biaya/satuan</th>
                  <th className="py-3 px-4 font-semibold text-right">Status</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {daftar.map((b) => (
                  <tr key={b.id} className="hover:bg-slate-800/40">
                    <td className="py-3 px-4 font-semibold text-white">{b.ingredientName} <span className="text-slate-500">({b.unit})</span></td>
                    <td className="py-3 px-4 font-mono text-amber-400">{b.batchNumber}</td>
                    <td className="py-3 px-4 text-slate-400">{b.receivedDate ? formatDate(b.receivedDate) : '-'}</td>
                    <td className="py-3 px-4 text-slate-300">{formatDate(b.expiryDate)}</td>
                    <td className={`py-3 px-4 text-right font-mono ${b.daysToExpiry < 0 ? 'text-rose-400' : b.daysToExpiry <= 14 ? 'text-amber-400' : 'text-emerald-400'}`}>
                      {b.daysToExpiry}
                    </td>
                    <td className="py-3 px-4 text-right font-mono">{formatMinor(b.unitCostMinorUnits)}</td>
                    <td className="py-3 px-4 text-right">
                      <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold border ${STATUS_TONE[b.status] ?? 'bg-slate-800 text-slate-400 border-slate-700'}`}>
                        {b.status}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {open && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-slate-950/90 backdrop-blur-sm">
          <form onSubmit={simpan} className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <div>
                <h3 className="text-base font-bold text-white">Catat Lot Bahan</h3>
                <p className="text-xs text-slate-400 mt-1">Untuk bahan yang dibeli tunai lalu dikemas sendiri, di luar struk penerimaan.</p>
              </div>
              <button type="button" onClick={() => setOpen(false)} className="text-slate-400 hover:text-white"><X className="w-4 h-4" /></button>
            </div>
            <div className="p-5 space-y-4">
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Bahan</label>
                <select value={form.ingredientId} onChange={(e) => setForm({ ...form, ingredientId: e.target.value })}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required>
                  <option value="">Pilih bahan...</option>
                  {bahanAktif.map((i) => <option key={i.id} value={i.id}>{i.name} ({i.unit})</option>)}
                </select>
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Nomor batch</label>
                <input value={form.batchNumber} onChange={(e) => setForm({ ...form, batchNumber: e.target.value })}
                  placeholder="mis. GUL-2026-10-A" maxLength={60}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Tanggal terima</label>
                  <input type="date" value={form.receivedDate} onChange={(e) => setForm({ ...form, receivedDate: e.target.value })}
                    className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Kedaluwarsa *</label>
                  <input type="date" value={form.expiryDate} onChange={(e) => setForm({ ...form, expiryDate: e.target.value })}
                    className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required />
                </div>
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Biaya per satuan (Rp)</label>
                <input type="number" min="0" step="1" value={form.unitCost} onChange={(e) => setForm({ ...form, unitCost: e.target.value })}
                  placeholder="0"
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" />
              </div>
              {galat && <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>}
              <div className="flex justify-end gap-2">
                <button type="button" onClick={() => setOpen(false)} className="px-4 py-2.5 text-xs font-bold text-slate-400 hover:text-white">Batal</button>
                <button type="submit" disabled={sedangSimpan}
                  className="px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 text-slate-950 font-bold rounded-xl text-sm disabled:opacity-50">
                  {sedangSimpan ? 'Menyimpan...' : 'Simpan Lot'}
                </button>
              </div>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
