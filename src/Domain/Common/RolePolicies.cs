using Seruni22Bakery.Domain.Entities;

namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Matriks kapabilitas: satu nama kebijakan, satu daftar peran yang boleh.
/// Dipasang di endpoint lewat RequireAuthorization(name) dan diuji langsung, jadi
/// "siapa boleh apa" hanya ada di satu tempat dan bisa dibaca orang baru.
///
/// Pembagian yang dipakai:
/// - Owner  : pemilik toko, penuh termasuk pengaturan, hak akses, dan pembatalan.
/// - Admin  : kepala operasional/toko, penuh kecuali pengaturan sistem dan hak akses.
/// - Dapur  : produksi, penerimaan barang, dan koreksi stok; tidak menyentuh uang atau buku.
/// - Pegawai: kasir/toko, mencatat pesanan dan pembayaran; tidak bisa membatalkan, membuka buku besar,
///   atau membaca persediaan/pembelian/produksi. Harga jual yang dipakai adalah harga katalog, dan
///   memotongnya lewat diskon juga bukan haknya — diskon mengubah harga sama seperti mengubah katalog.
/// </summary>
public static class RolePolicies
{
    public const string Sales = "sales";
    public const string SalesCancel = "sales-cancel";
    // Diskon memotong tagihan tanpa melewati harga katalog, jadi ia hak mengubah harga —
    // bukan tombol yang boleh ada di layar kasir.
    public const string SalesDiscount = "sales-discount";
    public const string SalesReturn = "sales-return";
    public const string FinanceRead = "finance-read";
    public const string FinanceWrite = "finance-write";
    public const string ProductionWrite = "production-write";
    public const string ProductionCancel = "production-cancel";
    public const string InventoryWrite = "inventory-write";
    public const string InventoryRead = "inventory-read";
    // Memesan barang ke pemasok mengikat uang toko; menerima barang hanya menutup pesanan
    // yang sudah ada. Dapur perlu yang kedua, bukan yang pertama.
    public const string PurchasesCreate = "purchases-create";
    public const string PurchasesReceive = "purchases-receive";
    public const string PurchasesCancel = "purchases-cancel";
    // Retur pemasok mengurangi stok DAN melepas utang, jadi ia menyentuh uang seperti pesanan,
    // bukan sekadar memindahkan barang seperti penerimaan.
    public const string PurchasesReturn = "purchases-return";
    public const string MasterDataWrite = "master-data-write";
    public const string SalesExport = "sales-export";
    public const string Administration = "administration";

    private static readonly IReadOnlyDictionary<string, UserRole[]> Allowed =
        new Dictionary<string, UserRole[]>
        {
            [Sales] = [UserRole.Owner, UserRole.Admin, UserRole.Pegawai],
            [SalesCancel] = [UserRole.Owner, UserRole.Admin],
            // Tanpa pagar ini, larangan "harga bukan masukan bebas untuk kasir" bisa dibelokkan:
            // harga katalog dibiarkan utuh, lalu tagihannya dipotong lewat diskon.
            [SalesDiscount] = [UserRole.Owner, UserRole.Admin],
            // Retur mengeluarkan uang dan mengembalikan stok, jadi bukan aksi kasir.
            [SalesReturn] = [UserRole.Owner, UserRole.Admin],
            [SalesExport] = [UserRole.Owner, UserRole.Admin],
            [FinanceRead] = [UserRole.Owner, UserRole.Admin],
            [FinanceWrite] = [UserRole.Owner, UserRole.Admin],
            [ProductionWrite] = [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
            [ProductionCancel] = [UserRole.Owner, UserRole.Admin],
            [InventoryWrite] = [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
            // Bahan baku, stok per lokasi, mutasi, dan lot adalah ranah dapur/gudang.
            // Kasir hanya perlu produk jadi dan harga jual, bukan isi gudang.
            [InventoryRead] = [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
            [PurchasesCreate] = [UserRole.Owner, UserRole.Admin],
            [PurchasesReceive] = [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
            [PurchasesCancel] = [UserRole.Owner, UserRole.Admin],
            [PurchasesReturn] = [UserRole.Owner, UserRole.Admin],
            [MasterDataWrite] = [UserRole.Owner, UserRole.Admin],
            [Administration] = [UserRole.Owner],
        };

    public static IReadOnlyCollection<string> Names { get; } = Allowed.Keys.ToArray();

    public static IReadOnlyList<UserRole> AllowedRoles(string policy) =>
        Allowed.TryGetValue(policy, out var roles)
            ? roles
            : throw new ArgumentException($"Kebijakan otorisasi tidak dikenal: {policy}");

    public static bool Allows(string policy, UserRole role) => AllowedRoles(policy).Contains(role);

    /// <summary>
    /// Daftar kebijakan yang boleh dibuka sebuah peran, untuk dikirim saat login. Ini hanya
    /// dipakai sisi antarmuka untuk menyembunyikan menu yang pasti ditolak 403; setiap endpoint
    /// tetap memeriksa tokennya sendiri lewat RequireAuthorization(policy).
    /// </summary>
    public static IReadOnlyList<string> Kapabilitas(UserRole role) =>
        Names.Where(p => Allows(p, role)).OrderBy(p => p, StringComparer.Ordinal).ToList();
}
