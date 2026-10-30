import React, { useEffect, useState } from 'react';
import {
  Printer,
  RefreshCw,
  CheckCircle2,
  AlertCircle,
  ScanLine,
  HardDrive,
  Info,
  Send
} from 'lucide-react';

import { formatDateTime } from '../utils/money';

const labelStatus = (status) => {
  if (status === 'Printed') return { teks: 'Tercetak', cls: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' };
  if (status === 'Failed') return { teks: 'Gagal', cls: 'bg-rose-500/10 text-rose-400 border-rose-500/20' };
  return { teks: 'Menunggu', cls: 'bg-amber-500/10 text-amber-400 border-amber-500/20' };
};

const jenisJob = (kind) => (kind === 'Test' ? 'Struk uji' : 'Struk pesanan');

/**
 * Pengaturan perangkat kasir. Semua pilihan disimpan di server (AppSettings), jadi tetap
 * berlaku setelah aplikasi ditutup atau Windows di-restart. Halaman ini tidak memasang
 * driver sendiri: printer harus sudah terpasang di Windows.
 */
export default function DeviceView({ snapshot, jobs, loading, onRefresh, onSaveSettings, onTestPrint }) {
  const [form, setForm] = useState({ printerName: '', paperWidthMm: 80, autoPrint: false });
  const [menyimpan, setMenyimpan] = useState(false);
  const [menguji, setMenguji] = useState(false);
  const [pesan, setPesan] = useState(null); // { jenis: 'ok' | 'galat', teks }
  const [kodePindai, setKodePindai] = useState('');

  useEffect(() => {
    if (!snapshot?.settings) return;
    setForm({
      printerName: snapshot.settings.printerName || '',
      paperWidthMm: snapshot.settings.paperWidthMm || 80,
      autoPrint: Boolean(snapshot.settings.autoPrint)
    });
  }, [snapshot]);

  if (!snapshot) {
    return (
      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-8 text-sm text-slate-400">
        {loading ? 'Memuat keadaan perangkat...' : 'Keadaan perangkat belum tersedia. Muat ulang halaman ini.'}
      </div>
    );
  }

  const simpan = async () => {
    setMenyimpan(true);
    setPesan(null);
    try {
      await onSaveSettings({
        'device.printer.name': form.printerName,
        'device.paper.width_mm': String(form.paperWidthMm),
        'device.printer.auto_print': form.autoPrint ? '1' : '0'
      });
      setPesan({ jenis: 'ok', teks: 'Pengaturan perangkat disimpan dan berlaku untuk sesi berikutnya.' });
    } catch (err) {
      setPesan({ jenis: 'galat', teks: err.message || 'Pengaturan gagal disimpan.' });
    } finally {
      setMenyimpan(false);
    }
  };

  const ujiCetak = async () => {
    setMenguji(true);
    setPesan(null);
    try {
      await onTestPrint();
      setPesan({ jenis: 'ok', teks: 'Struk uji terkirim ke printer. Periksa hasilnya di printer.' });
    } catch (err) {
      setPesan({
        jenis: 'galat',
        teks: `${err.message || 'Struk uji gagal dicetak.'}`
      });
    } finally {
      setMenguji(false);
    }
  };

  const printerTerpasang = snapshot.printers || [];
  const printerDipilihAda = !form.printerName
    || printerTerpasang.some((p) => p.name === form.printerName);

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold text-white tracking-tight">Perangkat Kasir</h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20 rounded-full">
              Printer &amp; Pemindai
            </span>
          </div>
          <p className="text-sm text-slate-400 mt-1">
            Pilih printer struk, atur lebar kertas, dan uji cetak. Driver printer dipasang lewat Windows,
            bukan oleh aplikasi ini.
          </p>
        </div>
        <button
          onClick={onRefresh}
          disabled={loading}
          className="flex items-center gap-2 px-4 py-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-200 rounded-xl text-sm border border-slate-700/60 transition-colors"
        >
          <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          {loading ? 'Memuat...' : 'Muat Ulang'}
        </button>
      </div>

      {pesan && (
        <div
          className={`p-4 rounded-xl border text-sm flex items-start gap-2 ${
            pesan.jenis === 'ok'
              ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-300'
              : 'bg-rose-500/10 border-rose-500/20 text-rose-300'
          }`}
        >
          {pesan.jenis === 'ok' ? <CheckCircle2 className="w-4 h-4 mt-0.5" /> : <AlertCircle className="w-4 h-4 mt-0.5" />}
          <span>{pesan.teks}</span>
        </div>
      )}

      <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <div className="xl:col-span-2 bg-slate-900/80 border border-slate-800 rounded-2xl p-5 shadow-xl space-y-5">
          <div className="flex items-center gap-2 text-white font-semibold">
            <HardDrive className="w-4 h-4 text-amber-400" />
            Printer Struk
          </div>

          <div>
            <label className="text-xs font-semibold text-slate-400 block mb-1.5">Printer terpasang di Windows</label>
            <select
              value={form.printerName}
              onChange={(e) => setForm({ ...form, printerName: e.target.value })}
              className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60"
            >
              <option value="">-- Belum dipilih --</option>
              {printerTerpasang.map((p) => (
                <option key={p.name} value={p.name}>
                  {p.name}{p.isDefault ? ' (bawaan Windows)' : ''}
                </option>
              ))}
            </select>
            {printerTerpasang.length === 0 && (
              <p className="text-[11px] text-amber-400/80 mt-1.5">
                Daftar ini dibaca dari Windows dan kosong. Pasang printer beserta drivernya lebih dulu.
              </p>
            )}
            {!printerDipilihAda && (
              <p className="text-[11px] text-rose-400 mt-1.5">
                Printer yang tersimpan sebelumnya tidak ditemukan lagi. Pilih ulang printer.
              </p>
            )}
          </div>

          <div>
            <label className="text-xs font-semibold text-slate-400 block mb-1.5">Lebar kertas</label>
            <div className="grid grid-cols-2 gap-3">
              {[58, 80].map((mm) => (
                <button
                  key={mm}
                  type="button"
                  onClick={() => setForm({ ...form, paperWidthMm: mm })}
                  className={`px-3 py-2.5 rounded-xl border text-sm transition-colors ${
                    form.paperWidthMm === mm
                      ? 'bg-amber-500/10 border-amber-500/40 text-amber-300'
                      : 'bg-slate-950 border-slate-800 text-slate-300 hover:border-slate-700'
                  }`}
                >
                  {mm} mm · {mm === 58 ? '32' : '48'} kolom
                </button>
              ))}
            </div>
          </div>

          <label className="flex items-center gap-2.5 text-sm text-slate-300">
            <input
              type="checkbox"
              checked={form.autoPrint}
              onChange={(e) => setForm({ ...form, autoPrint: e.target.checked })}
              className="w-4 h-4 accent-amber-500"
            />
            Cetak struk otomatis setelah pembayaran berhasil
          </label>

          <div className="flex flex-wrap gap-2 pt-1">
            <button
              onClick={simpan}
              disabled={menyimpan}
              className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 disabled:opacity-60"
            >
              <CheckCircle2 className="w-4 h-4" />
              {menyimpan ? 'Menyimpan...' : 'Simpan Pengaturan'}
            </button>
            <button
              onClick={ujiCetak}
              disabled={menguji || !form.printerName}
              className="flex items-center gap-2 px-4 py-2.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-xl text-sm border border-slate-700 disabled:opacity-50"
            >
              <Send className="w-4 h-4" />
              {menguji ? 'Mengirim...' : 'Cetak Struk Uji'}
            </button>
          </div>

          <p className="text-[11px] text-slate-500">
            Mode cetak: melalui driver Windows ({snapshot.mode}). Pemotong kertas dan laci uang mengikuti
            kemampuan printer/driver masing-masing dan belum diuji oleh aplikasi ini.
          </p>
        </div>

        <div className="space-y-6">
          <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 shadow-xl">
            <div className="flex items-center gap-2 text-white font-semibold mb-3">
              <Printer className="w-4 h-4 text-emerald-400" />
              Status
            </div>
            <dl className="space-y-2 text-xs">
              <div className="flex justify-between gap-3">
                <dt className="text-slate-400">Platform</dt>
                <dd className="text-slate-200">{snapshot.platform}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-slate-400">Pencetakan langsung</dt>
                <dd className={snapshot.perangkatDidukung ? 'text-emerald-400' : 'text-rose-400'}>
                  {snapshot.perangkatDidukung ? 'Tersedia' : 'Tidak tersedia'}
                </dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-slate-400">Printer terpasang</dt>
                <dd className="text-slate-200">{printerTerpasang.length}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-slate-400">Akhiran pindai</dt>
                <dd className="text-slate-200">{snapshot.settings.scannerSuffix}</dd>
              </div>
            </dl>
            {snapshot.catatan && (
              <p className="mt-3 text-[11px] text-amber-400/90 flex items-start gap-1.5">
                <Info className="w-3.5 h-3.5 mt-0.5 shrink-0" />
                <span>{snapshot.catatan}</span>
              </p>
            )}
          </div>

          <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 shadow-xl">
            <div className="flex items-center gap-2 text-white font-semibold mb-2">
              <ScanLine className="w-4 h-4 text-sky-400" />
              Pemindai Barcode
            </div>
            <p className="text-xs text-slate-400 leading-relaxed">
              Pemindai USB mode keyboard wedge dikenali Windows sebagai keyboard. Hasil pindai masuk ke
              kolom yang sedang aktif dan harus diakhiri Enter (akhiran yang didukung saat ini).
            </p>
            <input
              type="text"
              value={kodePindai}
              onChange={(e) => setKodePindai(e.target.value)}
              placeholder="Fokuskan di sini lalu pindai barcode..."
              className="mt-3 w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-sky-500/60"
            />
            <p className="text-[11px] text-slate-500 mt-2">
              Kode terbaca: <span className="font-mono text-slate-300">{kodePindai || '—'}</span>
            </p>
            <p className="text-[11px] text-slate-500 mt-2">
              Tidak semua pemindai USB identik: model yang tidak mengirim Enter perlu diprogram di
              perangkatnya. Pemindai Bluetooth/USB-serial belum didukung.
            </p>
          </div>
        </div>
      </div>

      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <div className="text-sm font-semibold text-white">Riwayat Pekerjaan Cetak</div>
          <div className="text-xs text-slate-400">{jobs?.length || 0} baris terakhir</div>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-xs">
            <thead className="bg-slate-950/60 text-slate-400">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium">Waktu</th>
                <th className="text-left px-4 py-2.5 font-medium">Jenis</th>
                <th className="text-left px-4 py-2.5 font-medium">Pesanan</th>
                <th className="text-left px-4 py-2.5 font-medium">Printer</th>
                <th className="text-left px-4 py-2.5 font-medium">Kertas</th>
                <th className="text-left px-4 py-2.5 font-medium">Status</th>
                <th className="text-left px-4 py-2.5 font-medium">Keterangan</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/80">
              {(jobs || []).map((job) => {
                const status = labelStatus(job.status);
                return (
                  <tr key={job.id} className="text-slate-300">
                    <td className="px-4 py-2.5 whitespace-nowrap font-mono text-[11px]">
                      {formatDateTime(job.createdAt)}
                    </td>
                    <td className="px-4 py-2.5">{jenisJob(job.kind)}</td>
                    <td className="px-4 py-2.5">{job.orderId ? `#${job.orderId}` : '—'}</td>
                    <td className="px-4 py-2.5">{job.printerName || '—'}</td>
                    <td className="px-4 py-2.5">{job.paperWidthMm} mm</td>
                    <td className="px-4 py-2.5">
                      <span className={`px-2 py-0.5 rounded-full border text-[11px] ${status.cls}`}>
                        {status.teks}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-slate-400 max-w-[320px]">
                      {job.lastError || (job.status === 'Printed' ? `Percobaan ke-${job.attempts}` : '—')}
                    </td>
                  </tr>
                );
              })}
              {(jobs || []).length === 0 && (
                <tr>
                  <td colSpan={7} className="px-4 py-8 text-center text-slate-500">
                    Belum ada pekerjaan cetak. Cetak struk uji untuk memulai.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
