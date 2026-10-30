import React, { useMemo, useState } from 'react';
import {
  FileText,
  KeyRound,
  Settings,
  ShieldCheck,
  ShieldAlert,
  UserPlus,
  Save,
  RotateCw,
  X,
  Database,
  HardDrive,
  Copy,
  CheckCircle2,
} from 'lucide-react';
import { api } from '../services/api';
import { formatDateTime } from '../utils/money';
import { roleName } from '../utils/enums';
import { fileKeLogo } from '../utils/brand';

// Tiga menu pada grup "Analisis & Pengaturan" memakai satu layar ini: isinya sama-sama
// pengawasan sistem, dan memisahkannya hanya akan mengulang kerangka yang sama tiga kali.
const JUDUL = {
  audit_log: {
    judul: 'Jejak Audit & Riwayat Sistem',
    arti: 'Setiap aksi tulis yang berhasil dicatat — siapa, kapan, dan nilainya berubah dari berapa ke berapa — beserta percobaan yang ditolak karena peran tidak berwenang.',
  },
  user_management: {
    judul: 'Hak Akses & Pegawai',
    arti: 'Akun, peran, dan kata sandi sementara pegawai. Peran menentukan menu apa yang bisa dibuka.',
  },
  settings: {
    judul: 'Pengaturan Toko & Cabang',
    arti: 'Identitas toko, zona waktu perhitungan hari bisnis, dan pemeriksaan mandiri basis data.',
  },
};

const PERAN = [
  { value: 'Owner', label: 'Pemilik Toko (Owner)', arti: 'Sepenuhnya, termasuk pengaturan, hak akses, dan pembatalan.' },
  { value: 'Admin', label: 'Manajer Operasional (Admin)', arti: 'Semua operasional kecuali pengaturan sistem dan hak akses.' },
  { value: 'Dapur', label: 'Kepala Dapur (Dapur)', arti: 'Produksi, penerimaan barang, dan koreksi stok; tidak menyentuh uang.' },
  { value: 'Pegawai', label: 'Staf Toko / Kasir (Pegawai)', arti: 'Pesanan dan pembayaran; tidak bisa membatalkan atau membuka buku besar.' },
];

const labelPeran = (peran) => PERAN.find((p) => p.value === peran)?.label ?? peran;

export default function SystemView({ view, settings, users, selfCheck, loading, onRefresh, onSaveSettings, onCreateUser, onUpdateUser }) {
  if (view === 'audit_log') return <JejakAudit loading={loading} onRefresh={onRefresh} />;
  if (view === 'user_management')
    return (
      <HakAkses users={(users ?? []).map((u) => ({ ...u, role: roleName(u.role) }))} loading={loading} onRefresh={onRefresh}
        onCreateUser={onCreateUser} onUpdateUser={onUpdateUser} />
    );
  return (
    <Pengaturan settings={settings ?? {}} selfCheck={selfCheck} loading={loading}
      onRefresh={onRefresh} onSaveSettings={onSaveSettings} />
  );
}

function Kerangka({ view, children, aksi }) {
  const teks = JUDUL[view] ?? JUDUL.settings;
  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">{teks.judul}</h1>
          <p className="text-sm text-slate-400 mt-1">{teks.arti}</p>
        </div>
        {aksi}
      </div>
      {children}
    </div>
  );
}

function TombolMuat({ onRefresh, loading }) {
  return (
    <button
      onClick={onRefresh}
      className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl transition-colors border border-slate-700/60"
      title="Muat Ulang Data"
    >
      <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
    </button>
  );
}

// ── Jejak audit ───────────────────────────────────────────────────────────────
const HALAMAN_AUDIT = 50;

function JejakAudit({ loading, onRefresh }) {
  const [entri, setEntri] = useState(null);
  const [galat, setGalat] = useState('');
  const [kunciFilter, setKunciFilter] = useState('');
  const [jenis, setJenis] = useState('');
  const [tampil, setTampil] = useState(HALAMAN_AUDIT);
  const [dimuat, setDimuat] = useState(false);

  const muat = async () => {
    setGalat('');
    try {
      setEntri(await api.getAudit({ entity: jenis || null, limit: 300 }));
      setDimuat(true);
    } catch (err) {
      setGalat(err.message || 'Jejak audit gagal dimuat.');
    }
  };

  React.useEffect(() => { muat(); }, [jenis]);

  const baris = (entri ?? []).filter((e) => {
    if (!kunciFilter.trim()) return true;
    const cari = kunciFilter.toLowerCase();
    return [e.username, e.action, e.entityType, e.oldValues, e.newValues]
      .some((n) => (n ?? '').toString().toLowerCase().includes(cari));
  });

  return (
    <Kerangka
      view="audit_log"
      loading={loading}
      aksi={<TombolMuat onRefresh={() => { onRefresh(); muat(); }} loading={loading} />}
    >
      {galat && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <input
          value={kunciFilter}
          onChange={(e) => setKunciFilter(e.target.value)}
          placeholder="Cari pelaksana, aksi, atau entitas..."
          className="flex-1 min-w-[16rem] px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60"
        />
        <select
          value={jenis}
          onChange={(e) => setJenis(e.target.value)}
          className="px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-xs text-white focus:outline-none focus:border-amber-500/60"
        >
          <option value="">Semua jenis entitas</option>
          {['SalesOrder', 'WorkOrder', 'PurchaseOrder', 'ProductTemplate', 'Ingredient', 'LocationStock', 'AppSetting', 'User', 'ItemBatch', 'JournalEntry', 'Akses'].map((t) => (
            <option key={t} value={t}>{t}</option>
          ))}
        </select>
      </div>

      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
        <div className="p-4 border-b border-slate-800 flex items-center gap-2">
          <FileText className="w-4 h-4 text-amber-400" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">
            Catatan perubahan ({baris.length})
          </h3>
        </div>

        {!dimuat && !galat && (
          <div className="p-6 text-xs text-slate-400">Memuat jejak audit...</div>
        )}
        {dimuat && baris.length === 0 && (
          <div className="p-6 text-xs text-slate-400">
            Belum ada aksi tulis yang tercatat{jenis ? ` untuk ${jenis}` : ''}. Daftar ini terisi sendiri
            setiap perubahan data berhasil disimpan.
          </div>
        )}
        {baris.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-3 px-4 font-semibold">Waktu</th>
                  <th className="py-3 px-4 font-semibold">Pelaksana</th>
                  <th className="py-3 px-4 font-semibold">Aksi</th>
                  <th className="py-3 px-4 font-semibold">Entitas</th>
                  <th className="py-3 px-4 font-semibold">Perubahan</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {baris.slice(0, tampil).map((e) => (
                  <tr key={e.id} className="hover:bg-slate-800/40 align-top">
                    <td className="py-3 px-4 whitespace-nowrap text-slate-400">{formatDateTime(e.occurredAt)}</td>
                    <td className="py-3 px-4 font-semibold text-white">{e.username ?? '(tanpa akun)'}</td>
                    <td className="py-3 px-4 font-mono text-amber-400">{e.action}</td>
                    <td className="py-3 px-4">
                      {e.entityType}{e.entityId ? ` #${e.entityId}` : ''}
                    </td>
                    <td className="py-3 px-4 text-slate-400">
                      {e.oldValues || e.newValues ? (
                        <div className="space-y-1">
                          {e.oldValues && <div className="text-rose-300/80">lama: {e.oldValues}</div>}
                          {e.newValues && <div className="text-emerald-300/80">baru: {e.newValues}</div>}
                        </div>
                      ) : (
                        <span className="text-slate-500">nilai tidak disimpan per baris</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {baris.length > tampil && (
              <button
                onClick={() => setTampil((n) => n + HALAMAN_AUDIT)}
                className="w-full py-3 text-xs font-bold text-amber-400 hover:bg-slate-800/40 border-t border-slate-800"
              >
                Tampilkan {Math.min(HALAMAN_AUDIT, baris.length - tampil)} entri lainnya
              </button>
            )}
          </div>
        )}
      </div>
    </Kerangka>
  );
}

// ── Hak akses & pegawai ───────────────────────────────────────────────────────
function HakAkses({ users, loading, onRefresh, onCreateUser, onUpdateUser }) {
  const [formOpen, setFormOpen] = useState(false);
  const [form, setForm] = useState({ username: '', password: '', role: 'Pegawai' });
  const [galat, setGalat] = useState('');
  const [sedangProses, setSedangProses] = useState(false);
  const [resetId, setResetId] = useState(null);
  const [sandiBaru, setSandiBaru] = useState('');

  const ownerAktif = useMemo(() => users.filter((u) => u.role === 'Owner' && u.isActive).length, [users]);

  const simpan = async (e) => {
    e.preventDefault();
    setGalat('');
    setSedangProses(true);
    try {
      await onCreateUser(form);
      setFormOpen(false);
      setForm({ username: '', password: '', role: 'Pegawai' });
    } catch (err) {
      setGalat(err.message || 'Gagal membuat pegawai.');
    } finally {
      setSedangProses(false);
    }
  };

  const ubah = async (id, payload) => {
    setGalat('');
    try {
      await onUpdateUser(id, payload);
      setResetId(null);
      setSandiBaru('');
    } catch (err) {
      setGalat(err.message || 'Gagal mengubah hak akses.');
    }
  };

  return (
    <Kerangka view="user_management" loading={loading} aksi={<TombolMuat onRefresh={onRefresh} loading={loading} />}>
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
          <div className="text-xs font-medium text-slate-400">Akun terdaftar</div>
          <div className="text-xl font-bold text-white mt-2 font-mono">{users.length}</div>
          <div className="text-[11px] text-slate-400 mt-1">
            {users.filter((u) => u.isActive).length} aktif, {users.filter((u) => !u.isActive).length} dinonaktifkan
          </div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
          <div className="text-xs font-medium text-slate-400">Owner aktif</div>
          <div className={`text-xl font-bold mt-2 font-mono ${ownerAktif > 1 ? 'text-emerald-400' : 'text-amber-400'}`}>
            {ownerAktif}
          </div>
          <div className="text-[11px] text-slate-400 mt-1">
            {ownerAktif > 1 ? 'ada cadangan bila satu akun keluar' : 'satu-satunya jalan ke layar ini — tidak bisa diturunkan atau dinonaktifkan'}
          </div>
        </div>
        <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
          <div className="text-xs font-medium text-slate-400">Wajib ganti sandi</div>
          <div className="text-xl font-bold text-white mt-2 font-mono">
            {users.filter((u) => u.mustChangePassword).length}
          </div>
          <div className="text-[11px] text-slate-400 mt-1">akun ini hanya bisa login untuk mengganti sandi</div>
        </div>
      </div>

      <div className="flex items-center justify-between">
        <p className="text-xs text-slate-400 max-w-2xl">
          Kata sandi tidak pernah ditampilkan atau dikirim balik oleh aplikasi. Yang diatur di sini hanyalah
          peran, status aktif, dan sandi sementara yang harus diganti sendiri oleh pegawai saat login berikutnya.
        </p>
        <button
          onClick={() => { setFormOpen(true); setGalat(''); }}
          className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all shrink-0"
        >
          <UserPlus className="w-4 h-4" />
          <span>Tambah Pegawai</span>
        </button>
      </div>

      {galat && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>
      )}

      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg">
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
              <tr>
                <th className="py-3 px-4 font-semibold">Nama pengguna</th>
                <th className="py-3 px-4 font-semibold">Peran</th>
                <th className="py-3 px-4 font-semibold">Status</th>
                <th className="py-3 px-4 font-semibold">Login terakhir</th>
                <th className="py-3 px-4 font-semibold text-right">Aksi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60">
              {users.length === 0 && (
                <tr><td colSpan={5} className="py-6 px-4 text-slate-400">Belum ada akun di basis data.</td></tr>
              )}
              {users.map((u) => (
                <tr key={u.id} className="hover:bg-slate-800/40">
                  <td className="py-3 px-4 font-semibold text-white">{u.username}</td>
                  <td className="py-3 px-4">
                    <select
                      value={u.role}
                      onChange={(e) => ubah(u.id, { role: e.target.value, isActive: u.isActive })}
                      className="px-2 py-1.5 bg-slate-950 border border-slate-800 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60"
                    >
                      {PERAN.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
                    </select>
                  </td>
                  <td className="py-3 px-4">
                    <span className={`px-2 py-0.5 rounded-full text-[10px] font-semibold border ${
                      u.isActive
                        ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20'
                        : 'bg-slate-800 text-slate-400 border-slate-700'
                    }`}>
                      {u.isActive ? 'aktif' : 'nonaktif'}
                    </span>
                    {u.mustChangePassword && (
                      <span className="ml-2 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20">
                        wajib ganti sandi
                      </span>
                    )}
                    {u.lockedUntil && (
                      <span className="ml-2 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-rose-500/10 text-rose-400 border border-rose-500/20">
                        terkunci
                      </span>
                    )}
                  </td>
                  <td className="py-3 px-4 text-slate-400">{u.lastLoginAt ? formatDateTime(u.lastLoginAt) : 'belum pernah'}</td>
                  <td className="py-3 px-4 text-right whitespace-nowrap">
                    <button
                      onClick={() => { setResetId(resetId === u.id ? null : u.id); setSandiBaru(''); setGalat(''); }}
                      className="px-2.5 py-1 text-[11px] font-bold text-amber-400 hover:bg-amber-500/10 border border-amber-500/20 rounded-lg"
                    >
                      {resetId === u.id ? 'Tutup' : 'Beri sandi sementara'}
                    </button>
                    <button
                      onClick={() => ubah(u.id, { role: u.role, isActive: !u.isActive })}
                      className={`ml-2 px-2.5 py-1 text-[11px] font-bold rounded-lg border ${
                        u.isActive
                          ? 'text-rose-400 hover:bg-rose-500/10 border-rose-500/20'
                          : 'text-emerald-400 hover:bg-emerald-500/10 border-emerald-500/20'
                      }`}
                    >
                      {u.isActive ? 'Nonaktifkan' : 'Aktifkan'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {resetId !== null && (
          <div className="p-4 border-t border-slate-800 bg-slate-950/40 flex flex-wrap items-end gap-3">
            <div className="flex-1 min-w-[14rem]">
              <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                Kata sandi sementara untuk {users.find((u) => u.id === resetId)?.username}
              </label>
              <input
                type="text"
                value={sandiBaru}
                onChange={(e) => setSandiBaru(e.target.value)}
                placeholder="minimal 8 karakter, huruf + angka"
                className="w-full px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60"
              />
            </div>
            <button
              onClick={() => sandiBaru.length >= 8 && ubah(resetId, { role: users.find((u) => u.id === resetId)?.role, passwordSementara: sandiBaru })}
              disabled={sandiBaru.length < 8}
              className="px-4 py-2 bg-amber-500 text-slate-950 font-bold rounded-xl text-xs disabled:opacity-40"
            >
              Simpan & paksa ganti
            </button>
            <p className="text-[11px] text-slate-500 basis-full">
              Sandi ini hanya untuk masuk pertama pegawai yang bersangkutan; ia wajib menggantinya sendiri.
            </p>
          </div>
        )}
      </div>

      {formOpen && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-slate-950/90 backdrop-blur-sm">
          <form onSubmit={simpan} className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <div>
                <h3 className="text-base font-bold text-white">Tambah Pegawai</h3>
                <p className="text-xs text-slate-400 mt-1">Akun baru lahir dengan sandi sementara yang wajib diganti sendiri.</p>
              </div>
              <button type="button" onClick={() => setFormOpen(false)} className="text-slate-400 hover:text-white"><X className="w-4 h-4" /></button>
            </div>
            <div className="p-5 space-y-4">
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Nama pengguna</label>
                <input
                  value={form.username}
                  onChange={(e) => setForm({ ...form, username: e.target.value })}
                  placeholder="mis. kasir-sore"
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60"
                  required
                />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Kata sandi awal</label>
                <input
                  value={form.password}
                  onChange={(e) => setForm({ ...form, password: e.target.value })}
                  placeholder="minimal 8 karakter, huruf + angka"
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60"
                  required
                />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Peran</label>
                <select
                  value={form.role}
                  onChange={(e) => setForm({ ...form, role: e.target.value })}
                  className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60"
                >
                  {PERAN.map((p) => <option key={p.value} value={p.value}>{p.label}</option>)}
                </select>
                <p className="text-[11px] text-slate-500 mt-1.5">
                  {PERAN.find((p) => p.value === form.role)?.arti}
                </p>
              </div>
              {galat && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>
              )}
              <div className="flex justify-end gap-2">
                <button type="button" onClick={() => setFormOpen(false)} className="px-4 py-2.5 text-xs font-bold text-slate-400 hover:text-white">
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={sedangProses}
                  className="px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 text-slate-950 font-bold rounded-xl text-sm disabled:opacity-50"
                >
                  {sedangProses ? 'Menyimpan...' : 'Simpan Pegawai'}
                </button>
              </div>
            </div>
          </form>
        </div>
      )}
    </Kerangka>
  );
}

// ── Pengaturan + periksa basis data ───────────────────────────────────────────
// Kunci yang boleh disunting pemilik. app_version dan currency ditulis tetap oleh genggaman
// build, jadi menampilkannya sebagai kolom kosong adalah ajakan untuk merusak data.
const KUNCI_SUNTING = [
  { key: 'brand_name', label: 'Nama toko (tampil di layar masuk, menu, struk, dan ekspor)', contoh: 'Seruni 22 Bakery', maks: 40 },
  { key: 'brand_tagline', label: 'Slogan di bawah nama toko', contoh: 'Sistem Manajemen Toko Kue', maks: 80 },
  { key: 'outlet_name', label: 'Nama tempat kerja (tampil di bilah atas)', contoh: 'Kantor Sentral & Toko Utama' },
  { key: 'timezone', label: 'Zona waktu operasi', contoh: 'Asia/Jakarta' },
];

const nilaiAwal = (settings) => ({
  ...Object.fromEntries(KUNCI_SUNTING.map((k) => [k.key, settings?.[k.key] ?? ''])),
  brand_logo: settings?.brand_logo ?? '',
});

function Pengaturan({ settings, selfCheck, loading, onRefresh, onSaveSettings }) {
  const [form, setForm] = useState(() => nilaiAwal(settings));
  const [pesan, setPesan] = useState('');
  const [galat, setGalat] = useState('');
  const [galatLogo, setGalatLogo] = useState('');
  const [sedangSimpan, setSedangSimpan] = useState(false);

  React.useEffect(() => {
    setForm(nilaiAwal(settings));
  }, [settings]);

  // Berkas diubah menjadi data URL di peramban, lalu baru disimpan bersama kunci lain. Server
  // memeriksa ulang jenis gambar dan ukurannya, jadi jalur ini hanya membuat unggahan kecil.
  const pilihLogo = async (e) => {
    const berkas = e.target.files?.[0];
    e.target.value = '';
    setGalatLogo('');
    if (!berkas) return;
    try {
      const dataUrl = await fileKeLogo(berkas);
      setForm((lama) => ({ ...lama, brand_logo: dataUrl }));
    } catch (err) {
      setGalatLogo(err.message || 'Logo tidak bisa dibaca.');
    }
  };

  const simpan = async (e) => {
    e.preventDefault();
    setGalat('');
    setPesan('');
    setSedangSimpan(true);
    try {
      const hasil = await onSaveSettings(form);
      setPesan(
        `Tersimpan (${hasil?.diterapkan ?? 0} kunci berubah).` +
        (hasil?.peringatan ? ` Perhatian: ${hasil.peringatan}` : '')
      );
    } catch (err) {
      setGalat(err.message || 'Pengaturan gagal disimpan.');
    } finally {
      setSedangSimpan(false);
    }
  };

  return (
    <Kerangka
      view="settings"
      loading={loading}
      aksi={
        <div className="flex items-center gap-2">
          <TombolMuat onRefresh={onRefresh} loading={loading} />
          <PeriksaBasisData />
        </div>
      }
    >
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <form onSubmit={simpan} className="lg:col-span-2 bg-slate-900/80 border border-slate-800 rounded-2xl p-5 space-y-4 shadow-lg">
          <div className="flex items-center gap-2">
            <Settings className="w-4 h-4 text-amber-400" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Identitas & waktu operasi</h3>
          </div>

          {KUNCI_SUNTING.map((k) => (
            <div key={k.key}>
              <label className="text-xs font-semibold text-slate-400 block mb-1.5">{k.label}</label>
              <input
                value={form[k.key] ?? ''}
                maxLength={k.maks}
                onChange={(e) => setForm({ ...form, [k.key]: e.target.value })}
                placeholder={k.contoh}
                className="w-full px-4 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60"
              />
            </div>
          ))}

          <div>
            <label className="text-xs font-semibold text-slate-400 block mb-1.5">Logo toko</label>
            <div className="flex items-center gap-3 p-3 bg-slate-950 border border-slate-800 rounded-xl">
              <div className="w-12 h-12 rounded-xl bg-gradient-to-tr from-amber-500 via-amber-600 to-amber-400 p-0.5 shrink-0 flex items-center justify-center">
                {form.brand_logo ? (
                  <img
                    src={form.brand_logo}
                    alt="Pratinjau logo toko"
                    className="w-full h-full rounded-[10px] bg-slate-950 object-contain p-1"
                  />
                ) : (
                  <span className="w-full h-full rounded-[10px] bg-slate-950 flex items-center justify-center text-[9px] text-slate-500 text-center px-1 leading-tight">
                    Tanpa logo
                  </span>
                )}
              </div>
              <div className="flex-1 min-w-0 space-y-1.5">
                <div className="flex flex-wrap items-center gap-2">
                  <label className="px-3 py-1.5 bg-slate-900 hover:bg-slate-800 border border-slate-700 rounded-lg text-xs font-semibold text-slate-200 cursor-pointer transition-colors">
                    Pilih gambar…
                    <input
                      type="file"
                      accept="image/png,image/jpeg,image/webp"
                      className="hidden"
                      onChange={pilihLogo}
                    />
                  </label>
                  {form.brand_logo && (
                    <button
                      type="button"
                      onClick={() => { setGalatLogo(''); setForm({ ...form, brand_logo: '' }); }}
                      className="px-3 py-1.5 bg-slate-900 hover:bg-rose-500/10 border border-slate-700 hover:border-rose-500/40 rounded-lg text-xs font-semibold text-slate-300 hover:text-rose-300 transition-colors"
                    >
                      Hapus logo
                    </button>
                  )}
                </div>
                <p className="text-[10px] text-slate-500">
                  PNG, JPEG, atau WebP. Gambar diperkecil otomatis; tanpa logo, inisial nama toko yang tampil di kotak merek.
                </p>
                {galatLogo && <p className="text-[11px] text-rose-400">{galatLogo}</p>}
              </div>
            </div>
            <p className="text-[10px] text-slate-500 mt-1">
              Perubahan nama, slogan, dan logo berlaku setelah tombol Simpan ditekan, tanpa memasang ulang aplikasi.
            </p>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-[11px] text-slate-400">
            <div className="p-3 bg-slate-950/60 border border-slate-800 rounded-xl">
              Mata uang: <span className="text-white font-semibold">{settings.currency ?? 'IDR'}</span>
              <div className="text-slate-500">ditetapkan oleh genggaman aplikasi, bukan isian bebas</div>
            </div>
            <div className="p-3 bg-slate-950/60 border border-slate-800 rounded-xl">
              Versi aplikasi: <span className="text-white font-semibold">{settings.app_version ?? '-'}</span>
              <div className="text-slate-500">naik hanya lewat installer baru</div>
            </div>
          </div>

          {pesan && (
            <div className="p-3 bg-emerald-500/10 border border-emerald-500/20 rounded-xl text-emerald-400 text-xs">{pesan}</div>
          )}
          {galat && (
            <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>
          )}

          <div className="flex justify-end">
            <button
              type="submit"
              disabled={sedangSimpan}
              className="flex items-center gap-2 px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 text-slate-950 font-bold rounded-xl text-sm disabled:opacity-50"
            >
              <Save className="w-4 h-4" />
              {sedangSimpan ? 'Menyimpan...' : 'Simpan Pengaturan'}
            </button>
          </div>
        </form>

        <div className="space-y-6">
          <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 shadow-lg space-y-3">
            <div className="flex items-center gap-2">
              <KeyRound className="w-4 h-4 text-amber-400" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Yang tidak bisa diubah di sini</h3>
            </div>
            <ul className="text-[11px] text-slate-400 space-y-2 list-disc pl-4">
              <li>Hak akses pegawai ada di menu "Hak Akses & Pegawai" dan hanya pemilik yang bisa membukanya.</li>
              <li>Zona waktu operasi menentukan "hari ini" untuk jatuh tempo, laporan harian, dan nomor dokumen.</li>
              <li>Perubahan di layar ini tercatat di Jejak Audit dengan nilai lama dan barunya.</li>
              <li>Kata sandi token aplikasi tidak ditampilkan di mana pun, termasuk di layar ini.</li>
            </ul>
          </div>

          <CadanganPenyimpanan />
        </div>
      </div>
    </Kerangka>
  );
}

// ── Cadangan & penyimpanan ────────────────────────────────────────────────────
// Angka di panel ini berasal dari berkas dan disk yang sebenarnya: ukuran basis data + WAL,
// jumlah cadangan terverifikasi, sisa ruang, dan umur cadangan terakhir.
const JENIS_CADANGAN = {
  manual: 'manual',
  otomatis: 'otomatis',
  'pre-upgrade': 'pra-upgrade',
  'pre-restore': 'pra-restore',
  'tanpa-manifes': 'tanpa manifes',
};

const ukuranBerkas = (byte) => {
  const n = Number(byte) || 0;
  if (n >= 1024 * 1024 * 1024) return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`;
  if (n >= 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} MB`;
  if (n >= 1024) return `${(n / 1024).toFixed(0)} KB`;
  return `${n} B`;
};

function CadanganPenyimpanan() {
  const [daftar, setDaftar] = useState(null);
  const [potret, setPotret] = useState(null);
  const [galat, setGalat] = useState('');
  const [pesan, setPesan] = useState('');
  const [proses, setProses] = useState('');
  const [tujuan, setTujuan] = useState('');

  const muat = async () => {
    setGalat('');
    try {
      const [isi, kapasitas] = await Promise.all([api.getBackups(), api.getStorage()]);
      setDaftar(isi);
      setPotret(kapasitas);
    } catch (err) {
      setGalat(err.message || 'Data cadangan gagal dimuat.');
    }
  };

  React.useEffect(() => {
    muat();
  }, []);

  const buat = async () => {
    setProses('buat');
    setGalat('');
    setPesan('');
    try {
      const manifes = await api.createBackup();
      setPesan(`Cadangan dibuat dan diverifikasi: ${manifes.file} (${ukuranBerkas(manifes.sizeBytes)}).`);
      await muat();
    } catch (err) {
      setGalat(err.message || 'Cadangan gagal dibuat.');
    } finally {
      setProses('');
    }
  };

  const salin = async () => {
    if (!tujuan.trim()) {
      setGalat('Isi folder tujuan lebih dulu, misalnya D:\\Cadangan Seruni.');
      return;
    }
    setProses('salin');
    setGalat('');
    setPesan('');
    try {
      const hasil = await api.copyBackup(tujuan.trim());
      setPesan(`Disalin ke ${hasil.folder} (${hasil.berkas}); checksum di tujuan cocok.`);
      setTujuan('');
    } catch (err) {
      setGalat(err.message || 'Penyalinan cadangan gagal.');
    } finally {
      setProses('');
    }
  };

  const tingkat = potret?.level ?? 'normal';
  const warnaTingkat = tingkat === 'kritis'
    ? 'text-rose-400 border-rose-500/30 bg-rose-500/10'
    : tingkat === 'perhatian'
      ? 'text-amber-400 border-amber-500/30 bg-amber-500/10'
      : 'text-emerald-400 border-emerald-500/30 bg-emerald-500/10';
  const teratas = (daftar?.item ?? []).slice(0, 5);

  return (
    <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 shadow-lg space-y-4">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <HardDrive className="w-4 h-4 text-amber-400" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Cadangan & penyimpanan</h3>
        </div>
        <span className={`text-[10px] font-bold px-2 py-0.5 rounded-full border ${warnaTingkat}`}>
          {tingkat}
        </span>
      </div>

      {potret && (
        <div className="grid grid-cols-2 gap-2 text-[11px] text-slate-400">
          <div className="p-2.5 bg-slate-950/60 border border-slate-800 rounded-xl">
            Basis data + WAL
            <div className="text-white font-semibold">{ukuranBerkas((potret.dbBytes ?? 0) + (potret.walBytes ?? 0))}</div>
          </div>
          <div className="p-2.5 bg-slate-950/60 border border-slate-800 rounded-xl">
            Total cadangan
            <div className="text-white font-semibold">
              {potret.backupCount ?? 0} berkas · {ukuranBerkas(potret.backupBytes)}
            </div>
          </div>
          <div className="p-2.5 bg-slate-950/60 border border-slate-800 rounded-xl">
            Sisa disk
            <div className="text-white font-semibold">{ukuranBerkas(potret.freeBytes)}</div>
          </div>
          <div className="p-2.5 bg-slate-950/60 border border-slate-800 rounded-xl">
            Cadangan terverifikasi terakhir
            <div className="text-white font-semibold">
              {potret.lastVerifiedBackupUtc ? formatDateTime(potret.lastVerifiedBackupUtc) : 'belum ada'}
            </div>
          </div>
        </div>
      )}

      {potret?.reasons?.length > 0 && (
        <ul className="text-[11px] text-amber-300/90 space-y-1 list-disc pl-4">
          {potret.reasons.map((r) => <li key={r}>{r}</li>)}
        </ul>
      )}

      <p className="text-[10px] text-slate-500 break-words">
        Folder: {daftar?.folder ?? '-'}
      </p>

      {teratas.length > 0 && (
        <ul className="space-y-1.5">
          {teratas.map((b) => (
            <li key={b.berkas} className="flex items-center justify-between gap-2 text-[11px]">
              <span className="text-slate-300 truncate" title={b.berkas}>{b.berkas}</span>
              <span className="flex items-center gap-2 shrink-0">
                <span className="text-slate-500">{JENIS_CADANGAN[b.jenis] ?? b.jenis}</span>
                <span className="text-slate-400">{ukuranBerkas(b.ukuranByte)}</span>
                {b.terverifikasi
                  ? <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" title="manifes + ukuran cocok" />
                  : <ShieldAlert className="w-3.5 h-3.5 text-amber-400" title="belum terverifikasi" />}
              </span>
            </li>
          ))}
        </ul>
      )}

      <button
        onClick={buat}
        disabled={proses === 'buat'}
        className="w-full flex items-center justify-center gap-2 px-4 py-2.5 bg-amber-500/15 hover:bg-amber-500/25 text-amber-300 border border-amber-500/30 rounded-xl text-xs font-bold transition-colors disabled:opacity-50"
      >
        <Database className="w-4 h-4" />
        {proses === 'buat' ? 'Membuat cadangan...' : 'Buat cadangan sekarang'}
      </button>

      <div className="space-y-2">
        <label className="text-[11px] font-semibold text-slate-400 block">Salin cadangan terbaru ke folder lain (flash disk/komputer lain)</label>
        <div className="flex gap-2">
          <input
            value={tujuan}
            onChange={(e) => setTujuan(e.target.value)}
            placeholder="D:\Cadangan Seruni"
            className="flex-1 px-3 py-2 bg-slate-950 border border-slate-800 rounded-xl text-xs text-white focus:outline-none focus:border-amber-500/60"
          />
          <button
            onClick={salin}
            disabled={proses === 'salin'}
            className="flex items-center gap-1.5 px-3 py-2 bg-slate-800 hover:bg-slate-700 text-white rounded-xl text-xs font-bold border border-slate-700 disabled:opacity-50"
          >
            <Copy className="w-3.5 h-3.5" />
            {proses === 'salin' ? 'Menyalin...' : 'Salin'}
          </button>
        </div>
        <p className="text-[10px] text-slate-500">
          Pemulihan dijalankan saat aplikasi tertutup: Seruni22Bakery.exe --restore "path berkas cadangan".
          Cadangan otomatis dibuat sekali sehari selama aplikasi dipakai.
        </p>
      </div>

      {pesan && (
        <div className="p-3 bg-emerald-500/10 border border-emerald-500/20 rounded-xl text-emerald-400 text-[11px]">{pesan}</div>
      )}
      {galat && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-[11px]">{galat}</div>
      )}
    </div>
  );
}

function PeriksaBasisData() {
  const [hasil, setHasil] = useState(null);
  const [galat, setGalat] = useState('');
  const [proses, setProses] = useState(false);

  const jalankan = async () => {
    setGalat('');
    setProses(true);
    try {
      setHasil(await api.getSelfCheck());
    } catch (err) {
      setGalat(err.message || 'Pemeriksaan gagal dijalankan.');
    } finally {
      setProses(false);
    }
  };

  const gagal = hasil?.checks?.filter((c) => !c.passed) ?? [];
  const lulus = hasil?.checks?.filter((c) => c.passed) ?? [];

  return (
    <div className="space-y-3">
      <button
        onClick={jalankan}
        disabled={proses}
        className="flex items-center gap-2 px-4 py-2.5 bg-slate-800 hover:bg-slate-700 text-white rounded-xl text-xs font-bold border border-slate-700 transition-colors disabled:opacity-50"
        title="Menanyakan kondisi basis data langsung ke SQLite"
      >
        <ShieldCheck className="w-4 h-4 text-emerald-400" />
        {proses ? 'Memeriksa...' : 'Periksa Basis Data'}
      </button>

      {galat && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">{galat}</div>
      )}

      {hasil && (
        <div className="bg-slate-950/60 border border-slate-800 rounded-2xl p-4 space-y-2 max-h-[28rem] overflow-y-auto custom-scrollbar">
          <div className="flex items-center gap-2">
            {gagal.length === 0 ? (
              <ShieldCheck className="w-4 h-4 text-emerald-400" />
            ) : (
              <ShieldAlert className="w-4 h-4 text-rose-400" />
            )}
            <span className={`text-xs font-bold ${gagal.length === 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
              {gagal.length === 0
                ? `Sehat — ${lulus.length} pemeriksaan lolos`
                : `${gagal.length} dari ${hasil.checks.length} pemeriksaan bermasalah`}
            </span>
          </div>
          <p className="text-[10px] text-slate-500">
            {hasil.databasePath} · {formatDateTime(hasil.diperiksaPada)}
          </p>

          <ul className="space-y-2 pt-1">
            {hasil.checks.map((c) => (
              <li key={c.name} className="text-[11px]">
                <div className="flex items-center gap-2">
                  <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${c.passed ? 'bg-emerald-400' : 'bg-rose-400'}`} />
                  <span className="font-semibold text-white">{c.name}</span>
                </div>
                <p className="text-slate-400 pl-3.5 break-words">{c.detail}</p>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
