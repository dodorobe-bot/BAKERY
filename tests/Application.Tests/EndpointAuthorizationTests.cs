using System.Text.RegularExpressions;
using FluentAssertions;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Entities;
using Xunit;

namespace BakeryDesktop.Application.Tests;

/// <summary>
/// Pagar sumber untuk titik kegagalan yang sebenarnya terjadi dulu: claim peran sudah dikirim
/// server tetapi tidak ada yang membacanya, sehingga "/api/users Owner only" tidak ditegakkan.
/// Tes ini membaca Program.cs dan menuntut setiap endpoint API memasang pagar otorisasi, dengan
/// kebijakan peran untuk semua aksi perubahan dan segelintir jalur yang memang sengaja terbuka.
/// </summary>
public class EndpointAuthorizationTests
{
    private static readonly string[] BolehTanpaPagar =
    [
        "GET /health",
        "POST /api/auth/login",
        // Merek toko harus terbaca di layar masuk, sebelum ada token. Jawabannya hanya nama,
        // slogan, dan logo — tidak ada data toko yang bisa disalahgunakan tanpa login.
        "GET /api/brand",
    ];

    private static readonly string[] AksiPerubahan = ["POST", "PUT", "DELETE", "PATCH"];

    private static string ProgramSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BakeryDesktop.slnx")))
            dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "src", "Host.Service", "Program.cs");
        File.Exists(path).Should().BeTrue($"Program.cs harus bisa dibaca dari {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Endpoint beserta nama kebijakan yang terpasang; Policy null berarti tidak ada pagar.</summary>
    private static (string Endpoint, string? Policy)[] BacaPetaEndpoint()
    {
        var baris = Regex.Split(ProgramSource(), @"\r?\n");
        var daftar = new List<(string Endpoint, int Baris)>();
        var pagar = new Dictionary<int, string?>();

        for (var i = 0; i < baris.Length; i++)
        {
            var cocok = Regex.Match(baris[i], @"app\.Map(Get|Post|Put|Delete|Patch)\(""([^""]+)""");
            if (cocok.Success)
            {
                daftar.Add(($"{cocok.Groups[1].Value.ToUpperInvariant()} {cocok.Groups[2].Value}", i));
                continue;
            }

            var terpasang = Regex.Match(baris[i], @"\.RequireAuthorization\((?:""([^""]+)""|)\)");
            if (terpasang.Success && daftar.Count > 0 && !pagar.ContainsKey(daftar[^1].Baris))
                pagar[daftar[^1].Baris] = terpasang.Groups[1].Value;
        }

        return daftar.Select(d => (d.Endpoint, pagar.TryGetValue(d.Baris, out var p) ? p : null)).ToArray();
    }

    [Fact]
    public void EndpointTanpaKebijakanHanyaYangSengajaDibuka()
    {
        var tanpaPagar = BacaPetaEndpoint()
            .Where(e => e.Policy is null)
            .Select(e => e.Endpoint)
            .ToArray();

        tanpaPagar.Should().BeEquivalentTo(BolehTanpaPagar,
            "endpoint baru wajib memasang RequireAuthorization; tanpa itu seluruh API terbuka untuk siapa pun");
    }

    [Fact]
    public void SetiapAksiPerubahanMemakaiKebijakanPeran()
    {
        // POST /api/auth/change-password sengaja hanya menuntut login: akun yang masih wajib
        // ganti sandi harus bisa memperbaiki dirinya sendiri.
        var polos = BacaPetaEndpoint()
            .Where(e => e.Endpoint.StartsWith("/api") && !e.Endpoint.StartsWith("/api/auth/"))
            .Where(e => AksiPerubahan.Contains(e.Endpoint.Split(' ')[0]))
            .Where(e => string.IsNullOrEmpty(e.Policy))
            .Select(e => e.Endpoint)
            .ToArray();

        polos.Should().BeEmpty(
            "aksi perubahan harus memakai kebijakan dari RolePolicies; RequireAuthorization polos " +
            "membaca token apa pun tanpa melihat peran");
    }

    [Fact]
    public void KebijakanYangDipakaiTerdaftarDiMatriksDanSemuanyaTerpasang()
    {
        var dipakai = BacaPetaEndpoint()
            .Select(e => e.Policy)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToArray();

        dipakai.Should().NotBeEmpty();
        foreach (var kebijakan in dipakai)
            RolePolicies.Names.Should().Contain(kebijakan);

        // Ada kebijakan yang ditegakkan di dalam handler (misal pembatalan pesanan ditolak per peran),
        // jadi "terpasang" berarti muncul sebagai pagar endpoint atau disebut eksplisit di host.
        var sumber = ProgramSource();
        var tanpaRujukan = typeof(RolePolicies)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => ((string)f.GetValue(null)!, f.Name))
            .Where(k => !dipakai.Contains(k.Item1) && !sumber.Contains($"RolePolicies.{k.Item2}"))
            .Select(k => k.Item1)
            .ToArray();

        tanpaRujukan.Should().BeEmpty(
            "kebijakan yang tidak dipasang dan tidak ditegakkan di mana pun hanya memberi rasa aman palsu");
    }

    [Fact]
    public void EndpointPenggunaHanyaUntukOwner()
    {
        var peta = BacaPetaEndpoint().ToDictionary(e => e.Endpoint, e => e.Policy);

        peta.Should().ContainKey("GET /api/users");
        peta["GET /api/users"].Should().Be(RolePolicies.Administration);
        RolePolicies.AllowedRoles(RolePolicies.Administration).Should().BeEquivalentTo([UserRole.Owner]);
    }

    [Fact]
    public void JalurTulisPentingTidakBolehPolos()
    {
        var peta = BacaPetaEndpoint().ToDictionary(e => e.Endpoint, e => e.Policy);

        foreach (var endpoint in new[]
                 {
                     "POST /api/orders", "POST /api/expenses", "POST /api/production/orders",
                     "POST /api/purchases", "POST /api/ingredients", "PUT /api/settings",
                     "PUT /api/orders/{id:int}/status",
                 })
        {
            peta.Should().ContainKey(endpoint);
            peta[endpoint].Should().NotBeNullOrWhiteSpace($"{endpoint} harus dibatasi per peran");
        }
    }
}
