import { useMemo, useState } from 'react';

// Daftar panjang tetap utuh di memori supaya pencarian dan filter bekerja pada seluruh data,
// tetapi DOM hanya memuat sebagian baris. Tanpa ini layar dengan 300-600 baris menahan tata
// letak dan pengecatan ratusan milidetik setiap kali dibuka.
export const BARIS_AWAL = 50;

export function useBarisBertahap(daftar, awal = BARIS_AWAL, langkah = 100) {
  const [batas, setBatas] = useState(awal);
  const jumlah = daftar.length;

  // Batas tidak disetel ulang saat data bertambah: memuat halaman berikutnya atau menyegarkan
  // layar tidak boleh menyusutkan baris yang sudah sengaja dibuka pengguna. Daftar yang lebih
  // pendek dari batas otomatis terpotong oleh slice.
  const tampil = useMemo(() => daftar.slice(0, batas), [daftar, batas]);
  return {
    tampil,
    jumlah,
    adaLagi: batas < jumlah,
    tampilkanLagi: () => setBatas((b) => b + langkah),
  };
}
