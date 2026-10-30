// Merek bawaan hanya dipakai bila pemilik belum mengisi Pengaturan. Nilainya sama dengan seed
// AppSettings supaya layar tidak berkedip ke teks lain saat pengaturan pertama kali dimuat.
export const DEFAULT_BRAND = {
  name: 'Seruni 22 Bakery',
  tagline: 'Sistem Manajemen Toko Kue',
  logo: null,
};

// Inisial cadangan dihitung dari nama, bukan disimpan: penggantian nama langsung ikut terlihat.
// Bila nama memuat angka (mis. "Seruni 22 Bakery"), angka itu dipertahankan supaya penanda
// tetap "S22" — bentuk yang sudah dikenali pelanggan.
export const brandInitials = (name) => {
  const teks = String(name ?? '').trim();
  if (!teks) return 'S22';

  const angka = teks.match(/\d+/g);
  if (angka) {
    const awal = (teks.match(/[A-Za-z0-9]/) ?? ['S'])[0].toUpperCase();
    return (awal + angka.join('')).slice(0, 3).toUpperCase();
  }

  const kata = teks.split(/\s+/).filter(Boolean);
  const inisial = kata.slice(0, 2).map((k) => k.charAt(0).toUpperCase()).join('');
  return inisial || teks.charAt(0).toUpperCase();
};

export const brandFromSettings = (settings) => ({
  name: (settings?.brand_name ?? '').trim() || DEFAULT_BRAND.name,
  tagline: (settings?.brand_tagline ?? '').trim() || DEFAULT_BRAND.tagline,
  logo: settings?.brand_logo || null,
});

// Logo disimpan di AppSettings sebagai data URL, jadi ukurannya harus kecil: foto dari ponsel
// bisa 3 MB dan setiap pembacaan pengaturan akan ikut mengangkutnya. Gambar diperkecil ke 256
// piksel sisi terpanjang lalu dikodekan PNG sekali, sebelum menyentuh server.
export const MAKS_SISI_LOGO = 256;
export const BATAS_LOGO_BITA = 512 * 1024;
const JENIS_LOGO = ['image/png', 'image/jpeg', 'image/webp'];

export const fileKeLogo = (file) => new Promise((resolve, reject) => {
  if (!file) {
    reject(new Error('Berkas logo belum dipilih.'));
    return;
  }
  if (!JENIS_LOGO.includes(file.type)) {
    reject(new Error('Logo harus berupa gambar PNG, JPEG, atau WebP.'));
    return;
  }

  const alamat = URL.createObjectURL(file);
  const gambar = new Image();
  gambar.onload = () => {
    URL.revokeObjectURL(alamat);
    try {
      const skala = Math.min(1, MAKS_SISI_LOGO / Math.max(gambar.naturalWidth || 1, gambar.naturalHeight || 1));
      const lebar = Math.max(1, Math.round((gambar.naturalWidth || 1) * skala));
      const tinggi = Math.max(1, Math.round((gambar.naturalHeight || 1) * skala));
      const kanvas = document.createElement('canvas');
      kanvas.width = lebar;
      kanvas.height = tinggi;
      kanvas.getContext('2d').drawImage(gambar, 0, 0, lebar, tinggi);
      const dataUrl = kanvas.toDataURL('image/png');
      const bita = Math.floor((dataUrl.length - dataUrl.indexOf(',') - 1) * 3 / 4);
      if (bita > BATAS_LOGO_BITA) {
        reject(new Error('Logo masih terlalu besar setelah diperkecil. Pakai gambar yang lebih sederhana.'));
        return;
      }
      resolve(dataUrl);
    } catch {
      reject(new Error('Logo tidak bisa dibaca. Coba berkas gambar lain.'));
    }
  };
  gambar.onerror = () => {
    URL.revokeObjectURL(alamat);
    reject(new Error('Logo tidak bisa dibaca. Coba berkas gambar lain.'));
  };
  gambar.src = alamat;
});
