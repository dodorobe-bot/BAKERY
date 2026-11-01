namespace Seruni22Bakery.Application.Devices;

/// <summary>
/// Byte ESC/POS yang siap dikirim ke printer jaringan. Kelas ini murni: tidak menyentuh socket,
/// basis data, atau registry, supaya keluarannya bisa dibandingkan byte-per-byte di tes.
///
/// Yang sengaja TIDAK dikirim:
/// - <c>ESC a</c> (perataan): ReceiptRenderer sudah mengatur posisi dengan spasi. Perataan
///   printer akan menumpuk di atasnya dan teks justru keluar tengah.
/// - <c>ESC E</c> (tebal): di banyak printer 58 mm huruf tebal melebar, sementara lebar kolom
///   dihitung untuk huruf biasa; hasilnya kata terpotong.
/// - <c>ESC t</c> (code page): pelaksanaannya berbeda antar vendor. Encoder ini karena itu
///   hanya mengenali ASCII 0x20-0x7E; karakter lain diganti '?' dan jumlahnya dilaporkan.
/// - <c>ESC p</c> (laci uang): membuka laci harus aksi sadar yang terikat aturan transaksi
///   tunai, bukan efek samping mencetak struk.
/// </summary>
public static class EscPosEncoder
{
    /// <summary>Batas byte satu pekerjaan. Struk panjang punya ruang lega; nilai ini menahan
    /// permintaan bengong supaya tidak mengirim puluhan megabita ke printer.</summary>
    public const int MaksByte = 65_536;

    public static readonly byte[] Inisialisasi = [0x1B, 0x40];          // ESC @
    public static readonly byte[] Umpan = [0x1B, 0x64, 0x03];           // ESC d 3 : 3 baris kosong
    public static readonly byte[] PotongLengkap = [0x1D, 0x56, 0x00];   // GS V 0

    public static EscPosHasil Encode(string? isi, bool potong)
    {
        var baris = (isi ?? string.Empty).Replace("\r", string.Empty).Split('\n');
        var keluar = new List<byte>(Inisialisasi);
        var ganti = 0;
        var lf = 0;

        foreach (var teks in baris)
        {
            foreach (var c in teks)
            {
                if (c >= 0x20 && c <= 0x7E) keluar.Add((byte)c);
                else
                {
                    keluar.Add((byte)'?');
                    ganti++;
                }
            }
            keluar.Add(0x0A);
            lf++;
        }

        keluar.AddRange(Umpan);
        if (potong) keluar.AddRange(PotongLengkap);

        if (keluar.Count > MaksByte)
            throw new InvalidOperationException(
                $"Isi struk {keluar.Count} byte melewati batas {MaksByte} byte untuk satu pekerjaan cetak.");

        // Baris dihitung dari LF yang benar-benar dikirim, bukan dari jumlah potongan teks,
        // supaya struk yang berakhir dengan baris kosong tetap terhitung apa adanya.
        return new EscPosHasil(keluar.ToArray(), ganti, lf);
    }
}

/// <param name="Byte">urutan byte persis seperti yang akan dikirim ke socket</param>
/// <param name="Ganti">jumlah karakter di luar ASCII cetak yang dipaksa menjadi '?'</param>
/// <param name="Baris">jumlah baris teks yang dikirim</param>
public readonly record struct EscPosHasil(byte[] Byte, int Ganti, int Baris);
