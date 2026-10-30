namespace Seruni22Bakery.Domain.Common;

/// <summary>
/// Syarat sandi yang sama untuk sandi sementara, ganti sandi mandiri, dan reset oleh pemilik.
/// Panjang saja tidak cukup: "123456" lolos hitungan karakter tetapi tidak melindungi apa pun.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    /// <summary>Pesan kesalahan, atau null bila sandi memenuhi syarat.</summary>
    public static string? Error(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
            return $"Kata sandi minimal {MinimumLength} karakter.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Kata sandi harus memuat huruf dan angka.";
        return null;
    }
}
