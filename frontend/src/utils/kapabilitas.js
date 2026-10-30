// Satu-satunya sumber kebenaran "siapa boleh apa" ada di server (RolePolicies.cs). Yang dikirim
// saat login hanyalah daftar kebijakan yang boleh dibuka peran ini, supaya layar tidak
// menampilkan menu atau tombol yang pasti-basti ditolak 403.
//
// Ini penyembunyi antarmuka, bukan pemeriksaan keamanan: setiap endpoint tetap memeriksa
// tokennya sendiri. Mengandalkan daftar ini saja sama saja dengan membuka API untuk peran yang
// salah, jadi jangan pernah memindahkan aturan bisnis ke sini.
export const bolehAksi = (user, kebijakan) => {
  if (!kebijakan) return true;
  const daftar = user?.capabilities;
  return Array.isArray(daftar) && daftar.includes(kebijakan);
};
