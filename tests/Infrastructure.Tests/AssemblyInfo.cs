// Test database berupa file sementara per-test, tapi SqliteConnection.ClearAllPools()
// bersifat global untuk proses. Dua kelas yang jalan paralel bisa saling mematikan
// koneksi, sehingga migrasi ulang tampak gagal secara acak.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
