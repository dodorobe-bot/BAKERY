import React, { useMemo, useState } from 'react';
import { ShieldCheck, RotateCw, Plus, X, CheckCircle2, AlertTriangle } from 'lucide-react';
import { formatDateTime } from '../utils/money';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';

// Hasil pemeriksaan mutu disimpan per SPK. Daftar di sini adalah dokumen yang benar-benar
// tersimpan, bukan ringkasan dari angka di layar; SPK selesai yang belum diperiksa ditawarkan
// sebagai pekerjaan berikutnya.
const statusSpk = (v) => (typeof v === 'number' ? v : ({ Planned: 0, In_Process: 1, Completed: 2, Cancelled: 3 }[v] ?? -1));

const KEPUTUSAN_TONE = {
  Passed: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
  Failed: 'bg-rose-500/10 text-rose-400 border-rose-500/20',
};

export default function QualityView({
  qualityChecks, workOrders, loading, onRefresh, onCreateQualityCheck, canWrite,
}) {
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({
    workOrderId: '', sampleQuantity: '', passedQuantity: '', failedQuantity: '',
    appearanceNotes: '', tasteNotes: '', notes: '',
  });
  const [galat, setGalat] = useState('');
  const [pesan, setPesan] = useState('');
  const { sedangSimpan, mulai, selesai } = useKunciSimpan();
  const [kunci, setKunci] = useState(null);

  const sudahDiperiksa = useMemo(
    () => new Set((qualityChecks ?? []).map((q) => q.workOrderId)),
    [qualityChecks],
  );

  const menunggu = useMemo(
    () => (workOrders ?? []).filter((wo) => statusSpk(wo.status) === 2 && !sudahDiperiksa.has(wo.id)),
    [workOrders, sudahDiperiksa],
  );

  const buka = () => {
    setForm({
      workOrderId: '', sampleQuantity: '', passedQuantity: '', failedQuantity: '',
      appearanceNotes: '', tasteNotes: '', notes: '',
    });
    setGalat(''); setPesan('');
    setKunci(kunciBaru('qc'));
    setOpen(true);
  };

  const simpan = async (e) => {
    e.preventDefault();
    if (!mulai()) return;
    setGalat('');
    try {
      await onCreateQualityCheck({
        workOrderId: Number(form.workOrderId),
        sampleQuantity: Number(form.sampleQuantity),
        passedQuantity: Number(form.passedQuantity),
        failedQuantity: Number(form.failedQuantity || 0),
        appearanceNotes: form.appearanceNotes.trim() || null,
        tasteNotes: form.tasteNotes.trim() || null,
        notes: form.notes.trim() || null,
        idempotensi: kunci,
      });
      setOpen(false);
      setPesan('Hasil pemeriksaan tersimpan.');
      await onRefresh();
    } catch (err) {
      setGalat(err.message || 'Gagal menyimpan hasil pemeriksaan.');
    } finally {
      selesai();
    }
  };

  const totalLulus = (qualityChecks ?? []).reduce((s, q) => s + (q.passedQuantity || 0), 0);
  const totalGagal = (qualityChecks ?? []).reduce((s, q) => s + (q.failedQuantity || 0), 0);
  const gagalSpk = (qualityChecks ?? []).filter((q) => q.decision === 'Failed').length;

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Pemeriksaan Mutu (QC)</h1>
          <p className="text-sm text-slate-400 mt-1">
            Hasil pemeriksaan disimpan per SPK: jumlah sampel, lulus, gagal, catatan tampilan dan rasa.
            Keputusan lulus/gagal dihitung server dari angka yang dikirim.
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button onClick={onRefresh} className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl border border-slate-700/60" title="Muat Ulang">
            <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button onClick={buka} disabled={!canWrite || menunggu.length === 0}
            className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 disabled:opacity-40 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20">
            <Plus className="w-4 h-4" /><span>Periksa SPK</span>
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Sudah diperiksa</span><ShieldCheck className="w-4 h-4 text-emerald-400" /></div>
          <div className="text-2xl font-black text-white mt-2">{(qualityChecks ?? []).length} SPK</div>
          <div className="text-[11px] text-slate-500 mt-1">{gagalSpk} dengan temuan gagal</div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Unit lulus sampel</span><CheckCircle2 className="w-4 h-4 text-emerald-400" /></div>
          <div className="text-2xl font-black text-emerald-400 mt-2 font-mono">{totalLulus}</div>
          <div className="text-[11px] text-slate-500 mt-1">dari pemeriksaan yang tercatat</div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Unit gagal sampel</span><AlertTriangle className="w-4 h-4 text-rose-400" /></div>
          <div className="text-2xl font-black text-rose-400 mt-2 font-mono">{totalGagal}</div>
          <div className="text-[11px] text-slate-500 mt-1">perlu ditindaklanjuti dapur</div>
        </div>
      </div>

      {menunggu.length > 0 && (
        <div className="p-4 bg-amber-500/10 border border-amber-500/20 rounded-2xl text-xs text-amber-300">
          {menunggu.length} SPK selesai belum diperiksa: {menunggu.slice(0, 4).map((wo) => wo.orderNumber).join(', ')}
          {menunggu.length > 4 ? ', …' : ''}
        </div>
      )}

      {pesan && <div className="p-3 bg-emerald-500/10 border border-emerald-500/20 rounded-xl text-emerald-400 text-xs">{pesan}</div>}

      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Riwayat Pemeriksaan</h3>
          <span className="text-[11px] text-slate-500">{(qualityChecks ?? []).length} hasil</span>
        </div>
        {(qualityChecks ?? []).length === 0 ? (
          <div className="p-6 text-xs text-slate-400">
            Belum ada hasil pemeriksaan. Selesaikan SPK lebih dulu, lalu tekan "Periksa SPK".
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-3 px-4 font-semibold">Waktu</th>
                  <th className="py-3 px-4 font-semibold">SPK</th>
                  <th className="py-3 px-4 font-semibold">Produk</th>
                  <th className="py-3 px-4 font-semibold text-right">Sampel</th>
                  <th className="py-3 px-4 font-semibold text-right">Lulus</th>
                  <th className="py-3 px-4 font-semibold text-right">Gagal</th>
                  <th className="py-3 px-4 font-semibold">Keputusan</th>
                  <th className="py-3 px-4 font-semibold">Catatan</th>
                  <th className="py-3 px-4 font-semibold">Pemeriksa</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {(qualityChecks ?? []).map((q) => (
                  <tr key={q.id} className="hover:bg-slate-800/40">
                    <td className="py-3 px-4 text-slate-400">{formatDateTime(q.checkedAt)}</td>
                    <td className="py-3 px-4 font-mono text-amber-400">{q.orderNumber}</td>
                    <td className="py-3 px-4 font-semibold text-white">{q.productName || '-'}</td>
                    <td className="py-3 px-4 text-right font-mono">{q.sampleQuantity}</td>
                    <td className="py-3 px-4 text-right font-mono text-emerald-400">{q.passedQuantity}</td>
                    <td className="py-3 px-4 text-right font-mono text-rose-400">{q.failedQuantity}</td>
                    <td className="py-3 px-4">
                      <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold border ${KEPUTUSAN_TONE[q.decision] ?? 'bg-slate-800 text-slate-400 border-slate-700'}`}>
                        {q.decision === 'Passed' ? 'Lulus' : 'Gagal'}
                      </span>
                    </td>
                    <td className="py-3 px-4 text-slate-400 max-w-[16rem]">
                      {[q.appearanceNotes, q.tasteNotes, q.notes].filter(Boolean).join(' · ') || '-'}
                    </td>
                    <td className="py-3 px-4 text-slate-400">{q.checkerUsername || '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {open && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-slate-950/90 backdrop-blur-sm">
          <form onSubmit={simpan} className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg shadow-2xl overflow-hidden">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <div>
                <h3 className="text-base font-bold text-white">Catat Pemeriksaan Mutu</h3>
                <p className="text-xs text-slate-400 mt-1">Satu SPK hanya punya satu hasil pemeriksaan.</p>
              </div>
              <button type="button" onClick={() => setOpen(false)} className="text-slate-400 hover:text-white"><X className="w-4 h-4" /></button>
            </div>
            <div className="p-5 space-y-4">
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">SPK selesai</label>
                <select value={form.workOrderId} onChange={(e) => setForm({ ...form, workOrderId: e.target.value })}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required>
                  <option value="">Pilih SPK...</option>
                  {menunggu.map((wo) => (
                    <option key={wo.id} value={wo.id}>
                      {wo.orderNumber} · {wo.productName || `Produk ${wo.productTemplateId}`} · {wo.actualGoodQty} unit baik
                    </option>
                  ))}
                </select>
              </div>
              <div className="grid grid-cols-3 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Sampel *</label>
                  <input type="number" min="1" step="1" value={form.sampleQuantity}
                    onChange={(e) => setForm({ ...form, sampleQuantity: e.target.value })}
                    className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Lulus *</label>
                  <input type="number" min="0" step="1" value={form.passedQuantity}
                    onChange={(e) => setForm({ ...form, passedQuantity: e.target.value })}
                    className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Gagal *</label>
                  <input type="number" min="0" step="1" value={form.failedQuantity}
                    onChange={(e) => setForm({ ...form, failedQuantity: e.target.value })}
                    className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required />
                </div>
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Catatan tampilan</label>
                <input value={form.appearanceNotes} onChange={(e) => setForm({ ...form, appearanceNotes: e.target.value })}
                  placeholder="Warna, bentuk, kerataan..."
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Catatan rasa</label>
                <input value={form.tasteNotes} onChange={(e) => setForm({ ...form, tasteNotes: e.target.value })}
                  placeholder="Rasa, tekstur, aroma..."
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Catatan tambahan</label>
                <textarea value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} rows={2}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" />
              </div>
              {galat && <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>}
              <div className="flex justify-end gap-2">
                <button type="button" onClick={() => setOpen(false)} className="px-4 py-2.5 text-xs font-bold text-slate-400 hover:text-white">Batal</button>
                <button type="submit" disabled={sedangSimpan}
                  className="px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 text-slate-950 font-bold rounded-xl text-sm disabled:opacity-50">
                  {sedangSimpan ? 'Menyimpan...' : 'Simpan Hasil'}
                </button>
              </div>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
