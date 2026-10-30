using FluentAssertions;
using Seruni22Bakery.Host;

namespace BakeryDesktop.UI.Tests;

/// <summary>
/// Provider konfigurasi baris perintah menelan token sesudah saklar tanpa nilai, sehingga
/// "--backup --Database:Path=..." dulu diam-diam memakai basis data bawaan. Pengurai langsung
/// ini menutup celah tersebut; tesnya menjaga agar perilaku itu tidak kembali.
/// </summary>
public class MaintenanceArgumentsTests
{
    [Fact]
    public void PathBasisData_DibacaWalauTepatSetelahSaklarTanpaNilai()
    {
        MaintenanceCommands.PathBasisDataDariArgumen(
                new[] { "--backup", @"--Database:Path=C:\data\uji.db" })
            .Should().Be(@"C:\data\uji.db");

        MaintenanceCommands.PathBasisDataDariArgumen(
                new[] { "--backup", "--urls", "http://127.0.0.1:5000", @"--Database:Path=C:\data\b.db" })
            .Should().Be(@"C:\data\b.db");
    }

    [Fact]
    public void PathBasisData_KosongAtauTidakAda_MengembalikanNull()
    {
        MaintenanceCommands.PathBasisDataDariArgumen(new[] { "--backup" }).Should().BeNull();
        MaintenanceCommands.PathBasisDataDariArgumen(new[] { "--Database:Path=" }).Should().BeNull();
        MaintenanceCommands.PathBasisDataDariArgumen(new[] { "--Database:Path=   " }).Should().BeNull();
    }

    [Fact]
    public void PathBasisData_MenerimaHurufKecilDanTandaKutip()
    {
        MaintenanceCommands.PathBasisDataDariArgumen(
                new[] { "--database:path=\"C:\\Program Files\\Seruni 22\\data.db\"" })
            .Should().Be(@"C:\Program Files\Seruni 22\data.db");
    }
}
