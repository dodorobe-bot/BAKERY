import React, { useMemo, useState } from 'react';
import { RotateCcw, RotateCw, Plus, X, Receipt } from 'lucide-react';
import { formatMinor, formatQty, formatDateTime } from '../utils/money';
import { orderStatusName } from '../utils/enums';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';

// Retur penjualan: uang keluar, stok produk jadi kembali ke etalase, dan jurnal pembalik
// pendapatan tercatat bersama dokumennya. Hanya pesanan lunas yang bisa diretur — pesanan
// berjalan dibatalkan lewat jalur pembatalan, bukan dikembalikan uangnya sebagian.
export default function ReturnsView({
  returns, orders, loading, onRefresh, onCreateReturn, canWrite,
}) {
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ orderId: '', quantity: '', reason: '', method: 'Tunai' });
  const [galat, setGalat] = useState('');
  const [pesan, setPesan] = useState('');
  const { sedangSimpan, mulai, selesai } = useKunciSimpan();
  const [kunci, setKunci] = useState(null);

  const lunas = useMemo(
    () => (orders ?? []).filter((o) => orderStatusName(o.status) === 'Paid'),
    [orders],
  );

  // Sisa yang boleh diretur dihitung dari dokumen retur yang sudah ada, bukan dari isian form.
  const terpakai = useMemo(() => {
    const map = new Map();
    for (const r of returns ?? []) {
      const ada = map.get(r.salesOrderId) ?? { qty: 0, amount: 0 };
      map.set(r.salesOrderId, { qty: ada.qty + Number(r.quantity || 0), amount: ada.amount + Number(r.amount || 0) });
    }
    return map;
  }, [returns]);

  const terpilih = lunas.find((o) => String(o.id) === String(form.orderId));
  const dipakai = terpilih ? (terpakai.get(terpilih.id) ?? { qty: 0, amount: 0 }) : { qty: 0, amount: 0 };
  const sisaQty = terpilih ? Number(terpilih.quantity || 0) - dipakai.qty : 0;
  const sisaNilai = terpilih ? Number(terpilih.totalAmount || 0) - dipakai.amount : 0;

  // Nilai uang mengikuti jumlah unit, sama seperti aturan di server. Retur terakhir memakai
  // sisa nilai apa adanya supaya total retur menutup pesanan tepat tanpa selisih pembulatan.
  const qtyRetur = Number(form.quantity || 0);
  const nilaiRetur = !terpilih || qtyRetur <= 0
    ? 0
    : qtyRetur === sisaQty
      ? sisaNilai
      : Math.round((qtyRetur * Number(terpilih.totalAmount || 0)) / Number(terpilih.quantity || 1));

  const buka = () => {
    setForm({ orderId: '', quantity: '', reason: '', method: 'Tunai' });
    setGalat(''); setPesan('');
    setKunci(kunciBaru('retur'));
    setOpen(true);
  };

  const pilihPesanan = (value) => {
    const o = lunas.find((x) => String(x.id) === String(value));
    const dip = o ? (terpakai.get(o.id) ?? { qty: 0, amount: 0 }) : { qty: 0, amount: 0 };
    setForm({
      ...form,
      orderId: value,
      quantity: o ? String(Math.max(0, Number(o.quantity || 0) - dip.qty)) : '',
    });
  };

  const simpan = async (e) => {
    e.preventDefault();
    if (!mulai()) return;
    setGalat('');
    try {
      await onCreateReturn({
        salesOrderId: Number(form.orderId),
        quantity: qtyRetur,
        amount: nilaiRetur,
        reason: form.reason.trim(),
        refundMethod: form.method,
        idempotensi: kunci,
      });
      setOpen(false);
      setPesan('Retur tercatat, stok kembali ke etalase, dan jurnal pengembalian sudah dibukukan.');
      await onRefresh();
    } catch (err) {
      setGalat(err.message || 'Gagal menyimpan retur.');
    } finally {
      selesai();
    }
  };

  const totalNilai = (returns ?? []).reduce((s, r) => s + Number(r.amount || 0), 0);
  const totalUnit = (returns ?? []).reduce((s, r) => s + Number(r.quantity || 0), 0);

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">Retur Penjualan</h1>
          <p className="text-sm text-slate-400 mt-1">
            Pengembalian barang dari pesanan lunas. Uang kembali ke pelanggan, stok produk jadi
            kembali ke etalase, dan pendapatan dikurangi lewat jurnal.
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button onClick={onRefresh} className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl border border-slate-700/60" title="Muat Ulang">
            <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button onClick={buka} disabled={!canWrite || lunas.length === 0}
            className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 disabled:opacity-40 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20">
            <Plus className="w-4 h-4" /><span>Catat Retur</span>
          </button>
        </div>
      </div>

      {!canWrite && (
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-4 text-xs text-slate-400">
          Peran Anda dapat melihat riwayat retur, tetapi tidak dapat mencatatnya. Retur dilakukan Owner atau Admin.
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Dokumen retur</span><RotateCcw className="w-4 h-4 text-amber-400" /></div>
          <div className="text-2xl font-black text-white mt-2">{(returns ?? []).length}</div>
          <div className="text-[11px] text-slate-500 mt-1">seluruh riwayat</div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Unit dikembalikan</span><Receipt className="w-4 h-4 text-blue-400" /></div>
          <div className="text-2xl font-black text-white mt-2 font-mono">{formatQty(totalUnit)}</div>
          <div className="text-[11px] text-slate-500 mt-1">kembali ke stok etalase</div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-5 rounded-2xl">
          <div className="flex items-center justify-between text-slate-400 text-xs"><span>Uang dikembalikan</span><RotateCcw className="w-4 h-4 text-rose-400" /></div>
          <div className="text-2xl font-black text-rose-400 mt-2 font-mono">{formatMinor(totalNilai)}</div>
          <div className="text-[11px] text-slate-500 mt-1">mengurangi pendapatan</div>
        </div>
      </div>

      {pesan && <div className="p-3 bg-emerald-500/10 border border-emerald-500/20 rounded-xl text-emerald-400 text-xs">{pesan}</div>}
      {!canWrite && (orders ?? []).some((o) => orderStatusName(o.status) === 'Paid') && (
        <div className="text-[11px] text-slate-500">
          {lunas.length} pesanan lunas tersedia sebagai sumber retur.
        </div>
      )}

      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Riwayat Retur</h3>
          <span className="text-[11px] text-slate-500">{(returns ?? []).length} dokumen</span>
        </div>
        {(returns ?? []).length === 0 ? (
          <div className="p-6 text-xs text-slate-400">
            Belum ada retur. Setelah pesanan lunas, tekan "Catat Retur" untuk mengembalikan barang
            dan dana pelanggan.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-3 px-4 font-semibold">Nomor retur</th>
                  <th className="py-3 px-4 font-semibold">Waktu</th>
                  <th className="py-3 px-4 font-semibold">Pesanan</th>
                  <th className="py-3 px-4 font-semibold">Pelanggan</th>
                  <th className="py-3 px-4 font-semibold">Produk</th>
                  <th className="py-3 px-4 font-semibold text-right">Jumlah</th>
                  <th className="py-3 px-4 font-semibold text-right">Nilai</th>
                  <th className="py-3 px-4 font-semibold">Metode</th>
                  <th className="py-3 px-4 font-semibold">Alasan</th>
                  <th className="py-3 px-4 font-semibold">Dicatat oleh</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {(returns ?? []).map((r) => (
                  <tr key={r.id} className="hover:bg-slate-800/40">
                    <td className="py-3 px-4 font-mono text-amber-400">{r.returnNumber}</td>
                    <td className="py-3 px-4 text-slate-400">{formatDateTime(r.returnedAt)}</td>
                    <td className="py-3 px-4 font-mono text-slate-300">{r.orderNumber || `#${r.salesOrderId}`}</td>
                    <td className="py-3 px-4 font-semibold text-white">{r.customerName}</td>
                    <td className="py-3 px-4 text-slate-300">{r.productName || '-'}</td>
                    <td className="py-3 px-4 text-right font-mono">{formatQty(r.quantity)}</td>
                    <td className="py-3 px-4 text-right font-mono text-rose-400">{formatMinor(r.amount)}</td>
                    <td className="py-3 px-4 text-slate-300">{r.refundMethod}</td>
                    <td className="py-3 px-4 text-slate-400 max-w-[16rem]">{r.reason}</td>
                    <td className="py-3 px-4 text-slate-400">{r.createdByUsername || '-'}</td>
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
                <h3 className="text-base font-bold text-white">Catat Retur Penjualan</h3>
                <p className="text-xs text-slate-400 mt-1">Hanya pesanan lunas; nilai dibatasi sisa pesanan.</p>
              </div>
              <button type="button" onClick={() => setOpen(false)} className="text-slate-400 hover:text-white"><X className="w-4 h-4" /></button>
            </div>
            <div className="p-5 space-y-4">
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Pesanan lunas</label>
                <select value={form.orderId} onChange={(e) => pilihPesanan(e.target.value)}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required>
                  <option value="">Pilih pesanan...</option>
                  {lunas.map((o) => {
                    const dip = terpakai.get(o.id) ?? { qty: 0, amount: 0 };
                    const sisa = Number(o.quantity || 0) - dip.qty;
                    return (
                      <option key={o.id} value={o.id} disabled={sisa <= 0}>
                        {o.orderNumber || `#${o.id}`} · {o.customerNameSnapshot} · sisa {formatQty(sisa)} unit
                      </option>
                    );
                  })}
                </select>
              </div>

              {terpilih && (
                <div className="p-3 bg-slate-950/60 border border-slate-800 rounded-xl text-[11px] text-slate-400">
                  Pesanan {terpilih.orderNumber || `#${terpilih.id}`} · {formatQty(terpilih.quantity)} unit ·
                  {' '}{formatMinor(terpilih.totalAmount)}.
                  {' '}Sudah diretur: {formatQty(dipakai.qty)} unit / {formatMinor(dipakai.amount)}.
                  {' '}Sisa: {formatQty(sisaQty)} unit / {formatMinor(sisaNilai)}.
                </div>
              )}

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Jumlah unit *</label>
                  <input type="number" min="0.001" step="0.001" max={sisaQty || undefined} value={form.quantity}
                    onChange={(e) => setForm({ ...form, quantity: e.target.value })}
                    className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" required />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Nilai retur (mengikuti jumlah)</label>
                  <div className="w-full px-4 py-2.5 bg-slate-950/60 border border-slate-800 rounded-xl text-sm font-mono text-amber-400">
                    {nilaiRetur > 0 ? formatMinor(nilaiRetur) : '-'}
                  </div>
                  <p className="text-[10px] text-slate-500 mt-1">Dihitung dari harga pesanan per unit, tidak bisa diisi bebas.</p>
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Metode pengembalian dana</label>
                <select value={form.method} onChange={(e) => setForm({ ...form, method: e.target.value })}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60">
                  <option value="Tunai">Tunai</option>
                  <option value="Transfer">Transfer</option>
                </select>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Alasan retur *</label>
                <textarea value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })}
                  rows={2} maxLength={300} required placeholder="mis. produk tidak sesuai pesanan"
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60" />
              </div>

              {galat && <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>}
              <div className="flex justify-end gap-2">
                <button type="button" onClick={() => setOpen(false)} className="px-4 py-2.5 text-xs font-bold text-slate-400 hover:text-white">Batal</button>
                <button type="submit" disabled={sedangSimpan}
                  className="px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 text-slate-950 font-bold rounded-xl text-sm disabled:opacity-50">
                  {sedangSimpan ? 'Menyimpan...' : 'Simpan Retur'}
                </button>
              </div>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
