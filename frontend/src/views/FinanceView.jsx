import React, { useCallback, useEffect, useMemo, useState } from 'react';
import {
  BookMarked,
  Landmark,
  Receipt,
  FileSpreadsheet,
  Wallet,
  Plus,
  X,
  Send,
  Download,
  TrendingUp,
  TrendingDown,
  RotateCw,
  AlertTriangle,
} from 'lucide-react';
import { api } from '../services/api';
import { useBarisBertahap } from '../utils/bertahap';
import { formatMinor, formatQty, formatDate, toMinor, tanggalTokoISO, selisihHariBisnis } from '../utils/money';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';
import {
  accountTypeLabel,
  orderStatusLabel,
  purchaseStatusLabel,
} from '../utils/enums';

const SECTION_BY_MENU = {
  general_ledger: 'ledger',
  cash_bank: 'cash',
  payables_receivables: 'payables',
  financial_reports: 'reports',
};
const MENU_BY_SECTION = {
  ledger: 'general_ledger',
  cash: 'cash_bank',
  payables: 'payables_receivables',
  reports: 'financial_reports',
};

const ACCOUNT_TYPE = { ASSET: 0, LIABILITY: 1, EQUITY: 2, REVENUE: 3, EXPENSE: 4 };
const TYPE_NAMES = ['Asset', 'Liability', 'Equity', 'Revenue', 'Expense'];

// Satu halaman jurnal. Rentang sebulan bisa berisi ribuan kaki jurnal; memuat semuanya sekaligus
// membuat pindah menu tersendat, sedangkan laporan tetap tepat karena angkanya dihitung server.
const PAGE_JURNAL = 300;

// Server mengirim enum sebagai angka; peta nama ikut diterima bila format berubah.
const isType = (account, wanted) =>
  String(account?.type) === String(wanted) || account?.type === TYPE_NAMES[wanted];

// Kategori yang benar-benar dibedakan server saat penjurnalan biaya (AccountingService.PostExpenseAsync).
const EXPENSE_CATEGORIES = [
  { value: 'HPP', label: 'Bahan Baku & Kebutuhan Dapur', account: '5-001 Beban Bahan Baku (HPP)' },
  { value: 'Gaji', label: 'Gaji & Upah Karyawan', account: '5-003 Beban Gaji' },
  { value: 'Operasional', label: 'Biaya Operasional Lain', account: '5-002 Beban Operasional' },
];

// Default rentang memakai kalender toko, bukan UTC: toISOString() masih menunjukkan tanggal
// kemarin antara 00:00-06:59 WIB sehingga jurnal hari ini tidak ikut terpilih.
const todayISO = () => tanggalTokoISO();
const firstOfMonthISO = () => todayISO().slice(0, 8) + '01';

const cashAccounts = (balances) =>
  (balances ?? []).filter(
    (a) => isType(a, ACCOUNT_TYPE.ASSET) && /kas|bank/i.test(a.name ?? '')
  );

const accountByCode = (balances, code) =>
  (balances ?? []).find((a) => a.code === code);

// Akun dicari dari tipe + namanya lebih dulu; kode hanya cadangan. Bagan akun bisa saja memakai kode
// lain (Uang Muka Pelanggan tercatat 2-001, bukan 3-001), jadi menempel pada satu kode akan salah baca.
const findAccount = (balances, type, namePattern, fallbackCode) =>
  (balances ?? []).find((a) => isType(a, type) && namePattern.test(a.name ?? ''))
  ?? accountByCode(balances, fallbackCode);

// Umur piutang dibandingkan ke tanggal ambil: sudah lewat, tepat hari ini, atau masih hitung mundur.
const ageLabel = (days) => (days > 0 ? `${days} hari lewat` : days === 0 ? 'hari ini' : `H-${-days}`);

export default function FinanceView({
  view,
  onViewChange,
  balances,
  orders,
  purchases,
  summary,
  loading,
  onRefresh,
}) {
  const section = SECTION_BY_MENU[view] ?? 'ledger';
  const [range, setRange] = useState({ from: firstOfMonthISO(), to: todayISO() });
  const [applied, setApplied] = useState(range);
  const [accountFilter, setAccountFilter] = useState('');
  const [sourceFilter, setSourceFilter] = useState('');
  const [journal, setJournal] = useState(null);
  const [journalError, setJournalError] = useState('');
  const [journalLoading, setJournalLoading] = useState(false);
  const [journalMemuatLagi, setJournalMemuatLagi] = useState(false);
  const [journalAdaLagi, setJournalAdaLagi] = useState(false);
  // Ringkasan seluruh rentang dari server (bukan dari entri yang kebetulan termuat) dan saldo
  // akun per akhir rentang untuk buku kas.
  const [ringkasJurnal, setRingkasJurnal] = useState(null);
  const [saldoAkhirRentang, setSaldoAkhirRentang] = useState(null);
  const [versiData, setVersiData] = useState(0);
  const [expenseOpen, setExpenseOpen] = useState(false);
  const [expenseForm, setExpenseForm] = useState({ category: '', amount: '', description: '' });
  const [expenseError, setExpenseError] = useState('');
  const { sedangSimpan: expenseSaving, mulai: mulaiBiaya, selesai: selesaiBiaya } = useKunciSimpan();
  const [expenseKunci, setExpenseKunci] = useState('');

  const loadJournal = useCallback(async (from, to) => {
    setJournalLoading(true);
    setJournalError('');
    try {
      // Satu halaman terbaru cukup untuk buku besar; total rentang datang dari /api/journal/summary.
      const [halaman, saldo] = await Promise.all([
        api.getJournal(from, to, { limit: PAGE_JURNAL }),
        api.getJournalBalances(to),
      ]);
      const entri = Array.isArray(halaman) ? halaman : [];
      setJournal(entri);
      setJournalAdaLagi(entri.length === PAGE_JURNAL);
      setSaldoAkhirRentang(Array.isArray(saldo) ? saldo : null);
    } catch (err) {
      setJournalError(err.message || 'Gagal memuat jurnal.');
      setJournal([]);
      setJournalAdaLagi(false);
    } finally {
      setJournalLoading(false);
    }
  }, []);

  useEffect(() => {
    loadJournal(applied.from, applied.to);
  }, [applied.from, applied.to, versiData, loadJournal]);

  useEffect(() => {
    let batal = false;
    api.getJournalSummary(applied.from, applied.to, sourceFilter)
      .then((r) => { if (!batal) setRingkasJurnal(r); })
      .catch(() => { if (!batal) setRingkasJurnal(null); });
    return () => { batal = true; };
  }, [applied.from, applied.to, sourceFilter, versiData]);

  // Halaman berikutnya memakai kunci entri terakhir yang tampil, jadi entri baru yang masuk
  // tidak menggeser baris dan tidak ada entri yang terlewat.
  const muatJurnalLagi = async () => {
    const terakhir = (journal ?? [])[journal.length - 1];
    if (!terakhir) return;
    setJournalMemuatLagi(true);
    setJournalError('');
    try {
      const halaman = await api.getJournal(applied.from, applied.to, {
        limit: PAGE_JURNAL,
        beforeDate: String(terakhir.transactionDate ?? '').slice(0, 10),
        beforeId: terakhir.id,
      });
      const lanjutan = Array.isArray(halaman) ? halaman : [];
      const sudahAda = new Set((journal ?? []).map((e) => e.id));
      setJournal((lama) => [...(lama ?? []), ...lanjutan.filter((e) => !sudahAda.has(e.id))]);
      setJournalAdaLagi(lanjutan.length === PAGE_JURNAL);
    } catch (err) {
      setJournalError(err.message || 'Gagal memuat halaman berikutnya.');
    } finally {
      setJournalMemuatLagi(false);
    }
  };

  const entries = journal ?? [];
  const accountsById = useMemo(() => {
    const map = {};
    (balances ?? []).forEach((a) => { map[a.accountId] = a; });
    return map;
  }, [balances]);

  // Satu baris per kaki jurnal; entri tanpa baris tetap tampil supaya ketidaklengkapan terlihat.
  const lines = useMemo(() => {
    const rows = [];
    entries.forEach((entry) => {
      const entryLines = entry.lines ?? [];
      const sourceLabel = entry.sourceDocumentType
        ? `${entry.sourceDocumentType}${entry.sourceDocumentId ? ` #${entry.sourceDocumentId}` : ''}`
        : 'tidak bertaut';
      if (entryLines.length === 0) {
        rows.push({
          key: `e${entry.id}-none`, entryId: entry.id, date: entry.transactionDate,
          description: entry.description ?? '', account: null, debit: 0, credit: 0,
          sourceType: entry.sourceDocumentType ?? '', sourceLabel, orphan: true,
        });
        return;
      }
      entryLines.forEach((line, idx) => {
        // Akun bisa datang bersama kaki jurnal (JournalLine.Account) atau hanya lewat daftarnya.
        const raw = line.account ?? accountsById[line.accountId] ?? null;
        const account = raw
          ? {
              accountId: raw.accountId ?? raw.id ?? line.accountId,
              code: raw.code ?? '',
              name: raw.name ?? '',
              type: raw.type,
            }
          : null;
        rows.push({
          key: `e${entry.id}-l${line.id ?? idx}`, entryId: entry.id, date: entry.transactionDate,
          description: entry.description ?? '',
          account,
          debit: Number(line.debitAmount ?? 0),
          credit: Number(line.creditAmount ?? 0),
          sourceType: entry.sourceDocumentType ?? '',
          sourceLabel,
          status: entry.status,
        });
      });
    });
    return rows;
  }, [entries, accountsById]);

  // Pilihan sumber dokumen datang dari ringkasan server: daftar ini tidak boleh menyusut hanya
  // karena halaman jurnal yang termuat kebetulan tidak memuat sumber tertentu.
  const sourceOptions = useMemo(() => (ringkasJurnal?.sources ?? []).filter(Boolean), [ringkasJurnal]);

  const visibleLines = lines.filter((l) => {
    if (accountFilter && String(l.account?.accountId ?? '') !== accountFilter) return false;
    if (sourceFilter === '__none__') return !l.sourceType;
    if (sourceFilter && l.sourceType !== sourceFilter) return false;
    return true;
  });

  // Jurnal 300+ baris ditampilkan bertahap: filter dan total tetap dihitung dari seluruh baris,
  // yang dipotong hanya jumlah baris yang digambar.
  const barisJurnal = useBarisBertahap(visibleLines);

  // Total debit/kredit seluruh rentang dihitung dari ringkasan server (akun terpilih dapat
  // disaring di sini), bukan dari baris halaman yang sedang tampil.
  const akunRingkas = useMemo(() => (ringkasJurnal?.accounts ?? []).filter(
    (a) => !accountFilter || String(a.accountId) === accountFilter
  ), [ringkasJurnal, accountFilter]);
  const totals = akunRingkas.reduce(
    (acc, a) => ({ debit: acc.debit + a.debit, credit: acc.credit + a.credit }),
    { debit: 0, credit: 0 }
  );
  const balanced = totals.debit === totals.credit;
  const isFiltered = Boolean(accountFilter || sourceFilter);
  const postedCount = ringkasJurnal?.entries ?? entries.length;
  const totalKaki = ringkasJurnal?.legs ?? lines.length;

  const cashClosing = cashAccounts(saldoAkhirRentang ?? balances)
    .reduce((s, a) => s + Number(a.balance ?? 0), 0);

  // Buku kas: kaki pada akun kas/bank saja. Entri datang terbaru dulu; saldo berjalan dihitung
  // mundur dari saldo akhir rentang, lalu barisnya dibalik supaya tampil maju seperti sebelumnya.
  const cashRows = useMemo(() => {
    const ids = new Set(cashAccounts(saldoAkhirRentang ?? balances).map((a) => a.accountId));
    const kaki = [];
    entries.forEach((entry) => {
      (entry.lines ?? []).forEach((line) => {
        const raw2 = line.account ?? null;
        const accountId = raw2?.id ?? raw2?.accountId ?? line.accountId;
        if (!ids.has(accountId)) return;
        kaki.push({
          key: `c${entry.id}-${line.id}`,
          date: entry.transactionDate,
          description: entry.description ?? '',
          debit: Number(line.debitAmount ?? 0),
          credit: Number(line.creditAmount ?? 0),
          source: entry.sourceDocumentType ?? '-',
        });
      });
    });
    let saldo = cashClosing;
    const mundur = kaki.map((row) => {
      const denganSaldo = { ...row, balance: saldo };
      saldo -= row.debit - row.credit;
      return denganSaldo;
    });
    return mundur.reverse();
  }, [entries, balances, saldoAkhirRentang, cashClosing]);

  // Piutang dari pesanan + pembayaran; utang dari saldo akun Utang Usaha dan baris PO.
  const receivables = useMemo(() => {
    // Jadwal ambil dan "hari ini" sama-sama tanggal bisnis kalender toko; zona perangkat
    // tidak dipakai supaya status lewat tempo tidak bergeser sehari.
    const hariIni = tanggalTokoISO();
    return (orders ?? [])
      .filter((o) => String(o.status) !== '4' && o.status !== 'Cancelled')
      .map((o) => {
        const paid = Number(o.downPayment ?? 0) +
          (o.payments ?? []).reduce((s, p) => s + Number(p.amount ?? 0), 0);
        const remaining = Number(o.totalAmount ?? 0) - paid;
        // Positif = sudah lewat tanggal ambil, nol = hari ini, negatif = masih hitung mundur.
        const dayDiff = o.pickupDate ? selisihHariBisnis(o.pickupDate, hariIni) : null;
        return {
          id: o.id, orderNumber: o.orderNumber ?? `ORD-${o.id}`,
          customer: o.customerNameSnapshot ?? '-', pickupDate: o.pickupDate,
          total: Number(o.totalAmount ?? 0), paid, remaining,
          status: o.status,
          overdue: dayDiff !== null && dayDiff > 0 && remaining > 0,
          ageDays: dayDiff,
        };
      })
      .filter((r) => r.remaining > 0)
      .sort((a, b) => (b.overdue - a.overdue) || (b.remaining - a.remaining));
  }, [orders]);

  const payables = useMemo(() => (purchases ?? []).map((po) => {
    const linesOfPo = po.lines ?? [];
    const receivedValue = linesOfPo.reduce(
      (s, l) => s + Number(l.receivedQty ?? 0) * Number(l.unitCostMinorUnits ?? 0), 0
    );
    const orderedValue = linesOfPo.reduce(
      (s, l) => s + Number(l.orderedQty ?? 0) * Number(l.unitCostMinorUnits ?? 0), 0
    );
    return {
      id: po.id, poNumber: po.poNumber, supplier: po.supplierName ?? '-',
      status: po.status, orderedValue, receivedValue, lineCount: linesOfPo.length,
    };
  }), [purchases]);

  // Buku kas, piutang, dan utang juga bisa berisi ratusan baris; digambar bertahap sambil
  // ringkasan saldo tetap dihitung dari seluruh data.
  const barisKas = useBarisBertahap(cashRows);
  const barisPiutang = useBarisBertahap(receivables);
  const barisUtang = useBarisBertahap(payables);

  const receivableAccount = findAccount(balances, ACCOUNT_TYPE.ASSET, /piutang/i, '1-002');
  const payableAccount = findAccount(balances, ACCOUNT_TYPE.LIABILITY, /utang/i, '2-002');
  const advanceAccount = findAccount(balances, ACCOUNT_TYPE.LIABILITY, /uang muka/i, '2-001');
  const payableBalance = Number(payableAccount?.balance ?? 0);
  const advanceBalance = Number(advanceAccount?.balance ?? 0);
  const receivableBalance = Number(receivableAccount?.balance ?? 0);

  // Laba rugi per periode dihitung dari total per akun pada SELURUH rentang (server), bukan dari
  // baris halaman yang sedang tampil. Filter sumber sudah diterapkan server; filter akun di sini.
  const periodPnl = useMemo(() => {
    const group = (wanted, netOf) => akunRingkas
      .filter((a) => isType(a, wanted))
      .map((a) => ({ code: a.code, name: a.name, net: netOf(a), legs: a.legs }))
      .filter((r) => r.net !== 0)
      .sort((a, b) => b.net - a.net);
    const revenue = group(ACCOUNT_TYPE.REVENUE, (a) => a.credit - a.debit);
    const expense = group(ACCOUNT_TYPE.EXPENSE, (a) => a.debit - a.credit);
    const revenueTotal = revenue.reduce((s, r) => s + r.net, 0);
    const expenseTotal = expense.reduce((s, r) => s + r.net, 0);
    // HPP dikenali dari kode 5-001 atau nama bahan baku; tanpa keduanya margin kotor tidak dihitung.
    const hasCogs = expense.some((e) => /5-001/.test(e.code ?? '') || /bahan baku|hpp/i.test(e.name ?? ''));
    const cogs = expense
      .filter((e) => /5-001/.test(e.code ?? '') || /bahan baku|hpp/i.test(e.name ?? ''))
      .reduce((s, e) => s + e.net, 0);
    return {
      revenue, expense, revenueTotal, expenseTotal, hasCogs,
      profit: revenueTotal - expenseTotal,
      grossMargin: revenueTotal > 0 && hasCogs ? ((revenueTotal - cogs) / revenueTotal) * 100 : null,
    };
  }, [akunRingkas]);

  const balanceSheet = (balances ?? []).filter((a) =>
    isType(a, ACCOUNT_TYPE.ASSET) || isType(a, ACCOUNT_TYPE.LIABILITY) || isType(a, ACCOUNT_TYPE.EQUITY)
  );
  const assetTotal = balanceSheet.filter((a) => isType(a, ACCOUNT_TYPE.ASSET)).reduce((s, a) => s + Number(a.balance ?? 0), 0);
  const claimTotal = balanceSheet
    .filter((a) => !isType(a, ACCOUNT_TYPE.ASSET))
    .reduce((s, a) => s + Number(a.balance ?? 0), 0);

  const submitExpense = async (event) => {
    event.preventDefault();
    const nominal = Number(expenseForm.amount);
    if (!Number.isFinite(nominal) || nominal <= 0) {
      setExpenseError('Nominal harus angka lebih besar dari nol.');
      return;
    }
    if (!expenseForm.category) {
      setExpenseError('Pilih kategori biaya.');
      return;
    }
    if (!mulaiBiaya()) return;
    setExpenseError('');
    try {
      await api.recordExpense({
        category: expenseForm.category,
        description: expenseForm.description,
        amount: toMinor(nominal),
        idempotensi: expenseKunci,
      });
      setExpenseOpen(false);
      setExpenseForm({ category: '', amount: '', description: '' });
      await loadJournal(applied.from, applied.to);
      await onRefresh?.();
      setVersiData((v) => v + 1);
    } catch (err) {
      setExpenseError(err.message || 'Gagal mencatat pengeluaran.');
    } finally {
      selesaiBiaya();
    }
  };

  const tabs = [
    { id: 'ledger', label: 'Buku Besar & Jurnal Umum', icon: BookMarked },
    { id: 'cash', label: 'Kas Toko & Rekening Bank', icon: Landmark },
    { id: 'payables', label: 'Utang & Piutang Usaha', icon: Receipt },
    { id: 'reports', label: 'Laporan Keuangan & Laba Rugi', icon: FileSpreadsheet },
  ];

  const exportJournal = (format) =>
    api.exportJournal(applied.from, applied.to, format).catch((e) => alert('Gagal export: ' + e.message));

  return (
    <div className="space-y-6">
      {/* Banner */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold text-white tracking-tight">Keuangan, Buku Kas & Jurnal Otomatis</h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold bg-blue-500/10 text-blue-400 border border-blue-500/20 rounded-full">
              Buku Besar
            </span>
          </div>
          <p className="text-sm text-slate-400 mt-1">
            Semua nominal dibaca dari kaki jurnal pada rentang tanggal terpilih. Istilah: "tercatat" = status Posted di basis data.
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button
            onClick={onRefresh}
            className="p-2.5 bg-slate-800/80 hover:bg-slate-700 text-slate-300 rounded-xl transition-colors border border-slate-700/60"
            title="Muat Ulang Data"
          >
            <RotateCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button
            onClick={() => { setExpenseError(''); setExpenseKunci(kunciBaru('biaya-baru')); setExpenseOpen(true); }}
            className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all"
          >
            <Plus className="w-4 h-4" />
            <span>Catat Pengeluaran Kas</span>
          </button>
        </div>
      </div>

      {/* navigasi empat layar */}
      <div className="flex items-center gap-2 border-b border-slate-800 pb-2 flex-wrap">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => onViewChange?.(MENU_BY_SECTION[tab.id])}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-bold transition-all ${
              section === tab.id
                ? 'bg-blue-500/20 text-blue-400 border border-blue-500/30'
                : 'text-slate-400 hover:text-white'
            }`}
          >
            <tab.icon className="w-3.5 h-3.5" />
            <span>{tab.label}</span>
          </button>
        ))}
      </div>

      {/* Panel filter periode — dipakai semua tab */}
      <div className="bg-slate-900/60 border border-slate-800 rounded-2xl p-4 flex items-end gap-3 flex-wrap">
        <div>
          <label className="text-[11px] font-semibold text-slate-400 block mb-1">Dari tanggal</label>
          <input
            type="date"
            value={range.from}
            onChange={(e) => setRange({ ...range, from: e.target.value })}
            className="px-2.5 py-1.5 bg-slate-950 text-slate-200 rounded-lg text-xs border border-slate-800 focus:outline-none focus:border-blue-500"
          />
        </div>
        <div>
          <label className="text-[11px] font-semibold text-slate-400 block mb-1">Sampai tanggal</label>
          <input
            type="date"
            value={range.to}
            onChange={(e) => setRange({ ...range, to: e.target.value })}
            className="px-2.5 py-1.5 bg-slate-950 text-slate-200 rounded-lg text-xs border border-slate-800 focus:outline-none focus:border-blue-500"
          />
        </div>
        <button
          onClick={() => setApplied(range)}
          disabled={journalLoading}
          className="px-4 py-1.5 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-xs font-bold transition-colors disabled:opacity-50"
        >
          Terapkan
        </button>
        <span className="text-[11px] text-slate-500 ml-auto">
          Rentang aktif: <span className="text-slate-300 font-mono">{applied.from}</span> – <span className="text-slate-300 font-mono">{applied.to}</span>
          {journalLoading ? ' · memuat…' : ` · ${postedCount} entri · ${entries.length} termuat`}
        </span>
      </div>

      {journalError && (
        <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs flex items-center gap-2">
          <AlertTriangle className="w-4 h-4 shrink-0" />
          <span>{journalError}</span>
        </div>
      )}

      {section === 'ledger' && (
        <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
          <div className="p-4 border-b border-slate-800 flex items-center justify-between flex-wrap gap-2">
            <h2 className="text-base font-semibold text-white flex items-center gap-2">
              <BookMarked className="w-4 h-4 text-amber-400" />
              <span>Jurnal per rentang tanggal</span>
            </h2>
            <div className="flex items-center gap-2 flex-wrap">
              <select
                value={accountFilter}
                onChange={(e) => setAccountFilter(e.target.value)}
                className="bg-slate-950 text-slate-200 border border-slate-800 rounded-lg px-2 py-1.5 text-xs focus:outline-none focus:border-blue-500"
              >
                <option value="">Semua akun</option>
                {(balances ?? []).map((a) => (
                  <option key={a.accountId} value={a.accountId}>{a.code} · {a.name}</option>
                ))}
              </select>
              <select
                value={sourceFilter}
                onChange={(e) => setSourceFilter(e.target.value)}
                className="bg-slate-950 text-slate-200 border border-slate-800 rounded-lg px-2 py-1.5 text-xs focus:outline-none focus:border-blue-500"
              >
                <option value="">Semua sumber</option>
                {sourceOptions.map((s) => (
                  <option key={s} value={s}>{s}</option>
                ))}
                <option value="__none__">tidak bertaut</option>
              </select>
              <button
                onClick={() => exportJournal('excel')}
                className="flex items-center gap-1.5 px-3 py-1.5 bg-emerald-700 hover:bg-emerald-600 text-white rounded-lg text-xs font-medium transition-colors"
              >
                <Download className="w-3.5 h-3.5" />
                <span>Excel</span>
              </button>
              <button
                onClick={() => exportJournal('pdf')}
                className="flex items-center gap-1.5 px-3 py-1.5 bg-rose-700 hover:bg-rose-600 text-white rounded-lg text-xs font-medium transition-colors"
              >
                <Download className="w-3.5 h-3.5" />
                <span>PDF</span>
              </button>
            </div>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs text-slate-300">
              <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                <tr>
                  <th className="py-3 px-4 font-semibold">Tgl.</th>
                  <th className="py-3 px-4 font-semibold">Ref</th>
                  <th className="py-3 px-4 font-semibold">Keterangan</th>
                  <th className="py-3 px-4 font-semibold">Akun</th>
                  <th className="py-3 px-4 font-semibold text-right">Debit</th>
                  <th className="py-3 px-4 font-semibold text-right">Kredit</th>
                  <th className="py-3 px-4 font-semibold">Sumber</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60 font-mono">
                {visibleLines.length === 0 && (
                  <tr>
                    <td colSpan={7} className="py-6 px-4 text-center text-slate-500 font-sans">
                      {journalLoading
                        ? 'Memuat jurnal…'
                        : 'Tidak ada kaki jurnal pada rentang dan filter ini. Tidak ada contoh data yang ditampilkan.'}
                    </td>
                  </tr>
                )}
                {barisJurnal.tampil.map((line) => (
                    <tr key={line.key} className="hover:bg-slate-800/40 transition-colors">
                      <td className="py-3 px-4 text-slate-400">{formatDate(line.date)}</td>
                      <td className="py-3 px-4 font-bold text-amber-400">J-{String(line.entryId).padStart(4, '0')}</td>
                      <td className="py-3 px-4 font-sans text-slate-200">
                        {line.description}
                        {line.orphan && <span className="text-[10px] text-rose-400 font-mono"> · entri tanpa baris</span>}
                      </td>
                      <td className="py-3 px-4 font-sans text-white">
                        {line.account ? `${line.account.code} · ${line.account.name}` : <span className="text-slate-500">akun tidak diketahui</span>}
                      </td>
                      <td className="py-3 px-4 text-right text-emerald-400">{line.debit > 0 ? formatMinor(line.debit) : '-'}</td>
                      <td className="py-3 px-4 text-right text-rose-400">{line.credit > 0 ? formatMinor(line.credit) : '-'}</td>
                      <td className="py-3 px-4 text-[11px] text-slate-400">{line.sourceLabel}</td>
                    </tr>
                  ))}
              </tbody>
              <tfoot className="bg-slate-950/60 border-t border-slate-800">
                <tr>
                  <td className="py-3 px-4 font-sans text-slate-400" colSpan={4}>
                    {visibleLines.length} baris tampil dari {entries.length} entri termuat · {totalKaki} kaki jurnal · {postedCount} entri tercatat pada rentang
                  </td>
                  <td className="py-3 px-4 text-right font-bold text-emerald-400">{formatMinor(totals.debit)}</td>
                  <td className="py-3 px-4 text-right font-bold text-rose-400">{formatMinor(totals.credit)}</td>
                  <td className="py-3 px-4">
                    <span className={`text-[10px] font-sans font-bold ${isFiltered ? 'text-slate-400' : balanced ? 'text-emerald-400' : 'text-rose-400'}`}>
                      {isFiltered ? 'parsial' : balanced ? 'seimbang' : 'SELISIH'}
                    </span>
                  </td>
                </tr>
                {barisJurnal.adaLagi && (
                  <tr>
                    <td colSpan={7} className="py-2 px-4 text-center border-t border-slate-800">
                      <button
                        onClick={barisJurnal.tampilkanLagi}
                        className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                      >
                        Tampilkan 100 baris berikutnya ({barisJurnal.jumlah - barisJurnal.tampil.length} tersisa)
                      </button>
                    </td>
                  </tr>
                )}
              </tfoot>
            </table>
          </div>

          {journalAdaLagi && (
            <div className="px-4 py-2.5 border-t border-slate-800 flex items-center justify-between gap-2">
              <span className="text-[11px] text-slate-500">
                Menampilkan {entries.length} entri terbaru; entri lama dimuat bertahap.
              </span>
              <button
                onClick={muatJurnalLagi}
                disabled={journalMemuatLagi}
                className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors disabled:opacity-50"
              >
                {journalMemuatLagi ? 'Memuat…' : 'Muat entri lebih lama'}
              </button>
            </div>
          )}

          <div className="px-4 py-3 border-t border-slate-800 text-[11px] text-slate-500 leading-relaxed">
            Kolom "Ref" dibuat dari Id entri jurnal; basis data belum punya nomor dokumen jurnal. Kolom "Sumber"
            menunjukkan tautan dokumen: entri biaya belum menyimpan nomor dokumen, jadi tidak dapat ditelusuri ke bukti aslinya.
          </div>
        </div>
      )}

      {section === 'cash' && (
        <>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Saldo kas &amp; bank per {applied.to}</div>
              <div className={`text-xl font-bold mt-2 font-mono ${cashClosing < 0 ? 'text-rose-400' : 'text-white'}`}>
                {formatMinor(cashClosing)}
              </div>
              <div className="text-[11px] text-slate-400 mt-1">
                {cashAccounts(balances).map((a) => a.code).join(', ') || 'tidak ada akun kas'} · arah normal debit
              </div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400 flex items-center gap-1.5">
                <TrendingUp className="w-4 h-4 text-amber-400" /> Masuk hari bisnis ini
              </div>
              <div className="text-xl font-bold text-amber-400 mt-2 font-mono">
                {summary ? formatMinor(summary.revenue ?? 0) : '-'}
              </div>
              <div className="text-[11px] text-slate-400 mt-1">kas masuk pada akun 1-001 hari ini</div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400 flex items-center gap-1.5">
                <TrendingDown className="w-4 h-4 text-rose-400" /> Keluar hari bisnis ini
              </div>
              <div className="text-xl font-bold text-rose-400 mt-2 font-mono">
                {summary ? formatMinor(summary.expense ?? 0) : '-'}
              </div>
              <div className="text-[11px] text-slate-400 mt-1">kas keluar pada akun 1-001 hari ini</div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Selisih vs kas fisik</div>
              <div className="text-xl font-bold text-slate-500 mt-2 font-mono">belum diisi</div>
              <div className="text-[11px] text-slate-400 mt-1">butuh hitung kas per akhir shift — belum ada fiturnya</div>
            </div>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-3 gap-4">
            <div className="xl:col-span-2 bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
              <div className="p-4 border-b border-slate-800">
                <h2 className="text-base font-semibold text-white">
                  Buku kas — {cashAccounts(saldoAkhirRentang ?? balances).map((a) => `${a.code} ${a.name}`).join(' + ') || 'tidak ada akun kas'}
                </h2>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs text-slate-300">
                  <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="py-3 px-4 font-semibold">Tgl.</th>
                      <th className="py-3 px-4 font-semibold">Keterangan</th>
                      <th className="py-3 px-4 font-semibold text-right">Masuk</th>
                      <th className="py-3 px-4 font-semibold text-right">Keluar</th>
                      <th className="py-3 px-4 font-semibold text-right">Saldo berjalan</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60 font-mono">
                    {cashRows.length === 0 && (
                      <tr><td colSpan={5} className="py-6 px-4 text-center text-slate-500 font-sans">
                        Tidak ada gerakan kas pada rentang ini.
                      </td></tr>
                    )}
                    {barisKas.tampil.map((row) => (
                      <tr key={row.key} className="hover:bg-slate-800/40">
                        <td className="py-3 px-4 text-slate-400">{formatDate(row.date)}</td>
                        <td className="py-3 px-4 font-sans text-slate-200">{row.description}</td>
                        <td className="py-3 px-4 text-right text-emerald-400">{row.debit > 0 ? formatMinor(row.debit) : '-'}</td>
                        <td className="py-3 px-4 text-right text-rose-400">{row.credit > 0 ? formatMinor(row.credit) : '-'}</td>
                        <td className={`py-3 px-4 text-right font-bold ${row.balance < 0 ? 'text-rose-400' : 'text-white'}`}>
                          {formatMinor(row.balance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot className="bg-slate-950/60 border-t border-slate-800">
                    <tr>
                      <td colSpan={4} className="py-3 px-4 font-sans text-slate-400">Saldo akun kas per akhir rentang ({applied.to})</td>
                      <td className={`py-3 px-4 text-right font-bold ${cashClosing < 0 ? 'text-rose-400' : 'text-white'}`}>{formatMinor(cashClosing)}</td>
                    </tr>
                    {barisKas.adaLagi && (
                      <tr>
                        <td colSpan={5} className="py-3 px-4 text-center">
                          <button
                            onClick={barisKas.tampilkanLagi}
                            className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                          >
                            Tampilkan 100 baris berikutnya ({barisKas.jumlah - barisKas.tampil.length} tersisa)
                          </button>
                        </td>
                      </tr>
                    )}
                  </tfoot>
                </table>
              </div>
              <div className="px-4 py-3 border-t border-slate-800 text-[11px] text-slate-500 leading-relaxed">
                Satu akun menampung kas toko dan rekening bank, jadi saldo tidak dapat dipisahkan per rekening.
                Saldo negatif berarti pencatatan keluar lebih besar dari masuk — cek lagi posting yang belum dibuat.
              </div>
            </div>

            <div className="bg-slate-900/80 border border-slate-800 rounded-2xl p-5 space-y-3">
              <h2 className="text-base font-semibold text-white flex items-center gap-2">
                <Landmark className="w-4 h-4 text-blue-400" />
                <span>Rekonsiliasi akhir shift</span>
              </h2>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1">Kas fisik dihitung (Rp)</label>
                <input type="number" disabled className="w-full bg-slate-950/60 text-slate-600 border border-slate-800 rounded-xl p-2.5 text-xs cursor-not-allowed" placeholder="0" />
              </div>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1">Tanggal hitung</label>
                <input type="date" disabled value={applied.to} className="w-full bg-slate-950/60 text-slate-600 border border-slate-800 rounded-xl p-2.5 text-xs cursor-not-allowed" />
              </div>
              <button disabled className="w-full px-4 py-2 bg-slate-800 text-slate-500 rounded-xl text-xs font-bold cursor-not-allowed">
                Simpan rekonsiliasi
              </button>
              <p className="text-[11px] text-slate-500 leading-relaxed">
                Sengaja dinonaktifkan: belum ada endpoint rekonsiliasi maupun tabel penyimpanan selisih kas.
                Saat fitur ini dibuat, selisih harus tercatat sebagai penyesuaian kas plus jejak audit, bukan diubah diam-diam.
              </p>
            </div>
          </div>
        </>
      )}

      {section === 'payables' && (
        <>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Piutang usaha (saldo akun)</div>
              <div className="text-xl font-bold text-white mt-2 font-mono">{formatMinor(receivableBalance)}</div>
              <div className="text-[11px] text-slate-400 mt-1">{receivableAccount?.code ?? 'belum ada akun'} · dari kaki jurnal Posted</div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Sisa bayar pesanan</div>
              <div className="text-xl font-bold text-amber-400 mt-2 font-mono">
                {formatMinor(receivables.reduce((s, r) => s + r.remaining, 0))}
              </div>
              <div className="text-[11px] text-slate-400 mt-1">{receivables.length} pesanan belum lunas</div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Jatuh tempo lewat</div>
              <div className={`text-xl font-bold mt-2 font-mono ${receivables.some((r) => r.overdue) ? 'text-rose-400' : 'text-emerald-400'}`}>
                {formatMinor(receivables.filter((r) => r.overdue).reduce((s, r) => s + r.remaining, 0))}
              </div>
              <div className="text-[11px] text-slate-400 mt-1">
                {receivables.filter((r) => r.overdue).length} pesanan lewat tanggal ambil
              </div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Uang muka pelanggan</div>
              <div className="text-xl font-bold text-white mt-2 font-mono">{formatMinor(advanceBalance)}</div>
              <div className="text-[11px] text-slate-400 mt-1">
                {advanceAccount?.code ?? 'belum ada akun'} · {advanceAccount?.name ?? 'uang muka pelanggan'}
                <span className="text-slate-500"> · negatif = pendapatan diakui melebihi setoran</span>
              </div>
            </div>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
              <div className="p-4 border-b border-slate-800">
                <h2 className="text-base font-semibold text-white">Piutang per pesanan</h2>
                <p className="text-[11px] text-slate-500 mt-0.5">sumber: SalesOrders + SalesPayments</p>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs text-slate-300">
                  <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="py-3 px-4 font-semibold">No. pesanan</th>
                      <th className="py-3 px-4 font-semibold">Pelanggan</th>
                      <th className="py-3 px-4 font-semibold">Nilai</th>
                      <th className="py-3 px-4 font-semibold text-right">Dibayar</th>
                      <th className="py-3 px-4 font-semibold text-right">Sisa</th>
                      <th className="py-3 px-4 font-semibold text-right">Umur vs tanggal ambil</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60 font-mono">
                    {receivables.length === 0 && (
                      <tr><td colSpan={6} className="py-6 px-4 text-center text-slate-500 font-sans">Tidak ada pesanan dengan sisa bayar.</td></tr>
                    )}
                    {barisPiutang.tampil.map((r) => (
                      <tr key={r.id} className="hover:bg-slate-800/40">
                        <td className="py-3 px-4">
                          <div className="text-amber-400">{r.orderNumber}</div>
                          <div className="text-[10px] text-slate-500">{orderStatusLabel(r.status)} · ambil {formatDate(r.pickupDate)}</div>
                        </td>
                        <td className="py-3 px-4 font-sans text-white">{r.customer}</td>
                        <td className="py-3 px-4">{formatMinor(r.total)}</td>
                        <td className="py-3 px-4 text-right text-emerald-400">{formatMinor(r.paid)}</td>
                        <td className="py-3 px-4 text-right font-bold text-rose-400">{formatMinor(r.remaining)}</td>
                        <td className="py-3 px-4 text-right">
                          {r.ageDays === null ? '-' : (
                            <span className={r.overdue ? 'text-rose-400 font-bold' : r.ageDays < 0 ? 'text-slate-400' : 'text-amber-400'}>
                              {ageLabel(r.ageDays)}
                            </span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {barisPiutang.adaLagi && (
                <div className="px-4 py-2.5 border-t border-slate-800 text-center">
                  <button
                    onClick={barisPiutang.tampilkanLagi}
                    className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                  >
                    Tampilkan 100 baris berikutnya ({barisPiutang.jumlah - barisPiutang.tampil.length} tersisa)
                  </button>
                </div>
              )}
              <div className="px-4 py-3 border-t border-slate-800 text-[11px] text-slate-500">
                Sisa bayar dihitung dari pesanan; sebagian pembayaran belum punya kaki jurnal, jadi angka ini dapat
                berbeda dari saldo akun {receivableAccount?.code ?? 'piutang'} di atas.
              </div>
            </div>

            <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
              <div className="p-4 border-b border-slate-800">
                <h2 className="text-base font-semibold text-white">Utang pemasok per dokumen</h2>
                <p className="text-[11px] text-slate-500 mt-0.5">
                  sumber: PO yang belum diterima penuh (Draft/Terkirim/Sebagian) + barisnya; saldo akun {payableAccount?.code ?? '(belum ada)'} = {formatMinor(payableBalance)}
                </p>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs text-slate-300">
                  <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="py-3 px-4 font-semibold">Dokumen</th>
                      <th className="py-3 px-4 font-semibold">Pemasok</th>
                      <th className="py-3 px-4 font-semibold text-right">Nilai pesanan</th>
                      <th className="py-3 px-4 font-semibold text-right">Nilai terima</th>
                      <th className="py-3 px-4 font-semibold text-right">Baris</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60 font-mono">
                    {payables.length === 0 && (
                      <tr><td colSpan={5} className="py-6 px-4 text-center text-slate-500 font-sans">Belum ada dokumen pembelian.</td></tr>
                    )}
                    {barisUtang.tampil.map((p) => (
                      <tr key={p.id} className="hover:bg-slate-800/40">
                        <td className="py-3 px-4">
                          <div className="text-amber-400">{p.poNumber}</div>
                          <div className="text-[10px] text-slate-500">{purchaseStatusLabel(p.status)}</div>
                        </td>
                        <td className="py-3 px-4 font-sans text-white">{p.supplier}</td>
                        <td className="py-3 px-4 text-right">{formatMinor(p.orderedValue)}</td>
                        <td className="py-3 px-4 text-right text-white">{p.receivedValue > 0 ? formatMinor(p.receivedValue) : '-'}</td>
                        <td className="py-3 px-4 text-right">{p.lineCount}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {barisUtang.adaLagi && (
                <div className="px-4 py-2.5 border-t border-slate-800 text-center">
                  <button
                    onClick={barisUtang.tampilkanLagi}
                    className="px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 rounded-lg text-xs font-medium transition-colors"
                  >
                    Tampilkan 100 dokumen berikutnya ({barisUtang.jumlah - barisUtang.tampil.length} tersisa)
                  </button>
                </div>
              )}

              <div className="p-4 border-t border-slate-800 space-y-3">
                <h3 className="text-sm font-semibold text-white">Catat pembayaran pemasok</h3>
                <div className="grid grid-cols-2 gap-3">
                  <select disabled className="bg-slate-950/60 text-slate-600 border border-slate-800 rounded-xl p-2.5 text-xs cursor-not-allowed">
                    <option>— pilih dokumen —</option>
                  </select>
                  <input disabled placeholder="Nominal (Rp)" className="bg-slate-950/60 text-slate-600 border border-slate-800 rounded-xl p-2.5 text-xs cursor-not-allowed" />
                </div>
                <button disabled className="w-full px-4 py-2 bg-slate-800 text-slate-500 rounded-xl text-xs font-bold cursor-not-allowed">
                  Simpan pembayaran
                </button>
                <p className="text-[11px] text-slate-500 leading-relaxed">
                  Sengaja dinonaktifkan: belum ada endpoint pembayaran pemasok, jadi tidak ada posting
                  Dr Utang Usaha / Cr Kas. Nilai terima di atas baru memakai harga per baris PO.
                </p>
              </div>
            </div>
          </div>
        </>
      )}

      {section === 'reports' && (
        <>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Omzet periode</div>
              <div className="text-xl font-bold text-emerald-400 mt-2 font-mono">{formatMinor(periodPnl.revenueTotal)}</div>
              <div className="text-[11px] text-slate-400 mt-1">kredit akun pendapatan pada rentang</div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Laba (rugi) bersih</div>
              <div className={`text-xl font-bold mt-2 font-mono ${periodPnl.profit < 0 ? 'text-rose-400' : 'text-white'}`}>
                {formatMinor(periodPnl.profit)}
              </div>
              <div className="text-[11px] text-slate-400 mt-1">omzet dikurangi seluruh beban terjurnal</div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Margin kotor periode</div>
              <div className="text-xl font-bold text-white mt-2 font-mono">
                {periodPnl.grossMargin === null ? '-' : `${formatQty(periodPnl.grossMargin)} %`}
              </div>
              <div className="text-[11px] text-slate-400 mt-1">
                {periodPnl.hasCogs ? 'setelah beban bahan baku (HPP)' : 'tidak ada beban bahan baku terjurnal, margin tidak dihitung'}
              </div>
            </div>
            <div className="bg-slate-900/80 border border-slate-800 p-4 rounded-xl">
              <div className="text-xs font-medium text-slate-400">Pagar periode</div>
              <div className="text-xl font-bold text-amber-400 mt-2 font-mono">belum ada</div>
              <div className="text-[11px] text-slate-400 mt-1">FiscalPeriods belum punya endpoint, tanggal lama masih bisa diubah</div>
            </div>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
            <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
              <div className="p-4 border-b border-slate-800 flex items-center justify-between gap-2 flex-wrap">
                <h2 className="text-base font-semibold text-white">Laba Rugi — {applied.from} s/d {applied.to}</h2>
                <div className="flex items-center gap-2">
                  <button onClick={() => exportJournal('excel')} className="flex items-center gap-1.5 px-3 py-1.5 bg-emerald-700 hover:bg-emerald-600 text-white rounded-lg text-xs font-medium">
                    <Download className="w-3.5 h-3.5" /><span>Excel</span>
                  </button>
                  <button onClick={() => exportJournal('pdf')} className="flex items-center gap-1.5 px-3 py-1.5 bg-rose-700 hover:bg-rose-600 text-white rounded-lg text-xs font-medium">
                    <Download className="w-3.5 h-3.5" /><span>PDF</span>
                  </button>
                </div>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs text-slate-300">
                  <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="py-3 px-4 font-semibold">Perkiraan</th>
                      <th className="py-3 px-4 font-semibold text-right">Pendapatan</th>
                      <th className="py-3 px-4 font-semibold text-right">Beban</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60 font-mono">
                    {periodPnl.revenue.length + periodPnl.expense.length === 0 && (
                      <tr><td colSpan={3} className="py-6 px-4 text-center text-slate-500 font-sans">
                        Tidak ada akun pendapatan/beban yang bergerak pada rentang ini.
                      </td></tr>
                    )}
                    {periodPnl.revenue.map((r) => (
                      <tr key={`r-${r.code}`} className="hover:bg-slate-800/40">
                        <td className="py-3 px-4 font-sans text-white">
                          {r.code} · {r.name}
                          <span className="text-[10px] text-slate-500"> · {r.legs} kaki</span>
                        </td>
                        <td className="py-3 px-4 text-right text-emerald-400">{formatMinor(r.net)}</td>
                        <td className="py-3 px-4 text-right text-slate-600">-</td>
                      </tr>
                    ))}
                    {periodPnl.expense.map((e) => (
                      <tr key={`e-${e.code}`} className="hover:bg-slate-800/40">
                        <td className="py-3 px-4 font-sans text-white">
                          {e.code} · {e.name}
                          <span className="text-[10px] text-slate-500"> · {e.legs} kaki</span>
                        </td>
                        <td className="py-3 px-4 text-right text-slate-600">-</td>
                        <td className="py-3 px-4 text-right text-rose-400">{formatMinor(e.net)}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot className="bg-slate-950/60 border-t border-slate-800">
                    <tr>
                      <td className="py-3 px-4 font-sans font-bold text-white">Laba (rugi) bersih</td>
                      <td className="py-3 px-4 text-right font-bold text-emerald-400">{formatMinor(periodPnl.revenueTotal)}</td>
                      <td className={`py-3 px-4 text-right font-bold ${periodPnl.profit < 0 ? 'text-rose-400' : 'text-white'}`}>
                        {formatMinor(periodPnl.profit)}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
              <div className="px-4 py-3 border-t border-slate-800 text-[11px] text-slate-500">
                Hanya memakai kaki jurnal Posted pada rentang aktif. Biaya tenaga kerja dan overhead SPK belum terjurnal
                bila pesanan produksinya belum diselesaikan, jadi beban periode ini bisa lebih kecil dari kenyataan.
              </div>
            </div>

            <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
              <div className="p-4 border-b border-slate-800">
                <h2 className="text-base font-semibold text-white">Neraca saldo per akun</h2>
                <p className="text-[11px] text-slate-500 mt-0.5">saldo sepanjang masa pada arah normal akun</p>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs text-slate-300">
                  <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="py-3 px-4 font-semibold">Akun</th>
                      <th className="py-3 px-4 font-semibold">Tipe</th>
                      <th className="py-3 px-4 font-semibold text-right">Saldo normal</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/60 font-mono">
                    {balanceSheet.length === 0 && (
                      <tr><td colSpan={3} className="py-6 px-4 text-center text-slate-500 font-sans">Belum ada saldo akun.</td></tr>
                    )}
                    {balanceSheet.map((a) => (
                      <tr key={a.accountId} className="hover:bg-slate-800/40">
                        <td className="py-3 px-4 font-sans text-white">{a.code} · {a.name}</td>
                        <td className="py-3 px-4 text-slate-400">{accountTypeLabel(a.type)}</td>
                        <td className={`py-3 px-4 text-right ${Number(a.balance) < 0 ? 'text-rose-400' : 'text-slate-200'}`}>
                          {formatMinor(a.balance)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot className="bg-slate-950/60 border-t border-slate-800">
                    <tr>
                      <td colSpan={2} className="py-3 px-4 font-sans text-slate-400">Aset vs Kewajiban + Modal</td>
                      <td className={`py-3 px-4 text-right font-bold ${assetTotal !== claimTotal ? 'text-rose-400' : 'text-emerald-400'}`}>
                        {formatMinor(assetTotal)} / {formatMinor(claimTotal)}
                      </td>
                    </tr>
                  </tfoot>
                </table>
              </div>
              <div className="px-4 py-3 border-t border-slate-800 text-[11px] text-slate-500 leading-relaxed">
                Selisih antara aset dan kewajiban+modal adalah laba berjalan yang belum ditutup ke akun Modal —
                pembukuan belum punya langkah tutup buku. Omzet per produk dan tren antar periode ada di menu
                Analisis &amp; Tren Penjualan; rincian omzet per kanal belum dicatat karena transaksi tidak
                menyimpan kanal penjualan.
              </div>
            </div>
          </div>
        </>
      )}

      {/* Modal pengeluaran kas */}
      {expenseOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden">
            <div className="p-4 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between">
              <h3 className="text-base font-bold text-white flex items-center gap-2">
                <Wallet className="w-4 h-4 text-amber-400" />
                <span>Catat Pengeluaran Kas</span>
              </h3>
              <button onClick={() => setExpenseOpen(false)} className="text-slate-400 hover:text-white transition-colors">
                <X className="w-4 h-4" />
              </button>
            </div>

            <form onSubmit={submitExpense} className="p-5 space-y-4">
              {expenseError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
                  {expenseError}
                </div>
              )}

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Kategori biaya</label>
                <select
                  value={expenseForm.category}
                  onChange={(e) => setExpenseForm({ ...expenseForm, category: e.target.value })}
                  className="w-full bg-slate-950 text-slate-200 border border-slate-800 rounded-xl p-2.5 text-xs focus:outline-none focus:border-amber-500"
                  required
                >
                  <option value="">— pilih kategori —</option>
                  {EXPENSE_CATEGORIES.map((cat) => (
                    <option key={cat.value} value={cat.value}>{cat.label}</option>
                  ))}
                </select>
                <div className="text-[11px] text-slate-500 mt-1">
                  Akan didebit: <span className="text-slate-300">
                    {EXPENSE_CATEGORIES.find((c) => c.value === expenseForm.category)?.account ?? '5-002 Beban Operasional'}
                  </span>{' '}
                  dan dikredit dari 1-001 Kas &amp; Bank.
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Nominal (Rp)</label>
                <input
                  type="number"
                  min="1"
                  step="1"
                  value={expenseForm.amount}
                  onChange={(e) => setExpenseForm({ ...expenseForm, amount: e.target.value })}
                  className="w-full bg-slate-950 text-white font-bold text-base border border-slate-800 rounded-xl p-2.5 focus:outline-none focus:border-amber-500"
                  placeholder="0"
                  required
                />
                <div className="text-[11px] text-slate-500 mt-1">
                  {expenseForm.amount ? `Terkirim sebagai ${formatMinor(toMinor(expenseForm.amount))} (unit minor Rp × 100)` : 'Nominal dikirim dalam unit minor.'}
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Keterangan</label>
                <input
                  type="text"
                  value={expenseForm.description}
                  onChange={(e) => setExpenseForm({ ...expenseForm, description: e.target.value })}
                  className="w-full bg-slate-950 text-slate-200 border border-slate-800 rounded-xl p-2.5 text-xs focus:outline-none focus:border-amber-500"
                  placeholder="Misal: beli gas elpiji 3 tabung"
                  required
                />
              </div>

              <div className="pt-1 flex justify-end gap-2.5">
                <button type="button" onClick={() => setExpenseOpen(false)} className="px-4 py-2 text-xs font-semibold text-slate-400 hover:text-white">
                  Batal
                </button>
                <button
                  type="submit"
                  disabled={expenseSaving}
                  className="flex items-center gap-1.5 px-4 py-2 bg-gradient-to-r from-amber-500 to-amber-600 text-slate-950 font-bold rounded-xl text-xs shadow-lg shadow-amber-500/20 disabled:opacity-50"
                >
                  <Send className="w-3.5 h-3.5" />
                  <span>{expenseSaving ? 'Menyimpan…' : 'Simpan Pengeluaran'}</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
