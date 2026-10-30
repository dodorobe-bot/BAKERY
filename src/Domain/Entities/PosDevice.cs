namespace Seruni22Bakery.Domain.Entities;

public enum PrintJobStatus { Pending, Printed, Failed }

public enum PrintJobKind { Receipt, Test }

/// <summary>
/// Satu permintaan cetak yang tercatat. Pesanan tersimpan lebih dulu, baru pekerjaan cetak
/// dibuat; karena itu kegagalan printer tidak pernah membatalkan atau mengulang penjualan.
/// </summary>
public class PrintJob
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public PrintJobKind Kind { get; set; } = PrintJobKind.Receipt;
    public string? PrinterName { get; set; }
    public int PaperWidthMm { get; set; } = 80;
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    /// <summary>Isi struk yang diminta dicetak, disimpan untuk bukti dan cetak ulang.</summary>
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedByUserId { get; set; }
    public DateTime? FinishedAt { get; set; }
}
