import React, { useState } from 'react';
import { Search, ChevronDown, ChevronRight, LogOut, Shield } from 'lucide-react';
import { ENTERPRISE_NAV } from '../config/navigation';
import { bolehAksi } from '../utils/kapabilitas';
import { roleName } from '../utils/enums';
import BrandMark from './BrandMark';

export default function Sidebar({ activeTab, onSelectTab, user, brand, onLogout }) {
  const [searchQuery, setSearchQuery] = useState('');
  const [collapsedSections, setCollapsedSections] = useState({});

  const toggleSection = (sectionId) => {
    setCollapsedSections(prev => ({
      ...prev,
      [sectionId]: !prev[sectionId]
    }));
  };

  // Peran disimpan sebagai nama ("Owner"), bukan angka; roleName menerima keduanya supaya
  // sesi lama dan baru tidak pernah jatuh ke label keliru.
  const roleLabels = { Owner: 'Pemilik Toko', Admin: 'Manajer Operasional', Dapur: 'Kepala Dapur', Pegawai: 'Staf Toko / Kasir' };

  // Menu disaring dua kali: dulu oleh kapabilitas peran (daftar dari server saat login), baru
  // oleh kata kunci pencarian. Tanpa penyaringan pertama, pegawai melihat menu yang isinya
  // ditolak server dan setiap klik terasa seperti aplikasi rusak.
  const bolehDibuka = (item) => bolehAksi(user, item.requires);

  const filteredNav = ENTERPRISE_NAV.map(group => {
    const matchingItems = group.items.filter(item =>
      bolehDibuka(item) && (
        item.label.toLowerCase().includes(searchQuery.toLowerCase()) ||
        group.label.toLowerCase().includes(searchQuery.toLowerCase())
      )
    );
    return {
      ...group,
      items: matchingItems
    };
  }).filter(group => group.items.length > 0);

  return (
    <aside className="w-72 bg-slate-950/95 backdrop-blur-xl border-r border-slate-800/80 flex flex-col h-screen shrink-0 select-none z-30 transition-all duration-300 shadow-2xl">
      {/* Brand Header */}
      <div className="p-4 border-b border-slate-800/80 bg-slate-900/40">
        <div className="flex items-center gap-3">
          <BrandMark brand={brand} />
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-1.5">
              <span className="font-bold text-white text-base tracking-wide truncate">{brand?.name}</span>
            </div>
            <p className="text-xs text-slate-400 truncate">{brand?.tagline}</p>
          </div>
        </div>

        {/* Quick Search in Sidebar */}
        <div className="mt-3 relative">
          <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Cari menu atau fitur..."
            className="w-full pl-9 pr-3 py-1.5 bg-slate-900/90 text-xs text-slate-200 placeholder-slate-500 rounded-lg border border-slate-800 focus:outline-none focus:border-amber-500/50 transition-colors"
          />
        </div>
      </div>

      {/* Navigation Groups List */}
      <div className="flex-1 overflow-y-auto px-2.5 py-3 space-y-4 custom-scrollbar">
        {filteredNav.map(group => {
          const isCollapsed = collapsedSections[group.id];

          return (
            <div key={group.id} className="space-y-1">
              {/* Group Header */}
              <button
                onClick={() => toggleSection(group.id)}
                className="w-full flex items-center justify-between px-2.5 py-1 text-[11px] font-bold text-slate-400 hover:text-slate-200 tracking-wider uppercase transition-colors group"
              >
                <span>{group.label}</span>
                <span className="text-slate-500 group-hover:text-slate-300">
                  {isCollapsed ? <ChevronRight className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />}
                </span>
              </button>

              {/* Items in Group */}
              {!isCollapsed && (
                <div className="space-y-0.5">
                  {group.items.map(item => {
                    const Icon = item.icon;
                    const isActive = activeTab === item.id;

                    return (
                      <button
                        key={item.id}
                        onClick={() => onSelectTab(item.id)}
                        className={`w-full flex items-center justify-between px-3 py-2 rounded-xl text-xs font-medium transition-all duration-150 text-left ${
                          isActive
                            ? 'bg-gradient-to-r from-amber-500/20 to-amber-500/5 text-amber-300 border border-amber-500/30 shadow-sm font-semibold'
                            : 'text-slate-300 hover:text-white hover:bg-slate-900/80 border border-transparent'
                        }`}
                      >
                        <div className="flex items-center gap-2.5 min-w-0">
                          <Icon className={`w-4 h-4 shrink-0 transition-colors ${isActive ? 'text-amber-400' : 'text-slate-400'}`} />
                          <span className="truncate">{item.label}</span>
                        </div>

                        {item.badge && (
                          <span className={`text-[10px] px-1.5 py-0.2 rounded-full font-semibold shrink-0 ml-1.5 ${
                            isActive
                              ? 'bg-amber-400 text-slate-950'
                              : 'bg-slate-800 text-slate-400 border border-slate-700/60'
                          }`}>
                            {item.badge}
                          </span>
                        )}
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* User Footer Profile */}
      <div className="p-3 border-t border-slate-800/80 bg-slate-900/60 flex items-center justify-between">
        <div className="flex items-center gap-2.5 min-w-0">
          <div className="w-8 h-8 rounded-lg bg-gradient-to-tr from-amber-500 to-amber-400 flex items-center justify-center font-bold text-slate-950 text-xs shrink-0 shadow-md">
            {user?.username ? user.username.charAt(0).toUpperCase() : 'P'}
          </div>
          <div className="min-w-0">
            <div className="text-xs font-semibold text-white truncate flex items-center gap-1">
              <span>{user?.username || 'Pengguna'}</span>
              <Shield className="w-3 h-3 text-amber-400 shrink-0" />
            </div>
            <div className="text-[11px] text-slate-400 truncate">
              {roleLabels[roleName(user?.role)] || 'Pegawai Toko'}
            </div>
          </div>
        </div>

        <button
          onClick={onLogout}
          title="Keluar dari Sistem"
          className="p-1.5 text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-colors shrink-0"
        >
          <LogOut className="w-4 h-4" />
        </button>
      </div>
    </aside>
  );
}
