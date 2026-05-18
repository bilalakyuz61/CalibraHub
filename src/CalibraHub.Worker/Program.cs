using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Configuration;
using CalibraHub.Application.Services;
using CalibraHub.Application.Services.Integration;
using CalibraHub.Infrastructure.Integrations;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Repositories;
using CalibraHub.Worker;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Servis modunda (LocalSystem) cwd = System32 — exe konumuna setle
System.IO.Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// Yakalanmamis exception'lari Event Viewer'a yaz
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        var ex = e.ExceptionObject as Exception;
        if (OperatingSystem.IsWindows())
        {
            if (!System.Diagnostics.EventLog.SourceExists("CalibraHub Worker"))
                System.Diagnostics.EventLog.CreateEventSource("CalibraHub Worker", "Application");
            using var log = new System.Diagnostics.EventLog("Application") { Source = "CalibraHub Worker" };
            log.WriteEntry(
                $"FATAL UnhandledException: {ex}\r\n\r\nIsTerminating: {e.IsTerminating}\r\nCWD: {Environment.CurrentDirectory}",
                System.Diagnostics.EventLogEntryType.Error);
        }
    }
    catch { }
};

var host = Host.CreateDefaultBuilder(args)
    // ContentRoot'u binary dizinine cek — `dotnet run --project X` ile calistirildiginda
    // cwd repo root kaliyor ve appsettings.json bulunamiyor. BaseDirectory bin/Debug/netX/
    // olur; appsettings.json oraya kopyalanirsa (csproj'da None Update) dogru okunur.
    .UseContentRoot(AppContext.BaseDirectory)
    .UseWindowsService(options =>
    {
        options.ServiceName = "CalibraHub Worker";
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        var environment = context.HostingEnvironment;

        var simulatedDocumentsPerPull = configuration.GetValue<int>("Integrator:SimulatedDocumentsPerPull", 2);
        var useMockIntegratorClient = configuration.GetValue<bool?>("Integrator:UseMockClient") ?? false;
        if (!environment.IsDevelopment())
        {
            useMockIntegratorClient = false;
        }

        var rawCs = configuration[$"{CalibraDatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty;
        var decryptedCs = DecryptIfNeeded(rawCs);
        // Debug: Console'a kisaltarak yazdir (sifre icermez cunku DPAPI oncesi raw veya kullanici plain)
        Console.WriteLine($"[WORKER BOOT] CalibraDatabase:ConnectionString raw-length={rawCs.Length}, decrypted-length={decryptedCs.Length}");
        if (decryptedCs.Length > 0)
        {
            try
            {
                var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(decryptedCs);
                Console.WriteLine($"[WORKER BOOT] DataSource={b.DataSource}, InitialCatalog={b.InitialCatalog}");
            }
            catch (Exception e) { Console.WriteLine($"[WORKER BOOT] CS parse hatasi: {e.Message}"); }
        }

        var databaseOptions = new CalibraDatabaseOptions
        {
            ConnectionString = decryptedCs,
            Schema = configuration[$"{CalibraDatabaseOptions.SectionName}:Schema"] ?? "dbo",
            AutoCreateDatabaseOnStartup = configuration.GetValue<bool?>($"{CalibraDatabaseOptions.SectionName}:AutoCreateDatabaseOnStartup") ?? true
        };

        var bootstrapAdminOptions = new BootstrapAdminOptions
        {
            SeedOnStartup = configuration.GetValue<bool?>($"{BootstrapAdminOptions.SectionName}:SeedOnStartup") ?? true,
            FullName = configuration[$"{BootstrapAdminOptions.SectionName}:FullName"] ?? "Sistem Admin",
            Email = configuration[$"{BootstrapAdminOptions.SectionName}:Email"] ?? "admin@calibra.local",
            EmployeeCode = configuration[$"{BootstrapAdminOptions.SectionName}:EmployeeCode"] ?? "ADM-001",
            DefaultPassword = configuration[$"{BootstrapAdminOptions.SectionName}:DefaultPassword"] ?? "12345678"
        };

        // Named HTTP clients — IHttpClientFactory ile pool yonetilir
        services.AddHttpClient("tcmb", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("integrator-reachability", c => c.Timeout = TimeSpan.FromSeconds(10));
        // SOAP cagri timeout'u 5dk — Reachability postboxservice.svc icin (rapor §2.10 fix).
        services.AddHttpClient(ReachabilityIntegratorDocumentClient.HttpClientName,
            c => c.Timeout = TimeSpan.FromSeconds(300));

        if (useMockIntegratorClient)
        {
            services.AddSingleton<IIntegratorDocumentClient>(sp => new MockIntegratorDocumentClient(
                sp.GetRequiredService<IHttpClientFactory>(), simulatedDocumentsPerPull));
        }
        else
        {
            services.AddSingleton<IIntegratorDocumentClient, ReachabilityIntegratorDocumentClient>();
        }

        services.AddSingleton<InMemoryDataStore>();
        services.AddSingleton(databaseOptions);
        services.AddSingleton(bootstrapAdminOptions);
        // Per-company connection registry (Web ile ayni) — SqlServerConnectionFactory bu
        // dependency'yi bekliyor, kayit olmadan DI validation basarisiz oluyor.
        services.AddSingleton<CompanyConnectionRegistry>();
        services.AddSingleton<ICompanyConnectionRegistry>(sp => sp.GetRequiredService<CompanyConnectionRegistry>());
        // SqlServerConnectionFactory Web'de IHttpContextAccessor kullanarak tenant cozer.
        // Worker'da HTTP yok — dummy accessor yeter (HttpContext null doner, factory ilk
        // sirket kaydina duser).
        services.AddHttpContextAccessor();
        services.AddSingleton<SqlServerConnectionFactory>();
        services.AddScoped<IDocumentImportService, DocumentImportService>();
        services.AddScoped<IAdminReadService, AdminReadService>();
        services.AddScoped<IAdminManagementService, AdminManagementService>();

        // Grafana provisioning — Worker AdminManagementService kullaniyor, DI grafi
        // icin gerekli. Worker normal kosulda Grafana cagrisi yapmaz; IsEnabled=false
        // ise no-op. AdminPassword DPAPI ile sifrelenmistir, PostConfigure'da cozulur.
        services.Configure<GrafanaOptions>(configuration.GetSection(GrafanaOptions.SectionName));
        services.PostConfigure<GrafanaOptions>(opts =>
        {
            opts.AdminPassword = DecryptIfNeeded(opts.AdminPassword);
        });
        services.AddHttpClient<
            CalibraHub.Application.Abstractions.Services.IGrafanaProvisioningService,
            CalibraHub.Infrastructure.Grafana.GrafanaProvisioningService>();
        services.AddScoped<IApprovalQueueService, ApprovalQueueService>();
        services.AddScoped<IPasswordHashService, Pbkdf2PasswordHashService>();
        services.AddScoped<CalibraDatabaseInitializer>();
        services.AddScoped<IIntegratorSettingsRepository, SqlIntegratorSettingsRepository>();
        services.AddScoped<ISmtpProfileRepository, SqlSmtpProfileRepository>();
        services.AddScoped<IErpConnectionSettingsRepository, SqlErpConnectionSettingsRepository>();
        services.AddScoped<ICompanyRepository, SqlCompanyRepository>();
        services.AddScoped<IIntegrationApiProfileRepository, SqlIntegrationApiProfileRepository>();
        services.AddScoped<IDepartmentRepository, SqlDepartmentRepository>();
        services.AddScoped<IUserProfileRepository, SqlUserProfileRepository>();
        services.AddScoped<IIncomingDocumentRepository, SqlIncomingDocumentRepository>();
        services.AddScoped<IIntegratorImportLogRepository, SqlPltSystemLogRepository>();
        services.AddScoped<INoteRepository, SqlNoteRepository>();
        services.AddScoped<IUserNotificationRepository, SqlUserNotificationRepository>();
        services.AddScoped<CalibraHub.Application.Abstractions.Services.IReminderEmailSender,
                           CalibraHub.Infrastructure.Notifications.SmtpReminderEmailSender>();
        services.AddScoped<IScheduledTaskRepository, SqlScheduledTaskRepository>();
        services.AddScoped<IScheduledTaskRunRepository, SqlScheduledTaskRunRepository>();
        services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskTokenResolver,
                           CalibraHub.Application.Services.Scheduling.ScheduledTaskTokenResolver>();

        // Executor registry — her TaskType icin bir IScheduledTaskExecutor.
        // DI `IEnumerable<IScheduledTaskExecutor>` enjekte ettiginde tum kayitlari doner.
        services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Persistence.Scheduling.SqlProcedureTaskExecutor>();
        services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Infrastructure.Scheduling.HttpApiTaskExecutor>();
        services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Application.Services.Scheduling.CurrencyRefreshTaskExecutor>();
        services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Infrastructure.Scheduling.ViewReportTaskExecutor>();
        services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Application.Services.Scheduling.IntegrationTaskExecutor>();

        // Integration runtime — cron tetiklenen entegrasyonlar icin Worker tarafinda da gerekli
        services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
        services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IIntegrationApiProfileRepository,
                           CalibraHub.Persistence.Repositories.SqlIntegrationApiProfileRepository>();
        services.AddScoped<IFormMetadataService, FormMetadataService>();
        services.AddScoped<IFormLinesRepository, SqlFormLinesRepository>();
        services.AddScoped<IItemCombinationResolver, SqlItemCombinationResolver>();
        services.AddScoped<IPostProcedureExecutor, SqlPostProcedureExecutor>();
        services.AddScoped<IIntegrationStatusTracker, SqlIntegrationStatusTracker>();
        services.AddScoped<IIntegrationRecordStatusRepository, SqlIntegrationRecordStatusRepository>();
        services.AddScoped<IMappingEngine, MappingEngine>();
        services.AddScoped<IIntegrationAuthHandler, IntegrationAuthHandler>();
        services.AddScoped<IHttpExecutor, HttpExecutor>();
        services.AddScoped<IIntegrationRunner, IntegrationRunner>();
        services.AddMemoryCache();
        services.AddScoped<CalibraHub.Application.Abstractions.Services.IEmailSender,
                           CalibraHub.Infrastructure.Notifications.SmtpEmailSender>();
        services.AddHttpClient();

        services.AddScoped<CalibraHub.Application.Services.Scheduling.IScheduledTaskDispatcher,
                           CalibraHub.Application.Services.Scheduling.ScheduledTaskDispatcher>();

        // Scheduler polling worker — BUILTIN olmayan tasklari orchestrate eder
        services.AddHostedService<ScheduledTaskPollingWorker>();

        // Notlar at-rest sifreleme — Web ile ayni key ring'i paylasir.
        // Worker sadece reminder icin metadata okur, note.Content alanini kullanmaz,
        // o yuzden key paylasimi zorunlu degil; ama Unprotect basarisiz olursa
        // ciphertext aynen gelir ve worker tarafindan kullanilmaz (risk yok).
        var dpKeysPath = System.IO.Path.Combine(environment.ContentRootPath, ".app-data-protection");
        System.IO.Directory.CreateDirectory(dpKeysPath);
        services.AddDataProtection()
            .SetApplicationName("CalibraHub.Web")
            .PersistKeysToFileSystem(new System.IO.DirectoryInfo(dpKeysPath));
        services.AddSingleton<
            CalibraHub.Application.Abstractions.Security.INoteEncryptionService,
            CalibraHub.Infrastructure.Security.DataProtectionNoteEncryptionService>();

        services.AddHostedService<DocumentImportWorker>();
        services.AddHostedService<ReminderNotificationWorker>();
        services.AddScoped<ICurrencyRepository, SqlCurrencyRepository>();
        services.AddScoped<IExchangeRateRepository, SqlExchangeRateRepository>();
        services.AddScoped<ICurrencyService, CurrencyService>();
        services.AddSingleton<ITcmbExchangeRateClient, TcmbExchangeRateClient>();
        services.AddHostedService<ExchangeRateUpdateWorker>();
    })
    .Build();

using (var scope = host.Services.CreateScope())
{
    var dbInitializer = scope.ServiceProvider.GetRequiredService<CalibraDatabaseInitializer>();
    await dbInitializer.InitializeAsync(CancellationToken.None);
}

await host.RunAsync();

static string DecryptIfNeeded(string value)
{
    const string prefix = "dpapi:";
    if (!value.StartsWith(prefix, StringComparison.Ordinal)) return value;
    if (!OperatingSystem.IsWindows()) return value;
    var encrypted = Convert.FromBase64String(value[prefix.Length..]);
    var decrypted = System.Security.Cryptography.ProtectedData.Unprotect(
        encrypted, null, System.Security.Cryptography.DataProtectionScope.LocalMachine);
    return System.Text.Encoding.UTF8.GetString(decrypted);
}
