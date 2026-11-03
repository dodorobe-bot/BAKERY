import { api } from '../services/api';

// Mendaftar printer terpasang bisa lambat (printer jaringan yang tidak menyahut). Status ini
// hanya untuk menandai tombol cetak, jadi versi singkatnya dipakai ulang selama beberapa detik
// alih-alih memeriksa ulang setiap kali layar kasir dibuka.
const UMUR_STATUS_PERANGKAT_MS = 15000;
let statusPerangkatTerakhir = { waktu: 0, nilai: null };

export function statusPerangkatSingkat() {
  if (statusPerangkatTerakhir.nilai && Date.now() - statusPerangkatTerakhir.waktu < UMUR_STATUS_PERANGKAT_MS) {
    return Promise.resolve(statusPerangkatTerakhir.nilai);
  }
  return api.getDeviceStatus().then((s) => {
    statusPerangkatTerakhir = { waktu: Date.now(), nilai: s };
    return s;
  });
}

// Pengaturan perangkat disimpan dari layar lain. Tanpa pembersihan ini layar kasir masih memakai
// status lama sampai 15 detik, sehingga tombol cetak bisa menandai tujuan yang sudah diganti.
export function lupakanStatusPerangkat() {
  statusPerangkatTerakhir = { waktu: 0, nilai: null };
}
