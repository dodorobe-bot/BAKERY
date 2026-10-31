import { 
  LayoutDashboard, 
  CheckSquare, 
  Bell, 
  ShoppingBag, 
  CalendarClock, 
  Users, 
  Receipt, 
  ClipboardList, 
  ChefHat, 
  BookOpen, 
  FlaskConical, 
  ShieldCheck, 
  Calculator, 
  Truck, 
  Contact, 
  Warehouse, 
  ArrowLeftRight, 
  Boxes, 
  ClipboardCheck, 
  QrCode, 
  BookMarked, 
  Landmark, 
  FileSpreadsheet, 
  BarChart3, 
  FileText, 
  Settings,
  ShieldAlert,
  KeyRound,
  Store,
  RotateCcw,
  Printer
} from 'lucide-react';

// `requires` adalah nama kebijakan sisi server yang harus dimiliki peran pengguna supaya menu
// ini bisa membuka isinya. Menu tanpa `requires` boleh dibaca semua peran. Disaring di Sidebar
// dari daftar kapabilitas yang dikirim saat login, jadi tidak ada menu yang ujungnya 403.
// Badge hanya boleh berupa hitungan nyata; angka tetap seperti "3" adalah hiasan dan dihapus.
export const ENTERPRISE_NAV = [
  {
    id: 'workspace',
    label: 'RUANG KERJA',
    description: 'Pusat kendali harian & aktivitas',
    items: [
      { id: 'dashboard', label: 'Dasbor & Indikator Utama', icon: LayoutDashboard, requires: 'finance-read' },
      { id: 'tasks', label: 'Kotak Tugas & Persetujuan', icon: CheckSquare },
      { id: 'notifications', label: 'Pemberitahuan & Catatan Aktivitas', icon: Bell, requires: 'finance-read' }
    ]
  },
  {
    id: 'sales',
    label: 'PENJUALAN & PELANGGAN',
    description: 'Dari kasir hingga pelanggan',
    items: [
      { id: 'pos', label: 'Kasir Penjualan Cepat', icon: Store, requires: 'sales' },
      { id: 'orders', label: 'Daftar Pesanan & Penjualan', icon: ShoppingBag, requires: 'sales' },
      { id: 'preorders', label: 'Pesanan Terjadwal (Pre-Order)', icon: CalendarClock, requires: 'sales' },
      { id: 'customers', label: 'Data Pelanggan & Klien', icon: Users, requires: 'sales' },
      { id: 'receivables', label: 'Piutang & Pembayaran', icon: Receipt, requires: 'sales' },
      { id: 'sales_returns', label: 'Retur Penjualan', icon: RotateCcw, requires: 'sales-return' }
    ]
  },
  {
    id: 'production',
    label: 'OPERASIONAL & PRODUKSI',
    description: 'Perencanaan hingga produk siap jual',
    items: [
      { id: 'production_plan', label: 'Perencanaan Jadwal Dapur', icon: ClipboardList, requires: 'production-write' },
      { id: 'production_orders', label: 'Perintah Kerja (SPK)', icon: ChefHat, requires: 'production-write' },
      { id: 'recipes_bom', label: 'Resep & Daftar Bahan Baku', icon: BookOpen, requires: 'inventory-read' },
      { id: 'production_actual', label: 'Pemakaian Aktual & Hasil', icon: FlaskConical, requires: 'production-write' },
      { id: 'quality_control', label: 'Pemeriksaan Mutu (QC)', icon: ShieldCheck, requires: 'production-write' },
      { id: 'cost_calc', label: 'Harga Pokok Produksi (HPP)', icon: Calculator, requires: 'production-write' }
    ]
  },
  {
    id: 'supply_chain',
    label: 'RANTAI PASOK & PERSEDIAAN',
    description: 'Pengadaan, mutasi, dan gudang',
    items: [
      { id: 'purchases', label: 'Pembelian & Penerimaan Barang', icon: Truck, requires: 'purchases-receive' },
      { id: 'suppliers', label: 'Pemasok & Rekanan', icon: Contact, requires: 'purchases-receive' },
      { id: 'inventory_locations', label: 'Gudang & Lokasi Rak', icon: Warehouse, requires: 'inventory-read' },
      { id: 'kitchen_transfer', label: 'Mutasi Gudang ke Dapur', icon: ArrowLeftRight, requires: 'inventory-read' },
      { id: 'stock_catalog', label: 'Katalog Bahan & Produk Jadi', icon: Boxes, requires: 'inventory-read' },
      { id: 'stock_opname', label: 'Pemeriksaan Fisik (Stok Opname)', icon: ClipboardCheck, requires: 'inventory-read' },
      { id: 'batch_expiry', label: 'Nomor Batch & Kedaluwarsa', icon: QrCode, requires: 'inventory-read' }
    ]
  },
  {
    id: 'finance',
    label: 'KEUANGAN & AKUNTANSI',
    description: 'Pencatatan keuangan terpadu',
    items: [
      { id: 'general_ledger', label: 'Buku Besar & Jurnal Umum', icon: BookMarked, requires: 'finance-read' },
      { id: 'cash_bank', label: 'Kas Toko & Rekening Bank', icon: Landmark, requires: 'finance-read' },
      { id: 'payables_receivables', label: 'Utang & Piutang Usaha', icon: Receipt, requires: 'finance-read' },
      { id: 'financial_reports', label: 'Laporan Keuangan & Laba Rugi', icon: FileSpreadsheet, requires: 'finance-read' }
    ]
  },
  {
    id: 'analytics_settings',
    label: 'ANALISIS & PENGATURAN',
    description: 'Pengawasan, hak akses, dan sistem',
    items: [
      { id: 'executive_analytics', label: 'Analisis & Tren Penjualan', icon: BarChart3, requires: 'finance-read' },
      { id: 'audit_log', label: 'Jejak Audit & Riwayat Sistem', icon: FileText, requires: 'administration' },
      { id: 'user_management', label: 'Hak Akses & Pegawai', icon: KeyRound, requires: 'administration' },
      { id: 'device_settings', label: 'Perangkat Kasir', icon: Printer, requires: 'administration' },
      { id: 'settings', label: 'Pengaturan Toko & Cabang', icon: Settings, requires: 'administration' }
    ]
  }
];
