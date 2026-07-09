using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Services;
using CalibraHub.Application.Services.Integration;
using CalibraHub.Infrastructure.Integrations;
using CalibraHub.Infrastructure.Reporting;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Repositories;
using CalibraHub.Web.Infrastructure;
using CalibraHub.Web.Infrastructure.Collaboration;
using CalibraHub.Web.Infrastructure.Ui;
using CalibraHub.Web.Infrastructure.Workspace;
using CalibraHub.Web.Middleware;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using System.Threading.RateLimiting;

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
var forceInMemory = builder.Configuration.GetValue<bool>("CalibraDatabase:ForceInMemory");
var useInMemoryPersistence = builder.Environment.IsDevelopment() && forceInMemory;
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
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWaContactRepository,
                           CalibraHub.Persistence.Repositories.SqlWaContactRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWaGroupRepository,
                           CalibraHub.Persistence.Repositories.SqlWaGroupRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IWaContactResolver,
                           CalibraHub.Application.Services.Messaging.WaContactResolver>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Services.IWhatsAppRealTimeNotifier,
                              CalibraHub.Web.Infrastructure.WhatsApp.SignalRWhatsAppNotifier>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Messaging.IMessageBus,
                              CalibraHub.Infrastructure.Messaging.InMemoryMessageBus>();
builder.Services.AddScoped<CalibraHub.Application.Services.Messaging.WhatsAppInboundProcessor>();
builder.Services.AddHostedService<CalibraHub.Application.Services.Messaging.WhatsAppInboxPollingService>();
builder.Services.AddHostedService<CalibraHub.Application.Workflow.WorkflowTimeoutEscalationJob>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Services.IMachineIdProvider,
                              CalibraHub.Infrastructure.Security.WindowsMachineIdProvider>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Services.INoteOcrService,
                              CalibraHub.Infrastructure.Security.WindowsNoteOcrService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.ILicenseRepository,
                           CalibraHub.Persistence.Repositories.SqlLicenseRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.ILicenseService,
                           CalibraHub.Application.Services.Licensing.LicenseService>();
// Rapor Motoru (IMemoryCache + per-company SQL)
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IReportSourceRepository,
                           CalibraHub.Persistence.Repositories.SqlReportSourceRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IReportDesignRepository,
                           CalibraHub.Persistence.Repositories.SqlReportDesignRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IReportQueryService,
                           CalibraHub.Infrastructure.Reporting.ReportQueryService>();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name        = ".CalibraHub.Session";
    options.Cookie.HttpOnly    = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite    = SameSiteMode.Strict;
    // Development'ta HTTP çalıştığı için SameAsRequest; production IIS/Nginx HTTPS termination yapar
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Application.Services.Scheduling.ReportSnapshotRefreshTaskExecutor>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IScheduledTaskExecutor,
                           CalibraHub.Application.Services.Scheduling.CurrencyRefreshTaskExecutor>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IEmailSender,
                           CalibraHub.Infrastructure.Notifications.SmtpEmailSender>();
// Mail sablonu HTML render — DocLayout (OutputFormat='email') -> HTML mail govdesi.
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Services.IMailTemplateRenderer,
                              CalibraHub.Application.Services.Email.MailTemplateRenderer>();
builder.Services.AddScoped<CalibraHub.Application.Services.Scheduling.IScheduledTaskDispatcher,
                           CalibraHub.Application.Services.Scheduling.ScheduledTaskDispatcher>();
builder.Services.AddScoped<IReportDataRepository, SqlReportDataRepository>();
builder.Services.AddSingleton<ZplGeneratorService>();
builder.Services.AddScoped<IAdminReadService, AdminReadService>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IUiConfigurationService, UiConfigurationService>();
builder.Services.AddScoped<ILogisticsConfigurationService, LogisticsConfigurationService>();
// Varlık Yönetimi (Asset Management)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IAssetService,
                           CalibraHub.Application.Services.AssetService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IAssetRepository,
                           CalibraHub.Persistence.Repositories.SqlAssetRepository>();
builder.Services.AddScoped<IFinanceService, FinanceService>();
builder.Services.AddScoped<IApprovalQueueService, ApprovalQueueService>();
builder.Services.AddSingleton<CalibraHub.Application.Services.OrgChartDomainService>();
builder.Services.AddSingleton<CalibraHub.Application.Services.ShopFloorLockoutTracker>();
builder.Services.AddSingleton<CalibraHub.Application.Services.LoginLockoutTracker>();

// Rate limiting — yalnizca [EnableRateLimiting("...")] ile isaretli hassas endpoint'lerde calisir.
// Global limiter bilincli olarak YOK: ERP UI'i (grid yukleme, paralel fetch) yuksek burst uretir.
// Not: Kestrel dogrudan dinliyor (61001) — RemoteIpAddress gercek istemci IP'sidir. Reverse proxy
// arkasina alinirsa ForwardedHeaders middleware'i eklenmeli, yoksa tum istemciler tek partition'a duser.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await ctx.HttpContext.Response.WriteAsync(
            "{\"ok\":false,\"error\":\"Çok fazla istek gönderildi. Lütfen biraz bekleyip yeniden deneyin.\"}", ct);
    };

    static string ClientIp(HttpContext ctx)
        => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    static RateLimitPartition<string> PerIpFixedWindow(HttpContext ctx, int permitPerMinute)
        => RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });

    // Kimliksiz auth yuzeyi: login POST + sirket listesi (e-posta enumeration'i da yavaslatir)
    limiter.AddPolicy("auth", ctx => PerIpFixedWindow(ctx, 20));

    // Anonim token'li public linkler (hizli onay, not paylasimi)
    limiter.AddPolicy("public-share", ctx => PerIpFixedWindow(ctx, 30));

    // Webhook — Meta Cloud API burst'lerine tolerans birak
    limiter.AddPolicy("webhook", ctx => PerIpFixedWindow(ctx, 300));

    // Pahali operasyonlar (AI cagrisi, Excel export) — girisli kullanici bazinda
    limiter.AddPolicy("expensive", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.Identity?.IsAuthenticated == true ? $"u:{ctx.User.Identity!.Name}" : $"ip:{ClientIp(ctx)}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IApprovalFlowService,
                           CalibraHub.Application.Services.ApprovalFlowService>();
builder.Services.AddScoped<CalibraHub.Application.Services.Approval.IApprovalNotificationDispatcher,
                           CalibraHub.Application.Services.Approval.ApprovalNotificationDispatcher>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IApprovalTokenRepository,
                           CalibraHub.Persistence.Repositories.SqlApprovalTokenRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.ICurrentCompanyProvider,
                           CalibraHub.Web.Services.HttpCurrentCompanyProvider>();
// Faz 4 — Runtime executor + Decision evaluator + Document context provider
builder.Services.AddScoped<CalibraHub.Application.Services.Approval.IApprovalDocumentContextProvider,
                           CalibraHub.Application.Services.Approval.ApprovalDocumentContextProvider>();
builder.Services.AddScoped<CalibraHub.Application.Services.Approval.IDecisionEvaluator,
                           CalibraHub.Application.Services.Approval.DecisionEvaluator>();
builder.Services.AddScoped<CalibraHub.Application.Services.Approval.IApprovalFlowExecutor,
                           CalibraHub.Application.Services.Approval.ApprovalFlowExecutor>();
// Entity-agnostic plugin registry — her IApprovalEntityType implementasyonu DI'a kayıt
// edilir, ApprovalEntityTypeRegistry IEnumerable<IApprovalEntityType> üzerinden toplar.
// Belgeler (9 tip: 1 wildcard "Document" + 8 spesifik): DocumentEntityTypes.AddDocumentEntityTypes()
// — paylaşılan field/parameter setiyle GenericDocumentApprovalEntityType instance'ları.
CalibraHub.Application.Approval.EntityTypes.DocumentEntityTypes.AddDocumentEntityTypes(builder.Services);
builder.Services.AddScoped<CalibraHub.Application.Approval.IApprovalEntityType,
                           CalibraHub.Application.Approval.EntityTypes.WorkOrderApprovalEntityType>();
builder.Services.AddScoped<CalibraHub.Application.Approval.IApprovalEntityType,
                           CalibraHub.Application.Approval.EntityTypes.ItemApprovalEntityType>();
builder.Services.AddScoped<CalibraHub.Application.Approval.IApprovalEntityType,
                           CalibraHub.Application.Approval.EntityTypes.ContactApprovalEntityType>();
builder.Services.AddScoped<CalibraHub.Application.Approval.IApprovalEntityType,
                           CalibraHub.Application.Approval.EntityTypes.ProductionRecordApprovalEntityType>();
// Document tipi IApprovalDocumentContextProvider'a bağımlı (Scoped); registry de Scoped olur.
builder.Services.AddScoped<CalibraHub.Application.Approval.IApprovalEntityTypeRegistry,
                           CalibraHub.Application.Approval.ApprovalEntityTypeRegistry>();
builder.Services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
builder.Services.AddScoped<IPasswordHashService, Pbkdf2PasswordHashService>();
builder.Services.AddScoped<IUiTextService, UiTextService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSignalR(options =>
{
    // Stale connection cleanup hızlandırma — default 30sn ServerTimeout +
    // 15sn KeepAlive idle tab'larda 1+ dakika beklerken connection state RAM tutar.
    // Client-side heartbeat 15sn (collaboration.js) → server 20sn'de yoksa disconnect.
    options.ClientTimeoutInterval     = TimeSpan.FromSeconds(20); // server-side: client'tan 20sn ping gelmezse drop
    options.KeepAliveInterval         = TimeSpan.FromSeconds(10); // server → client ping
    options.HandshakeTimeout          = TimeSpan.FromSeconds(5);
    options.MaximumReceiveMessageSize = 64 * 1024;
});
builder.Services.AddSingleton<CollaborationRuntimeStore>();
builder.Services.AddHostedService<CollaborationStartupLoadService>();
builder.Services.AddHostedService<CollaborationCleanupService>();

// Named HTTP clients — static HttpClient anti-pattern yerine IHttpClientFactory ile pool yonetilir
builder.Services.AddHttpClient("tcmb", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("integrator-reachability", c => c.Timeout = TimeSpan.FromSeconds(10));
// SOAP cagri timeout'u 5dk — Reachability postboxservice.svc icin (rapor §2.10 fix).
builder.Services.AddHttpClient(ReachabilityIntegratorDocumentClient.HttpClientName,
    c => c.Timeout = TimeSpan.FromSeconds(300));

// 2026-05-23 Yapay Zeka — Anthropic + Gemini için named HTTP clients (timeout 120sn
// stream cevaplari uzun olabilir). OpenAI ve Azure OpenAI kendi SDK'larında HttpClient
// yonetir, ayrica named client gerekmez.
builder.Services.AddHttpClient("ai-anthropic", c => c.Timeout = TimeSpan.FromSeconds(120));
builder.Services.AddHttpClient("ai-gemini",    c => c.Timeout = TimeSpan.FromSeconds(120));
// 2026-05-23 Ollama (lokal LLM) — 8B model ilk token icin 20+ sn surebilir, uzun timeout.
builder.Services.AddHttpClient("ai-ollama",    c => c.Timeout = TimeSpan.FromMinutes(5));

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
builder.Services.AddScoped<IContactPersonRepository, SqlContactPersonRepository>();
builder.Services.AddScoped<IContactPersonTitleRepository, SqlContactPersonTitleRepository>();
builder.Services.AddScoped<IMailSendBatchRepository, SqlMailSendBatchRepository>();
builder.Services.AddScoped<IDbSchemaRepository, SqlDbSchemaRepository>();
builder.Services.AddScoped<IDbSchemaService, DbSchemaService>();
builder.Services.AddScoped<ICardGroupRepository, SqlCardGroupRepository>();
builder.Services.AddScoped<ICollaborationLockRepository, SqlCollaborationLockRepository>();
builder.Services.AddScoped<IDesignTemplateRepository, SqlDesignTemplateRepository>();
builder.Services.AddScoped<IIntegrationApiProfileRepository, SqlIntegrationApiProfileRepository>();
builder.Services.AddScoped<IDocumentRepository, SqlDocumentRepository>();
builder.Services.AddScoped<IDocumentSourceRepository, SqlDocumentSourceRepository>();
builder.Services.AddScoped<IArgeProjectRepository, SqlArgeProjectRepository>();
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

// 2026-05-23 Yapay Zeka — repository + service registration.
// IAiClientFactory scoped (per-request) cunku IHttpClientFactory'den hot client cekiyor +
// IAiProviderRepository scoped (per-company DB).
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IAiProviderRepository,
                           CalibraHub.Persistence.Repositories.SqlAiProviderRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IAiUserKeyRepository,
                           CalibraHub.Persistence.Repositories.SqlAiUserKeyRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IAiClientFactory,
                           CalibraHub.Application.Services.Ai.AiClientFactory>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IAiProviderService,
                           CalibraHub.Application.Services.Ai.AiProviderService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IAiUserKeyService,
                           CalibraHub.Application.Services.Ai.AiUserKeyService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IAiChatService,
                           CalibraHub.Application.Services.Ai.AiChatService>();

// 2026-06-20 Şablon-tabanlı içe aktarım (AI'sız) — Cari pilotu.
// Repo + Service scoped (per-company DB + IFinanceService bağımlılığı); ExcelReader stateless singleton.
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IImportTemplateRepository,
                           CalibraHub.Persistence.Repositories.SqlImportTemplateRepository>();
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Services.IExcelReader,
                           CalibraHub.Infrastructure.Import.ExcelReader>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IImportService,
                           CalibraHub.Application.Services.ImportService>();
// İçe aktarım hedef-handler'ları (her entity bir handler; ImportService IEnumerable ile toplar).
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IImportTargetHandler,
                           CalibraHub.Application.Services.Import.ContactImportHandler>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IImportTargetHandler,
                           CalibraHub.Application.Services.Import.ItemImportHandler>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IImportTargetHandler,
                           CalibraHub.Application.Services.Import.ContactPersonImportHandler>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IImportTargetHandler,
                           CalibraHub.Application.Services.Import.PriceListImportHandler>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IImportTargetHandler,
                           CalibraHub.Application.Services.Import.BomImportHandler>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IImportTargetHandler,
                           CalibraHub.Application.Services.Import.InventoryCountImportHandler>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IImportTargetHandler,
                           CalibraHub.Application.Services.Import.RoutingImportHandler>();

// 2026-06-06 Yetkilendirme (F1) — PermissionDef + UserPermission repository + service.
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IPermissionDefRepository,
                           CalibraHub.Persistence.Repositories.SqlPermissionDefRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IPermissionGrantRepository,
                           CalibraHub.Persistence.Repositories.SqlPermissionGrantRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IPermissionGroupRepository,
                           CalibraHub.Persistence.Repositories.SqlPermissionGroupRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IPermissionService,
                           CalibraHub.Application.Services.Security.PermissionService>();
builder.Services.AddScoped<CalibraHub.Application.Services.Security.PermissionDefDiscoveryService>();
// 2026-06-12 Satır bazlı veri görünürlük kuralları (row-level security) repository (per-company DB).
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IDataVisibilityRuleRepository,
                           CalibraHub.Persistence.Repositories.SqlDataVisibilityRuleRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IDataVisibilityFilter,
                           CalibraHub.Persistence.Security.SqlDataVisibilityFilter>();
// Faz 0: FormCode sabitleri ile DB tutarlılık denetimi — startup'ta WARNING log üretir, hard-fail yok.
builder.Services.AddScoped<FormCodeValidator>();
// Faz 2 (2026-06-09): DB-driven menu servisi
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IMenuService,
                           CalibraHub.Application.Services.MenuService>();
// 2026-05-24 Calibo tool registry — read-only + write tool method'lari icin scoped service.
builder.Services.AddScoped<CalibraHub.Application.Services.Ai.Tools.CalibroTools>();
builder.Services.AddScoped<CalibraHub.Application.Services.Ai.Tools.CalibroContactTools>();
builder.Services.AddScoped<CalibraHub.Application.Services.Ai.Tools.CalibroItemTools>();
builder.Services.AddScoped<CalibraHub.Application.Services.Ai.Tools.CalibroDocumentTools>();
// 2026-05-24: Audit log — Calibo write tool denetim kaydi
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IAiToolInvocationRepository,
                           CalibraHub.Persistence.Repositories.SqlAiToolInvocationRepository>();
// 2026-05-24: Calibo dokuman okuyucu — xlsx/pdf/docx text extraction
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.Services.IDocumentTextExtractor,
                              CalibraHub.Infrastructure.DocumentExtraction.DocumentTextExtractor>();
// In-memory pending action cache (write tool onay flow'u icin) — IMemoryCache kaydı satır ~136'da
builder.Services.AddSingleton<CalibraHub.Application.Services.Ai.Tools.AiPendingActionStore>();
// Web tarafindan HttpContext'ten user context dolduran scoped impl
builder.Services.AddScoped<CalibraHub.Application.Services.Ai.Tools.ICalibroUserContext,
                           CalibraHub.Web.Services.HttpCalibroUserContext>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDocumentNumberService, SqlDocumentNumberService>();
builder.Services.AddScoped<IArgeProjectService, ArgeProjectService>();
builder.Services.AddScoped<IDocumentNumberRuleRepository, SqlDocumentNumberRuleRepository>();
// CodeRule — Cari/Stok kod türetme kuralları (Tasarım Kuralları altında 2 yeni tab)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.ICodeRuleRepository,
                           CalibraHub.Persistence.Repositories.SqlCodeRuleRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.ICodeGeneratorService,
                           CalibraHub.Persistence.Services.CodeRule.CodeGeneratorService>();
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
// 2026-05-26: HealthCheck schema probe (INSERT...ROLLBACK testi). Scope: per-request (tenant-aware connection).
builder.Services.AddScoped<CalibraHub.Web.Services.SchemaProbeService>();
// 2026-05-26: Onayda Bekleyenler ekrani
builder.Services.AddScoped<CalibraHub.Application.Services.IPendingApprovalAuthority,
    CalibraHub.Web.Services.HttpPendingApprovalAuthority>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IPendingApprovalService,
    CalibraHub.Application.Services.PendingApprovalService>();
// Eager warm-up: Lazy yerine startup'ta parse — CSV bulunamiyorsa veya parse hatasi
// varsa erken yakala (lazy ise ilk istegi atan kullaniciya geri donus)
builder.Services.AddScoped<IHttpExecutor, HttpExecutor>();
builder.Services.AddScoped<IBodySchemaResolver, BodySchemaResolver>();
builder.Services.AddScoped<IIntegrationRunner, IntegrationRunner>();
builder.Services.AddScoped<IIntegrationService, IntegrationService>();
// 2026-05-21 Faz 1: Entegrasyon JSON bundle ile dışa/içe aktarma
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IIntegrationBundleService,
                           CalibraHub.Application.Services.Integration.IntegrationBundleService>();
// 2026-05-22 Pre-flight Filter Engine — Queue + Runner + Manual button shared helper
builder.Services.AddSingleton<CalibraHub.Application.Services.Integration.IntegrationFilterEngine>();
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
// 2026-06-14: Ana Sayfa Panosu (Home Dashboard) — layout + widget veri toplama servisi.
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IDashboardService,
                           CalibraHub.Application.Services.DashboardService>();
// SQL View tabanli Rehber (Lookup / LOV) sistemi
builder.Services.AddScoped<IGuideRepository, SqlGuideRepository>();
builder.Services.AddScoped<IGuideService, GuideService>();

// Form Yöneticisi (dbo.Forms CRUD)
builder.Services.AddScoped<IFormRepository, SqlFormRepository>();

// Depo İşlemleri (Transfer + Ambar Giriş/Çıkış + Sayım taslak) — arka planda Document/DocumentLine
// ve InventoryCount/InventoryCountLine'a baglanir (2026-07-02 stok hareketi konsolidasyonu).
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IStockDocRepository,
                           CalibraHub.Persistence.Repositories.SqlStockDocRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IInventoryCountRepository,
                           CalibraHub.Persistence.Repositories.SqlInventoryCountRepository>();

// Sabit alan ayarlari (FldSet — rehber eslestirme)
builder.Services.AddScoped<IFieldSettingRepository, SqlFieldSettingRepository>();

// Onay akışı Karar (Decision) node SQL kütüphanesi — admin CRUD + designer validate/test.
// Service Persistence katmanında çünkü SqlServerConnectionFactory'ye ihtiyaç var
// (Application onu referans vermez — DocumentNumberService/PostProcedureExecutor ile aynı pattern).
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IApprovalSqlQueryRepository,
                           CalibraHub.Persistence.Repositories.SqlApprovalSqlQueryRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IApprovalSqlQueryService,
                           CalibraHub.Persistence.Repositories.ApprovalSqlQueryService>();

// Faz 0: Sirket Parametreleri + Numerator (sayac altyapisi)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.ICompanyParameterRepository,
    CalibraHub.Persistence.Repositories.SqlCompanyParameterRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.ICompanyParameterService,
    CalibraHub.Application.Services.CompanyParameterService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.INumeratorRepository,
    CalibraHub.Persistence.Repositories.SqlNumeratorRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.INumeratorService,
    CalibraHub.Application.Services.NumeratorService>();

// Ondalik ayarlari (form bazinda hane hassasiyeti — 2026-07-05)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IDecimalSettingRepository,
    CalibraHub.Persistence.Repositories.SqlDecimalSettingRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IDecimalSettingService,
    CalibraHub.Application.Services.DecimalSettingService>();

// Adres tanimlamalari — Ulke/Sehir/Ilce (Genel Tanimlamalar sayfasi — 2026-07-06)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IAddressDefinitionRepository,
    CalibraHub.Persistence.Repositories.SqlAddressDefinitionRepository>();
// Lokasyon tanimlamalari — Bolum/Alt Bolum (Genel Tanimlamalar ikinci grup — 2026-07-06)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.ILocationSectionRepository,
    CalibraHub.Persistence.Repositories.SqlLocationSectionRepository>();
// Malzeme karti Stok Hareketleri sekmesi — DocumentLine tabanli hareket ekstresi (2026-07-06)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IStockMovementQueryRepository,
    CalibraHub.Persistence.Repositories.SqlStockMovementQueryRepository>();

// Faz 1: Uretim Is Emri
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkOrderRepository,
    CalibraHub.Persistence.Repositories.SqlWorkOrderRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IWorkOrderService,
    CalibraHub.Application.Services.WorkOrderService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.ICalendarRepository,
    CalibraHub.Persistence.Repositories.SqlCalendarRepository>();
builder.Services.AddScoped<CalibraHub.Application.Services.Calendar.CalendarService>();

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

// 2026-05-20 — Faz 1 MVP: Saha aktivite log (Hazirlik/Uretim/MalzemeBekle/Ariza/...)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkOrderOperationActivityRepository,
    CalibraHub.Persistence.Repositories.SqlWorkOrderOperationActivityRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IWorkOrderOperationActivityService,
    CalibraHub.Application.Services.WorkOrderOperationActivityService>();

// 2026-05-21 — Faz 2: Aktivite sebep sozlugu (Ariza icin 'Sensor', Malzeme Bekle icin 'Tedarikci gec' vs.)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IActivityReasonRepository,
    CalibraHub.Persistence.Repositories.SqlActivityReasonRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IActivityReasonService,
    CalibraHub.Application.Services.ActivityReasonService>();

// 2026-05-21 — Faz 3: Vardiya tanimi + atama (haftalik tekrar pattern)
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IShiftRepository,
    CalibraHub.Persistence.Repositories.SqlShiftRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IShiftService,
    CalibraHub.Application.Services.ShiftService>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IShiftAssignmentRepository,
    CalibraHub.Persistence.Repositories.SqlShiftAssignmentRepository>();
builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IShiftAssignmentService,
    CalibraHub.Application.Services.ShiftAssignmentService>();

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
builder.Services.AddSingleton<CalibraHub.Application.Abstractions.DesignProvider.IDesignCriterion,
                              CalibraHub.Application.Services.DesignProvider.AccountTypeCriterion>();
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

// 2026-06-10 — Metadata-Driven Engine ARCHITECTURE REMOVED.
// Karar: küçük ekip (1-3 kişi) + canlı kopya yok + "hızlı ayağa kaldırma + stabilite"
// öncelikleri ile engine vizyonu rafa kaldırıldı. Customer özelleştirme ihtiyaçları
// mevcut WidgetMas EAV sistemi ile karşılanır. Detaylı kararlar için bkz. CLAUDE.md.

// Sprint 1 — Universal Form Engine: Domain entity property'lerini WidgetMas'a
// IsSystemField=true ile seed eden discovery servisi. Startup'ta sistem DB'sinde
// calistirilir (Item → ITEMS POC). Idempotent.
builder.Services.AddScoped<ISystemFieldDiscoveryService, SystemFieldDiscoveryService>();
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
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IApprovalFlowRepository,
                               CalibraHub.Persistence.Repositories.SqlApprovalFlowRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IApprovalInstanceRepository,
                               CalibraHub.Persistence.Repositories.SqlApprovalInstanceRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IApprovalNodeLogger,
                               CalibraHub.Persistence.Repositories.SqlApprovalNodeLogRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkflowDefinitionRepository,
                               CalibraHub.Persistence.Repositories.SqlWorkflowDefinitionRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Services.WorkflowDefinitionService>();
    builder.Services.AddScoped<CalibraHub.Application.Services.WorkflowTemplateService>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkflowInstanceRepository,
                               CalibraHub.Persistence.Repositories.SqlWorkflowInstanceRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.IDocumentContextBuilder,
                               CalibraHub.Application.Workflow.DocumentContextBuilder>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.IActorResolver,
                               CalibraHub.Application.Workflow.ActorResolver>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.OrgChartNCalcFunctions>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.WorkflowEngine>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IBpmFormRepository,
                               CalibraHub.Persistence.Repositories.SqlBpmFormRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Services.BpmFormService>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.BpmFormContextBuilder>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IAttachmentRepository,
                               CalibraHub.Persistence.Repositories.SqlAttachmentRepository>();
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
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IApprovalFlowRepository,
                               CalibraHub.Persistence.Repositories.SqlApprovalFlowRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IApprovalInstanceRepository,
                               CalibraHub.Persistence.Repositories.SqlApprovalInstanceRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Services.IApprovalNodeLogger,
                               CalibraHub.Persistence.Repositories.SqlApprovalNodeLogRepository>();
    builder.Services.AddScoped<IIntegratorImportLogRepository, SqlPltSystemLogRepository>();
    builder.Services.AddScoped<INoteRepository, SqlNoteRepository>();
    builder.Services.AddScoped<IUserNotificationRepository, SqlUserNotificationRepository>();
    builder.Services.AddScoped<IOrgChartRepository, SqlOrgChartRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkflowDefinitionRepository,
                               CalibraHub.Persistence.Repositories.SqlWorkflowDefinitionRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Services.WorkflowDefinitionService>();
    builder.Services.AddScoped<CalibraHub.Application.Services.WorkflowTemplateService>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IWorkflowInstanceRepository,
                               CalibraHub.Persistence.Repositories.SqlWorkflowInstanceRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.IDocumentContextBuilder,
                               CalibraHub.Application.Workflow.DocumentContextBuilder>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.IActorResolver,
                               CalibraHub.Application.Workflow.ActorResolver>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.OrgChartNCalcFunctions>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.WorkflowEngine>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IBpmFormRepository,
                               CalibraHub.Persistence.Repositories.SqlBpmFormRepository>();
    builder.Services.AddScoped<CalibraHub.Application.Services.BpmFormService>();
    builder.Services.AddScoped<CalibraHub.Application.Workflow.BpmFormContextBuilder>();
    builder.Services.AddScoped<CalibraHub.Application.Abstractions.Persistence.IAttachmentRepository,
                               CalibraHub.Persistence.Repositories.SqlAttachmentRepository>();
}

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.HttpOnly     = true;
    options.Cookie.SameSite     = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.HeaderName          = "RequestVerificationToken"; // JS fetch'ten header ile kabul et
});

// Oturum atalet backstop'u (sunucu tarafı) — appsettings Authentication:IdleMinutes (varsayılan 30).
// SlidingExpiration ile: her istek cookie'yi tazeler; bu süre boyunca hiç istek gelmezse
// cookie ölür → sonraki istek /Account/Login'e düşer (JS-kapalı / kapalı-tarayıcı senaryosu).
// Aktif kullanıcı Shell'in throttle'lı KeepAlive ping'i ile cookie'yi taze tutar. Per-company
// hassas idle (uyarı + kesin logout) client tarafında SECURITY/SESSION_IDLE_MINUTES ile uygulanır.
var authIdleMinutes = builder.Configuration.GetValue<int?>("Authentication:IdleMinutes") ?? 30;
if (authIdleMinutes < 5) authIdleMinutes = 5;   // aşırı düşük değerle aktif kullanıcıyı kilitleme
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath           = "/Account/Login";
        options.AccessDeniedPath    = "/Account/Login";
        options.SlidingExpiration   = true;
        options.ExpireTimeSpan      = TimeSpan.FromMinutes(authIdleMinutes);
        options.Cookie.HttpOnly     = true;
        options.Cookie.SameSite     = SameSiteMode.Strict;
        // Development HTTP → SameAsRequest; production HTTPS termination → Secure gönderilir
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ──────────────────────────────────────────────────────────────────────────
var mvcBuilder = builder.Services.AddControllersWithViews(opts =>
{
    // 2026-06-07 — Global izin kontrolü. Controller veya action [PermissionScope("...")]
    // attribute taşıyorsa filter çalışır; yoksa atlar (geriye dönük güvenli — opt-in).
    opts.Filters.Add<CalibraHub.Web.Authorization.PermissionEnforcementFilter>();
    // 2026-06-27 — Tüm unsafe HTTP method'ları (POST/PUT/DELETE/PATCH) için CSRF token zorunlu.
    // [IgnoreAntiforgeryToken] ile muaf tutulabilir (webhook/setup endpoint'leri).
    opts.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});
mvcBuilder.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    // Enum'lari string olarak hem deserialize hem serialize et — frontend bunlari
    // "AnyUser", "Department" gibi string degerlerle gonderiyor; numerik enum
    // deserialization (STJ default) bu durumda tum payload'u null'a bind ediyor.
    options.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter(
            namingPolicy: null, allowIntegerValues: true));
    // SQL Server DATETIME → ADO.NET Kind=Unspecified → JS new Date() local time sanıyor.
    // Tüm DateTime değerlerini UTC olarak serialize et (Z suffix'i ekle).
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
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

        // Sprint 1 — Universal Form Engine: Item entity → ITEMS form sistem alan'lari seed.
        // Idempotent; mevcut IsSystemField satirlari atlanir. Hata olursa server engellenmez.
        try
        {
            var sysFieldDiscovery = scope.ServiceProvider.GetRequiredService<ISystemFieldDiscoveryService>();
            var seeded = await sysFieldDiscovery.DiscoverAndSeedAsync(CancellationToken.None);
            if (seeded > 0)
                app.Logger.LogInformation("[SysField Discovery] {Count} sistem alani widget olarak seed edildi", seeded);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "[SysField Discovery] Startup discovery basarisiz — Item.* alanlari widget olarak gorulmeyecek");
        }

        // 2026-06-06 — Yetkilendirme F2: dbo.Forms'taki her form için 6 standart action
        // PermissionDef tablosuna upsert. Idempotent — yeni form eklenirse otomatik catalog'a girer.
        try
        {
            var permDiscovery = scope.ServiceProvider
                .GetRequiredService<CalibraHub.Application.Services.Security.PermissionDefDiscoveryService>();
            var seededDefs = await permDiscovery.DiscoverAndSeedAsync(CancellationToken.None);
            if (seededDefs > 0)
                app.Logger.LogInformation("[Perm Discovery] {Count} izin tanımı seed edildi", seededDefs);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "[Perm Discovery] Startup seed basarisiz — yetki kontrolleri default deny ile çalışır");
        }

        // Faz 0: FormCode sabitleri ↔ dbo.Forms tutarlılık denetimi.
        // WARNING log üretir — hard-fail yok, production'ı engellemez.
        try
        {
            var formCodeValidator = scope.ServiceProvider.GetRequiredService<FormCodeValidator>();
            await formCodeValidator.ValidateAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "[FormCodeValidator] Startup doğrulaması başarısız — atlandı");
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

            // 2026-07-05: snake_case → PascalCase tablo+kolon migration (document_types, currencies vb.)
            try
            {
                await dbInitForCompanies.MigrateSchemaForConnectionAsync(
                    c.DatabaseConnectionString, CancellationToken.None);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex,
                    "[Schema Migration] Sirket {CompanyId} icin tablo/kolon migration basarisiz", c.Id);
            }

            // Notes tablosu kolon migrasyonu — linked_entity_*, visibility vb.
            try
            {
                await dbInitForCompanies.EnsureNotesSchemaForConnectionAsync(
                    c.DatabaseConnectionString, CancellationToken.None);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex,
                    "[Notes Schema] Sirket {CompanyId} icin notes kolon migrasyonu basarisiz", c.Id);
            }

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

// Güvenlik başlıkları — pipeline başında kayıt, tüm yanıtlara (302 redirect dahil) uygulanır
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var h = context.Response.Headers;
        // Clickjacking — SAMEORIGIN: uygulama içi iframe'ler (ViewPayload A4 önizleme vb.) çalışır
        h["X-Frame-Options"] = "SAMEORIGIN";
        // MIME-type sniffing engelle
        h["X-Content-Type-Options"] = "nosniff";
        // Cross-origin isteklerde tam URL sızdırmayı engelle
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        // Gereksiz tarayıcı API'larını kısıtla
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        // HTML yanıtları için cache başlıkları
        if (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            h["Cache-Control"] = "no-store, no-cache, must-revalidate";
            h["Pragma"] = "no-cache";
        }
        return Task.CompletedTask;
    });
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // 2026-07-05: Dosya-hash versiyonlu istekler (?v=<hash> — asp-append-version /
        // FileVersioner) icerik degisince URL de degistigi icin guvenle uzun sure
        // cache'lenir. Onceki global no-cache, 5MB React bundle'ini her sayfa
        // acilisinda 304 roundtrip'ine zorluyordu (recete "alanlar sonradan geliyor"
        // gecikmesinin bilesenlerinden biri). Versiyonsuz istekler no-cache kalir.
        if (ctx.Context.Request.Query.ContainsKey("v"))
            ctx.Context.Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        else
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
    }
});

app.UseRouting();
app.UseSession();  // GateController'in Session'a erisimi icin
app.UseAuthentication();
app.UseAuthorization();

// Auth'tan SONRA: "expensive" policy'si kullanici kimligine gore partition'lar,
// bu yuzden HttpContext.User dolmus olmali.
app.UseRateLimiter();

// CORS — Authentication'dan sonra ki cookie-based credentials akabilsin.
// Sadece [EnableCors("MobileApi")] olan endpoint'lerde devreye girer.
app.UseCors();
app.UseMiddleware<WorkspaceRedirectPreservationMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var h = context.Response.Headers;

        // Clickjacking — SAMEORIGIN: uygulama içi iframe'ler (ViewPayload A4 önizleme vb.) çalışır
        h["X-Frame-Options"] = "SAMEORIGIN";
        // MIME-type sniffing engelle (tarayıcı, Content-Type başlığına uysun)
        h["X-Content-Type-Options"] = "nosniff";
        // Cross-origin isteklerde yalnızca origin gönder, tam URL sızdırma
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        // Gereksiz tarayıcı API'larını kısıtla
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";

        // HTML yanıtları için cache başlıkları
        if (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            h["Cache-Control"] = "no-store, no-cache, must-revalidate";
            h["Pragma"] = "no-cache";
        }

        return Task.CompletedTask;
    });
    await next();
});

app.MapControllers();   // attribute routing ([Route], [HttpGet] vb.) için gerekli
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<CollaborationHub>("/hubs/collaboration");
app.MapHub<CalibraHub.Web.Hubs.WhatsAppHub>("/hubs/whatsapp");

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
