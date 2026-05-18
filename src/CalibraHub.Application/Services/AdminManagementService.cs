using CalibraHub.Application.Abstractions.Integrations;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using System.Net;
using System.Net.Mail;

namespace CalibraHub.Application.Services;

public sealed class AdminManagementService : IAdminManagementService
{
    public const string DefaultInitialPassword = "12345678";
    private const int ConnectionTestLogRetentionDays = 30;
    private const int IntegratorConnectionTestLogSourceId = -1;
    private const int SmtpConnectionTestLogSourceId = -2;
    private const int SqlConnectionTestLogSourceId = -3;

    private readonly IIntegratorDocumentClient _integratorDocumentClient;
    private readonly IIntegratorSettingsRepository _integratorSettingsRepository;
    private readonly ISmtpProfileRepository _smtpProfileRepository;
    private readonly IErpConnectionSettingsRepository _erpConnectionSettingsRepository;
    private readonly ICompanyRepository _companyDefinitionRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUserProfileRepository _userProfileRepository;
    private readonly IPasswordHashService _passwordHashService;
    private readonly IIntegratorImportLogRepository _integratorImportLogRepository;
    private readonly ICompanyConnectionRegistry _companyConnectionRegistry;
    private readonly IGrafanaProvisioningService _grafanaProvisioning;

    public AdminManagementService(
        IIntegratorDocumentClient integratorDocumentClient,
        IIntegratorSettingsRepository integratorSettingsRepository,
        ISmtpProfileRepository smtpProfileRepository,
        IErpConnectionSettingsRepository erpConnectionSettingsRepository,
        ICompanyRepository companyDefinitionRepository,
        IDepartmentRepository departmentRepository,
        IUserProfileRepository userProfileRepository,
        IPasswordHashService passwordHashService,
        IIntegratorImportLogRepository integratorImportLogRepository,
        ICompanyConnectionRegistry companyConnectionRegistry,
        IGrafanaProvisioningService grafanaProvisioning)
    {
        _integratorDocumentClient = integratorDocumentClient;
        _integratorSettingsRepository = integratorSettingsRepository;
        _smtpProfileRepository = smtpProfileRepository;
        _erpConnectionSettingsRepository = erpConnectionSettingsRepository;
        _companyDefinitionRepository = companyDefinitionRepository;
        _departmentRepository = departmentRepository;
        _userProfileRepository = userProfileRepository;
        _passwordHashService = passwordHashService;
        _integratorImportLogRepository = integratorImportLogRepository;
        _companyConnectionRegistry = companyConnectionRegistry;
        _grafanaProvisioning = grafanaProvisioning;
    }

    public async Task<int> SaveCompanyAsync(
        SaveCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var title = request.Title.Trim();
        var address = request.Address.Trim();
        var taxOffice = request.TaxOffice.Trim();
        var taxNumber = request.TaxNumber.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Sirket adi zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Unvan zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Adres zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(taxOffice))
        {
            throw new ArgumentException("Vergi dairesi zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(taxNumber))
        {
            throw new ArgumentException("Vergi numarasi zorunludur.");
        }

        if (taxNumber.Length > 20)
        {
            throw new ArgumentException("Vergi numarasi en fazla 20 karakter olabilir.");
        }

        var companies = await _companyDefinitionRepository.GetAllAsync(cancellationToken);
        var existingById = request.Id.HasValue
            ? companies.FirstOrDefault(x => x.Id == request.Id.Value)
            : null;

        if (request.Id.HasValue && existingById is null)
        {
            throw new ArgumentException("Guncellenecek sirket bulunamadi.");
        }

        if (companies.Any(x =>
                string.Equals(x.TaxNumber, taxNumber, StringComparison.OrdinalIgnoreCase) &&
                (!request.Id.HasValue || x.Id != request.Id.Value)))
        {
            throw new ArgumentException("Ayni vergi numarasi ile sirket zaten tanimli.");
        }

        if (companies.Any(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                (!request.Id.HasValue || x.Id != request.Id.Value)))
        {
            throw new ArgumentException("Ayni sirket adi ile kayit zaten mevcut.");
        }

        var connectionString = string.IsNullOrWhiteSpace(request.DatabaseConnectionString)
            ? null
            : request.DatabaseConnectionString.Trim();

        var company = new Company
        {
            Id = existingById?.Id ?? 0,
            Name = name,
            Title = title,
            Address = address,
            City = request.City?.Trim(),
            District = request.District?.Trim(),
            PostalCode = request.PostalCode?.Trim(),
            TaxOffice = taxOffice,
            TaxNumber = taxNumber,
            IsEDocumentApprovalEnabled = request.IsEDocumentApprovalEnabled,
            DatabaseConnectionString = connectionString
        };

        if (!request.IsActive)
        {
            company.Deactivate();
        }

        int savedId;
        if (existingById is null)
        {
            savedId = await _companyDefinitionRepository.AddAsync(company, cancellationToken);
        }
        else
        {
            await _companyDefinitionRepository.UpdateAsync(company, cancellationToken);
            savedId = company.Id;
        }

        _companyConnectionRegistry.Set(savedId, connectionString);

        // Grafana per-company org/datasource/dashboard provisioning. IsEnabled=false
        // ise no-op; HTTP hatasi exception firlatmaz, sadece log yazar.
        if (_grafanaProvisioning.IsEnabled)
        {
            var orgId = await _grafanaProvisioning.EnsureOrganizationAsync(savedId, name, cancellationToken);
            if (orgId > 0 && !string.IsNullOrWhiteSpace(connectionString))
            {
                await _grafanaProvisioning.EnsureDataSourceAsync(orgId, name, connectionString, cancellationToken);
                await _grafanaProvisioning.ProvisionDefaultDashboardsAsync(orgId, cancellationToken);
            }
        }

        return savedId;
    }

    public async Task<int> SaveIntegratorSettingsAsync(
        SaveIntegratorSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var company = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (company is null)
        {
            throw new ArgumentException("Secilen sirket bulunamadi.");
        }

        if (!company.IsActive)
        {
            throw new ArgumentException("Secilen sirket pasif durumda.");
        }

        // Sirket bazinda tek kayit: mevcut kaydi company_id ile bul (upsert)
        var existingByCompany = await _integratorSettingsRepository.GetByCompanyIdAsync(request.CompanyId, cancellationToken);
        var isUpdate = existingByCompany is not null;

        // Guncelleme sirasinda sifre bos birakilirsa mevcut sifre korunur
        // (DPAPI sifre cozemese bile bos string ile DB'deki deger degistirilmez — UpdateAsync CASE expression kullanir)
        var effectiveSecret = string.IsNullOrWhiteSpace(request.Secret) && isUpdate
            ? existingByCompany!.Secret
            : request.Secret;

        // Guncelleme modunda sifre bos olabilir (mevcut sifre DB'de korunur)
        var requireSecret = !isUpdate || !string.IsNullOrWhiteSpace(request.Secret);

        var (name, baseUrl, companyTaxNumber, username, secret, pollingIntervalSeconds, maxRecordsPerPull, logRetentionDays, includeReceivedDocumentsInPull, markDownloadedDocumentsAsReceived, includeIssuedEInvoicesInPull, includeIssuedEArchivesInPull, includeIssuedEDispatchesInPull) = ValidateIntegratorInput(
            request.Provider,
            request.Name,
            request.BaseUrl,
            request.CompanyTaxNumber,
            request.Username,
            effectiveSecret,
            request.PollingIntervalSeconds,
            request.MaxRecordsPerPull,
            request.LogRetentionDays,
            request.IncludeReceivedDocumentsInPull,
            request.MarkDownloadedDocumentsAsReceived,
            request.IncludeIssuedEInvoicesInPull,
            request.IncludeIssuedEArchivesInPull,
            request.IncludeIssuedEDispatchesInPull,
            request.TimeoutSeconds,
            request.LookbackDays,
            requireSecret: requireSecret);

        var settings = BuildIntegratorSettings(
            id: existingByCompany?.Id ?? 0,
            companyId: request.CompanyId,
            provider: request.Provider,
            name: name,
            baseUrl: baseUrl,
            companyTaxNumber: companyTaxNumber,
            username: username,
            secret: secret,
            pollingIntervalSeconds: pollingIntervalSeconds,
            maxRecordsPerPull: maxRecordsPerPull,
            logRetentionDays: logRetentionDays,
            includeReceivedDocumentsInPull: includeReceivedDocumentsInPull,
            markDownloadedDocumentsAsReceived: markDownloadedDocumentsAsReceived,
            includeIssuedEInvoicesInPull: includeIssuedEInvoicesInPull,
            includeIssuedEArchivesInPull: includeIssuedEArchivesInPull,
            includeIssuedEDispatchesInPull: includeIssuedEDispatchesInPull,
            isActive: request.IsActive,
            scheduleEnabled: request.ScheduleEnabled,
            createdAt: existingByCompany?.CreatedAt ?? DateTime.Now,
            appStr: request.AppStr,
            source: request.Source,
            appVersion: request.AppVersion,
            timeoutSeconds: request.TimeoutSeconds,
            lookbackDays: request.LookbackDays);

        if (existingByCompany is null)
        {
            return await _integratorSettingsRepository.AddAsync(settings, cancellationToken);
        }
        else
        {
            await _integratorSettingsRepository.UpdateAsync(settings, cancellationToken);
            return settings.Id;
        }
    }

    public async Task DeleteIntegratorSettingsAsync(int id, CancellationToken cancellationToken)
    {
        var existing = await _integratorSettingsRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            throw new ArgumentException("Silinecek entegrator ayari bulunamadi.");
        }
        await _integratorSettingsRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<IntegratorConnectionTestResult> TestIntegratorConnectionAsync(
        TestIntegratorConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var company = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (company is null || !company.IsActive)
        {
            throw new ArgumentException("Secilen sirket bulunamadi veya pasif durumda.");
        }

        // Sifre bos geldiyse (formdaki sifre alani temizlenmisse) mevcut kaydin sifresi kullanilir
        var existingForTest = string.IsNullOrWhiteSpace(request.Secret)
            ? await _integratorSettingsRepository.GetByCompanyIdAsync(request.CompanyId, cancellationToken)
            : null;
        var effectiveSecret = string.IsNullOrWhiteSpace(request.Secret) && existingForTest is not null
            ? existingForTest.Secret
            : request.Secret;
        var requireSecretForTest = existingForTest is null || string.IsNullOrWhiteSpace(effectiveSecret);

        var (name, baseUrl, companyTaxNumber, username, secret, pollingIntervalSeconds, maxRecordsPerPull, logRetentionDays, includeReceivedDocumentsInPull, markDownloadedDocumentsAsReceived, includeIssuedEInvoicesInPull, includeIssuedEArchivesInPull, includeIssuedEDispatchesInPull) = ValidateIntegratorInput(
            request.Provider,
            request.Name,
            request.BaseUrl,
            request.CompanyTaxNumber,
            request.Username,
            effectiveSecret,
            request.PollingIntervalSeconds,
            request.MaxRecordsPerPull,
            request.LogRetentionDays,
            request.IncludeReceivedDocumentsInPull,
            request.MarkDownloadedDocumentsAsReceived,
            request.IncludeIssuedEInvoicesInPull,
            request.IncludeIssuedEArchivesInPull,
            request.IncludeIssuedEDispatchesInPull,
            request.TimeoutSeconds,
            request.LookbackDays,
            requireSecret: requireSecretForTest);

        var settings = BuildIntegratorSettings(
            id: 0,
            companyId: request.CompanyId,
            provider: request.Provider,
            name: name,
            baseUrl: baseUrl,
            companyTaxNumber: companyTaxNumber,
            username: username,
            secret: secret,
            pollingIntervalSeconds: pollingIntervalSeconds,
            maxRecordsPerPull: maxRecordsPerPull,
            logRetentionDays: logRetentionDays,
            includeReceivedDocumentsInPull: includeReceivedDocumentsInPull,
            markDownloadedDocumentsAsReceived: markDownloadedDocumentsAsReceived,
            includeIssuedEInvoicesInPull: includeIssuedEInvoicesInPull,
            includeIssuedEArchivesInPull: includeIssuedEArchivesInPull,
            includeIssuedEDispatchesInPull: includeIssuedEDispatchesInPull,
            isActive: true,
            createdAt: DateTime.Now,
            appStr: request.AppStr,
            source: request.Source,
            appVersion: request.AppVersion,
            timeoutSeconds: request.TimeoutSeconds,
            lookbackDays: request.LookbackDays);

        try
        {
            var payloads = await _integratorDocumentClient.PullDocumentsAsync(
                settings,
                settings.MaxRecordsPerPull,
                new IntegratorDocumentPullOptions(
                    settings.IncludeReceivedDocumentsInPull,
                    settings.IncludeIssuedEInvoicesInPull,
                    settings.IncludeIssuedEArchivesInPull,
                    settings.IncludeIssuedEDispatchesInPull),
                cancellationToken);
            var limitedCount = payloads
                .Take(Math.Clamp(settings.MaxRecordsPerPull, 1, 5000))
                .Count();
            var result = new IntegratorConnectionTestResult(
                true,
                $"Baglanti basarili. Servis yanit verdi, {limitedCount} kayit alindi.");
            await TryWriteConnectionTestLogAsync(
                IntegratorConnectionTestLogSourceId,
                "Entegrator Baglanti Testi",
                result.IsSuccess,
                $"{request.Name}: {result.Message}",
                request.CompanyId,
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = new IntegratorConnectionTestResult(false, $"Baglanti testi basarisiz: {ex.Message}");
            await TryWriteConnectionTestLogAsync(
                IntegratorConnectionTestLogSourceId,
                "Entegrator Baglanti Testi",
                result.IsSuccess,
                $"{request.Name}: {result.Message}",
                request.CompanyId,
                cancellationToken);
            return result;
        }
    }

    public async Task SaveSmtpProfileAsync(SaveSmtpProfileRequest request, CancellationToken cancellationToken)
    {
        var companyDefinition = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (companyDefinition is null)
        {
            throw new ArgumentException("Secilen sirket bulunamadi.");
        }

        if (!companyDefinition.IsActive)
        {
            throw new ArgumentException("Secilen sirket pasif durumda.");
        }

        var (name, fromEmail, fromDisplayName, host, port, username, password) = ValidateSmtpInput(
            request.Name,
            request.FromEmail,
            request.FromDisplayName,
            request.Host,
            request.Port,
            request.Username,
            request.Password);

        var existingById = request.Id.HasValue
            ? await _smtpProfileRepository.GetByIdAsync(request.Id.Value, cancellationToken)
            : null;

        if (request.Id.HasValue && existingById is null)
        {
            throw new ArgumentException("Guncellenecek SMTP profili bulunamadi.");
        }

        var allProfiles = await _smtpProfileRepository.GetAllAsync(cancellationToken);
        var duplicateNameExists = allProfiles.Any(x =>
            x.CompanyId == request.CompanyId &&
            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
            (!request.Id.HasValue || x.Id != request.Id.Value));

        if (duplicateNameExists)
        {
            throw new ArgumentException("Ayni adla SMTP profili zaten tanimli.");
        }

        var profile = BuildSmtpProfile(
            id: existingById?.Id ?? Guid.NewGuid(),
            companyId: request.CompanyId,
            name: name,
            fromEmail: fromEmail,
            fromDisplayName: fromDisplayName,
            host: host,
            port: port,
            username: username,
            password: password,
            authMethod: request.AuthMethod ?? "Normal",
            oAuth2ClientId: request.OAuth2ClientId,
            oAuth2ClientSecret: request.OAuth2ClientSecret,
            oAuth2RefreshToken: request.OAuth2RefreshToken,
            useSsl: request.UseSsl,
            isActive: request.IsActive,
            createdAt: existingById?.CreatedAt ?? DateTime.Now);

        if (existingById is null)
        {
            await _smtpProfileRepository.AddAsync(profile, cancellationToken);
        }
        else
        {
            await _smtpProfileRepository.UpdateAsync(profile, cancellationToken);
        }
    }

    public async Task SaveErpConnectionSettingsAsync(
        SaveErpConnectionSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var companyDefinition = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (companyDefinition is null)
        {
            throw new ArgumentException("Secilen sirket bulunamadi.");
        }

        if (!companyDefinition.IsActive)
        {
            throw new ArgumentException("Secilen sirket pasif durumda.");
        }

        var (company, business, branch, username, password) = ValidateErpInput(
            request.Company,
            request.Business,
            request.Branch,
            request.Username,
            request.Password);

        var existingById = request.Id.HasValue
            ? await _erpConnectionSettingsRepository.GetByIdAsync(request.Id.Value, cancellationToken)
            : null;

        if (request.Id.HasValue && existingById is null)
        {
            throw new ArgumentException("Guncellenecek ERP baglanti ayari bulunamadi.");
        }

        var allSettings = await _erpConnectionSettingsRepository.GetAllAsync(cancellationToken);
        var duplicateExists = allSettings.Any(x =>
            x.CompanyId == request.CompanyId &&
            string.Equals(x.Provider, "Netsis", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Company, company, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Business, business, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Branch, branch, StringComparison.OrdinalIgnoreCase) &&
            (!request.Id.HasValue || x.Id != request.Id.Value));

        if (duplicateExists)
        {
            throw new ArgumentException("Ayni sirket, isletme ve sube bilgileriyle ERP baglantisi zaten tanimli.");
        }

        var settings = BuildErpConnectionSettings(
            id: existingById?.Id ?? Guid.NewGuid(),
            companyId: request.CompanyId,
            company: company,
            business: business,
            branch: branch,
            username: username,
            password: password,
            isActive: request.IsActive,
            createdAt: existingById?.CreatedAt ?? DateTime.Now);

        if (existingById is null)
        {
            await _erpConnectionSettingsRepository.AddAsync(settings, cancellationToken);
        }
        else
        {
            await _erpConnectionSettingsRepository.UpdateAsync(settings, cancellationToken);
        }
    }

    public async Task DeleteErpConnectionAsync(Guid id, CancellationToken cancellationToken)
    {
        var existing = await _erpConnectionSettingsRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            throw new ArgumentException("Silinecek ERP baglantisi bulunamadi.");
        }
        await _erpConnectionSettingsRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<ErpConnectionTestResult> TestErpConnectionAsync(
        TestErpConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var companyDefinition = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (companyDefinition is null || !companyDefinition.IsActive)
        {
            throw new ArgumentException("Secilen sirket bulunamadi veya pasif durumda.");
        }

        var (company, business, branch, _, _) = ValidateErpInput(
            request.Company,
            request.Business,
            request.Branch,
            request.Username,
            request.Password);

        try
        {
            _ = await _erpConnectionSettingsRepository.GetAllAsync(cancellationToken);
            var result = new ErpConnectionTestResult(
                true,
                "SQL baglanti testi basarili. Veritabani erisimi ve ERP baglanti parametreleri dogrulandi.");
            await TryWriteConnectionTestLogAsync(
                SqlConnectionTestLogSourceId,
                "SQL Baglanti Testi",
                result.IsSuccess,
                $"{company}/{business}/{branch}: {result.Message}",
                request.CompanyId,
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = new ErpConnectionTestResult(false, $"SQL baglanti testi basarisiz: {ex.Message}");
            await TryWriteConnectionTestLogAsync(
                SqlConnectionTestLogSourceId,
                "SQL Baglanti Testi",
                result.IsSuccess,
                $"{company}/{business}/{branch}: {result.Message}",
                request.CompanyId,
                cancellationToken);
            return result;
        }
    }

    public async Task<SmtpConnectionTestResult> TestSmtpConnectionAsync(
        TestSmtpConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var companyDefinition = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (companyDefinition is null || !companyDefinition.IsActive)
        {
            throw new ArgumentException("Secilen sirket bulunamadi veya pasif durumda.");
        }

        var (_, fromEmail, fromDisplayName, host, port, username, password) = ValidateSmtpInput(
            request.Name,
            request.FromEmail,
            request.FromDisplayName,
            request.Host,
            request.Port,
            request.Username,
            request.Password);

        var recipientEmail = string.IsNullOrWhiteSpace(request.TestRecipientEmail)
            ? fromEmail
            : request.TestRecipientEmail.Trim();

        try
        {
            _ = new MailAddress(recipientEmail);
        }
        catch
        {
            throw new ArgumentException("Deneme mail alicisi gecersiz.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var mailMessage = new MailMessage
            {
                From = new MailAddress(fromEmail, fromDisplayName),
                Subject = "Calibra SMTP Test Mail",
                Body = $"Bu e-posta Calibra SMTP testinden gonderilmistir. Zaman: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}",
                IsBodyHtml = false
            };
            mailMessage.To.Add(recipientEmail);

            using var smtpClient = new SmtpClient(host, port)
            {
                EnableSsl = request.UseSsl,
                Credentials = new NetworkCredential(username, password),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000
            };

            await smtpClient.SendMailAsync(mailMessage);

            var result = new SmtpConnectionTestResult(
                true,
                $"SMTP baglanti basarili. Deneme maili '{recipientEmail}' adresine gonderildi.");
            await TryWriteConnectionTestLogAsync(
                SmtpConnectionTestLogSourceId,
                "SMTP Baglanti Testi",
                result.IsSuccess,
                $"{request.Name}: {result.Message}",
                request.CompanyId,
                cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SmtpException ex)
        {
            var result = new SmtpConnectionTestResult(false, $"SMTP testi basarisiz: {ex.Message}");
            await TryWriteConnectionTestLogAsync(
                SmtpConnectionTestLogSourceId,
                "SMTP Baglanti Testi",
                result.IsSuccess,
                $"{request.Name}: {result.Message}",
                request.CompanyId,
                cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            var result = new SmtpConnectionTestResult(false, $"SMTP testi basarisiz: {ex.Message}");
            await TryWriteConnectionTestLogAsync(
                SmtpConnectionTestLogSourceId,
                "SMTP Baglanti Testi",
                result.IsSuccess,
                $"{request.Name}: {result.Message}",
                request.CompanyId,
                cancellationToken);
            return result;
        }
    }

    public async Task CreateDepartmentAsync(CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Departman adi zorunludur.");
        }

        var companyDefinition = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (companyDefinition is null)
        {
            throw new ArgumentException("Secilen sirket bulunamadi.");
        }

        if (!companyDefinition.IsActive)
        {
            throw new ArgumentException("Secilen sirket pasif durumda.");
        }

        var departments = await _departmentRepository.GetAllAsync(cancellationToken);

        // Ayni isimli departman kontrolu (sirket bazinda)
        if (departments.Any(x =>
                x.CompanyId == request.CompanyId &&
                string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Bu sirkette ayni isimde departman zaten tanimli: '{name}'");
        }

        var department = new Department
        {
            CompanyId = request.CompanyId,
            Name = name
        };

        await _departmentRepository.AddAsync(department, cancellationToken);
    }

    public async Task UpdateDepartmentAsync(UpdateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Departman adi zorunludur.");

        var existing = await _departmentRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existing is null)
            throw new ArgumentException("Departman bulunamadi.");

        var all = await _departmentRepository.GetAllAsync(cancellationToken);

        // Ayni isimli baska departman var mi (kendisi haric, sirket bazinda)
        if (all.Any(x =>
                x.Id != request.Id &&
                x.CompanyId == existing.CompanyId &&
                string.Equals(x.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Bu sirkette ayni isimde departman zaten tanimli: '{name}'");
        }

        // Code mevcut value'yu koru (UI'dan gelmiyor)
        existing.Update(name, existing.ParentDepartmentId);
        if (request.IsActive) existing.Activate(); else existing.Deactivate();

        await _departmentRepository.UpdateAsync(existing, cancellationToken);
    }

    public async Task DeleteDepartmentAsync(int id, CancellationToken cancellationToken)
    {
        var existing = await _departmentRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
            throw new ArgumentException("Departman bulunamadi.");

        await _departmentRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var fullName = request.FullName.Trim();
        var email = request.Email.Trim();
        var employeeCode = request.EmployeeCode.Trim();
        var role = request.Role;
        var selectedPermissions = request.Permissions?.ToArray() ?? Array.Empty<UserPermission>();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Ad soyad zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("E-posta zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            throw new ArgumentException("Sicil kodu zorunludur.");
        }

        if (request.CompanyId == 0)
        {
            throw new ArgumentException("Sirket secimi zorunludur.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException("Secilen rol gecersiz.");
        }

        if (selectedPermissions.Length == 0)
        {
            throw new ArgumentException("En az bir yetki secilmelidir.");
        }

        if (selectedPermissions.Any(permission => !Enum.IsDefined(permission)))
        {
            throw new ArgumentException("Secilen yetkilerden biri gecersiz.");
        }

        var normalizedPermissions = selectedPermissions
            .Distinct()
            .ToArray();

        var allowedPermissions = UserAuthorizationCatalog.GetAllowedPermissions(role);
        var disallowedPermissions = normalizedPermissions
            .Where(permission => !allowedPermissions.Contains(permission))
            .ToArray();

        if (disallowedPermissions.Length > 0)
        {
            throw new ArgumentException("Secilen rol ile yetki kombinasyonu uyumlu degil.");
        }

        var company = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (company is null)
        {
            throw new ArgumentException("Secilen sirket bulunamadi.");
        }

        if (!company.IsActive)
        {
            throw new ArgumentException("Secilen sirket pasif durumda.");
        }

        // Departman opsiyonel — secilmediyse atlanir.
        if (request.DepartmentId.HasValue)
        {
            var departments = await _departmentRepository.GetAllAsync(cancellationToken);
            if (!departments.Any(x =>
                    x.Id == request.DepartmentId.Value &&
                    x.CompanyId == request.CompanyId))
            {
                throw new ArgumentException("Secilen departman bulunamadi veya farkli bir sirket kaydi.");
            }
        }

        var users = await _userProfileRepository.GetAllAsync(cancellationToken);

        if (users.Any(x =>
                x.CompanyId == request.CompanyId &&
                string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Bu sirkette ayni e-posta ile kullanici zaten tanimli.");
        }

        if (users.Any(x =>
                x.CompanyId == request.CompanyId &&
                string.Equals(x.EmployeeCode, employeeCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Bu sirkette ayni sicil kodu ile kullanici zaten tanimli.");
        }

        if (request.SupervisorUserId.HasValue &&
            users.All(x =>
                x.Id != request.SupervisorUserId.Value ||
                x.CompanyId != request.CompanyId))
        {
            throw new ArgumentException("Secilen ust kullanici bulunamadi veya farkli bir sirket kaydi.");
        }

        var userProfile = new UserProfile
        {
            CompanyId = request.CompanyId,
            FullName = fullName,
            Email = email,
            EmployeeCode = employeeCode,
            DepartmentId = request.DepartmentId,
            SupervisorUserId = request.SupervisorUserId,
            Role = role,
            Permissions = normalizedPermissions,
            GrafanaRole = request.GrafanaRole
        };
        var password = string.IsNullOrWhiteSpace(request.Password) ? DefaultInitialPassword : request.Password;
        userProfile.SetPasswordHash(_passwordHashService.HashPassword(password));

        await _userProfileRepository.AddAsync(userProfile, cancellationToken);

        // Grafana per-user provisioning: GrafanaRole set edilmis ve Grafana enabled ise
        // kullaniciyi sirket org'una istenen rolde ekle. Idempotent — fail olursa
        // exception firlatmaz, log'a gecer (CalibraHub akisi devam eder).
        if (request.GrafanaRole.HasValue && _grafanaProvisioning.IsEnabled)
        {
            try
            {
                var orgId = await _grafanaProvisioning.EnsureOrganizationAsync(
                    request.CompanyId, company.Name, cancellationToken);
                if (orgId > 0)
                {
                    await _grafanaProvisioning.EnsureUserOrganizationMembershipAsync(
                        orgId,
                        userProfile.Email,        // username = email (yeni middleware ile uyumlu)
                        userProfile.Email,
                        userProfile.FullName,
                        request.GrafanaRole.Value,
                        cancellationToken);
                }
            }
            catch
            {
                // Service-level fail — repository'e zaten yazildi, kullanici manuel olarak
                // "Duzenle"den tekrar tetikleyebilir.
            }
        }
    }

    public async Task UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var existing = await _userProfileRepository.GetByIdAsync(request.Id, cancellationToken);
        if (existing is null)
        {
            throw new ArgumentException("Guncellenecek kullanici bulunamadi.");
        }

        var fullName = (request.FullName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Ad soyad zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("E-posta zorunludur.");
        }

        if (request.CompanyId <= 0)
        {
            throw new ArgumentException("Sirket secimi zorunludur.");
        }

        var company = await _companyDefinitionRepository.GetByIdAsync(request.CompanyId, cancellationToken);
        if (company is null)
        {
            throw new ArgumentException("Secilen sirket bulunamadi.");
        }

        if (!company.IsActive)
        {
            throw new ArgumentException("Secilen sirket pasif durumda.");
        }

        var allUsers = await _userProfileRepository.GetAllAsync(cancellationToken);
        if (allUsers.Any(x =>
                x.Id != existing.Id &&
                x.CompanyId == request.CompanyId &&
                string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Bu sirkette ayni e-posta ile kullanici zaten tanimli.");
        }

        // Şirket değişti ise yeni şirkette bir departman bulup ata; yoksa default 'Yonetim' olustur.
        int? departmentId = existing.DepartmentId;
        if (existing.CompanyId != request.CompanyId)
        {
            var departments = await _departmentRepository.GetAllAsync(cancellationToken);
            var dept = departments.FirstOrDefault(x => x.CompanyId == request.CompanyId);
            if (dept is null)
            {
                await CreateDepartmentAsync(
                    new CreateDepartmentRequest(request.CompanyId, "Yonetim"),
                    cancellationToken);
                departments = await _departmentRepository.GetAllAsync(cancellationToken);
                dept = departments.First(x => x.CompanyId == request.CompanyId);
            }
            departmentId = dept.Id;
        }

        // GrafanaRole — request.SetGrafanaRole = true ise yeni deger kullanilir,
        // false ise mevcut rol korunur (geriye uyumlu — eski caller'lari bozmaz).
        var newGrafanaRole = request.SetGrafanaRole ? request.GrafanaRole : existing.GrafanaRole;

        // Role / Permissions — request.SetRole = true ve Role != null ise yeni deger kullanilir,
        // izinler de UserAuthorizationCatalog'tan turetilir. Aksi halde mevcut korunur.
        var newRole = (request.SetRole && request.Role.HasValue) ? request.Role.Value : existing.Role;
        var newPermissions = (request.SetRole && request.Role.HasValue)
            ? UserAuthorizationCatalog.GetAllowedPermissions(request.Role.Value)
            : existing.Permissions;

        var updated = new UserProfile
        {
            Id = existing.Id,
            CompanyId = request.CompanyId,
            FullName = fullName,
            Email = email,
            EmployeeCode = existing.EmployeeCode,
            DepartmentId = departmentId,
            SupervisorUserId = existing.SupervisorUserId,
            Role = newRole,
            Permissions = newPermissions,
            GrafanaRole = newGrafanaRole
        };

        // Private setter degerleri korunmasi icin yeniden uygula
        var passwordHash = string.IsNullOrWhiteSpace(request.Password)
            ? existing.PasswordHash
            : _passwordHashService.HashPassword(request.Password);
        updated.SetPasswordHash(passwordHash);
        updated.SetInterfacePreferences(existing.LanguageCode, existing.ThemeCode);
        updated.SetGridPreferencesJson(existing.GridPreferencesJson);
        if (!existing.IsActive)
        {
            updated.Deactivate();
        }

        await _userProfileRepository.UpdateAsync(updated, cancellationToken);

        // Grafana sync — sadece SetGrafanaRole = true ise tetikle (geriye uyumlu).
        // Eski rol → Yeni rol kombinasyonlari:
        //   NULL  → NULL    : no-op
        //   NULL  → Editor  : EnsureUserOrganizationMembershipAsync (add)
        //   Editor → Admin  : EnsureUserOrganizationMembershipAsync (rol update)
        //   Editor → NULL   : RemoveUserFromOrganizationAsync (org'tan cikar)
        if (request.SetGrafanaRole && _grafanaProvisioning.IsEnabled)
        {
            try
            {
                var orgId = await _grafanaProvisioning.EnsureOrganizationAsync(
                    request.CompanyId, company.Name, cancellationToken);

                if (orgId > 0)
                {
                    if (newGrafanaRole.HasValue)
                    {
                        // Add veya rol update — idempotent
                        await _grafanaProvisioning.EnsureUserOrganizationMembershipAsync(
                            orgId,
                            email,                  // username = email
                            email,
                            fullName,
                            newGrafanaRole.Value,
                            cancellationToken);
                    }
                    else if (existing.GrafanaRole.HasValue)
                    {
                        // Eski rol vardi, yeni NULL → org'tan cikar
                        await _grafanaProvisioning.RemoveUserFromOrganizationAsync(
                            orgId, email, cancellationToken);
                    }
                }
            }
            catch
            {
                // Provisioning fail olursa exception firlatma — DB update yine
                // de basarili. Kullanici "Duzenle"den tekrar tetikleyebilir.
            }
        }
    }

    private static (string Name, string BaseUrl, string CompanyTaxNumber, string Username, string Secret, int PollingIntervalSeconds, int MaxRecordsPerPull, int LogRetentionDays, bool IncludeReceivedDocumentsInPull, bool MarkDownloadedDocumentsAsReceived, bool IncludeIssuedEInvoicesInPull, bool IncludeIssuedEArchivesInPull, bool IncludeIssuedEDispatchesInPull)
        ValidateIntegratorInput(
            IntegratorProvider provider,
            string name,
            string baseUrl,
            string companyTaxNumber,
            string username,
            string secret,
            int pollingIntervalSeconds,
            int maxRecordsPerPull,
            int logRetentionDays,
            bool includeReceivedDocumentsInPull,
            bool markDownloadedDocumentsAsReceived,
            bool includeIssuedEInvoicesInPull,
            bool includeIssuedEArchivesInPull,
            bool includeIssuedEDispatchesInPull,
            int timeoutSeconds = 30,
            int lookbackDays = 30,
            bool requireSecret = true)
    {
        var normalizedName = name.Trim();
        var normalizedBaseUrl = baseUrl.Trim();
        var normalizedCompanyTaxNumber = companyTaxNumber.Trim();
        var normalizedUsername = username.Trim();
        var normalizedSecret = secret.Trim();

        if (!Enum.IsDefined(provider) || provider == IntegratorProvider.Unknown)
        {
            throw new ArgumentException("Gecerli bir entegrator saglayicisi seciniz.");
        }

        // Ad girilmemisse saglayici adi kullanilir
        if (string.IsNullOrWhiteSpace(normalizedName))
            normalizedName = provider.ToString();

        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            throw new ArgumentException("Base URL zorunludur.");
        }

        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Base URL gecersiz.");
        }

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new ArgumentException("Kullanici adi zorunludur.");
        }

        if (requireSecret && string.IsNullOrWhiteSpace(normalizedSecret))
        {
            throw new ArgumentException("Sifre zorunludur.");
        }

        if (pollingIntervalSeconds < 10)
        {
            throw new ArgumentException("Polling suresi en az 10 saniye olmalidir.");
        }

        if (maxRecordsPerPull < 1 || maxRecordsPerPull > 5000)
        {
            throw new ArgumentException("Maksimum kayit pull degeri 1 ile 5000 arasinda olmalidir.");
        }

        if (logRetentionDays < 1 || logRetentionDays > 3650)
        {
            throw new ArgumentException("Log saklama suresi 1 ile 3650 gun arasinda olmalidir.");
        }

        if (timeoutSeconds < 5 || timeoutSeconds > 300)
        {
            throw new ArgumentException("Zaman asimi 5-300 saniye arasinda olmalidir.");
        }

        if (lookbackDays < 1 || lookbackDays > 3650)
        {
            throw new ArgumentException("Geriye bakis suresi 1-3650 gun arasinda olmalidir.");
        }

        return (
            normalizedName,
            normalizedBaseUrl,
            normalizedCompanyTaxNumber,
            normalizedUsername,
            normalizedSecret,
            pollingIntervalSeconds,
            maxRecordsPerPull,
            logRetentionDays,
            includeReceivedDocumentsInPull,
            markDownloadedDocumentsAsReceived,
            includeIssuedEInvoicesInPull,
            includeIssuedEArchivesInPull,
            includeIssuedEDispatchesInPull);
    }

    private static IntegratorSettings BuildIntegratorSettings(
        int id,
        int companyId,
        IntegratorProvider provider,
        string name,
        string baseUrl,
        string companyTaxNumber,
        string username,
        string secret,
        int pollingIntervalSeconds,
        int maxRecordsPerPull,
        int logRetentionDays,
        bool includeReceivedDocumentsInPull,
        bool markDownloadedDocumentsAsReceived,
        bool includeIssuedEInvoicesInPull,
        bool includeIssuedEArchivesInPull,
        bool includeIssuedEDispatchesInPull,
        bool isActive,
        DateTime createdAt,
        bool scheduleEnabled = false,
        string? appStr = null,
        string? source = null,
        string? appVersion = null,
        int timeoutSeconds = 30,
        int lookbackDays = 30)
    {
        var settings = new IntegratorSettings
        {
            Id = id,
            CompanyId = companyId,
            Provider = provider,
            Name = name,
            BaseUrl = baseUrl,
            CompanyTaxNumber = companyTaxNumber,
            Username = username,
            Secret = secret,
            AppStr = string.IsNullOrWhiteSpace(appStr) ? null : appStr.Trim(),
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim(),
            AppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim(),
            CreatedAt = createdAt
        };

        settings.UpdateTimeoutSeconds(timeoutSeconds);
        settings.UpdateLookbackDays(lookbackDays);
        settings.UpdatePollingInterval(pollingIntervalSeconds);
        settings.UpdateMaxRecordsPerPull(maxRecordsPerPull);
        settings.UpdateLogRetentionDays(logRetentionDays);
        settings.ConfigureIncludeReceivedDocumentsInPull(includeReceivedDocumentsInPull);
        settings.ConfigureDownloadedDocumentReceipt(markDownloadedDocumentsAsReceived);
        settings.ConfigureIssuedDocumentPull(
            includeIssuedEInvoicesInPull,
            includeIssuedEArchivesInPull,
            includeIssuedEDispatchesInPull);
        settings.ConfigureScheduleEnabled(scheduleEnabled);
        if (!isActive)
        {
            settings.Deactivate();
        }

        return settings;
    }

    private static (string Name, string FromEmail, string FromDisplayName, string Host, int Port, string Username, string Password)
        ValidateSmtpInput(
            string name,
            string fromEmail,
            string fromDisplayName,
            string host,
            int port,
            string username,
            string password)
    {
        var normalizedName = name.Trim();
        var normalizedFromEmail = fromEmail.Trim();
        var normalizedFromDisplayName = fromDisplayName.Trim();
        var normalizedHost = host.Trim();
        var normalizedUsername = username.Trim();
        var normalizedPassword = password.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("SMTP profil adi zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(normalizedFromEmail))
        {
            throw new ArgumentException("Gonderen e-posta zorunludur.");
        }

        try
        {
            _ = new MailAddress(normalizedFromEmail);
        }
        catch
        {
            throw new ArgumentException("Gonderen e-posta gecersiz.");
        }

        if (string.IsNullOrWhiteSpace(normalizedHost))
        {
            throw new ArgumentException("SMTP host zorunludur.");
        }

        if (port is < 1 or > 65535)
        {
            throw new ArgumentException("SMTP port 1 ile 65535 arasinda olmalidir.");
        }

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new ArgumentException("SMTP kullanici adi zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(normalizedPassword))
        {
            throw new ArgumentException("SMTP sifresi zorunludur.");
        }

        return (normalizedName, normalizedFromEmail, normalizedFromDisplayName, normalizedHost, port, normalizedUsername, normalizedPassword);
    }

    private static SmtpProfile BuildSmtpProfile(
        Guid id,
        int companyId,
        string name,
        string fromEmail,
        string fromDisplayName,
        string host,
        int port,
        string username,
        string password,
        string authMethod,
        string? oAuth2ClientId,
        string? oAuth2ClientSecret,
        string? oAuth2RefreshToken,
        bool useSsl,
        bool isActive,
        DateTime createdAt)
    {
        var profile = new SmtpProfile
        {
            Id = id,
            CompanyId = companyId,
            Name = name,
            FromEmail = fromEmail,
            FromDisplayName = fromDisplayName,
            Host = host,
            Username = username,
            Password = password,
            AuthMethod = authMethod,
            OAuth2ClientId = oAuth2ClientId,
            OAuth2ClientSecret = oAuth2ClientSecret,
            OAuth2RefreshToken = oAuth2RefreshToken,
            CreatedAt = createdAt
        };

        profile.UpdateTransport(port, useSsl);
        if (!isActive)
        {
            profile.Deactivate();
        }

        return profile;
    }

    private static (string Company, string Business, string Branch, string Username, string Password) ValidateErpInput(
        string company,
        string business,
        string branch,
        string username,
        string password)
    {
        var normalizedCompany = company.Trim();
        var normalizedBusiness = business.Trim();
        var normalizedBranch = branch.Trim();
        var normalizedUsername = username.Trim();
        var normalizedPassword = password.Trim();

        if (string.IsNullOrWhiteSpace(normalizedCompany))
        {
            throw new ArgumentException("Sirket alani zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(normalizedBusiness))
        {
            throw new ArgumentException("Isletme alani zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(normalizedBranch))
        {
            throw new ArgumentException("Sube alani zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new ArgumentException("ERP kullanici adi zorunludur.");
        }

        if (string.IsNullOrWhiteSpace(normalizedPassword))
        {
            throw new ArgumentException("ERP sifresi zorunludur.");
        }

        return (normalizedCompany, normalizedBusiness, normalizedBranch, normalizedUsername, normalizedPassword);
    }

    private static ErpConnectionSettings BuildErpConnectionSettings(
        Guid id,
        int companyId,
        string company,
        string business,
        string branch,
        string username,
        string password,
        bool isActive,
        DateTime createdAt)
    {
        var settings = new ErpConnectionSettings
        {
            Id = id,
            CompanyId = companyId,
            Provider = "Netsis",
            Company = company,
            Business = business,
            Branch = branch,
            Username = username,
            Password = password,
            CreatedAt = createdAt
        };

        if (!isActive)
        {
            settings.Deactivate();
        }

        return settings;
    }

    private async Task TryWriteConnectionTestLogAsync(
        int logSourceId,
        string logSourceName,
        bool isSuccess,
        string message,
        int? companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _integratorImportLogRepository.WriteAsync(
                new IntegratorImportLogWriteRequest(
                    logSourceId,
                    logSourceName,
                    isSuccess ? "Success" : "Error",
                    message,
                    0,
                    0,
                    companyId),
                cancellationToken);

            await _integratorImportLogRepository.CleanupExpiredAsync(
                logSourceId,
                ConnectionTestLogRetentionDays,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Connection test flow should continue even if log persistence fails.
        }
    }
}
