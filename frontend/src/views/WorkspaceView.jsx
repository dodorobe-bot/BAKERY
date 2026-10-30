import React from 'react';
import { CheckSquare, Bell, Clock, AlertTriangle, PackageX, CalendarClock, ArrowRight, RotateCw, Receipt, CheckCheck } from 'lucide-react';
import { formatDate, formatDateTime, formatInstantDate } from '../utils/money';

// Kotak tugas disusun dari kondisi basis data saat ini lewat /api/tasks; pemberitahuan dibaca
// dari tabel Notifications lewat /api/notifications. Keduanya menunjuk menu tempat pekerjaan
// benar-benar bisa dibereskan, jadi angkanya bisa ditelusuri, bukan cuma dipajang.
const IKON_JENIS = {
  piutang: Receipt,
  pengambilan: CalendarClock,
  produksi: CheckSquare,
  pembelian: PackageX,
  stok: AlertTriangle,
  kedaluwarsa: Clock,
};

// Setiap baris notifikasi menyimpan entitas asalnya, sehingga "Buka" menuju layar yang tepat.
const TAB_ENTITAS = {
  SalesOrder: 'orders',
  PurchaseOrder: 'purchases',
  WorkOrder: 'production_orders',
  QualityCheck: 'quality_control',
  Ingredient: 'stock_catalog',
  ItemBatch: 'batch_expiry',
  SalesReturn: 'sales_returns',
};

const GAYA_SEVERITY = {
  Critical: { badge: 'text-rose-300 bg-rose-500/10 border-rose-500/30', titik: 'bg-rose-400', ikon: 'text-rose-400' },
  Warning: { badge: 'text-amber-300 bg-amber-500/10 border-amber-500/30', titik: 'bg-amber-400', ikon: 'text-amber-400' },
  Info: { badge: 'text-sky-300 bg-sky-500/10 border-sky-500/30', titik: 'bg-sky-400', ikon: 'text-sky-400' },
};

const TONE = 'bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-lg';

export default function WorkspaceView({
  view, tasks, notifications, loading, onRefresh, onOpenTab,
  onSyncNotifications, onMarkRead, onMarkAllRead,
}) {
  const isTask = view === 'tasks';
  const ringkas = notifications?.ringkasan ?? { total: 0, unread: 0, criticalUnread: 0 };
  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <h1 className="text-2xl font-bold text-white tracking-tight">
            {isTask ? 'Kotak Tugas & Persetujuan' : 'Notifikasi Internal'}
          </h1>
          <p className="text-sm text-slate-400 mt-1">
            {isTask
              ? 'Pekerjaan yang benar-benar menunggu tindakan, disusun dari kondisi stok, pesanan, pembelian, dan produksi saat ini.'
              : 'Catatan transaksi, tugas, stok, produksi, dan ringkasan harian yang tersimpan di basis data lokal. Tidak ada pesan yang dikirim ke layanan luar.'}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs text-slate-400 mr-1">
            {isTask && tasks ? `disusun ${formatInstantDate(tasks.disusunPada)}` : ''}
          </span>
          {!isTask && (
            <>
              <button onClick={onSyncNotifications} disabled={loading}
                className="px-3 py-2.5 bg-slate-800/80 hover:bg-slate-700 disabled:opacity-50 text-xs font-bold text-slate-200 rounded-xl border border-slate-700/60"
                title="Susun ulang pengingat stok, kedaluwarsa, pesanan, dan ringkasan harian">
                Segarkan Pengingat
              </button>
              <button onClick={onMarkAllRead} disabled={loading || ringkas.unread === 0}
                className="px-3 py-2.5 bg-slate-800/80 hover:bg-slate-700 disabled:opacity-50 text-xs font-bold text-slate-200 rounded-xl border border-slate-700/60 inline-flex items-center gap-1.5"
                title="Tandai semua notifikasi sudah dibaca">
                <CheckCheck className="w-4 h-4" /> Tandai Semua
              </button>
            </>
          )}
          <button onClick={onRefresh} className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl border border-slate-700/60" title="Muat Ulang">
            <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      {isTask
        ? <IsiTugas tasks={tasks} onOpenTab={onOpenTab} />
        : <IsiNotifikasi notifications={notifications} ringkas={ringkas} onOpenTab={onOpenTab} onMarkRead={onMarkRead} />}
    </div>
  );
}

function IsiTugas({ tasks, onOpenTab }) {
  const item = tasks?.item ?? [];
  if (item.length === 0) {
    return (
      <div className={TONE + ' p-6 text-xs text-slate-400'}>
        Tidak ada pekerjaan yang menunggu sekarang. Daftar ini terisi sendiri begitu ada pesanan jatuh tempo, SPK terbuka, PO belum diterima, stok di bawah batas, atau lot mendekati kedaluwarsa.
      </div>
    );
  }

  const perJenis = tasks?.perJenis ?? {};
  return (
    <>
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
        {Object.entries(perJenis).map(([jenis, jumlah]) => (
          <div key={jenis} className="bg-slate-900/80 border border-slate-800 p-3 rounded-xl">
            <div className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{jenis}</div>
            <div className={`text-xl font-bold mt-1 font-mono ${jumlah > 0 ? 'text-amber-400' : 'text-slate-400'}`}>{jumlah}</div>
          </div>
        ))}
      </div>

      <div className={TONE}>
        <div className="p-4 border-b border-slate-800 flex items-center gap-2">
          <CheckSquare className="w-4 h-4 text-amber-400" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Perlu tindakan ({tasks.jumlah})</h3>
        </div>
        <div className="divide-y divide-slate-800/60">
          {item.map((t, idx) => {
            const Ikon = IKON_JENIS[t.jenis] ?? Clock;
            return (
              <button key={idx} onClick={() => onOpenTab?.(t.tab)}
                className="w-full text-left px-4 py-3 flex items-start gap-3 hover:bg-slate-800/40 transition-colors group">
                <Ikon className="w-4 h-4 mt-0.5 shrink-0 text-slate-400 group-hover:text-amber-400" />
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-bold text-white truncate">{t.judul}</span>
                    <span className="text-[10px] text-slate-500 shrink-0">
                      {t.tenggat ? `batas ${formatDate(t.tenggat)}` : ''}
                    </span>
                  </div>
                  <p className="text-[11px] text-slate-400 mt-0.5">{t.detail}</p>
                </div>
                <ArrowRight className="w-4 h-4 mt-0.5 shrink-0 text-slate-600 group-hover:text-amber-400" />
              </button>
            );
          })}
        </div>
      </div>
    </>
  );
}

function Ringkasan({ label, nilai, warna }) {
  return (
    <div className="bg-slate-900/80 border border-slate-800 p-3 rounded-xl">
      <div className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{label}</div>
      <div className={`text-xl font-bold mt-1 font-mono ${warna}`}>{nilai}</div>
    </div>
  );
}

function IsiNotifikasi({ notifications, ringkas, onOpenTab, onMarkRead }) {
  if (notifications === null || notifications === undefined) {
    return <div className={TONE + ' p-6 text-xs text-slate-400'}>Memuat notifikasi…</div>;
  }

  const item = notifications.item ?? [];
  if (item.length === 0) {
    return (
      <div className={TONE + ' p-6 text-xs text-slate-400'}>
        Belum ada notifikasi. Catatan akan muncul sendiri setelah transaksi tercatat, atau setelah pengingat stok, kedaluwarsa, tugas, dan ringkasan harian disusun.
      </div>
    );
  }

  return (
    <>
      <div className="grid grid-cols-3 gap-3">
        <Ringkasan label="Total" nilai={ringkas.total} warna="text-slate-200" />
        <Ringkasan label="Belum Dibaca" nilai={ringkas.unread} warna={ringkas.unread > 0 ? 'text-amber-400' : 'text-slate-400'} />
        <Ringkasan label="Kritis Belum Dibaca" nilai={ringkas.criticalUnread} warna={ringkas.criticalUnread > 0 ? 'text-rose-400' : 'text-slate-400'} />
      </div>

      <div className={TONE}>
        <div className="p-4 border-b border-slate-800 flex items-center gap-2">
          <Bell className="w-4 h-4 text-amber-400" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-400">Riwayat ({item.length})</h3>
        </div>
        <div className="divide-y divide-slate-800/60">
          {item.map((n) => {
            const gaya = GAYA_SEVERITY[n.severity] ?? GAYA_SEVERITY.Info;
            const tab = TAB_ENTITAS[n.entityType];
            return (
              <div key={n.id} className={`px-4 py-3 flex items-start gap-3 ${n.isRead ? '' : 'bg-slate-800/20'}`}>
                <span className={`mt-1.5 w-2 h-2 rounded-full shrink-0 ${n.isRead ? 'bg-slate-700' : gaya.titik}`} />
                <div className="flex-1 min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className={`inline-flex px-2 py-0.5 rounded-full text-[10px] font-bold border ${gaya.badge}`}>
                      {n.severity}
                    </span>
                    <span className={`text-xs ${n.isRead ? 'text-slate-300' : 'font-bold text-white'}`}>{n.title}</span>
                  </div>
                  <p className="text-[11px] text-slate-400 mt-1">{n.body}</p>
                  <p className="text-[10px] text-slate-500 mt-1">
                    {formatDateTime(n.createdAt)} · {n.eventType}
                  </p>
                </div>
                <div className="shrink-0 flex items-center gap-2 mt-0.5">
                  {!n.isRead && (
                    <button onClick={() => onMarkRead?.(n.id)}
                      className="px-2 py-1 text-[10px] font-bold text-slate-300 bg-slate-800/80 hover:bg-slate-700 rounded-lg border border-slate-700/60"
                      title="Tandai notifikasi ini sudah dibaca">
                      Tandai dibaca
                    </button>
                  )}
                  {tab && (
                    <button onClick={() => onOpenTab?.(tab)}
                      className="p-1.5 text-slate-500 hover:text-amber-400" title="Buka menu terkait">
                      <ArrowRight className="w-4 h-4" />
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </>
  );
}
