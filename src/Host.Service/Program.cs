using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Seruni22Bakery.Application.Devices;
using Seruni22Bakery.Application.Services;
using Seruni22Bakery.Domain.Common;
using Seruni22Bakery.Domain.Interfaces;
using Seruni22Bakery.Domain.Entities;
using Seruni22Bakery.Host;
using Seruni22Bakery.Host.Devices;
using Seruni22Bakery.Infrastructure.Sqlite.Database;
using Seruni22Bakery.Infrastructure.Sqlite.Repositories;

// ── Bootstrap logger ──────────────────────────────────────────────────────────
// Mode MCP memakai stdout untuk protokol JSON-RPC, jadi log tidak boleh menulis ke konsol.
var modeMcp = args.Contains("--mcp-stdio", StringComparer.Ordinal);
var konfigurasiLog = new LoggerConfiguration()
    .WriteTo.File(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Seruni22Bakery", "logs", "seruni-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30);
if (!modeMcp) konfigurasiLog.WriteTo.Console();
Log.Logger = konfigurasiLog.CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // Peramban memakai kembali soket HTTP yang sama untuk beberapa permintaan. Bawaan Kestrel
    // menutup soket diam setelah 130 detik; peramban yang tidak tahu soket itu sudah ditutup tetap
    // mengirim permintaan berikutnya di sana dan menerima kegagalan jaringan tanpa kode status —
    // itulah banner "Server lokal tidak menjawab" saat aplikasi baru dibuka lagi setelah lama
    // didiamkan. Batas yang jauh lebih panjang membuat penutupan itu praktis tidak pernah terjadi
    // selama jendela aplikasi terbuka.
    builder.WebHost.ConfigureKestrel(options => options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10));

    // ── Configuration ──────────────────────────────────────────────────────────
    // Data operasional, log, kunci, cadangan, dan konfigurasi pengguna hidup di folder data
    // pengguna, terpisah dari folder program. Konfigurasi bawaan tetap di folder program;
    // berkas di folder data hanya menimpa nilai yang memang diubah pemilik.
    var dataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Seruni22Bakery");
    var userConfigPath = Path.Combine(dataRoot, "config", "appsettings.json");
    builder.Configuration.AddJsonFile(userConfigPath, optional: true, reloadOnChange: false);
    if (File.Exists(userConfigPath))
        Log.Information("Konfigurasi pengguna dibaca dari {Path}", userConfigPath);

    // Nilai dari baris perintah dibaca langsung karena provider konfigurasi bisa menelan
    // "--Database:Path=..." bila letaknya tepat setelah saklar tanpa nilai seperti "--backup".
    var configuredDbPath = MaintenanceCommands.PathBasisDataDariArgumen(args)
        ?? builder.Configuration["Database:Path"];
    var dbPath = !string.IsNullOrWhiteSpace(configuredDbPath)
        ? configuredDbPath
        : Path.Combine(dataRoot, "seruni22bakery.db");

    // Perintah cadangan/pemulihan berjalan sebelum host web dan sebelum migrasi lain.
    if (MaintenanceCommands.TryRun(args, dbPath, dataRoot))
        return;

    // ── Pagar jaringan: hanya loopback kecuali sengaja diizinkan ───────────────
    // Diperiksa sebelum kunci JWT dan basis data dibuat supaya salah konfigurasi
    // tidak meninggalkan berkas apa pun dan tidak sempat membuka port ke LAN.
    if (!modeMcp)
    {
        NetworkExposureGuard.PastikanLoopback(
            builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey));
        foreach (var endpoint in builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            var endpointUrl = endpoint["Url"];
            if (!string.IsNullOrWhiteSpace(endpointUrl))
                NetworkExposureGuard.PastikanLoopback(endpointUrl);
        }
    }

    // ── JWT secret: harus sudah resolved sebelum migrasi & sebelum app.Run() ──
    const string jwtSecretPlaceholder = "CHANGE_THIS_SECRET_IN_PRODUCTION_MIN_32_CHARS_LONG";
    const int minPanjangRahasiaJwt = 32;   // HS256: 256 bit. Lebih pendek bisa dipecah offline.
    var jwtSecret = builder.Configuration["Jwt:Secret"];
    if (!string.IsNullOrWhiteSpace(jwtSecret) && jwtSecret != jwtSecretPlaceholder)
    {
        // Kunci pendek yang diisi pemilik tidak diganti diam-diam: pemilik harus tahu token
        // dari instalasi ini bisa dipecah, jadi start dihentikan dengan alasan yang terbaca.
        if (Encoding.UTF8.GetByteCount(jwtSecret) < minPanjangRahasiaJwt)
            throw new InvalidOperationException(
                $"Jwt:Secret hanya {Encoding.UTF8.GetByteCount(jwtSecret)} byte; minimal {minPanjangRahasiaJwt} byte acak. " +
                "Kosongkan nilai itu agar aplikasi membuat kunci per-instalasi, atau isi dengan nilai acak yang lebih panjang.");
    }
    else
    {
        if (Environment.GetEnvironmentVariable("SERUNI_ALLOW_DEFAULT_JWT") == "1")
        {
            jwtSecret = jwtSecretPlaceholder;
            Log.Warning("[SECURITY] Jwt:Secret memakai nilai default, diizinkan via SERUNI_ALLOW_DEFAULT_JWT=1. Jangan dipakai di produksi.");
        }
        else
        {
            // Secret per-instalasi, disimpan di folder data yang sama dengan DB.
            // Lokasinya bisa dialihkan (uji/instalasi khusus) supaya pengetatan izin bisa
            // dibuktikan tanpa menyentuh kunci milik instalasi yang sedang dipakai.
            var secretFile = builder.Configuration["Jwt:SigningKeyFile"];
            if (string.IsNullOrWhiteSpace(secretFile))
            {
                secretFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Seruni22Bakery", "jwt.key");
            }
            var stored = File.Exists(secretFile) ? File.ReadAllText(secretFile).Trim() : null;
            if (!string.IsNullOrWhiteSpace(stored) && stored != jwtSecretPlaceholder
                && Encoding.UTF8.GetByteCount(stored) >= minPanjangRahasiaJwt)
            {
                jwtSecret = stored;
                // Kunci dari instalasi lama masih bisa membawa izin warisan folder, jadi
                // pengetatan dilakukan setiap start, bukan hanya saat kunci dibuat.
                RestrictSecretFile(secretFile);
            }
            else
            {
                // Berkas kunci yang terlalu pendek/rusak diganti baru: token lama tidak berlaku
                // lagi, tetapi tidak ada data pengguna yang hilang dan tidak ada kunci lemah
                // yang terus dipakai.
                if (!string.IsNullOrWhiteSpace(stored))
                    Log.Warning("Kunci JWT di {Path} terlalu pendek atau tidak sah; kunci baru dibuat.", secretFile);
                jwtSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
                Directory.CreateDirectory(Path.GetDirectoryName(secretFile)!);
                File.WriteAllText(secretFile, jwtSecret);
                RestrictSecretFile(secretFile);
                Log.Information("Jwt:Secret dibuat otomatis untuk instalasi ini dan disimpan di {Path}", secretFile);
            }
        }
    }
    // Konfigurasi yang tidak bisa dibaca tidak boleh membatalkan start aplikasi.
    var jwtExpiry = int.TryParse(builder.Configuration["Jwt:ExpiryMinutes"], out var expiryMinutes) && expiryMinutes > 0
        ? expiryMinutes
        : 480;

    var dir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }
    var connString = $"Data Source={dbPath};";

    // ── Database Migration ─────────────────────────────────────────────────────
    var dbDirectory = Path.GetDirectoryName(dbPath);
    var backupDirectory = Path.Combine(
        string.IsNullOrWhiteSpace(dbDirectory) ? dataRoot : dbDirectory, "backups");
    var migrator = new DatabaseMigrator(connString, backupDirectory, message => Log.Information("{Pesan}", message));
    migrator.Migrate();

    // ── DI Registration ────────────────────────────────────────────────────────
    var factory = new DbConnectionFactory(connString);
    builder.Services.AddSingleton(factory);
    builder.Services.AddSingleton<DatabaseIntegrity>();

    // Cadangan otomatis, retensi, dan penilaian kapasitas berjalan di latar. Koordinatornya
    // singleton karena memegang kunci pembuatan cadangan; pengaturan dan notifikasi dibaca
    // lewat scope baru supaya koneksi tidak menetap sepanjang umur aplikasi.
    var logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Seruni22Bakery", "logs");
    builder.Services.AddSingleton(sp => new BackupCoordinator(
        dbPath, backupDirectory, logDirectory, factory,
        sp.GetRequiredService<IServiceScopeFactory>()));
    builder.Services.AddHostedService<PemeliharaanService>();

    // Kunci pengaturan zona waktu ikut menentukan "hari ini" di seluruh aplikasi, jadi nilainya
    // dibaca sebelum ada layanan yang menghitung tanggal. Nama yang tidak dikenal tidak dipakai:
    // perhitungannya tetap berjalan dengan zona yang masih sah, dan galatnya ditulis ke log.
    var pengaturanAwal = new SettingRepository(factory).GetAllAsync().GetAwaiter().GetResult();
    if (pengaturanAwal.TryGetValue("timezone", out var zonaAwal))
    {
        var galatZona = BusinessTime.KonfigurasikanZona(zonaAwal);
        if (galatZona is not null) Log.Warning("[Setting] {Pesan}", galatZona);
    }

    // Versi di layar Pengaturan mengikuti assembly yang benar-benar berjalan. Baris seed tidak
    // pernah diperbarui migrasi, jadi tanpa ini pengguna melihat versi lama setelah upgrade.
    var versiBerjalan = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    if (!pengaturanAwal.TryGetValue("app_version", out var versiTersimpan) || versiTersimpan != versiBerjalan)
        new SettingRepository(factory).SetAsync("app_version", versiBerjalan).GetAwaiter().GetResult();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
    builder.Services.AddScoped<IIngredientRepository, IngredientRepository>();
    builder.Services.AddScoped<IProductTemplateRepository, ProductTemplateRepository>();
    builder.Services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
    builder.Services.AddScoped<IJournalRepository, JournalRepository>();
    builder.Services.AddScoped<IStockMovementRepository, StockMovementRepository>();
    builder.Services.AddScoped<ISettingRepository, SettingRepository>();
    builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
    builder.Services.AddScoped<IProductionRepository, ProductionRepository>();
    builder.Services.AddScoped<ILocationStockRepository, LocationStockRepository>();
    builder.Services.AddScoped<IAccountRepository, AccountRepository>();
    builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
    builder.Services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
    builder.Services.AddScoped<IDocumentSequenceRepository, DocumentSequenceRepository>();
    builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
    builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
    builder.Services.AddScoped<IBatchRepository, BatchRepository>();
    builder.Services.AddScoped<IQualityCheckRepository, QualityCheckRepository>();
    builder.Services.AddScoped<ISalesReturnRepository, SalesReturnRepository>();
    builder.Services.AddScoped<ISupplierSettlementRepository, SupplierSettlementRepository>();
    builder.Services.AddScoped<IPrintJobRepository, PrintJobRepository>();
    builder.Services.AddScoped<IFiscalPeriodRepository, FiscalPeriodRepository>();
    builder.Services.AddScoped<PurchaseService>();
    builder.Services.AddScoped<SalesReturnService>();
    builder.Services.AddScoped<SupplierSettlementService>();
    builder.Services.AddScoped<PeriodCloseService>();
    builder.Services.AddScoped<CostSummaryService>();
    builder.Services.AddScoped<PosDeviceService>();

    // Perangkat keras hanya disentuh lewat adaptor ini; layar tidak pernah memanggil driver,
    // registry, atau spooler sendiri.
    builder.Services.AddSingleton<IPrinterCatalog, WindowsPrinterCatalog>();
    builder.Services.AddSingleton<WindowsSpoolerPrinter>();
    builder.Services.AddSingleton<EscPosTcpPrinter>();
    // Mode cetak ditentukan per pekerjaan dari pengaturan; yang di-inject adalah perantaranya.
    builder.Services.AddSingleton<IReceiptPrinter>(sp => new CompositeReceiptPrinter(
        sp.GetRequiredService<WindowsSpoolerPrinter>(), sp.GetRequiredService<EscPosTcpPrinter>()));

    builder.Services.AddSingleton(new JwtService(jwtSecret, jwtExpiry));
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<AccountingService>();
    builder.Services.AddScoped<OrderService>();
    builder.Services.AddScoped<DashboardService>();
    builder.Services.AddScoped<ExportService>();
    builder.Services.AddScoped<AnalyticsService>();
    builder.Services.AddScoped<TaskInboxService>();
    builder.Services.AddScoped<NotificationService>();
    builder.Services.AddScoped<McpToolbox>();
    builder.Services.AddHostedService<NotificationWorker>();

    // QuestPDF community license (gratis untuk revenue < $1M/tahun)
    QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    // ── JWT Auth ───────────────────────────────────────────────────────────────
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = "seruni22bakery",
                ValidateAudience = true, ValidAudience = "seruni22bakery",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
            };
            // Token yang sudah dicetak tidak boleh menjadi izin permanen: akun yang dinonaktifkan
            // harus langsung kehilangan akses, dan peran yang diturunkan harus langsung berlaku.
            // Jadi identitas, peran, dan status wajib-ganti-sandi dibaca ulang dari basis data
            // setiap permintaan; klaim di token hanya dipakai sebagai penunjuk akun.
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = async ctx =>
                {
                    var idText = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    if (!int.TryParse(idText, out var userId))
                    {
                        ctx.Fail("Token tidak memuat identitas pengguna.");
                        return;
                    }

                    var users = ctx.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                    var user = await users.GetByIdAsync(userId);
                    if (user is null || !user.IsActive)
                    {
                        ctx.Fail("Akun tidak aktif.");
                        return;
                    }

                    // Token yang sudah logout atau dicetak sebelum sandi diganti tidak boleh
                    // hidup lagi. Versi di klaim dibandingkan dengan baris Users sekarang juga.
                    var versiKlaim = ctx.Principal?.FindFirst("token_version")?.Value;
                    if (!int.TryParse(versiKlaim, out var versiToken) || versiToken != user.TokenVersion)
                    {
                        ctx.Fail("Sesi sudah dicabut. Silakan masuk kembali.");
                        return;
                    }

                    var identity = (System.Security.Claims.ClaimsIdentity)ctx.Principal!.Identity!;
                    foreach (var klaim in identity.FindAll(System.Security.Claims.ClaimTypes.Role).ToList())
                        identity.RemoveClaim(klaim);
                    identity.AddClaim(new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.Role, user.Role.ToString()));

                    var gerbang = identity.FindFirst("must_change_password");
                    if (gerbang is not null) identity.RemoveClaim(gerbang);
                    identity.AddClaim(new System.Security.Claims.Claim(
                        "must_change_password", user.MustChangePassword ? "true" : "false"));
                }
            };
        });
    // ── Kebijakan peran: matriksnya tinggal satu di Domain/Common/RolePolicies.cs ──
    builder.Services.AddAuthorization(options =>
    {
        foreach (var policy in RolePolicies.Names)
            options.AddPolicy(policy, b => b.RequireRole(RolePolicies.AllowedRoles(policy).Select(r => r.ToString())));

        // Setiap endpoint yang tidak menyatakan kebijakan apa pun tetap wajib login. Tanpa
        // kebijakan cadangan ini, satu endpoint baru yang lupa memasang RequireAuthorization
        // otomatis terbuka untuk siapa saja — persis kelas bug yang sulit terlihat di review.
        // Jalur yang memang publik (login, merek, cek kesehatan) ditandai AllowAnonymous.
        options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    // Frontend disajikan dari origin yang sama (desktop/) dan vite dev hanya di loopback,
    // jadi tidak ada alasan membuka CORS untuk asal mana pun.
    var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ??
                          "http://localhost:5000,http://127.0.0.1:5000,http://localhost:5173")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

    var app = builder.Build();

    // Mode server MCP: tidak menyalakan Kestrel sama sekali. Migrasi, kunci JWT, dan seluruh
    // registrasi layanan sudah selesai; yang berjalan hanya protokol stdio di atas basis data
    // yang sama dengan aplikasi. stdout khusus JSON-RPC, log hanya ke berkas.
    if (modeMcp)
    {
        Environment.ExitCode = await McpStdio.JalankanAsync(app.Services, builder.Configuration);
        return;
    }

    app.UseCors();

    // Satu bentuk galat untuk semua kegagalan tak tertangani: layar menerima kode, pesan, dan
    // nomor permintaan yang bisa dicari di log, tanpa jejak tumpukan yang membocorkan isi mesin.
    app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
    {
        var galat = ctx.Features
            .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var idPermintaan = ctx.TraceIdentifier;
        Log.Error(galat, "Permintaan {Metode} {Jalur} gagal (request {RequestId})",
            ctx.Request.Method, ctx.Request.Path, idPermintaan);

        // Klien yang memutus koneksi bukan kegagalan server; jangan mencoba menulis jawaban.
        if (ctx.RequestAborted.IsCancellationRequested || ctx.Response.HasStarted) return;

        // Pagar periode adalah penolakan yang diharapkan, bukan kerusakan mesin. Semua aksi tulis
        // bermuara ke satu penulis jurnal, jadi jalur yang tidak punya penanganan sendiri —
        // opname, mutasi, penyelesaian SPK — akan menjawab 500 generik tanpa nama bulan mana
        // yang terkunci dan siapa yang menutupnya.
        if (galat is PeriodClosedException)
        {
            Log.Warning(galat, "Penulisan jurnal ditolak pagar periode: {Jalur}", ctx.Request.Path);
            ctx.Response.Clear();
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsJsonAsync(new
            {
                errorCode = ErrorCodes.ValidationFailed,
                errorMessage = galat.Message,
            });
            return;
        }

        ctx.Response.Clear();
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsJsonAsync(new
        {
            errorCode = ErrorCodes.InternalError,
            errorMessage = "Terjadi kesalahan di server. Sebutkan nomor permintaan ini saat melapor.",
            requestId = idPermintaan,
        });
    }));

    // Pengukur waktu dipasang paling luar di antara middleware API: angkanya harus mencakup
    // otorisasi dan endpoint, karena justru itu yang membedakan menu cepat dari menu lambat.
    app.Use(async (ctx, next) => await WaktuPermintaan.TengahkanAsync(ctx, next));

    // ── Static Files (Desktop Frontend SPA) ────────────────────────────────────
    // Berkas statis tidak punya endpoint, sehingga kebijakan cadangan (wajib login) akan
    // menolaknya bila middleware ini dipasang sesudah UseAuthorization. Halaman masuk dan
    // bundelnya harus terbaca sebelum sesi ada, jadi penyajian berkas ditempatkan lebih dulu.
    var desktopPath = builder.Configuration["Desktop:StaticFilesPath"];
    if (string.IsNullOrEmpty(desktopPath))
    {
        var localWwwroot = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
        if (Directory.Exists(localWwwroot))
        {
            desktopPath = localWwwroot;
        }
        else
        {
            desktopPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "desktop"));
        }
    }
    if (Directory.Exists(desktopPath))
    {
        var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(desktopPath);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    }

    app.UseAuthentication();

    // Antrean cetak yang masih Pending saat aplikasi berhenti tidak akan selesai sendiri.
    // Tandai Failed sekarang supaya layar perangkat tidak menampilkan pekerjaan "menggantung"
    // yang sebenarnya sudah mati.
    try
    {
        using var scopeAwal = app.Services.CreateScope();
        var dipulihkan = await scopeAwal.ServiceProvider
            .GetRequiredService<PosDeviceService>()
            .PulihkanJobMenggantungAsync();
        if (dipulihkan > 0)
            Log.Warning("[Perangkat] {Jumlah} pekerjaan cetak menggantung ditandai gagal saat start.", dipulihkan);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "[Perangkat] Pemulihan antrean cetak tidak berjalan.");
    }

    // Jejak dipasang sebelum otorisasi: percobaan yang ditolak (401/403) harus ikut tercatat,
    // dan itu hanya terlihat kalau middleware ini membungkus UseAuthorization.
    app.Use(async (ctx, next) => await Jejak.TengahkanAsync(ctx, next));

    app.UseAuthorization();

    // Gerbang kata sandi bawaan. Selama akun masih menandai must_change_password, token yang
    // dicetaknya hanya boleh dipakai untuk login dan ganti sandi; tanpa ini, sandi default
    // admin/admin123 masih cukup untuk membaca seluruh data usaha lewat API.
    app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        var allowedWhileGated = path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
                                 || path.Equals("/health", StringComparison.OrdinalIgnoreCase);
        if (!allowedWhileGated
            && ctx.User.Identity?.IsAuthenticated == true
            && ctx.User.FindFirst("must_change_password")?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new
            {
                errorCode = ErrorCodes.Forbidden,
                errorMessage = "Kata sandi akun ini masih bawaan. Ganti kata sandi lebih dulu melalui menu ganti sandi."
            });
            return;
        }

        await next();
    });

    // Gerbang anti-duplikat untuk seluruh aksi perubahan API (POST/PUT/PATCH). Letaknya sesudah
    // gerbang kata sandi supaya permintaan yang sudah ditolak 403 tidak ikut memakan kunci.
    app.Use(async (ctx, next) => await Idempotensi.TengahkanAsync(ctx, next));

    // ── API Endpoints ──────────────────────────────────────────────────────────

    // Health
    // Versi dibaca dari metadata assembly supaya /health mencerminkan paket yang benar-benar
    // berjalan, bukan angka yang ditulis ulang manual setiap rilis.
    var appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    app.MapGet("/health", () => Results.Ok(new { status = "ok", app = "Seruni 22 Bakery", version = appVersion }))
        .AllowAnonymous();

    // Auth
    app.MapPost("/api/auth/login", async (LoginRequest req, AuthService auth) =>
    {
        var result = await auth.LoginAsync(req);
        return result.Success ? Results.Ok(result.Data) : Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: 401);
    }).AllowAnonymous();

    app.MapPost("/api/auth/change-password", async (ChangePasswordDto dto, AuthService auth, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await auth.ChangePasswordAsync(userId, dto.CurrentPassword, dto.NewPassword);
        return result.Success ? Results.Ok() : Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: 400);
    }).RequireAuthorization();

    // Logout tidak hanya menghapus token di layar: versi token akun dinaikkan di basis data,
    // sehingga salinan token yang sudah terlanjur dicetak tidak bisa dipakai lagi.
    app.MapPost("/api/auth/logout", async (AuthService auth, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await auth.LogoutAsync(userId);
        return Results.Ok(new { message = "Sesi diakhiri dan token dicabut." });
    }).RequireAuthorization();

    // Dashboard
    app.MapGet("/api/dashboard", async (DashboardService dashboard) =>
        Results.Ok(await dashboard.GetSummaryAsync())).RequireAuthorization("finance-read");

    // Kotak tugas: daftar pekerjaan yang benar-benar menunggu tindakan, disusun dari kondisi
    // basis data saat ini. Isinya disaring per peran di dalam service: kasir tidak menerima
    // bocoran jadwal produksi atau PO, dan dapur tidak perlu daftar piutang.
    app.MapGet("/api/tasks", async (TaskInboxService inbox, HttpContext ctx) =>
    {
        var roleText = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var role = Enum.TryParse<UserRole>(roleText, out var r) ? r : UserRole.Pegawai;
        return Results.Ok(await inbox.GetAsync(role, IdPengguna(ctx)));
    }).RequireAuthorization();

    // ── Notifikasi internal ────────────────────────────────────────────────────
    // Setiap pengguna hanya membaca kotaknya sendiri; tidak ada endpoint yang membuka kotak
    // pengguna lain. Notifikasi ini tersimpan di SQLite, jadi riwayatnya tetap ada setelah
    // aplikasi ditutup dan dibuka kembali.
    app.MapGet("/api/notifications", async (bool? unreadOnly, int? limit, NotificationService notifikasi, HttpContext ctx) =>
    {
        var daftar = await notifikasi.DaftarAsync(
            IdPengguna(ctx), unreadOnly == true, limit is > 0 ? Math.Min(limit.Value, 200) : 100);
        // Severity dikirim sebagai teks (Info/Warning/Critical) supaya layar tidak perlu tahu
        // urutan angka enum di sisi server.
        return Results.Ok(new
        {
            item = daftar.Item.Select(n => new
            {
                n.Id, n.EventType, severity = n.Severity.ToString(), n.Title, n.Body,
                n.EntityType, n.EntityId, n.IsRead, n.ReadAt, n.CreatedAt
            }),
            ringkasan = daftar.Ringkasan
        });
    }).RequireAuthorization();

    app.MapGet("/api/notifications/summary", async (INotificationRepository repo, HttpContext ctx) =>
        Results.Ok(await repo.SummaryAsync(IdPengguna(ctx)))).RequireAuthorization();

    app.MapPost("/api/notifications/{id:int}/read", async (int id, NotificationService notifikasi, HttpContext ctx) =>
        await notifikasi.TandaiDibacaAsync(id, IdPengguna(ctx)) ? Results.Ok() : Results.NotFound())
        .RequireAuthorization();

    app.MapPost("/api/notifications/read-all", async (NotificationService notifikasi, HttpContext ctx) =>
        Results.Ok(new { ditandai = await notifikasi.TandaiSemuaDibacaAsync(IdPengguna(ctx)) }))
        .RequireAuthorization();

    // Penyegaran pengingat operasional. Hasilnya idempoten: kunci dedupe memuat tanggal bisnis,
    // jadi menekan tombol ini berkali-kali tidak menggandakan pengingat hari itu.
    app.MapPost("/api/notifications/sync", async (NotificationService notifikasi) =>
        Results.Ok(new { ditulis = await notifikasi.SinkronkanOperasionalAsync(DateTime.UtcNow) }))
        .RequireAuthorization();

    // Analisis penjualan hanya untuk yang boleh membaca buku besar: angkanya diambil dari
    // jurnal posted, jadi menyebarkannya ke kasir tidak ada gunanya dan tidak pada tempatnya.
    app.MapGet("/api/analytics/sales", async (int? days, AnalyticsService analytics) =>
        Results.Ok(await analytics.GetSalesTrendAsync(days is > 0 ? Math.Min(days.Value, 730) : 90)))
        .RequireAuthorization("finance-read");

    // Orders
    // Kasir hanya melihat pesanan yang ia catat sendiri: daftar seluruh toko memuat nama,
    // nomor telepon, dan nilai pesanan pelanggan lain — bukan isi pekerjaan satu kasir.
    // Pemilik/Admin tetap melihat seluruhnya.
    // Halaman daftar pesanan: 100 baris pertama secara bawaan, jumlah seluruhnya lewat
    // X-Total-Count supaya layar tahu ada halaman berikutnya dan bisa memuatnya.
    app.MapGet("/api/orders", async (ISalesOrderRepository repo, int? limit, int? offset, bool? outstanding,
        HttpContext ctx, HttpResponse res) =>
    {
        var roleText = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        int? pembatas = Enum.TryParse<UserRole>(roleText, out var role) && role == UserRole.Pegawai
            ? int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value)
            : null;
        var take = limit is > 0 and <= 500 ? limit.Value : 100;
        var skip = offset is > 0 ? offset.Value : 0;
        // Layar keuangan hanya membutuhkan pesanan bersisa bayar. Tanpa penyaring ini daftar
        // piutang berhenti pada 100 pesanan mana pun yang lebih dulu, sehingga total yang
        // dihitung dari daftar itu tidak sama dengan angka server.
        if (outstanding == true)
        {
            res.Headers["X-Total-Count"] = (await repo.CountOutstandingAsync(pembatas)).ToString();
            return Results.Ok(await repo.GetOutstandingListAsync(take, skip, pembatas));
        }
        res.Headers["X-Total-Count"] = (await repo.CountAsync(pembatas)).ToString();
        return Results.Ok(await repo.GetAllAsync(take, pembatas, skip));
    }).RequireAuthorization("sales");

    app.MapGet("/api/orders/{id:int}", async (int id, ISalesOrderRepository repo, HttpContext ctx) =>
    {
        var o = await repo.GetByIdAsync(id);
        if (o is null) return Results.NotFound();

        var roleText = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (Enum.TryParse<UserRole>(roleText, out var role) && role == UserRole.Pegawai)
        {
            var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            // 404, bukan 403: keberadaan pesanan kasir lain juga bukan informasi untuknya.
            if (o.CreatedByUserId != userId) return Results.NotFound();
        }
        return Results.Ok(o);
    }).RequireAuthorization("sales");

    app.MapPost("/api/orders", async (CreateOrderRequest req, OrderService svc,
        IProductTemplateRepository products, NotificationService notif, HttpContext ctx) =>
    {
        var userIdClaim = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out int userId))
            return Results.Unauthorized();
        // Override CreatedByUserId dari request dengan nilai dari token
        req = req with { CreatedByUserId = userId };

        // Harga bukan masukan bebas untuk kasir. Tanpa pagar ini, akun kasir bisa menulis
        // total berapa pun untuk produk katalog — persis celah "ubah harga" yang dilarang.
        var roleText = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (Enum.TryParse<UserRole>(roleText, out var role) && role == UserRole.Pegawai)
        {
            if (req.ProductTemplateId is null)
                return Results.BadRequest(new
                {
                    errorCode = ErrorCodes.ValidationFailed,
                    errorMessage = "Kasir harus memilih produk dari katalog supaya harganya diambil dari daftar harga, bukan diketik bebas."
                });

            var produk = (await products.GetAllActiveAsync())
                .FirstOrDefault(p => p.Id == req.ProductTemplateId.Value);
            if (produk is null)
                return Results.BadRequest(new
                {
                    errorCode = ErrorCodes.ValidationFailed,
                    errorMessage = "Produk tidak ditemukan atau sudah tidak aktif. Pilih produk lain dari katalog."
                });
            if (produk.SellingPrice <= 0)
                return Results.BadRequest(new
                {
                    errorCode = ErrorCodes.ValidationFailed,
                    errorMessage = $"Harga jual {produk.Name} belum diisi. Minta Admin mengisi harga katalog lebih dulu."
                });

            // Yang diuji di sini harga barang, bukan tagihan akhir: TotalAmount permintaan adalah
            // nilai barang sebelum diskon dan sebelum PPN, sehingga membandingkannya dengan harga
            // katalog tetap tidak memberi kasir ruang untuk menulis harga sendiri.
            var seharusnya = produk.SellingPrice * req.Quantity;
            if (req.Quantity is > 0 and <= OrderService.MaxOrderQuantity)
            {
                if (seharusnya != Math.Truncate(seharusnya) || seharusnya > long.MaxValue)
                    return Results.BadRequest(new
                    {
                        errorCode = ErrorCodes.ValidationFailed,
                        errorMessage = "Jumlah unit menghasilkan total pecahan rupiah; sesuaikan jumlah unit."
                    });
                if (req.TotalAmount != (long)seharusnya)
                    return Results.BadRequest(new
                    {
                        errorCode = ErrorCodes.ValidationFailed,
                        errorMessage = $"Total pesanan harus {Money.Format((long)seharusnya)} ({produk.Name} × {req.Quantity:0.###}). "
                                       + "Perubahan harga hanya bisa dilakukan Admin lewat katalog produk."
                    });
            }
        }

        // Diskon juga perubahan harga, hanya lewat tombol: kalau kasir boleh memotong tagihan,
        // pagar harga katalog di atas bisa disiasati tanpa mengetik angka apa pun.
        // Peran yang tidak terbaca ikut ditolak — bukan dianggap pemilik.
        var peranDiskon = PeranPemanggil(ctx);
        if ((req.DiscountAmount > 0 || req.DiscountPercent > 0)
            && (peranDiskon is null || !RolePolicies.Allows(RolePolicies.SalesDiscount, peranDiskon.Value)))
            return Results.BadRequest(new
            {
                errorCode = ErrorCodes.ValidationFailed,
                errorMessage = "Diskon pesanan diberikan lewat Admin, bukan kasir. "
                              + "Simpan pesanan tanpa diskon, lalu minta Admin atau pemilik yang memberikan diskonnya."
            });

        var result = await svc.CreateOrderAsync(req);
        if (result.Success)
        {
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin],
                new KirimNotifikasi("transaction.order.created", NotificationSeverity.Info,
                    "Pesanan baru tercatat",
                    $"{req.CustomerName} · {Money.Format(req.TotalAmount)} · ambil {req.PickupDate:yyyy-MM-dd}.",
                    "SalesOrder", result.Data, $"transaction.order.created:{result.Data}"), DateTime.UtcNow));
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePenggunaAsync(
                userId,
                new KirimNotifikasi("transaction.order.created", NotificationSeverity.Info,
                    "Pesanan Anda tersimpan",
                    $"{req.CustomerName} · {Money.Format(req.TotalAmount)} · ambil {req.PickupDate:yyyy-MM-dd}.",
                    "SalesOrder", result.Data, $"transaction.order.created:{result.Data}"), DateTime.UtcNow));
            return Results.Created($"/api/orders/{result.Data}", new { id = result.Data });
        }

        await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePenggunaAsync(
            userId,
            new KirimNotifikasi("transaction.order.failed", NotificationSeverity.Warning,
                "Pesanan gagal disimpan", result.ErrorMessage ?? "Penyimpanan pesanan ditolak.",
                "SalesOrder", null), DateTime.UtcNow));
        return Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: 400);
    }).RequireAuthorization("sales");

    app.MapPost("/api/orders/{id:int}/payment", async (int id, AddPaymentDto dto, OrderService svc,
        ISalesOrderRepository orders, NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        // Cakupan kasir sama dengan daftar dan detail pesanan: pesanan kasir lain tidak boleh
        // dilunasi atau diubah statusnya dari akun ini, karena uang dan jejaknya jadi milik orang lain.
        if (await PesananDiLuarCakupanAsync(id, orders, ctx)) return Results.NotFound();
        var result = await svc.AddPaymentAsync(new AddPaymentRequest(
            id, dto.Amount, dto.Method, userId, dto.Notes, dto.IdempotencyKey, dto.TenderAmount));
        if (result.Success)
        {
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin],
                new KirimNotifikasi("transaction.order.paid", NotificationSeverity.Info,
                    "Pembayaran pesanan diterima",
                    $"Pesanan #{id} menerima {Money.Format(dto.Amount)} lewat {dto.Method}.",
                    "SalesOrder", id, $"transaction.order.paid:{id}:{dto.IdempotencyKey ?? DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")}"),
                DateTime.UtcNow));
            // Kembalian hanya dikirim ke layar, tidak pernah ke buku besar: yang tercatat tetap
            // dto.Amount. Tanpanya kasir harus menghitung sendiri uang yang harus dikembalikan.
            if (dto.TenderAmount > dto.Amount)
                return Results.Ok(new { changeAmount = dto.TenderAmount!.Value - dto.Amount });
            return Results.Ok();
        }

        await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePenggunaAsync(
            userId,
            new KirimNotifikasi("transaction.order.payment_failed", NotificationSeverity.Warning,
                "Pembayaran gagal dicatat", result.ErrorMessage ?? "Pembayaran ditolak.",
                "SalesOrder", id), DateTime.UtcNow));
        return Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: 400);
    }).RequireAuthorization("sales");

    // Pesanan diambil pelanggan adalah titik pendapatan diakui: servis yang menulis tanggalnya,
    // jadi permintaan cukup mengirim id — tidak ada nilai uang yang bisa diubah dari layar.
    app.MapPost("/api/orders/{id:int}/deliver", async (int id, OrderService svc,
        ISalesOrderRepository orders, NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        // Cakupan kasir sama dengan pembayaran: pesanan kasir lain tidak bisa dinyatakan sudah
        // diserahkan dari akun ini, karena pendapatan dan jejaknya jadi milik orang lain.
        if (await PesananDiLuarCakupanAsync(id, orders, ctx)) return Results.NotFound();

        var result = await svc.MarkDeliveredAsync(id, userId);
        if (result.Success)
        {
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin],
                new KirimNotifikasi("transaction.order.delivered", NotificationSeverity.Info,
                    "Pesanan diambil pelanggan",
                    $"Pesanan #{id} dinyatakan sudah diserahkan oleh pengguna #{userId}.",
                    "SalesOrder", id, $"transaction.order.delivered:{id}:{DateTime.UtcNow:yyyyMMddHHmmssfff}"),
                DateTime.UtcNow));
            return Results.Ok();
        }

        await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePenggunaAsync(
            userId,
            new KirimNotifikasi("transaction.order.deliver_failed", NotificationSeverity.Warning,
                "Pengambilan pesanan gagal dicatat", result.ErrorMessage ?? "Status pesanan tidak mengizinkan.",
                "SalesOrder", id), DateTime.UtcNow));
        return Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: 400);
    }).RequireAuthorization("sales");

    app.MapPut("/api/orders/{id:int}/status", async (int id, UpdateStatusDto dto, OrderService svc,
        ISalesOrderRepository orders, NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        Seruni22Bakery.Domain.Entities.OrderStatus newStatus;
        try
        {
            newStatus = Enum.Parse<Seruni22Bakery.Domain.Entities.OrderStatus>(dto.Status, ignoreCase: true);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "Status tidak valid. Nilai yang diterima: Pending, Processing, Ready, Paid, Cancelled" });
        }
        // Satu endpoint untuk semua transisi status: pembatalan adalah bagian yang berbahaya,
        // jadi peran diperiksa ulang di sini sebelum servis menjalankan pembalikan stok dan jurnal.
        // Urutannya sengaja sebelum pagar cakupan: pegawai tidak pernah boleh membatalkan pesanan
        // siapa pun, jadi jawabannya 403 — bukan 404 yang bergantung pada siapa pemilik pesanan.
        if (newStatus == Seruni22Bakery.Domain.Entities.OrderStatus.Cancelled)
        {
            var roleText = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var boleh = Enum.TryParse<UserRole>(roleText, out var role)
                        && RolePolicies.Allows(RolePolicies.SalesCancel, role);
            if (!boleh)
                return Results.Json(new
                {
                    errorCode = ErrorCodes.Forbidden,
                    errorMessage = $"Pembatalan pesanan hanya untuk {string.Join(" atau ", RolePolicies.AllowedRoles(RolePolicies.SalesCancel))}."
                }, statusCode: 403);
        }

        if (await PesananDiLuarCakupanAsync(id, orders, ctx)) return Results.NotFound();

        // Status Lunas hanya boleh lahir dari pembayaran yang tercatat. Tanpa pagar ini, kasir
        // bisa menandai pesanan lunas tanpa uang masuk dan piutangnya hilang dari laporan.
        if (newStatus == Seruni22Bakery.Domain.Entities.OrderStatus.Paid
            && await orders.GetByIdAsync(id) is { } cekLunas && !cekLunas.IsFullyPaid())
            return Results.Json(new
            {
                errorCode = ErrorCodes.ValidationFailed,
                errorMessage = $"Pesanan ini masih kurang {Money.Format(cekLunas.RemainingAmount())}. "
                               + "Pelunasan dicatat lewat pembayaran, bukan dengan mengubah status langsung."
            }, statusCode: 400);

        var sebelum = (await orders.GetByIdAsync(id))?.Status.ToString();
        var result = await svc.UpdateStatusAsync(id, newStatus, userId, dto.CancelReason);
        if (result.Success && sebelum != newStatus.ToString())
        {
            // Status pesanan menentukan stok, piutang, dan jurnal pembalik, jadi perubahan ini
            // tercatat dengan nilai lama dan barunya — bukan hanya "ada yang menekan tombol".
            await Jejak.CatatDetilAsync(ctx, $"ubah status pesanan {dto.Status}", "SalesOrder", id,
                new { status = sebelum }, new { status = newStatus.ToString(), alasan = dto.CancelReason });

            var dibatalkan = newStatus == Seruni22Bakery.Domain.Entities.OrderStatus.Cancelled;
            IReadOnlyCollection<UserRole> penerimaStatus = dibatalkan
                ? new[] { UserRole.Owner, UserRole.Admin }
                : new[] { UserRole.Owner, UserRole.Admin, UserRole.Pegawai };
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                penerimaStatus,
                new KirimNotifikasi(
                    dibatalkan ? "transaction.order.cancelled" : "transaction.order.status",
                    dibatalkan ? NotificationSeverity.Warning : NotificationSeverity.Info,
                    dibatalkan ? $"Pesanan #{id} dibatalkan" : $"Status pesanan #{id} menjadi {newStatus}",
                    dto.CancelReason is { Length: > 0 }
                        ? $"Alasan: {dto.CancelReason}"
                        : $"Diubah oleh pengguna #{userId}.",
                    "SalesOrder", id, $"transaction.order.status:{id}:{newStatus}:{DateTime.UtcNow:yyyyMMddHHmmssfff}"),
                DateTime.UtcNow));
        }
        return result.Success ? Results.Ok() : Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: 400);
    }).RequireAuthorization("sales");

    // Customers
    // Halaman daftar pelanggan: layar hanya memuat sebagian, jumlah seluruhnya dikirim lewat
    // X-Total-Count supaya "Muat lagi" tahu kapan berhenti.
    app.MapGet("/api/customers", async (ICustomerRepository repo, string? search, int? limit, int? offset,
        HttpContext ctx, HttpResponse res) =>
    {
        var take = limit is > 0 and <= 2000 ? limit.Value : 300;
        var skip = offset is > 0 ? offset.Value : 0;
        // Kasir hanya perlu pelanggan yang pernah ia layani. Buku pelanggan seluruh toko adalah
        // data pemilik; tanpa penyaringan ini setiap akun kasir bisa mengunduh semuanya.
        int? hanyaMilik = null;
        if (PeranPemanggil(ctx) == UserRole.Pegawai
            && int.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid))
            hanyaMilik = uid;
        var total = await repo.CountActiveAsync(search, hanyaMilik);
        res.Headers["X-Total-Count"] = total.ToString();
        return Results.Ok(await repo.GetPageAsync(search, take, skip, hanyaMilik));
    }).RequireAuthorization("sales");

    // ── Retur penjualan ─────────────────────────────────────────────────────────
    // Hanya Owner/Admin: retur mengeluarkan uang tunai dan mengembalikan stok.
    app.MapGet("/api/returns", async (ISalesReturnRepository repo) =>
        Results.Ok(await repo.GetAllAsync())).RequireAuthorization("sales-return");

    app.MapPost("/api/returns", async (CreateSalesReturnDto dto, SalesReturnService svc,
        NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await svc.CreateAsync(new CreateSalesReturnRequest(
            dto.SalesOrderId, dto.Quantity, dto.Amount, dto.Reason,
            dto.RefundMethod, userId, dto.IdempotencyKey));

        if (result.Success)
        {
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin],
                new KirimNotifikasi("transaction.return.created", NotificationSeverity.Warning,
                    "Retur penjualan dicatat",
                    $"Pesanan #{dto.SalesOrderId}: {dto.Quantity:0.###} unit, {Money.Format(dto.Amount)}, "
                    + $"metode {dto.RefundMethod}. Alasan: {dto.Reason}.",
                    "SalesReturn", result.Data, $"transaction.return.created:{result.Data}"), DateTime.UtcNow));
            return Results.Created($"/api/returns/{result.Data}", new { id = result.Data });
        }

        var status = result.ErrorCode == ErrorCodes.NotFound
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        return Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: status);
    }).RequireAuthorization("sales-return");

    // Ingredients
    app.MapGet("/api/ingredients", async (IIngredientRepository repo) =>
        Results.Ok(await repo.GetAllActiveAsync())).RequireAuthorization("inventory-read");

    app.MapPost("/api/ingredients", async (IngredientDto dto, IIngredientRepository repo, ILocationStockRepository stock, HttpContext ctx) =>
    {
        if (GalatBahan(dto) is { } galat)
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galat });
        var userId = int.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
        // Cek nama + simpan dalam satu transaksi tulis: dua kiriman bersamaan dengan nama sama
        // tidak bisa sama-sama lolos cek lalu menyimpan dua baris bahan baku kembar.
        var id = await repo.TryCreateAsync(new Ingredient
        {
            Name = dto.Name!.Trim(),
            Brand = string.IsNullOrWhiteSpace(dto.Brand) ? null : dto.Brand.Trim(),
            Unit = dto.Unit!.Trim(),
            PricePerUnit = dto.PricePerUnit ?? 0,
            MinStockAlert = dto.MinStockAlert ?? 0,
            AdditionalUnits = (dto.Units ?? new()).Select(u => new IngredientUnit
            {
                Unit = u.Unit!.Trim(),
                FactorToBase = u.FactorToBase!.Value
            }).ToList()
        });
        if (id is null)
            return BahanKembar();
        var idBahan = id.Value;
        // Stok awal masuk lewat jalur stok resmi, bukan lewat kolom cache. Jurnalnya ditulis
        // bersamaan: bahan yang sudah ada di rak tapi tidak pernah membukukan nilainya membuat
        // saldo persediaan di neraca kurang (pernah 8 bahan, Rp 5,7 juta) tanpa terlihat.
        if (dto.CurrentStock is > 0 and var stokAwal)
        {
            var warehouseId = await stock.GetLocationIdByTypeAsync("Warehouse");
            await stock.ApplyIngredientMasterStockAsync(warehouseId, idBahan, stokAwal,
                stokAwal: true, userId, "Stok awal bahan baku");
        }
        return Results.Created($"/api/ingredients/{idBahan}", new { id = idBahan });
    }).RequireAuthorization("master-data-write");

    app.MapPut("/api/ingredients/{id:int}", async (int id, IngredientDto dto, IIngredientRepository repo, ILocationStockRepository stock, HttpContext ctx) =>
    {
        if (GalatBahan(dto) is { } galat)
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galat });

        var sebelumnya = await repo.GetByIdAsync(id);
        if (sebelumnya is null)
            return Results.NotFound(new { errorCode = ErrorCodes.NotFound, errorMessage = "Bahan baku tidak ditemukan." });

        var nama = dto.Name!.Trim();
        if (await repo.IsNameTakenAsync(nama, id))
            return BahanKembar();

        var userId = int.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
        // Nilai yang tidak dikirim berarti "jangan diubah", bukan nol. Formulir ubah harga tidak
        // mengirim stok; membaca ketiadaannya sebagai nol akan menghapus persediaan yang tercatat.
        var lama = new
        {
            nama = sebelumnya.Name, hargaPerUnitMinor = sebelumnya.PricePerUnit,
            satuan = sebelumnya.Unit, minStok = sebelumnya.MinStockAlert,
            satuanTambahan = SebutSatuanTambahan(sebelumnya)
        };
        sebelumnya.Name = nama;
        sebelumnya.Brand = string.IsNullOrWhiteSpace(dto.Brand) ? null : dto.Brand.Trim();
        sebelumnya.Unit = dto.Unit!.Trim();
        sebelumnya.PricePerUnit = dto.PricePerUnit ?? sebelumnya.PricePerUnit;
        sebelumnya.MinStockAlert = dto.MinStockAlert ?? sebelumnya.MinStockAlert;
        // Tidak mengirim "units" berarti jangan diubah; mengirim daftar kosong berarti menghapus
        // semua satuan tambahan — dan pemicu menolak bila ada resep yang masih memakainya.
        if (dto.Units is { } satuanBaru)
            sebelumnya.AdditionalUnits = satuanBaru.Select(u => new IngredientUnit
            {
                Unit = u.Unit!.Trim(),
                FactorToBase = u.FactorToBase!.Value
            }).ToList();
        try
        {
            await repo.UpdateAsync(sebelumnya);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Results.BadRequest(new
            {
                errorCode = ErrorCodes.ValidationFailed,
                errorMessage = PesanPemicu(ex)
            });
        }

        // Angka di layar bahan baku adalah TOTAL stok; selisihnya dibukukan di gudang utama.
        if (dto.CurrentStock is { } stokBaru)
        {
            var delta = stokBaru - await stock.GetTotalStockAsync(id);
            if (delta != 0)
            {
                var warehouseId = await stock.GetLocationIdByTypeAsync("Warehouse");
                try
                {
                    // Bukan penyertaan modal baru: koreksi total di layar diperlakukan seperti
                    // hasil opname, yakni melawan Beban Selisih Persediaan.
                    await stock.ApplyIngredientMasterStockAsync(warehouseId, id, delta,
                        stokAwal: false, userId, "Penyesuaian dari layar bahan baku");
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = ex.Message });
                }
            }
        }
        // Harga bahan menentukan HPP semua produk yang memakainya, jadi nilai lamanya wajib
        // bisa dibaca balik di jejak audit.
        await Jejak.CatatDetilAsync(ctx, "ubah bahan baku", "Ingredient", id,
            lama,
            new
            {
                nama = sebelumnya.Name, hargaPerUnitMinor = sebelumnya.PricePerUnit,
                satuan = sebelumnya.Unit, minStok = sebelumnya.MinStockAlert,
                satuanTambahan = SebutSatuanTambahan(sebelumnya)
            });
        return Results.Ok();
    }).RequireAuthorization("master-data-write");

    app.MapDelete("/api/ingredients/{id:int}", async (int id, IIngredientRepository repo, HttpContext ctx) =>
    {
        var bahan = await repo.GetByIdAsync(id);
        try
        {
            await repo.DeleteAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            // Stok yang tinggal di rak membuat neraca dan daftar bahan tidak lagi menunjuk
            // barang yang sama, jadi penonaktifan ditolak dan alasannya disampaikan.
            return Results.BadRequest(new { error = ex.Message });
        }
        await Jejak.CatatDetilAsync(ctx, "nonaktifkan bahan baku", "Ingredient", id,
            bahan is null ? null : new { nama = bahan.Name, aktif = true },
            new { aktif = false });
        return Results.Ok();
    }).RequireAuthorization("master-data-write");

    // Products
    // Kasir memakai katalog ini untuk mencatat pesanan; resep dan harga bahan bukan urusannya.
    // Dapur/Owner/Admin tetap menerima baris resep karena layar Resep & HPP membutuhkannya.
    app.MapGet("/api/products", async (IProductTemplateRepository repo, HttpContext ctx) =>
    {
        var produk = await repo.GetAllActiveAsync();
        var bolehResep = BolehBacaResep(ctx);
        return Results.Ok(produk.Select(p => bolehResep
            ? (object)new
            {
                p.Id, p.Name, p.Category, p.Barcode,
                SellingPrice = p.SellingPrice, p.IsActive, p.RecipeLines
            }
            : new { p.Id, p.Name, p.Category, p.Barcode, SellingPrice = p.SellingPrice, p.IsActive }));
    }).RequireAuthorization();

    app.MapGet("/api/products/{id:int}", async (int id, IProductTemplateRepository repo, HttpContext ctx) =>
    {
        var product = await repo.GetByIdWithRecipesAsync(id);
        if (product == null) return Results.NotFound();
        if (!BolehBacaResep(ctx))
            return Results.Ok(new
            {
                product.Id,
                product.Name,
                product.Category,
                product.Barcode,
                SellingPriceMinorUnits = product.SellingPrice,
                product.IsActive,
            });
        return Results.Ok(new
        {
            product.Id,
            product.Name,
            product.Category,
            product.Barcode,
            SellingPriceMinorUnits = product.SellingPrice,
            product.IsActive,
            product.RecipeLines,
            Hpp = product.CalculateHpp()
        });
    }).RequireAuthorization();

    app.MapPost("/api/products", async (CreateProductDto dto, IProductTemplateRepository repo,
        IIngredientRepository repoBahan, HttpContext ctx) =>
    {
        if (GalatProduk(dto) is { } galat)
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galat });
        if (await GalatSatuanResepAsync(dto.Lines, repoBahan) is { } galatResep)
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galatResep });
        var product = new Seruni22Bakery.Domain.Entities.ProductTemplate
        {
            Name = dto.Name,
            Category = dto.Category,
            Barcode = NormalisasiBarcode(dto.Barcode),
            SellingPrice = dto.SellingPriceMinorUnits,
            IsActive = true
        };
        var lines = dto.Lines.Select(l => new Seruni22Bakery.Domain.Entities.RecipeLine
        {
            IngredientId = l.IngredientId,
            Quantity = l.Quantity,
            Unit = string.IsNullOrWhiteSpace(l.Unit) ? null : l.Unit.Trim()
        });
        int id;
        try
        {
            id = await repo.CreateAsync(product, lines);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            // Barcode adalah kode yang ditempel di kemasan; dua produk dengan kode sama membuat
            // hasil pindai tidak bisa ditentukan. Jawab sebagai konflik, bukan galat server.
            if (ex.Message.Contains("Barcode", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new
                {
                    errorCode = ErrorCodes.DuplicateKey,
                    errorMessage = "Barcode itu sudah dipakai produk lain."
                });
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = PesanPemicu(ex) });
        }
        return Results.Ok(new { id });
    }).RequireAuthorization("master-data-write");

    app.MapPut("/api/products/{id:int}", async (int id, CreateProductDto dto, IProductTemplateRepository repo,
        IIngredientRepository repoBahan, HttpContext ctx) =>
    {
        if (GalatProduk(dto) is { } galat)
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galat });
        if (await GalatSatuanResepAsync(dto.Lines, repoBahan) is { } galatResep)
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galatResep });
        var product = await repo.GetByIdWithRecipesAsync(id);
        if (product == null) return Results.NotFound();
        var hargaSebelum = product.SellingPrice;
        var resepSebelum = product.RecipeLines.Count;
        product.Name = dto.Name;
        product.Category = dto.Category;
        product.Barcode = NormalisasiBarcode(dto.Barcode);
        product.SellingPrice = dto.SellingPriceMinorUnits;
        var lines = dto.Lines.Select(l => new Seruni22Bakery.Domain.Entities.RecipeLine
        {
            IngredientId = l.IngredientId,
            Quantity = l.Quantity,
            Unit = string.IsNullOrWhiteSpace(l.Unit) ? null : l.Unit.Trim()
        });
        try
        {
            await repo.UpdateAsync(product, lines);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            if (ex.Message.Contains("Barcode", StringComparison.OrdinalIgnoreCase))
                return Results.Conflict(new
                {
                    errorCode = ErrorCodes.DuplicateKey,
                    errorMessage = "Barcode itu sudah dipakai produk lain."
                });
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = PesanPemicu(ex) });
        }
        // Harga jual dan isi resep mengubah uang serta HPP, jadi nilai lamanya ikut tercatat:
        // "harga ini berubah kapan dan dari berapa" harus bisa dijawab setahun kemudian.
        await Jejak.CatatDetilAsync(ctx, "ubah produk", "ProductTemplate", id,
            new
            {
                hargaJualMinor = hargaSebelum, barisResep = resepSebelum,
                barisBersatuan = product.RecipeLines.Count(l => !string.IsNullOrWhiteSpace(l.Unit))
            },
            new
            {
                hargaJualMinor = dto.SellingPriceMinorUnits, barisResep = dto.Lines.Count,
                barisBersatuan = dto.Lines.Count(l => !string.IsNullOrWhiteSpace(l.Unit))
            });
        return Results.Ok();
    }).RequireAuthorization("master-data-write");

    app.MapDelete("/api/products/{id:int}", async (int id, IProductTemplateRepository repo, HttpContext ctx) =>
    {
        var product = await repo.GetByIdWithRecipesAsync(id);
        if (product == null) return Results.NotFound();
        // UpdateAsync hanya menulis Name/Category/SellingPrice, jadi IsActive=false yang diatur
        // di sini dulu tidak pernah sampai ke baris database: klien dapat 200 sementara produk
        // tetap muncul di daftar. DeleteAsync memang tugasnya menonaktifkan produk.
        try
        {
            await repo.DeleteAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        await Jejak.CatatDetilAsync(ctx, "nonaktifkan produk", "ProductTemplate", id,
            new { nama = product.Name, hargaJualMinor = product.SellingPrice, aktif = true },
            new { nama = product.Name, aktif = false });
        return Results.Ok();
    }).RequireAuthorization("master-data-write");

    // ── Production & Work Orders (SPK) ──────────────────────────────────────────
    app.MapGet("/api/production/orders", async (IProductionRepository repo) =>
        Results.Ok(await repo.GetAllAsync())).RequireAuthorization("production-write");

    app.MapGet("/api/production/orders/{id:int}", async (int id, IProductionRepository repo) =>
    {
        var wo = await repo.GetByIdAsync(id);
        return wo is null ? Results.NotFound() : Results.Ok(wo);
    }).RequireAuthorization("production-write");

    app.MapPost("/api/production/orders", async (CreateWorkOrderDto dto, IProductionRepository repo, IProductTemplateRepository productRepo, IDocumentSequenceRepository documents, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        if (dto.TargetQuantity <= 0)
            return Results.BadRequest(new { errorMessage = "Target jumlah produksi harus lebih besar dari nol." });
        if (MoneyRules.CostError(dto.LaborCost, "Biaya tenaga kerja") is { } laborError)
            return Results.BadRequest(new { errorMessage = laborError });
        if (MoneyRules.CostError(dto.OverheadCost, "Biaya overhead") is { } overheadError)
            return Results.BadRequest(new { errorMessage = overheadError });

        var product = await productRepo.GetByIdWithRecipesAsync(dto.ProductTemplateId);
        if (product == null) return Results.BadRequest("Product template not found.");

        var wo = new Seruni22Bakery.Domain.Entities.WorkOrder
        {
            OrderNumber = await documents.NextNumberAsync(DocumentKind.WorkOrder, BusinessTime.Now()),
            ProductTemplateId = dto.ProductTemplateId,
            TargetQuantity = dto.TargetQuantity,
            LaborCost = dto.LaborCost,
            OverheadCost = dto.OverheadCost,
            StartDate = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        // BaseQuantity sudah dalam satuan stok: 250 g pada bahan bersatuan kg menjadi 0,25 kg.
        var materials = product.RecipeLines.Select(r => new Seruni22Bakery.Domain.Entities.WorkOrderMaterial
        {
            IngredientId = r.IngredientId,
            StandardQty = r.BaseQuantity * dto.TargetQuantity,
            ActualQty = r.BaseQuantity * dto.TargetQuantity,
            UnitCost = r.Ingredient?.PricePerUnit ?? 0
        }).ToList();

        wo.TotalMaterialCost = materials.Sum(m =>
            (long)Math.Round(m.ActualQty * m.UnitCost, 0, MidpointRounding.AwayFromZero));

        var id = await repo.CreateAsync(wo, materials);
        return Results.Created($"/api/production/orders/{id}", new { id, orderNumber = wo.OrderNumber });
    }).RequireAuthorization("production-write");

    app.MapPost("/api/production/orders/{id:int}/complete", async (int id, CompleteWorkOrderDto dto,
        IProductionRepository repo, AccountingService acct, NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var wo = await repo.GetByIdAsync(id);
        if (wo == null) return Results.NotFound();

        if (dto.ActualGoodQty < 0 || dto.ActualFailedQty < 0)
            return Results.BadRequest(new { errorMessage = "Jumlah produk jadi dan rusak tidak boleh negatif." });
        if (dto.ActualGoodQty + dto.ActualFailedQty <= 0)
            return Results.BadRequest(new { errorMessage = "Penyelesaian SPK harus melaporkan hasil produksi (baik atau rusak) lebih dari nol." });

        // Bahan dan harganya milik rencana SPK di server, bukan payload klien. Klien hanya
        // boleh melaporkan berapa yang benar-benar terpakai; harga satuan diambil dari baris
        // rencana yang tersimpan saat SPK dibuat, supaya nilai jurnal dan HPP tidak bisa
        // ditekan dari luar. Setiap bahan rencana wajib dilaporkan tepat satu kali: bahan
        // asing atau duplikat akan menggeser stok bahan yang tidak ada hubungannya dengan SPK.
        var bahanMasuk = dto.Materials ?? new();
        var bahanRencana = wo.Materials;
        if (bahanRencana.GroupBy(m => m.IngredientId).Any(g => g.Count() > 1))
            return Results.BadRequest(new { errorMessage = "Rencana SPK memuat bahan yang sama lebih dari satu baris; perbaiki resep lebih dulu." });
        if (bahanMasuk.GroupBy(m => m.IngredientId).Any(g => g.Count() > 1))
            return Results.BadRequest(new { errorMessage = "Satu bahan dilaporkan lebih dari sekali." });
        var bahanAsing = bahanMasuk
            .Where(m => bahanRencana.All(r => r.IngredientId != m.IngredientId))
            .Select(m => m.IngredientId)
            .ToList();
        if (bahanAsing.Count > 0)
            return Results.BadRequest(new { errorMessage = $"Bahan #{string.Join(", #", bahanAsing)} tidak ada dalam rencana SPK ini." });
        var bahanHilang = bahanRencana
            .Where(r => bahanMasuk.All(m => m.IngredientId != r.IngredientId))
            .Select(r => r.IngredientName ?? $"#{r.IngredientId}")
            .ToList();
        if (bahanHilang.Count > 0)
            return Results.BadRequest(new { errorMessage = $"Bahan rencana belum dilaporkan: {string.Join(", ", bahanHilang)}." });

        var materials = bahanRencana.Select(r => new Seruni22Bakery.Domain.Entities.WorkOrderMaterial
        {
            IngredientId = r.IngredientId,
            ActualQty = bahanMasuk.First(m => m.IngredientId == r.IngredientId).ActualQty,
            UnitCost = r.UnitCost
        }).ToList();

        var scraps = (dto.Scraps ?? new()).Select(s => new Seruni22Bakery.Domain.Entities.WorkOrderScrap
        {
            WorkOrderId = id,
            Quantity = s.Quantity,
            ReasonCategory = s.ReasonCategory,
            ActionTaken = s.ActionTaken,
            Notes = s.Notes,
            CreatedByUserId = userId
        }).ToList();

        var totalMaterialCost = materials.Sum(m => (long)Math.Round(m.ActualQty * m.UnitCost, 0, MidpointRounding.AwayFromZero));
        var totalUnits = dto.ActualGoodQty + dto.ActualFailedQty;
        // Hanya scrap yang dibuang yang menjadi kerugian; unit rework masih akan diproduksi lagi.
        // Tanpa satu pun unit baik, seluruh bahan adalah kerugian: tidak ada aset produk jadi
        // yang bisa didukung stok, jadi tidak boleh ada bagian yang tetap dimodalkan.
        var lossUnits = Seruni22Bakery.Domain.Common.ProductionCostRules.LossUnits(scraps);
        var scrapLossCost = dto.ActualGoodQty == 0
            ? totalMaterialCost
            : Seruni22Bakery.Domain.Common.ProductionCostRules.LossCost(totalMaterialCost, lossUnits, totalUnits);

        if (materials.Any(m => m.ActualQty < 0))
            return Results.BadRequest(new { errorMessage = "Jumlah bahan terpakai tidak boleh negatif." });

        // Nilai ini dibatasi CHECK constraint di tabel scrap; tanpa pagar di sini, layar
        // hanya menerima teks SQLite mentah sebagai pesan galat.
        var scrapReasons = new[] { "Gosong", "Adonan_Gagal", "Salah_Resep", "Mesin_Error", "Lainnya" };
        var scrapActions = new[] { "Scrap_Loss", "Rework" };
        foreach (var s in scraps)
        {
            if (s.Quantity <= 0)
                return Results.BadRequest(new { errorMessage = "Jumlah produk scrap harus lebih besar dari nol." });
            if (!scrapReasons.Contains(s.ReasonCategory))
                return Results.BadRequest(new { errorMessage = $"Alasan scrap tidak dikenal: {s.ReasonCategory}. Pilihan: {string.Join(", ", scrapReasons)}." });
            if (!scrapActions.Contains(s.ActionTaken))
                return Results.BadRequest(new { errorMessage = $"Tindakan scrap tidak dikenal: {s.ActionTaken}. Pilihan: {string.Join(", ", scrapActions)}." });
        }
        if (dto.ActualFailedQty > 0 && scraps.Sum(s => s.Quantity) < dto.ActualFailedQty)
            return Results.BadRequest(new { errorMessage = "Jumlah scrap lebih kecil dari produk rusak yang dilaporkan." });

        JournalEntry? journal;
        try
        {
            journal = await acct.BuildProductionCompletionAsync(
                id, wo.OrderNumber, totalMaterialCost, scrapLossCost,
                wo.LaborCost, wo.OverheadCost, dto.ActualGoodQty, userId);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { errorMessage = ex.Message }, statusCode: 400);
        }

        // Nilai per unit yang menempel pada stok produk jadi: bahan yang lolos + tenaga kerja + overhead.
        var costPerGoodUnit = Seruni22Bakery.Domain.Common.ProductionCostRules.CostPerGoodUnit(
            totalMaterialCost, wo.LaborCost, wo.OverheadCost, scrapLossCost, dto.ActualGoodQty);

        try
        {
            await repo.CompleteWorkOrderAsync(
                id, dto.ActualGoodQty, dto.ActualFailedQty, materials, scraps, journal, costPerGoodUnit);
        }
        catch (InvalidOperationException ex)
        {
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePenggunaAsync(
                userId,
                new KirimNotifikasi("transaction.production.failed", NotificationSeverity.Warning,
                    "Penyelesaian SPK gagal", ex.Message, "WorkOrder", id), DateTime.UtcNow));
            return Results.Json(new { errorMessage = ex.Message }, statusCode: 400);
        }

        await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
            [UserRole.Owner, UserRole.Admin, UserRole.Dapur, UserRole.Pegawai],
            new KirimNotifikasi("transaction.production.completed", NotificationSeverity.Info,
                "Produksi selesai",
                $"SPK {wo.OrderNumber}: {dto.ActualGoodQty:0.###} unit siap, {dto.ActualFailedQty:0.###} rusak. "
                + $"HPP per unit sudah dibukukan.",
                "WorkOrder", id, $"transaction.production.completed:{id}"), DateTime.UtcNow));

        return Results.Ok(new { message = "Work order completed successfully" });
    }).RequireAuthorization("production-write");

    app.MapPut("/api/production/orders/{id:int}/cancel", async (int id, CancelWorkOrderDto dto,
        IProductionRepository repo, NotificationService notif, HttpContext ctx) =>
    {
        try
        {
            var wo = await repo.GetByIdAsync(id);
            if (wo == null) return Results.NotFound();
            if (wo.Status == Seruni22Bakery.Domain.Entities.WorkOrderStatus.Completed ||
                wo.Status == Seruni22Bakery.Domain.Entities.WorkOrderStatus.Cancelled)
            {
                return Results.Json(new { errorMessage = $"SPK tidak dapat dibatalkan karena statusnya sudah {wo.Status}." }, statusCode: 400);
            }
            await repo.CancelWorkOrderAsync(id, dto.Reason);
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
                new KirimNotifikasi("transaction.production.cancelled", NotificationSeverity.Warning,
                    $"SPK {wo.OrderNumber} dibatalkan",
                    string.IsNullOrWhiteSpace(dto.Reason) ? "Alasan tidak dicatat." : $"Alasan: {dto.Reason}",
                    "WorkOrder", id, $"transaction.production.cancelled:{id}"), DateTime.UtcNow));
            return Results.Ok(new { message = "Work order cancelled successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { errorMessage = ex.Message }, statusCode: 400);
        }
    }).RequireAuthorization("production-cancel");

    // ── Pemeriksaan Mutu (QC) ──────────────────────────────────────────────────
    // Hasil QC disimpan per SPK, bukan hanya ditampilkan: keputusan lulus/gagal, jumlah
    // sampel, dan catatan pemeriksa harus bisa dibaca lagi setelah layar ditutup.
    app.MapGet("/api/production/quality-checks", async (IQualityCheckRepository repo) =>
        Results.Ok(await repo.GetAllAsync())).RequireAuthorization("production-write");

    app.MapPost("/api/production/quality-checks", async (
        CreateQualityCheckDto dto, IQualityCheckRepository repo, IProductionRepository production,
        NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        if (dto.SampleQuantity <= 0)
            return Results.BadRequest(new { errorMessage = "Jumlah sampel pemeriksaan harus lebih dari nol." });
        if (dto.PassedQuantity < 0 || dto.FailedQuantity < 0)
            return Results.BadRequest(new { errorMessage = "Jumlah lulus dan gagal tidak boleh negatif." });
        if (dto.PassedQuantity + dto.FailedQuantity > dto.SampleQuantity)
            return Results.BadRequest(new { errorMessage = "Jumlah lulus + gagal melebihi jumlah sampel." });

        var wo = await production.GetByIdAsync(dto.WorkOrderId);
        if (wo is null) return Results.NotFound();
        if (wo.Status != Seruni22Bakery.Domain.Entities.WorkOrderStatus.Completed)
            return Results.BadRequest(new { errorMessage = "Pemeriksaan mutu hanya untuk SPK yang sudah selesai diproduksi." });
        if (await repo.GetByWorkOrderAsync(dto.WorkOrderId) is not null)
            return Results.BadRequest(new { errorMessage = "SPK ini sudah diperiksa mutunya. Satu SPK satu hasil pemeriksaan." });

        // Keputusan dihitung server: layar tidak boleh mengirim "Passed" bersama angka gagal > 0.
        var keputusan = dto.FailedQuantity == 0
            ? Seruni22Bakery.Domain.Entities.QualityDecision.Passed
            : Seruni22Bakery.Domain.Entities.QualityDecision.Failed;

        var check = new Seruni22Bakery.Domain.Entities.QualityCheck
        {
            WorkOrderId = dto.WorkOrderId,
            CheckedAt = DateTime.UtcNow,
            CheckedByUserId = userId,
            SampleQuantity = dto.SampleQuantity,
            PassedQuantity = dto.PassedQuantity,
            FailedQuantity = dto.FailedQuantity,
            Decision = keputusan,
            AppearanceNotes = string.IsNullOrWhiteSpace(dto.AppearanceNotes) ? null : dto.AppearanceNotes.Trim(),
            TasteNotes = string.IsNullOrWhiteSpace(dto.TasteNotes) ? null : dto.TasteNotes.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
        };

        try
        {
            var id = await repo.CreateAsync(check);
            await Jejak.CatatDetilAsync(ctx, "catat pemeriksaan mutu", "QualityCheck", id,
                null,
                new { spk = wo.OrderNumber, sampel = dto.SampleQuantity, lulus = dto.PassedQuantity,
                      gagal = dto.FailedQuantity, keputusan = keputusan.ToString() });
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
                new KirimNotifikasi(
                    keputusan == Seruni22Bakery.Domain.Entities.QualityDecision.Passed
                        ? "transaction.qc.passed" : "transaction.qc.failed",
                    keputusan == Seruni22Bakery.Domain.Entities.QualityDecision.Passed
                        ? NotificationSeverity.Info : NotificationSeverity.Critical,
                    keputusan == Seruni22Bakery.Domain.Entities.QualityDecision.Passed
                        ? $"QC lulus: {wo.OrderNumber}" : $"QC gagal: {wo.OrderNumber}",
                    $"Sampel {dto.SampleQuantity:0.###}, lulus {dto.PassedQuantity:0.###}, gagal {dto.FailedQuantity:0.###}.",
                    "QualityCheck", id, $"transaction.qc:{id}"), DateTime.UtcNow));
            return Results.Created($"/api/production/quality-checks/{id}", new { id, keputusan = keputusan.ToString() });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { errorMessage = ex.Message }, statusCode: 400);
        }
    }).RequireAuthorization("production-write");

    // Ringkasan HPP: standar dari resep + aktual dari SPK selesai, dihitung dari data tersimpan.
    app.MapGet("/api/production/cost-summary", async (CostSummaryService costs) =>
        Results.Ok(await costs.GetAsync())).RequireAuthorization("production-write");

    // ── Multi-Location Inventory ───────────────────────────────────────────────
    app.MapGet("/api/inventory/locations", async (ILocationStockRepository repo) =>
        Results.Ok(await repo.GetLocationsAsync())).RequireAuthorization("inventory-read");

    app.MapGet("/api/inventory/locations/{id:int}/stock", async (int id, ILocationStockRepository repo) =>
        Results.Ok(await repo.GetStockByLocationAsync(id))).RequireAuthorization("inventory-read");

    app.MapGet("/api/inventory/movements", async (int? locationId, int? limit, ILocationStockRepository repo) =>
        Results.Ok(await repo.GetMovementsAsync(locationId, limit is > 0 and <= 500 ? limit.Value : 100))).RequireAuthorization("inventory-read");

    // Stok produk jadi dibaca terpisah dari bahan: layarnya menampilkan dua hal yang berbeda dan
    // saldo keduanya tidak boleh saling menambal.
    app.MapGet("/api/inventory/finished-goods", async (int? locationId, ILocationStockRepository repo) =>
        Results.Ok(await repo.GetFinishedGoodsStockAsync(locationId))).RequireAuthorization("inventory-read");

    app.MapPost("/api/inventory/transfer", async (TransferStockDto dto, ILocationStockRepository repo, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        try
        {
            await repo.TransferStockAsync(dto.FromLocationId, dto.ToLocationId, dto.IngredientId, dto.Quantity, userId, dto.Notes);
            return Results.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }).RequireAuthorization("inventory-write");

    app.MapPost("/api/inventory/adjust", async (AdjustStockDto dto, ILocationStockRepository repo, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var saldoSebelum = (await repo.GetStockByLocationAsync(dto.LocationId))
            .FirstOrDefault(s => s.IngredientId == dto.IngredientId)?.Quantity;
        try
        {
            await repo.AdjustStockAsync(dto.LocationId, dto.IngredientId, dto.NewQuantity, userId, dto.Reason);
            // Koreksi stok fisik mengubah nilai persediaan dan buku mutasi, jadi angka sebelum
            // dan sesudahnya dicatat berdampingan bersama alasannya.
            await Jejak.CatatDetilAsync(ctx, "koreksi stok (opname)", "LocationStock", dto.IngredientId,
                new { lokasi = dto.LocationId, jumlah = saldoSebelum },
                new { lokasi = dto.LocationId, jumlah = dto.NewQuantity, alasan = dto.Reason });
            return Results.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { errorMessage = ex.Message }, statusCode: 400);
        }
    }).RequireAuthorization("inventory-write");

    // ── Suppliers ─────────────────────────────────────────────────────────────
    app.MapGet("/api/suppliers", async (ISupplierRepository repo) =>
        Results.Ok(await repo.GetAllAsync())).RequireAuthorization("purchases-receive");

    app.MapPost("/api/suppliers", async (CreateSupplierDto dto, ISupplierRepository repo) =>
    {
        var supplier = new Supplier
        {
            Name = dto.Name, ContactName = dto.ContactName, Phone = dto.Phone,
            Email = dto.Email, Address = dto.Address, Notes = dto.Notes
        };
        var id = await repo.CreateAsync(supplier);
        return Results.Ok(new { id });
    }).RequireAuthorization("master-data-write");

    app.MapPut("/api/suppliers/{id:int}", async (int id, CreateSupplierDto dto, ISupplierRepository repo) =>
    {
        var supplier = await repo.GetByIdAsync(id);
        if (supplier == null) return Results.NotFound();
        supplier.Name = dto.Name; supplier.ContactName = dto.ContactName; supplier.Phone = dto.Phone;
        supplier.Email = dto.Email; supplier.Address = dto.Address; supplier.Notes = dto.Notes;
        await repo.UpdateAsync(supplier);
        return Results.Ok();
    }).RequireAuthorization("master-data-write");

    // ── Purchase Orders ────────────────────────────────────────────────────────
    // Halaman daftar PO (baris ikut). status=open menyaring PO yang masih menunggu tindakan;
    // jumlah seluruhnya dikirim lewat X-Total-Count.
    app.MapGet("/api/purchases", async (IPurchaseOrderRepository repo, string? status, int? limit, int? offset,
        HttpResponse res) =>
    {
        var take = limit is > 0 and <= 2000 ? limit.Value : 300;
        var skip = offset is > 0 ? offset.Value : 0;
        var total = await repo.CountAsync(status);
        res.Headers["X-Total-Count"] = total.ToString();
        return Results.Ok(await repo.GetPageAsync(status, take, skip));
    }).RequireAuthorization("purchases-receive");

    app.MapGet("/api/purchases/{id:int}", async (int id, IPurchaseOrderRepository repo) =>
    {
        var po = await repo.GetByIdAsync(id);
        return po == null ? Results.NotFound() : Results.Ok(po);
    }).RequireAuthorization("purchases-receive");

    app.MapPost("/api/purchases", async (CreatePurchaseOrderDto dto, IPurchaseOrderRepository repo,
        IIngredientRepository ingredients, IDocumentSequenceRepository documents, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        // Tanggal dari layar bisa berupa string apa pun; Parse yang melempar akan jadi 500.
        DateTime? expectedDate = null;
        if (!string.IsNullOrWhiteSpace(dto.ExpectedDate))
        {
            if (!DateTime.TryParse(dto.ExpectedDate, out var parsed))
                return Results.BadRequest(new { error = "Tanggal perkiraan penerimaan tidak valid, gunakan format YYYY-MM-DD." });
            expectedDate = parsed.Date;
        }
        var po = new PurchaseOrder
        {
            PoNumber = await documents.NextNumberAsync(DocumentKind.PurchaseOrder, BusinessTime.Now()),
            SupplierId = dto.SupplierId,
            ExpectedDate = expectedDate,
            Notes = dto.Notes,
            CreatedByUserId = userId,
            Lines = dto.Lines.Select(l => new PurchaseOrderLine
            {
                IngredientId = l.IngredientId,
                OrderedQty = l.OrderedQty,
                UnitCostMinorUnits = l.UnitCostMinorUnits,
                Unit = l.Unit
            }).ToList()
        };

        if (po.Lines.Count == 0)
            return Results.BadRequest(new { errorMessage = "PO harus punya minimal satu baris bahan." });
        var lineError = po.Lines.Select(l =>
                l.OrderedQty <= 0 ? "Jumlah pesan harus lebih besar dari nol."
                : l.OrderedQty > 100_000 ? "Jumlah pesan melebihi batas wajar 100.000 satuan."
                : MoneyRules.CostError(l.UnitCostMinorUnits, "Harga per satuan")
                ?? (string.IsNullOrWhiteSpace(l.Unit) ? "Satuan bahan wajib diisi (kg, pcs, liter)." : null))
            .FirstOrDefault(e => e is not null);
        if (lineError is not null)
            return Results.BadRequest(new { errorMessage = lineError });

        // Satuan stok mengikuti master bahan: barang masuk tanpa konversi, jadi PO bersatuan
        // lain akan mencampur kg dan pcs pada angka stok yang sama. Bahan nonaktif juga tidak
        // boleh dipesan lagi karena penerimaannya tidak bisa dipakai produksi.
        foreach (var l in po.Lines)
        {
            var bahan = await ingredients.GetByIdAsync(l.IngredientId);
            if (bahan is null)
                return Results.BadRequest(new { errorMessage = $"Bahan Id {l.IngredientId} tidak ditemukan." });
            if (!bahan.IsActive)
                return Results.BadRequest(new { errorMessage = $"Bahan {bahan.Name} sudah tidak aktif; aktifkan dulu atau pilih bahan lain." });
            if (!string.Equals(bahan.Unit?.Trim(), l.Unit.Trim(), StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { errorMessage = $"Satuan {bahan.Name} adalah '{bahan.Unit}', bukan '{l.Unit}'. Stok tidak dikonversi, jadi PO harus memakai satuan bahan." });
        }

        try
        {
            var id = await repo.CreateAsync(po);
            return Results.Ok(new { id });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { errorMessage = ex.Message }, statusCode: 400);
        }
    }).RequireAuthorization("purchases-create");

    app.MapPost("/api/purchases/{id:int}/receive", async (int id, ReceiveGoodsRequestDto dto, PurchaseService svc,
        NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;

        // Tanggal kedaluwarsa datang dari layar sebagai teks; Parse yang melempar akan jadi 500,
        // jadi nilai yang tidak sah dijawab sebagai galat validasi yang bisa dibaca pengguna.
        var baris = new List<ReceiveGoodsLineDto>();
        foreach (var l in dto.Lines)
        {
            DateTime? kedaluwarsa = null;
            if (!string.IsNullOrWhiteSpace(l.ExpiryDate))
            {
                if (!DateTime.TryParse(l.ExpiryDate, out var parsed))
                    return Results.BadRequest(new { error = $"Tanggal kedaluwarsa lot {l.BatchNumber} tidak valid, gunakan format YYYY-MM-DD." });
                kedaluwarsa = parsed.Date;
            }
            baris.Add(new ReceiveGoodsLineDto(
                l.PurchaseOrderLineId, l.IngredientId, l.ReceivedQty, l.UnitCostMinorUnits, l.LocationId,
                string.IsNullOrWhiteSpace(l.BatchNumber) ? null : l.BatchNumber.Trim(), kedaluwarsa));
        }

        var receiveDto = new ReceiveGoodsDto(dto.Notes, userId, baris);
        var result = await svc.ReceiveGoodsAsync(id, receiveDto);
        if (result.Success)
        {
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin, UserRole.Dapur],
                new KirimNotifikasi("transaction.purchase.received", NotificationSeverity.Info,
                    "Barang diterima",
                    $"PO #{id} menerima {baris.Count} baris barang; stok dan jurnal sudah tercatat.",
                    "PurchaseOrder", id, $"transaction.purchase.received:{id}"), DateTime.UtcNow));
            return Results.Ok();
        }

        if (userId is { } pelaku)
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePenggunaAsync(
                pelaku,
                new KirimNotifikasi("transaction.purchase.receive_failed", NotificationSeverity.Warning,
                    "Penerimaan barang gagal", result.ErrorMessage ?? "Penerimaan ditolak.",
                    "PurchaseOrder", id), DateTime.UtcNow));
        return Results.Json(new { errorCode = result.ErrorCode, errorMessage = result.ErrorMessage }, statusCode: 400);
    }).RequireAuthorization("purchases-receive");

    app.MapPut("/api/purchases/{id:int}/cancel", async (int id, IPurchaseOrderRepository repo) =>
    {
        var po = await repo.GetByIdAsync(id);
        if (po == null) return Results.NotFound();
        if (po.Status == PurchaseOrderStatus.Cancelled)
            return Results.BadRequest(new { error = "PO sudah dibatalkan" });
        if (po.Status == PurchaseOrderStatus.Received)
            return Results.BadRequest(new { error = "PO sudah diterima penuh, tidak bisa dibatalkan" });
        // Penerimaan barang langsung menambah stok dan menerbitkan jurnal. Membatalkan PO yang
        // sebagian diterima akan meninggalkan stok dan utang yang tidak punya dokumen induk,
        // jadi jalurnya harus lewat retur pembelian, bukan pembatalan.
        if (po.Lines.Any(l => l.ReceivedQty > 0))
            return Results.BadRequest(new
            {
                error = "Sebagian barang sudah diterima, pembatalan akan meninggalkan stok dan jurnal yatim. Gunakan retur pembelian."
            });
        // Penjagaan yang sama diulang di dalam transaksi repository; balapan "batal vs terima"
        // ditolak di sana, bukan hanya oleh pemeriksaan layar ini.
        try
        {
            await repo.CancelAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        return Results.Ok();
    }).RequireAuthorization("purchases-cancel");

    // ── Hutang pemasok: retur dan pembayaran ────────────────────────────────────
    // Kredit 2-002 Utang Usaha lahir per struk penerimaan, jadi sisa hutang dibaca dan
    // diturunkan per struk juga. Dua jalur turunnya: barang dikembalikan (retur, kebijakan
    // purchases-return) atau uang dibayarkan (finance-write). Daftar hutangnya sendiri adalah
    // angka buku besar, sehingga hanya peran yang boleh membaca buku yang bisa memuatnya.
    app.MapGet("/api/payables", async (ISupplierSettlementRepository repo, bool? includeLunas, int? limit) =>
        Results.Ok(await repo.GetPayablesAsync(
            includeLunas ?? false, limit is > 0 and <= 500 ? limit.Value : 200)))
        .RequireAuthorization("finance-read");

    app.MapGet("/api/payables/{receiptId:int}", async (int receiptId, ISupplierSettlementRepository repo) =>
    {
        var struk = await repo.GetPayableAsync(receiptId);
        return struk is null ? Results.NotFound() : Results.Ok(struk);
    }).RequireAuthorization("finance-read");

    app.MapGet("/api/supplier-returns", async (
        ISupplierSettlementRepository repo, int? goodsReceiptId, int? limit) =>
        Results.Ok(await repo.GetReturnsAsync(
            goodsReceiptId, limit is > 0 and <= 500 ? limit.Value : 200)))
        .RequireAuthorization("finance-read");

    app.MapGet("/api/supplier-payments", async (
        ISupplierSettlementRepository repo, int? goodsReceiptId, int? limit) =>
        Results.Ok(await repo.GetPaymentsAsync(
            goodsReceiptId, limit is > 0 and <= 500 ? limit.Value : 200)))
        .RequireAuthorization("finance-read");

    app.MapPost("/api/supplier-returns", async (
        CreateSupplierReturnDto dto, SupplierSettlementService svc, NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var baris = dto.Lines?.Select(l => new ReturnSupplierLineRequest(
            l.GoodsReceiptLineId, l.Quantity, l.LocationId)).ToList();
        var result = await svc.ReturnAsync(dto.GoodsReceiptId,
            new ReturnSupplierRequest(userId, dto.Reason, dto.Notes, baris, dto.IdempotencyKey));

        if (result.Success)
        {
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin],
                new KirimNotifikasi("transaction.supplier.return_created", NotificationSeverity.Warning,
                    "Retur pemasok dicatat",
                    $"Struk penerimaan #{dto.GoodsReceiptId}: {baris?.Count ?? 0} baris barang dikembalikan " +
                    $"dan hutang terkait dilepas. Alasan: {dto.Reason}.",
                    "SupplierReturn", result.Data, $"transaction.supplier.return_created:{result.Data}"), DateTime.UtcNow));
            return Results.Created($"/api/supplier-returns/{result.Data}", new { id = result.Data });
        }

        var status = result.ErrorCode == ErrorCodes.NotFound
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        return Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: status);
    }).RequireAuthorization("purchases-return");

    app.MapPost("/api/supplier-payments", async (
        CreateSupplierPaymentDto dto, SupplierSettlementService svc, NotificationService notif, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await svc.PayAsync(dto.GoodsReceiptId,
            new PaySupplierRequest(userId, dto.Amount, dto.Method, dto.Notes, dto.IdempotencyKey));

        if (result.Success)
        {
            await CatatNotifikasiAmanAsync(notif, () => notif.KirimKePeranAsync(
                [UserRole.Owner, UserRole.Admin],
                new KirimNotifikasi("transaction.supplier.payment_created", NotificationSeverity.Info,
                    "Pembayaran pemasok dicatat",
                    $"Struk penerimaan #{dto.GoodsReceiptId} dibayar {Money.Format(dto.Amount)} " +
                    $"({dto.Method ?? "Tunai"}).",
                    "SupplierPayment", result.Data, $"transaction.supplier.payment_created:{result.Data}"), DateTime.UtcNow));
            return Results.Created($"/api/supplier-payments/{result.Data}", new { id = result.Data });
        }

        var status = result.ErrorCode == ErrorCodes.NotFound
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
        return Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: status);
    }).RequireAuthorization("finance-write");

    // Journal / Finance
    // Rentang dimuat per halaman (terbaru dulu, keyset sebelum entri terakhir yang tampil).
    // Laporan laba rugi tetap tepat karena angkanya datang dari /api/journal/summary.
    app.MapGet("/api/journal", async (IJournalRepository repo, DateTime? from, DateTime? to, int? limit,
        DateTime? beforeDate, int? beforeId) =>
        Results.Ok(from.HasValue && to.HasValue
            ? await repo.GetByDateRangePageAsync(from.Value, to.Value, beforeDate, beforeId,
                limit is > 0 and <= 1000 ? limit.Value : 300)
            : await repo.GetRecentAsync(limit is > 0 and <= 1000 ? limit.Value : 200))).RequireAuthorization("finance-read");

    app.MapGet("/api/journal/summary", async (IJournalRepository repo, DateTime from, DateTime to, string? source) =>
        Results.Ok(await repo.GetRangeSummaryAsync(from, to, source))).RequireAuthorization("finance-read");

    app.MapGet("/api/journal/balances", async (IJournalRepository repo, DateTime? asOf) =>
        Results.Ok(await repo.GetBalancesAsync(asOf))).RequireAuthorization("finance-read");

    app.MapPost("/api/expenses", async (ExpenseDto dto, AccountingService acct, HttpContext ctx) =>
    {
        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        if (string.IsNullOrWhiteSpace(dto.Description))
            return Results.BadRequest(new { errorMessage = "Keterangan biaya wajib diisi." });
        if (MoneyRules.AmountError(dto.Amount, "Nominal biaya") is { } amountError)
            return Results.BadRequest(new { errorMessage = amountError });

        // Tanggal ikut ditentukan di server, bukan dipercaya dari browser: layar boleh mengirim
        // kosong (berarti hari bisnis hari ini) dan nilai yang tidak masuk akal ditolak dengan
        // pesan yang menyebut batasnya, bukan diam-diam digeser ke tanggal lain.
        if (!FiscalCalendar.TryExpenseDate(dto.TransactionDate, out var tanggalBiaya, out var galatTanggal))
            return Results.BadRequest(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galatTanggal });

        try
        {
            await acct.PostExpenseAsync(dto.Category, dto.Description, dto.Amount, userId, tanggalBiaya);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { errorMessage = ex.Message }, statusCode: 400);
        }
        return Results.Ok();
    }).RequireAuthorization("finance-write");

    // Periode buku (tutup bulan) — daftar untuk dibaca pemilik keuangan, aksi tutup/buka untuk Owner.
    app.MapGet("/api/periods", async (PeriodCloseService svc) =>
        Results.Ok(await svc.ListAsync())).RequireAuthorization("finance-read");

    app.MapPost("/api/periods/close", async (
        PeriodDto dto, PeriodCloseService svc, HttpContext ctx) =>
    {
        var userId = IdPengguna(ctx);
        var sebelum = (await svc.ListAsync()).FirstOrDefault(p => Bulan(p.StartDate) == Bulan(dto.Month));
        var result = await svc.CloseAsync(dto.Month, userId);
        if (!result.Success)
            return Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: StatusCodes.Status400BadRequest);

        // Ditulis setelah status berubah: jejak harus memuat keadaan yang benar-benar berlaku,
        // dan penutup periode adalah satu dari sedikit aksi yang mengubah hak tulis orang lain.
        // Success selalu membawa ringkasannya; layanan gagal lebih dulu kalau barisnya tidak kembali.
        var sesudah = result.Data!;
        await Jejak.CatatDetilAsync(ctx, "tutup periode", "FiscalPeriod", sesudah.Id,
            new { sebelum?.Status, sebelum?.ClosedAt },
            new { sesudah.Status, sesudah.ClosedAt, Penutup = sesudah.ClosedByName });
        return Results.Ok(sesudah);
    }).RequireAuthorization("administration");

    app.MapPost("/api/periods/reopen", async (
        PeriodDto dto, PeriodCloseService svc, HttpContext ctx) =>
    {
        var userId = IdPengguna(ctx);
        var sebelum = (await svc.ListAsync()).FirstOrDefault(p => Bulan(p.StartDate) == Bulan(dto.Month));
        var result = await svc.ReopenAsync(dto.Month, userId);
        if (!result.Success)
        {
            var status = result.ErrorCode == ErrorCodes.Forbidden
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status400BadRequest;
            return Results.Json(new { result.ErrorCode, result.ErrorMessage }, statusCode: status);
        }

        var sesudah = result.Data!;
        await Jejak.CatatDetilAsync(ctx, "buka periode", "FiscalPeriod", sesudah.Id,
            new { sebelum?.Status, sebelum?.ClosedAt },
            new { sesudah.Status, sesudah.ClosedAt });
        return Results.Ok(sesudah);
    }).RequireAuthorization("administration");

    // Users (Owner only)
    // Dikirim sebagai DTO terbatas: layar hanya butuh identitas dan status akun. Seluruh isi
    // entitas User tidak pernah keluar lewat JSON, sehingga kolom internal baru tidak otomatis
    // menjadi data yang bisa dibaca browser.
    app.MapGet("/api/users", async (IUserRepository repo) =>
    {
        var semua = await repo.GetAllAsync();
        return Results.Ok(semua.Select(u => new
        {
            u.Id,
            u.Username,
            Role = u.Role.ToString(),
            u.IsActive,
            u.MustChangePassword,
            u.LastLoginAt,
            u.LockedUntil,
        }));
    }).RequireAuthorization("administration");

    // Pegawai baru selalu lahir dengan sandi sementara yang harus diganti sendiri saat login
    // berikutnya: pemilik tidak boleh tahu sandi harian kasirnya, dan sandi bawaan yang sama
    // untuk semua akun membuat "siapa yang mengubah ini" tidak bisa dijawab dari jejak audit.
    app.MapPost("/api/users", async (BuatPegawaiDto dto, IUserRepository users, HttpContext ctx) =>
    {
        var nama = (dto.Username ?? string.Empty).Trim();
        if (nama.Length is < 3 or > 32 || !System.Text.RegularExpressions.Regex.IsMatch(nama, @"^[A-Za-z0-9._-]+$"))
            return Results.BadRequest(new { error = "Nama pengguna 3-32 karakter: huruf, angka, titik, garis bawah, atau tanda hubung." });
        if (PasswordPolicy.Error(dto.Password) is { } sandiAwalError)
            return Results.BadRequest(new { error = $"Kata sandi awal tidak memenuhi syarat. {sandiAwalError}" });
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var peran))
            return Results.BadRequest(new { error = "Peran harus salah satu dari: Owner, Admin, Dapur, Pegawai." });
        if (await users.GetByUsernameAsync(nama) is not null)
            return Results.BadRequest(new { error = $"Nama pengguna {nama} sudah dipakai." });

        var id = await users.CreateAsync(new User
        {
            Username = nama,
            PasswordHash = User.HashPassword(dto.Password),
            Role = peran,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow,
        });

        await Jejak.CatatDetilAsync(ctx, "buat pegawai", "User", id,
            null, new { username = nama, peran = peran.ToString(), wajibGantiSandi = true });
        return Results.Created($"/api/users/{id}", new { id, username = nama, peran = peran.ToString() });
    }).RequireAuthorization("administration");

    app.MapPut("/api/users/{id:int}", async (int id, UbahPegawaiDto dto, IUserRepository users, HttpContext ctx) =>
    {
        var target = await users.GetByIdAsync(id);
        if (target is null) return Results.NotFound();
        var penulis = int.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : 0;

        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var peran))
            return Results.BadRequest(new { error = "Peran harus salah satu dari: Owner, Admin, Dapur, Pegawai." });

        var mauNonaktif = dto.IsActive == false;
        var mauTurunkanPeran = target.Role == UserRole.Owner && peran != UserRole.Owner;

        // Owner terakhir yang aktif adalah satu-satunya jalan masuk ke layar pengaturan dan
        // hak akses. Menonaktifkan atau menurunkannya mengunci pemilik dari aplikasinya sendiri,
        // dan tidak ada recovery offline untuk keadaan itu.
        if (mauNonaktif || mauTurunkanPeran)
        {
            var pemilikAktif = (await users.GetAllAsync())
                .Count(u => u.Role == UserRole.Owner && u.IsActive && u.Id != id);
            if (pemilikAktif == 0)
                return Results.BadRequest(new {
                    error = mauTurunkanPeran
                        ? "Owner terakhir tidak boleh diturunkan perannya; angkat Owner lain lebih dulu."
                        : "Owner terakhir tidak boleh dinonaktifkan; angkat Owner lain lebih dulu."
                });
        }
        if (mauNonaktif && penulis == id)
            return Results.BadRequest(new { error = "Anda tidak dapat menonaktifkan akun sendiri." });

        var lama = new { peran = target.Role.ToString(), aktif = target.IsActive, wajibGantiSandi = target.MustChangePassword };
        target.Role = peran;
        target.IsActive = dto.IsActive ?? target.IsActive;
        // Sandi baru dari layar hak akses selalu bersifat sementara: pemilik memberi akses,
        // pegawai menggantinya sendiri sebelum bisa memakai aplikasinya.
        if (!string.IsNullOrEmpty(dto.PasswordSementara))
        {
            if (PasswordPolicy.Error(dto.PasswordSementara) is { } sandiSementaraError)
                return Results.BadRequest(new { error = $"Kata sandi sementara tidak memenuhi syarat. {sandiSementaraError}" });
            target.PasswordHash = User.HashPassword(dto.PasswordSementara);
            target.MustChangePassword = true;
            target.FailedLoginCount = 0;
            target.LockedUntil = null;
            // Reset sandi oleh pemilik juga memutus sesi lama akun itu.
            target.TokenVersion++;
        }
        await users.UpdateAsync(target);

        await Jejak.CatatDetilAsync(ctx, "ubah hak akses pegawai", "User", id,
            lama, new { peran = target.Role.ToString(), aktif = target.IsActive, wajibGantiSandi = target.MustChangePassword });
        return Results.Ok(new { id = target.Id, username = target.Username, peran = target.Role.ToString(), aktif = target.IsActive });
    }).RequireAuthorization("administration");

    // Periksa-diri basis data (Owner only). Angka "sehat" harus berasal dari SQLite, bukan dari
    // perasaan aplikasi, supaya pemilik bisa melihat sendiri apa yang rusak sebelum menelepon.
    app.MapGet("/api/system/self-check", (DatabaseIntegrity integrity) =>
    {
        var checks = integrity.Check();
        var gagal = checks.Count(c => !c.Passed);
        return Results.Ok(new
        {
            status = gagal == 0 ? "ok" : "perhatian",
            jumlahPemeriksaan = checks.Count,
            jumlahGagal = gagal,
            databasePath = dbPath,
            diperiksaPada = DateTime.UtcNow.ToString("o"),
            checks,
        });
    }).RequireAuthorization("administration");

    // Cadangan: daftar, buat sekarang, dan salin ke luar komputer. Pemulihan tidak lewat
    // aplikasi yang sedang berjalan — berkas basis data sedang dibuka oleh host ini, jadi
    // pemulihan dijalankan saat aplikasi tertutup lewat Seruni22Bakery.exe --restore,
    // yang meneruskan perintah itu ke host sekali pakai.
    app.MapGet("/api/backup", (BackupCoordinator cadangan) => Results.Ok(new
    {
        folder = cadangan.BackupDirectory,
        item = cadangan.Daftar().Select(e => new
        {
            berkas = e.FileName,
            ukuranByte = e.SizeBytes,
            dibuatUtc = e.CreatedAtUtc,
            jenis = e.Kind,
            label = e.Label,
            skema = e.SchemaVersion,
            punyaManifes = e.PunyaManifes,
            terverifikasi = e.Terverifikasi,
        })
    })).RequireAuthorization("administration");

    app.MapPost("/api/backup", async (BackupCoordinator cadangan, HttpContext ctx) =>
    {
        var manifes = await cadangan.BuatSekarangAsync("manual");
        await Jejak.CatatDetilAsync(ctx, "buat cadangan", "Backup", null, null, new
        {
            manifes.File, manifes.SizeBytes, manifes.Sha256,
            manifes.SchemaVersion, manifes.Orders, manifes.JournalEntries
        });
        return Results.Ok(manifes);
    }).RequireAuthorization("administration");

    app.MapPost("/api/backup/copy", async (SalinCadanganDto dto, BackupCoordinator cadangan, HttpContext ctx) =>
    {
        try
        {
            var (folder, berkas, sha) = await cadangan.SalinKeluarAsync(dto.Tujuan);
            await Jejak.CatatDetilAsync(ctx, "salin cadangan ke luar komputer", "Backup", null, null,
                new { folder, berkas, sha256 = sha });
            return Results.Ok(new { folder, berkas, sha256 = sha });
        }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
    }).RequireAuthorization("administration");

    app.MapGet("/api/system/storage", async (BackupCoordinator cadangan, CancellationToken ct) =>
        Results.Ok(await cadangan.PotretAsync(ct))).RequireAuthorization("administration");

    // Settings
    // Merek toko dibaca layar masuk sebelum ada sesi, jadi endpoint ini sengaja anonim. Isinya
    // dibatasi tiga kunci merek — bukan seluruh AppSettings — supaya pengaturan lain (printer,
    // biaya, dsb.) tetap tertutup tanpa login.
    app.MapGet("/api/brand", async (ISettingRepository repo) =>
    {
        var semua = await repo.GetAllAsync();
        semua.TryGetValue(StoreBrand.NameKey, out var nama);
        semua.TryGetValue(StoreBrand.TaglineKey, out var tagline);
        semua.TryGetValue(StoreBrand.LogoKey, out var logo);
        semua.TryGetValue("outlet_name", out var outlet);
        var logoSah = !string.IsNullOrWhiteSpace(logo)
                      && StoreBrand.Validate(StoreBrand.LogoKey, logo) is null;
        return Results.Ok(new
        {
            name = StoreBrand.Name(nama),
            tagline = StoreBrand.Tagline(tagline),
            logo = logoSah ? logo : null,
            // Nama outlet tampil di kepala layar untuk semua peran, jadi ikut di sini — bukan
            // alasan membuka seluruh AppSettings lewat /api/settings.
            outlet = string.IsNullOrWhiteSpace(outlet) ? null : outlet,
        });
    }).AllowAnonymous();

    // Seluruh pengaturan (termasuk kunci printer, biaya, PPN, dan nama outlet) hanya untuk Owner.
    // Peran lain cukup merek toko dari /api/brand. Daftar isinya datang apa adanya dari AppSettings,
    // jadi kunci PPN (tax_enabled, tax_rate_percent, tax_inclusive) ikut terbaca di sini begitu
    // pemilik menyimpannya — layar pengaturan tidak perlu jalan pintas lewat /api/brand.
    app.MapGet("/api/settings", async (ISettingRepository repo) =>
        Results.Ok(await repo.GetAllAsync())).RequireAuthorization("administration");

    app.MapPut("/api/settings", async (Dictionary<string, string> settings, ISettingRepository repo, HttpContext ctx) =>
    {
        // Diperiksa semua lebih dulu: satu nilai merek yang salah tidak boleh menyisakan
        // separuh pengaturan lain sudah tersimpan.
        foreach (var (k, v) in settings)
        {
            if (StoreBrand.Validate(k, v) is { } galat)
                return Results.Json(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galat },
                    statusCode: 400);
            // Nilai PPN dibaca ulang setiap pesanan dibuat dan ikut menghitung jurnal, jadi tarif
            // yang tidak sah harus berhenti di sini — bukan tersimpan lalu menghasilkan pajak salah
            // di seluruh transaksi berikutnya.
            if (TaxSettings.Validate(k, v) is { } galatPajak)
                return Results.Json(new { errorCode = ErrorCodes.ValidationFailed, errorMessage = galatPajak },
                    statusCode: 400);
        }

        var userId = int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var lama = await repo.GetAllAsync();
        var perubahan = new List<(string Kunci, string? Lama, string Baru)>();
        foreach (var (k, v) in settings)
        {
            lama.TryGetValue(k, out var nilaiLama);
            if (!string.Equals(nilaiLama, v, StringComparison.Ordinal))
            {
                // Jejak audit tidak boleh memuat gambar logo utuh: satu unggahan 512 KB akan
                // menggandakan basis data dan membuat layar audit berat.
                var ringkas = k == StoreBrand.LogoKey;
                perubahan.Add((k,
                    ringkas ? (string.IsNullOrWhiteSpace(nilaiLama) ? null : "<logo terpasang>") : nilaiLama,
                    ringkas ? (string.IsNullOrWhiteSpace(v) ? "<logo dihapus>" : "<logo terpasang>") : v));
            }
        }

        // Semua kunci ditulis dalam satu transaksi; jejak audit ditulis setelah commit supaya
        // tidak ada jejak untuk nilai yang ternyata gagal tersimpan.
        await repo.SaveManyAsync(settings, userId);

        // Kunci 'timezone' menentukan hitungan "hari ini" di seluruh aplikasi, jadi nilai barunya
        // langsung dipasang — dan kalau mesin tidak mengenalinya, pemilik diberi tahu bahwa
        // perhitungannya belum berubah alih-alih disimpan diam-diam.
        string? peringatan = null;
        if (settings.TryGetValue("timezone", out var zona))
            peringatan = BusinessTime.KonfigurasikanZona(zona);

        await Jejak.CatatDetilAsync(ctx, "ubah pengaturan", "AppSetting", null,
            perubahan.Select(p => new { kunci = p.Kunci, nilai = p.Lama }).ToList(),
            perubahan.Select(p => new { kunci = p.Kunci, nilai = p.Baru }).ToList());

        return Results.Ok(peringatan is null ? new { diterapkan = perubahan.Count } : new { diterapkan = perubahan.Count, peringatan });
    }).RequireAuthorization("administration");

    // ── Perangkat kasir: printer Windows & ESC/POS langsung ke port 9100 ───────────
    // Layar hanya bisa membaca keadaan perangkat, menyimpan pilihan, mencetak uji, dan meminta
    // struk. Tidak ada endpoint yang menerima perintah shell, jalur driver, atau byte mentah:
    // satu-satunya jalan ke printer adalah adaptor yang terdaftar di server.
    app.MapGet("/api/devices", async (PosDeviceService devices) =>
        Results.Ok(await devices.GetSnapshotAsync())).RequireAuthorization("administration");

    // Ringkasan untuk layar kasir: cukup tahu printer mana yang dipakai dan apakah struk
    // dicetak otomatis. Daftar lengkap printer dan riwayat pekerjaan tetap milik Owner.
    app.MapGet("/api/devices/status", async (PosDeviceService devices) =>
    {
        var snapshot = await devices.GetSnapshotAsync();
        return Results.Ok(new
        {
            printerConfigured = snapshot.PerangkatDidukung
                                && !string.IsNullOrWhiteSpace(snapshot.Settings.NamaTujuan),
            printerName = snapshot.Settings.NamaTujuan,
            mode = snapshot.Mode,
            paperWidthMm = snapshot.Settings.PaperWidthMm,
            autoPrint = snapshot.Settings.AutoPrint,
            catatan = snapshot.Catatan
        });
    }).RequireAuthorization("sales");

    app.MapPut("/api/devices/settings", async (
        Dictionary<string, string> nilai, PosDeviceService devices, ISettingRepository settings, HttpContext ctx) =>
    {
        var sebelum = await settings.GetAllAsync();
        var hasil = await devices.SimpanPengaturanAsync(nilai, IdPengguna(ctx));
        if (!hasil.Success) return HasilPerangkat(hasil.ErrorCode!, hasil.ErrorMessage!);

        await Jejak.CatatDetilAsync(ctx, "ubah pengaturan perangkat kasir", "AppSetting", null,
            nilai.Keys.Select(k => new { kunci = k, nilai = sebelum.TryGetValue(k, out var lama) ? lama : null }).ToList(),
            nilai.Select(p => new { kunci = p.Key, nilai = p.Value }).ToList());
        return Results.Ok(hasil.Data);
    }).RequireAuthorization("administration");

    app.MapPost("/api/devices/test-print", async (PosDeviceService devices, HttpContext ctx) =>
    {
        var hasil = await devices.CetakUjiAsync(IdPengguna(ctx));
        return hasil.Berhasil
            ? Results.Ok(new { jobId = hasil.JobId, pesan = hasil.Pesan })
            : HasilPerangkat(hasil.ErrorCode!, hasil.Pesan!, hasil.JobId);
    }).RequireAuthorization("administration");

    // Status dan jenis dikirim sebagai teks: enum C# akan terserialisasi sebagai angka dan
    // layar akan menampilkan "status 1" tanpa arti. Isi struk (Payload) tidak ikut dikirim;
    // riwayat cukup menunjukkan apa yang terjadi, bukan menyalin data pelanggan.
    app.MapGet("/api/devices/print-jobs", async (int? limit, PosDeviceService devices) =>
    {
        var jobs = await devices.DaftarJobAsync(limit ?? 50);
        return Results.Ok(jobs.Select(j => new
        {
            j.Id,
            j.OrderId,
            kind = j.Kind.ToString(),
            status = j.Status.ToString(),
            j.PrinterName,
            j.PaperWidthMm,
            j.Attempts,
            j.LastError,
            j.CreatedAt,
            j.FinishedAt,
        }));
    }).RequireAuthorization("administration");

    // Struk dicetak dari pesanan yang dibaca ulang dari basis data. Kasir hanya boleh mencetak
    // pesanan yang ia catat sendiri, sama seperti aturan baca daftar pesanan.
    app.MapPost("/api/orders/{id:int}/receipt", async (
        int id, CetakStrukDto? dto, ISalesOrderRepository repo, PosDeviceService devices, HttpContext ctx) =>
    {
        var order = await repo.GetByIdAsync(id);
        if (order is null) return Results.NotFound();

        var userId = IdPengguna(ctx);
        var roleText = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (Enum.TryParse<UserRole>(roleText, out var role) && role == UserRole.Pegawai
            && order.CreatedByUserId != userId)
            return Results.NotFound();

        var hasil = await devices.CetakStrukAsync(order, dto?.CetakUlang ?? false, userId);
        return hasil.Berhasil
            ? Results.Ok(new { jobId = hasil.JobId, pesan = hasil.Pesan })
            : HasilPerangkat(hasil.ErrorCode!, hasil.Pesan!, hasil.JobId);
    }).RequireAuthorization("sales");

    // ── Jejak audit (Owner only) ────────────────────────────────────────────────
    // Siapa mengubah apa, kapan, dan dari sesi mana. Entri ditulis oleh middleware Jejak untuk
    // setiap aksi tulis yang berhasil, jadi daftar ini tidak pernah "kosok karena lupa mencatat".
    app.MapGet("/api/audit", async (
        IAuditLogRepository audit, string? entity, int? entityId, int? userId,
        DateTime? dari, DateTime? sampai, int? limit) =>
    {
        var aman = limit is > 0 and <= 1000 ? limit.Value : 100;
        return Results.Ok(await audit.SearchAsync(new AuditFilter(
            entity, entityId, userId, dari, sampai, aman)));
    }).RequireAuthorization("administration");

    // ── Lot bahan & kedaluwarsa ─────────────────────────────────────────────────
    app.MapGet("/api/inventory/batches", async (string? status, int? limit, IBatchRepository repo) =>
    {
        var statuses = new[] { "aman", "segera", "kedaluwarsa" };
        if (status is not null && !statuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = $"Filter status harus salah satu dari: {string.Join(", ", statuses)}." });
        var aman = limit is > 0 and <= 1000 ? limit.Value : 200;
        return Results.Ok(await repo.GetAsync(status?.ToLowerInvariant(), aman));
    }).RequireAuthorization("inventory-read");

    // Mencatat lot di luar struk penerimaan (mis. bahan yang dibeli tunai lalu dikemas sendiri).
    app.MapPost("/api/inventory/batches", async (CatatLotDto dto, IBatchRepository batches, IIngredientRepository ingredients) =>
    {
        var bahan = await ingredients.GetByIdAsync(dto.IngredientId);
        if (bahan is null)
            return Results.BadRequest(new { error = $"Bahan Id {dto.IngredientId} tidak ditemukan." });
        if (string.IsNullOrWhiteSpace(dto.BatchNumber))
            return Results.BadRequest(new { error = "Nomor lot wajib diisi." });
        if (dto.BatchNumber.Trim().Length > 60)
            return Results.BadRequest(new { error = "Nomor lot terlalu panjang (maks 60 karakter)." });
        if (!DateTime.TryParse(dto.ExpiryDate, out var kedaluwarsa))
            return Results.BadRequest(new { error = "Tanggal kedaluwarsa tidak valid, gunakan format YYYY-MM-DD." });
        if (!DateTime.TryParse(dto.ReceivedDate, out var diterima))
            diterima = BusinessTime.Today();
        if (kedaluwarsa.Date <= diterima.Date)
            return Results.BadRequest(new { error = "Tanggal kedaluwarsa harus setelah tanggal terima." });
        if (MoneyRules.CostError(dto.UnitCostMinorUnits, "Harga per satuan") is { } galatHarga)
            return Results.BadRequest(new { error = galatHarga });

        var id = await batches.CreateAsync(new NewBatch(
            dto.IngredientId, dto.BatchNumber.Trim(), diterima.Date, kedaluwarsa.Date, dto.UnitCostMinorUnits, null));
        return Results.Created($"/api/inventory/batches/{id}", new { id });
    }).RequireAuthorization("inventory-write");

    // ── Export PDF / Excel ─────────────────────────────────────────────────
    app.MapGet("/api/export/journal", async (DateTime? from, DateTime? to, string? format, ExportService exp) =>
    {
        var f = from ?? BusinessTime.Today().AddDays(-30);
        var t = to ?? BusinessTime.Today().AddDays(1);
        var fmt = string.IsNullOrEmpty(format) ? "excel" : format;
        var (data, ct, fn) = await exp.ExportJournalAsync(f, t, fmt);
        return Results.File(data, ct, fn);
    }).RequireAuthorization("finance-read");

    app.MapGet("/api/export/orders", async (DateTime? from, DateTime? to, string? format, ExportService exp) =>
    {
        var f = from ?? BusinessTime.Today().AddDays(-30);
        var t = to ?? BusinessTime.Today().AddDays(1);
        var fmt = string.IsNullOrEmpty(format) ? "excel" : format;
        var (data, ct, fn) = await exp.ExportOrdersAsync(f, t, fmt);
        return Results.File(data, ct, fn);
    }).RequireAuthorization("sales-export");

    app.MapGet("/api/export/stock", async (string? format, ExportService exp) =>
    {
        var fmt = string.IsNullOrEmpty(format) ? "excel" : format;
        var (data, ct, fn) = await exp.ExportStockAsync(fmt);
        return Results.File(data, ct, fn);
    }).RequireAuthorization("inventory-read");

    app.MapGet("/api/export/purchases", async (string? format, ExportService exp) =>
    {
        var fmt = string.IsNullOrEmpty(format) ? "excel" : format;
        var (data, ct, fn) = await exp.ExportPurchasesAsync(fmt);
        return Results.File(data, ct, fn);
    }).RequireAuthorization("finance-read");

    // Fallback SPA di bawahnya menangkap SEMUA URL, termasuk /api yang salah ketik. Tanpa penangkap
    // ini, satu typo nama endpoint membalas HTML aplikasi dengan status 200: layar menampilkan
    // "data kosong" dan bugnya hilang tanpa jejak. Tetap butuh login supaya penebak jalur tidak
    // mendapat jawaban yang bisa dibedakan dari endpoint sungguhan.
    app.Map("/api/{*jalur}", (HttpContext ctx, string jalur) =>
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return ctx.Response.WriteAsJsonAsync(new
        {
            errorCode = ErrorCodes.NotFound,
            errorMessage = $"Endpoint /api/{jalur} tidak dikenal untuk metode {ctx.Request.Method}."
        });
    }).RequireAuthorization();

    if (Directory.Exists(desktopPath))
    {
        var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(desktopPath);
        // Kerangka aplikasi (HTML + bundel) memang publik; isinya baru terisi setelah login.
        // Tanpa AllowAnonymous, kebijakan cadangan akan menolak layar masuk itu sendiri.
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fileProvider })
            .AllowAnonymous();
    }

    Log.Information("Seruni 22 Bakery service starting on {Urls}", string.Join(", ", app.Urls));
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Seruni 22 Bakery service failed to start");
    // Launcher menunggu /health; kode keluar ini juga membuat skrip diagnosa bisa membedakan
    // start yang gagal dari start yang berhasil tanpa membaca isi log.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Berkas jwt.key berisi kunci penanda token: aturan lama, baik hasil warisan folder maupun
// yang ditulis eksplisit, harus hilang. Kalau tidak, siapa pun yang bisa masuk ke mesin ini
// bisa membaca kunci dan mencetak token Owner sendiri.
static void RestrictToCurrentUser(string path)
{
    if (OperatingSystem.IsWindows())
    {
        var info = new FileInfo(path);
        var acl = new System.Security.AccessControl.FileSecurity();   // ACL baru: tanpa aturan lama
        acl.SetAccessRuleProtection(true, false);                     // dan tanpa warisan folder
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            identity.User!,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.InheritanceFlags.None,
            System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Allow));
        info.SetAccessControl(acl);
        return;
    }

    File.SetUnixFileMode(path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite);
}

// Izin ketat adalah penguatan, bukan syarat berjalan: kalau sistem berkas menolaknya,
// aplikasi tetap jalan tetapi operator harus tahu kuncinya masih longgar.
static void RestrictSecretFile(string path)
{
    try
    {
        RestrictToCurrentUser(path);
    }
    catch (Exception ex)
    {
        Log.Warning("Kunci JWT di {Path} tersimpan tapi izin ketatnya gagal diterapkan: {Error}", path, ex.Message);
    }
}

// Barcode disimpan tanpa spasi di ujung dan kosong berarti "tidak berlabel". Pemindai bisa
// mengirim spasi/enter sebagai akhiran; nilai itu tidak boleh menjadi bagian kode produk.
static string? NormalisasiBarcode(string? barcode)
{
    var bersih = barcode?.Trim();
    return string.IsNullOrEmpty(bersih) ? null : bersih;
}

static int IdPengguna(HttpContext ctx) =>
    int.Parse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

// Kunci bulan (yyyy-MM) dari teks tanggal periode. Perbandingan dilakukan pada kunci ini, bukan
// pada tanggal penuh, karena layar memilih bulan dan tidak pernah tahu tanggal berapa bulannya
// mulai di basis data.
static string Bulan(string? tanggal) =>
    string.IsNullOrWhiteSpace(tanggal) || tanggal.Trim().Length < 7 ? string.Empty : tanggal.Trim()[..7];

// Produk adalah data induk: nama kosong atau baris resep tak sah merusak katalog dan HPP tanpa
// pesan. Periksa sebelum menyentuh basis data supaya jawabannya 400, bukan 500 dari referensi null.
static string? GalatProduk(CreateProductDto dto)
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return "Nama produk wajib diisi.";
    if (dto.SellingPriceMinorUnits < 0) return "Harga jual tidak boleh negatif.";
    if (dto.Lines is null) return "Daftar bahan resep wajib diisi (boleh kosong).";
    if (dto.Lines.Any(l => l.IngredientId <= 0)) return "Baris resep harus menunjuk bahan baku yang sah.";
    if (dto.Lines.Any(l => l.Quantity <= 0)) return "Jumlah bahan pada resep harus lebih dari 0.";
    return null;
}

// Bahan baku adalah data induk yang dipakai resep, pembelian, dan opname. Nilai yang tidak sah
// harus ditolak dengan 400 di sini: pemicu basis data juga menolaknya, tetapi jawabannya 500 dan
// pengguna tidak tahu kolom mana yang salah.
static string? GalatBahan(IngredientDto dto)
{
    if (string.IsNullOrWhiteSpace(dto.Name)) return "Nama bahan baku wajib diisi.";
    if (dto.Name.Trim().Length > 120) return "Nama bahan baku terlalu panjang (maksimal 120 karakter).";
    if (string.IsNullOrWhiteSpace(dto.Unit)) return "Satuan bahan baku wajib diisi (mis. kg, liter, pcs).";
    if (dto.Unit.Trim().Length > 20) return "Satuan bahan baku terlalu panjang (maksimal 20 karakter).";
    if (dto.PricePerUnit is < 0) return "Harga per satuan tidak boleh negatif.";
    if (dto.MinStockAlert is < 0) return "Batas minimum stok tidak boleh negatif.";
    if (dto.CurrentStock is < 0) return "Stok tidak boleh negatif.";
    if (dto.Units is { } satuan)
    {
        // Satuan turunan hanya menerjemahkan jumlah di resep ke satuan stok. Duplikat dan
        // satuan yang sama dengan satuan stok membuat faktor mana yang dipakai tidak jelas.
        var dasar = dto.Unit!.Trim();
        var terlihat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in satuan)
        {
            if (string.IsNullOrWhiteSpace(u.Unit))
                return "Nama satuan tambahan wajib diisi (mis. g, bks).";
            var nama = u.Unit.Trim();
            if (nama.Length > 20)
                return $"Nama satuan tambahan \"{nama}\" terlalu panjang (maksimal 20 karakter).";
            if (string.Equals(nama, dasar, StringComparison.OrdinalIgnoreCase))
                return $"Satuan tambahan \"{nama}\" sama dengan satuan stok bahan; pakai nama yang berbeda.";
            if (!terlihat.Add(nama))
                return $"Satuan tambahan \"{nama}\" ditulis lebih dari sekali.";
            if (u.FactorToBase is null or <= 0)
                return $"Faktor satuan \"{nama}\" harus lebih dari nol (mis. 1 g = 0,001 kg).";
        }
    }
    return null;
}

// Satuan baris resep harus terdaftar pada bahan itu (satuan stok atau satuan turunannya). Tanpa
// pemeriksaan ini salah ketik "grm" akan tersimpan dan dinilai dengan faktor 1 seperti kilogram.
static async Task<string?> GalatSatuanResepAsync(
    IEnumerable<RecipeLineDto> lines, IIngredientRepository bahan)
{
    var perId = new Dictionary<int, Ingredient?>();
    foreach (var l in lines)
    {
        if (string.IsNullOrWhiteSpace(l.Unit)) continue;
        if (!perId.TryGetValue(l.IngredientId, out var i))
            perId[l.IngredientId] = i = await bahan.GetByIdAsync(l.IngredientId);
        if (i is null)
            return $"Baris resep memakai bahan #{l.IngredientId} yang tidak aktif atau tidak ada.";
        if (!i.KnowsUnit(l.Unit))
            return $"Satuan \"{l.Unit.Trim()}\" belum terdaftar untuk bahan \"{i.Name}\" " +
                   $"(satuan stok {i.Unit}). Tambahkan satuan itu di master bahan lebih dulu.";
    }
    return null;
}

// Pesan pemicu basis data dibungkus "SQLite Error 19: '…'". Yang berguna bagi pengguna adalah
// isi kutipnya; jawaban 500 dengan seluruh pesan SQL tidak menjelaskan kolom mana yang salah.
static string PesanPemicu(Microsoft.Data.Sqlite.SqliteException ex)
{
    var pesan = ex.Message;
    var awal = pesan.IndexOf('\'');
    var akhir = pesan.LastIndexOf('\'');
    return awal >= 0 && akhir > awal ? pesan[(awal + 1)..akhir] : pesan;
}

// Ringkasan satuan turunan untuk jejak audit, mis. "g=0,001 kg, bks=0,5 kg".
static string? SebutSatuanTambahan(Ingredient bahan) =>
    bahan.AdditionalUnits.Count == 0
        ? null
        : string.Join(", ", bahan.AdditionalUnits.Select(u => $"{u.Unit}={u.FactorToBase:0.####} {bahan.Unit}"));

// Dua bahan bernama sama tidak bisa dibedakan di layar resep maupun di laporan pembelian.
static IResult BahanKembar() => Results.Conflict(new
{
    errorCode = ErrorCodes.DuplicateKey,
    errorMessage = "Nama bahan baku itu sudah dipakai bahan lain. Pakai nama yang membedakan."
});

// Cakupan pesanan per kasir dipakai oleh daftar, detail, pembayaran, pengambilan, dan ubah status.
// Pesanan kasir lain dijawab 404 supaya keberadaannya pun tidak terbaca dari akun ini.
static async Task<bool> PesananDiLuarCakupanAsync(
    int orderId, ISalesOrderRepository orders, HttpContext ctx)
{
    var roleText = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    if (!Enum.TryParse<UserRole>(roleText, out var role) || role != UserRole.Pegawai)
        return false;
    if (!int.TryParse(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
        return true;
    var order = await orders.GetByIdAsync(orderId);
    return order is null || order.CreatedByUserId != userId;
}

// Peran pemanggil dari klaim yang sudah divalidasi ulang per permintaan. Dipakai endpoint yang
// bentuk jawabannya berbeda per peran (katalog vs resep, cakupan pelanggan).
static UserRole? PeranPemanggil(HttpContext ctx)
{
    var roleText = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
    return Enum.TryParse<UserRole>(roleText, out var role) ? role : null;
}

// Resep dan harga bahan adalah rahasia dapur: kasir hanya perlu produk jadi dan harga jualnya.
static bool BolehBacaResep(HttpContext ctx)
    => PeranPemanggil(ctx) is { } role && RolePolicies.Allows(RolePolicies.InventoryRead, role);

// Notifikasi internal ditulis setelah transaksi tersimpan. Kalau penulisannya gagal, transaksi
// tetap sah dan galatnya masuk log — pengguna tidak boleh melihat "gagal" padahal datanya ada.
static async Task CatatNotifikasiAmanAsync(NotificationService notifikasi, Func<Task> tulis)
{
    try { await tulis(); }
    catch (Exception ex)
    {
        Log.Warning(ex, "Notifikasi internal gagal ditulis; transaksi utama tidak dibatalkan.");
    }
}

// Jawaban galat perangkat memakai bentuk yang sama dengan endpoint lain ({ errorCode, errorMessage })
// supaya layar bisa menampilkan pesan tanpa menebak arti kode status.
static IResult HasilPerangkat(string kode, string pesan, int? jobId = null)
{
    var isi = new { errorCode = kode, errorMessage = pesan, jobId };
    return kode == ErrorCodes.ValidationFailed
        ? Results.BadRequest(isi)
        : Results.Json(isi, statusCode: StatusCodes.Status409Conflict);
}

// ── DTOs ───────────────────────────────────────────────────────────────────────
// Semua field opsional: nilainya null berarti "tidak dikirim". Endpoint ubah memakai arti itu
// untuk mempertahankan nilai lama, bukan menimpanya dengan nol.
record IngredientUnitDto(string? Unit, decimal? FactorToBase);
record IngredientDto(string? Name, string? Brand, string? Unit, long? PricePerUnit,
    decimal? CurrentStock, decimal? MinStockAlert, List<IngredientUnitDto>? Units = null);
record ChangePasswordDto(string CurrentPassword, string NewPassword);
// TenderAmount adalah uang yang diserahkan pelanggan. Boleh tidak dikirim — kalau layar belum
// mengenalnya, pembayaran dianggap pas dan tidak ada kembalian yang dilaporkan.
record AddPaymentDto(long Amount, string Method, string? Notes, string? IdempotencyKey, long? TenderAmount = null);
record UpdateStatusDto(string Status, string? CancelReason);
record ExpenseDto(string Category, string Description, long Amount, string? TransactionDate = null);
// Satu DTO untuk tutup dan buka kembali: isinya hanya bulan yang dituju, supaya layar tidak bisa
// mengirim status sendiri dan memilih sendiri bulan mana yang boleh dibuka.
record PeriodDto(string? Month);
record CreateWorkOrderDto(int ProductTemplateId, int TargetQuantity, long LaborCost, long OverheadCost);
record CompleteWorkOrderDto(int ActualGoodQty, int ActualFailedQty, List<WorkOrderMaterialDto> Materials, List<WorkOrderScrapDto> Scraps);
record WorkOrderMaterialDto(int IngredientId, decimal ActualQty, long UnitCost);
record WorkOrderScrapDto(int Quantity, string ReasonCategory, string ActionTaken, string? Notes);
record TransferStockDto(int FromLocationId, int ToLocationId, int IngredientId, decimal Quantity, string? Notes);
record AdjustStockDto(int LocationId, int IngredientId, decimal NewQuantity, string? Reason);
record CancelWorkOrderDto(string? Reason);
record CreateQualityCheckDto(
    int WorkOrderId, int SampleQuantity, int PassedQuantity, int FailedQuantity,
    string? AppearanceNotes, string? TasteNotes, string? Notes);
record CreateSalesReturnDto(
    int SalesOrderId, decimal Quantity, long Amount, string Reason,
    string? RefundMethod, string? IdempotencyKey);
record RecipeLineDto(int IngredientId, decimal Quantity, string? Unit = null);
record CreateProductDto(string Name, string? Category, long SellingPriceMinorUnits, List<RecipeLineDto> Lines, string? Barcode = null);
record CetakStrukDto(bool? CetakUlang);

// ── Purchase DTOs ─────────────────────────────────────────────────────────────
record CreateSupplierDto(string Name, string? ContactName, string? Phone, string? Email, string? Address, string? Notes);
record CreatePurchaseOrderDto(int SupplierId, string? ExpectedDate, string? Notes, List<CreatePurchaseLineDto> Lines);
record CreatePurchaseLineDto(int IngredientId, decimal OrderedQty, long UnitCostMinorUnits, string Unit);
record ReceiveGoodsRequestDto(string? Notes, List<ReceiveLineRequestDto> Lines);
record ReceiveLineRequestDto(int PurchaseOrderLineId, int IngredientId, decimal ReceivedQty, long UnitCostMinorUnits, int LocationId, string? BatchNumber, string? ExpiryDate);
record CatatLotDto(int IngredientId, string BatchNumber, string? ReceivedDate, string ExpiryDate, long UnitCostMinorUnits);
record CreateSupplierReturnDto(
    int GoodsReceiptId, string Reason, string? Notes,
    List<SupplierReturnLineRequestDto>? Lines, string? IdempotencyKey);
record SupplierReturnLineRequestDto(int GoodsReceiptLineId, decimal Quantity, int? LocationId);
record CreateSupplierPaymentDto(int GoodsReceiptId, long Amount, string? Method, string? Notes, string? IdempotencyKey);
record SalinCadanganDto(string Tujuan);
record BuatPegawaiDto(string Username, string Password, string Role);
record UbahPegawaiDto(string Role, bool? IsActive, string? PasswordSementara);
