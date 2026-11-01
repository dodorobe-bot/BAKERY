# Registri Test Case — Seruni 22 Bakery 1.2.3

Dibuat 2026-10-28 dari eksekusi nyata, bukan dari daftar niat.

> **Koreksi nama kebijakan (ditambahkan 2026-10-31).** Catatan eksekusi di bawah masih memakai
> nama lama `purchases-write`. Kebijakan itu kemudian dipecah menjadi `purchases-create`,
> `purchases-receive`, dan `purchases-cancel`; baris TC-DOM-025, TC-BATCH3-036, dan
> TC-BATCH3-037 dipertahankan apa adanya sebagai catatan tanggal itu. Matriks yang berlaku
> sekarang ada di `src/Domain/Common/RolePolicies.cs` dan diuji ulang tiap run.

| Sumber | Jumlah | Status | Bukti |
|---|---|---|---|
| Tes otomatis xUnit/Playwright | 265 | PASS semua | `/c/tmp/gate-20261028-final.log` (2026-10-28 23:49–23:53) |
| Pemeriksaan perilaku API (node) | 137 | PASS semua | log yang sama, blok verify-batch2/3/6 |
| Kit mesin bersih (Windows Sandbox, jaringan dimatikan) | 45 PASS, 0 FAIL | PASS | `C:\tmp\clean-run-122\evidence\UJI-MESIN-BERSIH-20261028-231834.json` |

## A. Tes otomatis (265)

### Application (unit layanan) — 46 kasus

| ID | Kelas | Kasus | Status |
|---|---|---|---|
| TC-APP-001 | AuthServiceTests | `LoginAsync_ValidCredentials_ReturnsSuccessAndToken` | PASS |
| TC-APP-002 | AuthServiceTests | `LoginAsync_InvalidPassword_ReturnsFailAndIncrementsFailedCount` | PASS |
| TC-APP-003 | AuthServiceTests | `LoginAsync_InactiveUser_ReturnsFail` | PASS |
| TC-APP-004 | EndpointAuthorizationTests | `EndpointTanpaKebijakanHanyaYangSengajaDibuka` | PASS |
| TC-APP-005 | EndpointAuthorizationTests | `SetiapAksiPerubahanMemakaiKebijakanPeran` | PASS |
| TC-APP-006 | EndpointAuthorizationTests | `KebijakanYangDipakaiTerdaftarDiMatriksDanSemuanyaTerpasang` | PASS |
| TC-APP-007 | EndpointAuthorizationTests | `EndpointPenggunaHanyaUntukOwner` | PASS |
| TC-APP-008 | EndpointAuthorizationTests | `JalurTulisPentingTidakBolehPolos` | PASS |
| TC-APP-009 | PasswordGateTokenTests | `TokenAkunBawaanMembawaPenandaWajibGantiSandi` | PASS |
| TC-APP-010 | PasswordGateTokenTests | `TokenAkunSudahDigantiSandinyaBersih` | PASS |
| TC-APP-011 | PasswordGateTokenTests | `TokenMembawaPeranSebagaiClaimYangDibacaKebijakan` | PASS |
| TC-APP-012 | PasswordGateTokenTests | `GantiSandi_MenghapusPenandaDanMemakaiSandiBaru` | PASS |
| TC-APP-013 | PasswordGateTokenTests | `GantiSandi_TolakSandiBaruTerlaluPendek` | PASS |
| TC-APP-014 | PosDeviceServiceTests | `CetakStruk_Berhasil_MenandaiPrintedDanMenyimpanIsi` | PASS |
| TC-APP-015 | PosDeviceServiceTests | `CetakStruk_PrinterMelempar_JobFailedDanPesananTidakBerubah` | PASS |
| TC-APP-016 | PosDeviceServiceTests | `CetakStruk_BatasWaktu_DilaporkanSebagaiPrinterTidakMerespons` | PASS |
| TC-APP-017 | PosDeviceServiceTests | `CetakStruk_TanpaPrinterTerdaftar_TidakMembuatPekerjaan` | PASS |
| TC-APP-018 | PosDeviceServiceTests | `CetakStruk_PrinterTidakTerpasang_DitolakDenganPesanJelas` | PASS |
| TC-APP-019 | PosDeviceServiceTests | `CetakStruk_NonWindows_DitolakSebelumMenyentuhAdapter` | PASS |
| TC-APP-020 | PosDeviceServiceTests | `CetakStruk_CetakUlang_PayloadMemuatPenanda` | PASS |
| TC-APP-021 | PosDeviceServiceTests | `CetakStruk_IsiTidakDiambilDariPemanggil_TetapiDariPesanan` | PASS |
| TC-APP-022 | PosDeviceServiceTests | `PantauStatus_TanpaPrinter_TerdeteksiDariKatalog` | PASS |
| TC-APP-023 | PosDeviceServiceTests | `PantauStatus_PrinterTersimpanSudahTidakAda_DiberiCatatan` | PASS |
| TC-APP-024 | PosDeviceServiceTests | `PantauStatus_NonWindows_TidakMengarangDukungan` | PASS |
| TC-APP-025 | PosDeviceServiceTests | `SimpanPengaturan_KunciAsing_Ditolak` | PASS |
| TC-APP-026 | PosDeviceServiceTests | `SimpanPengaturan_LebarTidakDidukung_Ditolak(lebar: "57")` | PASS |
| TC-APP-027 | PosDeviceServiceTests | `SimpanPengaturan_LebarTidakDidukung_Ditolak(lebar: "80.5")` | PASS |
| TC-APP-028 | PosDeviceServiceTests | `SimpanPengaturan_LebarTidakDidukung_Ditolak(lebar: "")` | PASS |
| TC-APP-029 | PosDeviceServiceTests | `SimpanPengaturan_ModeEscPosTanpaAlamat_Ditolak` (nama 2026-10-28: `..._ModeEscPosBelumTersedia_Ditolak`; diganti 2026-11-01 saat mode `escpos-raw` dibuka) | PASS |
| TC-APP-030 | PosDeviceServiceTests | `SimpanPengaturan_PrinterTidakTerpasang_Ditolak` | PASS |
| TC-APP-031 | PosDeviceServiceTests | `SimpanPengaturan_Sah_TersimpanDanTerbacaUlang` | PASS |
| TC-APP-032 | PosDeviceServiceTests | `PulihkanJobMenggantung_MemakaiBatasWaktuNyata` | PASS |
| TC-APP-033 | ReceiptRendererTests | `Kolom_MengikutiLebarKertas(mm: 58, kolom: 32)` | PASS |
| TC-APP-034 | ReceiptRendererTests | `Kolom_MengikutiLebarKertas(mm: 80, kolom: 48)` | PASS |
| TC-APP-035 | ReceiptRendererTests | `Render_MemuatNomorNotaJumlahDanTotalRupiah` | PASS |
| TC-APP-036 | ReceiptRendererTests | `Render_TidakAdaBarisMelebihiLebarKertas(mm: 58)` | PASS |
| TC-APP-037 | ReceiptRendererTests | `Render_TidakAdaBarisMelebihiLebarKertas(mm: 80)` | PASS |
| TC-APP-038 | ReceiptRendererTests | `Render_DeskripsiPanjang_DibungkusBukanDipotong` | PASS |
| TC-APP-039 | ReceiptRendererTests | `Render_Lunas_MemuatPenandaLunasDanTanpaSisa` | PASS |
| TC-APP-040 | ReceiptRendererTests | `Render_UangMuka_TampilSebagaiDpDanSisa` | PASS |
| TC-APP-041 | ReceiptRendererTests | `Render_TanpaCetakUlang_TidakMemuatPenanda` | PASS |
| TC-APP-042 | ReceiptRendererTests | `Render_CetakUlang_MemuatPenandaJelas` | PASS |
| TC-APP-043 | ReceiptRendererTests | `Render_NamaOutletKosong_MemakaiNamaToko` | PASS |
| TC-APP-044 | ReceiptRendererTests | `RenderTest_MemuatLebarDanPenandaUji(mm: 58)` | PASS |
| TC-APP-045 | ReceiptRendererTests | `RenderTest_MemuatLebarDanPenandaUji(mm: 80)` | PASS |
| TC-APP-046 | UnitTest1 | `Test1` | PASS |

### Domain (aturan bisnis) — 57 kasus

| ID | Kelas | Kasus | Status |
|---|---|---|---|
| TC-DOM-001 | DomainEntityTests | `User_PasswordHashing_AndVerification_ShouldSucceed` | PASS |
| TC-DOM-002 | DomainEntityTests | `User_LockoutCheck_ShouldReflectExpiryTime` | PASS |
| TC-DOM-003 | DomainEntityTests | `SalesOrder_PaymentCalculations_ShouldBeAccurate` | PASS |
| TC-DOM-004 | ProductionCostRulesTests | `LossUnits_MenghitungHanyaScrapYangDibuang` | PASS |
| TC-DOM-005 | ProductionCostRulesTests | `LossCost_ProporsionalDanDibatasiBiayaBahan(material: 200000, lossUnits: 1, totalUnits: 4, expected: 50000)` | PASS |
| TC-DOM-006 | ProductionCostRulesTests | `LossCost_ProporsionalDanDibatasiBiayaBahan(material: 200000, lossUnits: 0, totalUnits: 4, expected: 0)` | PASS |
| TC-DOM-007 | ProductionCostRulesTests | `LossCost_ProporsionalDanDibatasiBiayaBahan(material: 0, lossUnits: 1, totalUnits: 4, expected: 0)` | PASS |
| TC-DOM-008 | ProductionCostRulesTests | `LossCost_ProporsionalDanDibatasiBiayaBahan(material: 200000, lossUnits: 4, totalUnits: 4, expected: 200000)` | PASS |
| TC-DOM-009 | ProductionCostRulesTests | `LossCost_ProporsionalDanDibatasiBiayaBahan(material: 100000, lossUnits: 9, totalUnits: 4, expected: 100000)` | PASS |
| TC-DOM-010 | ProductionCostRulesTests | `LossCost_MembulatPembagianKeSenTerdekat` | PASS |
| TC-DOM-011 | ProductionCostRulesTests | `CostPerGoodUnit_MemasukkanTenagaKerjaDanOverheadTapiTidakTermasukKerugian` | PASS |
| TC-DOM-012 | ProductionCostRulesTests | `CostPerGoodUnit_TanpaUnitBaik_BernilaiNol` | PASS |
| TC-DOM-013 | ProductionCostRulesTests | `CostPerGoodUnit_KerugianLebihBesarDariBahan_TidakMenjadiNilaiNegatif` | PASS |
| TC-DOM-014 | DocumentNumberTests | `Prefix_MengikutiJenisDokumen(kind: Order, expected: "ORD")` | PASS |
| TC-DOM-015 | DocumentNumberTests | `Prefix_MengikutiJenisDokumen(kind: WorkOrder, expected: "SPK")` | PASS |
| TC-DOM-016 | DocumentNumberTests | `Prefix_MengikutiJenisDokumen(kind: PurchaseOrder, expected: "PO")` | PASS |
| TC-DOM-017 | DocumentNumberTests | `Format_MemakaiTanggalUtcDanUrutanEmpatDigit` | PASS |
| TC-DOM-018 | DocumentNumberTests | `Format_MembuangJamMenit_SehinggaSatuHariSatuBlokNomor` | PASS |
| TC-DOM-019 | DocumentNumberTests | `Format_MenolakUrutanNolAtauNegatif(sequence: 0)` | PASS |
| TC-DOM-020 | DocumentNumberTests | `Format_MenolakUrutanNolAtauNegatif(sequence: -1)` | PASS |
| TC-DOM-021 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales", role: Owner, expected: True)` | PASS |
| TC-DOM-022 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "master-data-write", role: Pegawai, expected: False)` | PASS |
| TC-DOM-023 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "master-data-write", role: Dapur, expected: False)` | PASS |
| TC-DOM-024 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "purchases-cancel", role: Dapur, expected: False)` | PASS |
| TC-DOM-025 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "purchases-write", role: Dapur, expected: True)` | PASS |
| TC-DOM-026 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-export", role: Dapur, expected: False)` | PASS |
| TC-DOM-027 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-export", role: Pegawai, expected: False)` | PASS |
| TC-DOM-028 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-export", role: Admin, expected: True)` | PASS |
| TC-DOM-029 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "inventory-read", role: Pegawai, expected: False)` | PASS |
| TC-DOM-030 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "inventory-read", role: Dapur, expected: True)` | PASS |
| TC-DOM-031 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "inventory-write", role: Pegawai, expected: False)` | PASS |
| TC-DOM-032 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "inventory-write", role: Dapur, expected: True)` | PASS |
| TC-DOM-033 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "production-cancel", role: Dapur, expected: False)` | PASS |
| TC-DOM-034 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "production-write", role: Pegawai, expected: False)` | PASS |
| TC-DOM-035 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "administration", role: Admin, expected: False)` | PASS |
| TC-DOM-036 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "production-write", role: Dapur, expected: True)` | PASS |
| TC-DOM-037 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "finance-write", role: Dapur, expected: False)` | PASS |
| TC-DOM-038 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "finance-read", role: Dapur, expected: False)` | PASS |
| TC-DOM-039 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "finance-read", role: Pegawai, expected: False)` | PASS |
| TC-DOM-040 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-return", role: Dapur, expected: False)` | PASS |
| TC-DOM-041 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-return", role: Pegawai, expected: False)` | PASS |
| TC-DOM-042 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-return", role: Owner, expected: True)` | PASS |
| TC-DOM-043 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-return", role: Admin, expected: True)` | PASS |
| TC-DOM-044 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-cancel", role: Admin, expected: True)` | PASS |
| TC-DOM-045 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-cancel", role: Dapur, expected: False)` | PASS |
| TC-DOM-046 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales-cancel", role: Pegawai, expected: False)` | PASS |
| TC-DOM-047 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales", role: Dapur, expected: False)` | PASS |
| TC-DOM-048 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales", role: Pegawai, expected: True)` | PASS |
| TC-DOM-049 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "sales", role: Admin, expected: True)` | PASS |
| TC-DOM-050 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "finance-write", role: Admin, expected: True)` | PASS |
| TC-DOM-051 | RolePoliciesTests | `Allows_SesuaiMatriksKapabilitas(policy: "administration", role: Owner, expected: True)` | PASS |
| TC-DOM-052 | RolePoliciesTests | `SetiapKebijakanPunyaDaftarPeranDanNamaTerdaftar` | PASS |
| TC-DOM-053 | RolePoliciesTests | `NamaKebijakanTidakBolehBerubahDiamDiam` | PASS |
| TC-DOM-054 | RolePoliciesTests | `KebijakanTidakDikenal_MelemparBukanMengizinkan` | PASS |
| TC-DOM-055 | RolePoliciesTests | `OwnerSelaluBolehSemuaKebijakan` | PASS |
| TC-DOM-056 | RolePoliciesTests | `SetiapKebijakanPunyaPalingSedikitSatuPenolak` | PASS |
| TC-DOM-057 | UnitTest1 | `Test1` | PASS |

### Infrastructure (SQLite, migrasi, cadangan) — 91 kasus

| ID | Kelas | Kasus | Status |
|---|---|---|---|
| TC-INF-001 | DatabaseBackupTests | `CreateBackup_MenyertakanDataDanLolosIntegritas` | PASS |
| TC-INF-002 | DatabaseBackupTests | `Restore_MengembalikanDataLama_DanMembuatSalinanPengaman` | PASS |
| TC-INF-003 | DatabaseBackupTests | `Restore_MenolakBerkasBukanDatabase_TanpaMengubahData` | PASS |
| TC-INF-004 | DatabaseBackupTests | `Restore_MenolakCadanganDariSkemaLebihBaru` | PASS |
| TC-INF-005 | DatabaseBackupTests | `Migrate_MembuatCadanganPraUpgrade_DanTidakMenghapusData` | PASS |
| TC-INF-006 | DatabaseBackupTests | `Migrate_MenolakBasisDataDariVersiLebihBaru_TanpaMengubahApaPun` | PASS |
| TC-INF-007 | DatabaseIntegrityTests | `BasisDataHasilMigrasi_SemuaPemeriksaanLolos` | PASS |
| TC-INF-008 | DatabaseIntegrityTests | `JurnalTidakSeimbang_Tertangkap` | PASS |
| TC-INF-009 | DatabaseIntegrityTests | `JurnalPostedTanpaBaris_Tertangkap` | PASS |
| TC-INF-010 | DatabaseIntegrityTests | `JurnalMengarahKeDokumenYangHilang_Tertangkap` | PASS |
| TC-INF-011 | DatabaseIntegrityTests | `NomorDokumenKembar_DitolakSkemaDanDilaporkanSehat` | PASS |
| TC-INF-012 | DatabaseIntegrityTests | `PenghitungDokumenTertinggalDariIsinya_Tertangkap` | PASS |
| TC-INF-013 | DatabaseIntegrityTests | `MigrasiBelumSelesaiPenuh_Tertangkap` | PASS |
| TC-INF-014 | DatabaseIntegrityTests | `BarisYatimTanpaKunciAsingAktif_Tertangkap` | PASS |
| TC-INF-015 | DatabaseMigratorTests | `SplitStatements_SemicolonInsideCommentOrString_KeepsStatementWhole` | PASS |
| TC-INF-016 | DatabaseMigratorTests | `SplitStatements_MigrationFileFromDisk_HasNoFragmentStartingWithProse` | PASS |
| TC-INF-017 | DatabaseMigratorTests | `SplitStatements_TriggerBody_KeepsBeginEndWhole` | PASS |
| TC-INF-018 | DatabaseMigratorTests | `SplitStatements_TriggerBodyWithCommentWordBegin_StaysWhole` | PASS |
| TC-INF-019 | DatabaseMigratorTests | `SplitStatements_TriggerBodyWithCaseEnd_StaysWhole` | PASS |
| TC-INF-020 | DatabaseMigratorTests | `Migrate_EmptyDatabase_ReachesVersionTertinggiDenganStokProdukJadiLotDanSesi` | PASS |
| TC-INF-021 | DatabaseMigratorTests | `Migrate_BasisDataVersiLama_SekaliJalanMenutupKekosongan` | PASS |
| TC-INF-022 | DatabaseMigratorTests | `Rollback013Lalu012Lalu011_MenghapusSkemaDanKembaliNaikTanpaSisa` | PASS |
| TC-INF-023 | DatabaseMigratorTests | `SalesOrders_JumlahNol_DitolakPemicuBukanHanyaValidasiAplikasi` | PASS |
| TC-INF-024 | DatabaseMigratorTests | `FinishedGoodsStock_TidakBolehNegatifDanUnikPerLokasiProduk` | PASS |
| TC-INF-025 | DatabaseMigratorTests | `Migrate_BentukBangunanDasar_TerpasangDariKosong` | PASS |
| TC-INF-026 | DatabaseMigratorTests | `Migrate_RerunOnSameDatabase_IsIdempotent` | PASS |
| TC-INF-027 | DatabaseMigratorTests | `LocationStock_CheckConstraint_RejectsNegativeQuantityAtDatabaseLevel` | PASS |
| TC-INF-028 | DocumentSequenceTests | `NomorBertambahSatuPerPanggilan` | PASS |
| TC-INF-029 | DocumentSequenceTests | `SetiapJenisDokumenPunyaPenghitungSendiri` | PASS |
| TC-INF-030 | DocumentSequenceTests | `GantiHariMulaiDariUrutanSatuLagi` | PASS |
| TC-INF-031 | DocumentSequenceTests | `PenulisanBersamaanTidakPernahMemakaiNomorYangSama` | PASS |
| TC-INF-032 | ExportOrdersPdfTests | `EksporPenjualanPdf_MenghasilkanBerkasPdf` | PASS |
| TC-INF-033 | FinishedGoodsFlowTests | `SelesaiSPK_ProdukJadiMasukEtalaseDenganNilaiHppLengkap` | PASS |
| TC-INF-034 | FinishedGoodsFlowTests | `JurnalPenyelesaianSPK_MemuatTenagaKerjaDanOverheadSertaSeimbang` | PASS |
| TC-INF-035 | FinishedGoodsFlowTests | `PerubahanStatusMenjadiProses_KurangiProdukJadiBukanBahanMentah` | PASS |
| TC-INF-036 | FinishedGoodsFlowTests | `Pembatalan_KembalikanStokTepatSatuKali` | PASS |
| TC-INF-037 | FinishedGoodsFlowTests | `StokEtalaseKurang_StatusDanSaldoTidakBerubahSamaSekali` | PASS |
| TC-INF-038 | FinishedGoodsFlowTests | `PesananLangsungLunas_EtalaseTerpotongSaatDibuat` | PASS |
| TC-INF-039 | FinishedGoodsFlowTests | `PelunasanLewatPembayaran_MengurangiEtalaseSekaliSaja` | PASS |
| TC-INF-040 | FinishedGoodsFlowTests | `JumlahPesananNol_DitolakSebelumMenyentuhBasisData` | PASS |
| TC-INF-041 | IdempotencyRepositoryTests | `KunciBaru_DipesanSekali_SisanyaDitolak` | PASS |
| TC-INF-042 | IdempotencyRepositoryTests | `AksiBerhasil_KunciMenutupDanMenyimpanIdDokumen` | PASS |
| TC-INF-043 | IdempotencyRepositoryTests | `AksiGagal_KunciDilepasPemilikBolehMencobaLagi` | PASS |
| TC-INF-044 | IdempotencyRepositoryTests | `AksiSudahSelesai_PelepasanTidakMenghapusKunci` | PASS |
| TC-INF-045 | IdempotencyRepositoryTests | `KunciBelumPernahDipakai_BacaMengembalikanNull` | PASS |
| TC-INF-046 | IntegrityTriggerTests | `Pesanan_Bernol_Nol_Ditolak` | PASS |
| TC-INF-047 | IntegrityTriggerTests | `Uang_Muka_Melebihi_Nilai_Pesanan_Ditolak` | PASS |
| TC-INF-048 | IntegrityTriggerTests | `Pembayaran_Melebihi_Sisa_Bayar_Ditolak_Tepat_Sisa_Diterima` | PASS |
| TC-INF-049 | IntegrityTriggerTests | `Pembayaran_Bernominal_Nol_Ditolak` | PASS |
| TC-INF-050 | IntegrityTriggerTests | `Bahan_Dengan_Harga_Negatif_Ditolak` | PASS |
| TC-INF-051 | IntegrityTriggerTests | `Resep_Dengan_Jumlah_Nol_Ditolak` | PASS |
| TC-INF-052 | IntegrityTriggerTests | `Penerimaan_Barang_Melebihi_Pesanan_Ditolak` | PASS |
| TC-INF-053 | IntegrityTriggerTests | `Baris_Penerimaan_Barang_Kosong_Ditolak` | PASS |
| TC-INF-054 | IntegrityTriggerTests | `Spk_Dengan_Target_Nol_Ditolak` | PASS |
| TC-INF-055 | JournalReadStatusTests | `EntriDraft_TidakMunculDiSemuaJalurBaca` | PASS |
| TC-INF-056 | JournalReadStatusTests | `SaldoAkun_MengabaikanEntriDraft` | PASS |
| TC-INF-057 | JournalReadStatusTests | `SaldoAkun_EntriDibalikDanPembaliknya_SalingMeniadakan` | PASS |
| TC-INF-058 | Migration014Tests | `Migrasi014_MenyediakanTabelAntreanDanKolomBarcode` | PASS |
| TC-INF-059 | Migration014Tests | `Migrasi014_MengisiPengaturanPerangkatBawaan` | PASS |
| TC-INF-060 | Migration014Tests | `BarcodeGanda_DitolakBasisData` | PASS |
| TC-INF-061 | Migration014Tests | `BarcodeGandaBedaHurufBesarKecil_Ditolak` | PASS |
| TC-INF-062 | Migration014Tests | `ProdukTanpaBarcode_BolehBanyak` | PASS |
| TC-INF-063 | Migration014Tests | `PrintJobs_MenolakStatusDanLebarDiLuarDaftar` | PASS |
| TC-INF-064 | Migration014Tests | `Rollback014_MencabutAntreanBarcodeDanPengaturan` | PASS |
| TC-INF-065 | Migration015Tests | `Migrasi015_MenyediakanTabelNotifikasiDanIndeksnya` | PASS |
| TC-INF-066 | Migration015Tests | `Notifikasi_TerikatKePenggunaPenerima` | PASS |
| TC-INF-067 | Migration015Tests | `Notifikasi_MenolakSeverityDanIsReadDiLuarDaftar` | PASS |
| TC-INF-068 | Migration015Tests | `Notifikasi_KunciDedupeKembar_DitolakBasisData` | PASS |
| TC-INF-069 | Migration015Tests | `Notifikasi_TanpaPenerimaYangAda_Ditolak` | PASS |
| TC-INF-070 | Migration015Tests | `Rollback015_MencabutTabelIndeksDanVersi` | PASS |
| TC-INF-071 | NotificationRepositoryTests | `Simpan_KunciDedupeSama_TidakMenumpuk` | PASS |
| TC-INF-072 | NotificationRepositoryTests | `Daftar_HanyaMemuatKotakPenggunaSendiri` | PASS |
| TC-INF-073 | NotificationRepositoryTests | `Ringkasan_MenghitungTotalBelumDibacaDanKritis` | PASS |
| TC-INF-074 | NotificationRepositoryTests | `TandaiDibaca_TidakBisaMenyentuhKotakPenggunaLain` | PASS |
| TC-INF-075 | NotificationRepositoryTests | `TandaiSemuaDibaca_DanFilterBelumDibaca` | PASS |
| TC-INF-076 | NotificationRepositoryTests | `TersimpanPermanen_SetelahKoneksiDibukaUlang` | PASS |
| TC-INF-077 | NotificationServiceTests | `Sinkron_StokMenipis_HanyaUntukPeranGudang_SekaliSehari` | PASS |
| TC-INF-078 | NotificationServiceTests | `Sinkron_BatchKedaluwarsa_MenjadiKritis` | PASS |
| TC-INF-079 | NotificationServiceTests | `Sinkron_RingkasanHarian_Kemarin_HanyaUntukPemilikDanAdmin` | PASS |
| TC-INF-080 | NotificationServiceTests | `PenggunaNonaktif_TidakMenerimaPengingat` | PASS |
| TC-INF-081 | ProductDeleteTests | `Delete_MenuNonaktifkanProdukDanProdukHilangDariDaftarAktif` | PASS |
| TC-INF-082 | ProductDeleteTests | `Update_TidakMengubahStatusAktifYangSudahDisimpan` | PASS |
| TC-INF-083 | ProductDeleteTests | `Update_MenggantiBarisResepTanpaMenyisakanDuplikat` | PASS |
| TC-INF-084 | StockLedgerTests | `ApplyAsync_Restock_UpdatesBalanceAndWritesOneLedgerLeg` | PASS |
| TC-INF-085 | StockLedgerTests | `ApplyAsync_Overdraft_ThrowsWithoutLeavingAnyTrace` | PASS |
| TC-INF-086 | StockLedgerTests | `ApplyAsync_LegAfterFailedLeg_RollsBackWholeBatch` | PASS |
| TC-INF-087 | StockLedgerTests | `TransferStockAsync_WritesTwoSignedLegsInOneTransaction` | PASS |
| TC-INF-088 | StockLedgerTests | `TransferStockAsync_SameLocation_Throws` | PASS |
| TC-INF-089 | StockLedgerTests | `AdjustStockAsync_NegativeTarget_Throws` | PASS |
| TC-INF-090 | StockLedgerTests | `GetLocationIdByTypeAsync_UnknownType_ThrowsInsteadOfMagicId` | PASS |
| TC-INF-091 | StockLedgerTests | `Ingredient_CurrentStock_IsDerivedFromLocationStockNotCacheColumn` | PASS |

### UI + API (host hidup, Playwright) — 71 kasus

| ID | Kelas | Kasus | Status |
|---|---|---|---|
| TC-UI-001 | MaintenanceArgumentsTests | `PathBasisData_DibacaWalauTepatSetelahSaklarTanpaNilai` | PASS |
| TC-UI-002 | MaintenanceArgumentsTests | `PathBasisData_KosongAtauTidakAda_MengembalikanNull` | PASS |
| TC-UI-003 | MaintenanceArgumentsTests | `PathBasisData_MenerimaHurufKecilDanTandaKutip` | PASS |
| TC-UI-004 | Tests | `Batch7ApiTests.Qc_TersimpanPerSpk_DanKeputusanDihitungServer` | PASS |
| TC-UI-005 | Tests | `Batch7ApiTests.Qc_HanyaUntukPeranProduksi` | PASS |
| TC-UI-006 | Tests | `Batch7ApiTests.Hpp_MemakaiBiayaSpkSelesaiBukanAngkaPerkiraan` | PASS |
| TC-UI-007 | Tests | `Batch7ApiTests.Retur_MengembalikanUangStokDanJurnal` | PASS |
| TC-UI-008 | Tests | `Batch7ApiTests.Retur_HanyaPesananLunasDanHanyaOwnerAdmin` | PASS |
| TC-UI-009 | Tests | `Batch7ApiTests.Logout_MencabutTokenDiSemuaSalinan` | PASS |
| TC-UI-010 | Tests | `Batch7ApiTests.Kasir_HanyaMelihatPesananYangIaCatatSendiri` | PASS |
| TC-UI-011 | Tests | `Batch7ApiTests.KebijakanSandi_DitegakkanServerSaatMembuatDanMeresetAkun` | PASS |
| TC-UI-012 | Tests | `Batch7ApiTests.PenulisanSerentak_KunciSamaHanyaSatuPesanan` | PASS |
| TC-UI-013 | Tests | `Batch7ApiTests.PenulisanSerentak_PelunasanGandaTidakMencatatDuaPembayaran` | PASS |
| TC-UI-014 | Tests | `Batch7ApiTests.PenulisanSerentak_PenyelesaianSpkTidakMenggandakanHasil` | PASS |
| TC-UI-015 | Tests | `Batch8DeviceApiTests.Kasir_TidakBolehMelihatDaftarPrinterAtauMengubahPengaturan` | PASS |
| TC-UI-016 | Tests | `Batch8DeviceApiTests.Kasir_BolehMelihatStatusRingkasYangDibutuhkanLayarPenjualan` | PASS |
| TC-UI-017 | Tests | `Batch8DeviceApiTests.LebarKertasTidakDidukung_DitolakDanNilaiLamaTetap` | PASS |
| TC-UI-018 | Tests | `Batch8DeviceApiTests.KunciPengaturanAsing_Ditolak` | PASS |
| TC-UI-019 | Tests | `Batch8DeviceApiTests.ModeEscPosTanpaAlamat_DitolakKarenaBelumLengkap` (nama 2026-10-28: `ModeEscPosLangsung_DitolakKarenaBelumTerverifikasi`; diganti 2026-11-01) | PASS |
| TC-UI-020 | Tests | `Batch8DeviceApiTests.PrinterTidakTerpasang_Ditolak409TanpaMengubahPengaturan` | PASS |
| TC-UI-021 | Tests | `Batch8DeviceApiTests.CetakTanpaPrinter_PesananTetapUtuhDanTidakAdaPekerjaanDibuat` | PASS |
| TC-UI-022 | Tests | `Batch8DeviceApiTests.CetakUjiTanpaPrinter_DitolakDanTidakMencatatPekerjaan` | PASS |
| TC-UI-023 | Tests | `Batch8DeviceApiTests.Kasir_TidakBisaMencetakStrukPesananKasirLain` | PASS |
| TC-UI-024 | Tests | `Batch8DeviceApiTests.BarcodeGandaLewatApi_Ditolak409DanProdukKeduaTidakDibuat` | PASS |
| TC-UI-025 | Tests | `Batch8DeviceApiTests.PengaturanPerangkat_TersimpanDanBertahanSetelahDibacaUlang` | PASS |
| TC-UI-026 | Tests | `Batch8DeviceUiTests.HalamanPerangkat_MenampilkanPrinterDariServerDanMenyimpanPilihan` | PASS |
| TC-UI-027 | Tests | `Batch8DeviceUiTests.PindaiBarcode_MengisiProdukDanTotalDariKatalog` | PASS |
| TC-UI-028 | Tests | `DashboardTests.Dashboard_Loads_ShowsKeyMetrics` | PASS |
| TC-UI-029 | Tests | `DashboardTests.Dashboard_ApiData_IsNotHardcoded` | PASS |
| TC-UI-030 | Tests | `IdempotencyApiTests.KunciSama_DuaKaliHanyaSatuPesananDanKlikKeduaMendapatJawaban` | PASS |
| TC-UI-031 | Tests | `IdempotencyApiTests.PermintaanDitolak_KunciDilepasPemilikBolehMenyimpanLagi` | PASS |
| TC-UI-032 | Tests | `IdempotencyApiTests.KunciSama_RuteBerbedaDitolakBukanDijalankan` | PASS |
| TC-UI-033 | Tests | `IdempotencyApiTests.KunciTidakSah_DitolakSebelumMenyentuhBasisData` | PASS |
| TC-UI-034 | Tests | `IdempotencyApiTests.TanpaKunci_PerilakuLamaTetapJalan` | PASS |
| TC-UI-035 | Tests | `IdempotencyApiTests.EndpointYangTidakDikenal_MenjawabJsonBukanHtmlAplikasi` | PASS |
| TC-UI-036 | Tests | `IdempotencyApiTests.PeriksaDiriBasisData_MelaporkanSemuaPemeriksaanUntukOwner` | PASS |
| TC-UI-037 | Tests | `IdempotencyApiTests.PeriksaDiriButuhLogin` | PASS |
| TC-UI-038 | Tests | `IdempotencyUiTests.KlikGandaSimpanPesanan_HanyaSatuPermintaanDanSatuBarisPesanan` | PASS |
| TC-UI-039 | Tests | `IdempotencyUiTests.MembukaFormLagi_KunciBergantiPesananKeduaTetapTersimpan` | PASS |
| TC-UI-040 | Tests | `LoginTests.Login_OnLoad_HasNoPrefilledCredentials` | PASS |
| TC-UI-041 | Tests | `LoginTests.Login_WithValidCredentials_RedirectsToDashboard` | PASS |
| TC-UI-042 | Tests | `LoginTests.Login_WithInvalidCredentials_ShowsError` | PASS |
| TC-UI-043 | Tests | `McpStdioTests.Inisialisasi_ToolsList_DanPemanggilanSah` | PASS |
| TC-UI-044 | Tests | `McpStdioTests.Penolakan_ToolAsingArgumenSalahDanMetodeTakDikenal` | PASS |
| TC-UI-045 | Tests | `McpStdioTests.TanpaAkunAgen_SemuaToolDitolak` | PASS |
| TC-UI-046 | Tests | `McpStdioTests.Pegawai_DitolakLaporanHarian_TetapiTugasTetapBoleh_DanSemuaDicatatAudit` | PASS |
| TC-UI-047 | Tests | `McpStdioTests.NotifikasiDariTransaksiHost_TerbacaDariProsesMcp` | PASS |
| TC-UI-048 | Tests | `NavigationTests.Sidebar_ClickSalesMenu_ShowsSalesView` | PASS |
| TC-UI-049 | Tests | `NavigationTests.Sidebar_ClickInventoryMenu_ShowsInventoryView` | PASS |
| TC-UI-050 | Tests | `NavigationTests.Sidebar_ClickProductionMenu_ShowsProductionView` | PASS |
| TC-UI-051 | Tests | `NavigationTests.Sidebar_ClickFinanceMenu_ShowsFinanceView` | PASS |
| TC-UI-052 | Tests | `NavigationTests.Sidebar_PeranPemilik_MenampilkanLabelDanMenuSistem` | PASS |
| TC-UI-053 | Tests | `NavigationTests.Sidebar_PeranPegawai_MenyembunyikanMenuAdministrasiDanDitolakServer` | PASS |
| TC-UI-054 | Tests | `NotificationApiTests.TanpaSesi_Ditolak` | PASS |
| TC-UI-055 | Tests | `NotificationApiTests.TransaksiNyata_MenulisNotifikasi_DanStatusBacaBisaDitelusuri` | PASS |
| TC-UI-056 | Tests | `NotificationApiTests.PenyegaranPengingat_BerulangTidakMenggandakanPengingat` | PASS |
| TC-UI-057 | Tests | `ProductValidationTests.TanpaDaftarResep_Ditolak400Bukan500` | PASS |
| TC-UI-058 | Tests | `ProductValidationTests.NamaKosong_HargaNegatif_DanJumlahBahanTidakSah_Ditolak400` | PASS |
| TC-UI-059 | Tests | `ProductValidationTests.ProdukSah_TetapDiterima_DanUbahTanpaResepJugaDitolak400` | PASS |
| TC-UI-060 | Tests | `PurchaseCancelApiTests.Pembatalan_DitolakSaatSebagianBarangSudahDiterima` | PASS |
| TC-UI-061 | Tests | `PurchaseCancelApiTests.Pembatalan_DitolakSetelahDiterimaPenuh` | PASS |
| TC-UI-062 | Tests | `PurchaseCancelApiTests.Pembatalan_JalanUntukPoYangBelumAdaBarangMasuk` | PASS |
| TC-UI-063 | Tests | `RbacApiTests.Kasir_HanyaBisaMembacaDataPenjualan` | PASS |
| TC-UI-064 | Tests | `RbacApiTests.Kasir_TidakBisaMenembakEndpointTulisTerlarang` | PASS |
| TC-UI-065 | Tests | `RbacApiTests.Kasir_TidakBisaMenawarHargaSendiri` | PASS |
| TC-UI-066 | Tests | `RbacApiTests.Kasir_TidakBisaMenandaiLunasTanpaPembayaran` | PASS |
| TC-UI-067 | Tests | `RbacApiTests.TokenLama_KehilanganHakSaatPeranTurunAtauAkunNonaktif` | PASS |
| TC-UI-068 | Tests | `RbacApiTests.Penolakan_TercatatDiJejakAudit` | PASS |
| TC-UI-069 | Tests | `SalesMoneyTests.OrderForm_RupiahTyped_IsStoredAsMinorUnitsAndShownBack` | PASS |
| TC-UI-070 | Tests | `SalesPickupDateTests.FormMengirimTanggalPickup_TanpaJamDanTanpaGeserHari` | PASS |
| TC-UI-071 | Tests | `SalesPickupDateTests.Api_MenerimaTanggalSaja_DanMenyimpannyaUtuh` | PASS |

## B. Pemeriksaan perilaku API (137)

| ID | Skrip | Pemeriksaan | Status | Detail |
|---|---|---|---|---|
| TC-BATCH2-001 | batch2 | login admin | PASS | user 1 0 |
| TC-BATCH2-002 | batch2 | gerbang sandi menolak endpoint data sebelum ganti sandi | PASS | http=403 Kata sandi akun ini masih bawaan. Ganti kata sandi lebih dulu melalui menu ganti  |
| TC-BATCH2-003 | batch2 | ganti sandi bawaan lewat API | PASS |  |
| TC-BATCH2-004 | batch2 | sandi baru membuka endpoint data | PASS | mustChangePassword=false |
| TC-BATCH2-005 | batch2 | lokasi tersediaan | PASS | kitchen=2 warehouse=1 display=3 |
| TC-BATCH2-006 | batch2 | bahan + produk uji dibuat | PASS | ingredient=1 product=1 |
| TC-BATCH2-007 | batch2 | stok awal dapur diisi | PASS |  |
| TC-BATCH2-008 | batch2 | pesanan bernilai 0 ditolak | PASS | Nilai pesanan harus lebih besar dari nol. |
| TC-BATCH2-009 | batch2 | uang muka melebihi nilai pesanan ditolak | PASS | Uang muka melebihi nilai pesanan. |
| TC-BATCH2-010 | batch2 | nominal menembus atap ditolak | PASS | Nilai pesanan melebihi batas 10,000,000,000 rupiah. |
| TC-BATCH2-011 | batch2 | pesanan + DP tersimpan | PASS | order=1 {"id":1} |
| TC-BATCH2-012 | batch2 | jurnal DP ikut tertulis dalam transaksi yang sama | PASS | entri 0 -> 1 |
| TC-BATCH2-013 | batch2 | entri DP menautkan SourceDocumentId pesanan | PASS | SalesOrder#1 :: DP Pesanan: Bu Sari |
| TC-BATCH2-014 | batch2 | SPK target 0 ditolak | PASS | Target jumlah produksi harus lebih besar dari nol. |
| TC-BATCH2-015 | batch2 | SPK biaya tenaga kerja negatif ditolak | PASS | Biaya tenaga kerja tidak boleh negatif. |
| TC-BATCH2-016 | batch2 | SPK dibuat | PASS | spk=1 |
| TC-BATCH2-017 | batch2 | alasan scrap tak dikenal ditolak dengan pesan terbaca | PASS | Alasan scrap tidak dikenal: Rusak panggang. Pilihan: Gosong, Adonan_Gagal, Salah_Resep, Me |
| TC-BATCH2-018 | batch2 | scrap ditolak tidak menulis jurnal | PASS | entri 1 -> 1 |
| TC-BATCH2-019 | batch2 | scrap lebih kecil dari produk rusak ditolak | PASS | Jumlah scrap lebih kecil dari produk rusak yang dilaporkan. |
| TC-BATCH2-020 | batch2 | penyelesaian SPK mencatat jurnal | PASS | http=200 {"message":"Work order completed successfully"} :: 1-004 D0/K12000000 1-006 D9600 |
| TC-BATCH2-021 | batch2 | hasil produksi menambah stok produk jadi di etalase | PASS | stok produk 0 -> 4 |
| TC-BATCH2-022 | batch2 | jurnal SPK seimbang dan memecah biaya bahan dengan benar | PASS | debit=12150000 kredit=12150000 fg=9600000 scrap=2400000 dapur=12000000 |
| TC-BATCH2-023 | batch2 | SPK selesai ulang ditolak oleh penjaga status | PASS | SPK berstatus Completed tidak dapat diselesaikan ulang. |
| TC-BATCH2-024 | batch2 | SPK selesai ulang tidak menambah jurnal | PASS | entri 2 -> 2 |
| TC-BATCH2-025 | batch2 | pembayaran melebihi sisa bayar ditolak | PASS | Pembayaran Rp 5.000 melebihi sisa bayar Rp 1.500. |
| TC-BATCH2-026 | batch2 | pembayaran nol ditolak | PASS | Pembayaran harus lebih besar dari nol. |
| TC-BATCH2-027 | batch2 | pembayaran sebagian diterima | PASS |  |
| TC-BATCH2-028 | batch2 | satu entri jurnal per pembayaran parsial | PASS | entri 3 |
| TC-BATCH2-029 | batch2 | pembayaran menautkan JournalEntryId | PASS | payment.journalEntryId=3 |
| TC-BATCH2-030 | batch2 | pelunasan mengubah status jadi Paid | PASS | status=3 |
| TC-BATCH2-031 | batch2 | pengakuan pendapatan tercatat saat lunas | PASS | total entri 5 |
| TC-BATCH2-032 | batch2 | pelunasan memotong stok produk jadi sebanyak jumlah pesanan | PASS | stok produk 4 -> 3 untuk 1 unit |
| TC-BATCH2-033 | batch2 | pembayaran ganda setelah lunas ditolak | PASS | Pesanan sudah lunas. |
| TC-BATCH2-034 | batch2 | idempotensi: pembayaran kunci sama tidak menulis ulang | PASS | entri tetap 5 |
| TC-BATCH2-035 | batch2 | total debit = total kredit di seluruh jurnal | PASS | debit=12550000 kredit=12550000 entri=5 |
| TC-BATCH2-036 | batch2 | pemrosesan pesanan memotong stok produk jadi di etalase | PASS | stok produk 3 -> 2 |
| TC-BATCH2-037 | batch2 | pembatalan mengembalikan stok produk jadi | PASS | stok produk 2 -> 3 (awal 3) |
| TC-BATCH2-038 | batch2 | pembatalan menulis entri pembalik | PASS | 1 pembalik, entri asli berstatus Reversed=0 |
| TC-BATCH2-039 | batch2 | pembatalan ganda ditolak | PASS | Pesanan sudah dibatalkan. |
| TC-BATCH2-040 | batch2 | pembatalan ganda tidak menambah jurnal | PASS | entri 6 |
| TC-BATCH2-041 | batch2 | stok tidak cukup ditolak dengan pesan jelas | PASS | Stok produk Kue Keju Uji di Etalase Toko & Kasir tidak cukup: tersedia 0, dibutuhkan 1. |
| TC-BATCH2-042 | batch2 | status pesanan tidak berubah saat stok ditolak | PASS | status=0, stockDeducted=false |
| TC-BATCH2-043 | batch2 | transaksi gagal tidak meninggalkan mutasi stok | PASS | mutasi 3 -> 3 |
| TC-BATCH2-044 | batch2 | transaksi gagal tidak meninggalkan jurnal | PASS | entri 6 -> 6 |
| TC-BATCH2-045 | batch2 | biaya negatif ditolak | PASS | Nominal biaya harus lebih besar dari nol. |
| TC-BATCH2-046 | batch2 | biaya tanpa keterangan ditolak | PASS | Keterangan biaya wajib diisi. |
| TC-BATCH2-047 | batch2 | biaya sah tercatat dan menjurnal | PASS | akun 1-001+5-001 |
| TC-BATCH2-048 | batch2 | kategori HPP memakai akun 5-001 | PASS | akun=1-001+5-001 |
| TC-BATCH2-049 | batch2 | PO dibuat | PASS | po=1 {"id":1} |
| TC-BATCH2-050 | batch2 | baris PO berjumlah 0 ditolak | PASS | Jumlah pesan harus lebih besar dari nol. |
| TC-BATCH2-051 | batch2 | baris PO tanpa satuan ditolak | PASS | Satuan bahan wajib diisi (kg, pcs, liter). |
| TC-BATCH2-052 | batch2 | penerimaan melebihi sisa terima ditolak | PASS | Jumlah 999 kg melebihi sisa terima 2.5 kg untuk Tepung Terigu Uji. |
| TC-BATCH2-053 | batch2 | penerimaan ditolak tidak mengubah stok | PASS | stok 50 -> 50 |
| TC-BATCH2-054 | batch2 | penerimaan ditolak tidak meninggalkan jurnal | PASS | entri 7 |
| TC-BATCH2-055 | batch2 | baris PO diisi bahan lain ditolak | PASS | Baris PO untuk Tepung Terigu Uji diisi bahan lain (Id 2). |
| TC-BATCH2-056 | batch2 | penerimaan barang sah diterima | PASS |  |
| TC-BATCH2-057 | batch2 | stok gudang bertambah sesuai jumlah terima | PASS | 50 -> 52.5 |
| TC-BATCH2-058 | batch2 | status PO jadi Received | PASS | status=3 |
| TC-BATCH2-059 | batch2 | jurnal Dr Persediaan / Cr Utang tercatat | PASS | 1-003:3000123/0 2-002:0/3000123 |
| TC-BATCH2-060 | batch2 | nilai penerimaan membulatkan pecahan satuan (2,5 x 1200049 = 3000123) | PASS | debit=3000123 (pembulatan ke bawah memberi 3000122) |
| TC-BATCH2-061 | batch2 | saldo akhir jurnal tetap seimbang | PASS | debit=15645123 kredit=15645123 entri=8 |
| TC-BATCH2-062 | batch2 | saldo akun sesuai arus kas dan dokumen yang tercatat | PASS | kas=-25000 (harus -25000) utang=3000123 pendapatan=200000 |
| TC-BATCH3-001 | batch3 | login sandi bawaan menandai ganti sandi wajib | PASS | role=0 |
| TC-BATCH3-002 | batch3 | token bawaan ditolak 403 untuk endpoint data | PASS | http=403 Kata sandi akun ini masih bawaan. Ganti kata sandi lebih dulu melalui menu ganti  |
| TC-BATCH3-003 | batch3 | buku besar juga tertutup untuk token bawaan | PASS | http=403 |
| TC-BATCH3-004 | batch3 | jalur ekspor tertutup untuk token bawaan | PASS | http=403 |
| TC-BATCH3-005 | batch3 | /health tetap terbuka (probe tanpa data) | PASS | http=200 |
| TC-BATCH3-006 | batch3 | sandi pengganti di bawah 8 karakter ditolak | PASS | Kata sandi minimal 8 karakter. |
| TC-BATCH3-007 | batch3 | sandi pengganti tanpa angka ditolak | PASS | Kata sandi harus memuat huruf dan angka. |
| TC-BATCH3-008 | batch3 | sandi lama salah ditolak | PASS | http=400 Password lama salah. |
| TC-BATCH3-009 | batch3 | ganti sandi lewat saat gerbang aktif | PASS | http=200  |
| TC-BATCH3-010 | batch3 | sandi bawaan tidak lagi bisa masuk | PASS | http=401 Username atau password salah. |
| TC-BATCH3-011 | batch3 | setelah ganti sandi, token tidak lagi berstatus wajib ganti | PASS | mustChangePassword=false |
| TC-BATCH3-012 | batch3 | endpoint data terbuka setelah sandi diganti | PASS | http=200 |
| TC-BATCH3-013 | batch3 | akun uji owner_b3 masuk sebagai Owner | PASS | claim=Owner |
| TC-BATCH3-014 | batch3 | akun uji admin_b3 masuk sebagai Admin | PASS | claim=Admin |
| TC-BATCH3-015 | batch3 | akun uji dapur_b3 masuk sebagai Dapur | PASS | claim=Dapur |
| TC-BATCH3-016 | batch3 | akun uji pegawai_b3 masuk sebagai Pegawai | PASS | claim=Pegawai |
| TC-BATCH3-017 | batch3 | tanpa token seluruh API ditolak 401 | PASS | http=401 |
| TC-BATCH3-018 | batch3 | GET /api/users tidak membocorkan PasswordHash | PASS | http=200 body=[{"id":1,"legacyId":null,"username":"admin","role":0,"isActive":true,"mustCh |
| TC-BATCH3-019 | batch3 | data dasar untuk uji peran siap | PASS | bahan=1 produk=1 supplier=1 |
| TC-BATCH3-020 | batch3 | Pegawai membuat pesanan (sales) | PASS | http=201 (harus boleh)  |
| TC-BATCH3-021 | batch3 | Pegawai tidak bisa menawar harga pesanan | PASS | http=400 Total pesanan harus Rp 5.000 (Roti Uji B3 A- 1). Perubahan harga hanya bisa dilak |
| TC-BATCH3-022 | batch3 | Dapur membuat pesanan (sales) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-023 | batch3 | Pegawai membatalkan pesanan (sales-cancel) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-024 | batch3 | percobaan batal oleh Pegawai tidak mengubah status | PASS | status=undefined |
| TC-BATCH3-025 | batch3 | Admin membatalkan pesanan (sales-cancel) boleh | PASS | http=200 (harus boleh)  |
| TC-BATCH3-026 | batch3 | Pegawai membaca jurnal (finance-read) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-027 | batch3 | Dapur membaca saldo akun (finance-read) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-028 | batch3 | Dapur mencatat biaya (finance-write) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-029 | batch3 | Admin mencatat biaya (finance-write) boleh | PASS | http=200 (harus boleh)  |
| TC-BATCH3-030 | batch3 | Dapur membuat SPK (production-write) boleh | PASS | http=201 (harus boleh)  |
| TC-BATCH3-031 | batch3 | Pegawai membuat SPK (production-write) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-032 | batch3 | Dapur membatalkan SPK (production-cancel) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-033 | batch3 | Owner membatalkan SPK (production-cancel) boleh | PASS | http=200 (harus boleh)  |
| TC-BATCH3-034 | batch3 | Dapur mengoreksi stok (inventory-write) boleh | PASS | http=200 (harus boleh)  |
| TC-BATCH3-035 | batch3 | Pegawai memindahkan stok (inventory-write) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-036 | batch3 | Dapur membuat PO (purchases-write) boleh | PASS | http=200 (harus boleh)  |
| TC-BATCH3-037 | batch3 | Pegawai membuat PO (purchases-write) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-038 | batch3 | Dapur membatalkan PO (purchases-cancel) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-039 | batch3 | Pegawai membuat bahan (master-data-write) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-040 | batch3 | Admin membuat bahan (master-data-write) boleh | PASS | http=201 (harus boleh)  |
| TC-BATCH3-041 | batch3 | Admin membaca daftar pengguna (administration) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-042 | batch3 | Admin mengubah pengaturan (administration) ditolak | PASS | http=403 (harus 403)  |
| TC-BATCH3-043 | batch3 | Owner mengubah pengaturan (administration) boleh | PASS | http=200 (harus boleh)  |
| TC-BATCH3-044 | batch3 | nomor pesanan berbentuk ORD-tanggal-urut | PASS | ORD-20261028-0003 ORD-20261028-0004 ORD-20261028-0005 ORD-20261028-0006 ORD-20261028-0007 |
| TC-BATCH3-045 | batch3 | nomor pesanan unik dan menaik | PASS | ORD-20261028-0003 ORD-20261028-0004 ORD-20261028-0005 ORD-20261028-0006 ORD-20261028-0007 |
| TC-BATCH3-046 | batch3 | 12 pesanan paralel tanpa galat server | PASS | 201,201,201,201,201,201,201,201,201,201,201,201 |
| TC-BATCH3-047 | batch3 | nomor dari penulisan paralel tetap unik | PASS | ORD-20261028-0008 ORD-20261028-0009 ORD-20261028-0016 ORD-20261028-0018 ORD-20261028-0015  |
| TC-BATCH3-048 | batch3 | urutan nomor rapat (tanpa lompatan akibat tabrakan) | PASS | 8..19 (jumlah 12) |
| TC-BATCH3-049 | batch3 | nomor SPK memakai awalan SPK dan unik | PASS | SPK-20261028-0002 SPK-20261028-0003 SPK-20261028-0004 |
| TC-BATCH3-050 | batch3 | nomor PO memakai awalan PO dan unik | PASS | PO-20261028-0002 PO-20261028-0003 |
| TC-BATCH3-051 | batch3 | penghitung hari ini = jumlah dokumen tersimpan | PASS | Order=19/19 SPK=4/4 PO=3/3 |
| TC-BATCH3-052 | batch3 | Origin asing tidak mendapat Access-Control-Allow-Origin | PASS | http=200 acao=(kosong) |
| TC-BATCH3-053 | batch3 | Origin loopback bawaan tetap diizinkan | PASS | acao=http://localhost:5173 |
| TC-BATCH3-054 | batch3 | preflight asing ditolak tanpa izin | PASS | http=204 acao=(kosong) |
| TC-BATCH6-001 | batch6 | login mengirim daftar kapabilitas | PASS | kapabilitas=["administration","finance-read","finance-write","inventory-read","inventory-w |
| TC-BATCH6-002 | batch6 | pemilik memegang administration & finance-read | PASS | role=0 |
| TC-BATCH6-003 | batch6 | GET /api tasks menjawab struktur | PASS | http=200 jumlah=0 |
| TC-BATCH6-004 | batch6 | GET analytics punya campo pendapatan & laba | PASS | http=200 |
| TC-BATCH6-005 | batch6 | self-check mengembalikan daftar pemeriksaan | PASS | http=200 n=9 |
| TC-BATCH6-006 | batch6 | PUT settings diterapkan | PASS | http=200 diterapkan=1 |
| TC-BATCH6-007 | batch6 | jejak audit mencatat perubahan pengaturan + nilai baru | PASS | http=200 baris=1 |
| TC-BATCH6-008 | batch6 | POST /api/users membuat pegawai (201) | PASS | http=201 {"id":2,"username":"kasir-uji","peran":"Pegawai"} |
| TC-BATCH6-009 | batch6 | daftar user TIDAK mengirim hash sandi | PASS | http=200 n=2 |
| TC-BATCH6-010 | batch6 | Owner terakhir tidak bisa menonaktifkan dirinya | PASS | http=400 Owner terakhir tidak boleh dinonaktifkan; angkat Owner lain lebih dulu. |
| TC-BATCH6-011 | batch6 | pegawai TIDAK memegang administration | PASS | kapabilitas=["sales"] |
| TC-BATCH6-012 | batch6 | audit ditolak 403 untuk pegawai | PASS | http=403 |
| TC-BATCH6-013 | batch6 | analytics ditolak 403 untuk pegawai | PASS | http=403 |
| TC-BATCH6-014 | batch6 | daftar pegawai ditolak 403 untuk pegawai | PASS | http=403 |
| TC-BATCH6-015 | batch6 | periksa-diri ditolak 403 untuk pegawai | PASS | http=403 |
| TC-BATCH6-016 | batch6 | pegawai tetap boleh membuka kotak tugas | PASS | http=200 |
| TC-BATCH6-017 | batch6 | bahan uji tercipta | PASS | http=201 {"id":1} |
| TC-BATCH6-018 | batch6 | lot tercatat | PASS | http=201 {"id":1} |
| TC-BATCH6-019 | batch6 | lot muncul di daftar batch & kedaluwarsa | PASS | http=200 n=1 |
| TC-BATCH6-020 | batch6 | lot dengan kedaluwarsa lampau ditolak | PASS | http=400 |
| TC-BATCH6-021 | batch6 | AuditLogs benar-benar berisi baris | PASS | baris=10 |

## C. Kit mesin bersih (53)

| ID | Pemeriksaan | Status | Detail |
|---|---|---|---|
| TC-KIT-001 | Sistem | INFO | Microsoft Windows 11 Enterprise build 26100; RAM 1.5 GB; arsitektur 64-bit |
| TC-KIT-002 | .NET di PATH | INFO | tidak ada (diharapkan: aplikasi self-contained) |
| TC-KIT-003 | Peramban Edge tersedia | PASS | versi 156.0.4314.50 |
| TC-KIT-004 | Peramban default | INFO | MSEdgeHTM |
| TC-KIT-005 | Belum ada instalasi sebelumnya | PASS | entri HKCU=0 |
| TC-KIT-006 | Belum ada data pengguna sebelumnya | PASS | folder C:\Users\WDAGUtilityAccount\AppData\Local\Seruni22Bakery |
| TC-KIT-007 | Port 5000 bebas | PASS | bebas |
| TC-KIT-008 | Installer ada | PASS | C:\share\Seruni22Bakery_Setup_v1.2.3.exe |
| TC-KIT-009 | SHA-256 sesuai artefak rilis | PASS | 28680DFCD281BC859AB281535D0FB0BB2709E75129B03980EFF8DEA96AD24E83 |
| TC-KIT-010 | Tanda tangan Authenticode | INFO | NotSigned |
| TC-KIT-011 | Installer selesai | PASS | exit=0 |
| TC-KIT-012 | Entri uninstall terdaftar | PASS | Seruni 22 Bakery (Sistem Toko & Pabrik Roti) 1.2.3 / Seruni 22 |
| TC-KIT-013 | Versi terdaftar 1.2.3 | PASS | 1.2.3 |
| TC-KIT-014 | Berkas paket Seruni22Bakery.Host.exe | PASS | ada |
| TC-KIT-015 | Berkas paket Seruni22Bakery.Host.dll | PASS | ada |
| TC-KIT-016 | Berkas paket hostfxr.dll | PASS | ada |
| TC-KIT-017 | Berkas paket coreclr.dll | PASS | ada |
| TC-KIT-018 | Berkas paket appsettings.json | PASS | ada |
| TC-KIT-019 | Berkas paket Seruni22Bakery.exe | PASS | ada |
| TC-KIT-020 | Berkas paket wwwroot\index.html | PASS | ada |
| TC-KIT-021 | Berkas paket app.ico | PASS | ada |
| TC-KIT-022 | Tanpa appsettings.Development.json | PASS | tidak ada |
| TC-KIT-023 | Tanpa PDB | PASS | 0 pdb |
| TC-KIT-024 | Jumlah berkas terpasang | INFO | 397 |
| TC-KIT-025 | Peluncur mandiri tanpa VBScript | PASS | tidak ada berkas .vbs |
| TC-KIT-026 | Peluncur exit 0 | PASS | exit=0, 7 detik |
| TC-KIT-027 | Tanpa pesan galat peluncur | PASS | C:\Users\WDAGUtilityAccount\AppData\Local\Seruni22Bakery\logs\launcher-error.txt |
| TC-KIT-028 | Host menjawab /health | PASS | {"status":"ok","app":"Seruni 22 Bakery","version":"1.2.3"} |
| TC-KIT-029 | Versi aplikasi 1.2.3 | PASS | 1.2.3 |
| TC-KIT-030 | Basis data dibuat | PASS | C:\Users\WDAGUtilityAccount\AppData\Local\Seruni22Bakery\seruni22bakery.db |
| TC-KIT-031 | Kunci JWT dibuat | PASS | jwt.key |
| TC-KIT-032 | Login awal diterima | PASS | role=0 mustChange=True |
| TC-KIT-033 | Gerbang ganti sandi menolak dashboard | PASS | HTTP 403 |
| TC-KIT-034 | Sandi baru berlaku | PASS | mustChange=False |
| TC-KIT-035 | Periksa-diri basis data | PASS | status=ok jumlah=9 gagal=0 |
| TC-KIT-036 | Pesanan tersimpan | PASS | id=1 nomor=ORD-20261028-0001 total=85000 qty=2 |
| TC-KIT-037 | Ekspor PDF | PASS | HTTP 200, 20683 bita |
| TC-KIT-038 | Ekspor Excel | PASS | HTTP 200, 6572 bita |
| TC-KIT-039 | Peluncur kedua exit 0 | PASS | exit=0 |
| TC-KIT-040 | Tidak ada host ganda | PASS | jumlah proses host=1 |
| TC-KIT-041 | Host hidup lagi | PASS | health ok |
| TC-KIT-042 | Pesanan bertahan setelah mulai ulang | PASS | id=1 |
| TC-KIT-043 | Cadangan manual dibuat | PASS | C:\Users\WDAGUtilityAccount\AppData\Local\Seruni22Bakery\backups\manual-20261028-231920.db (446464 bita) |
| TC-KIT-044 | Pemulihan dari cadangan | PASS | exit=0 |
| TC-KIT-045 | Basis data sebelum/sesudah pulih | INFO | C6F68F72F1F65149CB7F1CCCF43015B4CAF60DF6628CB6969C015E1FE7CBEE26\|446464 -> A830DF0832D36270E1F560748A2AF68856 |
| TC-KIT-046 | Salinan pengaman pra-pulih dibuat | PASS | pre-restore-20261028-231924.db |
| TC-KIT-047 | Uninstaller ada | PASS | C:\Users\WDAGUtilityAccount\AppData\Local\Programs\Seruni22Bakery\unins000.exe |
| TC-KIT-048 | Uninstall exit 0 | PASS | exit=0 |
| TC-KIT-049 | Folder aplikasi terhapus | PASS | C:\Users\WDAGUtilityAccount\AppData\Local\Programs\Seruni22Bakery |
| TC-KIT-050 | Entri uninstall terhapus | PASS | sisa=0 |
| TC-KIT-051 | Data pengguna utuh setelah uninstall | PASS | berkas identik; DB ada=True |
| TC-KIT-052 | Data disimpan di | INFO | C:\Users\WDAGUtilityAccount\AppData\Local\Seruni22Bakery |
| TC-KIT-053 | Log aplikasi terbaru | INFO | C:\Users\WDAGUtilityAccount\AppData\Local\Seruni22Bakery\logs\seruni-20261028.log |

## D. Kasus non-xUnit: manual, terbatas, dan terhalang

| ID | Kategori | Kasus | Status | Bukti / alasan |
|---|---|---|---|---|
| TC-MAN-001 | Fungsional | Alur A — penjualan/POS: pesanan, pembayaran, stok turun, jurnal seimbang | PASS | Laporan audit 2026-10-28 §D; verify-batch2 |
| TC-MAN-002 | Fungsional | Alur B — pre-order & pelanggan: tanggal pickup, nama/telepon, cakupan per kasir | PASS | Laporan audit §D; SalesPickupDateTests |
| TC-MAN-003 | Fungsional | Alur C — resep & produksi: edit BOM, SPK selesai, hasil aktual + scrap, HPP/unit | PASS | Laporan audit §D; verify-batch6 |
| TC-MAN-004 | Fungsional | Alur D — persediaan: penerimaan PO + batch, mutasi, opname, stok tidak negatif | PASS | Laporan audit §D; StockLedgerTests |
| TC-MAN-005 | Fungsional | Alur E — HPP & keuangan: jurnal tenaga kerja/overhead, buku besar, laporan | PASS | Laporan audit §D |
| TC-MAN-006 | Fungsional | Alur F — dasbor & laporan: KPI dari jurnal, ekspor PDF/XLSX | PASS | Laporan audit §D + Addendum A |
| TC-MAN-007 | Fungsional | Matriks 32 menu diuji di UI/endpoint | PASS dengan catatan | Laporan audit §C.1; BUG-09 terbuka, BUG-02/07 gagal label |
| TC-SEC-001 | Keamanan | Tanpa token: seluruh endpoint terlindungi menjawab 401 | PASS | Laporan audit §F |
| TC-SEC-002 | Keamanan | Peran Pegawai: 18 aksi terlarang dijawab 403 | PASS | Laporan audit §F; verify-batch3 |
| TC-SEC-003 | Keamanan | Peran Dapur: 17 aksi terlarang dijawab 403 | PASS | Laporan audit §F; verify-batch3 |
| TC-SEC-004 | Keamanan | Cakupan pesanan per kasir: 404 tanpa kebocoran data | PASS | Laporan audit §F |
| TC-SEC-005 | Keamanan | Pencabutan sesi: token lama dijawab 401 | PASS | Laporan audit §F; Batch7ApiTests |
| TC-SEC-006 | Keamanan | Gerbang sandi bawaan menolak endpoint lain (403) sampai sandi diganti | PASS | Laporan audit §F; kit mesin bersih TC-KIT-033 |
| TC-SEC-007 | Keamanan | CORS hanya loopback; origin asing ditolak | PASS | Laporan audit §F |
| TC-SEC-008 | Keamanan | Pemindaian SQL: 362 penggunaan, semuanya berparameter | PASS | Laporan audit §F |
| TC-SEC-009 | Keamanan | Kunci JWT di luar basis data, ACL hanya pengguna berjalan | PASS | Laporan audit §F; Batch-3e |
| TC-SEC-010 | Keamanan | Pemindaian dependensi CVE dan biner | NOT RUN | Tidak ada alat pemindai di mesin ini |
| TC-SEC-011 | Keamanan | Uji penetrasi jaringan | NOT RUN | Aplikasi hanya loopback; tidak ada lingkungan terpisah |
| TC-INT-001 | Integritas | Periksa-diri 9/9: integrity, foreign key, skema, jurnal seimbang, stok non-negatif | PASS | report/evidence/self-check.json; kit mesin bersih |
| TC-INT-002 | Integritas | Konkurensi tulis: 12 pesanan paralel, nomor unik dan rapat | PASS | gate log, verify-batch3 |
| TC-INT-003 | Integritas | Pemulihan setelah host dibunuh paksa | PASS | Laporan audit §G.4 |
| TC-DB-001 | Migrasi | Basis data kosong menjalankan 001–015 tanpa galat | PASS | Migrate_EmptyDatabase_ReachesVersionTertinggi… |
| TC-DB-002 | Migrasi | Basis data versi lama naik sekali jalan menutup kekosongan | PASS | Migrate_BasisDataVersiLama_SekaliJalan… |
| TC-DB-003 | Migrasi | Rollback 013→012→011 lalu naik lagi tanpa sisa | PASS | Rollback013Lalu012Lalu011_… |
| TC-DB-004 | Migrasi | Migrasi ulang pada basis data yang sama bersifat idempoten | PASS | Migrate_RerunOnSameDatabase_IsIdempotent |
| TC-DB-005 | Migrasi | Upgrade basis data nyata 13→15 dengan cadangan pra-upgrade | PASS | manual-20261028-214555.db + pre-upgrade-v13-to-v15-20261028-214556.db; skema 15, integrity ok |
| TC-BAK-001 | Backup/restore | Cadangan memuat data dan lolos integrity_check | PASS | DatabaseBackupTests.CreateBackup_… |
| TC-BAK-002 | Backup/restore | Pemulihan mengembalikan data lama dan membuat salinan pengaman | PASS | DatabaseBackupTests.Restore_Mengembalikan… |
| TC-BAK-003 | Backup/restore | Restore menolak berkas bukan basis data tanpa mengubah data | PASS | DatabaseBackupTests.Restore_MenolakBerkasBukanDatabase… |
| TC-BAK-004 | Backup/restore | Restore menolak cadangan dari skema lebih baru | PASS | DatabaseBackupTests.Restore_MenolakCadanganDariSkemaLebihBaru |
| TC-BAK-005 | Backup/restore | Cadangan + pulih di mesin bersih (sandbox, jaringan mati) | PASS | kit TC-KIT-043..045 |
| TC-BAK-006 | Backup/restore | Cadangan terjadwal otomatis | NOT RUN | Fitur belum ada; hanya cadangan manual |
| TC-INS-001 | Installer | Pasang 1.2.3 di mesin bersih (sandbox) lalu jalankan | PASS | kit TC-KIT-001..033 |
| TC-INS-002 | Installer | Mulai ulang aplikasi, data bertahan, tidak ada host ganda | PASS | kit TC-KIT-039..042 |
| TC-INS-003 | Installer | Uninstall menghapus program tanpa menghapus data pengguna | PASS | kit TC-KIT-047..051 |
| TC-INS-004 | Installer | Pasang ulang di atas instalasi yang ada (upgrade 1.2.2→1.2.3) | PASS | C:\tmp\real-before-123 vs real-after-123 |
| TC-INS-005 | Installer | Rollback ke versi sebelumnya | NOT RUN | Tidak ada paket 1.2.2 lagi; jalur turun tidak didukung |
| TC-INS-006 | Installer | Pasang MSI (WiX) build 1.2.3 | NOT RUN | MSI 1.2.3 tidak dibangun; hanya EXE Inno |
| TC-INS-007 | Installer | Pasang di perangkat Windows fisik (bukan sandbox) | BLOCKED | Tidak ada perangkat bersih; sandbox sudah diuji |
| TC-INS-008 | Installer | Tanda tangan code-signing | NOT RUN | Tidak ada sertifikat; status NotSigned |
| TC-OFF-001 | Offline | Instalasi + start + login + pesanan + ekspor + periksa-diri tanpa jaringan | PASS | clean.wsb <Networking>Disable</Networking>; kit 45 PASS |
| TC-OFF-002 | Offline | Bukti eksplisit tidak ada adaptor jaringan saat uji | NOT RUN | Kit 1.2.3 tidak mencatat Get-NetAdapter; hanya konfigurasi .wsb |
| TC-OFF-003 | Offline | Penggunaan > 1 jam tanpa jaringan (endurance) | NOT RUN | Belum dijadwalkan |
| TC-MCP-001 | MCP | Protokol stdio + 4 tool berizin + penolakan tanpa izin | PASS | McpStdioTests (5 kasus di suite UI) |
| TC-MCP-002 | MCP | Klien MCP pihak ketiga (Claude Desktop dll.) | NOT RUN | Belum diuji dengan klien luar |
| TC-PERF-001 | Kinerja | Baseline 6 endpoint (median 7 panggilan): 2–5 ms | PASS (terukur) | Laporan audit §H.1; data uji kecil |
| TC-PERF-002 | Kinerja | Ekspor PDF/XLSX: 13–373 ms (panggilan pertama 1.965 ms) | PASS (terukur) | Laporan audit §H.1 |
| TC-PERF-003 | Beban | Beban konkuren 10–50 pengguna pada 10.000+ pesanan | NOT RUN | Belum ada target/workload disetujui |
| TC-PERF-004 | Stress | Titik jenuh dan perilaku saat sumber daya habis | NOT RUN | Belum ada skenario; RAM mesin 3,8 GB |
| TC-PERF-005 | Skalabilitas | Pertumbuhan dataset 1–3 tahun (50.000 pesanan) | NOT RUN | Belum ada dataset sintetis |
| TC-PERF-006 | Soak | Operasi 8–24 jam tanpa kebocoran memori | NOT RUN | Belum dijadwalkan |
| TC-DEV-001 | Perangkat | Printer thermal 80 mm: cetak struk, kertas habis, printer mati | BLOCKED | Tidak ada perangkat |
| TC-DEV-002 | Perangkat | Pemindai barcode USB (keyboard wedge) | BLOCKED | Tidak ada perangkat |
| TC-DEV-003 | Perangkat | Laci uang lewat printer | BLOCKED | Menunggu printer & pemindai lulus |
| TC-ESC-001 | Perangkat (2026-11-01) | ESC/POS raw: byte per byte yang tiba di socket (`ESC @`, baris `LF`, `ESC d 3`, `GS V 0` hanya saat dihidupkan; `ESC a`/`ESC E`/`ESC t`/`ESC p` dipastikan tidak ada) | PASS (emulator) | `EscPosRawPrinterTests` terhadap `LoopbackEscPosEmulator`; **bukan printer fisik** |
| TC-ESC-002 | Perangkat (2026-11-01) | Penjaga alamat: hanya loopback/jaringan privat, IP sebagian ditolak, hostname diperiksa ulang terhadap DNS saat mencetak, port 1–65535, muatan > 65 536 byte ditolak sebelum socket dibuka | PASS (emulator) | `EscPosAlamatTests` + `PosDeviceServiceTests` + `Batch8DeviceApiTests` |
| TC-ESC-003 | Perangkat (2026-11-01) | AlurHTTP mode `escpos-raw`: struk uji & struk pesanan tiba, printer tidak menjawab → 409 + pekerjaan `Failed` dengan pesanan utuh, mode driver tidak membuka socket | PASS (emulator) | `Batch8DeviceApiTests`, filter `FullyQualifiedName~EscPos` = 19 lulus |
| TC-ESC-004 | Perangkat | Printer kasir ESC/POS fisik di jaringan toko (keluaran kertas, potongan, kertas habis, printer mati) | BLOCKED | Tidak ada perangkat di mesin ini; tetap menjadi syarat verdict |
| TC-ESC-005 | Perangkat (2026-11-01) | Pesan galat ESC/POS berbahasa Indonesia: sambungan ditolak → "…menolak sambungan…", nama host tak ter-resolution → "…tidak dikenali jaringan…"; teks `SocketException` sistem tidak boleh jadi satu-satunya penjelasan (tersimpan permanen di riwayat cetak) | PASS | `EscPosRawPrinterTests.SambunganDitolak_PesanBahasaIndonesiaBukanTeksSistem` + `NamaHostTidakTerResolution_PesanMenyuruhPakaiAlamatIp`; nama tes lama `SambunganDitolak_GalatDisampaikanKePemanggil` diganti 2026-11-01 |
| TC-ESC-006 | Peramban (2026-11-01) | Layar Perangkat Kasir dijalankan di peramban terhadap socket nyata: struk uji 636 byte dan struk pesanan 928 byte tiba di `127.0.0.1:9111`, riwayat menampilkan `tcp://127.0.0.1:9111`, printer dimatikan → banner gagal + pesanan utuh, dan catatan "Pilihan di layar ini belum disimpan" muncul saat layar berbeda dari keadaan tersimpan | PASS | Host uji + DB scratch `C:\tmp\escpos-ui.db` port 5177; skrip pendengar `C:\tmp\escpos-listener.mjs`. **Berkas byte mentah run ini (`C:\tmp\escpos-diterima.bin`, `C:\tmp\escpos-listener.log`) terhapus saat perkakas uji dijalankan ulang dan nama itu lalu diisi run lain, jadi angka byte di atas tercatat dari pengamatan langsung, bukan dari berkas yang masih ada — lihat `docs/pos-hardware.md` §5b.1; byte yang bisa dibuka ulang ada di TC-ESC-007.** **Emulator, bukan printer fisik** |
| TC-ESC-007 | Perkakas (2026-11-01) | `scripts/pos-physical-check.ps1` cabang `escpos-raw`: mode Print (dua pekerjaan `Printed` ke `tcp://127.0.0.1:9111`, pesanan tetap 2500000, 1572 byte = struk uji 636 + struk pesanan 936 tiba) dan mode Failure dengan pendengar dimatikan (HTTP 409, dua pekerjaan `Failed`, `galat=` berbahasa Indonesia, pesanan tidak berubah); regresi jalur driver `-SkipPrint` dan `-AllowPrint` tetap PASS (pada `-AllowPrint` spooler menerima dua pekerjaan `Printed` — itu penerimaan antrean, bukan bukti kertas); argumen tidak sah keluar kode 2 tanpa menyentuh apa pun, diuji dua-duanya: `escpos-raw` tanpa `-EscPosHost` dan `windows-driver` tanpa `-PrinterName` | PASS | `%TEMP%\seruni-pos-fisik-20261101-104601\bukti.txt`, `...-105531\bukti.txt`, `...-104632\bukti.txt`, `...-140257\bukti.txt`; byte utuh di `C:\tmp\escpos-bukti\escpos-diterima.bin`. **Loopback, bukan printer fisik — TC-ESC-004 tetap BLOCKED** |

Keterangan: PASS = dijalankan dan lulus dengan bukti; PASS (terukur) = angka nyata tanpa ambang; NOT RUN = belum dijalankan; BLOCKED = tidak dapat dijalankan di lingkungan ini.