import { useCallback, useRef, useState } from 'react';

// Satu kunci mewakili satu niat, bukan satu klik: dibuat saat form dibuka dan dipakai terus untuk
// setiap percobaan "Simpan" dari form itu. Klik ganda karena itu hanya menyimpan sekali, tetapi
// setelah galat validasi pengguna masih boleh menyimpan ulang isian yang sama.
const acak = () =>
  globalThis.crypto?.randomUUID?.() ??
  `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 10)}`;

// Server hanya menerima huruf, angka, titik, garis bawah, dan tanda hubung (8-100 karakter).
// Dipanggil saat form dibuka, bukan saat halaman dimuat: satu kali buka form = satu niat.
export const kunciBaru = (rute) =>
  `${rute.replace(/[^A-Za-z0-9._-]+/g, '-')}-${acak()}`.slice(0, 100);

// Penghenti klik ganda yang sinkron. Pengecekan state React tidak cukup: dua klik dalam satu
// tick sama-sama membaca state lama, jadi permintaan kedua sempat berangkat dan server
// membalasnya dengan 409 "masih diproses" yang di layar terbaca sebagai kegagalan menyimpan.
// Ref berubah seketika, jadi klik kedua pada form yang sama tidak pernah mengirim apa pun.
export const useKunciSimpan = () => {
  const berjalan = useRef(false);
  const [sedangSimpan, setSedangSimpan] = useState(false);

  const mulai = useCallback(() => {
    if (berjalan.current) return false;
    berjalan.current = true;
    setSedangSimpan(true);
    return true;
  }, []);

  const selesai = useCallback(() => {
    berjalan.current = false;
    setSedangSimpan(false);
  }, []);

  return { sedangSimpan, mulai, selesai };
};
