using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Ui;
using CalibraHub.Domain.Enums;
using CalibraHub.Web.Infrastructure.Collaboration;
using CalibraHub.Web.Models.Admin;
using CalibraHub.Web.Models.Shared;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace CalibraHub.Web.Controllers;

[Authorize]
public sealed class AdminController : Controller
{
    private readonly IAdminReadService _adminReadService;
    private readonly IAdminManagementService _adminManagementService;
    private readonly IUiConfigurationService _uiConfigurationService;
    private readonly ILogisticsConfigurationService _logisticsConfigurationService;
    private readonly IDocumentImportService _documentImportService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IIntegrationApiProfileRepository _apiProfileRepo;
    private readonly IScheduledTaskRepository _scheduledTaskRepo;
    private readonly ICompanyParameterService _companyParameters;
    private readonly IFormRepository _formRepository;
    private readonly CollaborationRuntimeStore _collaborationStore;
    private static readonly JsonSerializerOptions ProjectRequestJsonOptions = new()
    {
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions BoardConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public AdminController(
        IAdminReadService adminReadService,
        IAdminManagementService adminManagementService,
        IUiConfigurationService uiConfigurationService,
        ILogisticsConfigurationService logisticsConfigurationService,
        IDocumentImportService documentImportService,
        IWebHostEnvironment webHostEnvironment,
        IIntegrationApiProfileRepository apiProfileRepo,
        IScheduledTaskRepository scheduledTaskRepo,
        ICompanyParameterService companyParameters,
        IFormRepository formRepository,
        CollaborationRuntimeStore collaborationStore)
    {
        _adminReadService = adminReadService;
        _adminManagementService = adminManagementService;
        _uiConfigurationService = uiConfigurationService;
        _logisticsConfigurationService = logisticsConfigurationService;
        _documentImportService = documentImportService;
        _webHostEnvironment = webHostEnvironment;
        _apiProfileRepo = apiProfileRepo;
        _scheduledTaskRepo = scheduledTaskRepo;
        _companyParameters = companyParameters;
        _formRepository = formRepository;
        _collaborationStore = collaborationStore;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        SetActiveMenu("dashboard");
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        return View(snapshot);
    }

    [HttpGet]
    public IActionResult Settings()
    {
        return RedirectToAction(nameof(Index));
    }

    // NOT: Parameters + SaveGeneralParametersJson + ParametersList + SaveParameter + DeleteParameter + GeneralParametersInput + DeleteCompanyParameterRequest ParametersController'a tasindi (rapor 2.3 split).

    // NOT: ScheduledTask* cluster (10 endpoint + BuildScheduledTasksBoardConfig/Type/Status helper'lar) ScheduledTaskController'a tasindi (rapor 2.3 AdminController split).

    /// <summary>
    /// ShellPreview — React tabanli yeni nesil kabuk (Navbar + Sidebar + Tabs +
    /// Status Bar) mockup'i. Sadece UI preview amacli; uretim akisi degisikligi yok.
    /// Gezinme: /Admin/ShellPreview
    /// </summary>
    [HttpGet]
    public IActionResult ShellPreview()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Appearance(
        string? formKey,
        string? languageCode,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("appearance");
        var viewModel = await BuildAppearanceSettingsViewModelAsync(
            formKey,
            languageCode,
            null,
            null,
            cancellationToken);
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAppearancePreferences(
        AppearanceSettingsViewModel model,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("appearance");

        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Kullanici bilgisi bulunamadi.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildAppearanceSettingsViewModelAsync(
                model.SelectedFormKey,
                model.SelectedLanguageCode,
                model.LanguageCode,
                model.ThemeCode,
                cancellationToken);
            return View(nameof(Appearance), invalidModel);
        }

        try
        {
            await _uiConfigurationService.SaveUserPreferenceAsync(
                new SaveUserInterfacePreferenceRequest(
                    userId!.Value,
                    model.LanguageCode,
                    model.ThemeCode),
                cancellationToken);

            TempData["AdminSuccess"] = "Dil ve tema tercihleriniz kaydedildi.";
            return RedirectToAction(
                nameof(Appearance),
                new
                {
                    formKey = model.SelectedFormKey,
                    languageCode = model.SelectedLanguageCode
                });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildAppearanceSettingsViewModelAsync(
                model.SelectedFormKey,
                model.SelectedLanguageCode,
                model.LanguageCode,
                model.ThemeCode,
                cancellationToken);
            return View(nameof(Appearance), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFormLabels(
        AppearanceSettingsViewModel model,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("appearance");

        if (model.Labels is null || model.Labels.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Kaydedilecek etiket bulunamadi.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildAppearanceSettingsViewModelAsync(
                model.SelectedFormKey,
                model.SelectedLanguageCode,
                model.LanguageCode,
                model.ThemeCode,
                cancellationToken);
            return View(nameof(Appearance), invalidModel);
        }

        try
        {
            await _uiConfigurationService.SaveLabelTranslationsAsync(
                new SaveUiLabelTranslationsRequest(
                    model.SelectedFormKey,
                    model.SelectedLanguageCode,
                    (model.Labels ?? [])
                        .Select(x => new SaveUiLabelTranslationEntryRequest(x.LabelKey, x.LabelText))
                        .ToArray()),
                cancellationToken);

            TempData["AdminSuccess"] = "Form etiketleri kaydedildi.";
            return RedirectToAction(
                nameof(Appearance),
                new
                {
                    formKey = model.SelectedFormKey,
                    languageCode = model.SelectedLanguageCode
                });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildAppearanceSettingsViewModelAsync(
                model.SelectedFormKey,
                model.SelectedLanguageCode,
                model.LanguageCode,
                model.ThemeCode,
                cancellationToken);
            return View(nameof(Appearance), invalidModel);
        }
    }

    // NOT: WhatsApp/* cluster (Save, Test, SendTest, PairingCode, Qr) WhatsAppAdminController'a tasindi (rapor 2.3 split).

    // NOT: CompanySettings GET+POST CompanySettingsController'a tasindi (rapor 2.3 split).

    [HttpGet]
    public async Task<IActionResult> Logs(
        string? searchTerm,
        string? level,
        int? companyId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("system-logs");
        var viewModel = await BuildSystemLogsViewModelAsync(searchTerm, level, companyId, page, pageSize, cancellationToken);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> ProjectInstructions(CancellationToken cancellationToken)
    {
        SetActiveMenu("project-instructions");
        var viewModel = await BuildProjectInstructionsViewModelAsync(cancellationToken);
        return View(viewModel);
    }

    // NOT: FormManagement + FormsBoardConfig + DeleteFormJson + FormEdit + BuildFormsBoardConfigAsync/BuildFormWidgets helper FormManagementController'a tasindi (rapor 2.3 split).

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveProjectInstructions(
        ProjectInstructionsViewModel model,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("project-instructions");

        var normalizedContent = model.Content ?? string.Empty;
        var filePath = ResolveProjectInstructionsPath();

        await System.IO.File.WriteAllTextAsync(filePath, normalizedContent, new UTF8Encoding(false), cancellationToken);

        TempData["AdminSuccess"] = "Talimat dosyasi kaydedildi.";
        return RedirectToAction(nameof(ProjectInstructions));
    }

    [HttpGet]
    public async Task<IActionResult> IntegratorSettings(
        int? id,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("integrator-settings");
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);

        IntegratorSettingsInput input;
        if (id.HasValue)
        {
            var selected = snapshot.Integrators.FirstOrDefault(x => x.Id == id.Value);
            input = selected is null
                ? new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                }
                : new IntegratorSettingsInput
                {
                    Id = selected.Id,
                    CompanyId = selected.CompanyId,
                    Provider = selected.Provider,
                    Name = selected.Name,
                    BaseUrl = selected.BaseUrl,
                    CompanyTaxNumber = selected.CompanyTaxNumber,
                    Username = selected.Username,
                    Secret = selected.Secret,
                    PollingIntervalSeconds = selected.PollingIntervalSeconds,
                    MaxRecordsPerPull = selected.MaxRecordsPerPull,
                    LogRetentionDays = selected.LogRetentionDays,
                    IncludeReceivedDocumentsInPull = selected.IncludeReceivedDocumentsInPull,
                    MarkDownloadedDocumentsAsReceived = selected.MarkDownloadedDocumentsAsReceived,
                    IncludeIssuedEInvoicesInPull = selected.IncludeIssuedEInvoicesInPull,
                    IncludeIssuedEArchivesInPull = selected.IncludeIssuedEArchivesInPull,
                    IncludeIssuedEDispatchesInPull = selected.IncludeIssuedEDispatchesInPull,
                    IsActive = selected.IsActive,
                    AppStr = selected.AppStr,
                    Source = selected.Source,
                    AppVersion = selected.AppVersion
                };
        }
        else
        {
            input = new IntegratorSettingsInput
            {
                Provider = IntegratorProvider.Logo.ToString()
            };
        }

        var viewModel = await BuildIntegratorSettingsViewModelAsync(
            input,
            new SmtpProfileInput(),
            new ErpConnectionInput(),
            "admin-integrators",
            page,
            pageSize,
            cancellationToken);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> MailSettings(
        Guid? smtpId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("mail-settings");
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);

        var input = new IntegratorSettingsInput
        {
            Provider = IntegratorProvider.Logo.ToString()
        };

        SmtpProfileInput smtpInput;
        if (smtpId.HasValue)
        {
            var selectedSmtpProfile = snapshot.SmtpProfiles.FirstOrDefault(x => x.Id == smtpId.Value);
            smtpInput = selectedSmtpProfile is null
                ? new SmtpProfileInput()
                : new SmtpProfileInput
                {
                    Id = selectedSmtpProfile.Id,
                    CompanyId = selectedSmtpProfile.CompanyId,
                    Name = selectedSmtpProfile.Name,
                    FromEmail = selectedSmtpProfile.FromEmail,
                    FromDisplayName = selectedSmtpProfile.FromDisplayName,
                    Host = selectedSmtpProfile.Host,
                    Port = selectedSmtpProfile.Port,
                    Username = selectedSmtpProfile.Username,
                    Password = selectedSmtpProfile.Password,
                    TestRecipientEmail = selectedSmtpProfile.FromEmail,
                    UseSsl = selectedSmtpProfile.UseSsl,
                    IsActive = selectedSmtpProfile.IsActive
                };
        }
        else
        {
            smtpInput = new SmtpProfileInput();
        }

        var viewModel = await BuildIntegratorSettingsViewModelAsync(
            input,
            smtpInput,
            new ErpConnectionInput(),
            "admin-smtp-profiles",
            page,
            pageSize,
            cancellationToken);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> ErpSettings(
        Guid? erpId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("erp-settings");
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);

        var input = new IntegratorSettingsInput
        {
            Provider = IntegratorProvider.Logo.ToString()
        };

        var smtpInput = new SmtpProfileInput();
        ErpConnectionInput erpInput;

        if (erpId.HasValue)
        {
            var selectedErp = snapshot.ErpConnections.FirstOrDefault(x => x.Id == erpId.Value);
            erpInput = selectedErp is null
                ? new ErpConnectionInput()
                : new ErpConnectionInput
                {
                    Id = selectedErp.Id,
                    CompanyId = selectedErp.CompanyId,
                    Provider = selectedErp.Provider,
                    Company = selectedErp.Company,
                    Business = selectedErp.Business,
                    Branch = selectedErp.Branch,
                    Username = selectedErp.Username,
                    Password = selectedErp.Password,
                    IsActive = selectedErp.IsActive
                };
        }
        else
        {
            erpInput = new ErpConnectionInput();
        }

        var viewModel = await BuildIntegratorSettingsViewModelAsync(
            input,
            smtpInput,
            erpInput,
            "admin-erp-connections",
            page,
            pageSize,
            cancellationToken);
        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> ViewSettings(
        string? screenCode,
        Guid? groupId,
        bool openGroupModal,
        Guid? fieldId,
        CancellationToken cancellationToken)
    {
        // Iframe/tarayici cache Razor degisikligini yansitmiyor — no-cache set et.
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        SetActiveMenu("view-settings-screens");
        ViewData["OpenMaterialGroupModal"] = openGroupModal;
        var viewModel = await BuildScreenDesignSettingsPageViewModelAsync(
            screenCode,
            groupId,
            fieldId,
            cancellationToken);
        return View(nameof(ViewSettings), viewModel);
    }

    [HttpGet]
    public IActionResult MaterialCardViewSettings(
        Guid? groupId,
        bool openGroupModal,
        Guid? fieldId)
    {
        return RedirectToAction(
            nameof(ViewSettings),
            new
            {
                screenCode = ScreenDesignCatalog.MaterialCardsScreenCode,
                groupId,
                openGroupModal,
                fieldId
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFieldGroup(
        MaterialCardViewSettingsViewModel model,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("view-settings-screens");

        // Grup kaydederken FieldInput alanlari gonderilmez — React'in
        // saveGroup akisi sadece GroupInput.* gonderir. FieldInput.FieldKey,
        // FieldLabel, DataType [Required] oldugu icin ModelState hatali
        // oluyor ve save hic gerceklesmiyordu. Bu entry'leri temizliyoruz.
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("FieldInput.", StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(key);
        }

        if (!ModelState.IsValid)
        {
            var invalidDesigner = await BuildMaterialCardViewSettingsViewModelAsync(
                model.GroupInput.GroupId,
                model.FieldInput.FieldId,
                cancellationToken);
            invalidDesigner.GroupInput = model.GroupInput;
            invalidDesigner.FieldInput = model.FieldInput;
            var pageModel = await BuildScreenDesignSettingsPageViewModelAsync(
                ScreenDesignCatalog.MaterialCardsScreenCode,
                model.GroupInput.GroupId,
                model.FieldInput.FieldId,
                cancellationToken);
            pageModel.MaterialCardDesigner = invalidDesigner;
            ViewData["OpenMaterialGroupModal"] = true;
            return View(nameof(ViewSettings), pageModel);
        }

        try
        {
            await _logisticsConfigurationService.SaveFieldGroupAsync(
                new SaveFieldGroupRequest(
                    model.GroupInput.GroupId,
                    model.GroupInput.GroupKey,
                    model.GroupInput.GroupLabel,
                    model.GroupInput.DisplayOrder,
                    model.GroupInput.IsActive,
                    ScreenCode: model.GroupInput.ScreenCode,
                    LayerKey: model.GroupInput.LayerKey),
                cancellationToken);

            var savedGroupId = model.GroupInput.GroupId;
            if (!savedGroupId.HasValue)
            {
                var schema = await _logisticsConfigurationService.GetMaterialCardDynamicSchemaAsync(cancellationToken);
                savedGroupId = schema.Groups
                    .FirstOrDefault(x => string.Equals(x.GroupKey, model.GroupInput.GroupKey, StringComparison.OrdinalIgnoreCase))
                    ?.Id;
            }

            TempData["AdminSuccess"] = "Saha grubu kaydedildi.";
            return RedirectToAction(
                nameof(ViewSettings),
                new
                {
                    screenCode = ScreenDesignCatalog.MaterialCardsScreenCode,
                    groupId = savedGroupId,
                    fieldId = model.FieldInput.FieldId
                });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidDesigner = await BuildMaterialCardViewSettingsViewModelAsync(
                model.GroupInput.GroupId,
                model.FieldInput.FieldId,
                cancellationToken);
            invalidDesigner.GroupInput = model.GroupInput;
            invalidDesigner.FieldInput = model.FieldInput;
            var pageModel = await BuildScreenDesignSettingsPageViewModelAsync(
                ScreenDesignCatalog.MaterialCardsScreenCode,
                model.GroupInput.GroupId,
                model.FieldInput.FieldId,
                cancellationToken);
            pageModel.MaterialCardDesigner = invalidDesigner;
            ViewData["OpenMaterialGroupModal"] = true;
            return View(nameof(ViewSettings), pageModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveMaterialCardDynamicField(
        MaterialCardViewSettingsViewModel model,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("view-settings-screens");

        // Field kaydederken GroupInput alanlari gonderilmez — React'in
        // saveField akisi sadece FieldInput.* gonderir. GroupInput.GroupKey
        // ve GroupInput.GroupLabel [Required] oldugu icin ModelState hatali
        // oluyor ve save hic gerceklesmiyordu. Bu entry'leri temizliyoruz.
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("GroupInput.", StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(key);
        }

        if (!Enum.TryParse<MaterialCardDynamicFieldDataType>(model.FieldInput.DataType, true, out var dataType) ||
            !Enum.IsDefined(dataType))
        {
            ModelState.AddModelError("FieldInput.DataType", "Gecerli bir veri tipi seciniz.");
        }

        if (!ModelState.IsValid)
        {
            var invalidDesigner = await BuildMaterialCardViewSettingsViewModelAsync(
                model.GroupInput.GroupId,
                model.FieldInput.FieldId,
                cancellationToken);
            invalidDesigner.GroupInput = model.GroupInput;
            invalidDesigner.FieldInput = model.FieldInput;
            var pageModel = await BuildScreenDesignSettingsPageViewModelAsync(
                ScreenDesignCatalog.MaterialCardsScreenCode,
                model.GroupInput.GroupId,
                model.FieldInput.FieldId,
                cancellationToken);
            pageModel.MaterialCardDesigner = invalidDesigner;
            return View(nameof(ViewSettings), pageModel);
        }

        try
        {
            await _logisticsConfigurationService.SaveMaterialCardDynamicFieldAsync(
                new SaveMaterialCardDynamicFieldRequest(
                    model.FieldInput.FieldId,
                    model.FieldInput.GroupId,
                    model.FieldInput.FieldKey,
                    model.FieldInput.FieldLabel,
                    dataType,
                    model.FieldInput.IsVisible,
                    model.FieldInput.IsRequired,
                    model.FieldInput.DefaultValue,
                    model.FieldInput.DisplayOrder,
                    model.FieldInput.ColumnSpan,
                    model.FieldInput.IsActive,
                    (model.FieldInput.Options ?? [])
                        .Select(x => new SaveMaterialCardFieldOptionRequest(
                            x.OptionId,
                            x.OptionKey,
                            x.OptionLabel,
                            x.SortOrder,
                            x.IsActive))
                        .ToArray(),
                    ScreenCode: model.FieldInput.ScreenCode,
                    LayerKey: model.FieldInput.LayerKey),
                cancellationToken);

            TempData["AdminSuccess"] = "Saha tasarimi kaydedildi.";
            return RedirectToAction(
                nameof(ViewSettings),
                new
                {
                    screenCode = ScreenDesignCatalog.MaterialCardsScreenCode,
                    fieldId = model.FieldInput.FieldId
                });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidDesigner = await BuildMaterialCardViewSettingsViewModelAsync(
                model.GroupInput.GroupId,
                model.FieldInput.FieldId,
                cancellationToken);
            invalidDesigner.GroupInput = model.GroupInput;
            invalidDesigner.FieldInput = model.FieldInput;
            var pageModel = await BuildScreenDesignSettingsPageViewModelAsync(
                ScreenDesignCatalog.MaterialCardsScreenCode,
                model.GroupInput.GroupId,
                model.FieldInput.FieldId,
                cancellationToken);
            pageModel.MaterialCardDesigner = invalidDesigner;
            return View(nameof(ViewSettings), pageModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveScreenLayout(
        StandardScreenDesignViewModel model,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("view-settings-screens");

        try
        {
            await _uiConfigurationService.SaveScreenDesignLayoutAsync(
                new SaveScreenDesignLayoutRequest(
                    model.ScreenCode,
                    (model.Tabs ?? [])
                        .Select(x => new SaveScreenDesignTabRequest(
                            x.TabKey,
                            x.TabLabel,
                            x.DisplayOrder,
                            x.IsActive))
                        .ToArray(),
                    (model.Items ?? [])
                        .Select(x => new SaveScreenDesignItemRequest(
                            x.ItemKey,
                            x.TabKey,
                            x.DisplayOrder,
                            x.ColumnSpan,
                            x.IsVisible,
                            x.IsRequired))
                        .ToArray()),
                cancellationToken);

            TempData["AdminSuccess"] = "Ekran tasarimi kaydedildi.";
            return RedirectToAction(nameof(ViewSettings), new { screenCode = model.ScreenCode });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var pageModel = await BuildScreenDesignSettingsPageViewModelAsync(
                model.ScreenCode,
                null,
                null,
                cancellationToken);
            pageModel.StandardDesigner = model;
            return View(nameof(ViewSettings), pageModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IntegratorSettings(
        IntegratorSettingsInput input,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("integrator-settings");

        if (!TryParseIntegratorProvider(input.Provider, out var provider))
        {
            ModelState.AddModelError(nameof(input.Provider), "Gecerli bir saglayici seciniz.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                input,
                new SmtpProfileInput(),
                new ErpConnectionInput(),
                "admin-integrators",
                page,
                pageSize,
                cancellationToken);
            return View(nameof(IntegratorSettings), invalidModel);
        }

        try
        {
            await _adminManagementService.SaveIntegratorSettingsAsync(
                new SaveIntegratorSettingsRequest(
                    input.Id,
                    input.CompanyId!.Value,
                    provider,
                    input.Name,
                    input.BaseUrl,
                    input.CompanyTaxNumber,
                    input.Username,
                    input.Secret,
                    input.PollingIntervalSeconds,
                    input.MaxRecordsPerPull,
                    input.LogRetentionDays,
                    input.IncludeReceivedDocumentsInPull,
                    input.MarkDownloadedDocumentsAsReceived,
                    input.IncludeIssuedEInvoicesInPull,
                    input.IncludeIssuedEArchivesInPull,
                    input.IncludeIssuedEDispatchesInPull,
                    input.IsActive,
                    input.ScheduleEnabled,
                    input.AppStr,
                    input.Source,
                    input.AppVersion,
                    input.TimeoutSeconds,
                    input.LookbackDays),
                cancellationToken);

            TempData["AdminSuccess"] = "Entegrator ayari kaydedildi.";
            return RedirectToAction(nameof(IntegratorSettings), new { page, pageSize });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                input,
                new SmtpProfileInput(),
                new ErpConnectionInput(),
                "admin-integrators",
                page,
                pageSize,
                cancellationToken);
            return View(nameof(IntegratorSettings), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteIntegratorSettings(
        int id,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("integrator-settings");
        try
        {
            await _adminManagementService.DeleteIntegratorSettingsAsync(id, cancellationToken);
            TempData["AdminSuccess"] = "Entegrator ayari basariyla silindi.";
            return RedirectToAction(nameof(IntegratorSettings), new { page, pageSize });
        }
        catch (ArgumentException ex)
        {
            TempData["AdminError"] = ex.Message;
            return RedirectToAction(nameof(IntegratorSettings), new { page, pageSize });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(
        IntegratorSettingsInput input,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("integrator-settings");

        if (!TryParseIntegratorProvider(input.Provider, out var provider))
        {
            ModelState.AddModelError(nameof(input.Provider), "Gecerli bir saglayici seciniz.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                input,
                new SmtpProfileInput(),
                new ErpConnectionInput(),
                "admin-integrators",
                page,
                pageSize,
                cancellationToken,
                "Baglanti testi icin tum zorunlu alanlari doldurunuz.",
                false);
            return View(nameof(IntegratorSettings), invalidModel);
        }

        try
        {
            var result = await _adminManagementService.TestIntegratorConnectionAsync(
                new TestIntegratorConnectionRequest(
                    input.CompanyId!.Value,
                    provider,
                    input.Name,
                    input.BaseUrl,
                    input.CompanyTaxNumber,
                    input.Username,
                    input.Secret,
                    input.PollingIntervalSeconds,
                    input.MaxRecordsPerPull,
                    input.LogRetentionDays,
                    input.IncludeReceivedDocumentsInPull,
                    input.MarkDownloadedDocumentsAsReceived,
                    input.IncludeIssuedEInvoicesInPull,
                    input.IncludeIssuedEArchivesInPull,
                    input.IncludeIssuedEDispatchesInPull,
                    input.AppStr,
                    input.Source,
                    input.AppVersion,
                    input.TimeoutSeconds,
                    input.LookbackDays),
                cancellationToken);

            var viewModel = await BuildIntegratorSettingsViewModelAsync(
                input,
                new SmtpProfileInput(),
                new ErpConnectionInput(),
                "admin-integrators",
                page,
                pageSize,
                cancellationToken,
                result.Message,
                result.IsSuccess);
            return View(nameof(IntegratorSettings), viewModel);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                input,
                new SmtpProfileInput(),
                new ErpConnectionInput(),
                "admin-integrators",
                page,
                pageSize,
                cancellationToken,
                ex.Message,
                false);
            return View(nameof(IntegratorSettings), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PullIntegratorData(
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("integrator-settings");

        try
        {
            var result = await _documentImportService.ImportFromActiveIntegratorsAsync(cancellationToken);
            TempData["AdminInfo"] =
                $"Veri cekme tamamlandi. Yeni kayit: {result.ImportedCount}, atlanan: {result.SkippedCount}.";

            if (result.Notes.Count > 0)
            {
                TempData["AdminInfo"] = $"{TempData["AdminInfo"]} {string.Join(" ", result.Notes.Take(2))}";
            }
        }
        catch (Exception ex)
        {
            TempData["AdminError"] = $"Veri cekme islemi basarisiz: {ex.Message}";
        }

        return RedirectToAction(nameof(IntegratorSettings), new { page, pageSize });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSmtpProfile(
        [Bind(Prefix = "SmtpInput")] SmtpProfileInput smtpInput,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("mail-settings");

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                smtpInput,
                new ErpConnectionInput(),
                "admin-smtp-profiles",
                page,
                pageSize,
                cancellationToken);
            return View(nameof(MailSettings), invalidModel);
        }

        try
        {
            await _adminManagementService.SaveSmtpProfileAsync(
                new SaveSmtpProfileRequest(
                    smtpInput.Id,
                    smtpInput.CompanyId!.Value,
                    smtpInput.Name,
                    smtpInput.FromEmail,
                    smtpInput.FromDisplayName,
                    smtpInput.Host,
                    smtpInput.Port,
                    smtpInput.Username,
                    smtpInput.Password,
                    smtpInput.AuthMethod ?? "Normal",
                    smtpInput.OAuth2ClientId,
                    smtpInput.OAuth2ClientSecret,
                    smtpInput.OAuth2RefreshToken,
                    smtpInput.UseSsl,
                    smtpInput.IsActive),
                cancellationToken);

            TempData["AdminSuccess"] = "SMTP profili kaydedildi.";
            return RedirectToAction(nameof(MailSettings), new { page, pageSize });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                smtpInput,
                new ErpConnectionInput(),
                "admin-smtp-profiles",
                page,
                pageSize,
                cancellationToken);
            return View(nameof(MailSettings), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSmtpConnection(
        [Bind(Prefix = "SmtpInput")] SmtpProfileInput smtpInput,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("mail-settings");

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                smtpInput,
                new ErpConnectionInput(),
                "admin-smtp-profiles",
                page,
                pageSize,
                cancellationToken,
                smtpConnectionTestMessage: "SMTP testi icin tum zorunlu alanlari doldurunuz.",
                isSmtpConnectionTestSuccess: false);
            return View(nameof(MailSettings), invalidModel);
        }

        try
        {
            var result = await _adminManagementService.TestSmtpConnectionAsync(
                new TestSmtpConnectionRequest(
                    smtpInput.CompanyId!.Value,
                    smtpInput.Name,
                    smtpInput.FromEmail,
                    smtpInput.FromDisplayName,
                    smtpInput.Host,
                    smtpInput.Port,
                    smtpInput.Username,
                    smtpInput.Password,
                    smtpInput.UseSsl,
                    smtpInput.TestRecipientEmail),
                cancellationToken);

            var viewModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                smtpInput,
                new ErpConnectionInput(),
                "admin-smtp-profiles",
                page,
                pageSize,
                cancellationToken,
                smtpConnectionTestMessage: result.Message,
                isSmtpConnectionTestSuccess: result.IsSuccess);
            return View(nameof(MailSettings), viewModel);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                smtpInput,
                new ErpConnectionInput(),
                "admin-smtp-profiles",
                page,
                pageSize,
                cancellationToken,
                smtpConnectionTestMessage: ex.Message,
                isSmtpConnectionTestSuccess: false);
            return View(nameof(MailSettings), invalidModel);
        }
    }

    // NOT: TestSmtpConnectionJson ConnectivityTestsController'a tasindi (rapor 2.3 split).

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestErpConnection(
        [Bind(Prefix = "ErpInput")] ErpConnectionInput erpInput,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("erp-settings");

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                new SmtpProfileInput(),
                erpInput,
                "admin-erp-connections",
                page,
                pageSize,
                cancellationToken,
                erpConnectionTestMessage: "SQL testi icin tum zorunlu alanlari doldurunuz.",
                isErpConnectionTestSuccess: false);
            return View(nameof(ErpSettings), invalidModel);
        }

        try
        {
            var result = await _adminManagementService.TestErpConnectionAsync(
                new TestErpConnectionRequest(
                    erpInput.CompanyId!.Value,
                    erpInput.Company,
                    erpInput.Business,
                    erpInput.Branch,
                    erpInput.Username,
                    erpInput.Password),
                cancellationToken);

            var viewModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                new SmtpProfileInput(),
                erpInput,
                "admin-erp-connections",
                page,
                pageSize,
                cancellationToken,
                erpConnectionTestMessage: result.Message,
                isErpConnectionTestSuccess: result.IsSuccess);
            return View(nameof(ErpSettings), viewModel);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                new SmtpProfileInput(),
                erpInput,
                "admin-erp-connections",
                page,
                pageSize,
                cancellationToken,
                erpConnectionTestMessage: ex.Message,
                isErpConnectionTestSuccess: false);
            return View(nameof(ErpSettings), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveErpSettings(
        [Bind(Prefix = "ErpInput")] ErpConnectionInput erpInput,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("erp-settings");

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                new SmtpProfileInput(),
                erpInput,
                "admin-erp-connections",
                page,
                pageSize,
                cancellationToken);
            return View(nameof(ErpSettings), invalidModel);
        }

        try
        {
            await _adminManagementService.SaveErpConnectionSettingsAsync(
                new SaveErpConnectionSettingsRequest(
                    erpInput.Id,
                    erpInput.CompanyId!.Value,
                    erpInput.Company,
                    erpInput.Business,
                    erpInput.Branch,
                    erpInput.Username,
                    erpInput.Password,
                    erpInput.IsActive),
                cancellationToken);

            TempData["AdminSuccess"] = "ERP baglanti ayari kaydedildi.";
            return RedirectToAction(nameof(ErpSettings), new { page, pageSize });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildIntegratorSettingsViewModelAsync(
                new IntegratorSettingsInput
                {
                    Provider = IntegratorProvider.Logo.ToString()
                },
                new SmtpProfileInput(),
                erpInput,
                "admin-erp-connections",
                page,
                pageSize,
                cancellationToken);
            return View(nameof(ErpSettings), invalidModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteErpSettings(
        Guid id,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("erp-settings");
        try
        {
            await _adminManagementService.DeleteErpConnectionAsync(id, cancellationToken);
            TempData["AdminSuccess"] = "ERP baglanti ayari basariyla silindi.";
            return RedirectToAction(nameof(ErpSettings), new { page, pageSize });
        }
        catch (ArgumentException ex)
        {
            TempData["AdminError"] = ex.Message;
            return RedirectToAction(nameof(ErpSettings), new { page, pageSize });
        }
    }

    // NOT: ERP Settings JSON (Get/Get/Save/Delete/Test) ErpSettingsJsonController'a tasindi (rapor 2.3 split).

    // NOT: GetDepartmentsJson DepartmentController'a tasindi (GetCompaniesJson burada kaldi).

    [HttpGet]
    public async Task<IActionResult> GetCompaniesJson(CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        return Json(snapshot.Companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToArray());
    }

    // NOT: SaveDepartmentJson + GetDepartmentJson + UpdateDepartmentJson + DeleteDepartmentJson DepartmentController'a tasindi.

    // NOT: ToggleDepartmentJson DepartmentController'a tasindi.

    // NOT: GetDepartmentsLookup DepartmentController'a tasindi.

    // NOT: DepartmentEdit DepartmentController'a tasindi.

    // NOT: GetProjectInstructionsJson + SaveProjectInstructionsJson ProjectInstructionsJsonController'a tasindi (rapor 2.3 split).

    [HttpGet]
    public async Task<IActionResult> Departments(
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("departments");
        var viewModel = await BuildDepartmentViewModelAsync(new DepartmentCreateInput(), page, pageSize, cancellationToken);
        var board = await BuildDepartmentBoardConfigAsync(cancellationToken);
        return View(new DepartmentManagementViewModel
        {
            Departments    = viewModel.Departments,
            CompanyOptions = viewModel.CompanyOptions,
            ListState      = viewModel.ListState,
            Input          = viewModel.Input,
            BoardConfig    = board,
        });
    }

    // NOT: DepartmentsBoardConfig DepartmentController'a tasindi (helper duplicate olarak burada kaldi - geri uyum).

    // C-Grid (SmartBoard) konfigurasyonu — Personel ekraniyla benzer pattern.
    // Sadece aktif kullanicinin sirketine ait departmanlar listelenir
    // (sistem genelindeki tum departmanlar gosterilmez — sirket secimi kaldirildi).
    private async Task<object> BuildDepartmentBoardConfigAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var currentCompanyId = GetCompanyId();
        var departments = snapshot.Departments
            .Where(d => currentCompanyId <= 0 || d.CompanyId == currentCompanyId)
            .OrderBy(d => d.Name)
            .ToArray();

        var entities = new List<object>();
        foreach (var d in departments)
        {
            var widgets = new List<object>
            {
                new
                {
                    id       = "w_active",
                    type     = "data",
                    dataType = "text",
                    label    = "Durum",
                    value    = d.IsActive ? "Aktif" : "Pasif",
                    detail   = (string?)null,
                    color    = d.IsActive ? "emerald" : "slate",
                },
            };

            entities.Add(new
            {
                id            = d.Id,
                title         = d.Name,
                subtitle      = (string?)null,
                description   = (string?)null,
                imageUrl      = (string?)null,
                statusBadge   = (object?)null,
                widgets,
                primaryAction = new
                {
                    label      = "Duzenle",
                    icon       = "Edit",
                    color      = "amber",
                    url        = $"/Admin/DepartmentEdit?id={d.Id}",
                    hideButton = true,
                },
                secondaryAction = new
                {
                    label     = "Sil",
                    icon      = "Trash2",
                    apiUrl    = $"/Admin/DeleteDepartmentJson?id={d.Id}",
                    apiMethod = "POST",
                    confirm   = $"Bu departmani silmek istediginize emin misiniz? ({d.Name})",
                },
            });
        }

        return new
        {
            boardKey          = "admin-departments",
            title             = "Departman Tanimlamalari",
            subtitle          = $"{entities.Count} departman",
            icon              = "Building2",
            iconColor         = "blue",
            refreshUrl        = "/Admin/DepartmentsBoardConfig",
            searchPlaceholder = "Hizli ara... (ad, sirket)",
            emptyText         = "Henuz departman tanimlanmamis",
            actions           = new object[]
            {
                new
                {
                    id      = "new",
                    label   = "Yeni Departman",
                    icon    = "Plus",
                    variant = "primary",
                    url     = "/Admin/DepartmentEdit",
                },
            },
            masterWidgets = Array.Empty<object>(),
            entities,
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Departments(
        DepartmentCreateInput input,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("departments");

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildDepartmentViewModelAsync(input, page, pageSize, cancellationToken);
            return View(invalidModel);
        }

        try
        {
            await _adminManagementService.CreateDepartmentAsync(
                new CreateDepartmentRequest(input.CompanyId!.Value, input.Name),
                cancellationToken);

            TempData["AdminSuccess"] = "Departman tanimi olusturuldu.";
            return RedirectToAction(nameof(Departments));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildDepartmentViewModelAsync(input, page, pageSize, cancellationToken);
            return View(invalidModel);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Users(
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("users");
        var viewModel = await BuildUserViewModelAsync(
            new UserCreateInput
            {
                Role = UserRole.Operator.ToString(),
                Permissions = new List<string> { UserPermission.ViewIncomingDocuments.ToString() }
            },
            page,
            pageSize,
            cancellationToken);
        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Users(
        UserCreateInput input,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("users");

        input.Permissions ??= new List<string>();
        input.Permissions = input.Permissions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        UserRole role = default;
        IReadOnlyCollection<UserPermission> permissions = Array.Empty<UserPermission>();

        if (!string.IsNullOrWhiteSpace(input.Role) && !TryParseRole(input.Role, out role))
        {
            ModelState.AddModelError(nameof(input.Role), "Secilen rol gecersiz.");
        }

        if (input.Permissions.Count > 0 && !TryParsePermissions(input.Permissions, out permissions))
        {
            ModelState.AddModelError(nameof(input.Permissions), "Secilen yetkilerden biri gecersiz.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildUserViewModelAsync(input, page, pageSize, cancellationToken);
            return View(invalidModel);
        }

        try
        {
            await _adminManagementService.CreateUserAsync(
                new CreateUserRequest(
                    input.CompanyId!.Value,
                    input.FullName,
                    input.Email,
                    input.EmployeeCode,
                    input.DepartmentId,
                    input.SupervisorUserId,
                    role,
                    permissions),
                cancellationToken);

            TempData["AdminSuccess"] = "Kullanici tanimi olusturuldu.";
            return RedirectToAction(nameof(Users));
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var invalidModel = await BuildUserViewModelAsync(input, page, pageSize, cancellationToken);
            return View(invalidModel);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Roles(
        int? companyId,
        int? rolesPage,
        int? rolesPageSize,
        int? usersPage,
        int? usersPageSize,
        CancellationToken cancellationToken)
    {
        SetActiveMenu("roles");

        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var activeCompanies = snapshot.Companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToArray();

        if (!companyId.HasValue && activeCompanies.Length > 0)
        {
            companyId = activeCompanies[0].Id;
        }

        var companyOptions = activeCompanies
            .Select(x => new SelectListItem(
                x.Name,
                x.Id.ToString(),
                companyId == x.Id))
            .ToArray();

        var roles = UserAuthorizationCatalog.Roles
            .Select(role => new RoleDefinitionViewModel
            {
                Name = UserAuthorizationCatalog.GetRoleLabel(role),
                Permissions = UserAuthorizationCatalog.GetAllowedPermissions(role)
                    .Select(UserAuthorizationCatalog.GetPermissionLabel)
                    .ToArray()
            })
            .ToArray();

        var allUsers = snapshot.Users
            .Where(x => !companyId.HasValue || x.CompanyId == companyId.Value)
            .OrderBy(x => x.FullName)
            .Select(x => new RoleUserViewModel
            {
                FullName = x.FullName,
                Email = x.Email,
                Role = x.Role
            })
            .ToArray();

        var resolvedRolesPageSize = await ResolveGridPageSizeAsync("admin-roles-matrix", rolesPageSize, cancellationToken);
        var resolvedUsersPageSize = await ResolveGridPageSizeAsync("admin-role-users", usersPageSize, cancellationToken);
        var rolesTotalPages = roles.Length == 0 ? 0 : (int)Math.Ceiling(roles.Length / (double)resolvedRolesPageSize);
        var usersTotalPages = allUsers.Length == 0 ? 0 : (int)Math.Ceiling(allUsers.Length / (double)resolvedUsersPageSize);
        var currentRolesPage = rolesTotalPages == 0 ? 1 : Math.Min(Math.Max(rolesPage.GetValueOrDefault(1), 1), rolesTotalPages);
        var currentUsersPage = usersTotalPages == 0 ? 1 : Math.Min(Math.Max(usersPage.GetValueOrDefault(1), 1), usersTotalPages);
        var pagedRoles = roles
            .Skip((currentRolesPage - 1) * resolvedRolesPageSize)
            .Take(resolvedRolesPageSize)
            .ToArray();
        var pagedUsers = allUsers
            .Skip((currentUsersPage - 1) * resolvedUsersPageSize)
            .Take(resolvedUsersPageSize)
            .ToArray();

        return View(new RoleManagementViewModel
        {
            Roles = pagedRoles,
            CompanyOptions = companyOptions,
            Users = pagedUsers,
            RolesListState = BuildGridListState(
                gridKey: "admin-roles-matrix",
                page: currentRolesPage,
                pageSize: resolvedRolesPageSize,
                totalCount: roles.Length,
                totalPages: rolesTotalPages,
                itemLabel: "rol",
                pageParameterName: "rolesPage",
                pageSizeParameterName: "rolesPageSize"),
            UsersListState = BuildGridListState(
                gridKey: "admin-role-users",
                page: currentUsersPage,
                pageSize: resolvedUsersPageSize,
                totalCount: allUsers.Length,
                totalPages: usersTotalPages,
                itemLabel: "kullanici",
                pageParameterName: "usersPage",
                pageSizeParameterName: "usersPageSize"),
            CompanyId = companyId
        });
    }

    private async Task<DepartmentManagementViewModel> BuildDepartmentViewModelAsync(
        DepartmentCreateInput input,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var activeCompanies = snapshot.Companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToArray();

        if (!input.CompanyId.HasValue && activeCompanies.Length > 0)
        {
            input.CompanyId = activeCompanies[0].Id;
        }

        var companyOptions = activeCompanies
            .Select(x => new SelectListItem(
                x.Name,
                x.Id.ToString(),
                input.CompanyId == x.Id))
            .ToArray();

        var filteredDepartments = input.CompanyId.HasValue
            ? snapshot.Departments.Where(x => x.CompanyId == input.CompanyId.Value).ToArray()
            : snapshot.Departments.ToArray();
        var resolvedPageSize = await ResolveGridPageSizeAsync("admin-departments", pageSize, cancellationToken);
        var totalCount = filteredDepartments.Length;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);
        var departments = filteredDepartments
            .Skip((currentPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToArray();

        return new DepartmentManagementViewModel
        {
            Departments = departments,
            CompanyOptions = companyOptions,
            ListState = BuildGridListState(
                gridKey: "admin-departments",
                page: currentPage,
                pageSize: resolvedPageSize,
                totalCount: totalCount,
                totalPages: totalPages,
                itemLabel: "departman"),
            Input = input
        };
    }

    private async Task<IntegratorSettingsManagementViewModel> BuildIntegratorSettingsViewModelAsync(
        IntegratorSettingsInput input,
        SmtpProfileInput smtpInput,
        ErpConnectionInput erpInput,
        string activeGridKey,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken,
        string? connectionTestMessage = null,
        bool? isConnectionTestSuccess = null,
        string? smtpConnectionTestMessage = null,
        bool? isSmtpConnectionTestSuccess = null,
        string? erpConnectionTestMessage = null,
        bool? isErpConnectionTestSuccess = null,
        bool includeIntegratorImportLogs = false)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var activeCompanies = snapshot.Companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToArray();

        var defaultCompanyId = activeCompanies.FirstOrDefault()?.Id;
        if (!input.CompanyId.HasValue && defaultCompanyId.HasValue)
        {
            input.CompanyId = defaultCompanyId;
        }

        if (!smtpInput.CompanyId.HasValue && defaultCompanyId.HasValue)
        {
            smtpInput.CompanyId = defaultCompanyId;
        }

        if (!erpInput.CompanyId.HasValue && defaultCompanyId.HasValue)
        {
            erpInput.CompanyId = defaultCompanyId;
        }

        var integratorImportLogs = includeIntegratorImportLogs
            ? await _adminReadService.GetRecentIntegratorImportLogsAsync(150, cancellationToken)
            : Array.Empty<IntegratorImportLogEntryDto>();

        var providerOptions = Enum.GetValues<IntegratorProvider>()
            .Where(provider => provider != IntegratorProvider.Unknown)
            .Select(provider => new SelectListItem(
                provider.ToString(),
                provider.ToString(),
                string.Equals(input.Provider, provider.ToString(), StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var companyOptions = activeCompanies
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToArray();

        var integratorSource = input.CompanyId.HasValue
            ? snapshot.Integrators.Where(x => x.CompanyId == input.CompanyId.Value).ToArray()
            : snapshot.Integrators.ToArray();
        var smtpProfileSource = smtpInput.CompanyId.HasValue
            ? snapshot.SmtpProfiles.Where(x => x.CompanyId == smtpInput.CompanyId.Value).ToArray()
            : snapshot.SmtpProfiles.ToArray();
        var erpConnectionSource = erpInput.CompanyId.HasValue
            ? snapshot.ErpConnections.Where(x => x.CompanyId == erpInput.CompanyId.Value).ToArray()
            : snapshot.ErpConnections.ToArray();

        if (includeIntegratorImportLogs && input.CompanyId.HasValue)
        {
            integratorImportLogs = integratorImportLogs
                .Where(x => x.CompanyId == input.CompanyId.Value)
                .ToArray();
        }

        var integratorPageSize = await ResolveGridPageSizeAsync(
            "admin-integrators",
            string.Equals(activeGridKey, "admin-integrators", StringComparison.Ordinal)
                ? pageSize
                : null,
            cancellationToken);
        var integratorTotalCount = integratorSource.Length;
        var integratorTotalPages = integratorTotalCount == 0
            ? 0
            : (int)Math.Ceiling(integratorTotalCount / (double)integratorPageSize);
        var integratorCurrentPage = string.Equals(activeGridKey, "admin-integrators", StringComparison.Ordinal)
            ? (integratorTotalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), integratorTotalPages))
            : 1;
        var integrators = integratorSource
            .Skip((integratorCurrentPage - 1) * integratorPageSize)
            .Take(integratorPageSize)
            .ToArray();

        var smtpPageSize = await ResolveGridPageSizeAsync(
            "admin-smtp-profiles",
            string.Equals(activeGridKey, "admin-smtp-profiles", StringComparison.Ordinal)
                ? pageSize
                : null,
            cancellationToken);
        var smtpTotalCount = smtpProfileSource.Length;
        var smtpTotalPages = smtpTotalCount == 0
            ? 0
            : (int)Math.Ceiling(smtpTotalCount / (double)smtpPageSize);
        var smtpCurrentPage = string.Equals(activeGridKey, "admin-smtp-profiles", StringComparison.Ordinal)
            ? (smtpTotalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), smtpTotalPages))
            : 1;
        var smtpProfiles = smtpProfileSource
            .Skip((smtpCurrentPage - 1) * smtpPageSize)
            .Take(smtpPageSize)
            .ToArray();

        var erpPageSize = await ResolveGridPageSizeAsync(
            "admin-erp-connections",
            string.Equals(activeGridKey, "admin-erp-connections", StringComparison.Ordinal)
                ? pageSize
                : null,
            cancellationToken);
        var erpTotalCount = erpConnectionSource.Length;
        var erpTotalPages = erpTotalCount == 0
            ? 0
            : (int)Math.Ceiling(erpTotalCount / (double)erpPageSize);
        var erpCurrentPage = string.Equals(activeGridKey, "admin-erp-connections", StringComparison.Ordinal)
            ? (erpTotalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), erpTotalPages))
            : 1;
        var erpConnections = erpConnectionSource
            .Skip((erpCurrentPage - 1) * erpPageSize)
            .Take(erpPageSize)
            .ToArray();

        return new IntegratorSettingsManagementViewModel
        {
            Integrators = integrators,
            IntegratorImportLogs = integratorImportLogs,
            SmtpProfiles = smtpProfiles,
            ErpConnections = erpConnections,
            IntegratorListState = BuildGridListState(
                gridKey: "admin-integrators",
                page: integratorCurrentPage,
                pageSize: integratorPageSize,
                totalCount: integratorTotalCount,
                totalPages: integratorTotalPages,
                itemLabel: "entegrator"),
            SmtpListState = BuildGridListState(
                gridKey: "admin-smtp-profiles",
                page: smtpCurrentPage,
                pageSize: smtpPageSize,
                totalCount: smtpTotalCount,
                totalPages: smtpTotalPages,
                itemLabel: "smtp profili"),
            ErpListState = BuildGridListState(
                gridKey: "admin-erp-connections",
                page: erpCurrentPage,
                pageSize: erpPageSize,
                totalCount: erpTotalCount,
                totalPages: erpTotalPages,
                itemLabel: "erp baglantisi"),
            CompanyOptions = companyOptions,
            ProviderOptions = providerOptions,
            Input = input,
            SmtpInput = smtpInput,
            ErpInput = erpInput,
            ConnectionTestMessage = connectionTestMessage,
            IsConnectionTestSuccess = isConnectionTestSuccess,
            SmtpConnectionTestMessage = smtpConnectionTestMessage,
            IsSmtpConnectionTestSuccess = isSmtpConnectionTestSuccess,
            ErpConnectionTestMessage = erpConnectionTestMessage,
            IsErpConnectionTestSuccess = isErpConnectionTestSuccess
        };
    }

    private async Task<SystemLogsViewModel> BuildSystemLogsViewModelAsync(
        string? searchTerm,
        string? level,
        int? companyId,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedSearchTerm = searchTerm?.Trim() ?? string.Empty;
        var normalizedLevel = level?.Trim() ?? string.Empty;

        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var logs = await _adminReadService.GetRecentIntegratorImportLogsAsync(500, cancellationToken);
        IEnumerable<IntegratorImportLogEntryDto> query = logs;

        var activeCompanies = snapshot.Companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToArray();
        var companyOptions = activeCompanies
            .Select(x => new SelectListItem(
                x.Name,
                x.Id.ToString(),
                companyId == x.Id))
            .ToArray();

        if (companyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedLevel))
        {
            query = query.Where(x =>
                string.Equals(x.Level, normalizedLevel, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearchTerm))
        {
            query = query.Where(x =>
                ContainsInsensitive(x.CompanyName, normalizedSearchTerm) ||
                ContainsInsensitive(x.IntegratorName, normalizedSearchTerm) ||
                ContainsInsensitive(x.Message, normalizedSearchTerm) ||
                ContainsInsensitive(x.SourceFileName, normalizedSearchTerm) ||
                ContainsInsensitive(x.Level, normalizedSearchTerm));
        }

        var filteredLogs = query.ToArray();
        var resolvedPageSize = await ResolveGridPageSizeAsync("admin-system-logs", pageSize, cancellationToken);
        var totalCount = filteredLogs.Length;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);

        return new SystemLogsViewModel
        {
            CompanyOptions = companyOptions,
            SearchTerm = normalizedSearchTerm,
            Level = normalizedLevel,
            CompanyId = companyId,
            ListState = BuildGridListState(
                gridKey: "admin-system-logs",
                page: currentPage,
                pageSize: resolvedPageSize,
                totalCount: totalCount,
                totalPages: totalPages,
                itemLabel: "log"),
            Logs = filteredLogs
                .Skip((currentPage - 1) * resolvedPageSize)
                .Take(resolvedPageSize)
                .ToArray()
        };
    }

    private async Task<ProjectInstructionsViewModel> BuildProjectInstructionsViewModelAsync(CancellationToken cancellationToken)
    {
        var filePath = ResolveProjectInstructionsPath();
        var requestsPath = ResolveProjectRequestsPath();
        var content = System.IO.File.Exists(filePath)
            ? await System.IO.File.ReadAllTextAsync(filePath, cancellationToken)
            : string.Empty;
        var requestItems = await LoadProjectRequestItemsAsync(cancellationToken);
        DateTime? lastModified = System.IO.File.Exists(filePath)
            ? System.IO.File.GetLastWriteTime(filePath)
            : null;
        DateTime? requestsLastModified = System.IO.File.Exists(requestsPath)
            ? System.IO.File.GetLastWriteTime(requestsPath)
            : null;
        var normalizedNewLines = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lineCount = string.IsNullOrEmpty(content) ? 0 : normalizedNewLines.Split('\n').Length;

        return new ProjectInstructionsViewModel
        {
            Content = content,
            RelativePath = Path.GetRelativePath(_webHostEnvironment.ContentRootPath, filePath),
            CharacterCount = content.Length,
            LineCount = lineCount,
            LastModified = lastModified,
            RequestsRelativePath = Path.GetRelativePath(_webHostEnvironment.ContentRootPath, requestsPath),
            RequestCount = requestItems.Count,
            CompletedRequestCount = requestItems.Count(x => x.IsCompleted),
            RequestsLastModified = requestsLastModified,
            RequestItems = requestItems
        };
    }

    private async Task<List<ProjectInstructionRequestItemInput>> LoadProjectRequestItemsAsync(CancellationToken cancellationToken)
    {
        var filePath = ResolveProjectRequestsPath();
        if (!System.IO.File.Exists(filePath))
        {
            return GetDefaultProjectRequestItems();
        }

        try
        {
            var content = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                return [];
            }

            var document = JsonSerializer.Deserialize<ProjectInstructionRequestDocument>(content);
            return NormalizeProjectRequestItems(document?.Items);
        }
        catch (JsonException)
        {
            return GetDefaultProjectRequestItems();
        }
    }

    private static List<ProjectInstructionRequestItemInput> NormalizeProjectRequestItems(
        IEnumerable<ProjectInstructionRequestItemInput>? items)
    {
        return (items ?? Enumerable.Empty<ProjectInstructionRequestItemInput>())
            .Select(item => new ProjectInstructionRequestItemInput
            {
                RequestId = item.RequestId?.Trim() ?? string.Empty,
                Category = item.Category?.Trim() ?? string.Empty,
                Title = item.Title?.Trim() ?? string.Empty,
                Description = item.Description?.Trim() ?? string.Empty,
                IsCompleted = item.IsCompleted,
                UserComment = item.UserComment?.Trim() ?? string.Empty
            })
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.RequestId) ||
                !string.IsNullOrWhiteSpace(item.Category) ||
                !string.IsNullOrWhiteSpace(item.Title) ||
                !string.IsNullOrWhiteSpace(item.Description) ||
                !string.IsNullOrWhiteSpace(item.UserComment))
            .ToList();
    }

    private static List<ProjectInstructionRequestItemInput> GetDefaultProjectRequestItems() =>
    [
        new()
        {
            RequestId = "GRID-001",
            Category = "Grid",
            Title = "Tum gridler ayni tipte ve sikisik gorunumde olmali.",
            Description = "Grid satirlari icerisindeki veriden cok az yuksek olmali.",
            IsCompleted = false
        },
        new()
        {
            RequestId = "GRID-002",
            Category = "Grid",
            Title = "Tum gridlerde zebra striping kullanilmali.",
            Description = "Satir okunabilirligi icin alternatif arka plan standardi uygulanmali.",
            IsCompleted = false
        },
        new()
        {
            RequestId = "GRID-003",
            Category = "Grid",
            Title = "Tum gridlerin solunda sec, duzelt ve sil aksiyonlari olmali.",
            Description = "Bu aksiyonlar sabit ve tum ekranlarda ayni sirada gorunmeli.",
            IsCompleted = false
        },
        new()
        {
            RequestId = "FORM-001",
            Category = "Form",
            Title = "Tum formlarda split screen yapisi kullanilmali.",
            Description = "Ust panel veri giris, alt panel ilgili kayitlarin listelendigi grid alani olmali.",
            IsCompleted = false
        },
        new()
        {
            RequestId = "GRID-004",
            Category = "Grid",
            Title = "Tum gridlerde server-side pagination kullanilmali.",
            Description = "Sayfa basi kayit sayisi gibi tercihler kullanici bazli kalici olarak saklanmali.",
            IsCompleted = false
        },
        new()
        {
            RequestId = "LAYOUT-001",
            Category = "Layout",
            Title = "Sayfada ana scroll yerine grid icinde internal scrolling kullanilmali.",
            Description = "Grid alani sabit yukseklikte olmali ve veri yogunlugunda yalnizca grid kaymali.",
            IsCompleted = false
        },
        new()
        {
            RequestId = "FORM-002",
            Category = "Form",
            Title = "Malzeme kartlarinda zorunlu alan hatalari popup yerine alan bazli gosterilmeli.",
            Description = "Malzeme kodu, malzeme adi ve bos gecilemeyen alanlar kaydet sirasinda kirmizi vurgu ile belirtilmeli.",
            IsCompleted = false
        },
        new()
        {
            RequestId = "DATA-001",
            Category = "Data",
            Title = "Form kayitlarindaki tarih alanlari server datetime beklentisine gore standardize edilmeli.",
            Description = "UTC zorunlulugu olmayan alanlarda veritabani tarih stratejisi netlestirilmeli ve tek tip uygulanmali.",
            IsCompleted = false
        }
    ];

    private static bool ContainsInsensitive(string source, string value) =>
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private async Task<CompanyManagementViewModel> BuildCompanyViewModelAsync(
        CompanyInput input,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var resolvedPageSize = await ResolveGridPageSizeAsync("admin-companies", pageSize, cancellationToken);
        var totalCount = snapshot.Companies.Count;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);
        var companies = snapshot.Companies
            .Skip((currentPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToArray();

        return new CompanyManagementViewModel
        {
            Companies = companies,
            ListState = BuildGridListState(
                gridKey: "admin-companies",
                page: currentPage,
                pageSize: resolvedPageSize,
                totalCount: totalCount,
                totalPages: totalPages,
                itemLabel: "sirket"),
            Input = input
        };
    }

    private async Task<UserManagementViewModel> BuildUserViewModelAsync(
        UserCreateInput input,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var snapshot = await _adminReadService.GetSnapshotAsync(cancellationToken);
        var selectedPermissions = input.Permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var companyOptions = snapshot.Companies
            .Where(x => x.IsActive || input.CompanyId == x.Id)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(
                x.Name,
                x.Id.ToString(),
                input.CompanyId == x.Id))
            .ToArray();

        if (!input.CompanyId.HasValue && companyOptions.Length > 0)
        {
            input.CompanyId = int.Parse(companyOptions[0].Value!);
            companyOptions[0].Selected = true;
        }

        var departmentOptions = snapshot.Departments
            .Where(x => !input.CompanyId.HasValue || x.CompanyId == input.CompanyId.Value)
            .Select(x => new SelectListItem(
                x.Name,
                x.Id.ToString(),
                input.DepartmentId.HasValue && input.DepartmentId.Value == x.Id))
            .ToArray();

        var supervisorOptions = new List<SelectListItem>
        {
            new("Seciniz", string.Empty, !input.SupervisorUserId.HasValue)
        };

        supervisorOptions.AddRange(snapshot.Users
            .Where(x => !input.CompanyId.HasValue || x.CompanyId == input.CompanyId.Value)
            .Select(x => new SelectListItem(
                x.FullName,
                x.Id.ToString(),
                input.SupervisorUserId == x.Id)));

        var roleOptions = UserAuthorizationCatalog.Roles
            .Select(role => new SelectListItem(
                UserAuthorizationCatalog.GetRoleLabel(role),
                role.ToString(),
                string.Equals(input.Role, role.ToString(), StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var permissionOptions = UserAuthorizationCatalog.Permissions
            .Select(permission => new PermissionOptionViewModel
            {
                Value = permission.ToString(),
                Label = UserAuthorizationCatalog.GetPermissionLabel(permission),
                IsSelected = selectedPermissions.Contains(permission.ToString())
            })
            .ToArray();

        var filteredUsers = input.CompanyId.HasValue
            ? snapshot.Users.Where(x => x.CompanyId == input.CompanyId.Value).ToArray()
            : snapshot.Users.ToArray();
        var resolvedPageSize = await ResolveGridPageSizeAsync("admin-users", pageSize, cancellationToken);
        var totalCount = filteredUsers.Length;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)resolvedPageSize);
        var currentPage = totalPages == 0 ? 1 : Math.Min(Math.Max(page.GetValueOrDefault(1), 1), totalPages);
        var users = filteredUsers
            .Skip((currentPage - 1) * resolvedPageSize)
            .Take(resolvedPageSize)
            .ToArray();

        return new UserManagementViewModel
        {
            Users = users,
            CompanyOptions = companyOptions,
            DepartmentOptions = departmentOptions,
            SupervisorOptions = supervisorOptions,
            RoleOptions = roleOptions,
            PermissionOptions = permissionOptions,
            ListState = BuildGridListState(
                gridKey: "admin-users",
                page: currentPage,
                pageSize: resolvedPageSize,
                totalCount: totalCount,
                totalPages: totalPages,
                itemLabel: "kullanici"),
            Input = input
        };
    }

    private static bool TryParseRole(string value, out UserRole role) =>
        Enum.TryParse(value, true, out role) && Enum.IsDefined(role);

    private static bool TryParsePermissions(
        IReadOnlyCollection<string> values,
        out IReadOnlyCollection<UserPermission> permissions)
    {
        var parsedPermissions = new List<UserPermission>();

        foreach (var value in values)
        {
            if (!Enum.TryParse(value, true, out UserPermission permission) || !Enum.IsDefined(permission))
            {
                permissions = Array.Empty<UserPermission>();
                return false;
            }

            parsedPermissions.Add(permission);
        }

        permissions = parsedPermissions
            .Distinct()
            .ToArray();

        return true;
    }

    private static bool TryParseIntegratorProvider(string value, out IntegratorProvider provider) =>
        Enum.TryParse(value, true, out provider) &&
        Enum.IsDefined(provider) &&
        provider != IntegratorProvider.Unknown;

    private static string GetMaterialCardDataTypeLabel(MaterialCardDynamicFieldDataType dataType) =>
        dataType switch
        {
            MaterialCardDynamicFieldDataType.String => "Metin",
            MaterialCardDynamicFieldDataType.Integer => "Tamsayi",
            MaterialCardDynamicFieldDataType.Decimal => "Ondalik",
            MaterialCardDynamicFieldDataType.Date => "Tarih",
            MaterialCardDynamicFieldDataType.Boolean => "Evet/Hayir",
            MaterialCardDynamicFieldDataType.Dropdown => "Dropdown",
            MaterialCardDynamicFieldDataType.MultiSelect => "Coklu Secim",
            _ => dataType.ToString()
        };

    private async Task<ScreenDesignSettingsPageViewModel> BuildScreenDesignSettingsPageViewModelAsync(
        string? screenCode,
        Guid? selectedGroupId,
        Guid? selectedFieldId,
        CancellationToken cancellationToken)
    {
        var normalizedScreenCode = ScreenDesignCatalog.NormalizeScreenCode(screenCode);
        var screens = _uiConfigurationService.GetSupportedScreenDesigns()
            .OrderBy(x => x.ScreenLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var usesMaterialCardSchema = ScreenDesignCatalog.UsesMaterialCardSchema(normalizedScreenCode);

        return new ScreenDesignSettingsPageViewModel
        {
            SelectedScreenCode = normalizedScreenCode,
            SelectedScreenLabel = ScreenDesignCatalog.GetScreenLabel(normalizedScreenCode),
            UsesMaterialCardSchema = usesMaterialCardSchema,
            ScreenOptions = screens
                .Select(x => new SelectListItem
                {
                    Text = x.ScreenLabel,
                    Value = x.ScreenCode,
                    Selected = string.Equals(x.ScreenCode, normalizedScreenCode, StringComparison.OrdinalIgnoreCase),
                    Group = new SelectListGroup { Name = x.GroupLabel }
                })
                .ToArray(),
            // MaterialCardDesigner — legacy material card field designer (artik kullanilmiyor;
            // yerine React panel /api/widgets uzerinden calisiyor). Eski tablo (FieldGroup/Field)
            // INT IDENTITY ID'li, entity Guid bekliyor → schema fetch InvalidCastException atiyor.
            // usesMaterialCardSchema=true icinde de bos donelim — legacy view'lar artik bu modeli
            // tuketmiyor.
            MaterialCardDesigner = new MaterialCardViewSettingsViewModel(),
            StandardDesigner = usesMaterialCardSchema
                ? new StandardScreenDesignViewModel
                {
                    ScreenCode = normalizedScreenCode,
                    ScreenLabel = ScreenDesignCatalog.GetScreenLabel(normalizedScreenCode)
                }
                : await BuildStandardScreenDesignViewModelAsync(normalizedScreenCode, cancellationToken)
        };
    }

    private async Task<StandardScreenDesignViewModel> BuildStandardScreenDesignViewModelAsync(
        string screenCode,
        CancellationToken cancellationToken)
    {
        var layout = await _uiConfigurationService.GetScreenDesignLayoutAsync(screenCode, cancellationToken);

        return new StandardScreenDesignViewModel
        {
            ScreenCode = layout.ScreenCode,
            ScreenLabel = layout.ScreenLabel,
            Tabs = layout.Tabs
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.TabLabel, StringComparer.OrdinalIgnoreCase)
                .Select(x => new StandardScreenDesignTabInput
                {
                    TabKey = x.TabKey,
                    TabLabel = x.TabLabel,
                    DisplayOrder = x.DisplayOrder,
                    IsActive = x.IsActive
                })
                .ToList(),
            Items = layout.Items
                .OrderBy(x => x.TabKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.DisplayOrder)
                .ThenBy(x => x.ItemLabel, StringComparer.OrdinalIgnoreCase)
                .Select(x => new StandardScreenDesignItemInput
                {
                    ItemKey = x.ItemKey,
                    ItemLabel = x.ItemLabel,
                    TabKey = x.TabKey,
                    DisplayOrder = x.DisplayOrder,
                    ColumnSpan = x.ColumnSpan,
                    IsVisible = x.IsVisible,
                    IsRequired = x.IsRequired
                })
                .ToList()
        };
    }

    /// <summary>
    /// Eski "MaterialCards" (CamelCase) ile yeni snake_case ScreenCode'lari
    /// karsilastirilabilir forma getirir. DynamicFieldService'deki helper ile ayni.
    /// </summary>
    private static string NormalizeForScreenFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "items";
        var lower = raw.Trim().ToLowerInvariant();
        return lower switch
        {
            "materialcards"   => "items",
            "contactaccounts" => "contacts",
            "salesquotes"     => "documents",
            _ => lower,
        };
    }

    private async Task<MaterialCardViewSettingsViewModel> BuildMaterialCardViewSettingsViewModelAsync(
        Guid? selectedGroupId,
        Guid? selectedFieldId,
        CancellationToken cancellationToken,
        string? screenCode = null)
    {
        var schema = await _logisticsConfigurationService.GetMaterialCardDynamicSchemaAsync(cancellationToken);

        // Screen-code bazli filter: yalnizca sectigimiz ekrana (ornegin
        // sales_quotes) ait gruplar ve field'lar gosterilir. "items"
        // default ise MaterialCards widget'lari gelir. Eski kayitlarda
        // ScreenCode "MaterialCards" (CamelCase) olabilir — normalize edilmis.
        var targetScreenCode = string.IsNullOrWhiteSpace(screenCode)
            ? ScreenDesignCatalog.MaterialCardsScreenCode
            : ScreenDesignCatalog.NormalizeScreenCode(screenCode);

        bool ScreenMatches(string? storedCode) =>
            string.Equals(
                NormalizeForScreenFilter(storedCode),
                targetScreenCode,
                StringComparison.OrdinalIgnoreCase);

        var groups = schema.Groups
            .Where(x => ScreenMatches(x.ScreenCode))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.GroupLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var filteredFields = schema.Fields
            .Where(x => ScreenMatches(x.ScreenCode))
            .ToArray();
        var nextGroupDisplayOrder = groups.Length == 0 ? 10 : groups.Max(x => x.DisplayOrder) + 10;
        var nextFieldDisplayOrder = filteredFields.Length == 0 ? 10 : filteredFields.Max(x => x.DisplayOrder) + 10;
        var selectedGroup = selectedGroupId.HasValue
            ? groups.FirstOrDefault(x => x.Id == selectedGroupId.Value)
            : null;
        var selectedField = selectedFieldId.HasValue
            ? filteredFields.FirstOrDefault(x => x.Id == selectedFieldId.Value)
            : null;

        return new MaterialCardViewSettingsViewModel
        {
            Groups = groups
                .Select(x => new FieldGroupListItemViewModel
                {
                    Id = x.Id,
                    GroupKey = x.GroupKey,
                    GroupLabel = x.GroupLabel,
                    DisplayOrder = x.DisplayOrder,
                    IsActive = x.IsActive,
                    LayerKey = x.LayerKey
                })
                .ToList(),
            Fields = filteredFields
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.FieldLabel, StringComparer.OrdinalIgnoreCase)
                .Select(x => new MaterialCardFieldListItemViewModel
                {
                    Id = x.Id,
                    GroupId = x.GroupId,
                    GroupLabel = x.GroupId.HasValue
                        ? groups.FirstOrDefault(g => g.Id == x.GroupId.Value)?.GroupLabel ?? "-"
                        : "-",
                    FieldKey = x.FieldKey,
                    FieldLabel = x.FieldLabel,
                    DataType = x.DataType,
                    DefaultValue = x.DefaultValue,
                    DisplayOrder = x.DisplayOrder,
                    ColumnSpan = x.ColumnSpan,
                    IsVisible = x.IsVisible,
                    IsRequired = x.IsRequired,
                    IsSystem = x.IsSystem,
                    IsActive = x.IsActive,
                    LayerKey = x.LayerKey,
                    Options = x.Options
                        .OrderBy(o => o.SortOrder)
                        .ThenBy(o => o.OptionLabel, StringComparer.OrdinalIgnoreCase)
                        .Select(o => new MaterialCardFieldOptionListItemViewModel
                        {
                            Id = o.Id,
                            OptionKey = o.OptionKey,
                            OptionLabel = o.OptionLabel,
                            SortOrder = o.SortOrder,
                            IsActive = o.IsActive
                        })
                        .ToArray()
                })
                .ToList(),
            GroupInput = selectedGroup is null
                ? new FieldGroupInput
                {
                    DisplayOrder = nextGroupDisplayOrder
                }
                : new FieldGroupInput
                {
                    GroupId = selectedGroup.Id,
                    GroupKey = selectedGroup.GroupKey,
                    GroupLabel = selectedGroup.GroupLabel,
                    DisplayOrder = selectedGroup.DisplayOrder,
                    IsActive = selectedGroup.IsActive
                },
            FieldInput = selectedField is null
                ? new MaterialCardDynamicFieldInput
                {
                    GroupId = selectedGroup?.Id,
                    DisplayOrder = nextFieldDisplayOrder
                }
                : new MaterialCardDynamicFieldInput
                {
                    FieldId = selectedField.Id,
                    GroupId = selectedField.GroupId,
                    FieldKey = selectedField.FieldKey,
                    FieldLabel = selectedField.FieldLabel,
                    DataType = selectedField.DataType.ToString(),
                    IsVisible = selectedField.IsVisible,
                    IsRequired = selectedField.IsRequired,
                    DefaultValue = selectedField.DefaultValue,
                    DisplayOrder = selectedField.DisplayOrder,
                    ColumnSpan = selectedField.ColumnSpan,
                    IsActive = selectedField.IsActive,
                    IsSystem = selectedField.IsSystem,
                    Options = selectedField.Options
                        .OrderBy(o => o.SortOrder)
                        .Select(o => new MaterialCardFieldOptionInput
                        {
                            OptionId = o.Id,
                            OptionKey = o.OptionKey,
                            OptionLabel = o.OptionLabel,
                            SortOrder = o.SortOrder,
                            IsActive = o.IsActive
                        })
                        .ToList()
                },
            GroupOptions = groups
                .Where(x => x.IsActive || (selectedField?.GroupId.HasValue == true && selectedField.GroupId == x.Id))
                .Select(x => new SelectListItem(x.GroupLabel, x.Id.ToString(), selectedField?.GroupId == x.Id))
                .ToArray(),
            DataTypeOptions = Enum.GetValues<MaterialCardDynamicFieldDataType>()
                .Select(x => new SelectListItem(GetMaterialCardDataTypeLabel(x), x.ToString(), string.Equals(selectedField?.DataType.ToString(), x.ToString(), StringComparison.OrdinalIgnoreCase)))
                .ToArray()
        };
    }

    private async Task<AppearanceSettingsViewModel> BuildAppearanceSettingsViewModelAsync(
        string? formKey,
        string? editorLanguageCode,
        string? preferenceLanguageCode,
        string? themeCode,
        CancellationToken cancellationToken)
    {
        var currentUserId = GetCurrentUserId();
        var preference = await _uiConfigurationService.GetUserPreferenceAsync(currentUserId, cancellationToken);
        var supportedLanguages = _uiConfigurationService.GetSupportedLanguages();
        var supportedThemes = _uiConfigurationService.GetSupportedThemes();
        var supportedForms = _uiConfigurationService.GetSupportedForms();

        var selectedPreferenceLanguage = supportedLanguages
            .FirstOrDefault(x => string.Equals(x.Code, preferenceLanguageCode, StringComparison.OrdinalIgnoreCase))
            ?.Code ?? preference.LanguageCode;

        var selectedTheme = supportedThemes
            .FirstOrDefault(x => string.Equals(x.Code, themeCode, StringComparison.OrdinalIgnoreCase))
            ?.Code ?? preference.ThemeCode;

        var selectedFormKey = supportedForms
            .FirstOrDefault(x => string.Equals(x.FormKey, formKey, StringComparison.OrdinalIgnoreCase))
            ?.FormKey ?? supportedForms.FirstOrDefault()?.FormKey ?? "home.dashboard";

        var selectedEditorLanguage = supportedLanguages
            .FirstOrDefault(x => string.Equals(x.Code, editorLanguageCode, StringComparison.OrdinalIgnoreCase))
            ?.Code ?? selectedPreferenceLanguage;

        var labels = await _uiConfigurationService.GetLabelEditorEntriesAsync(
            selectedFormKey,
            selectedEditorLanguage,
            cancellationToken);

        return new AppearanceSettingsViewModel
        {
            LanguageCode = selectedPreferenceLanguage,
            ThemeCode = selectedTheme,
            SelectedFormKey = selectedFormKey,
            SelectedLanguageCode = selectedEditorLanguage,
            PreferenceLanguageOptions = supportedLanguages
                .Select(x => new SelectListItem(x.DisplayName, x.Code, x.Code == selectedPreferenceLanguage))
                .ToArray(),
            EditorLanguageOptions = supportedLanguages
                .Select(x => new SelectListItem(x.DisplayName, x.Code, x.Code == selectedEditorLanguage))
                .ToArray(),
            ThemeOptions = supportedThemes
                .Select(x => new SelectListItem(x.DisplayName, x.Code, x.Code == selectedTheme))
                .ToArray(),
            FormOptions = supportedForms
                .Select(x => new SelectListItem(x.DisplayName, x.FormKey, x.FormKey == selectedFormKey))
                .ToArray(),
            Labels = labels
                .Select(x => new UiLabelEditorInputModel
                {
                    LabelKey = x.LabelKey,
                    DefaultText = x.DefaultText,
                    CurrentText = x.CurrentText,
                    LabelText = x.OverrideText
                })
                .ToList()
        };
    }

    private Guid? GetCurrentUserId()
    {
        var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(rawUserId, out var userId) ? userId : null;
    }

    private async Task<int> ResolveGridPageSizeAsync(
        string gridKey,
        int? requestedPageSize,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var storedPageSize = await _uiConfigurationService.GetGridPageSizePreferenceAsync(
            userId,
            gridKey,
            20,
            cancellationToken);

        var resolvedPageSize = requestedPageSize.GetValueOrDefault() > 0
            ? requestedPageSize!.Value
            : storedPageSize;

        if (userId.HasValue && userId.Value != Guid.Empty && resolvedPageSize != storedPageSize)
        {
            await _uiConfigurationService.SaveGridPageSizePreferenceAsync(
                userId.Value,
                gridKey,
                resolvedPageSize,
                cancellationToken);
        }

        return resolvedPageSize;
    }

    private static GridListStateViewModel BuildGridListState(
        string gridKey,
        int page,
        int pageSize,
        int totalCount,
        int totalPages,
        string itemLabel,
        string pageParameterName = "page",
        string pageSizeParameterName = "pageSize") =>
        new()
        {
            GridKey = gridKey,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            ItemLabel = itemLabel,
            PageParameterName = pageParameterName,
            PageSizeParameterName = pageSizeParameterName,
            PageSizeOptions =
            [
                new SelectListItem("10", "10", pageSize == 10),
                new SelectListItem("20", "20", pageSize == 20),
                new SelectListItem("30", "30", pageSize == 30),
                new SelectListItem("50", "50", pageSize == 50),
                new SelectListItem("100", "100", pageSize == 100)
            ]
        };

    private string ResolveProjectInstructionsPath() =>
        ResolveProjectRootFilePath("PROJECT_INSTRUCTIONS.txt");

    private string ResolveProjectRequestsPath() =>
        ResolveProjectRootFilePath("PROJECT_REQUESTS.json");

    private string ResolveProjectRootFilePath(string fileName)
    {
        var currentDirectory = new DirectoryInfo(_webHostEnvironment.ContentRootPath);

        while (currentDirectory is not null)
        {
            var candidatePath = Path.Combine(currentDirectory.FullName, fileName);
            if (System.IO.File.Exists(candidatePath))
            {
                return candidatePath;
            }

            if (currentDirectory.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any())
            {
                return candidatePath;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return Path.Combine(_webHostEnvironment.ContentRootPath, fileName);
    }

    private sealed class ProjectInstructionRequestDocument
    {
        public List<ProjectInstructionRequestItemInput> Items { get; init; } = [];
    }

    // NOT: TestCompanyDatabaseConnection ConnectivityTestsController'a tasindi (rapor 2.3 split).

    // NOT: Locks + LocksBoardConfig + BuildLocksBoardConfig LocksController'a tasindi (rapor 2.3 split).

    private void SetActiveMenu(string key)
    {
        ViewData["AdminMenu"] = key;
    }

    // ── Entegrasyon API Profilleri ───────────────────────────────────────────
    // Eski "Entegrasyon Tanimlari" (integration_event_*) ekrani kaldirildi.
    // Tum entegrasyon islemleri yeni IntegrationsHub / IntegrationWizard
    // uzerinden yapilir. API profil CRUD endpoint'leri wizard tarafindan
    // kullanildigi icin korundu.

    private int GetCompanyId()
    {
        var raw = User.FindFirstValue("company_id");
        return int.TryParse(raw, out var id) ? id : 0;
    }
    // NOT: TestRestApiConnectionJson + GetApiProfilesJson + GetIntegrationEndpointCatalogJson + SaveApiProfileJson + DeleteApiProfileJson ApiProfileController'a tasindi (rapor 2.3 split).

    // NOT: GetAdminUsersJson/GetUsersFormDataJson/SaveAdminUserJson/UpdateAdminUserJson → AdminUserJsonController
    // NOT: GetAppearanceFormDataJson/GetFormLabelsJson/SaveFormLabelsJson → AppearanceLabelsController
    // NOT: GetIntegratorFormDataJson/GetIntegratorsListJson/GetIntegratorJson/SaveIntegratorSettingsJson/DeleteIntegratorSettingsJson/TestIntegratorConnectionJson/PullIntegratorDataJson → IntegratorSettingsJsonController (rapor 2.3 split)
}

public sealed class TestRestApiInput
{
    public string? ApiConfigJson { get; set; }
}


public sealed class IntegratorSettingsJsonInput
{
    public int? Id { get; set; }
    public int? CompanyId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string CompanyTaxNumber { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public int PollingIntervalSeconds { get; set; } = 120;
    public int MaxRecordsPerPull { get; set; } = 200;
    public int LogRetentionDays { get; set; } = 30;
    public bool IncludeReceivedDocumentsInPull { get; set; }
    public bool MarkDownloadedDocumentsAsReceived { get; set; }
    public bool IncludeIssuedEInvoicesInPull { get; set; }
    public bool IncludeIssuedEArchivesInPull { get; set; }
    public bool IncludeIssuedEDispatchesInPull { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ScheduleEnabled { get; set; }
    public string? AppStr { get; set; }
    public string? Source { get; set; }
    public string? AppVersion { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int LookbackDays { get; set; } = 30;
}

public sealed class SaveFormLabelsJsonInput
{
    public string FormKey { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "tr-TR";
    public List<LabelEntry>? Labels { get; set; }
    public sealed class LabelEntry { public string Key { get; set; } = string.Empty; public string Text { get; set; } = string.Empty; }
}

public sealed class TestCompanyDatabaseConnectionRequest
{
    public string? ConnectionString { get; set; }
}

public sealed class ApiProfileInput
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? AuthType { get; set; }
    public string? BaseUrl { get; set; }
    public string? AuthConfigJson { get; set; }
    public bool IsActive { get; set; } = true;
    public string? ProviderCode { get; set; }
}
