namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Ditulis saat ada yang mencoba membukukan jurnal ke periode yang sudah ditutup. Sengaja
/// turunan InvalidOperationException: endpoint yang sudah menangkap jenis itu otomatis menjawab
/// 400 dengan pesan ini, dan jalur yang belum menangkapnya tetap bisa dibedakan dari kegagalan
/// mesin oleh pereda galat global.
/// </summary>
public sealed class PeriodClosedException : InvalidOperationException
{
    public PeriodClosedException(string pesan) : base(pesan) { }
}
