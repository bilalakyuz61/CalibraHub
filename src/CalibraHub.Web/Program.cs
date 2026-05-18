using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Services;
using CalibraHub.Application.Services.Integration;
using CalibraHub.Infrastructure.Integrations;
using CalibraHub.Infrastructure.Reporting;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Repositories;
using CalibraHub.Application.Configuration;
using CalibraHub.Web.Infrastructure.Collaboration;
using CalibraHub.Web.Infrastructure.Ui;
using CalibraHub.Web.Infrastructure.Workspace;
using CalibraHub.Web.Middleware;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Yarp.ReverseProxy.Configuration;

// ─── SERVICE STARTUP HARDENING ────────────────────────────────────────────
// Servis modunda (LocalSystem) cwd = C:\Windows\System32 olur — relative path
// kullanan herhangi bir kod fail eder. Exe konumuna SETLEDIK.
System.IO.Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// Erken yakalanmamis exception'lari Event Viewer'a yaz — installer'in inkjr
// loglari yetmezse Application Event Log'da CalibraHub.Web kaynagi altinda gorunur.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        var ex = e.ExceptionObject as Exception;
        if (OperatingSystem.IsWindows())
        {
            using var log = new System.Diagnostics.EventLog("Application") { Source = "CalibraHub Web" };
            if (!System.Diagnostics.EventLog.SourceExists("CalibraHub Web"))
                System.Diagnostics.EventLog.CreateEventSource("CalibraHub Web", "Application");
            log.WriteEntry(
                $"FATAL UnhandledException: {ex}\r\n\r\nIsTerminating: {e.IsTerminating}\r\nCWD: {Environment.CurrentDirectory}",
                System.Diagnostics.EventLogEntryType.Error);
        }
    }
    catch { /* swallow — daha kötü hale getirmesin */ }
};
// ──────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "CalibraHub Web";
});
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();
// Servis modunda Event Viewer'a da yaz — UseWindowsService eklemiyor, manuel ekledik
if (OperatingSystem.IsWindows())
{
    builder.Logging.AddEventLog(settings =>
    {
        settings.SourceName = "CalibraHub Web";
    });
}

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
// Eski FastReport servisleri (IReportService, IDocumentGenerationService,
// IReportTemplateRepository, IReportTemplateSourceRepository) kaldirildi —
// tum belge basimlari artik Belge Tasarimcisi (DocDesigner) uzerinden yapilir.
builder.Services.AddScoped<IDocumentTypeRepository, SqlDocumentTypeRepository>();
// Sistem Ayarlari gate + lisans dogrulama
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IGateCredentialsRepository,
                           CalibraHub.Persistence.Repositories.SqlGateCredentialsRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IGatePasswordService,
                           CalibraHub.Application.Services.Gate.GatePasswordService>();

// WhatsApp Cloud API + Safety Layer (rate limit, ban koruma)
builder.Services.AddHttpClient();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWhatsAppConfigRepository,
                           CalibraHub.Persistence.Repositories.SqlWhatsAppConfigRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWhatsAppSendLogRepository,
                           CalibraHub.Persistence.Repositories.SqlWhatsAppSendLogRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWhatsAppSafetyRulesRepository,
                           CalibraHub.Persistence.Repositories.SqlWhatsAppSafetyRulesRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWaInboxRepository,
                           CalibraHub.Persistence.Repositories.SqlWaInboxRepository>();
builder.Services.AddScoped<CalibraHub.Application.Services.Messaging.WhatsAppSafetyChecker>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IWhatsAppService,
                           CalibraHub.Application.Services.Messaging.WhatsAppService>();
builder.Services.AddHostedService<CalibraHub.Application.Services.Messaging.WhatsAppInboxPollingService>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Services.IMachineIdProvider,
                              CalibraHub.Infrastructure.Security.WindowsMachineIdProvider>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.ILicenseRepository,
                           CalibraHub.Persistence.Repositories.SqlLicenseRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.ILicenseService,
                           CalibraHub.Application.Services.Licensing.LicenseService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name        = ".CalibraHub.Session";
    options.Cookie.HttpOnly    = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout        = TimeSpan.FromMinutes(60);
});
builder.Services.AddScoped<IScheduledTaskRepository, SqlScheduledTaskRepository>();
builder.Services.AddScoped<IScheduledTaskRunRepository, SqlScheduledTaskRunRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskTokenResolver,
                           CalibraHub.Application.Services.Scheduling.ScheduledTaskTokenResolver>();
// Executor registry — her TaskType icin bir IScheduledTaskExecutor
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Persistence.Scheduling.SqlProcedureTaskExecutor>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Infrastructure.Scheduling.HttpApiTaskExecutor>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Infrastructure.Scheduling.ViewReportTaskExecutor>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Application.Services.Scheduling.IntegrationTaskExecutor>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IEmailSender,
                           CalibraHub.Infrastructure.Notifications.SmtpEmailSender>();
builder.Services.AddScoped<CalibraHub.Application.Services.Scheduling.IScheduledTaskDispatcher,
                           CalibraHub.Application.Services.Scheduling.ScheduledTaskDispatcher>();
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
builder.Services.AddHostedService<CollaborationStartupLoadService>();
builder.Services.AddHostedService<CollaborationCleanupService>();

// Named HTTP clients — static HttpClient anti-pattern yerine IHttpClientFactory ile pool yonetilir
builder.Services.AddHttpClient("tcmb", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("integrator-reachability", c => c.Timeout = TimeSpan.FromSeconds(10));
// SOAP cagri timeout'u 5dk — Reachability postboxservice.svc icin (rapor §2.10 fix).
builder.Services.AddHttpClient(ReachabilityIntegratorDocumentClient.HttpClientName,
    c => c.Timeout = TimeSpan.FromSeconds(300));

if (useMockIntegratorClient)
{
    builder.Services.AddSingleton<IIntegratorDocumentClient>(sp => new MockIntegratorDocumentClient(
        sp.GetRequiredService<IHttpClientFactory>(), simulatedDocumentsPerPull));
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
builder.Services.AddScoped<IAddressRepository, SqlAddressRepository>();
builder.Services.AddScoped<IContactItemRepository, SqlContactItemRepository>();
builder.Services.AddScoped<IDbSchemaRepository, SqlDbSchemaRepository>();
builder.Services.AddScoped<IDbSchemaService, DbSchemaService>();
builder.Services.AddScoped<ICardGroupRepository, SqlCardGroupRepository>();
builder.Services.AddScoped<ICollaborationLockRepository, SqlCollaborationLockRepository>();
builder.Services.AddScoped<IDesignTemplateRepository, SqlDesignTemplateRepository>();
builder.Services.AddScoped<IIntegrationApiProfileRepository, SqlIntegrationApiProfileRepository>();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IDocumentRepository, SqlDocumentRepository>();
builder.Services.AddScoped<IDocumentSourceRepository, SqlDocumentSourceRepository>();
builder.Services.AddScoped<IUserSettingRepository, SqlUserSettingRepository>();
builder.Services.AddScoped<ISalesRepresentativeRepository, SqlSalesRepresentativeRepository>();
builder.Services.AddScoped<ISalesRepresentativeService, SalesRepresentativeService>();
builder.Services.AddScoped<ICariGroupRepository, SqlCariGroupRepository>();
builder.Services.AddScoped<ICariGroupService, CariGroupService>();
builder.Services.AddScoped<IIntegrationRepository, SqlIntegrationRepository>();
builder.Services.AddScoped<IBodyTemplateRepository, SqlBodyTemplateRepository>();
builder.Services.AddScoped<IFormLinesRepository, SqlFormLinesRepository>();
builder.Services.AddScoped<IItemCombinationResolver, SqlItemCombinationResolver>();
builder.Services.AddScoped<IPostProcedureExecutor, SqlPostProcedureExecutor>();
builder.Services.AddScoped<IIntegrationStatusTracker, SqlIntegrationStatusTracker>();
builder.Services.AddScoped<IIntegrationRecordStatusRepository, SqlIntegrationRecordStatusRepository>();
builder.Services.AddScoped<IIntegrationQueueService, SqlIntegrationQueueService>();
builder.Services.AddScoped<IIntegrationDocCatalogRepository, SqlIntegrationDocCatalogRepository>();
builder.Services.AddScoped<IIntegrationDocCatalogService, IntegrationDocCatalogService>();
builder.Services.AddHostedService<CalibraHub.Web.Services.IntegrationDocCatalogSeedService>();
builder.Services.AddScoped<IDocumentNumberService, SqlDocumentNumberService>();
builder.Services.AddScoped<IDocumentNumberRuleRepository, SqlDocumentNumberRuleRepository>();
builder.Services.AddScoped<IFormMetadataService, FormMetadataService>();
builder.Services.AddScoped<IMappingEngine, MappingEngine>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IIntegrationLookupFunctionDefinitionRepository,
                           CalibraHub.Persistence.Repositories.SqlIntegrationLookupFunctionDefinitionRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IIntegrationLookupFunctionRegistry,
                           CalibraHub.Persistence.Repositories.SqlIntegrationLookupFunctionRegistry>();
// NOT: IntegrationLookupFunctionAdminService DI'dan kaldirildi — admin UI artik yok.
// Kullanici DB tarafinda 3-paramli SQL function tanimliyor; wizard direkt DB function
// listesinden secim yapiyor (/Integrations/api/db-functions endpoint).
builder.Services.AddScoped<IIntegrationAuthHandler, IntegrationAuthHandler>();
builder.Services.AddSingleton<IEndpointCatalogService, CalibraHub.Web.Services.EndpointCatalogService>();
// Eager warm-up: Lazy yerine startup'ta parse — CSV bulunamiyorsa veya parse hatasi
// varsa erken yakala (lazy ise ilk istegi atan kullaniciya geri donus)
builder.Services.AddScoped<IHttpExecutor, HttpExecutor>();
builder.Services.AddScoped<IBodySchemaResolver, BodySchemaResolver>();
builder.Services.AddScoped<IIntegrationRunner, IntegrationRunner>();
builder.Services.AddScoped<IIntegrationService, IntegrationService>();
// OnSave dispatcher — Save sonrasi otomatik trigger'lari arka planda fire eder.
// Singleton: scope'u kendi acar (IServiceScopeFactory uzerinden), HTTP context bittikten sonra calisir.
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Services.IIntegrationOnSaveDispatcher,
                              CalibraHub.Application.Services.Integration.IntegrationOnSaveDispatcher>();
// Mapster (rapor §2.4) — entity ↔ DTO mapping. Yeni kod IMapper veya .Adapt() kullanir.
builder.Services.AddSingleton(CalibraHub.Application.Mapping.MapsterConfig.BuildTypeAdapterConfig());
builder.Services.AddScoped<MapsterMapper.IMapper, MapsterMapper.ServiceMapper>();

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

// Depo İşlemleri (Transfer + Ambar Giriş/Çıkış)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IStockDocRepository,
                           CalibraHub.Persistence.Repositories.SqlStockDocRepository>();

// Sabit alan ayarlari (FldSet — rehber eslestirme)
builder.Services.AddScoped<IFieldSettingRepository, SqlFieldSettingRepository>();

// Faz 0: Sirket Parametreleri + Numerator (sayac altyapisi)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.ICompanyParameterRepository,
    CalibraHub.Persistence.Repositories.SqlCompanyParameterRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.ICompanyParameterService,
    CalibraHub.Application.Services.CompanyParameterService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.INumeratorRepository,
    CalibraHub.Persistence.Repositories.SqlNumeratorRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.INumeratorService,
    CalibraHub.Application.Services.NumeratorService>();

// Faz 1: Uretim Is Emri
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkOrderRepository,
    CalibraHub.Persistence.Repositories.SqlWorkOrderRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IWorkOrderService,
    CalibraHub.Application.Services.WorkOrderService>();

// Operasyon Tanımlamaları (Faz 3 routing/operasyon temel sözlüğü)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IOperationRepository,
    CalibraHub.Persistence.Repositories.SqlOperationRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IOperationService,
    CalibraHub.Application.Services.OperationService>();

// Routing + Operasyon-Makine Süreleri
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IRoutingRepository,
    CalibraHub.Persistence.Repositories.SqlRoutingRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IRoutingService,
    CalibraHub.Application.Services.RoutingService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IOperationMachineTimeRepository,
    CalibraHub.Persistence.Repositories.SqlOperationMachineTimeRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IOperationMachineTimeService,
    CalibraHub.Application.Services.OperationMachineTimeService>();

// Is Emri Operasyonlari (Faz 3a — shop-floor temel)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkOrderOperationRepository,
    CalibraHub.Persistence.Repositories.SqlWorkOrderOperationRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IWorkOrderOperationService,
    CalibraHub.Application.Services.WorkOrderOperationService>();

// Is Emri Bilesenleri (Faz 2 — BOM patlatma ciktisi)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkOrderComponentRepository,
    CalibraHub.Persistence.Repositories.SqlWorkOrderComponentRepository>();

// Personnel — uretim personneli kartlari (User tablosundan ayri, Faz 3a revize)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IPersonnelRepository,
    CalibraHub.Persistence.Repositories.SqlPersonnelRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IPersonnelService,
    CalibraHub.Application.Services.PersonnelService>();

// Dinamik Raporlama Modulu (RptView / RptDef / ReportEngine)
builder.Services.AddScoped<IRptViewRepository, SqlRptViewRepository>();
builder.Services.AddScoped<IRptDefinitionRepository, SqlRptDefinitionRepository>();
builder.Services.AddScoped<IRptRunLogRepository, SqlRptRunLogRepository>();
builder.Services.AddScoped<IReportQueryExecutor, SqlReportQueryExecutor>();
builder.Services.AddScoped<IReportEngineService, ReportEngineService>();

// Document Designer (Belge Tasarımcısı)
builder.Services.AddScoped<IDocLayoutRepository, SqlDocLayoutRepository>();
builder.Services.AddScoped<IDocLayoutRenderer, DocLayoutRenderer>();
builder.Services.AddScoped<IDocDesignerService, DocDesignerService>();

// Dinamik Tasarım Seçim Motoru
// Yeni kriter eklemek için: IDesignCriterion implementasyonu + AddSingleton.
// Repository ve Provider'a dokunmak gerekmez (Open/Closed).
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.DesignProvider.IDesignCriterion,
                              CalibraHub.Application.Services.DesignProvider.CustomerCriterion>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.DesignProvider.IDesignCriterion,
                              CalibraHub.Application.Services.DesignProvider.ContactGroupCriterion>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.DesignProvider.IDesignCriterion,
                              CalibraHub.Application.Services.DesignProvider.UserCriterion>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.DesignProvider.IDesignCriterion,
                              CalibraHub.Application.Services.DesignProvider.BranchCriterion>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.DesignProvider.IDesignCriterion,
                              CalibraHub.Application.Services.DesignProvider.WarehouseCriterion>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IDocLayoutRuleRepository,
                           CalibraHub.Persistence.Repositories.SqlDocLayoutRuleRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.DesignProvider.IDesignProvider,
                           CalibraHub.Application.Services.DesignProvider.DesignProvider>();
builder.Services.AddScoped<IDocLayoutRuleService, DocLayoutRuleService>();

// PrintDispatcher — Belge Tasarimcisi (DocDesigner) yoluyla PDF uretir
builder.Services.AddScoped<IPrintDispatcher, PrintDispatcher>();

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
    builder.Services.AddScoped<IUserNotificationRepository, SqlUserNotificationRepository>();
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
    builder.Services.AddScoped<IUserNotificationRepository, SqlUserNotificationRepository>();
    builder.Services.AddScoped<IOrgChartRepository, SqlOrgChartRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IAttachmentRepository,
                               CalibraHub.Persistence.Repositories.SqlAttachmentRepository>();
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

// ─── Grafana entegrasyonu ─────────────────────────────────────────────────
// Options + YARP reverse proxy + provisioning service. Servis 127.0.0.1:61005'te
// calisir (CalibraHub 61xxx port konvansiyonu), /grafana path'i YARP ile
// Grafana'ya forward edilir. Auth: cookie -> X-WEBAUTH-* (auth.proxy mode).
// AdminPassword DPAPI ile sifreli gelir.
builder.Services.Configure<GrafanaOptions>(
    builder.Configuration.GetSection(GrafanaOptions.SectionName));

builder.Services.PostConfigure<GrafanaOptions>(opts =>
{
    opts.AdminPassword = DecryptIfNeeded(opts.AdminPassword);
});

builder.Services.AddHttpClient<
    CalibraHub.Application.Abstractions.Services.IGrafanaProvisioningService,
    CalibraHub.Infrastructure.Grafana.GrafanaProvisioningService>();

var grafanaEnabled = builder.Configuration.GetValue<bool>($"{GrafanaOptions.SectionName}:Enabled");
var grafanaUrl = builder.Configuration[$"{GrafanaOptions.SectionName}:Url"] ?? "http://127.0.0.1:61005";

if (grafanaEnabled)
{
    var grafanaRoutes = new[]
    {
        new RouteConfig
        {
            RouteId = "grafana-route",
            ClusterId = "grafana-cluster",
            Match = new RouteMatch { Path = "/grafana/{**catch-all}" }
        }
    };

    var grafanaClusters = new[]
    {
        new ClusterConfig
        {
            ClusterId = "grafana-cluster",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["primary"] = new DestinationConfig { Address = grafanaUrl }
            }
        }
    };

    builder.Services.AddReverseProxy().LoadFromMemory(grafanaRoutes, grafanaClusters);
}
// ──────────────────────────────────────────────────────────────────────────
var mvcBuilder = builder.Services.AddControllersWithViews();
mvcBuilder.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
});

// ── FluentValidation (rapor §2.5) ────────────────────────────────────────
// Controller'larda manuel `ModelState.IsValid` ve service icinde manuel
// `if (string.IsNullOrWhiteSpace(...)) throw` pattern'leri yerine merkezi
// validator class'lari. Otomatik dogrulama [ApiController]'lar icin actif.
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(typeof(CalibraHub.Application.Validation.SaveDocumentRequestValidator).Assembly);

// ── Mobile API CORS ──────────────────────────────────────────────────────
// Android companion app (mobile/CalibraHubAndroid) icin. AVD emulator
// host alias'i 10.0.2.2; fiziksel cihazlar LAN IP'siyle gelir. Production
// erp.calibrahub.com da listeye eklendi.
// Sadece /api/mobile/* rotalarinin [EnableCors("MobileApi")] attribute'u ile
// devreye girer; diger rotalar etkilenmez.
var mobileCorsOrigins = builder.Configuration
    .GetSection("Mobile:CorsAllowedOrigins").Get<string[]>()
    ?? new[]
    {
        "http://10.0.2.2",          // Android emulator → host
        "http://localhost",
        "http://127.0.0.1",
        "https://erp.calibrahub.com"
    };
builder.Services.AddCors(opt => opt.AddPolicy("MobileApi", p => p
    .WithOrigins(mobileCorsOrigins)
    .SetIsOriginAllowedToAllowWildcardSubdomains()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}
builder.Services.AddDataProtection()
    .SetApplicationName("CalibraHub.Web")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

var app = builder.Build();

// Endpoint katalog warm-up — startup'ta CSV parse, hata olursa erken yakala
try
{
    var catalog = app.Services.GetRequiredService<IEndpointCatalogService>();
    var count = catalog.GetAll().Count;
    app.Logger.LogInformation("[EndpointCatalog] {Count} endpoint katalogtan yuklendi.", count);
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "[EndpointCatalog] Yukleme basarisiz — combobox bos kalir.");
}

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

        // Sistem Ayarlari (Gate) sifresi — DB'de yoksa appsettings'ten/random uretip seed et
        try
        {
            var gatePwdService = scope.ServiceProvider
                .GetRequiredService<CalibraHub.Application.Abstractions.Services.IGatePasswordService>();
            await gatePwdService.EnsureSeededAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "[Gate] Initial password seed basarisiz");
        }

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
        // yetiyor. Heuristic: Code/Name kolonlari ValueColumn/DisplayColumn olarak
        // oncelikli; aksi halde 1./2. kolon. Kullanici elle GuideMas'i duzenleyerek
        // override edebilir, ancak NormalizeStandardColumnsAsync her startup'ta
        // standart kurali yeniden uygular (Code/Name → Value/Display).
        try
        {
            var guideRepo = scope.ServiceProvider.GetRequiredService<IGuideRepository>();
            var addedGuides = await guideRepo.DiscoverAndRegisterGuidesAsync(CancellationToken.None);
            if (addedGuides > 0)
                app.Logger.LogInformation("[Guide Discovery] Otomatik {Count} yeni rehber kaydedildi", addedGuides);

            // Standart kural: tum mevcut kayitlarda Code/Name kolonlari varsa
            // ValueColumn=Code, DisplayColumn=Name olarak normalize edilir.
            var normalized = await guideRepo.NormalizeStandardColumnsAsync(CancellationToken.None);
            if (normalized > 0)
                app.Logger.LogInformation("[Guide Normalize] {Count} kayit standart kolon kurali ile guncellendi", normalized);
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

        // Master (system) DB adini connection string'den cozup vw_ReportDocument view'inde
        // 3-parcali isim [Calibra].[dbo].[Company] referansi icin kullaniriz.
        // Config'deki connection string DPAPI ile sifreli; startup'ta cozulmus
        // degeri databaseOptions.ConnectionString'den aliyoruz.
        string systemDbName;
        try
        {
            systemDbName = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(databaseOptions.ConnectionString).InitialCatalog;
            if (string.IsNullOrWhiteSpace(systemDbName)) systemDbName = "Calibra";
        }
        catch
        {
            systemDbName = "Calibra";
        }

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

            // DocDesigner belge view'i — vw_ReportDocument + stored proc.
            // Idempotent CREATE OR ALTER; her startup'ta v_Flat_* widget
            // kolonlarini hw_* / lw_* olarak yansitan guncel view uretir.
            try
            {
                await dbInitForCompanies.EnsureReportDocumentViewAsync(
                    c.DatabaseConnectionString, systemDbName, CancellationToken.None);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex,
                    "[Report View] Sirket {CompanyId} icin vw_ReportDocument uretilemedi", c.Id);
            }
        }
    }
}

// Tema-uyumlu ozel hata sayfasi — hem Development hem Production'da /Home/Error kullanilir.
// Development'ta view'da stack trace + detay gosterilir; Prod'da sadece kullanici-dostu mesaj.
// Bu, default developer exception page (sari sayfa) yerine kurumsal gorunum saglar.
app.UseExceptionHandler("/Home/Error");
// JSON API endpoint'leri icin standart ApiResponse<T> formatinda hata cevabi.
// HTML akisi etkilenmez (UseExceptionHandler'a re-throw eder). Rapor §2.7.
// Sirasi: UseExceptionHandler'dan SONRA, UseRouting'ten ONCE.
app.UseApiExceptionHandler();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
    }
});

app.UseRouting();
app.UseSession();  // GateController'in Session'a erisimi icin
app.UseAuthentication();
app.UseAuthorization();

// CORS — Authentication'dan sonra ki cookie-based credentials akabilsin.
// Sadece [EnableCors("MobileApi")] olan endpoint'lerde devreye girer.
app.UseCors();
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

// Grafana reverse proxy — yalnizca Grafana:Enabled=true ise haritalanir.
// Pipeline: cookie auth challenge (RequireAuthorization), header injection
// (GrafanaAuthProxyMiddleware), ve YARP forward.
if (grafanaEnabled)
{
    app.MapReverseProxy(proxyPipeline =>
    {
        proxyPipeline.UseMiddleware<GrafanaAuthProxyMiddleware>();
    }).RequireAuthorization();
}

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
