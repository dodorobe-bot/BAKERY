import React, { useMemo, useState } from 'react';
import { Calculator, RotateCw, TrendingUp, TrendingDown, AlertTriangle } from 'lucide-react';
import { formatMinor, formatQty } from '../utils/money';

// Dua angka berdampingan: HPP standar dari resep hari ini, dan HPP aktual per unit dari SPK
// yang sudah selesai. Selisihnya adalah pertanyaan yang harus dijawab pemilik, bukan hiasan.
export default function CostView({ summary, loading, onRefresh }) {
  const [hanyaBermasalah, setHanyaBermasalah] = useState(false);

  const rows = useMemo(() => {
    const daftar = summary?.products ?? [];
    return hanyaBermasalah
      ? daftar.filter((r) => r.belowStandardCost || (r.hasActual && r.varianceMinorUnits > 0))
      : daftar;
  }, [summary, hanyaBermasalah]);

  const s = summary;

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Harga Pokok Produksi (HPP)</h1>
          <p className="text-sm text-slate-400 mt-1">
            HPP standar dihitung dari resep × harga bahan saat ini; HPP aktual dari bahan aktual,
            tenaga kerja, dan overhead pada SPK yang sudah selesai. Margin memakai harga jual katalog.
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button onClick={onRefresh} className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl border border-slate-700/60" title="Muat Ulang">
            <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Produk aktif</span><Calculator className="w-4 h-4 text-amber-400" /></div>
          <div className="text-2xl font-black text-white mt-2">{s ? s.productCount : '-'}</div>
          <div className="text-[11px] text-slate-500 mt-1">{s ? `${s.productsWithRecipe} beresep · ${s.productsWithoutRecipe} tanpa resep` : 'Memuat data...'}</div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>HPP aktual tertimbang</span><TrendingUp className="w-4 h-4 text-blue-400" /></div>
          <div className="text-2xl font-black text-white mt-2 font-mono">{s ? formatMinor(s.weightedActualHppMinorUnits) : '-'}</div>
          <div className="text-[11px] text-slate-500 mt-1">{s ? `${formatQty(s.goodUnits)} unit baik dari ${s.completedWorkOrders} SPK` : 'Memuat data...'}</div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Harga jual di bawah HPP</span><AlertTriangle className="w-4 h-4 text-rose-400" /></div>
          <div className={`text-2xl font-black mt-2 ${s && s.productsBelowCost > 0 ? 'text-rose-400' : 'text-emerald-400'}`}>{s ? s.productsBelowCost : '-'}</div>
          <div className="text-[11px] text-slate-500 mt-1">produk yang menjual di bawah biaya resep</div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Biaya SPK selesai</span><TrendingDown className="w-4 h-4 text-emerald-400" /></div>
          <div className="text-2xl font-black text-white mt-2 font-mono">{s ? formatMinor(s.materialCostMinorUnits + s.laborCostMinorUnits + s.overheadCostMinorUnits) : '-'}</div>
          <div className="text-[11px] text-slate-500 mt-1">{s ? `bahan ${formatMinor(s.materialCostMinorUnits)} · tenaga ${formatMinor(s.laborCostMinorUnits)} · overhead ${formatMinor(s.overheadCostMinorUnits)}` : 'Memuat data...'}</div>
        </div>
      </div>

      <div className="flex items-center gap-2">
        <button onClick={() => setHanyaBermasalah(false)}
          className={`px-3 py-1.5 rounded-lg text-xs font-bold border transition-all ${!hanyaBermasalah ? 'bg-amber-500/20 text-amber-400 border-amber-500/30' : 'text-slate-400 hover:text-white border-transparent'}`}>
          Semua produk
        </button>
        <button onClick={() => setHanyaBermasalah(true)}
          className={`px-3 py-1.5 rounded-lg text-xs font-bold border transition-all ${hanyaBermasalah ? 'bg-amber-500/20 text-amber-400 border-amber-500/30' : 'text-slate-400 hover:text-white border-transparent'}`}>
          Perlu perhatian
        </button>
        <span className="ml-auto text-[11px] text-slate-500">{rows.length} produk</span>
      </div>

      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
        {rows.length === 0 ? (
          <div className="p-6 text-xs text-slate-400">
            {hanyaBermasalah
              ? 'Tidak ada produk yang harganya di bawah HPP atau biaya aktualnya melebihi standar.'
              : 'Belum ada produk aktif. Tambahkan produk dan resep di menu Resep & Daftar Bahan Baku.'}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-3 px-4 font-semibold">Produk</th>
                  <th className="py-3 px-4 font-semibold text-right">Harga jual</th>
                  <th className="py-3 px-4 font-semibold text-right">HPP standar</th>
                  <th className="py-3 px-4 font-semibold text-right">Margin standar</th>
                  <th className="py-3 px-4 font-semibold text-right">SPK selesai</th>
                  <th className="py-3 px-4 font-semibold text-right">HPP aktual/unit</th>
                  <th className="py-3 px-4 font-semibold text-right">Selisih</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {rows.map((r) => (
                  <tr key={r.productId} className="hover:bg-slate-800/40">
                    <td className="py-3 px-4">
                      <div className="font-semibold text-white">{r.name}</div>
                      <div className="text-[10px] text-slate-500">{r.category || 'Tanpa kategori'} · {r.recipeLineCount} bahan resep</div>
                    </td>
                    <td className="py-3 px-4 text-right font-mono">{r.sellingPriceMinorUnits > 0 ? formatMinor(r.sellingPriceMinorUnits) : <span className="text-rose-400">belum diisi</span>}</td>
                    <td className="py-3 px-4 text-right font-mono">{r.recipeLineCount > 0 ? formatMinor(r.standardHppMinorUnits) : <span className="text-slate-500">tanpa resep</span>}</td>
                    <td className={`py-3 px-4 text-right font-mono ${r.belowStandardCost ? 'text-rose-400' : r.standardMarginPercent >= 30 ? 'text-emerald-400' : 'text-amber-400'}`}>
                      {r.sellingPriceMinorUnits > 0 ? `${(Number(r.standardMarginPercent) || 0).toFixed(1)}%` : '-'}
                    </td>
                    <td className="py-3 px-4 text-right font-mono">{r.hasActual ? `${r.completedWorkOrders} · ${formatQty(r.goodUnits)} u` : <span className="text-slate-500">belum ada</span>}</td>
                    <td className="py-3 px-4 text-right font-mono">{r.hasActual ? formatMinor(r.actualHppMinorUnits) : <span className="text-slate-500">-</span>}</td>
                    <td className={`py-3 px-4 text-right font-mono ${!r.hasActual ? 'text-slate-500' : r.varianceMinorUnits > 0 ? 'text-rose-400' : 'text-emerald-400'}`}>
                      {r.hasActual
                        ? `${r.varianceMinorUnits > 0 ? '+' : ''}${formatMinor(r.varianceMinorUnits)}${r.variancePercent != null ? ` (${r.variancePercent > 0 ? '+' : ''}${Number(r.variancePercent).toFixed(1)}%)` : ''}`
                        : '-'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <p className="text-[11px] text-slate-500">
        HPP aktual dihitung hanya dari SPK berstatus selesai. Produk yang belum pernah diproduksi
        tetap menampilkan HPP standar resep, bukan angka perkiraan.
      </p>
    </div>
  );
}
