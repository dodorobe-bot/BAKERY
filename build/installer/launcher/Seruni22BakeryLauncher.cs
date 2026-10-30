using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Peluncur desktop Seruni 22 Bakery.
// Sengaja memakai .NET Framework 4.x (komponen bawaan Windows) dan WinExe, bukan
// VBScript: mesin skrip VBScript tidak selalu ada di Windows 11 (deprecated sejak
// 24H2), sehingga peluncur .vbs gagal dengan "no script engine" pada mesin bersih.
internal static class Program
{
    private const string AppUrl = "http://127.0.0.1:5000/";
    private const string HealthUrl = "http://127.0.0.1:5000/health";
    private const string AppMarker = "Seruni 22 Bakery";
    private const int UpayaKesehatan = 60;
    private const int JedaMilidetik = 500;

    [STAThread]
    private static int Main(string[] args)
    {
        var direktori = AppDomain.CurrentDomain.BaseDirectory;
        var exe = Path.Combine(direktori, "Seruni22Bakery.Host.exe");
        var tanpaBrowser = Environment.GetEnvironmentVariable("SERUNI_LAUNCH_NO_BROWSER") == "1"
            || args.Any(a => string.Equals(a, "--tanpa-browser", StringComparison.OrdinalIgnoreCase));
        var senyap = Environment.GetEnvironmentVariable("SERUNI_LAUNCH_QUIET") == "1";

        if (!File.Exists(exe))
        {
            return Gagal("Berkas aplikasi tidak ditemukan: " + exe, senyap);
        }

        var dimulaiDiSini = false;
        if (!HostBerjalan())
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    WorkingDirectory = direktori,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                dimulaiDiSini = true;
            }
            catch (Exception ex)
            {
                return Gagal("Aplikasi tidak dapat dijalankan: " + ex.Message, senyap);
            }
        }

        var hidup = false;
        for (var i = 0; i < UpayaKesehatan && !hidup; i++)
        {
            Thread.Sleep(JedaMilidetik);
            hidup = HealthMenjawab();
            // Proses yang mati tidak akan pernah menjawab; jangan tunggu penuh 30 detik.
            if (!hidup && dimulaiDiSini && !HostBerjalan()) break;
        }

        if (!hidup)
        {
            return Gagal("Layanan aplikasi tidak menjawab dalam 30 detik.", senyap);
        }

        if (!tanpaBrowser)
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppUrl) { UseShellExecute = true });
            }
            catch
            {
                // Peramban bawaan tidak tersedia: aplikasi tetap berjalan di latar.
            }
        }
        return 0;
    }

    private static bool HostBerjalan()
    {
        try
        {
            return Process.GetProcessesByName("Seruni22Bakery.Host").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool HealthMenjawab()
    {
        try
        {
            var permintaan = (HttpWebRequest)WebRequest.Create(HealthUrl);
            permintaan.Proxy = null;
            permintaan.Timeout = 2000;
            permintaan.ReadWriteTimeout = 2000;
            using (var jawaban = (HttpWebResponse)permintaan.GetResponse())
            using (var pembaca = new StreamReader(jawaban.GetResponseStream(), Encoding.UTF8))
            {
                return jawaban.StatusCode == HttpStatusCode.OK && pembaca.ReadToEnd().Contains(AppMarker);
            }
        }
        catch
        {
            return false;
        }
    }

    private static int Gagal(string sebab, bool senyap)
    {
        var folderLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seruni22Bakery", "logs");
        var pesan = "Seruni 22 Bakery tidak dapat dijalankan." + Environment.NewLine + Environment.NewLine
            + sebab + Environment.NewLine + Environment.NewLine
            + "Kemungkinan penyebab: port 5000 dipakai aplikasi lain, berkas instalasi tidak lengkap, "
            + "atau basis data tidak dapat dibuka." + Environment.NewLine + Environment.NewLine
            + "Log diagnostik: " + LogTerbaru(folderLog);

        try
        {
            Directory.CreateDirectory(folderLog);
            File.WriteAllText(
                Path.Combine(folderLog, "launcher-error.txt"),
                DateTime.Now.ToString("s") + Environment.NewLine + pesan + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
            // Folder log tidak dapat ditulis: pesan tetap ditampilkan ke pengguna.
        }

        if (senyap)
        {
            Console.Error.WriteLine(pesan);
            return 1;
        }
        MessageBox.Show(pesan, "Seruni 22 Bakery", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }

    private static string LogTerbaru(string folder)
    {
        try
        {
            var berkas = new DirectoryInfo(folder).GetFiles("seruni-*.log")
                .OrderByDescending(b => b.LastWriteTimeUtc)
                .FirstOrDefault();
            return berkas != null ? berkas.FullName : folder;
        }
        catch
        {
            return folder;
        }
    }
}
