using FluentAssertions;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;
using Xunit;

namespace BakeryDesktop.Infrastructure.Tests;

public class DocumentSequenceTests
{
    private readonly TempDatabase _db = new();
    private readonly DocumentSequenceRepository _sequences;

    public DocumentSequenceTests() =>
        _sequences = new DocumentSequenceRepository(new DbConnectionFactory(_db.ConnectionString));

    private static readonly DateTime Hari = new(2026, 10, 27, 8, 0, 0);

    [Fact]
    public async Task NomorBertambahSatuPerPanggilan()
    {
        var pertama = await _sequences.NextNumberAsync(DocumentKind.Order, Hari);
        var kedua = await _sequences.NextNumberAsync(DocumentKind.Order, Hari);
        var ketiga = await _sequences.NextNumberAsync(DocumentKind.Order, Hari);

        pertama.Should().Be("ORD-20261027-0001");
        kedua.Should().Be("ORD-20261027-0002");
        ketiga.Should().Be("ORD-20261027-0003");
    }

    [Fact]
    public async Task SetiapJenisDokumenPunyaPenghitungSendiri()
    {
        (await _sequences.NextNumberAsync(DocumentKind.Order, Hari)).Should().Be("ORD-20261027-0001");
        (await _sequences.NextNumberAsync(DocumentKind.WorkOrder, Hari)).Should().Be("SPK-20261027-0001");
        (await _sequences.NextNumberAsync(DocumentKind.PurchaseOrder, Hari)).Should().Be("PO-20261027-0001");
        (await _sequences.NextNumberAsync(DocumentKind.Order, Hari)).Should().Be("ORD-20261027-0002");
    }

    [Fact]
    public async Task GantiHariMulaiDariUrutanSatuLagi()
    {
        await _sequences.NextNumberAsync(DocumentKind.Order, Hari);
        await _sequences.NextNumberAsync(DocumentKind.Order, Hari);

        var besok = Hari.AddDays(1);
        (await _sequences.NextNumberAsync(DocumentKind.Order, besok)).Should().Be("ORD-20261028-0001");

        _db.Count("DocumentSequences").Should().Be(2);
    }

    [Fact]
    public async Task PenulisanBersamaanTidakPernahMemakaiNomorYangSama()
    {
        // Inilah galat lama: nomor dari Random.Shared + detik sehingga tabrakan UNIQUE jadi 500.
        var lomba = Enumerable.Range(0, 40)
            .Select(i => _sequences.NextNumberAsync(DocumentKind.Order, Hari))
            .ToArray();
        var nomor = await Task.WhenAll(lomba);

        nomor.Should().OnlyHaveUniqueItems();
        nomor.Select(n => long.Parse(n[^4..])).Should().BeInAscendingOrder()
            .And.HaveCount(40);
        _db.Scalar("SELECT LastValue FROM DocumentSequences WHERE Kind='Order';").Should().Be(40L);
    }
}
