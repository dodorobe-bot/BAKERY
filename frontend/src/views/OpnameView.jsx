import React, { useMemo, useState } from 'react';
import { ClipboardCheck, RotateCw, Save } from 'lucide-react';
import { formatQty } from '../utils/money';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';
import { useBarisBertahap } from '../utils/bertahap';

// Pemeriksaan fisik (stock opname): menghitung ulang isi rak dan menyetor selisihnya lewat jalur
// penyesuaian stok resmi, bukan lewat kolom cache. Angka sistem yang tampil di sini adalah saldo
// LocationStock — sama seperti yang dipakai laporan nilai persediaan, jadi tidak ada dua sumber.
export default function OpnameView({
  locations, ingredients, stockByLocation, loading, onRefresh, onAdjust, canWrite,
}) {
  const aktif = (locations ?? []).filter((l) => l.isActive !== false);
  const [locationId, setLocationId] = useState(aktif[0]?.id ?? '');
  const [query, setQuery] = useState('');
  const [hitung, setHitung] = useState({});   // ingredientId -> jumlah fisik
  const [alasan, setAlasan] = useState('');
  const [pesan, setPesan] = useState('');
  const [galat, setGalat] = useState('');
  const [kunci, setKunci] = useState(null);
  const { sedangSimpan, mulai, selesai } = useKunciSimpan();

  const currentLocation = aktif.find((l) => l.id === Number(locationId));
  const rows = useMemo(() => {
    const held = new Map((stockByLocation?.[currentLocation?.id] ?? []).map((r) => [r.ingredientId, r]));
    return (ingredients ?? [])
      .filter((ing) => ing.isActive !== false)
      .map((ing) => ({
        ingredientId: ing.id,
        name: ing.name,
        unit: ing.unit ?? held.get(ing.id)?.unit ?? '-',
        systemQty: Number(held.get(ing.id)?.quantity ?? 0),
        minStock: Number(ing.minStock ?? ing.minStockAlert ?? 0),
      }))
      .filter((r) => !query.trim() || r.name.toLowerCase().includes(query.toLowerCase()))
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [ingredients, stockByLocation, currentLocation, query]);

  const selisih = (row) => {
    const fisik = hitung[row.ingredientId];
    if (fisik === undefined || fisik === '') return null;
    return Number(fisik) - row.systemQty;
  };
  const adaSelisih = rows.filter((r) => selisih(r) !== null && selisih(r) !== 0);
  const barisOpname = useBarisBertahap(rows);

  const bukaForm = (row) => {
    setPesan(''); setGalat('');
    setKunci(kunciBaru(`opname-${currentLocation?.id}-${row.ingredientId}`));
    setHitung((h) => ({ ...h, [row.ingredientId]: String(row.systemQty) }));
  };

  const setelUlang = (row) => {
    setKunci(kunciBaru(`opname-${currentLocation?.id}-${row.ingredientId}`));
    setHitung((h) => { const s = { ...h }; delete s[row.ingredientId]; return s; });
  };

  const simpan = async (row) => {
    if (!mulai()) return;
    setPesan(''); setGalat('');
    try {
      await onAdjust({
        locationId: Number(currentLocation.id),
        ingredientId: Number(row.ingredientId),
        newQuantity: Number(hitung[row.ingredientId]),
        reason: alasan || 'Pemeriksaan fisik (stock opname)',
        idempotensi: kunci,
      });
      setPesan(`${row.name}: stok disetel ke ${formatQty(hitung[row.ingredientId])} ${row.unit}.`);
      setHitung((h) => { const s = { ...h }; delete s[row.ingredientId]; return s; });
    } catch (err) {
      setGalat(err.message || 'Koreksi stok gagal disimpan.');
    } finally {
      selesai();
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Pemeriksaan Fisik (Stok Opname)</h1>
          <p className="text-sm text-slate-400 mt-1">
            Hitung isi rak, lalu setel saldo sistem ke jumlah fisik. Selisih dibukukan sebagai penyesuaian dan tercatat di jejak audit.
          </p>
        </div>
        <button onClick={onRefresh} className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl border border-slate-700/60" title="Muat Ulang">
          <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
        </button>
      </div>

      {aktif.length === 0 && (
        <div className="bg-amber-500/10 border border-amber-500/20 rounded-2xl p-4 text-xs text-amber-300">
          Belum ada lokasi aktif, jadi tidak ada rak yang bisa dihitung.
        </div>
      )}

      {canWrite === false && (
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-4 text-xs text-slate-400">
          Peran Anda dapat melihat hasil opname, tetapi tidak dapat menyetel stok. Koreksi dilakukan Kepala Dapur atau ke atas.
        </div>
      )}

      {aktif.length > 0 && (
        <>
          <div className="flex flex-wrap items-center gap-3">
            <select value={locationId} onChange={(e) => { setLocationId(e.target.value); setHitung({}); setPesan(''); }}
              className="px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60">
              {aktif.map((l) => <option key={l.id} value={l.id}>{l.name} ({l.code})</option>)}
            </select>
            <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Cari bahan..."
              className="flex-1 min-w-[14rem] px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" />
          </div>

          {adaSelisih.length > 0 && (
            <div className="flex flex-wrap items-center gap-3 bg-slate-900/80 border border-slate-800 rounded-2xl p-4">
              <label className="text-xs font-semibold text-slate-400">Alasan opname</label>
              <input value={alasan} onChange={(e) => setAlasan(e.target.value)} placeholder="mis. selisih hitung fisik 27 Okt"
                className="flex-1 min-w-[14rem] px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-xs text-white focus:outline-none focus:border-amber-500/60" />
            </div>
          )}

          {pesan && <div className="p-3 bg-emerald-500/10 border border-emerald-500/20 rounded-xl text-emerald-400 text-xs">{pesan}</div>}
          {galat && <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>}

          <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
            <div className="p-4 border-b border-slate-800 flex items-center gap-2">
              <ClipboardCheck className="w-4 h-4 text-amber-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">
                {currentLocation?.name} — {rows.length} bahan
              </h3>
            </div>
            {rows.length === 0 && (
              <div className="p-6 text-xs text-slate-400">
                {ingredients?.length ? 'Tidak ada bahan yang cocok dengan pencarian.' : 'Belum ada bahan baku di katalog.'}
              </div>
            )}
            {rows.length > 0 && (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs text-slate-300">
                  <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="py-3 px-4 font-semibold">Bahan</th>
                      <th className="py-3 px-4 font-semibold text-right">Saldo sistem</th>
                      <th className="py-3 px-4 font-semibold text-right">Jumlah fisik</th>
                      <th className="py-3 px-4 font-semibold text-right">Selisih</th>
                      <th className="py-3 px-4 font-semibold text-right">Aksi</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60">
                    {barisOpname.tampil.map((r) => {
                      const d = selisih(r);
                      return (
                        <tr key={r.ingredientId} className="hover:bg-slate-800/40">
                          <td className="py-3 px-4 font-semibold text-white">{r.name} <span className="text-slate-500">({r.unit})</span></td>
                          <td className="py-3 px-4 text-right font-mono">{formatQty(r.systemQty)}</td>
                          <td className="py-3 px-4 text-right">
                            <input
                              type="number" step="any" min="0" disabled={!canWrite}
                              value={hitung[r.ingredientId] ?? ''}
                              onChange={(e) => {
                                if (!kunci) setKunci(kunciBaru(`opname-${currentLocation?.id}-${r.ingredientId}`));
                                setHitung((h) => ({ ...h, [r.ingredientId]: e.target.value }));
                              }}
                              placeholder={String(r.systemQty)}
                              className="w-24 px-2 py-1.5 bg-slate-950 border border-slate-800 rounded-lg text-xs text-white text-right focus:outline-none focus:border-amber-500/60 disabled:opacity-40"
                            />
                          </td>
                          <td className={`py-3 px-4 text-right font-mono ${d === null || d === 0 ? 'text-slate-500' : d > 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
                            {d === null ? '-' : `${d > 0 ? '+' : ''}${formatQty(d)}`}
                          </td>
                          <td className="py-3 px-4 text-right whitespace-nowrap">
                            {d !== null && d !== 0 ? (
                              <>
                                <button onClick={() => simpan(r)} disabled={!canWrite || sedangSimpan}
                                  className="px-3 py-1 text-[11px] font-bold text-slate-950 bg-amber-500 hover:bg-amber-400 rounded-lg disabled:opacity-40">
                                  <Save className="w-3 h-3 inline mr-1" />Setel
                                </button>
                                <button onClick={() => setelUlang(r)} className="ml-2 px-2.5 py-1 text-[11px] font-bold text-slate-400 hover:text-white">
                                  Batal
                                </button>
                              </>
                            ) : (
                              <button onClick={() => bukaForm(r)} disabled={!canWrite}
                                className="px-2.5 py-1 text-[11px] font-bold text-amber-400 hover:bg-amber-500/10 border border-amber-500/20 rounded-lg disabled:opacity-30">
                                Hitung
                              </button>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
            {barisOpname.adaLagi && (
              <div className="px-4 py-3 border-t border-slate-800 text-center">
                <button
                  onClick={barisOpname.tampilkanLagi}
                  className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                >
                  Tampilkan 100 baris berikutnya ({barisOpname.jumlah - barisOpname.tampil.length} tersisa)
                </button>
              </div>
            )}
          </div>
        </>
      )}
    </div>
  );
}
