using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Domain.Interfaces;

namespace Seruni22Bakery.Application.Services;

public record LoginRequest(string Username, string Password);

/// <param name="Capabilities">Nama kebijakan yang boleh dibuka peran ini — dihitung dari matriks
/// RolePolicies di sisi server, jadi layar tidak pernah menampilkan menu yang ujungnya 403.</param>
public record LoginResponse(
    int UserId, string Username, UserRole Role, bool MustChangePassword, string Token,
    IReadOnlyList<string> Capabilities);

public class AuthService
{
    private readonly IUserRepository _users;
    private readonly JwtService _jwt;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public AuthService(IUserRepository users, JwtService jwt)
    {
        _users = users;
        _jwt = jwt;
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest req)
    {
        var user = await _users.GetByUsernameAsync(req.Username);
        if (user is null || !user.IsActive)
            return Result<LoginResponse>.Fail(ErrorCodes.InvalidCredentials, "Username atau password salah.");

        if (user.IsLocked())
            return Result<LoginResponse>.Fail(ErrorCodes.AccountLocked,
                $"Akun terkunci hingga {user.LockedUntil:HH:mm}. Coba lagi nanti.");

        // Masa kunci yang sudah lewat harus menghapus hitungan gagal. Tanpa ini satu salah sandi
        // setiap 15 menit langsung mengunci akun lagi: pemilik tidak pernah bisa masuk.
        if (user.LockedUntil is not null && user.LockedUntil <= DateTime.UtcNow)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
        }

        if (!user.VerifyPassword(req.Password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
            await _users.UpdateAsync(user);
            return Result<LoginResponse>.Fail(ErrorCodes.InvalidCredentials, "Username atau password salah.");
        }

        // Reset lockout on success
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);

        var token = _jwt.GenerateToken(user);
        return Result<LoginResponse>.Ok(new LoginResponse(
            user.Id, user.Username, user.Role, user.MustChangePassword, token,
            RolePolicies.Kapabilitas(user.Role)));
    }

    public async Task<Result> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return Result.Fail(ErrorCodes.NotFound, "User tidak ditemukan.");
        if (!user.VerifyPassword(currentPassword))
            return Result.Fail(ErrorCodes.InvalidCredentials, "Password lama salah.");
        if (PasswordPolicy.Error(newPassword) is { } policyError)
            return Result.Fail(ErrorCodes.ValidationFailed, policyError);
        // Akun bawaan wajib berganti sandi; tanpa pagar ini sandi bawaan yang "diganti" dengan
        // sandi yang sama akan melepas gerbang must_change_password tanpa ada rahasia baru.
        if (user.VerifyPassword(newPassword))
            return Result.Fail(ErrorCodes.ValidationFailed, "Sandi baru harus berbeda dari sandi lama.");

        user.PasswordHash = User.HashPassword(newPassword);
        user.MustChangePassword = false;
        // Token yang dicetak dengan sandi lama tidak boleh tetap berlaku setelah sandinya diganti.
        user.TokenVersion++;
        await _users.UpdateAsync(user);
        return Result.Ok();
    }

    /// <summary>Mencabut seluruh token akun ini; token yang sedang dipakai ikut mati.</summary>
    public async Task<Result> LogoutAsync(int userId)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null) return Result.Ok();
        user.TokenVersion++;
        await _users.UpdateAsync(user);
        return Result.Ok();
    }
}
