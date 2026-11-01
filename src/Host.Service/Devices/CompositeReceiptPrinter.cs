using Seruni22Bakery.Application.Devices;

namespace Seruni22Bakery.Host.Devices;

/// <summary>
/// Satu IReceiptPrinter untuk PosDeviceService; mode pada PrintTarget yang menentukan adaptor
/// mana yang dipakai. Layanan tetap tidak tahu-menahu soal socket atau spooler.
/// </summary>
public sealed class CompositeReceiptPrinter(
    WindowsSpoolerPrinter driver,
    EscPosTcpPrinter escpos) : IReceiptPrinter
{
    public bool Tersedia => driver.Tersedia || escpos.Tersedia;
    public string NamaAdapter => "pilih-per-mode";

    public Task CetakAsync(PrintTarget target, string isi, CancellationToken ct) =>
        PilihAdapter(target.Mode).CetakAsync(target, isi, ct);

    private IReceiptPrinter PilihAdapter(string mode) =>
        mode.Equals(DeviceSettings.ModeEscPosRaw, StringComparison.OrdinalIgnoreCase)
            ? escpos
            : driver;
}
