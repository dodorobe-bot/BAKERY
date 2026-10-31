using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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

        // Semua argumen selain --tanpa-browser adalah perintah pemeliharaan host
        // (--backup, --restore <berkas>, --Database:Path=...). Tanpa penerusan ini
        // perintah pemulihan hanya menyalakan aplikasi dan data tidak dipulihkan.
        // Perintah pemeliharaan diserahkan ke host: host memeriksa sendiri instance yang
        // berjalan (--restore keluar dengan kode 3) dan --backup memang diizinkan saat
        // aplikasi hidup, jadi pemeriksaan "folder lain" di bawah tidak boleh mendahuluinya.
        var argumenHost = args
            .Where(a => !string.Equals(a, "--tanpa-browser", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (argumenHost.Length > 0)
        {
            return JalankanPemeliharaan(exe, direktori, argumenHost, senyap);
        }

        // Kanal EXE dan MSI memasang folder berbeda tetapi memakai port 5000 dan folder data
        // yang sama. Kalau host dari folder lain sedang berjalan, peluncur ini hanya akan
        // membuka peramban ke server milik instalasi itu — bukan miliknya sendiri.
        var hostLain = HostLainBerjalan(direktori);
        if (hostLain != null)
        {
            return Gagal(
                "Aplikasi Seruni 22 Bakery sedang berjalan dari folder lain:" + Environment.NewLine
                + hostLain + Environment.NewLine + Environment.NewLine
                + "Tutup aplikasi itu lebih dulu. Dua instalasi (EXE dan MSI) tidak boleh berjalan bersamaan "
                + "karena memakai port dan basis data yang sama.", senyap);
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

        // /health membawa versi host. Kalau angkanya berbeda dari versi peluncur ini, server
        // yang menjawab berasal dari paket lain — kemungkinan besar instalasi kanal lain yang
        // memegang port 5000. Membukanya berarti pengguna bekerja di atas versi yang salah.
        var versiHost = VersiDariHealth(HealthBody());
        var versiPeluncur = VersiPeluncur();
        if (!string.IsNullOrEmpty(versiHost) && !string.IsNullOrEmpty(versiPeluncur)
            && !string.Equals(versiHost, versiPeluncur, StringComparison.OrdinalIgnoreCase))
        {
            return Gagal(
                "Server yang menjawab di port 5000 melaporkan versi " + versiHost
                + ", sedangkan peluncur ini versi " + versiPeluncur + "." + Environment.NewLine + Environment.NewLine
                + "Kemungkinan besar instalasi Seruni 22 Bakery lain (kanal EXE atau MSI) sedang berjalan. "
                + "Tutup aplikasi itu, lalu jalankan kembali peluncur ini.", senyap);
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

    // Menjalankan host sekali pakai untuk perintah pemeliharaan, lalu keluar dengan
    // kode keluar host supaya skrip bisa membedakan berhasil dan gagal. Keluaran host
    // ditampilkan sebagai kotak pesan karena peluncur adalah WinExe tanpa konsol.
    private static int JalankanPemeliharaan(string exe, string direktori, string[] argumen, bool senyap)
    {
        try
        {
            var info = new ProcessStartInfo(exe)
            {
                WorkingDirectory = direktori,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = RangkaiArgumen(argumen),
            };

            string keluaran;
            string galat;
            int kode;
            using (var proses = Process.Start(info))
            {
                // stderr dibaca asinkron: membaca dua aliran besar bergantian bisa membuat
                // kedua proses saling menunggu sampai buffer penuh.
                var pembacaGalat = proses.StandardError.ReadToEndAsync();
                keluaran = proses.StandardOutput.ReadToEnd().Trim();
                galat = pembacaGalat.Result.Trim();
                proses.WaitForExit();
                kode = proses.ExitCode;
            }

            if (kode == 0)
            {
                var pesanSukses = string.IsNullOrWhiteSpace(keluaran)
                    ? "Perintah pemeliharaan selesai."
                    : "Perintah pemeliharaan selesai." + Environment.NewLine + Environment.NewLine + keluaran;
                Tampilkan(pesanSukses, MessageBoxIcon.Information, senyap, false);
                return 0;
            }

            var sebab = !string.IsNullOrWhiteSpace(galat) ? galat : keluaran;
            if (string.IsNullOrWhiteSpace(sebab)) sebab = "Host tidak memberi keterangan.";
            return GagalPerintah(kode, sebab, senyap);
        }
        catch (Exception ex)
        {
            return Gagal("Perintah pemeliharaan tidak dapat dijalankan: " + ex.Message, senyap);
        }
    }

    private static string RangkaiArgumen(string[] argumen)
    {
        return string.Join(" ", argumen.Select(Kutip));
    }

    // Aturan kutip CommandLineToArgvW: garis miring terbalik sebelum tanda kutip harus
    // digandakan, garis miring di akhir argumen juga, supaya path berspasi diterima utuh.
    private static string Kutip(string argumen)
    {
        if (argumen.Length > 0 && argumen.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return argumen;
        var hasil = new StringBuilder("\"");
        var miring = 0;
        foreach (var c in argumen)
        {
            if (c == '\\')
            {
                miring++;
                continue;
            }
            if (c == '"')
            {
                hasil.Append('\\', miring * 2 + 1).Append('"');
                miring = 0;
                continue;
            }
            if (miring > 0)
            {
                hasil.Append('\\', miring);
                miring = 0;
            }
            hasil.Append(c);
        }
        if (miring > 0) hasil.Append('\\', miring * 2);
        return hasil.Append('"').ToString();
    }

    private static int GagalPerintah(int kode, string sebab, bool senyap)
    {
        string penjelasan;
        switch (kode)
        {
            case 1: penjelasan = "Perintah gagal. Basis data dikembalikan ke keadaan sebelum pemulihan."; break;
            case 2: penjelasan = "Argumen salah atau berkas cadangan tidak ditemukan."; break;
            case 3: penjelasan = "Aplikasi masih berjalan. Tutup aplikasi lebih dulu."; break;
            default: penjelasan = "Perintah gagal."; break;
        }

        var folderLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seruni22Bakery", "logs");
        var pesan = "Seruni 22 Bakery — perintah pemeliharaan gagal (kode " + kode + ")." + Environment.NewLine + Environment.NewLine
            + penjelasan + Environment.NewLine + Environment.NewLine
            + sebab + Environment.NewLine + Environment.NewLine
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

        Tampilkan(pesan, MessageBoxIcon.Error, senyap, true);
        return kode;
    }

    private static void Tampilkan(string pesan, MessageBoxIcon ikon, bool senyap, bool keGalat)
    {
        if (senyap)
        {
            if (keGalat) Console.Error.WriteLine(pesan);
            else Console.Out.WriteLine(pesan);
            return;
        }
        MessageBox.Show(pesan, "Seruni 22 Bakery", MessageBoxButtons.OK, ikon);
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
        var badan = HealthBody();
        return badan != null && badan.Contains(AppMarker);
    }

    private static string HealthBody()
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
                return jawaban.StatusCode == HttpStatusCode.OK ? pembaca.ReadToEnd() : null;
            }
        }
        catch
        {
            return null;
        }
    }

    // Jalur host lain yang sedang berjalan, atau null bila host yang aktif memang milik
    // folder ini. Proses milik pengguna lain bisa menolak dibaca; dalam kasus itu peluncur
    // tidak menyimpulkan apa pun dan pemeriksaan versi /health di bawah tetap menjadi pagar.
    private static string HostLainBerjalan(string direktori)
    {
        try
        {
            var exeLokal = Path.GetFullPath(Path.Combine(direktori, "Seruni22Bakery.Host.exe"));
            foreach (var proses in Process.GetProcessesByName("Seruni22Bakery.Host"))
            {
                try
                {
                    var jalur = proses.MainModule != null ? proses.MainModule.FileName : null;
                    if (jalur != null && !string.Equals(Path.GetFullPath(jalur), exeLokal,
                            StringComparison.OrdinalIgnoreCase))
                        return jalur;
                }
                catch
                {
                    // Tidak berhak membaca proses: abaikan, versi /health yang memutuskan.
                }
                finally
                {
                    proses.Dispose();
                }
            }
        }
        catch
        {
            // Pencarian proses gagal: jangan menghalangi start karena pemeriksaan bantu ini.
        }
        return null;
    }

    private static string VersiPeluncur()
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(Application.ExecutablePath).ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    private static string VersiDariHealth(string badan)
    {
        if (string.IsNullOrEmpty(badan)) return null;
        var cocok = Regex.Match(badan, "\"version\"\\s*:\\s*\"([^\"]+)\"");
        return cocok.Success ? cocok.Groups[1].Value : null;
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
