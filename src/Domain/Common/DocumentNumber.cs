namespace Seruni22Bakery.Domain.Common;

/// <summary>Dokumen yang dinomori harian oleh server, bukan oleh layar.</summary>
public enum DocumentKind
{
    Order,
    WorkOrder,
    PurchaseOrder,
    Return,
    SupplierReturn,
    SupplierPayment
}

/// <summary>
/// Nomor dokumen tampil di cetak dan di laporan, jadi bentuknya tidak boleh berubah diam-diam:
/// PREFIKS-tanggalUTC-urutan, urutan dinaikkan satu per hari per jenis.
/// </summary>
public static class DocumentNumber
{
    public static string Prefix(DocumentKind kind) => kind switch
    {
        DocumentKind.Order => "ORD",
        DocumentKind.WorkOrder => "SPK",
        DocumentKind.PurchaseOrder => "PO",
        DocumentKind.Return => "RTR",
        // RTB/PPB sengaja memakai huruf depan berbeda dari RTR supaya retur barang keluar ke
        // pemasok tidak pernah tertukar dengan retur dari pelanggan saat dibaca di buku besar.
        DocumentKind.SupplierReturn => "RTB",
        DocumentKind.SupplierPayment => "PPB",
        _ => throw new ArgumentException($"Jenis dokumen tidak dikenal: {kind}")
    };

    public static string Format(DocumentKind kind, DateTime dayUtc, long sequence)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Urutan dokumen dimulai dari 1.");
        var day = dayUtc.Date;
        return $"{Prefix(kind)}-{day:yyyyMMdd}-{sequence:D4}";
    }
}
