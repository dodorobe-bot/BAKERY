namespace Seruni22Bakery.Domain.Entities;

public enum QualityDecision { Passed, Failed }

/// <summary>
/// Hasil pemeriksaan mutu untuk satu SPK. Satu SPK hanya diperiksa sekali: nilai jualnya
/// bergantung pada berapa unit yang benar-benar lolos, jadi pemeriksaan ulang harus menjadi
/// koreksi yang tercatat, bukan baris kedua yang saling bertentangan.
/// </summary>
public class QualityCheck
{
    public int Id { get; set; }
    public int WorkOrderId { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public int? CheckedByUserId { get; set; }
    public int SampleQuantity { get; set; }
    public int PassedQuantity { get; set; }
    public int FailedQuantity { get; set; }
    public QualityDecision Decision { get; set; } = QualityDecision.Passed;
    public string? AppearanceNotes { get; set; }
    public string? TasteNotes { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Pengembalian barang dari pesanan yang sudah lunas. Nilainya dikembalikan ke pelanggan
/// dan stok produk jadi masuk lagi ke etalase; jumlah kumulatifnya dibatasi pemicu basis data.
/// </summary>
public class SalesReturn
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public int SalesOrderId { get; set; }
    public int ProductTemplateId { get; set; }
    public decimal Quantity { get; set; }
    public long Amount { get; set; } // minor units
    /// <summary>Diskon yang ikut dibalik pada retur ini, supaya 4-002 tidak menahan potongan lama.</summary>
    public long DiscountAmount { get; set; }
    /// <summary>PPN yang dihapus dari kewajiban karena barangnya kembali.</summary>
    public long TaxAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RefundMethod { get; set; } = "Tunai";
    public DateTime ReturnedAt { get; set; } = DateTime.UtcNow;
    public int? JournalEntryId { get; set; }
    public string? IdempotencyKey { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
