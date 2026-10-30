import React from 'react';
import { BarChart3, RotateCw, TrendingUp, Receipt, Wallet, Percent } from 'lucide-react';
import { formatMinor, formatDate } from '../utils/money';

// Angka diambil dari jurnal posted dan pesanan yang tidak dibatalkan (lihat AnalyticsService).
// Tidak ada target, proyeksi, atau "pertumbuhan %" karangan — kalau sewaan sepi, grafiknya datar.
// Uang disajikan dalam unit minor sesuai kontrak API, diformat lewat formatMinor.
export default function AnalyticsView({ analytics, loading, onRefresh }) {
  const a = analytics;
  const margin = a && a.totalPendapatan > 0 ? Math.round((a.labaKotor / a.totalPendapatan) * 1000) / 10 : null;

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Analisis & Tren Penjualan</h1>
          <p className="text-sm text-slate-400 mt-1">
            {a ? `Rentang ${formatDate(a.dari)} – ${formatDate(a.sampai)} · pendapatan & beban dari jurnal, pesanan dari daftar penjualan.` : 'Memuat rentang 90 hari terakhir...'}
          </p>
        </div>
        <button onClick={onRefresh} className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl border border-slate-700/60" title="Muat Ulang">
          <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
        </button>
      </div>

      {!a ? (
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-6 text-xs text-slate-400">Analisis gagal dimuat atau belum ada data.</div>
      ) : (
        <>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-4">
            <Kpi icon={TrendingUp} label="Pendapatan" nilai={formatMinor(a.totalPendapatan)} nada="emerald" />
            <Kpi icon={Wallet} label="Beban" nilai={formatMinor(a.totalBeban)} nada="rose" />
            <Kpi icon={TrendingUp} label="Selisih Pendapatan − Beban" nilai={formatMinor(a.labaKotor)} nada={a.labaKotor >= 0 ? 'emerald' : 'rose'} />
            <Kpi icon={Percent} label="Rasio Selisih" nilai={margin === null ? '-' : `${margin}%`} nada="amber" />
            <Kpi icon={Receipt} label="Pesanan" nilai={`${a.jumlahPesanan}`} sub={a.jumlahPesananBatal ? `${a.jumlahPesananBatal} batal` : 'tanpa pembatalan'} />
            <Kpi icon={Receipt} label="Rata-rata Pesanan" nilai={formatMinor(a.rataRataNilaiPesanan)} />
          </div>

          <div className="bg-slate-900/80 border border-slate-800 rounded-xl px-4 py-3 text-[11px] text-slate-400">
            Selisih dihitung dari seluruh pendapatan jurnal dikurangi seluruh beban jurnal, termasuk
            tenaga kerja dan overhead produksi. Penjualan belum dibebani harga pokok penjualan, jadi
            angka ini bukan laba kotor akuntansi dan tidak bisa dipakai sebagai margin kotor.
          </div>

          <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
            <div className="p-4 border-b border-slate-800 flex items-center gap-2">
              <BarChart3 className="w-4 h-4 text-amber-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Pendapatan harian</h3>
            </div>
            <div className="p-5">
              {a.harian.length === 0 ? (
                <p className="text-xs text-slate-400">Belum ada jurnal pendapatan pada rentang ini.</p>
              ) : (
                <GrafikBar data={a.harian} />
              )}
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
            <TabelJudul judul="Produk terlaris" kosong="Belum ada produk terjual pada rentang ini.">
              {a.produk.length === 0 ? null : (
                <table className="w-full text-left text-xs">
                  <thead className="text-[10px] uppercase tracking-wider text-slate-500">
                    <tr><th className="py-2">Produk</th><th className="py-2 text-right">Unit</th><th className="py-2 text-right">Nilai</th></tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60">
                    {a.produk.map((p, i) => (
                      <tr key={i}><td className="py-2 text-white font-semibold">{p.nama}</td><td className="py-2 text-right font-mono text-slate-300">{p.unitTerjual}</td><td className="py-2 text-right font-mono text-emerald-400">{formatMinor(p.nilai)}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </TabelJudul>

            <TabelJudul judul="Beban per akun" kosong="Belum ada beban tercatat pada rentang ini.">
              {a.beban.length === 0 ? null : (
                <table className="w-full text-left text-xs">
                  <thead className="text-[10px] uppercase tracking-wider text-slate-500">
                    <tr><th className="py-2">Akun</th><th className="py-2 text-right">Nominal</th></tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60">
                    {a.beban.map((b, i) => (
                      <tr key={i}><td className="py-2 text-white font-semibold">{b.nama} <span className="text-slate-500">({b.kode})</span></td><td className="py-2 text-right font-mono text-rose-400">{formatMinor(b.nominal)}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </TabelJudul>
          </div>
        </>
      )}
    </div>
  );
}

const NADA = {
  emerald: 'text-emerald-400', rose: 'text-rose-400', amber: 'text-amber-400', white: 'text-white',
};

function Kpi({ icon: Ikon, label, nilai, sub, nada = 'white' }) {
  return (
    <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
      <div className="flex items-center justify-between text-slate-400 text-[11px]">
        <span>{label}</span>
        <Ikon className={`w-4 h-4 ${NADA[nada]}`} />
      </div>
      <div className={`text-lg font-black mt-2 font-mono ${NADA[nada]}`}>{nilai}</div>
      {sub && <div className="text-[10px] text-slate-500 mt-1">{sub}</div>}
    </div>
  );
}

function GrafikBar({ data }) {
  const maks = Math.max(...data.map((d) => d.pendapatan), 1);
  return (
    <>
      <div className="flex items-end gap-1 h-40 overflow-x-auto custom-scrollbar pb-1">
        {data.map((d, i) => {
          const pct = Math.max(2, Math.round((d.pendapatan / maks) * 100));
          return (
            <div key={i} className="flex-1 min-w-[6px] flex flex-col items-center justify-end h-full group" title={`${formatDate(d.tanggal)} · ${formatMinor(d.pendapatan)}`}>
              <div className="w-full bg-amber-500/70 hover:bg-amber-400 rounded-t" style={{ height: `${pct}%` }} />
            </div>
          );
        })}
      </div>
      <div className="flex justify-between text-[10px] text-slate-500 mt-2">
        <span>{formatDate(data[0].tanggal)}</span>
        <span>{formatDate(data[data.length - 1].tanggal)}</span>
      </div>
    </>
  );
}

function TabelJudul({ judul, kosong, children }) {
  return (
    <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
      <div className="p-4 border-b border-slate-800">
        <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">{judul}</h3>
      </div>
      <div className="p-4">
        {children ?? <p className="text-xs text-slate-400">{kosong}</p>}
      </div>
    </div>
  );
}
