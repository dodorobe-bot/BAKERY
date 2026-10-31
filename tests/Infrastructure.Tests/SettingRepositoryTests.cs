using FluentAssertions;
using Microsoft.Data.Sqlite;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Layar Pengaturan menyimpan banyak kunci sekaligus. Menyimpan satu per satu berarti kegagalan
/// di kunci kedua meninggalkan separuh nilai baru sudah berlaku; tes ini menuntut satu transaksi.
/// </summary>
public class SettingRepositoryTests
{
    [Fact]
    public async Task SaveManyAsync_MenyimpanSemuaKunci()
    {
        using var db = new TempDatabase();
        var repo = new SettingRepository(new DbConnectionFactory(db.ConnectionString));

        await repo.SaveManyAsync(new Dictionary<string, string>
        {
            ["outlet_name"] = "Toko Uji",
            ["timezone"] = "Asia/Jakarta",
        }, userId: 1);

        (await repo.GetAsync("outlet_name")).Should().Be("Toko Uji");
        (await repo.GetAsync("timezone")).Should().Be("Asia/Jakarta");
    }

    [Fact]
    public async Task SaveManyAsync_GagalDiTengah_JanganMenyisakanKunciLain()
    {
        using var db = new TempDatabase();
        db.Exec(@"
            CREATE TRIGGER uji_tolak_pengaturan BEFORE INSERT ON AppSettings
            WHEN NEW.Key = 'gagal'
            BEGIN
                SELECT RAISE(ABORT, 'uji: kunci kedua ditolak');
            END;");
        var repo = new SettingRepository(new DbConnectionFactory(db.ConnectionString));

        var aksi = async () => await repo.SaveManyAsync(new Dictionary<string, string>
        {
            ["sebelum"] = "nilai-baru",
            ["gagal"] = "nilai-baru",
        }, userId: 1);

        await aksi.Should().ThrowAsync<SqliteException>();
        // Urutan penulisan kamus bisa berbeda; yang penting tidak satu pun dari kedua kunci itu
        // tersimpan, karena transaksinya harus dibatalkan seluruhnya.
        (await repo.GetAsync("sebelum")).Should().BeNull();
        (await repo.GetAsync("gagal")).Should().BeNull();
    }
}
