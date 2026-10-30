using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

/// <summary>
/// Membentuk dan membukukan jurnal otomatis untuk setiap kejadian bisnis.
/// Metode Build... tidak menyentuh basis data, sehingga entri bisa ikut commit
/// bersama dokumen sumbernya (pesanan, struk penerimaan, SPK). Metode Post...
/// tetap ada untuk aksi tunggal seperti pencatatan biaya.
/// Akun dirujuk dengan kode bagan akun, bukan Id: Id tergantung urutan pembuatan data.
/// </summary>
public class AccountingService
{
    private readonly IJournalRepository _journals;
    private readonly IAccountRepository _accounts;
    private readonly Dictionary<string, int> _accountIdByCode = new();

    private const string KasKode = "1-001";
    private const string PersediaanBahanKode = "1-003";
    private const string PersediaanDapurKode = "1-004";
    private const string PersediaanJadiKode = "1-006";
    private const string UangMukaKode = "2-001";
    private const string UtangUsahaKode = "2-002";
    private const string PendapatanKode = "4-001";
    private const string BebanBahanKode = "5-001";
    private const string BebanOperasionalKode = "5-002";
    private const string BebanGajiKode = "5-003";
    private const string BebanProdukRusakKode = "5-004";

    public AccountingService(IJournalRepository journals, IAccountRepository accounts)
    {
        _journals = journals;
        _accounts = accounts;
    }

    /// <summary>Uang muka pelanggan: Dr Kas, Cr Uang Muka.</summary>
    /// <param name="orderId">Id pesanan, atau null bila pesannya belum tersimpan dan repository yang mengisi.</param>
    public async Task<JournalEntry> BuildDownPaymentAsync(
        int? orderId, string customerName, long amount, int userId, string? idempotencyKey = null)
    {
        var kas = await AccountIdAsync(KasKode);
        var uangMuka = await AccountIdAsync(UangMukaKode);

        return new JournalEntry
        {
            Description = $"DP Pesanan: {customerName}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "SalesOrder",
            SourceDocumentId = orderId,
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            IdempotencyKey = idempotencyKey,
            Lines =
            {
                new() { AccountId = kas, DebitAmount = amount },
                new() { AccountId = uangMuka, CreditAmount = amount }
            }
        };
    }

    /// <summary>
    /// Pembayaran berjalan: Dr Kas, Cr Uang Muka.
    /// Sebelumnya kreditnya masuk Piutang, padahal tidak ada satu pun kaki mendebit Piutang,
    /// sehingga saldo piutang jadi negatif di neraca saldo.
    /// </summary>
    public async Task<JournalEntry> BuildPaymentAsync(
        int orderId, string customerName, long amount, int userId)
    {
        var kas = await AccountIdAsync(KasKode);
        var uangMuka = await AccountIdAsync(UangMukaKode);

        return new JournalEntry
        {
            Description = $"Pembayaran: {customerName}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "SalesOrder",
            SourceDocumentId = orderId,
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines =
            {
                new() { AccountId = kas, DebitAmount = amount },
                new() { AccountId = uangMuka, CreditAmount = amount }
            }
        };
    }

    /// <summary>Pelunasan: Dr Uang Muka, Cr Pendapatan atas seluruh nilai pesanan.</summary>
    public async Task<JournalEntry> BuildRevenueRecognitionAsync(
        int? orderId, string customerName, long totalAmount, int userId)
    {
        var uangMuka = await AccountIdAsync(UangMukaKode);
        var pendapatan = await AccountIdAsync(PendapatanKode);

        return new JournalEntry
        {
            Description = $"Pendapatan Pesanan: {customerName}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "SalesOrder",
            SourceDocumentId = orderId,
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines =
            {
                new() { AccountId = uangMuka, DebitAmount = totalAmount },
                new() { AccountId = pendapatan, CreditAmount = totalAmount }
            }
        };
    }

    /// <summary>
    /// Retur penjualan: Dr Pendapatan, Cr Kas. Retur hanya dilakukan untuk pesanan yang sudah
    /// lunas, jadi pendapatannya sudah diakui dan pengembalian dana mengurangi pendapatan —
    /// bukan uang muka, yang saldonya sudah nol setelah pelunasan.
    /// </summary>
    public async Task<JournalEntry> BuildSalesReturnAsync(
        string returnNumber, string customerName, long amount, int userId)
    {
        var pendapatan = await AccountIdAsync(PendapatanKode);
        var kas = await AccountIdAsync(KasKode);

        return new JournalEntry
        {
            Description = $"Retur Penjualan {returnNumber}: {customerName}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "SalesReturn",
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines =
            {
                new() { AccountId = pendapatan, DebitAmount = amount },
                new() { AccountId = kas, CreditAmount = amount }
            }
        };
    }

    /// <summary>Biaya: Dr akun beban sesuai kategori, Cr Kas. Entri null bila nominal nol.</summary>
    public async Task<JournalEntry?> BuildExpenseAsync(
        string category, string description, long amount, int userId)
    {
        if (amount <= 0) return null;

        var bebanKode = category switch
        {
            "HPP" => BebanBahanKode,
            "Gaji" => BebanGajiKode,
            _ => BebanOperasionalKode
        };
        var beban = await AccountIdAsync(bebanKode);
        var kas = await AccountIdAsync(KasKode);

        return new JournalEntry
        {
            Description = $"Biaya: {description}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "Expense",
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines =
            {
                new() { AccountId = beban, DebitAmount = amount },
                new() { AccountId = kas, CreditAmount = amount }
            }
        };
    }

    /// <summary>
    /// Penyelesaian SPK: Cr Persediaan Dapur atas seluruh bahan terpakai,
    /// Dr Persediaan Produk Jadi untuk bagian yang lolos dan Dr Beban Produk Rusak untuk scrap.
    /// </summary>
    /// <summary>
    /// Penyelesaian SPK. Bahan yang lolos modalisasi ke stok produk jadi, bahan yang hilang
    /// langsung jadi beban, sedangkan tenaga kerja dan overhead dibebankan ke kas pada hari itu
    /// (keputusan bisnis: tidak dikapitalisasi ke persediaan, tetapi tetap dihitung dalam HPP
    /// per unit untuk margin).
    /// </summary>
    public async Task<JournalEntry?> BuildProductionCompletionAsync(
        int workOrderId, string? orderNumber, long totalMaterialCost, long scrapLossCost,
        long laborCost, long overheadCost, int userId)
    {
        if (totalMaterialCost <= 0 && laborCost <= 0 && overheadCost <= 0) return null;

        // Kerugian tidak mungkin melebihi biaya bahan; kalau input memaksa lebih besar,
        // journals-nya jadi tidak seimbang dan entri ditolak di jalur tulis.
        long scrap = Math.Clamp(scrapLossCost, 0, totalMaterialCost);
        long finishedGoodsCost = totalMaterialCost - scrap;

        var lines = new List<JournalLine>();

        if (totalMaterialCost > 0)
        {
            var dapur = await AccountIdAsync(PersediaanDapurKode);
            lines.Add(new() { AccountId = dapur, CreditAmount = totalMaterialCost });

            if (finishedGoodsCost > 0)
                lines.Add(new() { AccountId = await AccountIdAsync(PersediaanJadiKode), DebitAmount = finishedGoodsCost });
            if (scrap > 0)
                lines.Add(new() { AccountId = await AccountIdAsync(BebanProdukRusakKode), DebitAmount = scrap });
        }

        var kas = await AccountIdAsync(KasKode);
        if (laborCost > 0)
            lines.Add(new() { AccountId = await AccountIdAsync(BebanGajiKode), DebitAmount = laborCost });
        if (overheadCost > 0)
            lines.Add(new() { AccountId = await AccountIdAsync(BebanOperasionalKode), DebitAmount = overheadCost });
        long biayaTunai = laborCost + overheadCost;
        if (biayaTunai > 0)
            lines.Add(new() { AccountId = kas, CreditAmount = biayaTunai });

        return new JournalEntry
        {
            Description = $"Penyelesaian Produksi SPK: {orderNumber}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "WorkOrder",
            SourceDocumentId = workOrderId,
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines = lines
        };
    }

    /// <summary>Penerimaan barang: Dr Persediaan Bahan Baku, Cr Utang Usaha. Entri null bila nominal nol.</summary>
    public async Task<JournalEntry?> BuildPurchaseReceiptAsync(
        string poNumber, long totalAmount, int userId)
    {
        if (totalAmount <= 0) return null;

        var persediaan = await AccountIdAsync(PersediaanBahanKode);
        var utang = await AccountIdAsync(UtangUsahaKode);

        return new JournalEntry
        {
            Description = $"Penerimaan Barang PO: {poNumber}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "GoodsReceipt",
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines =
            {
                new() { AccountId = persediaan, DebitAmount = totalAmount },
                new() { AccountId = utang, CreditAmount = totalAmount }
            }
        };
    }

    /// <summary>
    /// Pembalik seluruh entri yang sudah dibukukan untuk satu dokumen sumber.
    /// Semua kaki ditukar arah, jadi pembatalan mengembalikan saldo persis seperti semula.
    /// </summary>
    public static List<JournalEntry> BuildReversals(IEnumerable<JournalEntry> originals, int userId)
        => originals
            .Where(e => e.Status != JournalStatus.Reversed && e.Lines.Count > 0)
            .Select(e => new JournalEntry
            {
                Description = $"Pembalik: {e.Description}",
                TransactionDate = BusinessTime.Now(),
                SourceDocumentType = e.SourceDocumentType,
                SourceDocumentId = e.SourceDocumentId,
                ReversalOfId = e.Id,
                PostedAt = DateTime.UtcNow,
                PostedByUserId = userId,
                Lines = e.Lines.Select(l => new JournalLine
                {
                    AccountId = l.AccountId,
                    DebitAmount = l.CreditAmount,
                    CreditAmount = l.DebitAmount,
                    Notes = l.Notes
                }).ToList()
            })
            .ToList();

    /// <summary>Membukukan entri tunggal di luar jalur atomik dokumen sumber.</summary>
    public async Task PostAsync(JournalEntry entry)
    {
        if (!entry.IsBalanced())
            throw new InvalidOperationException($"Jurnal tidak seimbang (debit != kredit): {entry.Description}");
        await _journals.CreateAsync(entry);
    }

    public async Task PostExpenseAsync(string category, string description, long amount, int userId)
    {
        var entry = await BuildExpenseAsync(category, description, amount, userId)
            ?? throw new InvalidOperationException("Nominal biaya harus lebih besar dari nol.");
        await PostAsync(entry);
    }

    private async Task<int> AccountIdAsync(string code)
    {
        if (_accountIdByCode.TryGetValue(code, out var cached)) return cached;

        var account = await _accounts.GetByCodeAsync(code)
            ?? throw new InvalidOperationException(
                $"Akun {code} belum ada di bagan akun, jadi jurnal tidak bisa dibukukan.");
        if (!account.IsActive)
            throw new InvalidOperationException($"Akun {code} · {account.Name} sudah dinonaktifkan.");

        _accountIdByCode[code] = account.Id;
        return account.Id;
    }
}
