using FluentAssertions;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Xunit;

namespace BakeryDesktop.Domain.Tests;

public class DocumentNumberTests
{
    private static readonly DateTime Hari = new(2026, 10, 27, 13, 45, 0);

    [Theory]
    [InlineData(DocumentKind.Order, "ORD")]
    [InlineData(DocumentKind.WorkOrder, "SPK")]
    [InlineData(DocumentKind.PurchaseOrder, "PO")]
    public void Prefix_MengikutiJenisDokumen(DocumentKind kind, string expected) =>
        DocumentNumber.Prefix(kind).Should().Be(expected);

    [Fact]
    public void Format_MemakaiTanggalUtcDanUrutanEmpatDigit()
    {
        DocumentNumber.Format(DocumentKind.Order, Hari, 1).Should().Be("ORD-20261027-0001");
        DocumentNumber.Format(DocumentKind.WorkOrder, Hari, 42).Should().Be("SPK-20261027-0042");
        DocumentNumber.Format(DocumentKind.PurchaseOrder, Hari, 9999).Should().Be("PO-20261027-9999");
    }

    [Fact]
    public void Format_MembuangJamMenit_SehinggaSatuHariSatuBlokNomor()
    {
        var pagi = new DateTime(2026, 10, 27, 0, 0, 1);
        var malam = new DateTime(2026, 10, 27, 23, 59, 59);

        DocumentNumber.Format(DocumentKind.Order, pagi, 7)
            .Should().Be(DocumentNumber.Format(DocumentKind.Order, malam, 7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Format_MenolakUrutanNolAtauNegatif(long sequence)
    {
        var act = () => DocumentNumber.Format(DocumentKind.Order, Hari, sequence);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class RolePoliciesTests
{
    private static readonly UserRole[] SemuaPeran = [UserRole.Owner, UserRole.Admin, UserRole.Dapur, UserRole.Pegawai];

    [Theory]
    [InlineData(RolePolicies.Sales, UserRole.Owner, true)]
    [InlineData(RolePolicies.Sales, UserRole.Admin, true)]
    [InlineData(RolePolicies.Sales, UserRole.Pegawai, true)]
    [InlineData(RolePolicies.Sales, UserRole.Dapur, false)]
    [InlineData(RolePolicies.SalesCancel, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.SalesCancel, UserRole.Dapur, false)]
    [InlineData(RolePolicies.SalesCancel, UserRole.Admin, true)]
    [InlineData(RolePolicies.SalesReturn, UserRole.Admin, true)]
    [InlineData(RolePolicies.SalesReturn, UserRole.Owner, true)]
    [InlineData(RolePolicies.SalesReturn, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.SalesReturn, UserRole.Dapur, false)]
    [InlineData(RolePolicies.FinanceRead, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.FinanceRead, UserRole.Dapur, false)]
    [InlineData(RolePolicies.FinanceWrite, UserRole.Dapur, false)]
    [InlineData(RolePolicies.FinanceWrite, UserRole.Admin, true)]
    [InlineData(RolePolicies.ProductionWrite, UserRole.Dapur, true)]
    [InlineData(RolePolicies.ProductionWrite, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.ProductionCancel, UserRole.Dapur, false)]
    [InlineData(RolePolicies.InventoryWrite, UserRole.Dapur, true)]
    [InlineData(RolePolicies.InventoryWrite, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.InventoryRead, UserRole.Dapur, true)]
    [InlineData(RolePolicies.InventoryRead, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.SalesExport, UserRole.Admin, true)]
    [InlineData(RolePolicies.SalesExport, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.SalesExport, UserRole.Dapur, false)]
    [InlineData(RolePolicies.PurchasesCreate, UserRole.Dapur, false)]
    [InlineData(RolePolicies.PurchasesCreate, UserRole.Admin, true)]
    [InlineData(RolePolicies.PurchasesReceive, UserRole.Dapur, true)]
    [InlineData(RolePolicies.PurchasesReceive, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.PurchasesCancel, UserRole.Dapur, false)]
    [InlineData(RolePolicies.MasterDataWrite, UserRole.Dapur, false)]
    [InlineData(RolePolicies.MasterDataWrite, UserRole.Pegawai, false)]
    [InlineData(RolePolicies.Administration, UserRole.Admin, false)]
    [InlineData(RolePolicies.Administration, UserRole.Owner, true)]
    public void Allows_SesuaiMatriksKapabilitas(string policy, UserRole role, bool expected) =>
        RolePolicies.Allows(policy, role).Should().Be(expected);

    [Fact]
    public void SetiapKebijakanPunyaDaftarPeranDanNamaTerdaftar()
    {
        foreach (var policy in RolePolicies.Names)
        {
            RolePolicies.AllowedRoles(policy).Should().NotBeEmpty($"kebijakan '{policy}' harus punya peran yang boleh");
            RolePolicies.Names.Should().Contain(policy);
        }
    }

    [Fact]
    public void NamaKebijakanTidakBolehBerubahDiamDiam()
    {
        // Endpoint memasang kebijakan lewat string; kalau namanya berubah tanpa disadari,
        // seluruh pagar otorisasi hilang tanpa galat saat build.
        RolePolicies.Names.Should().BeEquivalentTo(
        [
            RolePolicies.Sales, RolePolicies.SalesCancel, RolePolicies.SalesReturn,
            RolePolicies.SalesDiscount,
            RolePolicies.FinanceRead, RolePolicies.FinanceWrite,
            RolePolicies.ProductionWrite, RolePolicies.ProductionCancel, RolePolicies.InventoryWrite,
            RolePolicies.InventoryRead, RolePolicies.PurchasesCreate, RolePolicies.PurchasesReceive,
            RolePolicies.PurchasesCancel,
            RolePolicies.MasterDataWrite, RolePolicies.SalesExport, RolePolicies.Administration,
        ]);
    }

    [Fact]
    public void KebijakanTidakDikenal_MelemparBukanMengizinkan()
    {
        var act = () => RolePolicies.AllowedRoles("tidak-ada");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OwnerSelaluBolehSemuaKebijakan()
    {
        foreach (var policy in RolePolicies.Names)
            RolePolicies.Allows(policy, UserRole.Owner).Should().BeTrue(policy);
    }

    [Fact]
    public void SetiapKebijakanPunyaPalingSedikitSatuPenolak()
    {
        // Kebijakan yang boleh dipakai semua peran sama dengan tidak memagari apa pun.
        foreach (var policy in RolePolicies.Names)
            SemuaPeran.Should().Contain(role => !RolePolicies.Allows(policy, role),
                because: $"'{policy}' harus membatasi setidaknya satu peran");
    }
}
