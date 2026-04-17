using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Services;
using CalibraHub.Infrastructure.Integrations;
using CalibraHub.Infrastructure.Reporting;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Repositories;
using CalibraHub.Web.Infrastructure.Collaboration;
using CalibraHub.Web.Infrastructure.Ui;
using CalibraHub.Web.Infrastructure.Workspace;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "CalibraHub Web";
});
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var simulatedDocumentsPerPull = builder.Configuration.GetValue<int>("Integrator:SimulatedDocumentsPerPull", 2);
var useMockIntegratorClient = builder.Configuration.GetValue<bool?>("Integrator:UseMockClient") ?? false;
if (!builder.Environment.IsDevelopment())
{
    useMockIntegratorClient = false;
}
var databaseOptions = new CalibraDatabaseOptions
{
    ConnectionString = DecryptIfNeeded(builder.Configuration[$"{CalibraDatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty),
    Schema = builder.Configuration[$"{CalibraDatabaseOptions.SectionName}:Schema"] ?? "dbo",
    AutoCreateDatabaseOnStartup = builder.Configuration.GetValue<bool?>($"{CalibraDatabaseOptions.SectionName}:AutoCreateDatabaseOnStartup") ?? true
};
var bootstrapAdminOptions = new BootstrapAdminOptions
{
    SeedOnStartup = builder.Configuration.GetValue<bool?>($"{BootstrapAdminOptions.SectionName}:SeedOnStartup") ?? true,
    FullName = builder.Configuration[$"{BootstrapAdminOptions.SectionName}:FullName"] ?? "Sistem Admin",
    Email = builder.Configuration[$"{BootstrapAdminOptions.SectionName}:Email"] ?? "admin@calibra.local",
    EmployeeCode = builder.Configuration[$"{BootstrapAdminOptions.SectionName}:EmployeeCode"] ?? "ADM-001",
    DefaultPassword = builder.Configuration[$"{BootstrapAdminOptions.SectionName}:DefaultPassword"] ?? "12345678"
};
var useInMemoryPersistence = builder.Environment.IsDevelopment() &&
                             !await CanOpenSqlConnectionAsync(databaseOptions.ConnectionString);
var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, ".app-data-protection");

builder.Services.AddScoped<IDocumentImportService, DocumentImportService>();
builder.Services.AddSingleton<IReportService, FastReportService>();
builder.Services.AddScoped<IDocumentGenerationService, DocumentGenerationService>();
builder.Services.AddScoped<IDocumentTypeRepository, SqlDocumentTypeRepository>();
builder.Services.AddScoped<IReportTemplateRepository, SqlReportTemplateRepository>();
builder.Services.AddScoped<IReportDataRepository, SqlReportDataRepository>();
builder.Services.AddSingleton<ZplGeneratorService>();
builder.Services.AddScoped<IAdminReadService, AdminReadService>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IUiConfigurationService, UiConfigurationService>();
builder.Services.AddScoped<ILogisticsConfigurationService, LogisticsConfigurationService>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IApprovalQueueService, ApprovalQueueService>();
builder.Services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
builder.Services.AddScoped<IPasswordHashService, Pbkdf2PasswordHashService>();
builder.Services.AddScoped<IUiTextService, UiTextService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR();
builder.Services.AddSingleton<CollaborationRuntimeStore>();
builder.Services.AddHostedService<CollaborationCleanupService>();

if (useMockIntegratorClient)
{
    builder.Services.AddSingleton<IIntegratorDocumentClient>(_ => new MockIntegratorDocumentClient(simulatedDocumentsPerPull));
}
else
{
    builder.Services.AddSingleton<IIntegratorDocumentClient, ReachabilityIntegratorDocumentClient>();
}

builder.Services.AddSingleton<InMemoryDataStore>();
builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton(bootstrapAdminOptions);
builder.Services.AddSingleton<CompanyConnectionRegistry>();
builder.Services.AddSingleton<ICompanyConnectionRegistry>(sp => sp.GetRequiredService<CompanyConnectionRegistry>());
builder.Services.AddSingleton<SqlServerConnectionFactory>();
builder.Services.AddScoped<ILogisticsConfigurationRepository, SqlLogisticsConfigurationRepository>();
builder.Services.AddScoped<IFinanceRepository, SqlFinanceRepository>();
builder.Services.AddScoped<ICardGroupRepository, SqlCardGroupRepository>();
builder.Services.AddScoped<IDesignTemplateRepository, SqlDesignTemplateRepository>();
builder.Services.AddScoped<IIntegrationEventRepository, SqlIntegrationEventRepository>();
builder.Services.AddScoped<IIntegrationApiProfileRepository, SqlIntegrationApiProfileRepository>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IIntegrationEventService, IntegrationEventService>();
builder.Services.AddScoped<IDocumentRepository, SqlDocumentRepository>();
builder.Services.AddScoped<IUserSettingRepository, SqlUserSettingRepository>();
builder.Services.AddScoped<ISalesRepresentativeRepository, SqlSalesRepresentativeRepository>();
builder.Services.AddScoped<ISalesRepresentativeService, SalesRepresentativeService>();
builder.Services.AddScoped<ICurrencyRepository, SqlCurrencyRepository>();
builder.Services.AddScoped<IExchangeRateRepository, SqlExchangeRateRepository>();
builder.Services.AddScoped<ICurrencyService, CurrencyService>();
builder.Services.AddSingleton<ITcmbExchangeRateClient, TcmbExchangeRateClient>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
// SQL View tabanli Rehber (Lookup / LOV) sistemi
builder.Services.AddScoped<IGuideRepository, SqlGuideRepository>();
builder.Services.AddScoped<IGuideService, GuideService>();

// Form Yöneticisi (dbo.Forms CRUD)
builder.Services.AddScoped<IFormRepository, SqlFormRepository>();

// Sabit alan ayarlari (FldSet — rehber eslestirme)
builder.Services.AddScoped<IFieldSettingRepository, SqlFieldSettingRepository>();

// Dinamik Raporlama Modulu (RptView / RptDef / ReportEngine)
builder.Services.AddScoped<IRptViewRepository, SqlRptViewRepository>();
builder.Services.AddScoped<IRptDefinitionRepository, SqlRptDefinitionRepository>();
builder.Services.AddScoped<IRptRunLogRepository, SqlRptRunLogRepository>();
builder.Services.AddScoped<IReportQueryExecutor, SqlReportQueryExecutor>();
builder.Services.AddScoped<IReportEngineService, ReportEngineService>();

// EAV widget sistemi
builder.Services.AddScoped<IWidgetRepository, SqlWidgetRepository>();
builder.Services.AddScoped<IWidgetService, WidgetService>();
// Faz D Adim 1 — Legacy → Yeni EAV migration servisi (tek seferlik endpoint)
// Persistence katmaninda raw SQL kullandigi icin orada yasar.
builder.Services.AddScoped<ILegacyMigrationService, CalibraHub.Persistence.LegacyMigrationService>();
builder.Services.AddScoped<IPriceListRepository, SqlPriceListRepository>();
builder.Services.AddScoped<IPriceListService, PriceListService>();

// Notlar at-rest sifreleme (Katman 2) — DataProtection tabanli AES.
// Implementation Infrastructure katmanında — Worker de aynı servisi kullanır.
builder.Services.AddSingleton<
    CalibraHub.Application.Abstractions.Security.INoteEncryptionService,
    CalibraHub.Infrastructure.Security.DataProtectionNoteEncryptionService>();

if (useInMemoryPersistence)
{
    builder.Services.AddScoped<InMemoryDevelopmentBootstrapper>();
    builder.Services.AddScoped<IIntegratorSettingsRepository, InMemoryIntegratorSettingsRepository>();
    builder.Services.AddScoped<ISmtpProfileRepository, InMemorySmtpProfileRepository>();
    builder.Services.AddScoped<IErpConnectionSettingsRepository, InMemoryErpConnectionSettingsRepository>();
    builder.Services.AddScoped<ICompanyRepository, InMemoryCompanyRepository>();
    builder.Services.AddScoped<IDepartmentRepository, InMemoryDepartmentRepository>();
    builder.Services.AddScoped<IUserProfileRepository, InMemoryUserProfileRepository>();
    builder.Services.AddScoped<IUiLabelTranslationRepository, InMemoryUiLabelTranslationRepository>();
    builder.Services.AddScoped<IScreenLayoutRepository, InMemoryScreenLayoutRepository>();
    builder.Services.AddScoped<IIncomingDocumentRepository, InMemoryIncomingDocumentRepository>();
    builder.Services.AddScoped<IIntegratorImportLogRepository, InMemoryIntegratorImportLogRepository>();
    builder.Services.AddScoped<INoteRepository, SqlNoteRepository>();
    builder.Services.AddScoped<IOrgChartRepository, SqlOrgChartRepository>();
}
else
{
    builder.Services.AddScoped<CalibraDatabaseInitializer>();
    builder.Services.AddScoped<IIntegratorSettingsRepository, SqlIntegratorSettingsRepository>();
    builder.Services.AddScoped<ISmtpProfileRepository, SqlSmtpProfileRepository>();
    builder.Services.AddScoped<IErpConnectionSettingsRepository, SqlErpConnectionSettingsRepository>();
    builder.Services.AddScoped<ICompanyRepository, SqlCompanyRepository>();
    builder.Services.AddScoped<IDepartmentRepository, SqlDepartmentRepository>();
    builder.Services.AddScoped<IUserProfileRepository, SqlUserProfileRepository>();
    builder.Services.AddScoped<IUiLabelTranslationRepository, SqlUiLabelTranslationRepository>();
    builder.Services.AddScoped<IScreenLayoutRepository, SqlScreenLayoutRepository>();
    builder.Services.AddScoped<IIncomingDocumentRepository, SqlIncomingDocumentRepository>();
    builder.Services.AddScoped<IIntegratorImportLogRepository, SqlPltSystemLogRepository>();
    builder.Services.AddScoped<INoteRepository, SqlNoteRepository>();
    builder.Services.AddScoped<IOrgChartRepository, SqlOrgChartRepository>();
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization();
var mvcBuilder = builder.Services.AddControllersWithViews();
mvcBuilder.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}
builder.Services.AddDataProtection()
    .SetApplicationName("CalibraHub.Web")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    if (useInMemoryPersistence)
    {
        var bootstrapper = scope.ServiceProvider.GetRequiredService<InMemoryDevelopmentBootstrapper>();
        await bootstrapper.SeedAsync(CancellationToken.None);
        app.Logger.LogWarning("SQL Server connection unavailable. In-memory development persistence is active.");
    }
    else
    {
        var dbInitializer = scope.ServiceProvider.GetRequiredService<CalibraDatabaseInitializer>();
        await dbInitializer.InitializeAsync(CancellationToken.None);

        // Faz H — Flattened View otomasyonu: sistem DB'sinde mevcut tum BaseTable'li
        // formlar icin v_Flat_{FormCode} view'larini yeniden olustur. Widget
        // degisiklikleri sonrasi da WidgetService tarafindan tetiklenir; bu burada
        // sadece startup/restore senaryolarinda view'in mevcut widget state'ine
        // hizali olmasini garanti eder. Try/catch — hata server'i engellemez.
        try
        {
            var widgetRepo = scope.ServiceProvider.GetRequiredService<IWidgetRepository>();
            await widgetRepo.RegenerateAllFlattenedViewsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "[FlatView] Startup regen failed — view'lar widget degisikliginde tetiklenecek");
        }

        // Faz F+ — Guide auto-discovery: sys.views uzerinden v_Guide% pattern'ine
        // uyan view'lari GuideMas'a otomatik kaydet. Yeni bir rehber eklemek icin
        // admin'in sadece SSMS'te 'CREATE VIEW v_GuideXxx ...' yapmasi + restart
        // yetiyor. Heuristic: 1. kolon=Value, 2. kolon=Display. Kullanici elle
        // GuideMas'i duzenleyerek override edebilir (idempotent).
        try
        {
            var guideRepo = scope.ServiceProvider.GetRequiredService<IGuideRepository>();
            var addedGuides = await guideRepo.DiscoverAndRegisterGuidesAsync(CancellationToken.None);
            if (addedGuides > 0)
                app.Logger.LogInformation("[Guide Discovery] Otomatik {Count} yeni rehber kaydedildi", addedGuides);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "[Guide Discovery] Startup tarama basarisiz — rehberler manuel eklenebilir");
        }
    }

    // Seed per-company connection registry from DB
    var companyRepo = scope.ServiceProvider.GetRequiredService<ICompanyRepository>();
    var registry = app.Services.GetRequiredService<CompanyConnectionRegistry>();
    var allCompanies = await companyRepo.GetAllAsync(CancellationToken.None);
    foreach (var c in allCompanies)
    {
        registry.Set(c.Id, c.DatabaseConnectionString);
    }

    // Per-company guide view tazeleme — CREATE OR ALTER VIEW idempotent,
    // eski sema ile kurulmus sirket DB'lerinde cbv_Guide_* view'larini gunceller.
    // Kolon ekleme/cikarma sonrasi rehber aramasinin calismasi icin zorunlu.
    if (!useInMemoryPersistence)
    {
        var dbInitForCompanies = scope.ServiceProvider.GetRequiredService<CalibraDatabaseInitializer>();
        foreach (var c in allCompanies)
        {
            if (string.IsNullOrWhiteSpace(c.DatabaseConnectionString)) continue;
            try
            {
                await dbInitForCompanies.EnsureGuideSchemaForConnectionAsync(
                    c.DatabaseConnectionString, CancellationToken.None);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex,
                    "[Guide Schema] Sirket {CompanyId} icin view tazeleme basarisiz", c.Id);
            }
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
    }
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<WorkspaceRedirectPreservationMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
        }
        return Task.CompletedTask;
    });
    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<CollaborationHub>("/hubs/collaboration");

app.Run();

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

static async Task<bool> CanOpenSqlConnectionAsync(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return false;
    }

    try
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
            ConnectTimeout = 3,
            Pooling = false
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return true;
    }
    catch
    {
        return false;
    }
}
