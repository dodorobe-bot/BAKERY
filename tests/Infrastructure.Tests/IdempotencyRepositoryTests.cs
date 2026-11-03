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

    [Fact]
    public async Task PesananBasi_BolehDiambilAlihSetelahBatas()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        await repo.ReserveAsync("kunci-macet-1", "POST /api/orders", 1);
        // Meniru proses yang mati setelah memesan kunci tetapi sebelum menutupnya.
        db.Exec("UPDATE IdempotencyKeys SET ReservedAt = datetime('now', '-31 minutes') " +
                "WHERE Key = 'kunci-macet-1';");

        (await repo.GetAsync("kunci-macet-1")).Should().BeNull(
            "pesanan basi bukan aksi yang masih berjalan");

        (await repo.ReserveAsync("kunci-macet-1", "POST /api/orders", 1)).Should().BeTrue(
            "pengguna harus bisa mencoba lagi tanpa mengganti kunci");
        var baru = await repo.GetAsync("kunci-macet-1");
        baru!.Selesai.Should().BeFalse();
        baru.Route.Should().Be("POST /api/orders");
    }

    [Fact]
    public async Task PesananDalamBatasWaktu_TetapMenolakPercobaanKedua()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        await repo.ReserveAsync("kunci-berjalan-1", "POST /api/purchases", null);
        db.Exec("UPDATE IdempotencyKeys SET ReservedAt = datetime('now', '-29 minutes') " +
                "WHERE Key = 'kunci-berjalan-1';");

        (await repo.GetAsync("kunci-berjalan-1")).Should().NotBeNull(
            "permintaan yang masih dalam batas waktu dianggap sedang diproses");
        (await repo.ReserveAsync("kunci-berjalan-1", "POST /api/purchases", null)).Should().BeFalse();
    }

    [Fact]
    public async Task KunciSelesai_LewatMasaSimpan_DibersihkanSaatKunciBaruDipesan()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        await repo.ReserveAsync("kunci-lama-1", "POST /api/orders", null);
        await repo.CompleteAsync("kunci-lama-1", 11);
        db.Exec("UPDATE IdempotencyKeys SET ReservedAt = datetime('now', '-31 days') " +
                "WHERE Key = 'kunci-lama-1';");

        (await repo.ReserveAsync("kunci-baru-1", "POST /api/orders", null)).Should().BeTrue();

        (await repo.GetAsync("kunci-lama-1")).Should().BeNull(
            "kunci selesai yang sudah lewat masa simpan tidak boleh menumpuk selamanya");
    }

    [Fact]
    public async Task KunciSelesai_DalamMasaSimpan_TetapDihormati()
    {
        using var db = new TempDatabase();
        var repo = Repo(db);
        await repo.ReserveAsync("kunci-masih-1", "POST /api/orders", null);
        await repo.CompleteAsync("kunci-masih-1", 12);
        db.Exec("UPDATE IdempotencyKeys SET ReservedAt = datetime('now', '-29 days') " +
                "WHERE Key = 'kunci-masih-1';");

        (await repo.ReserveAsync("kunci-lain-1", "POST /api/orders", null)).Should().BeTrue();

        var ada = await repo.GetAsync("kunci-masih-1");
        ada!.Selesai.Should().BeTrue("pengulangan dalam masa retry wajar harus tetap dijawab sama");
        ada.ResourceId.Should().Be(12);
    }
}
