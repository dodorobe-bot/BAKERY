using FluentAssertions;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Kunci idempotensi hanya berguna kalau tiga keadaannya dibedakan dengan benar: belum pernah
/// dipakai, sedang dikerjakan, dan sudah selesai. Kekeliruan di sini berakibat dokumen kembar
/// (dianggap belum ada) atau pengguna terkunci tidak bisa menyimpan ulang (gagal tapi kunci tertinggal).
/// </summary>
public class IdempotencyRepositoryTests
{
    private static IIdempotencyRepository Repo(TempDatabase db) =>
        new IdempotencyRepository(new DbConnectionFactory(db.ConnectionString));

    [Fact]
    public async Task KunciBaru_DipesanSekali_SisanyaDitolak()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);

        (await repo.ReserveAsync("kunci-pesanan-1", "POST /api/orders", 1)).Should().BeTrue();
        (await repo.ReserveAsync("kunci-pesanan-1", "POST /api/orders", 1)).Should().BeFalse();

        var ada = await repo.GetAsync("kunci-pesanan-1");
        ada!.Selesai.Should().BeFalse("pemesanan belum ditutup, jadi belum ada hasilnya");
        ada.Route.Should().Be("POST /api/orders");
        ada.UserId.Should().Be(1);
    }

    [Fact]
    public async Task AksiBerhasil_KunciMenutupDanMenyimpanIdDokumen()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        await repo.ReserveAsync("kunci-tertutup-1", "POST /api/expenses", null);

        await repo.CompleteAsync("kunci-tertutup-1", 41);

        var ada = await repo.GetAsync("kunci-tertutup-1");
        ada!.Selesai.Should().BeTrue();
        ada.ResourceId.Should().Be(41);
    }

    [Fact]
    public async Task AksiGagal_KunciDilepasPemilikBolehMencobaLagi()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        await repo.ReserveAsync("kunci-gagal-1", "POST /api/purchases", null);

        await repo.ReleaseAsync("kunci-gagal-1");

        (await repo.GetAsync("kunci-gagal-1")).Should().BeNull();
        (await repo.ReserveAsync("kunci-gagal-1", "POST /api/purchases", null)).Should().BeTrue();
    }

    [Fact]
    public async Task AksiSudahSelesai_PelepasanTidakMenghapusKunci()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        await repo.ReserveAsync("kunci-jadi-1", "POST /api/orders", null);
        await repo.CompleteAsync("kunci-jadi-1", 7);

        // Melepaskan kunci yang sudah selesai harus mustahil: kalau bisa, permintaan ulang
        // setelah respons pertama justru membuka jalan bagi pesanan kedua.
        await repo.ReleaseAsync("kunci-jadi-1");

        (await repo.GetAsync("kunci-jadi-1"))!.ResourceId.Should().Be(7);
    }

    [Fact]
    public async Task KunciBelumPernahDipakai_BacaMengembalikanNull()
    {
        using var db = new TempDatabase();
        (await Repo(db).GetAsync("kunci-tak-terdaftar")).Should().BeNull();
    }
}
