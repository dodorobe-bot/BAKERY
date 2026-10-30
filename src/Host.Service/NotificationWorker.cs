using Seruni22Bakery.Application.Services;

namespace Seruni22Bakery.Host;

/// <summary>
/// Penyegar pengingat internal: stok menipis, lot mendekati kedaluwarsa, pesanan lewat jadwal,
/// dan ringkasan tugas. Semua tulisan idempoten lewat DedupeKey, jadi aman berjalan berkala.
/// Worker ini hanya menulis ke SQLite; tidak ada koneksi ke layanan luar.
/// </summary>
public sealed class NotificationWorker : BackgroundService
{
    private static readonly TimeSpan Jeda = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<NotificationWorker> _log;

    public NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var pengatur = new PeriodicTimer(Jeda);
        while (!stoppingToken.IsCancellationRequested)
        {
            await SinkronAsync();
            try
            {
                if (!await pengatur.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SinkronAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var notifikasi = scope.ServiceProvider.GetRequiredService<NotificationService>();
            var ditulis = await notifikasi.SinkronkanOperasionalAsync(DateTime.UtcNow);
            if (ditulis > 0)
                _log.LogInformation("{Jumlah} pengingat internal baru ditulis.", ditulis);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Penyegaran pengingat internal gagal; siklus berikutnya dicoba lagi.");
        }
    }
}
