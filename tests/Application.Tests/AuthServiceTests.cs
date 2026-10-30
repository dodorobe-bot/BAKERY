using FluentAssertions;
using NSubstitute;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;
using Xunit;

namespace BakeryDesktop.Application.Tests;

public class AuthServiceTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly JwtService _jwt = new("A_VERY_SECURE_TEST_KEY_THAT_HAS_32_BYTES_MIN", 60);

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsSuccessAndToken()
    {
        var password = "testPassword123";
        var user = new User
        {
            Id = 1,
            Username = "kasir",
            PasswordHash = User.HashPassword(password),
            Role = UserRole.Admin,
            IsActive = true
        };

        _users.GetByUsernameAsync("kasir").Returns(user);

        var service = new AuthService(_users, _jwt);
        var result = await service.LoginAsync(new LoginRequest("kasir", password));

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Token.Should().NotBeNullOrWhiteSpace();
        result.Data.Username.Should().Be("kasir");
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsFailAndIncrementsFailedCount()
    {
        var user = new User
        {
            Id = 2,
            Username = "kasir",
            PasswordHash = User.HashPassword("correctPassword"),
            Role = UserRole.Admin,
            IsActive = true,
            FailedLoginCount = 0
        };

        _users.GetByUsernameAsync("kasir").Returns(user);

        var service = new AuthService(_users, _jwt);
        var result = await service.LoginAsync(new LoginRequest("kasir", "wrongPassword"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidCredentials);
        user.FailedLoginCount.Should().Be(1);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ReturnsFail()
    {
        var user = new User
        {
            Id = 3,
            Username = "inactive_user",
            PasswordHash = User.HashPassword("password"),
            Role = UserRole.Pegawai,
            IsActive = false
        };

        _users.GetByUsernameAsync("inactive_user").Returns(user);

        var service = new AuthService(_users, _jwt);
        var result = await service.LoginAsync(new LoginRequest("inactive_user", "password"));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidCredentials);
    }

    [Fact]
    public async Task ChangePassword_SameAsCurrent_RejectedAndGateStays()
    {
        // Akun bawaan wajib berganti sandi; "mengganti" ke sandi yang sama tidak boleh
        // melepas gerbang itu, karena rahasianya tetap sandi bawaan yang diketahui umum.
        var user = new User
        {
            Id = 4,
            Username = "admin",
            PasswordHash = User.HashPassword("admin123"),
            Role = UserRole.Owner,
            IsActive = true,
            MustChangePassword = true,
            TokenVersion = 3
        };
        _users.GetByIdAsync(4).Returns(user);

        var service = new AuthService(_users, _jwt);
        var result = await service.ChangePasswordAsync(4, "admin123", "admin123");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("berbeda dari sandi lama");
        user.MustChangePassword.Should().BeTrue("gerbang ganti sandi belum benar-benar dilewati");
        user.TokenVersion.Should().Be(3, "hash sandi tidak berubah, jadi token lama tetap sah");
    }

    [Fact]
    public async Task LoginAsync_SetelahKunciBerakhir_HitunganGagalMulaiDariNol()
    {
        // Tanpa reset, satu salah sandi setiap 15 menit langsung mengunci akun lagi (hitungan
        // gagal masih 5 dari masa kunci sebelumnya), sehingga pemilik tidak pernah bisa masuk.
        var user = new User
        {
            Id = 5,
            Username = "pemilik",
            PasswordHash = User.HashPassword("SandiPemilik1"),
            Role = UserRole.Owner,
            IsActive = true,
            FailedLoginCount = 5,
            LockedUntil = DateTime.UtcNow.AddMinutes(-1)
        };
        _users.GetByUsernameAsync("pemilik").Returns(user);

        var service = new AuthService(_users, _jwt);
        var result = await service.LoginAsync(new LoginRequest("pemilik", "sandiSalah1"));

        result.Success.Should().BeFalse();
        user.FailedLoginCount.Should().Be(1, "hitungan lama tidak boleh langsung mengunci ulang");
        user.LockedUntil.Should().BeNull("akun tidak boleh terkunci lagi hanya karena satu salah sandi");
    }
}
