namespace Seruni22Bakery.Domain.Entities;

/// <summary>
/// Ringkasan piutang yang dihitung di SQL. Dashboard dan kotak tugas hanya memerlukan angka
/// ini, jadi tidak perlu memuat ribuan pesanan beserta pembayarannya ke memori.
/// </summary>
public record OutstandingSummary(int Jumlah, long Sisa);

/// <summary>
/// Jumlah pekerjaan bertenggat pada dua keranjang: yang sudah lewat tenggat dan yang belum.
/// Kotak tugas memakai angka ini apa adanya, sehingga jumlah yang ditampilkan tetap tepat
/// walaupun daftar barisnya dibatasi untuk layar.
/// </summary>
public record DeadlineCounts(int Overdue, int Upcoming);

/// <summary>Satu hari penjualan untuk grafik analitik: jumlah pesanan, nilai, dan jumlah batal.</summary>
public record DailySalesSummary(DateTime Tanggal, int JumlahPesanan, int JumlahBatal, decimal Kuantitas, long Nilai);

/// <summary>Rekap penjualan per produk untuk daftar produk terlaris.</summary>
public record ProductSalesSummary(string Nama, int JumlahPesanan, decimal Kuantitas, long Nilai);

/// <summary>Arus kas satu hari pada akun kas &amp; bank.</summary>
public record CashFlowSummary(long Masuk, long Keluar);

/// <summary>Pendapatan dan beban satu hari dari jurnal posted, dihitung di SQL.</summary>
public record DailyJournalTotals(DateTime Tanggal, long Pendapatan, long Beban);

/// <summary>Total beban per akun pada satu rentang, untuk rincian analitik.</summary>
public record AccountExpenseTotal(string Code, string Name, long Total);

/// <summary>
/// Debit, kredit, dan jumlah kaki jurnal satu akun pada satu rentang. Layar jurnal hanya memuat
/// sebagian entri untuk ditampilkan, tetapi laporan laba rugi dan saldo periode harus tepat untuk
/// seluruh rentang — angka itu datang dari sini, bukan dari baris yang kebetulan termuat.
/// </summary>
public record AccountPeriodTotal(
    int AccountId, string Code, string Name, string Type, long Debit, long Credit, int Legs);

/// <summary>Ringkasan satu rentang jurnal: jumlah entri/kaki, total per akun, dan sumber dokumen yang muncul.</summary>
public record JournalRangeSummary(
    int Entries, int Legs, IReadOnlyList<AccountPeriodTotal> Accounts, IReadOnlyList<string> Sources);
