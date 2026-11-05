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
    private const string PiutangKode = "1-002";
    private const string PersediaanBahanKode = "1-003";
    private const string PersediaanDapurKode = "1-004";
    private const string PersediaanJadiKode = "1-006";
    private const string UangMukaKode = "2-001";
    private const string UtangUsahaKode = "2-002";
    private const string UtangPpnKode = "2-003";
    private const string PendapatanKode = "4-001";
    private const string DiskonKode = "4-002";
    private const string BebanBahanKode = "5-001";
    private const string BebanOperasionalKode = "5-002";
    private const string BebanGajiKode = "5-003";
    private const string BebanProdukRusakKode = "5-004";
    private const string BebanSelisihPersediaanKode = "5-006";

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
    /// Pembayaran berjalan. Selama pendapatan belum diakui, uang titipan pelanggan ditahan di
    /// 2-001 Uang Muka; setelah barang diserahkan dan piutangnya lahir, pembayaran yang sama
    /// melunasi 1-002. Kredit ke 2-001 untuk pesanan yang sudah diakui akan membuat uang muka
    /// menumpuk tanpa pesanan yang menunggu.
    /// </summary>
    public async Task<JournalEntry> BuildPaymentAsync(
        int orderId, string customerName, long amount, int userId, bool melunasiPiutang = false)
    {
        var kas = await AccountIdAsync(KasKode);
        var lawan = await AccountIdAsync(melunasiPiutang ? PiutangKode : UangMukaKode);

        return new JournalEntry
        {
            Description = melunasiPiutang
                ? $"Pelunasan Piutang: {customerName}"
                : $"Pembayaran: {customerName}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "SalesOrder",
            SourceDocumentId = orderId,
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines =
            {
                new() { AccountId = kas, DebitAmount = amount },
                new() { AccountId = lawan, CreditAmount = amount }
            }
        };
    }

    /// <summary>
    /// Pengakuan pendapatan satu pesanan. Kaki pendebitnya adalah tempat tagihan berdiri:
    /// uang yang sudah masuk keluar dari 2-001 Uang Muka, sisa yang belum dibayar menjadi
    /// 1-002 Piutang Usaha. Pendapatan dikredit sebesar nilai sebelum PPN (termasuk diskon yang
    /// masih melekat), PPN dipisah ke 2-003 karena uang itu bukan milik toko, dan diskon dicatat
    /// di 4-002 supaya layar masih bisa melihat harga katalog aslinya.
    /// </summary>
    public async Task<JournalEntry> BuildRevenueRecognitionAsync(
        int? orderId, string customerName, long totalAmount, long receivedAmount,
        long discountAmount, long taxAmount, int userId)
    {
        if (totalAmount <= 0)
            throw new InvalidOperationException("Nilai pesanan yang diakui pendapatannya harus lebih dari nol.");
        if (receivedAmount < 0 || receivedAmount > totalAmount)
            throw new InvalidOperationException(
                "Uang yang sudah diterima pesanan tidak boleh di luar total tagihannya.");
        if (discountAmount < 0 || taxAmount < 0)
            throw new InvalidOperationException("Diskon dan PPN tidak boleh negatif.");

        var piutang = totalAmount - receivedAmount;
        var lines = new List<JournalLine>();
        // Pesanan yang diserahkan tanpa uang masuk sama sekali tidak punya sisa uang muka untuk
        // dibalik, jadi kaki 2-001-nya ditiatkan: seluruh tagihan berdiri sebagai piutang.
        if (receivedAmount > 0)
            lines.Add(new() { AccountId = await AccountIdAsync(UangMukaKode), DebitAmount = receivedAmount });
        if (piutang > 0)
            lines.Add(new() { AccountId = await AccountIdAsync(PiutangKode), DebitAmount = piutang });

        var pendapatan = totalAmount - taxAmount + discountAmount;
        lines.Add(new() { AccountId = await AccountIdAsync(PendapatanKode), CreditAmount = pendapatan });

        if (taxAmount > 0)
            lines.Add(new() { AccountId = await AccountIdAsync(UtangPpnKode), CreditAmount = taxAmount });
        if (discountAmount > 0)
            lines.Add(new() { AccountId = await AccountIdAsync(DiskonKode), DebitAmount = discountAmount });

        return new JournalEntry
        {
            Description = $"Pendapatan Pesanan: {customerName}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "SalesOrder",
            SourceDocumentId = orderId,
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines = lines
        };
    }

    /// <summary>
    /// Retur penjualan: pendapatan dan PPN-nya ditarik kembali, diskon yang tadinya mengurangi
    /// pendapatan dibalik, dan uang keluar dari kas. Hanya pesanan yang pendapatannya sudah
    /// diakui yang bisa diretur, jadi pembalikannya selalu punya lawan.
    /// </summary>
    public async Task<JournalEntry> BuildSalesReturnAsync(
        string returnNumber, string customerName, long refund, long discountAmount, long taxAmount, int userId)
    {
        if (refund <= 0)
            throw new InvalidOperationException("Nilai retur harus lebih besar dari nol.");
        if (discountAmount < 0 || taxAmount < 0)
            throw new InvalidOperationException("Diskon dan PPN retur tidak boleh negatif.");

        // Pendapatan yang dibalik adalah nilai sebelum PPN ditambah diskonnya, jadi selisih
        // debit-kredit selalu persis sama dengan uang yang kembali ke pelanggan.
        var pendapatan = refund + discountAmount - taxAmount;
        if (pendapatan <= 0)
            throw new InvalidOperationException(
                "Nilai retur lebih besar daripada pendapatan yang bisa dibalik.");

        var lines = new List<JournalLine>
        {
            new() { AccountId = await AccountIdAsync(PendapatanKode), DebitAmount = pendapatan }
        };
        if (taxAmount > 0)
            lines.Add(new() { AccountId = await AccountIdAsync(UtangPpnKode), DebitAmount = taxAmount });
        if (discountAmount > 0)
            lines.Add(new() { AccountId = await AccountIdAsync(DiskonKode), CreditAmount = discountAmount });
        lines.Add(new() { AccountId = await AccountIdAsync(KasKode), CreditAmount = refund });

        return new JournalEntry
        {
            Description = $"Retur Penjualan {returnNumber}: {customerName}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "SalesReturn",
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines = lines
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
    /// Penyelesaian SPK. Bahan yang lolos, tenaga kerja, dan overhead semuanya dikapitalisasi
    /// ke stok produk jadi supaya saldo 1-006 sama dengan nilai stok yang dipakai menu
    /// Inventaris dan HPP penjualan. Bahan yang hilang langsung menjadi beban produk rusak.
    /// </summary>
    public async Task<JournalEntry?> BuildProductionCompletionAsync(
        int workOrderId, string? orderNumber, long totalMaterialCost, long scrapLossCost,
        long laborCost, long overheadCost, int goodUnits, int userId)
    {
        if (totalMaterialCost <= 0 && laborCost <= 0 && overheadCost <= 0) return null;

        // Tanpa unit baik tidak ada stok yang bisa menopang aset produk jadi, jadi seluruh
        // bahan harus menjadi beban kerugian. Selain itu kerugian tidak mungkin melebihi
        // biaya bahan; kalau input memaksa lebih besar, jurnalnya jadi tidak seimbang dan
        // entri ditolak di jalur tulis.
        long scrap = goodUnits <= 0
            ? totalMaterialCost
            : Math.Clamp(scrapLossCost, 0, totalMaterialCost);

        var lines = new List<JournalLine>();

        if (totalMaterialCost > 0)
        {
            var dapur = await AccountIdAsync(PersediaanDapurKode);
            lines.Add(new() { AccountId = dapur, CreditAmount = totalMaterialCost });

            if (scrap > 0)
                lines.Add(new() { AccountId = await AccountIdAsync(BebanProdukRusakKode), DebitAmount = scrap });
        }

        var kas = await AccountIdAsync(KasKode);
        long biayaTunai = laborCost + overheadCost;

        if (goodUnits <= 0)
        {
            // Tidak ada unit yang bisa menopang biaya tenaga kerja/overhead: keduanya beban
            // periode berjalan, bukan aset persediaan.
            if (laborCost > 0)
                lines.Add(new() { AccountId = await AccountIdAsync(BebanGajiKode), DebitAmount = laborCost });
            if (overheadCost > 0)
                lines.Add(new() { AccountId = await AccountIdAsync(BebanOperasionalKode), DebitAmount = overheadCost });
        }
        else
        {
            // Nilai yang menempel pada stok harus persis sama dengan yang ditulis
            // FinishedGoodsLedger (harga pokok per unit × unit baik), bukan hanya bahan.
            // Tanpa ini jurnal mendebit 1-006 lebih kecil daripada nilai stok, sehingga akun
            // 1-006 bisa bersaldo negatif begitu barangnya terjual.
            long unitCost = ProductionCostRules.CostPerGoodUnit(
                totalMaterialCost, laborCost, overheadCost, scrap, goodUnits);
            long inventoryValue = unitCost * goodUnits;
            long capitalized = Math.Max(0, totalMaterialCost - scrap) + biayaTunai;

            if (inventoryValue > 0)
                lines.Add(new() { AccountId = await AccountIdAsync(PersediaanJadiKode), DebitAmount = inventoryValue });

            // Biaya rata-rata per unit adalah bilangan bulat, jadi sisa pembulatan (maksimal
            // setengah sen per unit) tidak bisa ikut tersimpan di stok. Selisihnya dibukukan
            // ke beban selisih persediaan agar jurnal tetap seimbang dan saldo 1-006 sama
            // dengan nilai stok yang dihitung dari harga rata-rata.
            long rounding = capitalized - inventoryValue;
            if (rounding > 0)
                lines.Add(new() { AccountId = await AccountIdAsync(BebanSelisihPersediaanKode), DebitAmount = rounding });
            else if (rounding < 0)
                lines.Add(new() { AccountId = await AccountIdAsync(BebanSelisihPersediaanKode), CreditAmount = -rounding });
        }

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

    /// <summary>
    /// Penerimaan barang: Dr persediaan sesuai lokasi tujuan, Cr Utang Usaha. Bahan yang
    /// langsung masuk dapur didebit ke persediaan dapur, bukan persediaan gudang. Entri null
    /// bila seluruh nilai nol.
    /// </summary>
    public async Task<JournalEntry?> BuildPurchaseReceiptAsync(
        string poNumber, IReadOnlyDictionary<string, long> amountByLocationType, int userId)
    {
        var debitPerAkun = new Dictionary<string, long>();
        foreach (var (jenis, nilai) in amountByLocationType)
        {
            if (nilai <= 0) continue;
            var kode = jenis switch
            {
                "Warehouse" => PersediaanBahanKode,
                "Kitchen" => PersediaanDapurKode,
                _ => throw new InvalidOperationException(
                    $"Jenis lokasi penerimaan tidak dikenal: {jenis}.")
            };
            debitPerAkun[kode] = debitPerAkun.GetValueOrDefault(kode) + nilai;
        }

        var totalAmount = debitPerAkun.Values.Sum();
        if (totalAmount <= 0) return null;

        var utang = await AccountIdAsync(UtangUsahaKode);
        var lines = new List<JournalLine>();
        foreach (var (kode, nilai) in debitPerAkun.OrderBy(k => k.Key, StringComparer.Ordinal))
            lines.Add(new() { AccountId = await AccountIdAsync(kode), DebitAmount = nilai });
        lines.Add(new() { AccountId = utang, CreditAmount = totalAmount });

        return new JournalEntry
        {
            Description = $"Penerimaan Barang PO: {poNumber}",
            TransactionDate = BusinessTime.Now(),
            SourceDocumentType = "GoodsReceipt",
            PostedAt = DateTime.UtcNow,
            PostedByUserId = userId,
            Lines = lines
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
