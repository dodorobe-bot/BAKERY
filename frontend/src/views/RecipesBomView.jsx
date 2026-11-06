import React, { useState } from 'react';
import { Plus, Search, X, ChefHat, Package, Edit2, Trash2 } from 'lucide-react';
import { api } from '../services/api';
import { formatMinor, fromMinor, toMinor, formatQty } from '../utils/money';
import { kunciBaru, useKunciSimpan } from '../utils/idempotensi';

const CATEGORIES = ['Roti', 'Kue', 'Minuman', 'Lainnya'];

const calcHpp = (product) => {
  // Try hpp field from GET /api/products/{id}; otherwise sum from recipeLines.
  // baseQuantity sudah dikonversi server ke satuan stok (250 g -> 0,25 kg).
  if (typeof product.hpp === 'number') return product.hpp;
  if (typeof product.Hpp === 'number') return product.Hpp;
  const lines = product.recipeLines || product.RecipeLines || [];
  return lines.reduce((sum, l) => {
    const price = l.ingredient?.pricePerUnit ?? l.Ingredient?.PricePerUnit ?? 0;
    const qty = l.baseQuantity ?? l.BaseQuantity ?? l.quantity ?? l.Quantity ?? 0;
    return sum + qty * price;
  }, 0);
};

const calcMargin = (sellingMinorUnits, hppMinorUnits) => {
  if (!sellingMinorUnits || sellingMinorUnits === 0) return '—';
  const margin = ((sellingMinorUnits - hppMinorUnits) / sellingMinorUnits) * 100;
  return margin.toFixed(1) + '%';
};

const emptyForm = () => ({
  name: '',
  category: 'Roti',
  barcode: '',
  sellingPrice: '',
  lines: [{ ingredientId: '', quantity: '', unit: '' }],
});

const emptyBahan = () => ({
  name: '',
  brand: '',
  unit: '',
  pricePerUnit: '',
  currentStock: '0',
  minStockAlert: '0',
  units: [],
});

export default function RecipesBomView({
  products,
  ingredients,
  onCreateProduct,
  onUpdateProduct,
  onDeleteProduct,
  onCreateIngredient,
  onUpdateIngredient,
  onDeleteIngredient,
  canManage = true,
}) {
  const [tab, setTab] = useState('produk');
  const [search, setSearch] = useState('');
  const [showModal, setShowModal] = useState(false);
  const [editId, setEditId] = useState(null); // null = create, number = update
  const [form, setForm] = useState(emptyForm());
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState('');
  // Satu kunci per kali membuka form produk: klik ganda tidak menghasilkan dua produk, tetapi
  // penyimpanan ulang setelah galat validasi tetap boleh memakai isian yang sama.
  const [formKunci, setFormKunci] = useState('');

  const [searchBahan, setSearchBahan] = useState('');
  const [showBahan, setShowBahan] = useState(false);
  const [editBahanId, setEditBahanId] = useState(null);
  const [formBahan, setFormBahan] = useState(emptyBahan());
  const [savingBahan, setSavingBahan] = useState(false);
  const [errorBahan, setErrorBahan] = useState('');
  const [galatHapus, setGalatHapus] = useState('');

  // Filter products by search
  const displayProducts = (products || []).filter((p) => {
    if (!search.trim()) return true;
    const q = search.toLowerCase();
    return (
      (p.name || p.Name || '').toLowerCase().includes(q) ||
      (p.category || p.Category || '').toLowerCase().includes(q) ||
      (p.barcode || p.Barcode || '').toLowerCase().includes(q)
    );
  });

  const displayBahan = (ingredients || []).filter((b) => {
    if (!searchBahan.trim()) return true;
    const q = searchBahan.toLowerCase();
    return (
      (b.name ?? b.Name ?? '').toLowerCase().includes(q) ||
      (b.brand ?? b.Brand ?? '').toLowerCase().includes(q) ||
      (b.unit ?? b.Unit ?? '').toLowerCase().includes(q)
    );
  });

  // ── Modal helpers ─────────────────────────────────────────────────────────────
  const openCreate = () => {
    setEditId(null);
    setForm(emptyForm());
    setFormError('');
    setFormKunci(kunciBaru('produk-baru'));
    setShowModal(true);
  };

  const openEdit = async (product) => {
    const productId = product.id ?? product.Id;
    setFormKunci(kunciBaru(`produk-${productId}`));
    // Daftar produk boleh saja sudah membawa baris resep, tapi hanya detail dari server yang
    // mengirim HPP hasil perhitungan ulang. Kalau fetch gagal, isian baris lama dipakai dan
    // pengguna diberi tahu — menyimpan tanpa sadar dengan daftar kosong akan menghapus resepnya.
    let sumber = product;
    let peringatan = '';
    try {
      sumber = await api.getProduct(productId);
    } catch (err) {
      peringatan = `Detail produk gagal dimuat (${err.message}); perubahan akan menimpa komposisi yang tersimpan.`;
    }
    const lines = (sumber.recipeLines || sumber.RecipeLines || []).map((l) => ({
      ingredientId: String(l.ingredientId ?? l.IngredientId ?? ''),
      quantity: String(l.quantity ?? l.Quantity ?? ''),
      unit: l.unit ?? l.Unit ?? '',
    }));
    setEditId(productId);
    setForm({
      name: sumber.name ?? sumber.Name ?? '',
      category: sumber.category ?? sumber.Category ?? 'Roti',
      barcode: sumber.barcode ?? sumber.Barcode ?? '',
      sellingPrice: String(fromMinor(sumber.sellingPriceMinorUnits ?? sumber.SellingPriceMinorUnits
        ?? sumber.sellingPrice ?? sumber.SellingPrice ?? 0)),
      lines: lines.length > 0 ? lines : [{ ingredientId: '', quantity: '', unit: '' }],
    });
    setFormError(peringatan);
    setShowModal(true);
  };

  const closeModal = () => {
    setShowModal(false);
    setEditId(null);
    setFormError('');
  };

  // ── Line item helpers ─────────────────────────────────────────────────────────
  const addLine = () =>
    setForm((f) => ({ ...f, lines: [...f.lines, { ingredientId: '', quantity: '', unit: '' }] }));

  const removeLine = (idx) =>
    setForm((f) => ({ ...f, lines: f.lines.filter((_, i) => i !== idx) }));

  // Ganti bahan berarti satuan lama belum tentu ada di bahan baru; kembali ke satuan stok.
  const updateLine = (idx, field, value) =>
    setForm((f) => {
      const lines = f.lines.map((l, i) => {
        if (i !== idx) return l;
        return field === 'ingredientId'
          ? { ...l, ingredientId: value, unit: '' }
          : { ...l, [field]: value };
      });
      return { ...f, lines };
    });

  // Satuan yang boleh dipilih untuk satu bahan: satuan stok + satuan turunannya.
  const daftarSatuan = (ingredientId) => {
    const b = (ingredients || []).find(
      (i) => String(i.id ?? i.Id ?? '') === String(ingredientId)
    );
    if (!b) return [];
    const dasar = b.unit ?? b.Unit ?? '';
    const hasil = dasar ? [{ nama: dasar, faktor: 1 }] : [];
    for (const u of b.additionalUnits ?? b.AdditionalUnits ?? []) {
      const nama = u.unit ?? u.Unit ?? '';
      if (!nama || hasil.some((h) => h.nama.toLowerCase() === nama.toLowerCase())) continue;
      hasil.push({ nama, faktor: Number(u.factorToBase ?? u.FactorToBase ?? 0) });
    }
    return hasil;
  };

  // Teks konversi + HPP baris, supaya "250 g" terlihat sebagai 0,25 kg sebelum disimpan.
  const infoBaris = (line) => {
    const b = (ingredients || []).find(
      (i) => String(i.id ?? i.Id ?? '') === String(line.ingredientId)
    );
    if (!b) return null;
    const dasar = b.unit ?? b.Unit ?? '';
    const pilihan = daftarSatuan(line.ingredientId);
    const dipilih = line.unit
      ? pilihan.find((s) => s.nama.toLowerCase() === line.unit.trim().toLowerCase())
      : pilihan[0];
    const qty = parseFloat(line.quantity);
    if (!dipilih || !(qty > 0)) return null;
    const jumlahDasar = qty * dipilih.faktor;
    const harga = b.pricePerUnit ?? b.PricePerUnit ?? 0;
    return {
      konversi: dipilih.faktor === 1
        ? `${formatQty(qty)} ${dipilih.nama}`
        : `${formatQty(qty)} ${dipilih.nama} = ${formatQty(jumlahDasar)} ${dasar}`,
      hpp: Math.round(jumlahDasar * harga),
    };
  };

  // ── Submit ─────────────────────────────────────────────────────────────────────
  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!form.name.trim()) { setFormError('Nama produk wajib diisi.'); return; }
    const sellingParsed = parseFloat(form.sellingPrice);
    if (isNaN(sellingParsed) || sellingParsed < 0) { setFormError('Harga jual tidak valid.'); return; }

    const payload = {
      name: form.name.trim(),
      category: form.category,
      barcode: form.barcode.trim() || null,
      sellingPriceMinorUnits: Math.round(sellingParsed * 100),
      lines: form.lines
        .filter((l) => l.ingredientId && parseFloat(l.quantity) > 0)
        .map((l) => ({
          ingredientId: parseInt(l.ingredientId, 10),
          quantity: parseFloat(l.quantity),
          unit: l.unit?.trim() || null,
        })),
      idempotensi: formKunci,
    };

    if (saving) return;
    setSaving(true);
    setFormError('');
    try {
      if (editId !== null) {
        await onUpdateProduct(editId, payload);
      } else {
        await onCreateProduct(payload);
      }
      closeModal();
    } catch (err) {
      setFormError(err.message || 'Gagal menyimpan produk.');
    } finally {
      setSaving(false);
    }
  };

  // ── Delete (soft) ──────────────────────────────────────────────────────────────
  const handleDelete = async (product) => {
    const name = product.name ?? product.Name ?? 'produk ini';
    if (!window.confirm(`Nonaktifkan "${name}"? Produk tidak akan muncul di daftar aktif.`)) return;
    setGalatHapus('');
    try {
      await onDeleteProduct(product.id ?? product.Id);
    } catch (err) {
      // Server menolak kalau stok produk jadi masih ada; pesan penolakannya harus sampai
      // ke pengguna, bukan hilang sebagai error yang tidak tertangani.
      setGalatHapus(err.message || 'Produk tidak bisa dinonaktifkan.');
    }
  };

  // ── Bahan baku ─────────────────────────────────────────────────────────────────
  const openBahanBaru = () => {
    setEditBahanId(null);
    setFormBahan(emptyBahan());
    setErrorBahan('');
    setShowBahan(true);
  };

  const openBahanUbah = (bahan) => {
    setEditBahanId(bahan.id ?? bahan.Id);
    setFormBahan({
      name: bahan.name ?? bahan.Name ?? '',
      brand: bahan.brand ?? bahan.Brand ?? '',
      unit: bahan.unit ?? bahan.Unit ?? '',
      pricePerUnit: String(fromMinor(bahan.pricePerUnit ?? bahan.PricePerUnit ?? 0)),
      // Stok yang tampil adalah total semua lokasi; mengubahnya mencatat penyesuaian di gudang.
      currentStock: String(bahan.currentStock ?? bahan.CurrentStock ?? 0),
      minStockAlert: String(bahan.minStockAlert ?? bahan.MinStockAlert ?? 0),
      units: (bahan.additionalUnits ?? bahan.AdditionalUnits ?? []).map((u) => ({
        unit: u.unit ?? u.Unit ?? '',
        factor: String(u.factorToBase ?? u.FactorToBase ?? ''),
      })),
    });
    setErrorBahan('');
    setShowBahan(true);
  };

  const closeBahan = () => {
    setShowBahan(false);
    setEditBahanId(null);
    setErrorBahan('');
  };

  const handleSimpanBahan = async (e) => {
    e.preventDefault();
    if (!formBahan.name.trim()) { setErrorBahan('Nama bahan baku wajib diisi.'); return; }
    if (!formBahan.unit.trim()) { setErrorBahan('Satuan bahan baku wajib diisi (mis. kg, liter, pcs).'); return; }
    const harga = parseFloat(formBahan.pricePerUnit);
    if (isNaN(harga) || harga < 0) { setErrorBahan('Harga per satuan tidak valid.'); return; }
    const stok = parseFloat(formBahan.currentStock);
    if (isNaN(stok) || stok < 0) { setErrorBahan('Stok tidak valid.'); return; }
    const minimum = parseFloat(formBahan.minStockAlert);
    if (isNaN(minimum) || minimum < 0) { setErrorBahan('Batas minimum stok tidak valid.'); return; }

    // Satuan tambahan: "1 g = 0,001 kg". Faktor harus positif dan namanya unik per bahan.
    const satuanDasar = formBahan.unit.trim().toLowerCase();
    const namaSatuan = new Set();
    const units = [];
    for (const u of formBahan.units) {
      const nama = (u.unit || '').trim();
      const faktor = parseFloat(u.factor);
      if (!nama && !String(u.factor ?? '').trim()) continue; // baris kosong diabaikan
      if (!nama) { setErrorBahan('Nama satuan tambahan wajib diisi.'); return; }
      if (nama.toLowerCase() === satuanDasar) {
        setErrorBahan(`Satuan tambahan "${nama}" sama dengan satuan stok bahan.`);
        return;
      }
      if (namaSatuan.has(nama.toLowerCase())) {
        setErrorBahan(`Satuan tambahan "${nama}" ditulis lebih dari sekali.`);
        return;
      }
      if (isNaN(faktor) || faktor <= 0) {
        setErrorBahan(`Faktor satuan "${nama}" harus lebih dari nol.`);
        return;
      }
      namaSatuan.add(nama.toLowerCase());
      units.push({ unit: nama, factorToBase: faktor });
    }

    const payload = {
      name: formBahan.name.trim(),
      brand: formBahan.brand.trim() || null,
      unit: formBahan.unit.trim(),
      pricePerUnit: toMinor(harga),
      currentStock: stok,
      minStockAlert: minimum,
      units,
    };

    if (savingBahan) return;
    setSavingBahan(true);
    setErrorBahan('');
    try {
      if (editBahanId !== null) {
        await onUpdateIngredient(editBahanId, payload);
      } else {
        await onCreateIngredient(payload);
      }
      closeBahan();
    } catch (err) {
      setErrorBahan(err.message || 'Gagal menyimpan bahan baku.');
    } finally {
      setSavingBahan(false);
    }
  };

  const addSatuan = () =>
    setFormBahan((f) => ({ ...f, units: [...f.units, { unit: '', factor: '' }] }));

  const removeSatuan = (idx) =>
    setFormBahan((f) => ({ ...f, units: f.units.filter((_, i) => i !== idx) }));

  const updateSatuan = (idx, field, value) =>
    setFormBahan((f) => ({
      ...f,
      units: f.units.map((u, i) => (i === idx ? { ...u, [field]: value } : u)),
    }));

  const handleHapusBahan = async (bahan) => {
    const nama = bahan.name ?? bahan.Name ?? 'bahan ini';
    if (!window.confirm(`Nonaktifkan "${nama}"? Bahan tidak akan muncul di pilihan resep dan pembelian baru.`)) return;
    setGalatHapus('');
    try {
      await onDeleteIngredient(bahan.id ?? bahan.Id);
    } catch (err) {
      setGalatHapus(err.message || 'Bahan tidak bisa dinonaktifkan.');
    }
  };

  return (
    <div className="space-y-6">
      {/* Header Banner */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 bg-gradient-to-r from-slate-900 to-slate-900/60 p-6 rounded-2xl border border-slate-800 shadow-xl">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-2xl font-bold text-white tracking-tight">Resep &amp; BOM Produk</h1>
            <span className="px-2.5 py-0.5 text-xs font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/20 rounded-full">
              Bill of Materials
            </span>
          </div>
          <p className="text-sm text-slate-400 mt-1">
            Kelola daftar produk beserta komposisi bahan baku, HPP, dan harga jual.
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          {canManage ? (
            <button
              onClick={tab === 'produk' ? openCreate : openBahanBaru}
              className="flex items-center gap-2 px-4 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all"
            >
              <Plus className="w-4 h-4" />
              <span>{tab === 'produk' ? 'Tambah Produk' : 'Tambah Bahan'}</span>
            </button>
          ) : (
            <span className="text-[11px] text-slate-400 max-w-[220px] text-right">
              Peran Anda dapat melihat resep, HPP, dan bahan baku, tetapi tidak dapat mengubahnya.
            </span>
          )}
        </div>
      </div>

      {galatHapus && (
        <div className="bg-rose-950/60 border border-rose-800 text-rose-200 text-sm rounded-xl px-4 py-3">
          {galatHapus}
        </div>
      )}

      {/* Tab produk/resep dan bahan baku: keduanya data induk yang saling menunjuk. */}
      <div className="flex items-center gap-1.5 bg-slate-900/80 border border-slate-800 p-1.5 rounded-xl w-fit">
        {[
          { id: 'produk', label: 'Produk & Resep', icon: ChefHat },
          { id: 'bahan', label: 'Bahan Baku', icon: Package },
        ].map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            onClick={() => setTab(id)}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg text-xs font-semibold transition-colors ${
              tab === id ? 'bg-amber-500 text-slate-950' : 'text-slate-400 hover:text-white hover:bg-slate-800'
            }`}
          >
            <Icon className="w-3.5 h-3.5" />
            {label}
          </button>
        ))}
      </div>

      {tab === 'produk' && (
      /* Products Table */
      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <div className="relative">
            <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              placeholder="Cari nama produk atau kategori..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9 pr-3 py-1.5 bg-slate-950 border border-slate-800 rounded-lg text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-amber-500/50 w-72"
            />
          </div>
          <div className="text-xs text-slate-400">
            Total: <strong className="text-white">{displayProducts.length} Produk</strong>
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
              <tr>
                <th className="py-3 px-4 font-semibold">Nama Produk</th>
                <th className="py-3 px-4 font-semibold">Kategori</th>
                <th className="py-3 px-4 font-semibold text-right">HPP (Rp)</th>
                <th className="py-3 px-4 font-semibold text-right">Harga Jual (Rp)</th>
                <th className="py-3 px-4 font-semibold text-right">Margin%</th>
                <th className="py-3 px-4 font-semibold text-center">Aksi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60">
              {displayProducts.length === 0 ? (
                <tr>
                  <td colSpan={6} className="py-12 text-center text-slate-500">
                    {search ? 'Tidak ada produk yang cocok dengan pencarian.' : canManage ? 'Belum ada produk. Klik "Tambah Produk" untuk memulai.' : 'Belum ada produk yang terdaftar.'}
                  </td>
                </tr>
              ) : (
                displayProducts.map((p) => {
                  const pId = p.id ?? p.Id;
                  const name = p.name ?? p.Name ?? '-';
                  const category = p.category ?? p.Category ?? '-';
                  const sellingMinor = p.sellingPrice ?? p.SellingPrice ?? p.sellingPriceMinorUnits ?? p.SellingPriceMinorUnits ?? 0;
                  const hppMinor = calcHpp(p);
                  const lines = p.recipeLines ?? p.RecipeLines ?? [];
                  return (
                    <tr key={pId} className="hover:bg-slate-800/40 transition-colors">
                      <td className="py-3 px-4 font-semibold text-white">
                        {name}
                        {lines.length > 0 && (
                          <span className="ml-2 text-[10px] text-slate-500">{lines.length} bahan</span>
                        )}
                      </td>
                      <td className="py-3 px-4">
                        <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-slate-800 text-slate-300 border border-slate-700">
                          {category}
                        </span>
                      </td>
                      <td className="py-3 px-4 text-right font-mono text-slate-300">
                        {formatMinor(hppMinor)}
                      </td>
                      <td className="py-3 px-4 text-right font-mono text-amber-400 font-semibold">
                        {formatMinor(sellingMinor)}
                      </td>
                      <td className="py-3 px-4 text-right">
                        <span className={`font-semibold ${
                          sellingMinor > hppMinor ? 'text-emerald-400' : 'text-rose-400'
                        }`}>
                          {calcMargin(sellingMinor, hppMinor)}
                        </span>
                      </td>
                      <td className="py-3 px-4 text-center">
                        <div className="flex items-center justify-center gap-1.5">
                          {canManage ? (
                            <>
                              <button
                                onClick={() => openEdit(p)}
                                className="p-1.5 text-slate-400 hover:text-amber-400 hover:bg-amber-500/10 rounded-lg transition-colors"
                                title="Edit produk"
                              >
                                <Edit2 className="w-3.5 h-3.5" />
                              </button>
                              <button
                                onClick={() => handleDelete(p)}
                                className="p-1.5 text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-colors"
                                title="Nonaktifkan produk"
                              >
                                <Trash2 className="w-3.5 h-3.5" />
                              </button>
                            </>
                          ) : (
                            <span className="text-[10px] text-slate-500 italic">Hanya lihat</span>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>
      )}

      {tab === 'bahan' && (
      /* Bahan Baku Table */
      <div className="bg-slate-900/80 border border-slate-800 rounded-2xl overflow-hidden shadow-xl">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <div className="relative">
            <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              placeholder="Cari nama, merek, atau satuan..."
              value={searchBahan}
              onChange={(e) => setSearchBahan(e.target.value)}
              className="pl-9 pr-3 py-1.5 bg-slate-950 border border-slate-800 rounded-lg text-xs text-slate-200 placeholder-slate-500 focus:outline-none focus:border-amber-500/50 w-72"
            />
          </div>
          <div className="text-xs text-slate-400">
            Total: <strong className="text-white">{displayBahan.length} Bahan</strong>
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead className="bg-slate-950/60 uppercase text-[10px] tracking-wider text-slate-400 border-b border-slate-800">
              <tr>
                <th className="py-3 px-4 font-semibold">Nama Bahan</th>
                <th className="py-3 px-4 font-semibold">Satuan</th>
                <th className="py-3 px-4 font-semibold text-right">Harga / Satuan</th>
                <th className="py-3 px-4 font-semibold text-right">Stok</th>
                <th className="py-3 px-4 font-semibold text-right">Min. Stok</th>
                <th className="py-3 px-4 font-semibold text-center">Aksi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800/60">
              {displayBahan.length === 0 ? (
                <tr>
                  <td colSpan={6} className="py-12 text-center text-slate-500">
                    {searchBahan
                      ? 'Tidak ada bahan yang cocok dengan pencarian.'
                      : canManage
                        ? 'Belum ada bahan baku. Klik "Tambah Bahan" untuk memulai.'
                        : 'Belum ada bahan baku yang terdaftar.'}
                  </td>
                </tr>
              ) : (
                displayBahan.map((b) => {
                  const bId = b.id ?? b.Id;
                  const nama = b.name ?? b.Name ?? '-';
                  const merek = b.brand ?? b.Brand ?? '';
                  const satuan = b.unit ?? b.Unit ?? '';
                  const harga = b.pricePerUnit ?? b.PricePerUnit ?? 0;
                  const stok = b.currentStock ?? b.CurrentStock ?? 0;
                  const minimum = b.minStockAlert ?? b.MinStockAlert ?? 0;
                  const menipis = Number(minimum) > 0 && Number(stok) <= Number(minimum);
                  return (
                    <tr key={bId} className="hover:bg-slate-800/40 transition-colors">
                      <td className="py-3 px-4 font-semibold text-white">
                        {nama}
                        {merek && <span className="ml-2 text-[10px] font-normal text-slate-500">{merek}</span>}
                      </td>
                      <td className="py-3 px-4">
                        <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-slate-800 text-slate-300 border border-slate-700">
                          {satuan || '-'}
                        </span>
                      </td>
                      <td className="py-3 px-4 text-right font-mono text-amber-400">{formatMinor(harga)}</td>
                      <td className="py-3 px-4 text-right font-mono">
                        <span className={menipis ? 'text-amber-400 font-semibold' : 'text-slate-300'}>
                          {formatQty(stok)} {satuan}
                        </span>
                        {menipis && <span className="ml-1.5 text-[10px] text-amber-400">menipis</span>}
                      </td>
                      <td className="py-3 px-4 text-right font-mono text-slate-400">{formatQty(minimum)}</td>
                      <td className="py-3 px-4 text-center">
                        {canManage ? (
                          <div className="flex items-center justify-center gap-1.5">
                            <button
                              onClick={() => openBahanUbah(b)}
                              className="p-1.5 text-slate-400 hover:text-amber-400 hover:bg-amber-500/10 rounded-lg transition-colors"
                              title="Edit bahan"
                            >
                              <Edit2 className="w-3.5 h-3.5" />
                            </button>
                            <button
                              onClick={() => handleHapusBahan(b)}
                              className="p-1.5 text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-colors"
                              title="Nonaktifkan bahan"
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                            </button>
                          </div>
                        ) : (
                          <span className="text-[10px] text-slate-500 italic">Hanya lihat</span>
                        )}
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
        <div className="px-4 py-3 border-t border-slate-800 text-[11px] text-slate-500">
          Stok di sini adalah total semua lokasi. Mengubahnya mencatat penyesuaian di gudang utama;
          jumlah per lokasi diatur lewat menu Inventaris.
        </div>
      </div>
      )}

      {/* Modal Form */}
      {showModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-lg shadow-2xl overflow-hidden flex flex-col max-h-[90vh]">
            {/* Modal Header */}
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between shrink-0">
              <div>
                <h3 className="text-base font-bold text-white flex items-center gap-2">
                  <ChefHat className="w-4 h-4 text-amber-400" />
                  {editId !== null ? 'Edit Produk' : 'Tambah Produk Baru'}
                </h3>
                <p className="text-xs text-slate-400 mt-0.5">Isi detail produk dan komposisi bahan baku.</p>
              </div>
              <button
                onClick={closeModal}
                className="p-2 text-slate-400 hover:text-white hover:bg-slate-800 rounded-lg transition-colors"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Modal Body */}
            <form id="product-form" onSubmit={handleSubmit} className="flex-1 overflow-y-auto p-5 space-y-4">
              {formError && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
                  {formError}
                </div>
              )}

              {/* Nama Produk */}
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Nama Produk <span className="text-rose-400">*</span>
                </label>
                <input
                  type="text"
                  value={form.name}
                  onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder="cth. Roti Tawar Susu"
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                />
              </div>

              {/* Kategori */}
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">Kategori</label>
                <select
                  value={form.category}
                  onChange={(e) => setForm({ ...form, category: e.target.value })}
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                >
                  {CATEGORIES.map((c) => (
                    <option key={c} value={c}>{c}</option>
                  ))}
                </select>
              </div>

              {/* Barcode */}
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Barcode Produk
                </label>
                <input
                  type="text"
                  value={form.barcode}
                  onChange={(e) => setForm({ ...form, barcode: e.target.value })}
                  placeholder="Scan atau ketik kode yang tertempel di kemasan"
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                />
                <p className="text-[10px] text-slate-500 mt-1">
                  Dipakai kotak pindai di layar kasir. Satu barcode hanya boleh dipakai satu produk.
                </p>
              </div>

              {/* Harga Jual */}
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Harga Jual (Rp) <span className="text-rose-400">*</span>
                </label>
                <input
                  type="number"
                  min="0"
                  step="any"
                  value={form.sellingPrice}
                  onChange={(e) => setForm({ ...form, sellingPrice: e.target.value })}
                  placeholder="cth. 15000"
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                />
                <p className="text-[10px] text-slate-500 mt-1">Masukkan nilai dalam Rupiah penuh (bukan sen).</p>
              </div>

              {/* Komposisi Bahan Baku */}
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label className="text-xs font-semibold text-slate-400">Komposisi Bahan Baku (BOM)</label>
                  <button
                    type="button"
                    onClick={addLine}
                    className="flex items-center gap-1 px-2.5 py-1 text-xs bg-slate-800 hover:bg-slate-700 text-amber-400 rounded-lg border border-slate-700 transition-colors"
                  >
                    <Plus className="w-3 h-3" />
                    Tambah Bahan
                  </button>
                </div>

                <div className="space-y-2">
                  {form.lines.map((line, idx) => {
                    const satuan = daftarSatuan(line.ingredientId);
                    const info = infoBaris(line);
                    return (
                      <div key={idx} className="rounded-xl border border-slate-800 bg-slate-950/40 p-2 space-y-1">
                        <div className="flex items-center gap-2">
                          <select
                            value={line.ingredientId}
                            onChange={(e) => updateLine(idx, 'ingredientId', e.target.value)}
                            className="flex-1 min-w-0 px-3 py-2 bg-slate-950 border border-slate-800 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                          >
                            <option value="">— Pilih Bahan —</option>
                            {(ingredients || []).map((ing) => {
                              const ingId = ing.id ?? ing.Id;
                              const ingName = ing.name ?? ing.Name ?? '-';
                              const ingUnit = ing.unit ?? ing.Unit ?? '';
                              return (
                                <option key={ingId} value={ingId}>
                                  {ingName} {ingUnit ? `(${ingUnit})` : ''}
                                </option>
                              );
                            })}
                          </select>
                          <select
                            value={line.unit || satuan[0]?.nama || ''}
                            onChange={(e) => updateLine(idx, 'unit', e.target.value)}
                            disabled={satuan.length === 0}
                            title="Satuan jumlah bahan pada baris ini"
                            className="w-20 px-2 py-2 bg-slate-950 border border-slate-800 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors disabled:opacity-40"
                          >
                            {satuan.map((s) => (
                              <option key={s.nama} value={s.nama}>{s.nama}</option>
                            ))}
                          </select>
                          <input
                            type="number"
                            min="0"
                            step="any"
                            value={line.quantity}
                            onChange={(e) => updateLine(idx, 'quantity', e.target.value)}
                            placeholder="Qty"
                            className="w-20 px-3 py-2 bg-slate-950 border border-slate-800 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                          />
                          <button
                            type="button"
                            onClick={() => removeLine(idx)}
                            disabled={form.lines.length === 1}
                            className="p-2 text-slate-500 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
                          >
                            <X className="w-3.5 h-3.5" />
                          </button>
                        </div>
                        {info && (
                          <div className="flex items-center justify-between px-1 text-[10px] text-slate-500">
                            <span>{info.konversi}</span>
                            <span className="font-mono">HPP baris {formatMinor(info.hpp)}</span>
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
                {ingredients && ingredients.length === 0 && (
                  <p className="text-[10px] text-amber-400 mt-1">
                    Belum ada bahan baku. Tutup formulir ini, lalu tambahkan lewat tab Bahan Baku di layar yang sama.
                  </p>
                )}
              </div>
            </form>

            {/* Modal Footer */}
            <div className="p-5 border-t border-slate-800 shrink-0 flex justify-end gap-2.5">
              <button
                type="button"
                onClick={closeModal}
                className="px-4 py-2.5 bg-slate-800 hover:bg-slate-700 text-slate-300 font-semibold rounded-xl text-sm transition-colors"
              >
                Batal
              </button>
              <button
                type="submit"
                form="product-form"
                disabled={saving}
                className="px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all disabled:opacity-60"
              >
                {saving ? 'Menyimpan...' : editId !== null ? 'Simpan Perubahan' : 'Tambah Produk'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Modal Bahan Baku */}
      {showBahan && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm">
          <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-md shadow-2xl overflow-hidden flex flex-col max-h-[90vh]">
            <div className="p-5 border-b border-slate-800 bg-slate-950/50 flex items-center justify-between shrink-0">
              <div>
                <h3 className="text-base font-bold text-white flex items-center gap-2">
                  <Package className="w-4 h-4 text-amber-400" />
                  {editBahanId !== null ? 'Edit Bahan Baku' : 'Tambah Bahan Baku'}
                </h3>
                <p className="text-xs text-slate-400 mt-0.5">
                  Bahan ini dipakai resep, pembelian, dan stok opname.
                </p>
              </div>
              <button
                onClick={closeBahan}
                className="p-2 text-slate-400 hover:text-white hover:bg-slate-800 rounded-lg transition-colors"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <form id="bahan-form" onSubmit={handleSimpanBahan} className="flex-1 overflow-y-auto p-5 space-y-4">
              {errorBahan && (
                <div className="p-3 bg-rose-500/10 border border-rose-500/20 rounded-xl text-rose-400 text-xs">
                  {errorBahan}
                </div>
              )}

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Nama Bahan <span className="text-rose-400">*</span>
                </label>
                <input
                  type="text"
                  value={formBahan.name}
                  onChange={(e) => setFormBahan({ ...formBahan, name: e.target.value })}
                  placeholder="cth. Tepung Terigu Cakra"
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                    Satuan <span className="text-rose-400">*</span>
                  </label>
                  <input
                    type="text"
                    value={formBahan.unit}
                    onChange={(e) => setFormBahan({ ...formBahan, unit: e.target.value })}
                    placeholder="cth. kg"
                    className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                    required
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Merek</label>
                  <input
                    type="text"
                    value={formBahan.brand}
                    onChange={(e) => setFormBahan({ ...formBahan, brand: e.target.value })}
                    placeholder="cth. Cakra Kembar"
                    className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  />
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                  Harga per Satuan (Rp) <span className="text-rose-400">*</span>
                </label>
                <input
                  type="number"
                  min="0"
                  step="any"
                  value={formBahan.pricePerUnit}
                  onChange={(e) => setFormBahan({ ...formBahan, pricePerUnit: e.target.value })}
                  placeholder="cth. 13000"
                  className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  required
                />
                <p className="text-[10px] text-slate-500 mt-1">
                  Rupiah penuh per satu satuan. Nilai ini menjadi dasar HPP setiap produk yang memakainya.
                </p>
              </div>

              {/* Satuan tambahan: resep boleh menulis 250 g walau stok bahan dihitung per kg. */}
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label className="text-xs font-semibold text-slate-400">Satuan tambahan</label>
                  <button
                    type="button"
                    onClick={addSatuan}
                    className="flex items-center gap-1 px-2.5 py-1 text-xs bg-slate-800 hover:bg-slate-700 text-amber-400 rounded-lg border border-slate-700 transition-colors"
                  >
                    <Plus className="w-3 h-3" />
                    Tambah satuan
                  </button>
                </div>
                <p className="text-[10px] text-slate-500 mb-2">
                  Faktor = berapa {formBahan.unit.trim() || 'satuan stok'} dalam 1 satuan ini.
                  Contoh: 1 g = 0,001 kg, 1 bks = 0,5 kg.
                </p>
                {formBahan.units.length === 0 ? (
                  <p className="text-[10px] text-slate-500">
                    Belum ada. Resep tetap bisa memakai satuan stok {formBahan.unit.trim() || 'bahan ini'}.
                  </p>
                ) : (
                  <div className="space-y-2">
                    {formBahan.units.map((u, idx) => (
                      <div key={idx} className="flex items-center gap-2">
                        <input
                          type="text"
                          value={u.unit}
                          onChange={(e) => updateSatuan(idx, 'unit', e.target.value)}
                          placeholder="cth. g"
                          className="flex-1 min-w-0 px-3 py-2 bg-slate-950 border border-slate-800 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                        />
                        <span className="text-[10px] text-slate-500 whitespace-nowrap">1 {u.unit.trim() || 'satuan'} =</span>
                        <input
                          type="number"
                          min="0"
                          step="any"
                          value={u.factor}
                          onChange={(e) => updateSatuan(idx, 'factor', e.target.value)}
                          placeholder="0,001"
                          className="w-24 px-3 py-2 bg-slate-950 border border-slate-800 rounded-lg text-xs text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                        />
                        <span className="text-[10px] text-slate-400 w-10 truncate">
                          {formBahan.unit.trim() || 'stok'}
                        </span>
                        <button
                          type="button"
                          onClick={() => removeSatuan(idx)}
                          className="p-2 text-slate-500 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-colors"
                          title="Hapus satuan"
                        >
                          <X className="w-3.5 h-3.5" />
                        </button>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">
                    {editBahanId !== null ? 'Stok Saat Ini' : 'Stok Awal'}
                  </label>
                  <input
                    type="number"
                    min="0"
                    step="any"
                    value={formBahan.currentStock}
                    onChange={(e) => setFormBahan({ ...formBahan, currentStock: e.target.value })}
                    className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  />
                </div>
                <div>
                  <label className="text-xs font-semibold text-slate-400 block mb-1.5">Min. Stok</label>
                  <input
                    type="number"
                    min="0"
                    step="any"
                    value={formBahan.minStockAlert}
                    onChange={(e) => setFormBahan({ ...formBahan, minStockAlert: e.target.value })}
                    className="w-full px-3 py-2.5 bg-slate-950 border border-slate-800 rounded-xl text-sm text-white focus:outline-none focus:border-amber-500/60 transition-colors"
                  />
                </div>
              </div>
              <p className="text-[10px] text-slate-500">
                Stok adalah total semua lokasi, jadi angka ini tidak dipakai untuk memindahkan stok.
                Min. stok memunculkan pengingat Bahan Baku Kritis di dasbor.
              </p>
            </form>

            <div className="p-5 border-t border-slate-800 shrink-0 flex justify-end gap-2.5">
              <button
                type="button"
                onClick={closeBahan}
                className="px-4 py-2.5 bg-slate-800 hover:bg-slate-700 text-slate-300 font-semibold rounded-xl text-sm transition-colors"
              >
                Batal
              </button>
              <button
                type="submit"
                form="bahan-form"
                disabled={savingBahan}
                className="px-5 py-2.5 bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-bold rounded-xl text-sm shadow-lg shadow-amber-500/20 transition-all disabled:opacity-60"
              >
                {savingBahan ? 'Menyimpan...' : editBahanId !== null ? 'Simpan Perubahan' : 'Tambah Bahan'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
