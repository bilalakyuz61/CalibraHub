using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Services;
using CalibraHub.Infrastructure.Integrations;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Repositories;
using CalibraHub.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
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

        var databaseOptions = new CalibraDatabaseOptions
        {
            ConnectionString = DecryptIfNeeded(configuration[$"{CalibraDatabaseOptions.SectionName}:ConnectionString"] ?? string.Empty),
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

        if (useMockIntegratorClient)
        {
            services.AddSingleton<IIntegratorDocumentClient>(_ => new MockIntegratorDocumentClient(simulatedDocumentsPerPull));
        }
        else
        {
            services.AddSingleton<IIntegratorDocumentClient, ReachabilityIntegratorDocumentClient>();
        }

        services.AddSingleton<InMemoryDataStore>();
        services.AddSingleton(databaseOptions);
        services.AddSingleton(bootstrapAdminOptions);
        services.AddSingleton<SqlServerConnectionFactory>();
        services.AddScoped<IDocumentImportService, DocumentImportService>();
        services.AddScoped<IAdminReadService, AdminReadService>();
        services.AddScoped<IAdminManagementService, AdminManagementService>();
        services.AddScoped<IApprovalQueueService, ApprovalQueueService>();
        services.AddScoped<IPasswordHashService, Pbkdf2PasswordHashService>();
        services.AddScoped<CalibraDatabaseInitializer>();
        services.AddScoped<IIntegratorSettingsRepository, SqlIntegratorSettingsRepository>();
        services.AddScoped<ISmtpProfileRepository, SqlSmtpProfileRepository>();
        services.AddScoped<IErpConnectionSettingsRepository, SqlErpConnectionSettingsRepository>();
        services.AddScoped<ICompanyDefinitionRepository, SqlCompanyDefinitionRepository>();
        services.AddScoped<IDepartmentRepository, SqlDepartmentRepository>();
        services.AddScoped<IUserProfileRepository, SqlUserProfileRepository>();
        services.AddScoped<IIncomingDocumentRepository, SqlIncomingDocumentRepository>();
        services.AddScoped<IIntegratorImportLogRepository, SqlPltSystemLogRepository>();
        services.AddScoped<INoteRepository, SqlNoteRepository>();
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
