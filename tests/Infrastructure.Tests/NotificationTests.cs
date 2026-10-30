using FluentAssertions;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Notifikasi internal harus bisa ditelusuri dan tidak bocor antar pengguna. Pengujian di sini
/// memakai basis data hasil migrasi sungguhan, bukan tiruan, sehingga kunci dedupe, batasan
/// peran, dan penyimpanan permanennya diuji apa adanya.
/// </summary>
internal sealed class NotificationFixture : IDisposable
{
    public TempDatabase Db { get; } = new();
    public DbConnectionFactory Factory { get; }
    public NotificationRepository Repo { get; }
    public NotificationService Service { get; }

    public NotificationFixture()
    {
        Factory = new DbConnectionFactory(Db.ConnectionString);
        var users = new UserRepository(Factory);
        var ingredients = new IngredientRepository(Factory);
        var batches = new BatchRepository(Factory);
        var orders = new SalesOrderRepository(Factory);
        Repo = new NotificationRepository(Factory);
        var analytics = new AnalyticsService(new JournalRepository(Factory), orders);
        var inbox = new TaskInboxService(
            orders, new ProductionRepository(Factory), new PurchaseOrderRepository(Factory),
            ingredients, batches);
        Service = new NotificationService(Repo, users, ingredients, batches, orders, analytics, inbox);
    }

    public int Pengguna(string username, string role, bool aktif = true) =>
        TambahPengguna(Db, username, role, aktif);

    public static int TambahPengguna(TempDatabase db, string username, string role, bool aktif = true)
    {
        db.Exec("INSERT INTO Users (Username, PasswordHash, Role, IsActive, CreatedAt, TokenVersion) " +
                $"VALUES ('{username}', 'x', '{role}', {(aktif ? 1 : 0)}, '2026-10-28T00:00:00Z', 0);");
        return Convert.ToInt32(db.Scalar($"SELECT Id FROM Users WHERE Username = '{username}';"));
    }

    public static Notification Isi(int penerima, string eventType = "transaction.order.created",
        NotificationSeverity severity = NotificationSeverity.Info, string? dedupe = null) => new()
    {
        EventType = eventType,
        Severity = severity,
        Title = "Judul " + eventType,
        Body = "Isi pesan",
        RecipientUserId = penerima,
        DedupeKey = dedupe ?? $"{eventType}:{Guid.NewGuid():N}",
        CreatedAt = DateTime.UtcNow
    };

    public void Dispose() => Db.Dispose();
}

public class NotificationRepositoryTests
{
    [Fact]
    public async Task Simpan_KunciDedupeSama_TidakMenumpuk()
    {
        using var f = new NotificationFixture();
        var user = f.Pengguna("pemilik", "Owner");

        var pertama = await f.Repo.CreateAsync(NotificationFixture.Isi(user, dedupe: "stock.low:1:2026-10-28"));
        var kedua = await f.Repo.CreateAsync(NotificationFixture.Isi(user, dedupe: "stock.low:1:2026-10-28"));

        pertama.Should().BeGreaterThan(0);
        kedua.Should().Be(0, "penyegaran berulang tidak boleh menggandakan pengingat yang sama");
        f.Db.Count("Notifications").Should().Be(1);
    }

    [Fact]
    public async Task Daftar_HanyaMemuatKotakPenggunaSendiri()
    {
        using var f = new NotificationFixture();
        var owner = f.Pengguna("pemilik", "Owner");
        var kasir = f.Pengguna("kasir", "Pegawai");
        await f.Repo.CreateAsync(NotificationFixture.Isi(owner));
        await f.Repo.CreateAsync(NotificationFixture.Isi(owner));
        await f.Repo.CreateAsync(NotificationFixture.Isi(kasir));

        var kotakOwner = await f.Repo.ListAsync(owner, unreadOnly: false, limit: 50);
        var kotakKasir = await f.Repo.ListAsync(kasir, unreadOnly: false, limit: 50);

        kotakOwner.Should().HaveCount(2).And.OnlyContain(n => n.RecipientUserId == owner);
        kotakKasir.Should().ContainSingle().Which.RecipientUserId.Should().Be(kasir);
    }

    [Fact]
    public async Task Ringkasan_MenghitungTotalBelumDibacaDanKritis()
    {
        using var f = new NotificationFixture();
        var user = f.Pengguna("pemilik", "Owner");
        await f.Repo.CreateAsync(NotificationFixture.Isi(user, severity: NotificationSeverity.Info));
        await f.Repo.CreateAsync(NotificationFixture.Isi(user, severity: NotificationSeverity.Critical));
        await f.Repo.CreateAsync(NotificationFixture.Isi(user, severity: NotificationSeverity.Critical));

        var ringkas = await f.Repo.SummaryAsync(user);

        ringkas.Total.Should().Be(3);
        ringkas.Unread.Should().Be(3);
        ringkas.CriticalUnread.Should().Be(2);
    }

    [Fact]
    public async Task TandaiDibaca_TidakBisaMenyentuhKotakPenggunaLain()
    {
        using var f = new NotificationFixture();
        var owner = f.Pengguna("pemilik", "Owner");
        var kasir = f.Pengguna("kasir", "Pegawai");
        var id = await f.Repo.CreateAsync(NotificationFixture.Isi(owner));

        (await f.Repo.MarkReadAsync(id, kasir, DateTime.UtcNow)).Should().BeFalse();
        (await f.Repo.MarkReadAsync(id, owner, DateTime.UtcNow)).Should().BeTrue();
        (await f.Repo.SummaryAsync(owner)).Unread.Should().Be(0);
        (await f.Repo.SummaryAsync(kasir)).Total.Should().Be(0);
    }

    [Fact]
    public async Task TandaiSemuaDibaca_DanFilterBelumDibaca()
    {
        using var f = new NotificationFixture();
        var user = f.Pengguna("pemilik", "Owner");
        await f.Repo.CreateAsync(NotificationFixture.Isi(user));
        await f.Repo.CreateAsync(NotificationFixture.Isi(user));

        var ditandai = await f.Repo.MarkAllReadAsync(user, DateTime.UtcNow);

        ditandai.Should().Be(2);
        (await f.Repo.ListAsync(user, unreadOnly: true, limit: 50)).Should().BeEmpty();
        (await f.Repo.ListAsync(user, unreadOnly: false, limit: 50)).Should().HaveCount(2)
            .And.OnlyContain(n => n.IsRead && n.ReadAt != null);
    }

    [Fact]
    public async Task TersimpanPermanen_SetelahKoneksiDibukaUlang()
    {
        using var f = new NotificationFixture();
        var user = f.Pengguna("pemilik", "Owner");
        var id = await f.Repo.CreateAsync(NotificationFixture.Isi(
            user, "transaction.qc.failed", NotificationSeverity.Critical, "qc:7"));

        // Membuka koneksi baru menirukan aplikasi yang ditutup lalu dijalankan lagi.
        var repoBaru = new NotificationRepository(new DbConnectionFactory(f.Db.ConnectionString));
        var tersimpan = await repoBaru.ListAsync(user, unreadOnly: false, limit: 50);

        tersimpan.Should().ContainSingle();
        tersimpan[0].Id.Should().Be(id);
        tersimpan[0].EventType.Should().Be("transaction.qc.failed");
        tersimpan[0].Severity.Should().Be(NotificationSeverity.Critical);
        tersimpan[0].DedupeKey.Should().Be("qc:7");
    }
}

public class NotificationServiceTests
{
    private static string HariIni => BusinessTime.Today().ToString("yyyy-MM-dd");

    private static void TambahBahanMenipis(TempDatabase db, int minStock = 10)
    {
        db.Exec("INSERT INTO Ingredients (Name, Unit, PricePerUnit, MinStockAlert, IsActive) " +
                $"VALUES ('Tepung', 'kg', 12000, {minStock}, 1);");
    }

    [Fact]
    public async Task Sinkron_StokMenipis_HanyaUntukPeranGudang_SekaliSehari()
    {
        using var f = new NotificationFixture();
        f.Pengguna("pemilik", "Owner");
        f.Pengguna("manajer", "Admin");
        f.Pengguna("koki", "Dapur");
        f.Pengguna("kasir", "Pegawai");
        TambahBahanMenipis(f.Db);

        var pertama = await f.Service.SinkronkanOperasionalAsync(DateTime.UtcNow);

        pertama.Should().BeGreaterThan(0);
        // Penerima adalah semua akun aktif berperan Owner/Admin/Dapur, termasuk akun bawaan migrasi.
        var penerimaStok = Convert.ToInt32(f.Db.Scalar(
            "SELECT COUNT(*) FROM Users WHERE IsActive = 1 AND Role IN ('Owner','Admin','Dapur');"));
        f.Db.Count("Notifications", "EventType = 'stock.low'").Should().Be(penerimaStok);
        f.Db.Count("Notifications n JOIN Users u ON u.Id = n.RecipientUserId",
            "n.EventType = 'stock.low' AND u.Role = 'Pegawai'").Should().Be(0);

        var kedua = await f.Service.SinkronkanOperasionalAsync(DateTime.UtcNow);

        kedua.Should().Be(0, "kunci dedupe memuat tanggal bisnis dan penerima");
        f.Db.Count("Notifications", "EventType = 'stock.low'").Should().Be(penerimaStok);
    }

    [Fact]
    public async Task Sinkron_BatchKedaluwarsa_MenjadiKritis()
    {
        using var f = new NotificationFixture();
        var owner = f.Pengguna("pemilik", "Owner");
        TambahBahanMenipis(f.Db, minStock: 0);
        var bahan = Convert.ToInt32(f.Db.Scalar("SELECT Id FROM Ingredients WHERE Name = 'Tepung';"));
        var kedaluwarsa = BusinessTime.Today().AddDays(-2).ToString("yyyy-MM-dd");
        var diterima = BusinessTime.Today().AddDays(-10).ToString("yyyy-MM-dd");
        f.Db.Exec("INSERT INTO ItemBatches (IngredientId, BatchNumber, ExpiryDate, ReceivedDate) " +
                  $"VALUES ({bahan}, 'LOT-001', '{kedaluwarsa}', '{diterima}');");

        await f.Service.SinkronkanOperasionalAsync(DateTime.UtcNow);

        var batch = (await f.Repo.ListAsync(owner, unreadOnly: false, limit: 50))
            .Single(n => n.EventType == "batch.expiry");
        batch.Severity.Should().Be(NotificationSeverity.Critical);
        batch.DedupeKey.Should().Be($"batch.expiry:1:{HariIni}:u{owner}");
    }

    [Fact]
    public async Task Sinkron_RingkasanHarian_Kemarin_HanyaUntukPemilikDanAdmin()
    {
        using var f = new NotificationFixture();
        var owner = f.Pengguna("pemilik", "Owner");
        f.Pengguna("manajer", "Admin");
        f.Pengguna("koki", "Dapur");
        var kemarin = BusinessTime.Today().AddDays(-1).ToString("yyyy-MM-dd");
        f.Db.Exec("INSERT INTO SalesOrders (OrderNumber, CustomerNameSnapshot, Description, PickupDate, TotalAmount, Quantity, Status, CreatedAt) " +
                  $"VALUES ('ORD-001', 'Ibu Ani', 'Nastar', '{kemarin}', 150000, 2, 'Paid', '{kemarin} 10:00:00');");

        await f.Service.SinkronkanOperasionalAsync(DateTime.UtcNow);

        var laporan = (await f.Repo.ListAsync(owner, unreadOnly: false, limit: 50))
            .Single(n => n.EventType == "report.daily");
        laporan.DedupeKey.Should().Be($"report.daily:{kemarin}:u{owner}");
        laporan.Body.Should().Contain("Pesanan 1");
        var penerimaLaporan = Convert.ToInt32(f.Db.Scalar(
            "SELECT COUNT(*) FROM Users WHERE IsActive = 1 AND Role IN ('Owner','Admin');"));
        f.Db.Count("Notifications", "EventType = 'report.daily'").Should().Be(penerimaLaporan);
        f.Db.Count("Notifications n JOIN Users u ON u.Id = n.RecipientUserId",
            "n.EventType = 'report.daily' AND u.Role = 'Dapur'").Should().Be(0);
    }

    [Fact]
    public async Task PenggunaNonaktif_TidakMenerimaPengingat()
    {
        using var f = new NotificationFixture();
        var owner = f.Pengguna("pemilik", "Owner");
        var admin = f.Pengguna("manajer", "Admin", aktif: false);
        TambahBahanMenipis(f.Db);

        await f.Service.SinkronkanOperasionalAsync(DateTime.UtcNow);

        (await f.Repo.ListAsync(owner, unreadOnly: false, limit: 50))
            .Should().Contain(n => n.EventType == "stock.low");
        (await f.Repo.ListAsync(admin, unreadOnly: false, limit: 50)).Should().BeEmpty(
            "akun nonaktif tidak boleh menerima pengingat baru");
    }
}
