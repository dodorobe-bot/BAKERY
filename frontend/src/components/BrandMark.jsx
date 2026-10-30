import React from 'react';
import { brandInitials } from '../utils/brand';

// Satu penanda merek untuk layar masuk dan bilah samping: kalau pemilik mengunggah logo, gambar
// itu yang tampil; kalau belum, inisial nama dipakai supaya kotak merek tidak pernah kosong.
export default function BrandMark({ brand, besar = false }) {
  const bingkai = besar
    ? 'w-16 h-16 rounded-2xl p-0.5'
    : 'w-10 h-10 rounded-xl p-0.5';
  const dalam = besar
    ? 'rounded-[14px] text-2xl'
    : 'rounded-[10px] text-lg';

  return (
    <div className={`${bingkai} bg-gradient-to-tr from-amber-500 via-amber-600 to-amber-400 shadow-lg shadow-amber-500/20 flex items-center justify-center shrink-0`}>
      {brand?.logo ? (
        <img
          src={brand.logo}
          alt=""
          className={`w-full h-full ${dalam} bg-slate-950 object-contain p-1`}
        />
      ) : (
        <div className={`w-full h-full bg-slate-950 flex items-center justify-center font-black text-amber-400 tracking-wider ${dalam}`}>
          {brandInitials(brand?.name)}
        </div>
      )}
    </div>
  );
}
