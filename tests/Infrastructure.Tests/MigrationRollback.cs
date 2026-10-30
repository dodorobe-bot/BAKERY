using Seruni22Bakery.Infrastructure.Sqlite.Database;

namespace BakeryDesktop.Infrastructure.Tests;

/// <summary>
/// Menjalankan skrip penurunan versi yang tertanam di assembly infrastruktur. Skrip ini sengaja
/// tidak dibaca otomatis oleh DatabaseMigrator, jadi pengujianlah yang membuktikan rollback nyata.
/// </summary>
internal static class MigrationRollback
{
    public static void Run011Down(TempDatabase db) => Run(db, "011_FinishedGoodsAndOrderQuantity.down.sql");

    public static void Run012Down(TempDatabase db) => Run(db, "012_BatchRegistrationAndAuditIndexes.down.sql");

    public static void Run013Down(TempDatabase db) => Run(db, "013_SessionQualityAndReturns.down.sql");

    public static void Run(TempDatabase db, string fileName)
    {
        var assembly = typeof(DatabaseMigrator).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith("." + fileName, StringComparison.Ordinal) || n == fileName);
        if (resource is null)
            throw new InvalidOperationException($"Skrip rollback {fileName} tidak tertanam di assembly. Periksa EmbeddedResource di csproj.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        foreach (var statement in DatabaseMigrator.SplitStatements(reader.ReadToEnd()))
            db.Exec(statement);
    }
}
