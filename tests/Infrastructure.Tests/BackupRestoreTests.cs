using FluentAssertions;
using Microsoft.Data.Sqlite;
using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Standar yang diuji di sini: cadangan harus konsisten, terverifikasi, dan bisa dipulihkan.
/// Setiap pengujian memakai berkas SQLite sungguhan, termasuk saat sengaja merusak berkas
/// cadangan — cadangan yang tampak berhasil tetapi tidak bisa dipulihkan dihitung gagal.
/// </summary>
public class BackupRestoreTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly FolderSementara _folder = new();
    private readonly DatabaseBackupService _service = new();

    public void Dispose() => _folder.Dispose();

    private void Pesanan(string nama) =>
        _db.Exec($@"INSERT INTO SalesOrders (CustomerNameSnapshot, Description, PickupDate, TotalAmount,
                                            DownPayment, Quantity, Status, CreatedAt)
                    VALUES ('{nama}', 'Kue', '2031-06-01', 50000, 0, 1, 'Pending', '2031-06-01T08:00:00');");

    private int JumlahPesanan(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SalesOrders;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Fact]
    public void Restore_TetapBerjalanSaatBasisDataBerlakuRusak()
    {
        Pesanan("Ibu Eka");
        var cadangan = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");

        // Basis data yang hancur adalah keadaan yang paling butuh pemulihan; salinan pengaman
        // yang gagal dibuat tidak boleh membuat pemulihan ikut batal.
        SqliteConnection.ClearAllPools();
        for (var i = 0; ; i++)
        {
            try
            {
                using var berkas = new FileStream(_db.FilePath, FileMode.Open, FileAccess.Write, FileShare.None);
                berkas.Seek(0, SeekOrigin.Begin);
                berkas.Write(new byte[2048]);
                break;
            }
            catch (IOException) when (i < 30) { Thread.Sleep(100); }
        }

        var cadanganSebelum = Directory.GetFiles(_folder.Path, "pre-restore-*.db").Length;

        _service.Restore(cadangan, _db.FilePath, _folder.Path);

        JumlahPesanan(_db.FilePath).Should().Be(1);
        DatabaseBackupService.CheckIntegrity(_db.FilePath).Should().Be(DatabaseBackupService.IntegrityOk);
        Directory.GetFiles(_folder.Path, "rusak-*.db").Should().ContainSingle(
            "berkas rusak disalin apa adanya untuk pemeriksaan");
        Directory.GetFiles(_folder.Path, "pre-restore-*.db").Length.Should().Be(cadanganSebelum,
            "cadangan pengaman terverifikasi memang tidak bisa dibuat dari berkas yang hancur");
    }

    [Fact]
    public void Retensi_TidakMenyisakanCadanganRusakSebagaiSalinanTerakhir()
    {
        Pesanan("Ibu Fani");
        var sehat = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");
        var muda = _service.CreateBackup(_db.FilePath, _folder.Path, "manual2");
        File.WriteAllBytes(muda, File.ReadAllBytes(muda).Take(4096).ToArray());

        var kebijakan = new RetentionPolicy(0, 0, 0, 0, MinimalSalinan: 1);
        BackupRetention.Terapkan(_folder.Path, kebijakan);

        var sisa = BackupCatalog.Baca(_folder.Path);
        sisa.Should().ContainSingle();
        sisa[0].Terverifikasi.Should().BeTrue("yang tersisa harus salinan yang terbukti sehat");
        sisa[0].Path.Should().Be(sehat);
        File.Exists(muda).Should().BeFalse("berkas rusak di luar jendela retensi dibuang");
    }

    [Fact]
    public void Cadangan_MembawaManifesChecksumDanHasilPemeriksaanSqlite()
    {
        Pesanan("Ibu Ani");
        Pesanan("Pak Budi");

        var path = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");

        File.Exists(path).Should().BeTrue();
        Directory.GetFiles(_folder.Path, "*.tmp").Should().BeEmpty("berkas sementara tidak boleh tertinggal");
        Directory.GetFiles(_folder.Path, "*.db").Should().ContainSingle();

        var manifes = BackupManifestFile.Baca(path);
        manifes.Should().NotBeNull();
        manifes!.File.Should().Be(Path.GetFileName(path));
        manifes.SizeBytes.Should().Be(new FileInfo(path).Length);
        manifes.Sha256.Should().Be(BackupManifestFile.Sha256(path));
        manifes.Format.Should().Be(BackupManifest.FormatVersion);
        manifes.Kind.Should().Be(BackupKind.Manual);
        manifes.Integrity.Should().Be(DatabaseBackupService.IntegrityOk);
        manifes.ForeignKeys.Should().Be(DatabaseBackupService.ForeignKeysOk);
        manifes.Orders.Should().Be(2);
        manifes.SchemaVersion.Should().Be(DatabaseMigrator.LatestEmbeddedVersion());

        _service.Verify(path).Should().OnlyContain(c => c.Passed,
            "cadangan yang baru dibuat harus lolos seluruh pemeriksaan");
    }

    [Fact]
    public void Cadangan_IkutMembawaIsiWalYangBelumCheckpoint()
    {
        // Koneksi dibiarkan terbuka: baris yang baru ditulis masih berada di -wal, bukan di
        // berkas utama. Salin berkas biasa akan kehilangan baris ini; API backup tidak.
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO SalesOrders (CustomerNameSnapshot, Description, PickupDate, TotalAmount,
                                                    DownPayment, Quantity, Status, CreatedAt)
                            VALUES ('Ibu Citra', 'Kue', '2031-06-01', 75000, 0, 1, 'Pending', '2031-06-01T09:00:00');";
        cmd.ExecuteNonQuery();

        var path = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");

        JumlahPesanan(path).Should().Be(1, "baris yang masih di WAL harus ikut ke cadangan");
    }

    [Fact]
    public void Cadangan_SatuBerkasTanpaSidecar()
    {
        Pesanan("Ibu Dina");

        var path = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");

        Directory.GetFiles(_folder.Path, "*.db-*").Should().BeEmpty(
            "isi WAL sudah menyatu ke berkas utama dan berkas pendamping tidak boleh tertinggal");

        // Pemulihan hanya menyalin satu berkas .db; salinan berkas itu sendirian harus utuh.
        var salinan = Path.Combine(_folder.Path, "salinan.db");
        File.Copy(path, salinan);
        JumlahPesanan(salinan).Should().Be(1);
    }

    [Fact]
    public void Cadangan_BerubahSetelahDibuat_DitolakSaatDiperiksaDanDipulihkan()
    {
        Pesanan("Ibu Diah");
        var path = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Seek(stream.Length / 2, SeekOrigin.Begin);
            var isi = stream.ReadByte();
            stream.Seek(-1, SeekOrigin.Current);
            stream.WriteByte((byte)(isi ^ 0xFF));
        }

        var hasil = _service.Verify(path);
        hasil.Single(c => c.Name == "checksum sha256 sesuai").Passed.Should().BeFalse();

        var dipulihkan = () => _service.Restore(path, _db.FilePath, _folder.Path);
        dipulihkan.Should().Throw<InvalidOperationException>().WithMessage("*checksum*");

        JumlahPesanan(_db.FilePath).Should().Be(1, "basis data berlaku tidak boleh tersentuh saat cadangan ditolak");
    }

    [Fact]
    public void Pemulihan_MengembalikanIsiCadangan_DanMenyimpanSalinanPengaman()
    {
        Pesanan("Ibu Eka");
        var path = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");
        Pesanan("Pak Fajar");

        JumlahPesanan(_db.FilePath).Should().Be(2);

        var pengaman = _service.Restore(path, _db.FilePath, _folder.Path);

        JumlahPesanan(_db.FilePath).Should().Be(1, "isi kembali ke saat cadangan dibuat");
        File.Exists(pengaman).Should().BeTrue();
        BackupManifestFile.Baca(pengaman)!.Kind.Should().Be(BackupKind.PraRestore);
        JumlahPesanan(pengaman).Should().Be(2, "salinan pengaman memuat keadaan sebelum pemulihan");
    }

    [Fact]
    public void CadanganTanpaManifes_TetapBisaDipulihkan()
    {
        Pesanan("Ibu Gita");
        var path = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");
        File.Delete(BackupManifestFile.PathUntuk(path));

        BackupManifestFile.Baca(path).Should().BeNull();
        var pesan = new List<string>();
        _service.Restore(path, _db.FilePath, _folder.Path, pesan.Add);

        JumlahPesanan(_db.FilePath).Should().Be(1);
        pesan.Should().Contain(p => p.Contains("tanpa manifes"),
            "pemulihan cadangan versi lama harus berjalan sambil menyebut keterbatasannya");
    }

    [Fact]
    public void CadanganDariSkemaLebihBaru_DitolakSebelumMenyentuhBasisData()
    {
        Pesanan("Ibu Hana");
        var path = _service.CreateBackup(_db.FilePath, _folder.Path, "manual");
        // Manifes dihapus supaya yang diuji benar-benar pemeriksaan versi skema, bukan checksum.
        File.Delete(BackupManifestFile.PathUntuk(path));
        using (var conn = new SqliteConnection($"Data Source={path};"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO SchemaVersion (Version, AppliedAt) VALUES (999, datetime('now'));";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var dipulihkan = () => _service.Restore(path, _db.FilePath, _folder.Path);
        dipulihkan.Should().Throw<InvalidOperationException>().WithMessage("*lebih baru*");
        JumlahPesanan(_db.FilePath).Should().Be(1);
    }

    [Fact]
    public void Retensi_MenyimpanSalinanTerbaruPraUpgradeDanPraRestore()
    {
        for (var i = 0; i < 3; i++) _service.CreateBackup(_db.FilePath, _folder.Path, $"manual{i}");
        var praUpgrade = _service.CreateBackup(_db.FilePath, _folder.Path, "pre-upgrade-v15-to-v16");
        var praRestore = _service.CreateBackup(_db.FilePath, _folder.Path, "pre-restore");

        var kebijakan = new RetentionPolicy(
            HariSimpanSemua: 0, HariHarian: 1, MingguMingguan: 0, BulanBulanan: 0, MinimalSalinan: 1);
        var keputusan = BackupRetention.Terapkan(_folder.Path, kebijakan);

        File.Exists(praUpgrade).Should().BeTrue("cadangan pra-upgrade adalah jalan pulang ke skema lama");
        File.Exists(praRestore).Should().BeTrue("cadangan pra-restore tidak boleh ikut dihapus");
        keputusan.Dihapus.Should().HaveCount(3);
        keputusan.Dihapus.Should().OnlyContain(n => n.StartsWith("manual"),
            "yang dihapus hanya salinan manual lama, bukan pra-upgrade/pra-restore");
        File.Exists(praRestore).Should().BeTrue("salinan sehat terbaru tetap disimpan meski jendela retensi sempit");

        var log = File.ReadAllText(Path.Combine(_folder.Path, BackupRetention.LogFile));
        log.Should().Contain("di luar jendela retensi", "alasan penghapusan harus tercatat, bukan dihapus diam-diam");
    }

    [Fact]
    public void Retensi_TidakPernahMenyisakanKurangDariBatasMinimum()
    {
        for (var i = 0; i < 5; i++) _service.CreateBackup(_db.FilePath, _folder.Path, $"manual{i}");

        // Semua jendela dimatikan: hanya pagar minimal yang tersisa.
        var kebijakan = new RetentionPolicy(0, 0, 0, 0, MinimalSalinan: 3);
        BackupRetention.Terapkan(_folder.Path, kebijakan);

        BackupCatalog.Baca(_folder.Path).Should().HaveCount(3);
        BackupCatalog.Baca(_folder.Path).Should().OnlyContain(e => e.Terverifikasi);
    }

    [Fact]
    public void Kapasitas_TigaTingkatSesuaiBuktiBerkasDanDisk()
    {
        var keluaran = _folder.Path;
        var longgar = new StorageThresholds(
            DbWarnBytes: long.MaxValue / 4, DbCriticalBytes: long.MaxValue / 2,
            FreeWarnBytes: 0, FreeCriticalBytes: 0, BackupMaxAgeHours: 48);

        StorageMonitor.Evaluate(_db.FilePath, keluaran, keluaran, longgar).Level
            .Should().Be(StorageMonitor.Critical, "belum ada cadangan sama sekali");

        var path = _service.CreateBackup(_db.FilePath, keluaran, "manual");
        var normal = StorageMonitor.Evaluate(_db.FilePath, keluaran, keluaran, longgar);
        normal.Level.Should().Be(StorageMonitor.Normal, "cadangan baru dan disk lapang");
        normal.LastVerifiedBackupUtc.Should().NotBeNull();
        normal.BackupCount.Should().Be(1);
        normal.BackupBytes.Should().Be(new FileInfo(path).Length);

        var ketat = new StorageThresholds(long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue, 48);
        var kritis = StorageMonitor.Evaluate(_db.FilePath, keluaran, keluaran, ketat);
        kritis.Level.Should().Be(StorageMonitor.Critical);
        kritis.Reasons.Should().Contain(r => r.Contains("sisa disk"));
    }

    [Fact]
    public void Kapasitas_CadanganTanpaManifes_HanyaPerhatian()
    {
        var keluaran = _folder.Path;
        var longgar = new StorageThresholds(
            DbWarnBytes: long.MaxValue / 4, DbCriticalBytes: long.MaxValue / 2,
            FreeWarnBytes: 0, FreeCriticalBytes: 0, BackupMaxAgeHours: 48);

        File.Copy(_db.FilePath, System.IO.Path.Combine(keluaran, "cadangan-lama.db"));

        var snapshot = StorageMonitor.Evaluate(_db.FilePath, keluaran, keluaran, longgar);
        snapshot.Level.Should().Be(StorageMonitor.Attention,
            "berkas cadangan ada tetapi belum terbukti utuh, jadi belum layak disebut kritis");
        snapshot.Reasons.Should().Contain(r => r.Contains("belum ada yang terverifikasi"));
    }

    [Fact]
    public void FolderTujuanSalinan_MenolakPathRelatifTitikDuaTitikDanFolderCadangan()
    {
        var cadangan = System.IO.Path.Combine(_folder.Path, "cadangan");

        FluentActions.Invoking(() => BackupDestination.Validasi(" ", cadangan))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BackupDestination.Validasi("hasil-salinan", cadangan))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => BackupDestination.Validasi(System.IO.Path.Combine(_folder.Path, "..", "luar"), cadangan))
            .Should().Throw<ArgumentException>("normalisasi path tidak boleh menyamarkan '..'");
        FluentActions.Invoking(() => BackupDestination.Validasi(cadangan, cadangan))
            .Should().Throw<ArgumentException>("salinan ke folder cadangan internal tidak ada gunanya");
        FluentActions.Invoking(() => BackupDestination.Validasi(System.IO.Path.Combine(_folder.Path, "flash"), cadangan))
            .Should().NotThrow();
    }

    [Fact]
    public void LabelCadangan_MenentukanJenisYangDiperlakukanKhususRetensi()
    {
        BackupKind.DariLabel("pre-upgrade-v16-to-v17").Should().Be(BackupKind.PraUpgrade);
        BackupKind.DariLabel("pre-restore").Should().Be(BackupKind.PraRestore);
        BackupKind.DariLabel("auto").Should().Be(BackupKind.Otomatis);
        BackupKind.DariLabel("manual").Should().Be(BackupKind.Manual);
    }
}

/// <summary>Folder sementara untuk cadangan; dihapus setelah pengujian selesai.</summary>
internal sealed class FolderSementara : IDisposable
{
    public string Path { get; }

    public FolderSementara()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"seruni-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
