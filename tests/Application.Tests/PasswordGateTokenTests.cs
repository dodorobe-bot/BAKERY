using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using NSubstitute;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Xunit;

namespace BakeryDesktop.Application.Tests;

/// <summary>
/// Bahan bakar gerbang ganti sandi sisi server: claim yang dicetak token dan flag yang dibersihkan
/// saat sandi diganti. Gerbangnya sendiri ada di middleware Host; tanpa dua hal ini middleware
/// tidak punya apa pun yang bisa dibaca.
/// </summary>
public class PasswordGateTokenTests
{
    private const string Secret = "A_VERY_SECURE_TEST_KEY_THAT_HAS_32_BYTES_MIN";

    private static JwtSecurityToken Token(User user) =>
        new JwtSecurityTokenHandler().ReadJwtToken(new JwtService(Secret, 60).GenerateToken(user));

    [Fact]
    public void TokenAkunBawaanMembawaPenandaWajibGantiSandi()
    {
        var token = Token(new User { Id = 1, Username = "admin", Role = UserRole.Owner, MustChangePassword = true });

        token.Claims.Should().ContainSingle(c => c.Type == "must_change_password")
            .Which.Value.Should().Be("true");
    }

    [Fact]
    public void TokenAkunSudahDigantiSandinyaBersih()
    {
        var token = Token(new User { Id = 1, Username = "admin", Role = UserRole.Owner, MustChangePassword = false });

        token.Claims.Single(c => c.Type == "must_change_password").Value.Should().Be("false");
    }

    [Fact]
    public void TokenMembawaPeranSebagaiClaimYangDibacaKebijakan()
    {
        foreach (var peran in new[] { UserRole.Owner, UserRole.Admin, UserRole.Dapur, UserRole.Pegawai })
        {
            var token = Token(new User { Id = 3, Username = "u", Role = peran });
            token.Claims.Single(c => c.Type == System.Security.Claims.ClaimTypes.Role).Value
                .Should().Be(peran.ToString());
        }
    }

    [Fact]
    public async Task GantiSandi_MenghapusPenandaDanMemakaiSandiBaru()
    {
        var lama = "admin123";
        var user = new User
        {
            Id = 1, Username = "admin", Role = UserRole.Owner,
            PasswordHash = User.HashPassword(lama), MustChangePassword = true,
        };
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(1).Returns(user);

        var service = new AuthService(users, new JwtService(Secret, 60));
        var hasil = await service.ChangePasswordAsync(1, lama, "SandiBaru123");

        hasil.Success.Should().BeTrue();
        user.MustChangePassword.Should().BeFalse();
        user.VerifyPassword("SandiBaru123").Should().BeTrue();
        user.VerifyPassword(lama).Should().BeFalse();
        await users.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task GantiSandi_TolakSandiBaruTerlaluPendek()
    {
        var user = new User
        {
            Id = 1, Username = "admin", PasswordHash = User.HashPassword("admin123"),
            MustChangePassword = true,
        };
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(1).Returns(user);

        var service = new AuthService(users, new JwtService(Secret, 60));
        var hasil = await service.ChangePasswordAsync(1, "admin123", "abc");

        hasil.Success.Should().BeFalse();
        user.MustChangePassword.Should().BeTrue("penanda tidak boleh lepas sebelum sandi sah diganti");
        await users.DidNotReceive().UpdateAsync(Arg.Any<User>());
    }
}
