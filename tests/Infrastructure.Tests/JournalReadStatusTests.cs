using FluentAssertions;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Semua jalur baca buku besar harus memakai kumpulan entri yang sama. Kalau satu jalur menyaring
/// status dan jalur lain tidak, layar jurnal bisa menunjukkan angka yang tidak sama dengan ekspor
/// atau saldo akun pada hari yang sama.
/// </summary>
public class JournalReadStatusTests
{
    private static IJournalRepository Repo(TempDatabase db) =>
        new JournalRepository(new DbConnectionFactory(db.ConnectionString));

    private static async Task<int> EntriAsync(
        TempDatabase db, string status, string description, string sourceType, int sourceId)
    {
        var repo = Repo(db);
        var entry = new JournalEntry
        {
            Description = description,
            TransactionDate = new DateTime(2030, 3, 3),
            Status = Enum.Parse<JournalStatus>(status),
            SourceDocumentType = sourceType,
            SourceDocumentId = sourceId,
            Lines =
            {
                new() { AccountId = 1, DebitAmount = 250_000 },
                new() { AccountId = 4, CreditAmount = 250_000 },
            }
        };
        return await repo.CreateAsync(entry);
    }

    [Fact]
    public async Task EntriDraft_TidakMunculDiSemuaJalurBaca()
    {
        using var db = new TempDatabase();
        var posted = await EntriAsync(db, "Posted", "Entri jadi", "SalesOrder", 12);
        var draft = await EntriAsync(db, "Draft", "Entri konsep", "SalesOrder", 12);

        var repo = Repo(db);
        var postedSaja = new[] { posted };
        draft.Should().NotBe(posted, "konsep dan entri jadi adalah dua baris berbeda");

        var terkini = await repo.GetRecentAsync(50);
        terkini.Select(e => e.Id).Should().BeEquivalentTo(postedSaja,
            "jalur buku besar terbaru hanya menampilkan entri Posted");

        var rentang = await repo.GetByDateRangeAsync(new DateTime(2030, 1, 1), new DateTime(2030, 12, 31));
        rentang.Select(e => e.Id).Should().BeEquivalentTo(postedSaja,
            "rentang tanggal memakai aturan status yang sama");

        var perDokumen = await repo.GetBySourceDocumentAsync("SalesOrder", 12);
        perDokumen.Select(e => e.Id).Should().BeEquivalentTo(postedSaja,
            "penelusuran sumber dokumen tidak boleh membocorkan konsep yang belum posted");
    }

    [Fact]
    public async Task SaldoAkun_MengabaikanEntriDraft()
    {
        using var db = new TempDatabase();
        await EntriAsync(db, "Posted", "Kas masuk", "SalesOrder", 21);
        await EntriAsync(db, "Draft", "Konsep yang belum seharusnya menggeser saldo", "SalesOrder", 22);

        var saldo = await Repo(db).GetBalancesAsync();

        var kas = saldo.Single(a => a.AccountId == 1);
        kas.Balance.Should().Be(250_000, "draft tidak boleh ikut mengubah saldo yang dilaporkan");
    }

    [Fact]
    public async Task SaldoAkun_EntriDibalikDanPembaliknya_SalingMeniadakan()
    {
        // Pembatalan menandai entri asli Reversed lalu menulis entri pembalik. Saldo harus
        // menghitung keduanya; membuang entri asli saja membuat setiap pembatalan menggeser
        // saldo sebesar nilai dokumennya (kas minus, pendapatan minus).
        using var db = new TempDatabase();
        var asli = await EntriAsync(db, "Posted", "Kas masuk", "SalesOrder", 31);

        var repo = Repo(db);
        await repo.UpdateStatusAsync(asli, JournalStatus.Reversed);
        await repo.CreateAsync(new JournalEntry
        {
            Description = "Pembalik: Kas masuk",
            TransactionDate = new DateTime(2030, 3, 4),
            Status = JournalStatus.Posted,
            SourceDocumentType = "SalesOrder",
            SourceDocumentId = 31,
            Lines =
            {
                new() { AccountId = 4, DebitAmount = 250_000 },
                new() { AccountId = 1, CreditAmount = 250_000 },
            }
        });

        var saldo = await repo.GetBalancesAsync();
        saldo.Single(a => a.AccountId == 1).Balance.Should().Be(0,
            "kas kembali ke nol setelah pembatalan, bukan minus nilai dokumen");
        saldo.Single(a => a.AccountId == 4).Balance.Should().Be(0,
            "pendapatan yang dibatalkan tidak boleh tetap diakui");
    }
}
