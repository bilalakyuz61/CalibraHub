using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Security;
using CalibraHub.Application.Ui;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Options;
using CalibraHub.Persistence.Security;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Text.Json;

namespace CalibraHub.Persistence.Database;

public sealed class CalibraDatabaseInitializer
{
    private const int DefaultCompanyId = 1;
    private static readonly Guid FinanceDepartmentId = Guid.Parse("8ad68ef8-63f8-4a26-a7fd-62c4fcbac120");
    private static readonly Guid OperationsDepartmentId = Guid.Parse("f744af50-51f8-4f74-a89b-66bc99c79c30");
    private static readonly Guid AdminUserId = Guid.Parse("0dbb6f1d-9a15-4f6f-b1f0-661bb6b43ec2");

    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IPasswordHashService _passwordHashService;
    private readonly string _schema;
    private readonly bool _autoCreateDatabaseOnStartup;
    private readonly BootstrapAdminOptions _bootstrapAdminOptions;

    public CalibraDatabaseInitializer(
        SqlServerConnectionFactory connectionFactory,
        IPasswordHashService passwordHashService,
        CalibraDatabaseOptions options,
        BootstrapAdminOptions bootstrapAdminOptions)
    {
        _connectionFactory = connectionFactory;
        _passwordHashService = passwordHashService;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _autoCreateDatabaseOnStartup = options.AutoCreateDatabaseOnStartup;
        _bootstrapAdminOptions = bootstrapAdminOptions;
    }

    /// <summary>
    /// Belirli bir (sirket) connection string'i uzerinden rehber tablolarini +
    /// cbv_Guide_* view'larini tazeler. CREATE OR ALTER VIEW idempotent —
    /// eski sema ile olusturulmus company DB'lerini gunceller.
    /// </summary>
    public async Task EnsureGuideSchemaForConnectionAsync(
        string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureGuideTablesAsync(connection, cancellationToken);
    }

    /// <summary>
    /// FastReport raporlari icin tek kapsamli belge view'i ve onu uretecek
    /// stored proc'u (sp_Report_RebuildDocumentView) per-company DB'de kurar.
    /// Proc INFORMATION_SCHEMA uzerinden v_Flat_SALES_QUOTE_EDIT ve
    /// v_Flat_SALES_QUOTE_LINES'daki widget kolonlarini hw_* / lw_* prefix ile
    /// re-aliaslayip vw_ReportDocument view'ini dinamik SQL ile olusturur.
    ///
    /// Idempotent: CREATE OR ALTER ile her startup'ta guvenle tekrar calisir.
    /// Master DB'deki Company tablosuna 3-parcali isimle erisim icin
    /// <paramref name="systemDatabaseName"/> cagirandan gecirilir.
    /// </summary>
    public async Task EnsureReportDocumentViewAsync(
        string connectionString, string systemDatabaseName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        if (string.IsNullOrWhiteSpace(systemDatabaseName))
            systemDatabaseName = "master"; // guvenli fallback

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await CreateReportDocumentProcAsync(connection, systemDatabaseName, cancellationToken);
        await RebuildReportDocumentViewAsync(connection, cancellationToken);
    }

    private static async Task CreateReportDocumentProcAsync(
        SqlConnection connection, string systemDatabaseName, CancellationToken ct)
    {
        // Identifier dogrulama — SQL injection onle
        if (systemDatabaseName.IndexOf(']') >= 0)
            systemDatabaseName = systemDatabaseName.Replace("]", "]]");

        // Stored proc govdesi. Icinde dinamik SQL ile view uretilir; widget
        // kolonlari v_Flat_* view'larindan hw_<code> / lw_<code> prefix ile alinir.
        // NOT: Bu string C#'ta raw literal — icerideki tek tirnakli SQL literal'leri
        // TEK tirnaktir (C# katmani cift tirnakla ayirildigindan iclerine gerek yok).
        var procSql = $@"
CREATE OR ALTER PROCEDURE [dbo].[sp_Report_RebuildDocumentView]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @DocCols TABLE (name SYSNAME PRIMARY KEY);
    INSERT INTO @DocCols
    SELECT c.[name]
    FROM sys.columns c
    INNER JOIN sys.objects o ON o.[object_id] = c.[object_id]
    WHERE o.[name] = N'Document' AND o.[schema_id] = SCHEMA_ID(N'dbo');

    DECLARE @LineCols TABLE (name SYSNAME PRIMARY KEY);
    INSERT INTO @LineCols
    SELECT c.[name]
    FROM sys.columns c
    INNER JOIN sys.objects o ON o.[object_id] = c.[object_id]
    WHERE o.[name] = N'DocumentLine' AND o.[schema_id] = SCHEMA_ID(N'dbo');

    DECLARE @HwColsSql NVARCHAR(MAX) = N'';
    SELECT @HwColsSql = @HwColsSql + N',    hw.[' + REPLACE(c.[name], N']', N']]') + N']'
                      + N' AS [UstAlan_' + REPLACE(c.[name], N']', N']]') + N']' + CHAR(13) + CHAR(10)
    FROM sys.columns c
    INNER JOIN sys.objects o ON o.[object_id] = c.[object_id]
    WHERE o.[schema_id] = SCHEMA_ID(N'dbo')
      AND o.[name]      = N'v_Flat_SALES_QUOTE_EDIT'
      AND o.[type]      = N'V '
      AND c.[name] NOT IN (SELECT name FROM @DocCols);

    DECLARE @LwColsSql NVARCHAR(MAX) = N'';
    SELECT @LwColsSql = @LwColsSql + N',    lw.[' + REPLACE(c.[name], N']', N']]') + N']'
                      + N' AS [KalemAlan_' + REPLACE(c.[name], N']', N']]') + N']' + CHAR(13) + CHAR(10)
    FROM sys.columns c
    INNER JOIN sys.objects o ON o.[object_id] = c.[object_id]
    WHERE o.[schema_id] = SCHEMA_ID(N'dbo')
      AND o.[name]      = N'v_Flat_SALES_QUOTE_LINES'
      AND o.[type]      = N'V '
      AND c.[name] NOT IN (SELECT name FROM @LineCols);

    DECLARE @HasHwView BIT = CASE WHEN OBJECT_ID(N'dbo.v_Flat_SALES_QUOTE_EDIT', N'V') IS NOT NULL THEN 1 ELSE 0 END;
    DECLARE @HasLwView BIT = CASE WHEN OBJECT_ID(N'dbo.v_Flat_SALES_QUOTE_LINES', N'V') IS NOT NULL THEN 1 ELSE 0 END;

    DECLARE @HwJoin NVARCHAR(MAX) = CASE WHEN @HasHwView = 1
        THEN N'LEFT JOIN [dbo].[v_Flat_SALES_QUOTE_EDIT]  hw ON hw.[id] = d.[id]' + CHAR(13) + CHAR(10)
        ELSE N'' END;
    DECLARE @LwJoin NVARCHAR(MAX) = CASE WHEN @HasLwView = 1
        THEN N'LEFT JOIN [dbo].[v_Flat_SALES_QUOTE_LINES] lw ON lw.[id] = dl.[id]' + CHAR(13) + CHAR(10)
        ELSE N'' END;

    IF @HasHwView = 0 SET @HwColsSql = N'';
    IF @HasLwView = 0 SET @LwColsSql = N'';

    DECLARE @Sql NVARCHAR(MAX) = N'
CREATE OR ALTER VIEW [dbo].[vw_ReportDocument]
AS
SELECT
    d.[id]                                 AS BelgeId,
    d.[DocumentNumber]                    AS BelgeNo,
    d.[DocumentTypeId]                   AS BelgeTurId,
    dt.[code]                              AS BelgeTurKodu,
    dt.[name]                              AS BelgeTurAdi,
    d.[CompanyId]                         AS BelgeSirketId,
    d.[DocumentDate]                      AS BelgeTarihi,
    d.[ValidUntil]                        AS GecerlilikTarihi,
    d.[currency]                           AS ParaBirimi,
    d.[SubTotal]                          AS AraToplam,
    d.[DiscountRate]                      AS IskontoOrani,
    d.[DiscountAmount]                    AS IskontoTutari,
    d.[TaxRate]                           AS KdvOrani,
    d.[TaxAmount]                         AS KdvTutari,
    d.[GrandTotal]                        AS GenelToplam,
    d.[PaymentTerms]                      AS OdemeKosullari,
    d.[DeliveryTerms]                     AS TeslimKosullari,
    d.[DeliveryAddress]                   AS TeslimatAdresi,
    d.[status]                             AS BelgeDurumu,
    d.[RevisionNo]                        AS RevizyonNo,
    d.[notes]                              AS BelgeNotu,
    d.[Created]                         AS OlusturulmaTarihi,
    d.[Updated]                         AS GuncellenmeTarihi,

    d.[ContactId]                         AS CariId,
    c.[AccountCode]                        AS CariKodu,
    c.[AccountTitle]                       AS CariUnvani,
    c.[TaxOffice]                          AS CariVergiDairesi,
    c.[TaxNumber]                          AS CariVergiNo,
    c.[Phone]                              AS CariTelefon,
    c.[Mobile]                             AS CariCep,
    c.[Email]                              AS CariEposta,
    c.[Website]                            AS CariWebSitesi,
    c.[Address]                            AS CariAdres,
    c.[PostalCode]                         AS CariPostaKodu,
    c.[City]                               AS CariSehir,
    c.[District]                           AS CariIlce,

    d.[SalesRepId]                       AS TemsilciId,
    sr.[rep_code]                          AS TemsilciKodu,
    sr.[rep_name]                          AS TemsilciAdi,

    comp.[name]                            AS SirketAdi,
    comp.[title]                           AS SirketUnvani,
    comp.[address]                         AS SirketAdresi,
    comp.[city]                            AS SirketSehir,
    comp.[district]                        AS SirketIlce,
    comp.[postal_code]                     AS SirketPostaKodu,
    comp.[tax_office]                      AS SirketVergiDairesi,
    comp.[tax_number]                      AS SirketVergiNo,
    comp.[is_e_document_approval_enabled]  AS SirketEBelgeAktif,
    CONCAT(comp.[address],
        CASE WHEN comp.[district]    IS NOT NULL THEN N'' / '' + comp.[district]    ELSE N'''' END,
        CASE WHEN comp.[city]        IS NOT NULL THEN N'' / '' + comp.[city]        ELSE N'''' END,
        CASE WHEN comp.[postal_code] IS NOT NULL THEN N'' ''   + comp.[postal_code] ELSE N'''' END
    )                                      AS SirketTamAdres,
    CONCAT(comp.[tax_office], N'' V.D. '', comp.[tax_number])
                                           AS SirketVergiSatiri,

    dl.[id]                                AS KalemId,
    dl.[line_no]                           AS SiraNo,
    dl.[quantity]                          AS Miktar,
    dl.[unit_price]                        AS BirimFiyat,
    dl.[discount_rate]                     AS KalemIskontoOrani,
    dl.[line_total]                        AS SatirToplami,
    dl.[combination_id]                    AS KombinasyonId,
    dl.[notes]                             AS KalemNotu,
    dl.[notes_pinned]                      AS KalemNotuSabitli,

    dl.[item_id]                           AS MalzemeId,
    i.[Code]                               AS MalzemeKodu,
    i.[Name]                               AS MalzemeAdi,
    i.[TaxRate]                            AS MalzemeKdvOrani,

    dl.[unit_id]                           AS BirimId,
    mu.[UnitCode]                          AS BirimKodu,
    mu.[UnitName]                          AS BirimAdi,

    dl.[location_id]                       AS LokasyonId,
    loc.[LocationCode]                     AS LokasyonKodu,
    loc.[LocationName]                     AS LokasyonAdi,

    -- Kombinasyon detaylarini tek satirda birlestir — Designer Community Edition
    -- Detail Data band desteklemedigi icin kullanisli. Ornek: Boy - 1200 / Renk - Kirmizi
    STUFF((
        SELECT N'' / '' + CONCAT(sqld.[feature_name], N'' - '', sqld.[value_name])
        FROM [dbo].[sales_quote_line_details] sqld
        WHERE sqld.[quote_line_id] = dl.[id]
        ORDER BY sqld.[line_order]
        FOR XML PATH(''''), TYPE
    ).value(N''.'', N''nvarchar(max)''), 1, 3, N'''')       AS KombinasyonOzet,

    -- Sadece deger kisimlari (virgulle ayrilmis) — etiketsiz, kisa gosterim
    STUFF((
        SELECT N'', '' + sqld.[value_name]
        FROM [dbo].[sales_quote_line_details] sqld
        WHERE sqld.[quote_line_id] = dl.[id]
        ORDER BY sqld.[line_order]
        FOR XML PATH(''''), TYPE
    ).value(N''.'', N''nvarchar(max)''), 1, 2, N'''')       AS KombinasyonDegerleri,

    -- Belge ozelinde girilen aciklamalar (sales_quote_line_details.description)
    -- Sadece NULL/bos olmayanlari virgulle birlestirir. Ornek: 4 kat koruma, ozel siparis
    STUFF((
        SELECT N'', '' + sqld.[description]
        FROM [dbo].[sales_quote_line_details] sqld
        WHERE sqld.[quote_line_id] = dl.[id]
          AND sqld.[description] IS NOT NULL
          AND LTRIM(RTRIM(sqld.[description])) <> N''''
        ORDER BY sqld.[line_order]
        FOR XML PATH(''''), TYPE
    ).value(N''.'', N''nvarchar(max)''), 1, 2, N'''')       AS KombinasyonAciklamalari,

    -- Tam detay: Her ozellik ayri satirda (CR+LF ile). FastReport TextObject
    -- WordWrap+CanGrow ile multi-line gosterir. Format:
    --   - Boy - 1200 (4 kat koruma)
    --   - Renk - Kirmizi
    STUFF((
        SELECT NCHAR(13) + NCHAR(10) + N''- '' + CONCAT(sqld.[feature_name], N'' - '', sqld.[value_name],
            CASE WHEN sqld.[description] IS NOT NULL
                  AND LTRIM(RTRIM(sqld.[description])) <> N''''
                 THEN N'' ('' + sqld.[description] + N'')''
                 ELSE N''''
            END)
        FROM [dbo].[sales_quote_line_details] sqld
        WHERE sqld.[quote_line_id] = dl.[id]
        ORDER BY sqld.[line_order]
        FOR XML PATH(''''), TYPE
    ).value(N''.'', N''nvarchar(max)''), 1, 2, N'''')       AS KombinasyonDetay
' + @HwColsSql + @LwColsSql + N'
FROM [dbo].[Document] d
LEFT JOIN [dbo].[DocumentLine]           dl   ON dl.[document_id]     = d.[id]
LEFT JOIN [dbo].[Contact]                c    ON c.[Id]               = d.[ContactId]
LEFT JOIN [dbo].[sales_representatives]  sr   ON sr.[id]              = d.[SalesRepId]
LEFT JOIN [dbo].[document_types]         dt   ON dt.[id]              = d.[DocumentTypeId]
LEFT JOIN [dbo].[Items]                  i    ON i.[Id]               = dl.[item_id]
LEFT JOIN [dbo].[Unit]                   mu   ON mu.[Id]              = dl.[unit_id]
LEFT JOIN [dbo].[Location]               loc  ON loc.[Id]             = dl.[location_id]
' + @HwJoin + @LwJoin + N'
LEFT JOIN [{systemDatabaseName}].[dbo].[Company] comp ON comp.[id] = d.[CompanyId];';

    EXEC sp_executesql @Sql;

    -- ============================================================
    -- vw_DocumentCombination — kalem bazinda kombinasyon detaylari
    -- ============================================================
    -- Her satir = bir kombinasyon ozelligi. 1 kalemde 3 ozellik varsa 3 satir doner.
    -- BelgeId ile filtrelenerek bir belgenin tum kalemlerinin tum kombinasyon
    -- detaylari alinir. KalemId ile gruplanabilir (master-detail kullanimi).
    -- NOT: FastReport OpenSource DataBand.MasterData destekemedigi icin
    -- tek view + GroupHeader yaklasimi kullaniliyor. Bu view artik DocumentLine
    -- taban alir (LEFT JOIN sales_quote_line_details), kombinasyonu olmayan
    -- kalemler bile 1 satir doner (kombinasyon kolonlari NULL). Kalem kolonlari
    -- (Miktar, BirimFiyat, SatirToplami, BirimKodu) GroupHeader icin eklendi.
    DECLARE @SqlCmb NVARCHAR(MAX) = N'
CREATE OR ALTER VIEW [dbo].[vw_DocumentCombination]
AS
SELECT
    d.[id]                       AS BelgeId,
    d.[DocumentNumber]           AS BelgeNo,
    dl.[id]                      AS KalemId,
    dl.[line_no]                 AS KalemSiraNo,
    i.[Code]                     AS MalzemeKodu,
    i.[Name]                     AS MalzemeAdi,
    dl.[quantity]                AS Miktar,
    dl.[unit_price]              AS BirimFiyat,
    dl.[line_total]              AS SatirToplami,
    mu.[UnitCode]                AS BirimKodu,
    sqld.[id]                    AS DetayId,
    sqld.[line_order]            AS SiraNo,
    sqld.[feature_name]          AS OzellikAdi,
    sqld.[value_code]            AS DegerKodu,
    sqld.[value_name]            AS DegerAdi,
    sqld.[description]           AS Aciklama
FROM [dbo].[DocumentLine] dl
INNER JOIN [dbo].[Document]     d  ON d.[id]          = dl.[document_id]
LEFT  JOIN [dbo].[sales_quote_line_details] sqld ON sqld.[quote_line_id] = dl.[id]
LEFT  JOIN [dbo].[Items]        i  ON i.[Id]          = dl.[item_id]
LEFT  JOIN [dbo].[Unit]         mu ON mu.[Id]         = dl.[unit_id];';

    EXEC sp_executesql @SqlCmb;
END;";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = procSql;
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RebuildReportDocumentViewAsync(
        SqlConnection connection, CancellationToken ct)
    {
        // v_Flat_* view'lar yoksa proc null widget listesiyle calisir — sorun yok.
        // Gerekli base tablolar yoksa (ornek Document henuz yaratilmadiysa) view
        // olusturulamaz — try/catch Program.cs katmaninda yapilir.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC [dbo].[sp_Report_RebuildDocumentView];";
        cmd.CommandTimeout = 60;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_autoCreateDatabaseOnStartup)
        {
            await _connectionFactory.EnsureDatabaseExistsAsync(cancellationToken);
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        try
        {
            await MigrateUtcColumnNamesAsync(connection, cancellationToken);
            // Tum tablolarda created_at -> Created, updated_at -> Updated rename (idempotent).
            // En basta calismali — sonraki migration'lar [Created]/[Updated] referansi veriyor.
            await RenameLegacyTimestampColumnsAsync(connection, cancellationToken);
            // Tum tablolarda Updated kolonunu nullable yap — ilk INSERT'te NULL kalabilir,
            // sadece UPDATE sirasinda doldurulur.
            await MakeUpdatedNullableAsync(connection, cancellationToken);
            await MigrateItemsTableAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateItemsTableAsync completed successfully.");
            await MigrateTableRenamesAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateTableRenamesAsync completed successfully.");
            await MigrateColumnRenamesAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateColumnRenamesAsync completed successfully.");
            await MigrateScreenCodesAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateScreenCodesAsync completed successfully.");
            await MigrateDocumentPkToIntAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateDocumentPkToIntAsync completed successfully.");
            await MigrateIntegratorSettingsTableAsync(connection, cancellationToken);
            await EnsureSchemaAndTablesAsync(connection, cancellationToken);
            await EnsureIntegratorLoginColumnsAsync(connection, cancellationToken);
            await EnsureCompanySchemaAsync(connection, cancellationToken);
            await EnsurePltSystemLogTableAsync(connection, cancellationToken);
            await EnsureNotesTablesAsync(connection, cancellationToken);
            await EnsureNoteExtensionsAsync(connection, cancellationToken);
            await EnsureBOMTablesAsync(connection, cancellationToken);
            await EnsureMaterialGroupTablesAsync(connection, cancellationToken);
            await EnsureFinanceTablesAsync(connection, cancellationToken);
            await EnsureAddressTablesAsync(connection, cancellationToken);
            await EnsureContactItemTableAsync(connection, cancellationToken);
            await EnsureDesignTemplatesTableAsync(connection, cancellationToken);
            await EnsureIntegrationEventTablesAsync(connection, cancellationToken);
            await MigrateIntegrationEventsTableAsync(connection, cancellationToken);
            await EnsureIntegrationApiProfilesTableAsync(connection, cancellationToken);
            await EnsureDynamicFieldValuesTableAsync(connection, cancellationToken);
            await EnsureDocumentTablesAsync(connection, cancellationToken);
            await MigrateDocumentContactNameDropAsync(connection, cancellationToken);
            await EnsureDocumentAttachmentsTableAsync(connection, cancellationToken);
            await EnsureDocumentTypesTableAsync(connection, cancellationToken);
            await EnsureReportTemplatesTableAsync(connection, cancellationToken);
            await EnsureReportTemplateSourcesTableAsync(connection, cancellationToken);
            await EnsureScheduledTasksTableAsync(connection, cancellationToken);
            await EnsureLicenseConfigTableAsync(connection, cancellationToken);
            await EnsureGateCredentialsTableAsync(connection, cancellationToken);
            await EnsureWhatsAppConfigTableAsync(connection, cancellationToken);
            await SeedDocumentTypesAsync(connection, cancellationToken);
            await BackfillDocumentTypeIdsAsync(connection, cancellationToken);
            await EnsureReportDataViewsAsync(connection, cancellationToken);
            await EnsureUserSettingsTableAsync(connection, cancellationToken);
            await EnsureSalesRepresentativeTableAsync(connection, cancellationToken);
            await EnsureCurrencyTablesAsync(connection, cancellationToken);
            await SeedCurrenciesAsync(connection, cancellationToken);
            await EnsureDocumentLineDetailsTableAsync(connection, cancellationToken);
            await EnsurePriceListTablesAsync(connection, cancellationToken);
            await EncryptLegacyIntegratorSecretsAsync(connection, cancellationToken);
            await EncryptLegacySmtpPasswordsAsync(connection, cancellationToken);
            await SeedFieldsAsync(connection, cancellationToken);
            await SeedScreenDesignLayoutsAsync(connection, cancellationToken);
            await SeedDepartmentsAsync(connection, cancellationToken);
            await SeedAdminUserAsync(connection, cancellationToken);
            await EnsureFormsTableAsync(connection, cancellationToken);
            await SeedFormsAsync(connection, cancellationToken);
            await EnsureWidgetEavTablesAsync(connection, cancellationToken);
            await EnsureGuideTablesAsync(connection, cancellationToken);
            await EnsureFieldSettingsTableAsync(connection, cancellationToken);
            await EnsureContactColumnsAsync(connection, cancellationToken);
            await EnsureOrgChartTablesAsync(connection, cancellationToken);
            await EnsureRptViewTableAsync(connection, cancellationToken);
            await EnsureRptViewColumnTableAsync(connection, cancellationToken);
            await EnsureRptDefinitionTableAsync(connection, cancellationToken);
            await EnsureRptDefinitionRoleTableAsync(connection, cancellationToken);
            await EnsureRptViewRoleTableAsync(connection, cancellationToken);
            await EnsureRptRunLogTableAsync(connection, cancellationToken);
            await SeedRptViewRegistryAsync(connection, cancellationToken);
            await EnsureProductionInfrastructureAsync(connection, cancellationToken);
            await EnsureDocLayoutTableAsync(connection, cancellationToken);
            await EnsureDocLayoutDsTableAsync(connection, cancellationToken);
        }
        catch (SqlException sqlEx)
        {
            Console.Error.WriteLine($"[DB INIT ERROR] SqlException {sqlEx.Number}: {sqlEx.Message}");
            Console.Error.WriteLine($"[DB INIT ERROR] Procedure: {sqlEx.Procedure}");
            throw;
        }
    }

    /// <summary>
    /// Renames legacy UTC-suffixed columns to local-time names (e.g. created_at_utc → created_at).
    /// Safe to run repeatedly – each rename is guarded by a COL_LENGTH check.
    /// </summary>
    private async Task MigrateUtcColumnNamesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");

        // (table, oldColumn, newColumn)
        var renames = new (string Table, string Old, string New)[]
        {
            ("ui_label_translations", "updated_at_utc", "updated_at"),
            ("integrator_settings", "created_at_utc", "created_at"),
            ("integrator_settings", "updated_at_utc", "updated_at"),
            ("smtp_profiles", "created_at_utc", "created_at"),
            ("smtp_profiles", "updated_at_utc", "updated_at"),
            ("erp_connection_settings", "created_at_utc", "created_at"),
            ("erp_connection_settings", "updated_at_utc", "updated_at"),
            ("incoming_documents", "imported_at_utc", "imported_at"),
            ("MaterialDefinitions", "created_at_utc", "created_at"),
            ("MaterialDefinitions", "updated_at_utc", "updated_at"),
            ("material_card_field_groups", "created_at_utc", "created_at"),
            ("material_card_field_groups", "updated_at_utc", "updated_at"),
            ("material_card_field_settings", "created_at_utc", "created_at"),
            ("material_card_field_settings", "updated_at_utc", "updated_at"),
            ("material_card_field_options", "created_at_utc", "created_at"),
            ("material_card_field_options", "updated_at_utc", "updated_at"),
            ("screen_layout_definitions", "created_at_utc", "created_at"),
            ("screen_layout_definitions", "updated_at_utc", "updated_at"),
            ("configuration_properties", "created_at_utc", "created_at"),
            ("configuration_properties", "updated_at_utc", "updated_at"),
            ("configuration_property_values", "created_at_utc", "created_at"),
            ("configuration_property_values", "updated_at_utc", "updated_at"),
            ("stock_card_property_mappings", "created_at_utc", "created_at"),
            ("stock_card_property_mappings", "updated_at_utc", "updated_at"),
            ("Locations", "created_at_utc", "created_at"),
            ("Locations", "updated_at_utc", "updated_at"),
            ("measure_unit_definitions", "created_at_utc", "created_at"),
            ("measure_unit_definitions", "updated_at_utc", "updated_at"),
            // PascalCase tables use PascalCase columns
            ("measure_unit_definitions", "CreatedAtUtc", "CreatedAt"),
            ("measure_unit_definitions", "UpdatedAtUtc", "UpdatedAt"),
            ("Locations", "CreatedAtUtc", "CreatedAt"),
            ("Locations", "UpdatedAtUtc", "UpdatedAt"),
            ("MaterialDefinitions", "CreatedAtUtc", "CreatedAt"),
            ("MaterialDefinitions", "UpdatedAtUtc", "UpdatedAt"),
            ("ItemConfiguration", "CreatedAtUtc", "CreatedAt"),
            ("ItemConfiguration", "UpdatedAtUtc", "UpdatedAt"),
            ("company", "created_at_utc", "created_at"),
            ("company", "updated_at_utc", "updated_at"),
            ("document_approvals", "action_date_utc", "action_date"),
            ("PLT_SISTEM_LOG", "occurred_at_utc", "occurred_at"),
            ("PLT_SISTEM_LOG", "OccurredAtUtc", "occurred_at"),
            // Document tablosu — snake_case → PascalCase (kullanici karari).
            ("Document", "company_id",         "CompanyId"),
            ("Document", "document_number",    "DocumentNumber"),
            ("Document", "document_type_id",   "DocumentTypeId"),
            ("Document", "document_date",      "DocumentDate"),
            ("Document", "valid_until",        "ValidUntil"),
            ("Document", "contact_id",         "ContactId"),
            ("Document", "contact_address",    "ContactAddress"),
            ("Document", "sales_rep_id",       "SalesRepId"),
            ("Document", "sub_total",          "SubTotal"),
            ("Document", "discount_rate",      "DiscountRate"),
            ("Document", "discount_amount",    "DiscountAmount"),
            ("Document", "tax_rate",           "TaxRate"),
            ("Document", "tax_amount",         "TaxAmount"),
            ("Document", "grand_total",        "GrandTotal"),
            ("Document", "payment_terms",      "PaymentTerms"),
            ("Document", "delivery_terms",     "DeliveryTerms"),
            ("Document", "delivery_address",   "DeliveryAddress"),
            ("Document", "revision_no",        "RevisionNo"),
            ("Document", "parent_document_id", "ParentDocumentId"),
            ("Document", "created_by",         "CreatedBy"),
            ("Document", "is_active",          "IsActive"),
        };

        var sb = new System.Text.StringBuilder();
        foreach (var (table, oldCol, newCol) in renames)
        {
            var tableLiteral = table.Replace("'", "''");
            var tableForSql = table.Replace("]", "]]");
            sb.AppendLine($"""
                IF COL_LENGTH(N'{sl}.{tableLiteral}', N'{oldCol}') IS NOT NULL
                   AND COL_LENGTH(N'{sl}.{tableLiteral}', N'{newCol}') IS NULL
                BEGIN
                    EXEC sp_rename N'[{s}].[{tableForSql}].[{oldCol}]', N'{newCol}', N'COLUMN';
                END;
                """);
        }

        // Phase 2: Convert DATETIMEOFFSET columns to DATETIME2 (after rename)
        // For each (table, column) pair that now exists, alter type if still datetimeoffset.
        var typeConversions = new (string Table, string Column)[]
        {
            ("ui_label_translations", "updated_at"),
            ("integrator_settings", "created_at"),
            ("integrator_settings", "updated_at"),
            ("smtp_profiles", "created_at"),
            ("smtp_profiles", "updated_at"),
            ("erp_connection_settings", "created_at"),
            ("erp_connection_settings", "updated_at"),
            ("incoming_documents", "imported_at"),
            ("MaterialDefinitions", "created_at"),
            ("MaterialDefinitions", "updated_at"),
            ("material_card_field_groups", "created_at"),
            ("material_card_field_groups", "updated_at"),
            ("material_card_field_settings", "created_at"),
            ("material_card_field_settings", "updated_at"),
            ("material_card_field_options", "created_at"),
            ("material_card_field_options", "updated_at"),
            ("screen_layout_definitions", "created_at"),
            ("screen_layout_definitions", "updated_at"),
            ("configuration_properties", "created_at"),
            ("configuration_properties", "updated_at"),
            ("configuration_property_values", "created_at"),
            ("configuration_property_values", "updated_at"),
            ("stock_card_property_mappings", "created_at"),
            ("stock_card_property_mappings", "updated_at"),
            ("measure_unit_definitions", "CreatedAt"),
            ("measure_unit_definitions", "UpdatedAt"),
            ("Locations", "CreatedAt"),
            ("Locations", "UpdatedAt"),
            ("Items", "CreatedAt"),
            ("Items", "UpdatedAt"),
            ("MaterialDefinitions", "CreatedAt"),
            ("MaterialDefinitions", "UpdatedAt"),
            ("company", "created_at"),
            ("company", "updated_at"),
            ("PLT_SISTEM_LOG", "occurred_at"),
        };

        // Execute Phase 1 renames first
        if (sb.Length > 0)
        {
            await using var renameCmd = connection.CreateCommand();
            renameCmd.CommandText = sb.ToString();
            await renameCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Phase 2: Each column is altered in its own batch so DECLARE @dropIdx has no conflicts
        // and dependent non-PK indexes are dropped before ALTER COLUMN, then recreated by EnsureSchemaAndTablesAsync.
        foreach (var (table, col) in typeConversions)
        {
            var tableLiteral = table.Replace("'", "''");
            var tableForSql = table.Replace("]", "]]");
            var colLiteral = col.Replace("'", "''");
            var colForSql = col.Replace("]", "]]");
            var alterSql = $"""
                IF COL_LENGTH(N'{sl}.{tableLiteral}', N'{colLiteral}') IS NOT NULL
                   AND EXISTS (
                       SELECT 1 FROM sys.columns c
                       JOIN sys.types t ON c.system_type_id = t.system_type_id AND c.user_type_id = t.user_type_id
                       WHERE c.[object_id] = OBJECT_ID(N'[{s}].[{tableForSql}]')
                         AND c.[name] = N'{colLiteral}'
                         AND t.[name] = N'datetimeoffset'
                   )
                BEGIN
                    DECLARE @dropIdx NVARCHAR(MAX) = N'';
                    SELECT @dropIdx = @dropIdx + N'DROP INDEX [' + i.[name] + N'] ON [{s}].[{tableForSql}];'
                    FROM sys.indexes i
                    INNER JOIN sys.index_columns ic ON i.[object_id] = ic.[object_id] AND i.index_id = ic.index_id
                    INNER JOIN sys.columns c ON ic.[object_id] = c.[object_id] AND ic.column_id = c.column_id
                    WHERE i.[object_id] = OBJECT_ID(N'[{s}].[{tableForSql}]')
                      AND c.[name] = N'{colLiteral}'
                      AND i.is_primary_key = 0
                      AND i.is_unique_constraint = 0;
                    IF LEN(@dropIdx) > 0 EXEC sp_executesql @dropIdx;
                    ALTER TABLE [{s}].[{tableForSql}] ALTER COLUMN [{colForSql}] DATETIME2 NULL;
                END;
                """;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = alterSql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task EnsureSchemaAndTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        // Items migration tamamlandiktan sonra ana batch'i calistir
        // Eger hala tip uyumsuzlugu varsa atla
        try
        {
            await EnsureSchemaAndTablesInternalAsync(connection, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 206 || ex.Number == 1781)
        {
            Console.Error.WriteLine($"[DB INIT WARN] EnsureSchemaAndTables partially failed (Error {ex.Number}): {ex.Message}");
            Console.Error.WriteLine("[DB INIT WARN] Re-running after cleanup...");
            // Ikinci deneme — migration temizligi yapildiktan sonra
            await MigrateItemsTableAsync(connection, cancellationToken);
            await EnsureSchemaAndTablesInternalAsync(connection, cancellationToken);
        }
    }

    private async Task EnsureSchemaAndTablesInternalAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var schemaForSql = _schema.Replace("]", "]]");
        var schemaLiteral = _schema.Replace("'", "''");

        var commandText = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE [name] = N'{schemaLiteral}')
            BEGIN
                EXEC(N'CREATE SCHEMA [{schemaForSql}]');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Department]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Department]
                (
                    [id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [pk_departments] PRIMARY KEY DEFAULT(NEWSEQUENTIALID()),
                    [code] NVARCHAR(20) NOT NULL,
                    [name] NVARCHAR(100) NOT NULL,
                    [parent_department_id] UNIQUEIDENTIFIER NULL,
                    [is_active] BIT NOT NULL CONSTRAINT [df_departments_is_active] DEFAULT(1)
                );
                CREATE UNIQUE INDEX [ux_departments_code] ON [{schemaForSql}].[Department]([code]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[User]
                (
                    [id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [pk_users] PRIMARY KEY DEFAULT(NEWSEQUENTIALID()),
                    [full_name] NVARCHAR(100) NOT NULL,
                    [email] NVARCHAR(120) NOT NULL,
                    [employee_code] NVARCHAR(30) NOT NULL,
                    [department_id] UNIQUEIDENTIFIER NOT NULL,
                    [supervisor_user_id] UNIQUEIDENTIFIER NULL,
                    [role] NVARCHAR(50) NOT NULL,
                    [permissions] NVARCHAR(MAX) NOT NULL,
                    [password_hash] NVARCHAR(512) NOT NULL,
                    [language_code] NVARCHAR(20) NOT NULL CONSTRAINT [df_users_language_code] DEFAULT(N'tr-TR'),
                    [theme_code] NVARCHAR(20) NOT NULL CONSTRAINT [df_users_theme_code] DEFAULT(N'light'),
                    [grid_preferences_json] NVARCHAR(MAX) NULL,
                    [grafana_role] NVARCHAR(20) NULL,
                    [is_active] BIT NOT NULL CONSTRAINT [df_users_is_active] DEFAULT(1),
                    CONSTRAINT [fk_users_departments_department_id]
                        FOREIGN KEY ([department_id]) REFERENCES [{schemaForSql}].[Department]([id]),
                    CONSTRAINT [fk_users_users_supervisor_user_id]
                        FOREIGN KEY ([supervisor_user_id]) REFERENCES [{schemaForSql}].[User]([id])
                );

                CREATE UNIQUE INDEX [ux_users_email] ON [{schemaForSql}].[User]([email]);
                CREATE UNIQUE INDEX [ux_users_employee_code] ON [{schemaForSql}].[User]([employee_code]);
            END;

            -- Grafana per-user role (Viewer / Editor / Admin / NULL).
            -- NULL = kullanici Grafana'ya eklenmez. Service tarafinda value set edildiginde
            -- IGrafanaProvisioningService.EnsureUserOrganizationMembershipAsync cagrilir.
            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'grafana_role') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[User]
                ADD [grafana_role] NVARCHAR(20) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'grid_preferences_json') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[User]
                ADD [grid_preferences_json] NVARCHAR(MAX) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[user_settings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[user_settings]
                (
                    [id]         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [user_id]    UNIQUEIDENTIFIER NOT NULL,
                    [setting_key] NVARCHAR(200)   NOT NULL,
                    [setting_value] NVARCHAR(MAX) NULL,
                    [Updated] DATETIME2(0)     NULL    
                );
                CREATE UNIQUE INDEX [ux_user_settings_user_key]
                    ON [{schemaForSql}].[user_settings]([user_id], [setting_key]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ui_label_translations]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[ui_label_translations]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_ui_label_translations] PRIMARY KEY,
                    [form_key] NVARCHAR(120) NOT NULL,
                    [label_key] NVARCHAR(120) NOT NULL,
                    [language_code] NVARCHAR(20) NOT NULL,
                    [label_text] NVARCHAR(500) NOT NULL,
                    [Updated] DATETIME2 NULL    
                );

                CREATE UNIQUE INDEX [ux_ui_label_translations_form_label_language]
                    ON [{schemaForSql}].[ui_label_translations]([form_key], [label_key], [language_code]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[integrator_settings]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_integrator_settings] PRIMARY KEY,
                    [provider] NVARCHAR(50) NOT NULL,
                    [name] NVARCHAR(100) NOT NULL,
                    [base_url] NVARCHAR(300) NOT NULL,
                    [company_tax_number] NVARCHAR(20) NOT NULL,
                    [username] NVARCHAR(120) NOT NULL,
                    [secret] NVARCHAR(1024) NOT NULL,
                    [polling_interval_seconds] INT NOT NULL CONSTRAINT [df_integrator_settings_polling_interval_seconds] DEFAULT(120),
                    [max_records_per_pull] INT NOT NULL CONSTRAINT [df_integrator_settings_max_records_per_pull] DEFAULT(200),
                    [log_retention_days] INT NOT NULL CONSTRAINT [df_integrator_settings_log_retention_days] DEFAULT(30),
                    [include_received_documents_in_pull] BIT NOT NULL CONSTRAINT [df_integrator_settings_include_received_documents_in_pull] DEFAULT(0),
                    [mark_downloaded_documents_as_received] BIT NOT NULL CONSTRAINT [df_integrator_settings_mark_downloaded_documents_as_received] DEFAULT(0),
                    [include_issued_einvoice_in_pull] BIT NOT NULL CONSTRAINT [df_integrator_settings_include_issued_einvoice_in_pull] DEFAULT(0),
                    [include_issued_earchive_in_pull] BIT NOT NULL CONSTRAINT [df_integrator_settings_include_issued_earchive_in_pull] DEFAULT(0),
                    [include_issued_edispatch_in_pull] BIT NOT NULL CONSTRAINT [df_integrator_settings_include_issued_edispatch_in_pull] DEFAULT(0),
                    [is_active] BIT NOT NULL CONSTRAINT [df_integrator_settings_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    ,
                    CONSTRAINT [ck_integrator_settings_polling_interval_seconds] CHECK ([polling_interval_seconds] >= 10),
                    CONSTRAINT [ck_integrator_settings_max_records_per_pull] CHECK ([max_records_per_pull] >= 1 AND [max_records_per_pull] <= 5000),
                    CONSTRAINT [ck_integrator_settings_log_retention_days] CHECK ([log_retention_days] >= 1 AND [log_retention_days] <= 3650)
                );

                CREATE UNIQUE INDEX [ux_integrator_settings_name]
                    ON [{schemaForSql}].[integrator_settings]([name]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
            BEGIN
                UPDATE [{schemaForSql}].[integrator_settings]
                SET [provider] = N'Logo'
                WHERE [provider] = N'Foriba';
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'secret') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'secret') < 1024
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ALTER COLUMN [secret] NVARCHAR(1024) NOT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'include_received_documents_in_pull') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ADD [include_received_documents_in_pull] BIT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'include_received_documents_in_pull') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[integrator_settings]
                    SET [include_received_documents_in_pull] = 0
                    WHERE [include_received_documents_in_pull] IS NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'include_received_documents_in_pull') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.[default_object_id] = dc.[object_id]
                   WHERE dc.[parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND c.[name] = N'include_received_documents_in_pull'
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ADD CONSTRAINT [df_integrator_settings_include_received_documents_in_pull] DEFAULT(0) FOR [include_received_documents_in_pull];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'include_received_documents_in_pull') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND [name] = N'include_received_documents_in_pull'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ALTER COLUMN [include_received_documents_in_pull] BIT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'mark_downloaded_documents_as_received') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ADD [mark_downloaded_documents_as_received] BIT NOT NULL
                    CONSTRAINT [df_integrator_settings_mark_downloaded_documents_as_received] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'include_issued_einvoice_in_pull') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ADD [include_issued_einvoice_in_pull] BIT NOT NULL
                    CONSTRAINT [df_integrator_settings_include_issued_einvoice_in_pull] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'include_issued_earchive_in_pull') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ADD [include_issued_earchive_in_pull] BIT NOT NULL
                    CONSTRAINT [df_integrator_settings_include_issued_earchive_in_pull] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'include_issued_edispatch_in_pull') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ADD [include_issued_edispatch_in_pull] BIT NOT NULL
                    CONSTRAINT [df_integrator_settings_include_issued_edispatch_in_pull] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'max_records_per_pull') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ADD [max_records_per_pull] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'max_records_per_pull') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[integrator_settings]
                    SET [max_records_per_pull] = 200
                    WHERE [max_records_per_pull] IS NULL
                       OR [max_records_per_pull] < 1
                       OR [max_records_per_pull] > 5000;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'max_records_per_pull') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.[default_object_id] = dc.[object_id]
                   WHERE dc.[parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND c.[name] = N'max_records_per_pull'
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ADD CONSTRAINT [df_integrator_settings_max_records_per_pull] DEFAULT(200) FOR [max_records_per_pull];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'max_records_per_pull') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND [name] = N'max_records_per_pull'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ALTER COLUMN [max_records_per_pull] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'max_records_per_pull') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.check_constraints
                   WHERE [name] = N'ck_integrator_settings_max_records_per_pull'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ADD CONSTRAINT [ck_integrator_settings_max_records_per_pull] CHECK ([max_records_per_pull] >= 1 AND [max_records_per_pull] <= 5000);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'log_retention_days') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ADD [log_retention_days] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'log_retention_days') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[integrator_settings]
                    SET [log_retention_days] = 30
                    WHERE [log_retention_days] IS NULL
                       OR [log_retention_days] < 1
                       OR [log_retention_days] > 3650;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'log_retention_days') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.[default_object_id] = dc.[object_id]
                   WHERE dc.[parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND c.[name] = N'log_retention_days'
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ADD CONSTRAINT [df_integrator_settings_log_retention_days] DEFAULT(30) FOR [log_retention_days];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'log_retention_days') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND [name] = N'log_retention_days'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ALTER COLUMN [log_retention_days] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'log_retention_days') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.check_constraints
                   WHERE [name] = N'ck_integrator_settings_log_retention_days'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ADD CONSTRAINT [ck_integrator_settings_log_retention_days] CHECK ([log_retention_days] >= 1 AND [log_retention_days] <= 3650);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[smtp_profiles]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_smtp_profiles] PRIMARY KEY,
                    [name] NVARCHAR(120) NOT NULL,
                    [from_email] NVARCHAR(160) NOT NULL,
                    [from_display_name] NVARCHAR(120) NULL,
                    [host] NVARCHAR(200) NOT NULL,
                    [port] INT NOT NULL CONSTRAINT [df_smtp_profiles_port] DEFAULT(587),
                    [username] NVARCHAR(160) NOT NULL,
                    [password] NVARCHAR(300) NOT NULL,
                    [use_ssl] BIT NOT NULL CONSTRAINT [df_smtp_profiles_use_ssl] DEFAULT(1),
                    [is_active] BIT NOT NULL CONSTRAINT [df_smtp_profiles_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    ,
                    CONSTRAINT [ck_smtp_profiles_port] CHECK ([port] >= 1 AND [port] <= 65535)
                );

                CREATE UNIQUE INDEX [ux_smtp_profiles_name]
                    ON [{schemaForSql}].[smtp_profiles]([name]);
            END;

            -- smtp_profiles: id INT → UNIQUEIDENTIFIER + company_id ekleme
            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]')
                     AND name = 'id'
                     AND system_type_id = TYPE_ID('int')
               )
            BEGIN
                -- Eski tabloyu yeniden olustur (veri az, migration guvenli)
                DROP TABLE [{schemaForSql}].[smtp_profiles];
                CREATE TABLE [{schemaForSql}].[smtp_profiles]
                (
                    [id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [pk_smtp_profiles] PRIMARY KEY,
                    [company_id] INT NOT NULL DEFAULT(0),
                    [name] NVARCHAR(120) NOT NULL,
                    [from_email] NVARCHAR(160) NOT NULL,
                    [from_display_name] NVARCHAR(120) NULL,
                    [host] NVARCHAR(200) NOT NULL,
                    [port] INT NOT NULL CONSTRAINT [df_smtp_profiles_port] DEFAULT(587),
                    [username] NVARCHAR(160) NOT NULL,
                    [password] NVARCHAR(2000) NOT NULL,
                    [auth_method] NVARCHAR(30) NOT NULL DEFAULT(N'Normal'),
                    [oauth2_client_id] NVARCHAR(300) NULL,
                    [oauth2_client_secret] NVARCHAR(300) NULL,
                    [oauth2_refresh_token] NVARCHAR(500) NULL,
                    [use_ssl] BIT NOT NULL CONSTRAINT [df_smtp_profiles_use_ssl] DEFAULT(1),
                    [is_active] BIT NOT NULL CONSTRAINT [df_smtp_profiles_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    ,
                    CONSTRAINT [ck_smtp_profiles_port] CHECK ([port] >= 1 AND [port] <= 65535)
                );
                CREATE UNIQUE INDEX [ux_smtp_profiles_name] ON [{schemaForSql}].[smtp_profiles]([name]);
            END;

            -- smtp_profiles: company_id yoksa ekle (olusturulmus ama eski versiyondan kalmis tablo)
            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.smtp_profiles', N'company_id') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[smtp_profiles] ADD [company_id] INT NOT NULL DEFAULT(0);
            END;

            -- smtp_profiles: 2FA / OAuth2 alanlari
            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.smtp_profiles', N'auth_method') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[smtp_profiles] ADD [auth_method] NVARCHAR(30) NOT NULL DEFAULT(N'Normal');
                ALTER TABLE [{schemaForSql}].[smtp_profiles] ADD [oauth2_client_id] NVARCHAR(300) NULL;
                ALTER TABLE [{schemaForSql}].[smtp_profiles] ADD [oauth2_client_secret] NVARCHAR(300) NULL;
                ALTER TABLE [{schemaForSql}].[smtp_profiles] ADD [oauth2_refresh_token] NVARCHAR(500) NULL;
            END;

            -- erp_connection_settings: id INT → UNIQUEIDENTIFIER migration
            IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]')
                     AND name = 'id'
                     AND system_type_id = TYPE_ID('int')
               )
            BEGIN
                DROP TABLE [{schemaForSql}].[erp_connection_settings];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[erp_connection_settings]
                (
                    [id] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [pk_erp_connection_settings] PRIMARY KEY,
                    [company_id] INT NOT NULL DEFAULT(0),
                    [provider] NVARCHAR(50) NOT NULL CONSTRAINT [df_erp_connection_settings_provider] DEFAULT(N'Netsis'),
                    [Company] NVARCHAR(50) NOT NULL,
                    [business] NVARCHAR(100) NOT NULL,
                    [branch] NVARCHAR(100) NOT NULL,
                    [username] NVARCHAR(160) NOT NULL,
                    [password] NVARCHAR(300) NOT NULL,
                    [is_active] BIT NOT NULL CONSTRAINT [df_erp_connection_settings_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    
                );

                CREATE UNIQUE INDEX [ux_erp_connection_settings_provider_company_business_branch]
                    ON [{schemaForSql}].[erp_connection_settings]([provider], [Company], [business], [branch]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[incoming_documents]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[incoming_documents]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_incoming_documents] PRIMARY KEY,
                    [integrator_settings_id] INT NOT NULL,
                    [envelope_id] NVARCHAR(120) NOT NULL,
                    [document_number] NVARCHAR(120) NOT NULL,
                    [kind] NVARCHAR(30) NOT NULL,
                    [issue_date] DATE NOT NULL,
                    [sender_tax_number] NVARCHAR(20) NOT NULL,
                    [recipient_tax_number] NVARCHAR(20) NOT NULL,
                    [payload_raw] NVARCHAR(MAX) NOT NULL,
                    [approval_status] NVARCHAR(20) NOT NULL CONSTRAINT [df_incoming_documents_approval_status] DEFAULT(N'Pending'),
                    [imported_at] DATETIME2 NOT NULL,
                    CONSTRAINT [fk_incoming_documents_integrator_settings_id]
                        FOREIGN KEY ([integrator_settings_id]) REFERENCES [{schemaForSql}].[integrator_settings]([id])
                );

                CREATE UNIQUE INDEX [ux_incoming_documents_envelope_id]
                    ON [{schemaForSql}].[incoming_documents]([envelope_id]);

                CREATE INDEX [ix_incoming_documents_approval_status_imported_at]
                    ON [{schemaForSql}].[incoming_documents]([approval_status], [imported_at] DESC);

                CREATE INDEX [ix_incoming_documents_kind]
                    ON [{schemaForSql}].[incoming_documents]([kind]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[incoming_documents]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.incoming_documents', N'kind') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.incoming_documents', N'document_number') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.incoming_documents', N'recipient_tax_number') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_incoming_documents_kind_document_number_recipient_tax_number'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[incoming_documents]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[incoming_documents]
                   GROUP BY [kind], [document_number], [recipient_tax_number]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_incoming_documents_kind_document_number_recipient_tax_number]
                    ON [{schemaForSql}].[incoming_documents]([kind], [document_number], [recipient_tax_number]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[incoming_documents]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [name] = N'ix_incoming_documents_approval_status_imported_at'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[incoming_documents]')
               )
            BEGIN
                CREATE INDEX [ix_incoming_documents_approval_status_imported_at]
                    ON [{schemaForSql}].[incoming_documents]([approval_status], [imported_at] DESC);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[incoming_documents]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.incoming_documents', N'kind') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [name] = N'ix_incoming_documents_kind'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[incoming_documents]')
               )
            BEGIN
                CREATE INDEX [ix_incoming_documents_kind]
                    ON [{schemaForSql}].[incoming_documents]([kind]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[incoming_documents]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.incoming_documents', N'sender_name') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[incoming_documents]
                ADD [sender_name] NVARCHAR(200) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.CBT_EBELGEMAS', N'PAYLOAD_RAW') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[CBT_EBELGEMAS]
                ADD [PAYLOAD_RAW] NVARCHAR(MAX) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.CBT_EBELGEMAS', N'BELGE_TIPI') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[CBT_EBELGEMAS]
                ADD [BELGE_TIPI] TINYINT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.CBT_EBELGEMAS', N'BELGE_TIPI') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[CBT_EBELGEMAS]
                    SET [BELGE_TIPI] = CASE
                        WHEN [TIPI] = 3 OR UPPER(LTRIM(RTRIM(ISNULL([FTIRSIP], N'''')))) = N''A'' THEN 3
                        WHEN [TIPI] = 2 OR UPPER(LTRIM(RTRIM(ISNULL([FTIRSIP], N'''')))) = N''I'' THEN 2
                        ELSE 1
                    END
                    WHERE [BELGE_TIPI] IS NULL
                       OR [BELGE_TIPI] NOT IN (1, 2, 3);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.CBT_EBELGEMAS', N'BELGE_TIPI') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.[default_object_id] = dc.[object_id]
                   WHERE dc.[parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]')
                     AND c.[name] = N'BELGE_TIPI'
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[CBT_EBELGEMAS]
                    ADD CONSTRAINT [df_CBT_EBELGEMAS_BELGE_TIPI] DEFAULT(1) FOR [BELGE_TIPI];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.CBT_EBELGEMAS', N'BELGE_TIPI') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.check_constraints
                   WHERE [name] = N'ck_CBT_EBELGEMAS_BELGE_TIPI'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[CBT_EBELGEMAS]
                    ADD CONSTRAINT [ck_CBT_EBELGEMAS_BELGE_TIPI] CHECK ([BELGE_TIPI] IN (1, 2, 3));
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.CBT_EBELGEMAS', N'BELGE_TIPI') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[CBT_EBELGEMAS]')
                     AND [name] = N'BELGE_TIPI'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[CBT_EBELGEMAS]
                    ALTER COLUMN [BELGE_TIPI] TINYINT NOT NULL;
                ');
            END;

            -- Legacy table rename: stock_cards -> Items
            IF OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NULL
               AND OBJECT_ID(N'[{schemaForSql}].[stock_cards]', N'U') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[stock_cards]'', ''Items'';
                ');
            END;

            -- Legacy table rename: Item -> Items
            IF OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NULL
               AND OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item]'', ''Items'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Items]
                (
                    [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Items] PRIMARY KEY,
                    [CompanyId] INT NOT NULL CONSTRAINT [df_Items_Company] DEFAULT(0),
                    [Code] NVARCHAR(50) NOT NULL,
                    [Name] NVARCHAR(200) NOT NULL,
                    [TypeId] INT NULL,
                    [UnitId] INT NULL,
                    [TaxRate] DECIMAL(5,2) NOT NULL CONSTRAINT [df_Items_TaxRate] DEFAULT(20),
                    [Combinations] BIT NOT NULL CONSTRAINT [df_Items_Combinations] DEFAULT(0),
                    [IsActive] BIT NOT NULL CONSTRAINT [df_Items_IsActive] DEFAULT(1),
                    [CreateDate] DATETIME NULL,
                    [ModifyDate] DATETIME NULL
                );

                EXEC(N'
                    CREATE UNIQUE INDEX [ux_Items_Company_Code]
                        ON [{schemaForSql}].[Items]([CompanyId], [Code]);
                ');
            END;

            -- CompanyId kolonu (sirket bazli izolasyon) — eski Items kayitlari icin idempotent ekle
            IF OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[Items]', N'CompanyId') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Items]
                    ADD [CompanyId] INT NOT NULL CONSTRAINT [df_Items_Company] DEFAULT(0);
            END;

            -- Eski tek-kolonlu unique index'i (Code) drop et, (CompanyId, Code) ile degistir
            IF OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Items]')
                     AND [name] = N'ux_Items_Code')
            BEGIN
                DROP INDEX [ux_Items_Code] ON [{schemaForSql}].[Items];
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[Items]', N'CompanyId') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Items]')
                     AND [name] = N'ux_Items_Company_Code')
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_Items_Company_Code]
                        ON [{schemaForSql}].[Items]([CompanyId], [Code]);
                ');
            END;

            -- FK Items.UnitId -> Unit.Id (idempotent — sadece Unit tablosu varsa kurar)
            IF OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Items]', N'UnitId') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE [name] = N'FK_Items_Unit'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[Items]'))
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Items]
                    WITH NOCHECK
                    ADD CONSTRAINT [FK_Items_Unit]
                        FOREIGN KEY ([UnitId]) REFERENCES [{schemaForSql}].[Unit]([Id]);
                ');
            END;

            -- CHECK constraint: Items.TypeId sabit ItemType enum araligina (1..9) kilitlenir.
            -- WITH NOCHECK — eski kayitlar dogrulanmaz, sadece yeni/guncellenen satirlar enforce edilir.
            IF OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Items]', N'TypeId') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.check_constraints
                   WHERE [name] = N'CK_Items_TypeId'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[Items]'))
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Items]
                    WITH NOCHECK
                    ADD CONSTRAINT [CK_Items_TypeId]
                        CHECK ([TypeId] IS NULL OR [TypeId] BETWEEN 1 AND 9);
                ');
            END;

            -- ── AGGRESSIVE MIGRATION: Field/FieldGroup tablolari yanlis tip ile yaratilmissa
            -- (eski v1.0.5 oncesi installer kalintilari) tamamen sil, asagidaki CREATE TABLE
            -- bloklari fix'li INT tipi ile yeniden yaratsin. Boylece DB'de eski schema
            -- kalintisindan dolayi FK uyumsuzlugu yasanmaz. Veri kaybi yok cunku bu tablolar
            -- bos olur (uygulama daha sefer baslayamamis).
            IF (
                -- FieldGroup.id UNIQUEIDENTIFIER kalmis ise
                EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[FieldGroup]')
                      AND name = 'id'
                      AND system_type_id = TYPE_ID('uniqueidentifier')
                )
                OR
                -- Field.group_id UNIQUEIDENTIFIER kalmis ise (FieldGroup INT ile uyumsuz)
                EXISTS (
                    SELECT 1 FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[Field]')
                      AND name = 'group_id'
                      AND system_type_id = TYPE_ID('uniqueidentifier')
                )
            )
            BEGIN
                -- Once FK constraints'leri drop et (varsa)
                IF EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = 'fk_material_card_field_settings_group_id'
                      AND parent_object_id = OBJECT_ID(N'[{schemaForSql}].[Field]')
                )
                BEGIN
                    ALTER TABLE [{schemaForSql}].[Field]
                        DROP CONSTRAINT [fk_material_card_field_settings_group_id];
                END;
                -- Tablolari drop et (siralama: child once)
                IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[Field];
                IF OBJECT_ID(N'[{schemaForSql}].[FieldGroup]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[FieldGroup];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[FieldGroup]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[FieldGroup]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_material_card_field_groups] PRIMARY KEY,
                    [group_key] NVARCHAR(60) NOT NULL,
                    [group_label] NVARCHAR(120) NOT NULL,
                    [display_order] INT NOT NULL CONSTRAINT [df_material_card_field_groups_display_order] DEFAULT(0),
                    [is_active] BIT NOT NULL CONSTRAINT [df_material_card_field_groups_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    
                );

                CREATE UNIQUE INDEX [ux_material_card_field_groups_group_key]
                    ON [{schemaForSql}].[FieldGroup]([group_key]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Field]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_material_card_field_settings] PRIMARY KEY,
                    -- group_id INT olmali — FieldGroup.id INT, FK uyumlu olsun (boş DB ilk kurulumda
                    -- UNIQUEIDENTIFIER tip uyumsuzlugu nedeniyle FK patliyodu).
                    [group_id] INT NULL,
                    [field_key] NVARCHAR(60) NOT NULL,
                    [field_label] NVARCHAR(120) NOT NULL,
                    [data_type] NVARCHAR(30) NOT NULL CONSTRAINT [df_material_card_field_settings_data_type] DEFAULT(N'STRING'),
                    [is_visible] BIT NOT NULL CONSTRAINT [df_material_card_field_settings_is_visible] DEFAULT(1),
                    [is_required] BIT NOT NULL CONSTRAINT [df_material_card_field_settings_is_required] DEFAULT(0),
                    [default_value] NVARCHAR(500) NULL,
                    [display_order] INT NOT NULL CONSTRAINT [df_material_card_field_settings_display_order] DEFAULT(0),
                    [column_span] INT NOT NULL CONSTRAINT [df_material_card_field_settings_column_span] DEFAULT(1),
                    [is_system] BIT NOT NULL CONSTRAINT [df_material_card_field_settings_is_system] DEFAULT(0),
                    [is_active] BIT NOT NULL CONSTRAINT [df_material_card_field_settings_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    ,
                    CONSTRAINT [fk_material_card_field_settings_group_id]
                        FOREIGN KEY ([group_id]) REFERENCES [{schemaForSql}].[FieldGroup]([id])
                );

                CREATE UNIQUE INDEX [ux_material_card_field_settings_field_key]
                    ON [{schemaForSql}].[Field]([field_key]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'group_id') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Field]
                ADD [group_id] INT NULL;
            END;

            -- Migration: eski kurulumlarda group_id UNIQUEIDENTIFIER kalmis olabilir,
            -- INT'e cevir (FK uyumlu olsun). Once varsa FK'i drop et.
            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[Field]')
                     AND name = 'group_id'
                     AND system_type_id = TYPE_ID('uniqueidentifier')
               )
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = 'fk_material_card_field_settings_group_id'
                      AND parent_object_id = OBJECT_ID(N'[{schemaForSql}].[Field]')
                )
                BEGIN
                    ALTER TABLE [{schemaForSql}].[Field]
                        DROP CONSTRAINT [fk_material_card_field_settings_group_id];
                END;
                ALTER TABLE [{schemaForSql}].[Field] DROP COLUMN [group_id];
                ALTER TABLE [{schemaForSql}].[Field] ADD [group_id] INT NULL;
                ALTER TABLE [{schemaForSql}].[Field]
                    ADD CONSTRAINT [fk_material_card_field_settings_group_id]
                    FOREIGN KEY ([group_id]) REFERENCES [{schemaForSql}].[FieldGroup]([id]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'data_type') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Field]
                ADD [data_type] NVARCHAR(30) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'data_type') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Field]
                    SET [data_type] = CASE
                        WHEN [field_key] IN (N''sale_ok'', N''purchase_ok'', N''is_configurable'') THEN N''BOOLEAN''
                        WHEN [field_key] IN (N''list_price'', N''cost_price'', N''purchase_price'', N''weight'', N''volume'') THEN N''DECIMAL''
                        ELSE N''STRING''
                    END
                    WHERE [data_type] IS NULL OR LTRIM(RTRIM([data_type])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'default_value') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Field]
                ADD [default_value] NVARCHAR(500) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'column_span') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Field]
                ADD [column_span] INT NOT NULL CONSTRAINT [df_material_card_field_settings_column_span] DEFAULT(1);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'column_span') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Field]
                    SET [column_span] = 1
                    WHERE [column_span] IS NULL OR [column_span] < 1;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'is_system') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Field]
                ADD [is_system] BIT NOT NULL CONSTRAINT [df_material_card_field_settings_is_system] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'is_active') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Field]
                ADD [is_active] BIT NOT NULL CONSTRAINT [df_material_card_field_settings_is_active] DEFAULT(1);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Field]', N'data_type') IS NOT NULL
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Field]
                    ALTER COLUMN [data_type] NVARCHAR(30) NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_material_card_field_settings_group_id'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[Field]')
               )
            BEGIN
                ALTER TABLE [{schemaForSql}].[Field]
                ADD CONSTRAINT [fk_material_card_field_settings_group_id]
                    FOREIGN KEY ([group_id]) REFERENCES [{schemaForSql}].[FieldGroup]([id]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_material_card_field_settings_field_key'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[Field]')
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_material_card_field_settings_field_key]
                    ON [{schemaForSql}].[Field]([field_key]);
            END;

            -- Eski schema migrate: field_definition_id UNIQUEIDENTIFIER kalmissa drop edip recreate
            IF OBJECT_ID(N'[{schemaForSql}].[material_card_field_options]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[material_card_field_options]')
                     AND name = 'field_definition_id'
                     AND system_type_id = TYPE_ID('uniqueidentifier')
               )
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM sys.foreign_keys
                    WHERE name = 'fk_material_card_field_options_field_definition_id'
                      AND parent_object_id = OBJECT_ID(N'[{schemaForSql}].[material_card_field_options]')
                )
                BEGIN
                    ALTER TABLE [{schemaForSql}].[material_card_field_options]
                        DROP CONSTRAINT [fk_material_card_field_options_field_definition_id];
                END;
                DROP TABLE [{schemaForSql}].[material_card_field_options];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[material_card_field_options]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[material_card_field_options]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_material_card_field_options] PRIMARY KEY,
                    -- field_definition_id INT olmali (Field.id INT IDENTITY)
                    [field_definition_id] INT NOT NULL,
                    [option_key] NVARCHAR(60) NOT NULL,
                    [option_label] NVARCHAR(160) NOT NULL,
                    [sort_order] INT NOT NULL CONSTRAINT [df_material_card_field_options_sort_order] DEFAULT(0),
                    [is_active] BIT NOT NULL CONSTRAINT [df_material_card_field_options_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    ,
                    CONSTRAINT [fk_material_card_field_options_field_definition_id]
                        FOREIGN KEY ([field_definition_id]) REFERENCES [{schemaForSql}].[Field]([id])
                );

                CREATE UNIQUE INDEX [ux_material_card_field_options_field_definition_option_key]
                    ON [{schemaForSql}].[material_card_field_options]([field_definition_id], [option_key]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[screen_layout_definitions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[screen_layout_definitions]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_screen_layout_definitions] PRIMARY KEY,
                    [screen_code] NVARCHAR(80) NOT NULL,
                    [layout_json] NVARCHAR(MAX) NOT NULL,
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    
                );

                CREATE UNIQUE INDEX [ux_screen_layout_definitions_screen_code]
                    ON [{schemaForSql}].[screen_layout_definitions]([screen_code]);
            END;

            -- Legacy rename: Feature -> ItemFeature (idempotent)
            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeature]', N'U') IS NULL
               AND OBJECT_ID(N'[{schemaForSql}].[Feature]', N'U') IS NOT NULL
            BEGIN
                EXEC sp_rename N'[{schemaForSql}].[Feature]', N'ItemFeature';
            END;

            -- Eski schema (UnitId yok = legacy code/name lowercase) tespit edilirse drop et
            -- (kullanici dev'de drop+recreate'e razi). UnitId yerine CompanyId kontrolu — yeni kolon.
            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeature]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[ItemFeature]', N'CompanyId') IS NULL
            BEGIN
                -- Once FeatureValue FK constraint'ini drop et (Feature/ItemFeature'a bagliydi)
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'fk_configuration_property_values_property_id')
                    ALTER TABLE [{schemaForSql}].[FeatureValue] DROP CONSTRAINT [fk_configuration_property_values_property_id];
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_FeatureValue_ItemFeature')
                    ALTER TABLE [{schemaForSql}].[FeatureValue] DROP CONSTRAINT [FK_FeatureValue_ItemFeature];
                DROP TABLE [{schemaForSql}].[ItemFeature];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeature]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[ItemFeature]
                (
                    [Id]              INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_ItemFeature] PRIMARY KEY,
                    [CompanyId]       INT NOT NULL,
                    [Name]            NVARCHAR(120) NOT NULL,
                    [DataType]        NVARCHAR(30)  NOT NULL,
                    [UnitOfMeasure]   NVARCHAR(20)  NULL,
                    [VisibleInDesign] BIT NOT NULL CONSTRAINT [df_ItemFeature_VisibleInDesign] DEFAULT(1),
                    [IsActive]        BIT NOT NULL CONSTRAINT [df_ItemFeature_IsActive] DEFAULT(1),
                    [CreatedAt]       DATETIME2 NOT NULL,
                    [UpdatedAt]       DATETIME2 NOT NULL
                );

                EXEC(N'
                    CREATE INDEX [ix_ItemFeature_CompanyId_Name]
                        ON [{schemaForSql}].[ItemFeature]([CompanyId], [Name]);
                ');
            END;

            -- Backfill UnitOfMeasure / VisibleInDesign kolonlari (eski schema'da yoksa)
            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeature]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[ItemFeature]', N'UnitOfMeasure') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[ItemFeature] ADD [UnitOfMeasure] NVARCHAR(20) NULL;
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeature]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[ItemFeature]', N'VisibleInDesign') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[ItemFeature] ADD [VisibleInDesign] BIT NOT NULL CONSTRAINT [df_ItemFeature_VisibleInDesign] DEFAULT(1);
            END;

            -- Eski FeatureValue tablosu UNIQUEIDENTIFIER id'lerle yaratilmis ise drop et
            -- (yeni schema INT id ile uyumsuz; FK ItemFeatureMappings.FeatureValueId INT bekliyor)
            IF OBJECT_ID(N'[{schemaForSql}].[FeatureValue]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns c
                   JOIN sys.types t ON c.system_type_id = t.system_type_id
                   WHERE c.object_id = OBJECT_ID(N'[{schemaForSql}].[FeatureValue]')
                     AND c.name = N'id'
                     AND t.name = N'uniqueidentifier'
               )
            BEGIN
                -- FK constraint'leri varsa drop
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'fk_configuration_property_values_property_id')
                    ALTER TABLE [{schemaForSql}].[FeatureValue] DROP CONSTRAINT [fk_configuration_property_values_property_id];
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_FeatureValue_ItemFeature')
                    ALTER TABLE [{schemaForSql}].[FeatureValue] DROP CONSTRAINT [FK_FeatureValue_ItemFeature];
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [name] = N'FK_ItemFeatureMappings_FeatureValue')
                    ALTER TABLE [{schemaForSql}].[ItemFeatureMappings] DROP CONSTRAINT [FK_ItemFeatureMappings_FeatureValue];
                DROP TABLE [{schemaForSql}].[FeatureValue];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[FeatureValue]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[FeatureValue]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_configuration_property_values] PRIMARY KEY,
                    [feature_id] INT NOT NULL,
                    [code] NVARCHAR(30) NOT NULL,
                    [description] NVARCHAR(160) NOT NULL,
                    [value] NVARCHAR(160) NOT NULL,
                    [sort_order] INT NOT NULL CONSTRAINT [df_configuration_property_values_sort_order] DEFAULT(0),
                    [is_active] BIT NOT NULL CONSTRAINT [df_configuration_property_values_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    ,
                    CONSTRAINT [FK_FeatureValue_ItemFeature]
                        FOREIGN KEY ([feature_id]) REFERENCES [{schemaForSql}].[ItemFeature]([Id])
                );

                EXEC(N'
                    CREATE UNIQUE INDEX [ux_configuration_property_values_property_id_value]
                        ON [{schemaForSql}].[FeatureValue]([feature_id], [value]);
                ');

                EXEC(N'
                    CREATE UNIQUE INDEX [ux_configuration_property_values_property_id_code]
                        ON [{schemaForSql}].[FeatureValue]([feature_id], [code]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[FeatureValue]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[FeatureValue]', N'code') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[FeatureValue]
                ADD [code] NVARCHAR(30) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[FeatureValue]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[FeatureValue]', N'description') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[FeatureValue]
                ADD [description] NVARCHAR(160) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[FeatureValue]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[FeatureValue]', N'code') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[FeatureValue]
                    SET [code] = LEFT(REPLACE(CONVERT(NVARCHAR(36), [id]), ''-'', ''''), 30)
                    WHERE [code] IS NULL OR LTRIM(RTRIM([code])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[FeatureValue]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[FeatureValue]', N'description') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[FeatureValue]
                    SET [description] = [value]
                    WHERE [description] IS NULL OR LTRIM(RTRIM([description])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[FeatureValue]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[FeatureValue]', N'code') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_configuration_property_values_property_id_code'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[FeatureValue]')
               )
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_configuration_property_values_property_id_code]
                        ON [{schemaForSql}].[FeatureValue]([feature_id], [code]);
                ');
            END;

            -- Legacy rename: stock_card_property_mappings -> ItemFeatureMappings
            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeatureMappings]', N'U') IS NULL
               AND OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]', N'U') IS NOT NULL
            BEGIN
                EXEC sp_rename N'[{schemaForSql}].[stock_card_property_mappings]', N'ItemFeatureMappings';
            END;

            -- Eski schema (FeatureId yok = legacy property_id, ya da Id UNIQUEIDENTIFIER) → drop & recreate
            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeatureMappings]', N'U') IS NOT NULL
               AND (COL_LENGTH(N'[{schemaForSql}].[ItemFeatureMappings]', N'FeatureId') IS NULL
                    OR EXISTS (
                        SELECT 1 FROM sys.columns c
                        JOIN sys.types t ON c.system_type_id = t.system_type_id
                        WHERE c.object_id = OBJECT_ID(N'[{schemaForSql}].[ItemFeatureMappings]')
                          AND c.name = N'Id' AND t.name = N'uniqueidentifier'))
            BEGIN
                DROP TABLE [{schemaForSql}].[ItemFeatureMappings];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeatureMappings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[ItemFeatureMappings]
                (
                    [Id]                       INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_ItemFeatureMappings] PRIMARY KEY,
                    [ItemId]                   INT NOT NULL,
                    [FeatureId]                INT NOT NULL,
                    [FeatureValueId]           INT NULL,
                    [PrintDescriptionInDesign] BIT NOT NULL CONSTRAINT [df_ItemFeatureMappings_PrintDescriptionInDesign] DEFAULT(1),
                    [IsActive]                 BIT NOT NULL CONSTRAINT [df_ItemFeatureMappings_IsActive] DEFAULT(1),
                    [CreatedAt]                DATETIME2 NOT NULL,
                    [UpdatedAt]                DATETIME2 NOT NULL,
                    CONSTRAINT [FK_ItemFeatureMappings_Items]
                        FOREIGN KEY ([ItemId]) REFERENCES [{schemaForSql}].[Items]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_ItemFeatureMappings_ItemFeature]
                        FOREIGN KEY ([FeatureId]) REFERENCES [{schemaForSql}].[ItemFeature]([Id]),
                    CONSTRAINT [FK_ItemFeatureMappings_FeatureValue]
                        FOREIGN KEY ([FeatureValueId]) REFERENCES [{schemaForSql}].[FeatureValue]([id])
                );

                CREATE INDEX [ix_ItemFeatureMappings_ItemId]
                    ON [{schemaForSql}].[ItemFeatureMappings]([ItemId]);
                CREATE INDEX [ix_ItemFeatureMappings_FeatureId]
                    ON [{schemaForSql}].[ItemFeatureMappings]([FeatureId]);
            END;

            -- PrintDescriptionInDesign kolonu (eski schema'larda yoksa ekle)
            IF OBJECT_ID(N'[{schemaForSql}].[ItemFeatureMappings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[ItemFeatureMappings]', N'PrintDescriptionInDesign') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[ItemFeatureMappings]
                ADD [PrintDescriptionInDesign] BIT NOT NULL
                    CONSTRAINT [df_ItemFeatureMappings_PrintDescriptionInDesign] DEFAULT(1);
            END;

            -- Legacy rename: ProductConfiguration -> ItemConfiguration (idempotent)
            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NULL
               AND OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
            BEGIN
                EXEC sp_rename N'[{schemaForSql}].[ItemConfiguration]', N'ItemConfiguration';
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[ItemConfiguration]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ParentId] INT NULL,
                    [RecordType] NVARCHAR(20) NOT NULL,
                    [RecordCode] NVARCHAR(100) NOT NULL,
                    [RecordName] NVARCHAR(255) NOT NULL,
                    [DataType] NVARCHAR(20) NULL,
                    [RelatedMaterialCode] NVARCHAR(50) NULL,
                    [ItemId] INT NULL,
                    [IsActive] BIT NOT NULL CONSTRAINT [df_ItemConfiguration_IsActive] DEFAULT(1),
                    [VisibleInDesign] BIT NOT NULL CONSTRAINT [df_ItemConfiguration_VisibleInDesign] DEFAULT(1),
                    [CreatedDate] DATETIME NOT NULL CONSTRAINT [df_ItemConfiguration_CreatedDate] DEFAULT(GETDATE())
                );
            END;

            -- ItemId kolonu (eski RelatedMaterialCode'un INT FK karsiligi)
            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[ItemConfiguration]', N'ItemId') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[ItemConfiguration] ADD [ItemId] INT NULL;
            END;

            -- ItemId backfill: RelatedMaterialCode -> Items.Id lookup
            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[ItemConfiguration]', N'ItemId') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[ItemConfiguration]', N'RelatedMaterialCode') IS NOT NULL
               AND OBJECT_ID(N'[{schemaForSql}].[Items]', N'U') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE ic SET ic.[ItemId] = i.[Id]
                    FROM [{schemaForSql}].[ItemConfiguration] ic
                    JOIN [{schemaForSql}].[Items] i ON i.[Code] = ic.[RelatedMaterialCode]
                    WHERE ic.[ItemId] IS NULL AND ic.[RelatedMaterialCode] IS NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[ItemConfiguration]', N'VisibleInDesign') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[ItemConfiguration]
                ADD [VisibleInDesign] BIT NOT NULL
                    CONSTRAINT [df_ProductConfiguration_VisibleInDesign] DEFAULT(1);
            END;

            -- RecordType CHECK constraint artik kullanilmiyor (RecordType kolonu legacy);
            -- mevcut constraint varsa drop et, yenisini ekleme.
            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.check_constraints
                   WHERE [name] = N'ck_ProductConfiguration_RecordType'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]')
               )
            BEGIN
                ALTER TABLE [{schemaForSql}].[ItemConfiguration]
                DROP CONSTRAINT [ck_ProductConfiguration_RecordType];
            END;

            -- Self-FK kaldirildi: VALUE/FEATURE_STOCK record'larinin ParentId'si artik
            -- ItemFeature.Id'ye logical pointer (FEATURE master ItemFeature'da). Eger
            -- onceki kurulumdan FK kalmissa drop et.
            IF EXISTS (
                SELECT 1 FROM sys.foreign_keys
                WHERE [name] = N'fk_ProductConfiguration_ParentId'
                  AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]')
            )
            BEGIN
                ALTER TABLE [{schemaForSql}].[ItemConfiguration]
                DROP CONSTRAINT [fk_ProductConfiguration_ParentId];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_ProductConfiguration_RecordType_ParentId'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]')
               )
            BEGIN
                CREATE INDEX [ix_ProductConfiguration_RecordType_ParentId]
                    ON [{schemaForSql}].[ItemConfiguration]([RecordType], [ParentId]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_ProductConfiguration_RelatedMaterialCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]')
               )
            BEGIN
                CREATE INDEX [ix_ProductConfiguration_RelatedMaterialCode]
                    ON [{schemaForSql}].[ItemConfiguration]([RelatedMaterialCode]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_ProductConfiguration_FeatureStock_Parent_RelatedMaterialCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ItemConfiguration]
                   WHERE [RecordType] = N'FEATURE_STOCK'
                     AND [ParentId] IS NOT NULL
                     AND [RelatedMaterialCode] IS NOT NULL
                   GROUP BY [ParentId], [RelatedMaterialCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_ProductConfiguration_FeatureStock_Parent_RelatedMaterialCode]
                    ON [{schemaForSql}].[ItemConfiguration]([ParentId], [RelatedMaterialCode])
                    WHERE [RecordType] = N'FEATURE_STOCK'
                      AND [ParentId] IS NOT NULL
                      AND [RelatedMaterialCode] IS NOT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_ProductConfiguration_Feature_RecordCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ItemConfiguration]
                   WHERE [RecordType] = N'FEATURE'
                   GROUP BY [RecordCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_ProductConfiguration_Feature_RecordCode]
                    ON [{schemaForSql}].[ItemConfiguration]([RecordCode])
                    WHERE [RecordType] = N'FEATURE';
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_ProductConfiguration_Value_Parent_RecordCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ItemConfiguration]
                   WHERE [RecordType] = N'VALUE'
                     AND [ParentId] IS NOT NULL
                   GROUP BY [ParentId], [RecordCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_ProductConfiguration_Value_Parent_RecordCode]
                    ON [{schemaForSql}].[ItemConfiguration]([ParentId], [RecordCode])
                    WHERE [RecordType] = N'VALUE' AND [ParentId] IS NOT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_ProductConfiguration_Config_Material_RecordCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ItemConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ItemConfiguration]
                   WHERE [RecordType] = N'CONFIG'
                     AND [RelatedMaterialCode] IS NOT NULL
                   GROUP BY [RelatedMaterialCode], [RecordCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_ProductConfiguration_Config_Material_RecordCode]
                    ON [{schemaForSql}].[ItemConfiguration]([RelatedMaterialCode], [RecordCode])
                    WHERE [RecordType] = N'CONFIG' AND [RelatedMaterialCode] IS NOT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Location]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ParentId] INT NULL,
                    [LocationTypeCode] NVARCHAR(20) NULL,
                    [LocationCode] NVARCHAR(50) NOT NULL,
                    [LocationName] NVARCHAR(100) NULL,
                    [SortOrder] INT NOT NULL CONSTRAINT [df_Locations_SortOrder] DEFAULT(0),
                    [MaxWeightCapacity] DECIMAL(18,2) NULL,
                    [VolumeCapacity] DECIMAL(18,2) NULL,
                    [IsActive] BIT NOT NULL CONSTRAINT [df_Locations_IsActive] DEFAULT(1),
                    CONSTRAINT [FK_Location_Parent]
                        FOREIGN KEY ([ParentId]) REFERENCES [{schemaForSql}].[Location]([Id])
                );

                CREATE UNIQUE INDEX [ux_Locations_LocationCode]
                    ON [{schemaForSql}].[Location]([LocationCode]);

                CREATE INDEX [ix_Locations_ParentId]
                    ON [{schemaForSql}].[Location]([ParentId]);

                CREATE INDEX [ix_Locations_LocationTypeCode]
                    ON [{schemaForSql}].[Location]([LocationTypeCode]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'ParentId') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Location]
                ADD [ParentId] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'LocationTypeCode') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Location]
                ADD [LocationTypeCode] NVARCHAR(20) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'LocationCode') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Location]
                ADD [LocationCode] NVARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'LocationName') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Location]
                ADD [LocationName] NVARCHAR(100) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'SortOrder') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Location]
                ADD [SortOrder] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'SortOrder') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Location]
                    SET [SortOrder] = 0
                    WHERE [SortOrder] IS NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'MaxWeightCapacity') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Location]
                ADD [MaxWeightCapacity] DECIMAL(18,2) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'VolumeCapacity') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Location]
                ADD [VolumeCapacity] DECIMAL(18,2) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'IsActive') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Location]
                ADD [IsActive] BIT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'IsActive') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Location]
                    SET [IsActive] = 1
                    WHERE [IsActive] IS NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'LocationCode') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[Location]
                   WHERE [LocationCode] IS NULL OR LTRIM(RTRIM([LocationCode])) = N''
               )
            BEGIN
                EXEC(N'
                    UPDATE wl
                    SET [LocationCode] = N''LOC-'' + RIGHT(N''000000'' + CAST(wl.[Id] AS NVARCHAR(10)), 6)
                    FROM [{schemaForSql}].[Location] wl
                    WHERE wl.[LocationCode] IS NULL OR LTRIM(RTRIM(wl.[LocationCode])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'LocationCode') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
                     AND [name] = N'LocationCode'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Location]
                    ALTER COLUMN [LocationCode] NVARCHAR(50) NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'SortOrder') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
                     AND [name] = N'SortOrder'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Location]
                    ALTER COLUMN [SortOrder] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'IsActive') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
                     AND [name] = N'IsActive'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Location]
                    ALTER COLUMN [IsActive] BIT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'SortOrder') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.[default_object_id] = dc.[object_id]
                   WHERE dc.[parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
                     AND c.[name] = N'SortOrder'
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Location]
                    ADD CONSTRAINT [df_Locations_SortOrder] DEFAULT(0) FOR [SortOrder];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'IsActive') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.[default_object_id] = dc.[object_id]
                   WHERE dc.[parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
                     AND c.[name] = N'IsActive'
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Location]
                    ADD CONSTRAINT [df_Locations_IsActive] DEFAULT(1) FOR [IsActive];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'LocationCode') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_Locations_LocationCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[Location]
                   GROUP BY [LocationCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_Locations_LocationCode]
                        ON [{schemaForSql}].[Location]([LocationCode]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_Locations_ParentId'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
               )
            BEGIN
                CREATE INDEX [ix_Locations_ParentId]
                    ON [{schemaForSql}].[Location]([ParentId]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_Locations_LocationTypeCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
               )
            BEGIN
                CREATE INDEX [ix_Locations_LocationTypeCode]
                    ON [{schemaForSql}].[Location]([LocationTypeCode]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Location]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Location]', N'ParentId') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'FK_Location_Parent'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[Location]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[Location] child
                   LEFT JOIN [{schemaForSql}].[Location] parent
                       ON parent.[Id] = child.[ParentId]
                   WHERE child.[ParentId] IS NOT NULL
                     AND parent.[Id] IS NULL
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Location]
                    WITH CHECK
                    ADD CONSTRAINT [FK_Location_Parent]
                        FOREIGN KEY ([ParentId]) REFERENCES [{schemaForSql}].[Location]([Id]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Unit]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [UnitCode] NVARCHAR(20) NOT NULL,
                    [UnitName] NVARCHAR(100) NOT NULL,
                    [SortOrder] INT NOT NULL CONSTRAINT [df_measure_unit_definitions_sort_order] DEFAULT(0),
                    [IsActive] BIT NOT NULL CONSTRAINT [df_measure_unit_definitions_is_active] DEFAULT(1),
                    [CreatedAt] DATETIME2 NOT NULL,
                    [UpdatedAt] DATETIME2 NOT NULL
                );

                CREATE UNIQUE INDEX [ux_measure_unit_definitions_unit_code]
                    ON [{schemaForSql}].[Unit]([UnitCode]);

                CREATE INDEX [ix_measure_unit_definitions_sort_order]
                    ON [{schemaForSql}].[Unit]([SortOrder], [UnitCode]);
            END;

            -- ── Machine: uretim/depo makineleri (LocationId FK ile lokasyona bagli) ─
            IF OBJECT_ID(N'[{schemaForSql}].[Machine]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Machine]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [LocationId] INT NOT NULL,
                    [MachineCode] NVARCHAR(50) NOT NULL,
                    [MachineName] NVARCHAR(150) NULL,
                    [MachineType] NVARCHAR(60) NULL,
                    [HourlyCapacity] DECIMAL(18,4) NULL,
                    [SortOrder] INT NOT NULL CONSTRAINT [df_Machine_SortOrder] DEFAULT(0),
                    [IsActive] BIT NOT NULL CONSTRAINT [df_Machine_IsActive] DEFAULT(1),
                    CONSTRAINT [FK_Machine_Location]
                        FOREIGN KEY ([LocationId]) REFERENCES [{schemaForSql}].[Location]([Id])
                );

                CREATE UNIQUE INDEX [ux_Machine_MachineCode]
                    ON [{schemaForSql}].[Machine]([MachineCode]);

                CREATE INDEX [ix_Machine_LocationId]
                    ON [{schemaForSql}].[Machine]([LocationId]);
            END;

            -- ── MachineType: makine tipi referans veri (Logo Netsis 9 std. tip + ozel) ──
            IF OBJECT_ID(N'[{schemaForSql}].[MachineType]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[MachineType]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [Code] NVARCHAR(50) NOT NULL,
                    [Name] NVARCHAR(120) NOT NULL,
                    [Description] NVARCHAR(800) NULL,
                    [IsBuiltIn] BIT NOT NULL CONSTRAINT [df_MachineType_IsBuiltIn] DEFAULT(0),
                    [SortOrder] INT NOT NULL CONSTRAINT [df_MachineType_SortOrder] DEFAULT(0),
                    [IsActive] BIT NOT NULL CONSTRAINT [df_MachineType_IsActive] DEFAULT(1)
                );
                CREATE UNIQUE INDEX [ux_MachineType_Code] ON [{schemaForSql}].[MachineType]([Code]);
            END;

            -- Built-in 9 standart makine tipi seed (idempotent — sadece eksik olanlari ekler).
            -- Description'larda kritik planlama davranisi/parametreleri belirtilir.
            ;WITH src ([Code],[Name],[Description],[SortOrder]) AS (
                SELECT * FROM (VALUES
                    (N'NORMAL_SINGLE', N'Normal (Single Processor)',
                     N'Ayni anda yalnizca 1 urun isleyebilir. Standart tek-islemcili tezgah/makine icin temel tip. Uretim Suresi + Uretim Miktari ikisi birlikte tanimlanir (orn. 70 sn''de 8 metre).',
                     10),
                    (N'NORMAL_MULTI', N'Normal (Multi Processor)',
                     N'Ayni anda birden fazla AYNI urunu paralel isler. Bu tip secildiginde "Islemci Sayisi" parametresi aktiflesir — makinenin paralel kapasitesi. Farkli urunler ayni anda islenemez.',
                     20),
                    (N'FASON', N'Fason',
                     N'Disarida islenmek uzere gonderilen uretim. "Cari Kodu" + "Kapasite" alanlari ile fason ureticiyi tanimlar. Fason Lojistik Plani sekmesinde gidis/donus gun ve saatleri girilir. Kapasite tanimli degilse sonsuz kapasite varsayilir; tanimliysa 24 saatlik dilime gore birim uretim suresi hesaplanir.',
                     30),
                    (N'FIRIN', N'Fırın',
                     N'Bos kapasitesi oldugu surece yeni urun alabilir (gercek ekmek firini gibi). Farkli urunleri ayni anda isler — her urun firinda kendi proses suresi kadar islenir. "Ise Baslayabilmek icin Min. Kapasite Doluluk Orani" parametresi: ornek 0.8 = firin kapasitesinin %80 orani dolmadan calismaya baslamaz.',
                     40),
                    (N'SUREKLI_FIRIN', N'Sürekli Fırın',
                     N'Firin icinde surekli akis var — urunler firina girer, icinde ilerler, ciktiklarinda islenmis olur. "Cevrim Suresi" parametresi: makine dolduktan sonra her birim urun icin makineden cikis suresi (orn. 30 sn''de 1 adet). Ilk urun cikana kadar gecen sure = uretim suresi.',
                     50),
                    (N'MONTAJ_HATTI', N'Montaj Hattı',
                     N'Surekli firin ile ayni mantik (cevrim suresi destekli). EK olarak Montaj Hatti Dengeleme destegi var — operasyon dagilimi optimum sekilde dengelenir. Hatti olusturan istasyonlar arasinda akis sirasi korunur.',
                     60),
                    (N'MANUEL_MONTAJ', N'Manuel Montaj',
                     N'Operator/kaynak tabanli uretim. "Min. Calisabilecek Kaynak Seti Sayisi" + "Maks. Calisabilecek Kaynak Seti Sayisi" parametreleriyle algoritma optimum kaynak atamasi yapar. Ayni anda farkli urunler islenebilir (orn. montaj masasi etrafinda paketleme operasyonu).',
                     70),
                    (N'KAZAN', N'Kazan',
                     N'Kapasite tamamen dolmamis olsa dahi makine basladigi an proses sona erene kadar yeni urun ALMAZ. "Farkli Urunler Ayni Anda Islem Gorebilsin" parametresi acilirsa "Gruplama Secenegi" listesi (Uretim Suresi, Grup Kodu, Kod-1..5, Urun Grubu) ile harman uretimi yapilir — gruptaki tum urunler max sureye gore islenir. Min. Kapasite Doluluk Orani da uygulanir.',
                     80),
                    (N'ROBOT', N'Robot',
                     N'Ayni anda farkli urunleri es zamanli isleyebilen makine. "Es Zamanli Is Setleri Tanimlama" sekmesi ile mumkun her kombinasyon (orn. A-B, A-C, B-C, A-B-C) set olarak girilmelidir. Tanimlanmamis bir kombinasyon olusursa makineye is emri ATANMAZ.',
                     90)
                ) v ([Code],[Name],[Description],[SortOrder])
            )
            -- HOLDLOCK: Web + Worker startup'ta paralel migration calistirinca race
            -- olusabilir (MATCHED kontrolu ile INSERT arasinda satir eklenirse duplicate
            -- key hatasi). HOLDLOCK ile MERGE update lock alir → seri calisir.
            MERGE [{schemaForSql}].[MachineType] WITH (HOLDLOCK) AS tgt
            USING src ON tgt.[Code] = src.[Code]
            WHEN NOT MATCHED THEN
                INSERT ([Code],[Name],[Description],[IsBuiltIn],[SortOrder],[IsActive])
                VALUES (src.[Code], src.[Name], src.[Description], 1, src.[SortOrder], 1)
            WHEN MATCHED THEN
                UPDATE SET [Name] = src.[Name],
                           [Description] = src.[Description],
                           [SortOrder] = src.[SortOrder],
                           [IsBuiltIn] = 1;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'UnitCode') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Unit]
                ADD [UnitCode] NVARCHAR(20) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'UnitName') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Unit]
                ADD [UnitName] NVARCHAR(100) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'SortOrder') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Unit]
                ADD [SortOrder] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'IsActive') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Unit]
                ADD [IsActive] BIT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'CreatedAt') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Unit]
                ADD [CreatedAt] DATETIME2 NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'UpdatedAt') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Unit]
                ADD [UpdatedAt] DATETIME2 NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
            BEGIN
                UPDATE [{schemaForSql}].[Unit]
                SET [SortOrder] = 0
                WHERE [SortOrder] IS NULL;

                UPDATE [{schemaForSql}].[Unit]
                SET [IsActive] = 1
                WHERE [IsActive] IS NULL;

                UPDATE [{schemaForSql}].[Unit]
                SET [CreatedAt] = GETDATE()
                WHERE [CreatedAt] IS NULL;

                UPDATE [{schemaForSql}].[Unit]
                SET [UpdatedAt] = COALESCE([UpdatedAt], [CreatedAt], GETDATE())
                WHERE [UpdatedAt] IS NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'UnitCode') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Unit]')
                     AND [name] = N'UnitCode'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Unit]
                    ALTER COLUMN [UnitCode] NVARCHAR(20) NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'IntlCode') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Unit]
                ADD [IntlCode] NVARCHAR(20) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'UnitName') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Unit]')
                     AND [name] = N'UnitName'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Unit]
                    ALTER COLUMN [UnitName] NVARCHAR(100) NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'SortOrder') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Unit]')
                     AND [name] = N'SortOrder'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Unit]
                    ALTER COLUMN [SortOrder] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'IsActive') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Unit]')
                     AND [name] = N'IsActive'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Unit]
                    ALTER COLUMN [IsActive] BIT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'CreatedAt') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Unit]')
                     AND [name] = N'CreatedAt'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Unit]
                    ALTER COLUMN [CreatedAt] DATETIME2 NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Unit]', N'UpdatedAt') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Unit]')
                     AND [name] = N'UpdatedAt'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Unit]
                    ALTER COLUMN [UpdatedAt] DATETIME2 NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_measure_unit_definitions_unit_code'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[Unit]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[Unit]
                   GROUP BY [UnitCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_measure_unit_definitions_unit_code]
                    ON [{schemaForSql}].[Unit]([UnitCode]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Unit]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_measure_unit_definitions_sort_order'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[Unit]')
               )
            BEGIN
                CREATE INDEX [ix_measure_unit_definitions_sort_order]
                    ON [{schemaForSql}].[Unit]([SortOrder], [UnitCode]);
            END;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureCompanySchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var schemaForSql = _schema.Replace("]", "]]");
        var schemaLiteral = _schema.Replace("'", "''");

        var commandText = $"""
            -- ── Migration: GUID → INT company ID (one-time, dev/upgrade path) ──────────
            -- If the company table still has a uniqueidentifier PK, drop all FK constraints
            -- in the schema first, then drop company-related tables so they can be recreated
            -- with INT IDs.
            IF OBJECT_ID(N'[{schemaForSql}].[Company]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[Company]')
                     AND name = 'id'
                     AND system_type_id = TYPE_ID('uniqueidentifier')
               )
            BEGIN
                -- Step 1: Drop ALL FK constraints in the schema (dynamic SQL)
                DECLARE @dropFkSql NVARCHAR(MAX) = N'';
                SELECT @dropFkSql = @dropFkSql
                    + N'ALTER TABLE [{schemaForSql}].[' + OBJECT_NAME(fk.parent_object_id) + N'] DROP CONSTRAINT [' + fk.name + N'];'
                FROM sys.foreign_keys fk
                WHERE SCHEMA_NAME(OBJECTPROPERTY(fk.parent_object_id, 'SchemaId')) = N'{schemaLiteral}'
                   OR SCHEMA_NAME(OBJECTPROPERTY(fk.referenced_object_id, 'SchemaId')) = N'{schemaLiteral}';
                IF LEN(@dropFkSql) > 0 EXEC(@dropFkSql);

                -- Step 2: Drop tables (order: children first)
                IF OBJECT_ID(N'[{schemaForSql}].[WidgetTra]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[WidgetTra];
                IF OBJECT_ID(N'[{schemaForSql}].[WidgetMas]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[WidgetMas];
                IF OBJECT_ID(N'[{schemaForSql}].[incoming_documents]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[incoming_documents];
                IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[erp_connection_settings];
                IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[smtp_profiles];
                IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[integrator_settings];
                IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[User];
                IF OBJECT_ID(N'[{schemaForSql}].[Department]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[Department];
                IF OBJECT_ID(N'[{schemaForSql}].[Company]', N'U') IS NOT NULL
                    DROP TABLE [{schemaForSql}].[Company];
            END;
            -- ── End migration ────────────────────────────────────────────────────────────

            IF OBJECT_ID(N'[{schemaForSql}].[Company]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Company]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_company] PRIMARY KEY,
                    [name] NVARCHAR(120) NOT NULL,
                    [title] NVARCHAR(200) NOT NULL,
                    [address] NVARCHAR(500) NOT NULL,
                    [tax_office] NVARCHAR(100) NOT NULL,
                    [tax_number] NVARCHAR(20) NOT NULL,
                    [is_e_document_approval_enabled] BIT NOT NULL CONSTRAINT [df_company_is_e_document_approval_enabled] DEFAULT(0),
                    [is_active] BIT NOT NULL CONSTRAINT [df_company_is_active] DEFAULT(1),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    
                );

                CREATE UNIQUE INDEX [ux_company_name] ON [{schemaForSql}].[Company]([name]);
                CREATE UNIQUE INDEX [ux_company_tax_number] ON [{schemaForSql}].[Company]([tax_number]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Company]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Company]', N'is_e_document_approval_enabled') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Company]
                ADD [is_e_document_approval_enabled] BIT NOT NULL
                    CONSTRAINT [df_company_is_e_document_approval_enabled] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Company]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Company]', N'connection_string') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Company]
                ADD [connection_string] NVARCHAR(500) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Company]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Company]', N'city') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Company] ADD [city] NVARCHAR(100) NULL;
                ALTER TABLE [{schemaForSql}].[Company] ADD [district] NVARCHAR(100) NULL;
                ALTER TABLE [{schemaForSql}].[Company] ADD [postal_code] NVARCHAR(10) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'company_id') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[User]
                ADD [company_id] INT NOT NULL CONSTRAINT [df_users_company_id_default] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'language_code') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[User]
                ADD [language_code] NVARCHAR(20) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'theme_code') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[User]
                ADD [theme_code] NVARCHAR(20) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Company]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[Company]
               )
            BEGIN
                INSERT INTO [{schemaForSql}].[Company]
                    ([name], [title], [address], [tax_office], [tax_number], [is_active], [Created], [Updated])
                VALUES
                    (@CompanyName, @CompanyTitle, @CompanyAddress, @CompanyTaxOffice, @CompanyTaxNumber, 1, @Now, @Now);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'company_id') IS NOT NULL
            BEGIN
                EXEC sp_executesql
                    N'UPDATE [{schemaForSql}].[User]
                      SET [company_id] = @DefaultCompanyId
                      WHERE [company_id] = 0;',
                    N'@DefaultCompanyId INT',
                    @DefaultCompanyId = @CompanyId;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'language_code') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[User]
                    SET [language_code] = N''tr-TR''
                    WHERE [language_code] IS NULL OR LTRIM(RTRIM([language_code])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'theme_code') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[User]
                    SET [theme_code] = N''light''
                    WHERE [theme_code] IS NULL OR LTRIM(RTRIM([theme_code])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'company_id') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND [name] = N'company_id'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[User]
                    ALTER COLUMN [company_id] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'language_code') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND [name] = N'language_code'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[User]
                    ALTER COLUMN [language_code] NVARCHAR(20) NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'theme_code') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND [name] = N'theme_code'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[User]
                    ALTER COLUMN [theme_code] NVARCHAR(20) NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'language_code') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.[default_object_id] = dc.[object_id]
                   WHERE dc.[parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND c.[name] = N'language_code'
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[User]
                    ADD CONSTRAINT [df_users_language_code] DEFAULT(N''tr-TR'') FOR [language_code];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'theme_code') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.default_constraints dc
                   INNER JOIN sys.columns c
                       ON c.[default_object_id] = dc.[object_id]
                   WHERE dc.[parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND c.[name] = N'theme_code'
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[User]
                    ADD CONSTRAINT [df_users_theme_code] DEFAULT(N''light'') FOR [theme_code];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_users_company_company_id'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[User]
                    ADD CONSTRAINT [fk_users_company_company_id]
                    FOREIGN KEY ([company_id]) REFERENCES [{schemaForSql}].[Company]([id]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND [name] = N'ux_users_email'
               )
            BEGIN
                EXEC(N'DROP INDEX [ux_users_email] ON [{schemaForSql}].[User];');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND [name] = N'ux_users_employee_code'
               )
            BEGIN
                EXEC(N'DROP INDEX [ux_users_employee_code] ON [{schemaForSql}].[User];');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND [name] = N'ux_users_company_email'
               )
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_users_company_email]
                    ON [{schemaForSql}].[User]([company_id], [email]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[User]', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[User]')
                     AND [name] = N'ux_users_company_employee_code'
               )
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_users_company_employee_code]
                    ON [{schemaForSql}].[User]([company_id], [employee_code]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Department]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Department]', N'company_id') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Department]
                ADD [company_id] INT NOT NULL CONSTRAINT [df_departments_company_id_default] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Department]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Department]', N'company_id') IS NOT NULL
            BEGIN
                EXEC sp_executesql
                    N'UPDATE [{schemaForSql}].[Department]
                      SET [company_id] = @DefaultCompanyId
                      WHERE [company_id] = 0;',
                    N'@DefaultCompanyId INT',
                    @DefaultCompanyId = @CompanyId;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Department]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Department]', N'company_id') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Department]')
                     AND [name] = N'company_id'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Department]
                    ALTER COLUMN [company_id] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Department]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Department]', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_departments_company_company_id'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[Department]')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Department]
                    ADD CONSTRAINT [fk_departments_company_company_id]
                    FOREIGN KEY ([company_id]) REFERENCES [{schemaForSql}].[Company]([id]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Department]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Department]')
                     AND [name] = N'ux_departments_code'
               )
            BEGIN
                EXEC(N'DROP INDEX [ux_departments_code] ON [{schemaForSql}].[Department];');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Department]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Department]', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[Department]')
                     AND [name] = N'ux_departments_company_code'
               )
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_departments_company_code]
                    ON [{schemaForSql}].[Department]([company_id], [code]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'company_id') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[integrator_settings]
                ADD [company_id] INT NOT NULL CONSTRAINT [df_integrator_settings_company_id_default] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'company_id') IS NOT NULL
            BEGIN
                EXEC sp_executesql
                    N'UPDATE [{schemaForSql}].[integrator_settings]
                      SET [company_id] = @DefaultCompanyId
                      WHERE [company_id] = 0;',
                    N'@DefaultCompanyId INT',
                    @DefaultCompanyId = @CompanyId;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'company_id') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND [name] = N'company_id'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ALTER COLUMN [company_id] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_integrator_settings_company_company_id'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[integrator_settings]
                    ADD CONSTRAINT [fk_integrator_settings_company_company_id]
                    FOREIGN KEY ([company_id]) REFERENCES [{schemaForSql}].[Company]([id]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND [name] = N'ux_integrator_settings_name'
               )
            BEGIN
                EXEC(N'DROP INDEX [ux_integrator_settings_name] ON [{schemaForSql}].[integrator_settings];');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[integrator_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.integrator_settings', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[integrator_settings]')
                     AND [name] = N'ux_integrator_settings_company_name'
               )
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_integrator_settings_company_name]
                    ON [{schemaForSql}].[integrator_settings]([company_id], [name]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.smtp_profiles', N'company_id') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[smtp_profiles]
                ADD [company_id] INT NOT NULL CONSTRAINT [df_smtp_profiles_company_id_default] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.smtp_profiles', N'company_id') IS NOT NULL
            BEGIN
                EXEC sp_executesql
                    N'UPDATE [{schemaForSql}].[smtp_profiles]
                      SET [company_id] = @DefaultCompanyId
                      WHERE [company_id] = 0;',
                    N'@DefaultCompanyId INT',
                    @DefaultCompanyId = @CompanyId;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.smtp_profiles', N'company_id') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]')
                     AND [name] = N'company_id'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[smtp_profiles]
                    ALTER COLUMN [company_id] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.smtp_profiles', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_smtp_profiles_company_company_id'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[smtp_profiles]
                    ADD CONSTRAINT [fk_smtp_profiles_company_company_id]
                    FOREIGN KEY ([company_id]) REFERENCES [{schemaForSql}].[Company]([id]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]')
                     AND [name] = N'ux_smtp_profiles_name'
               )
            BEGIN
                EXEC(N'DROP INDEX [ux_smtp_profiles_name] ON [{schemaForSql}].[smtp_profiles];');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.smtp_profiles', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[smtp_profiles]')
                     AND [name] = N'ux_smtp_profiles_company_name'
               )
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_smtp_profiles_company_name]
                    ON [{schemaForSql}].[smtp_profiles]([company_id], [name]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.erp_connection_settings', N'company_id') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[erp_connection_settings]
                ADD [company_id] INT NOT NULL CONSTRAINT [df_erp_connection_settings_company_id_default] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.erp_connection_settings', N'company_id') IS NOT NULL
            BEGIN
                EXEC sp_executesql
                    N'UPDATE [{schemaForSql}].[erp_connection_settings]
                      SET [company_id] = @DefaultCompanyId
                      WHERE [company_id] = 0;',
                    N'@DefaultCompanyId INT',
                    @DefaultCompanyId = @CompanyId;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.erp_connection_settings', N'company_id') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]')
                     AND [name] = N'company_id'
                     AND [is_nullable] = 1
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[erp_connection_settings]
                    ALTER COLUMN [company_id] INT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.erp_connection_settings', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_erp_connection_settings_company_company_id'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]')
               )
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[erp_connection_settings]
                    ADD CONSTRAINT [fk_erp_connection_settings_company_company_id]
                    FOREIGN KEY ([company_id]) REFERENCES [{schemaForSql}].[Company]([id]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]')
                     AND [name] = N'ux_erp_connection_settings_provider_company_business_branch'
               )
            BEGIN
                EXEC(N'DROP INDEX [ux_erp_connection_settings_provider_company_business_branch] ON [{schemaForSql}].[erp_connection_settings];');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.erp_connection_settings', N'company_id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[erp_connection_settings]')
                     AND [name] = N'ux_erp_connection_settings_company_provider_company_business_branch'
               )
            BEGIN
                EXEC(N'
                    CREATE UNIQUE INDEX [ux_erp_connection_settings_company_provider_company_business_branch]
                    ON [{schemaForSql}].[erp_connection_settings]([company_id], [provider], [Company], [business], [branch]);
                ');
            END;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.Add(new SqlParameter("@CompanyId", DefaultCompanyId));
        command.Parameters.Add(new SqlParameter("@CompanyName", "Calibra Merkez"));
        command.Parameters.Add(new SqlParameter("@CompanyTitle", "Calibra Teknoloji A.S."));
        command.Parameters.Add(new SqlParameter("@CompanyAddress", "Istanbul"));
        command.Parameters.Add(new SqlParameter("@CompanyTaxOffice", "Beyoglu"));
        command.Parameters.Add(new SqlParameter("@CompanyTaxNumber", "1234567890"));
        command.Parameters.Add(new SqlParameter("@Now", DateTime.Now));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsurePltSystemLogTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var schemaForSql = _schema.Replace("]", "]]");
        var schemaLiteral = _schema.Replace("'", "''");

        var commandText = $"""
            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[PLT_SISTEM_LOG]
                (
                    [ID] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [VERITABANI] VARCHAR(50) NULL,
                    [UYGULAMA_ID] INT NULL,
                    [ACIKLAMA] VARCHAR(200) NULL,
                    [MODUL_NO] INT NULL,
                    [PROGRAM_NO] INT NULL,
                    [KAYIT_NO] INT NULL,
                    [ISLETME_KODU] INT NULL,
                    [SUBE_KODU] INT NULL,
                    [BELGE_TURU] VARCHAR(50) NULL,
                    [STOK_KODU] VARCHAR(50) NULL,
                    [CARI_KOD] VARCHAR(50) NULL,
                    [MUH_KODU] VARCHAR(50) NULL,
                    [PROJE_KODU] VARCHAR(50) NULL,
                    [BELGE_NO] VARCHAR(50) NULL,
                    [DEPO_KODU] INT NULL,
                    [HESAP_KODU] VARCHAR(50) NULL,
                    [TARIH] DATETIME NULL,
                    [MIKTAR] DECIMAL(18, 8) NULL,
                    [FIYAT] DECIMAL(18, 8) NULL,
                    [TUTAR] DECIMAL(18, 8) NULL,
                    [SIRA_NO] INT NULL,
                    [SERI_NO] VARCHAR(50) NULL,
                    [S_SAHA_01] VARCHAR(50) NULL,
                    [S_SAHA_02] VARCHAR(100) NULL,
                    [S_SAHA_03] VARCHAR(50) NULL,
                    [S_SAHA_04] VARCHAR(50) NULL,
                    [S_SAHA_05] VARCHAR(50) NULL,
                    [C_SAHA_01] CHAR(1) NULL,
                    [C_SAHA_02] CHAR(1) NULL,
                    [C_SAHA_03] CHAR(1) NULL,
                    [C_SAHA_04] CHAR(1) NULL,
                    [C_SAHA_05] CHAR(1) NULL,
                    [I_SAHA_01] INT NULL,
                    [I_SAHA_02] INT NULL,
                    [I_SAHA_03] INT NULL,
                    [I_SAHA_04] INT NULL,
                    [I_SAHA_05] INT NULL,
                    [F_SAHA_01] DECIMAL(18, 8) NULL,
                    [F_SAHA_02] DECIMAL(18, 8) NULL,
                    [F_SAHA_03] DECIMAL(18, 8) NULL,
                    [F_SAHA_04] DECIMAL(18, 8) NULL,
                    [F_SAHA_05] DECIMAL(18, 8) NULL,
                    [D_SAHA_01] DATETIME NULL,
                    [D_SAHA_02] DATETIME NULL,
                    [D_SAHA_03] DATETIME NULL,
                    [D_SAHA_04] DATETIME NULL,
                    [D_SAHA_05] DATETIME NULL,
                    [N_SAHA_01] NVARCHAR(MAX) NULL,
                    [N_SAHA_02] NVARCHAR(MAX) NULL,
                    [COMPANY_ID] INT NULL,
                    [KAYITYAPANKUL] VARCHAR(50) NULL,
                    [KAYITTARIHI] DATETIME NULL,
                    [DUZELTMEYAPANKUL] VARCHAR(50) NULL,
                    [DUZELTMETARIHI] DATETIME NULL,
                    [ONAYTIPI] NCHAR(10) NULL,
                    [ONAYNUM] NCHAR(10) NULL
                );
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'VERITABANI') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [VERITABANI] VARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'UYGULAMA_ID') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [UYGULAMA_ID] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'ACIKLAMA') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [ACIKLAMA] VARCHAR(200) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'MODUL_NO') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [MODUL_NO] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'PROGRAM_NO') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [PROGRAM_NO] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'KAYIT_NO') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [KAYIT_NO] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'BELGE_TURU') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [BELGE_TURU] VARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'TARIH') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [TARIH] DATETIME NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'S_SAHA_01') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [S_SAHA_01] VARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'S_SAHA_02') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [S_SAHA_02] VARCHAR(100) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'S_SAHA_03') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [S_SAHA_03] VARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'I_SAHA_01') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [I_SAHA_01] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'I_SAHA_02') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [I_SAHA_02] INT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'N_SAHA_01') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [N_SAHA_01] NVARCHAR(MAX) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'KAYITYAPANKUL') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [KAYITYAPANKUL] VARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'KAYITTARIHI') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [KAYITTARIHI] DATETIME NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'COMPANY_ID') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG] ADD [COMPANY_ID] INT NULL;
            END;

            -- Migration: COMPANY_ID was UNIQUEIDENTIFIER, drop table so it recreates with INT column
            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'COMPANY_ID') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]')
                     AND name = 'COMPANY_ID'
                     AND system_type_id = TYPE_ID('uniqueidentifier')
               )
            BEGIN
                -- Can't ALTER uniqueidentifier to int directly — drop and recreate
                DROP TABLE [{schemaForSql}].[PLT_SISTEM_LOG];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'COMPANY_ID') IS NOT NULL
               AND OBJECT_ID(N'[{schemaForSql}].[Company]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_plt_sistem_log_company_company_id'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]')
               )
            BEGIN
                EXEC(N'
                    IF NOT EXISTS (
                        SELECT 1
                        FROM [{schemaForSql}].[PLT_SISTEM_LOG] logTable
                        LEFT JOIN [{schemaForSql}].[Company] company
                            ON company.[id] = logTable.[COMPANY_ID]
                        WHERE logTable.[COMPANY_ID] IS NOT NULL
                          AND company.[id] IS NULL
                    )
                    BEGIN
                        ALTER TABLE [{schemaForSql}].[PLT_SISTEM_LOG]
                        ADD CONSTRAINT [fk_plt_sistem_log_company_company_id]
                        FOREIGN KEY ([COMPANY_ID]) REFERENCES [{schemaForSql}].[Company]([id]);
                    END
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.PLT_SISTEM_LOG', N'COMPANY_ID') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[{schemaForSql}].[PLT_SISTEM_LOG]')
                     AND [name] = N'ix_plt_sistem_log_company_id'
               )
            BEGIN
                EXEC(N'
                    IF COL_LENGTH(N''{schemaLiteral}.PLT_SISTEM_LOG'', N''COMPANY_ID'') IS NOT NULL
                    BEGIN
                        CREATE INDEX [ix_plt_sistem_log_company_id]
                        ON [{schemaForSql}].[PLT_SISTEM_LOG]([COMPANY_ID]);
                    END
                ');
            END;
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedFieldsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var groupsTableName = $"[{_schema}].[FieldGroup]";
        var fieldsTableName = $"[{_schema}].[Field]";
        var now = DateTime.Now;
        var groupDefinitions = new[]
        {
            new { Key = "general", Label = "Genel Bilgiler", DisplayOrder = 10 },
            new { Key = "trade", Label = "Ticari Bilgiler", DisplayOrder = 20 },
            new { Key = "logistics", Label = "Lojistik Verileri", DisplayOrder = 30 },
            new { Key = "descriptions", Label = "Aciklamalar", DisplayOrder = 40 },
            new { Key = "configuration", Label = "Yapilandirma", DisplayOrder = 50 }
        };

        // FieldGroup.id INT IDENTITY oldugundan Dictionary<string,int> tutuyoruz.
        var groupIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupDefinitions)
        {
            await using var groupCommand = connection.CreateCommand();
            // INSERT'te [id] kolonu YOK — IDENTITY otomatik atar; SELECT ile yeni id'yi geri aliriz.
            groupCommand.CommandText = $"""
                IF EXISTS (SELECT 1 FROM {groupsTableName} WHERE [group_key] = @GroupKey)
                BEGIN
                    UPDATE {groupsTableName}
                    SET
                        [group_label] = CASE
                            WHEN [group_label] IS NULL OR LTRIM(RTRIM([group_label])) = N'' THEN @GroupLabel
                            ELSE [group_label]
                        END,
                        [display_order] = CASE
                            WHEN [display_order] = 0 THEN @DisplayOrder
                            ELSE [display_order]
                        END,
                        [is_active] = 1,
                        [Updated] = @UpdatedAt
                    WHERE [group_key] = @GroupKey;

                    SELECT [id] FROM {groupsTableName} WHERE [group_key] = @GroupKey;
                END
                ELSE
                BEGIN
                    INSERT INTO {groupsTableName}
                        ([group_key], [group_label], [display_order], [is_active], [Created], [Updated])
                    VALUES
                        (@GroupKey, @GroupLabel, @DisplayOrder, 1, @CreatedAt, @UpdatedAt);

                    SELECT CAST(SCOPE_IDENTITY() AS INT);
                END;
                """;

            groupCommand.Parameters.Add(new SqlParameter("@GroupKey", group.Key));
            groupCommand.Parameters.Add(new SqlParameter("@GroupLabel", group.Label));
            groupCommand.Parameters.Add(new SqlParameter("@DisplayOrder", group.DisplayOrder));
            groupCommand.Parameters.Add(new SqlParameter("@CreatedAt", now));
            groupCommand.Parameters.Add(new SqlParameter("@UpdatedAt", now));

            var result = await groupCommand.ExecuteScalarAsync(cancellationToken);
            // SqlClient SCOPE_IDENTITY DECIMAL doner — INT'e cast'leyip al
            int parsedId = result switch
            {
                int i => i,
                long l => (int)l,
                decimal d => (int)d,
                _ => 0,
            };
            groupIds[group.Key] = parsedId;
        }

        foreach (var field in MaterialCardFieldCatalog.Definitions.OrderBy(x => x.DisplayOrder))
        {
            var groupKey = "general";
            var dataType = field.DataType;

            await using var command = connection.CreateCommand();
            // INSERT'te [id] kolonu YOK — IDENTITY otomatik atar.
            command.CommandText = $"""
                IF EXISTS (SELECT 1 FROM {fieldsTableName} WHERE [field_key] = @FieldKey)
                BEGIN
                    UPDATE {fieldsTableName}
                    SET
                        [group_id] = @GroupId,
                        [field_label] = CASE
                            WHEN [field_label] IS NULL OR LTRIM(RTRIM([field_label])) = N'' THEN @FieldLabel
                            ELSE [field_label]
                        END,
                        [data_type] = @DataType,
                        [is_visible] = CASE WHEN [is_visible] IS NULL THEN @IsVisible ELSE [is_visible] END,
                        [is_required] = CASE WHEN [is_required] IS NULL THEN @IsRequired ELSE [is_required] END,
                        [display_order] = CASE WHEN [display_order] = 0 THEN @DisplayOrder ELSE [display_order] END,
                        [is_system] = 1,
                        [is_active] = 1,
                        [Updated] = @UpdatedAt
                    WHERE [field_key] = @FieldKey;
                END
                ELSE
                BEGIN
                    INSERT INTO {fieldsTableName}
                        ([group_id], [field_key], [field_label], [data_type], [is_visible], [is_required], [default_value], [display_order], [column_span], [is_system], [is_active], [Created], [Updated])
                    VALUES
                        (@GroupId, @FieldKey, @FieldLabel, @DataType, @IsVisible, @IsRequired, NULL, @DisplayOrder, 1, 1, 1, @CreatedAt, @UpdatedAt);
                END;
                """;

            command.Parameters.Add(new SqlParameter("@GroupId", groupIds[groupKey]));
            command.Parameters.Add(new SqlParameter("@FieldKey", field.Key));
            command.Parameters.Add(new SqlParameter("@FieldLabel", field.Label));
            command.Parameters.Add(new SqlParameter("@DataType", dataType));
            command.Parameters.Add(new SqlParameter("@IsVisible", field.DefaultVisible));
            command.Parameters.Add(new SqlParameter("@IsRequired", field.DefaultRequired));
            command.Parameters.Add(new SqlParameter("@DisplayOrder", field.DisplayOrder));
            command.Parameters.Add(new SqlParameter("@CreatedAt", now));
            command.Parameters.Add(new SqlParameter("@UpdatedAt", now));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Field options seed (Malzeme Tipi secenekleri vs.)
        // Field.id ve options.id INT IDENTITY oldugundan @FieldId INT, options'a [id] yazmiyoruz.
        var optionsTableName = $"[{_schema}].[material_card_field_options]";
        foreach (var (fieldKey, optionKey, optionLabel, sortOrder) in MaterialCardFieldCatalog.FieldOptions)
        {
            await using var optCmd = connection.CreateCommand();
            optCmd.CommandText = $"""
                IF NOT EXISTS (
                    SELECT 1 FROM {optionsTableName} o
                    INNER JOIN {fieldsTableName} f ON f.[id] = o.[field_definition_id]
                    WHERE f.[field_key] = @FieldKey AND o.[option_key] = @OptionKey
                )
                BEGIN
                    DECLARE @FieldId INT;
                    SELECT @FieldId = [id] FROM {fieldsTableName} WHERE [field_key] = @FieldKey;
                    IF @FieldId IS NOT NULL
                        INSERT INTO {optionsTableName} ([field_definition_id],[option_key],[option_label],[sort_order],[is_active],[Created],[Updated])
                        VALUES (@FieldId, @OptionKey, @OptionLabel, @SortOrder, 1, GETDATE(), GETDATE());
                END;
                """;
            optCmd.Parameters.AddWithValue("@FieldKey", fieldKey);
            optCmd.Parameters.AddWithValue("@OptionKey", optionKey);
            optCmd.Parameters.AddWithValue("@OptionLabel", optionLabel);
            optCmd.Parameters.AddWithValue("@SortOrder", sortOrder);
            await optCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task SeedScreenDesignLayoutsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tableName = $"[{_schema}].[screen_layout_definitions]";
        var now = DateTime.Now;

        foreach (var screen in ScreenDesignCatalog.GetSupportedScreens().Where(x => !x.UsesMaterialCardSchema))
        {
            var layout = ScreenDesignCatalog.GetDefaultLayout(screen.ScreenCode);
            var json = JsonSerializer.Serialize(new
            {
                Tabs = layout.Tabs.Select(x => new
                {
                    x.TabKey,
                    x.TabLabel,
                    x.DisplayOrder,
                    x.IsActive
                }),
                Items = layout.Items.Select(x => new
                {
                    x.ItemKey,
                    x.TabKey,
                    x.DisplayOrder,
                    x.ColumnSpan,
                    x.IsVisible,
                    x.IsRequired
                })
            });

            await using var command = connection.CreateCommand();
            // [id] INT IDENTITY oldugu icin INSERT'te kolon listesine yazmiyoruz —
            // SQL Server kendisi sira atar. Onceki versiyonda Guid.NewGuid() basiliyordu,
            // bu "Operand type clash: uniqueidentifier is incompatible with int" hatasi
            // veriyordu (sıfırdan kurulumda).
            command.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM {tableName} WHERE [screen_code] = @ScreenCode)
                BEGIN
                    INSERT INTO {tableName}
                        ([screen_code], [layout_json], [Created], [Updated])
                    VALUES
                        (@ScreenCode, @LayoutJson, @CreatedAt, @UpdatedAt);
                END;
                """;

            command.Parameters.Add(new SqlParameter("@ScreenCode", screen.ScreenCode));
            command.Parameters.Add(new SqlParameter("@LayoutJson", json));
            command.Parameters.Add(new SqlParameter("@CreatedAt", now));
            command.Parameters.Add(new SqlParameter("@UpdatedAt", now));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task EncryptLegacyIntegratorSecretsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tableName = $"[{_schema}].[integrator_settings]";
        var legacyRows = new List<(Guid Id, string Secret)>();

        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = $"""
                SELECT [id], [secret]
                FROM {tableName}
                WHERE [secret] IS NOT NULL
                  AND LTRIM(RTRIM([secret])) <> N''
                  AND [secret] NOT LIKE N'enc:v1:%';
                """;

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                legacyRows.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }

        foreach (var row in legacyRows)
        {
            var encryptedSecret = IntegratorSecretProtector.Protect(row.Secret);
            if (string.Equals(encryptedSecret, row.Secret, StringComparison.Ordinal))
            {
                continue;
            }

            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = $"""
                UPDATE {tableName}
                SET [secret] = @Secret,
                    [Updated] = @UpdatedAt
                WHERE [id] = @Id;
                """;
            updateCommand.Parameters.Add(new SqlParameter("@Id", row.Id));
            updateCommand.Parameters.Add(new SqlParameter("@Secret", encryptedSecret));
            updateCommand.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task EncryptLegacySmtpPasswordsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var tableName = $"[{_schema}].[smtp_profiles]";
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");

        // Table may not exist yet on fresh installs — skip gracefully
        await using (var existsCmd = connection.CreateCommand())
        {
            existsCmd.CommandText = $"SELECT OBJECT_ID(N'[{s}].[smtp_profiles]', N'U');";
            var result = await existsCmd.ExecuteScalarAsync(cancellationToken);
            if (result == null || result == DBNull.Value) return;
        }

        // Encrypted DPAPI value is much longer than plain password — widen the column first
        await using (var alterCmd = connection.CreateCommand())
        {
            alterCmd.CommandText = $"""
                IF COL_LENGTH(N'{sl}.smtp_profiles', N'password') < 2000
                BEGIN
                    ALTER TABLE [{s}].[smtp_profiles] ALTER COLUMN [password] NVARCHAR(2000) NOT NULL;
                END;
                """;
            await alterCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var legacyRows = new List<(Guid Id, string Password)>();

        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = $"""
                SELECT [id], [password]
                FROM {tableName}
                WHERE [password] IS NOT NULL
                  AND LTRIM(RTRIM([password])) <> N''
                  AND [password] NOT LIKE N'enc:v1:%';
                """;

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                legacyRows.Add((reader.GetGuid(0), reader.GetString(1)));
            }
        }

        foreach (var row in legacyRows)
        {
            var encryptedPassword = IntegratorSecretProtector.Protect(row.Password);
            if (string.Equals(encryptedPassword, row.Password, StringComparison.Ordinal))
            {
                continue;
            }

            await using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = $"""
                UPDATE {tableName}
                SET [password] = @Password,
                    [Updated] = @UpdatedAt
                WHERE [id] = @Id;
                """;
            updateCommand.Parameters.Add(new SqlParameter("@Id", row.Id));
            updateCommand.Parameters.Add(new SqlParameter("@Password", encryptedPassword));
            updateCommand.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task SeedDepartmentsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(1) FROM [{_schema}].[Department];";
        var countResult = await countCommand.ExecuteScalarAsync(cancellationToken);
        var departmentCount = Convert.ToInt32(countResult);

        if (departmentCount > 0)
        {
            return;
        }

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = $"""
            INSERT INTO [{_schema}].[Department] ([id], [company_id], [code], [name], [parent_department_id], [is_active])
            VALUES
                (@FinanceId, @CompanyId, @FinanceCode, @FinanceName, NULL, 1),
                (@OperationsId, @CompanyId, @OperationsCode, @OperationsName, NULL, 1);
            """;
        insertCommand.Parameters.Add(new SqlParameter("@FinanceId", FinanceDepartmentId));
        insertCommand.Parameters.Add(new SqlParameter("@CompanyId", DefaultCompanyId));
        insertCommand.Parameters.Add(new SqlParameter("@FinanceCode", "FIN"));
        insertCommand.Parameters.Add(new SqlParameter("@FinanceName", "Finans"));
        insertCommand.Parameters.Add(new SqlParameter("@OperationsId", OperationsDepartmentId));
        insertCommand.Parameters.Add(new SqlParameter("@OperationsCode", "OPS"));
        insertCommand.Parameters.Add(new SqlParameter("@OperationsName", "Operasyon"));

        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedAdminUserAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        if (!_bootstrapAdminOptions.SeedOnStartup)
        {
            return;
        }

        var adminEmail = _bootstrapAdminOptions.Email.Trim();
        var adminPassword = _bootstrapAdminOptions.DefaultPassword;
        var adminFullName = _bootstrapAdminOptions.FullName.Trim();
        var adminEmployeeCode = _bootstrapAdminOptions.EmployeeCode.Trim();

        if (string.IsNullOrWhiteSpace(adminEmail) ||
            string.IsNullOrWhiteSpace(adminPassword) ||
            string.IsNullOrWhiteSpace(adminFullName) ||
            string.IsNullOrWhiteSpace(adminEmployeeCode))
        {
            return;
        }

        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = $"SELECT COUNT(1) FROM [{_schema}].[User] WHERE [company_id] = @CompanyId AND [email] = @Email;";
        existsCommand.Parameters.Add(new SqlParameter("@CompanyId", DefaultCompanyId));
        existsCommand.Parameters.Add(new SqlParameter("@Email", adminEmail));
        var existsResult = await existsCommand.ExecuteScalarAsync(cancellationToken);
        var exists = Convert.ToInt32(existsResult) > 0;

        if (exists)
        {
            return;
        }

        var permissions = string.Join(',', UserAuthorizationCatalog.Permissions.Select(x => x.ToString()));
        var passwordHash = _passwordHashService.HashPassword(adminPassword);

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = $"""
            INSERT INTO [{_schema}].[User]
                ([id], [company_id], [full_name], [email], [employee_code], [department_id], [supervisor_user_id], [role], [permissions], [password_hash], [language_code], [theme_code], [grid_preferences_json], [is_active])
            VALUES
                (@Id, @CompanyId, @FullName, @Email, @EmployeeCode, @DepartmentId, NULL, @Role, @Permissions, @PasswordHash, @LanguageCode, @ThemeCode, NULL, 1);
            """;
        insertCommand.Parameters.Add(new SqlParameter("@Id", AdminUserId));
        insertCommand.Parameters.Add(new SqlParameter("@CompanyId", DefaultCompanyId));
        insertCommand.Parameters.Add(new SqlParameter("@FullName", adminFullName));
        insertCommand.Parameters.Add(new SqlParameter("@Email", adminEmail));
        insertCommand.Parameters.Add(new SqlParameter("@EmployeeCode", adminEmployeeCode));
        insertCommand.Parameters.Add(new SqlParameter("@DepartmentId", FinanceDepartmentId));
        insertCommand.Parameters.Add(new SqlParameter("@Role", UserRole.SystemAdmin.ToString()));
        insertCommand.Parameters.Add(new SqlParameter("@Permissions", permissions));
        insertCommand.Parameters.Add(new SqlParameter("@PasswordHash", passwordHash));
        insertCommand.Parameters.Add(new SqlParameter("@LanguageCode", UserProfile.DefaultLanguageCode));
        insertCommand.Parameters.Add(new SqlParameter("@ThemeCode", UserProfile.DefaultThemeCode));

        await insertCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureIntegratorLoginColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");

        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[integrator_settings]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'{sl}.integrator_settings', N'app_str') IS NULL
                    ALTER TABLE [{s}].[integrator_settings] ADD [app_str] NVARCHAR(100) NULL;
                IF COL_LENGTH(N'{sl}.integrator_settings', N'source') IS NULL
                    ALTER TABLE [{s}].[integrator_settings] ADD [source] NVARCHAR(20) NULL;
                IF COL_LENGTH(N'{sl}.integrator_settings', N'app_version') IS NULL
                    ALTER TABLE [{s}].[integrator_settings] ADD [app_version] NVARCHAR(20) NULL;
                IF COL_LENGTH(N'{sl}.integrator_settings', N'schedule_enabled') IS NULL
                    ALTER TABLE [{s}].[integrator_settings] ADD [schedule_enabled] BIT NOT NULL CONSTRAINT [df_integrator_settings_schedule_enabled] DEFAULT(0);
                IF COL_LENGTH(N'{sl}.integrator_settings', N'timeout_seconds') IS NULL
                    ALTER TABLE [{s}].[integrator_settings] ADD [timeout_seconds] INT NOT NULL DEFAULT 30;
                IF COL_LENGTH(N'{sl}.integrator_settings', N'lookback_days') IS NULL
                    ALTER TABLE [{s}].[integrator_settings] ADD [lookback_days] INT NOT NULL DEFAULT 30;
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureNotesTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        var commandText = $"""
            -- Migration: notes.company_id UNIQUEIDENTIFIER → INT (drop & recreate)
            IF OBJECT_ID(N'[{s}].[notes]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'[{s}].[notes]')
                     AND name = 'company_id'
                     AND system_type_id = TYPE_ID('uniqueidentifier')
               )
            BEGIN
                IF OBJECT_ID(N'[{s}].[note_shares]', N'U') IS NOT NULL DROP TABLE [{s}].[note_shares];
                IF OBJECT_ID(N'[{s}].[note_reminders]', N'U') IS NOT NULL DROP TABLE [{s}].[note_reminders];
                IF OBJECT_ID(N'[{s}].[note_folders]', N'U') IS NOT NULL DROP TABLE [{s}].[note_folders];
                DROP TABLE [{s}].[notes];
            END;

            IF OBJECT_ID(N'[{s}].[notes]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[notes]
                (
                    [id]                  UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [company_id]          INT NOT NULL,
                    [user_id]             UNIQUEIDENTIFIER NOT NULL,
                    [title]               NVARCHAR(200) NOT NULL,
                    [content]             NVARCHAR(MAX) NULL,
                    [Created]          DATETIME2 NOT NULL,
                    [Updated]          DATETIME2 NULL    ,
                    [is_deleted]          BIT NOT NULL CONSTRAINT [df_notes_is_deleted] DEFAULT(0),
                    [is_fully_encrypted]  BIT NOT NULL CONSTRAINT [df_notes_is_fully_encrypted] DEFAULT(0),
                    [encryption_hint]     NVARCHAR(300) NULL
                );
                CREATE INDEX [ix_notes_company_user] ON [{s}].[notes]([company_id], [user_id]);
            END;

            -- Idempotent: mevcut tabloya Mod 2 (E2E) kolonlari ekle
            IF OBJECT_ID(N'[{s}].[notes]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[notes]', N'is_fully_encrypted') IS NULL
            BEGIN
                ALTER TABLE [{s}].[notes]
                ADD [is_fully_encrypted] BIT NOT NULL
                    CONSTRAINT [df_notes_is_fully_encrypted] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{s}].[notes]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[notes]', N'encryption_hint') IS NULL
            BEGIN
                ALTER TABLE [{s}].[notes] ADD [encryption_hint] NVARCHAR(300) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[note_reminders]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[note_reminders]
                (
                    [id]        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [note_id]   UNIQUEIDENTIFIER NOT NULL,
                    [remind_at] DATETIME2 NOT NULL,
                    [is_sent]   BIT NOT NULL CONSTRAINT [df_note_reminders_is_sent] DEFAULT(0),
                    [sent_at]   DATETIME2 NULL,
                    CONSTRAINT [fk_note_reminders_notes_note_id]
                        FOREIGN KEY ([note_id]) REFERENCES [{s}].[notes]([id])
                );
                CREATE INDEX [ix_note_reminders_note_id] ON [{s}].[note_reminders]([note_id]);
                CREATE INDEX [ix_note_reminders_unsent] ON [{s}].[note_reminders]([is_sent], [remind_at]);
            END;

            IF OBJECT_ID(N'[{s}].[note_shares]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[note_shares]
                (
                    [id]                   UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [note_id]              UNIQUEIDENTIFIER NOT NULL,
                    [shared_with_user_id]  UNIQUEIDENTIFIER NOT NULL,
                    [shared_at]            DATETIME2 NOT NULL,
                    CONSTRAINT [fk_note_shares_notes_note_id]
                        FOREIGN KEY ([note_id]) REFERENCES [{s}].[notes]([id]),
                    CONSTRAINT [ux_note_shares_note_user]
                        UNIQUE ([note_id], [shared_with_user_id])
                );
                CREATE INDEX [ix_note_shares_note_id] ON [{s}].[note_shares]([note_id]);
                CREATE INDEX [ix_note_shares_shared_with] ON [{s}].[note_shares]([shared_with_user_id]);
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureNoteExtensionsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");

        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[note_folders]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[note_folders]
                (
                    [id]               UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [company_id]       INT NOT NULL,
                    [user_id]          UNIQUEIDENTIFIER NOT NULL,
                    [name]             NVARCHAR(200) NOT NULL,
                    [parent_folder_id] UNIQUEIDENTIFIER NULL,
                    [Created]       DATETIME2 NOT NULL,
                    [is_deleted]       BIT NOT NULL CONSTRAINT [df_note_folders_is_deleted] DEFAULT(0)
                );
                CREATE INDEX [ix_note_folders_company_user] ON [{s}].[note_folders]([company_id], [user_id]);
            END;

            IF OBJECT_ID(N'[{s}].[notes]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'{sl}.notes', N'folder_id') IS NULL
                    ALTER TABLE [{s}].[notes] ADD [folder_id] UNIQUEIDENTIFIER NULL;
            END;

            IF OBJECT_ID(N'[{s}].[notes]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'{sl}.notes', N'is_pinned') IS NULL
                    ALTER TABLE [{s}].[notes] ADD [is_pinned] BIT NOT NULL CONSTRAINT [df_notes_is_pinned] DEFAULT(0);
            END;

            IF OBJECT_ID(N'[{s}].[note_reminders]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'{sl}.note_reminders', N'recurrence_type') IS NULL
                    ALTER TABLE [{s}].[note_reminders] ADD [recurrence_type] INT NOT NULL CONSTRAINT [df_note_reminders_recurrence_type] DEFAULT(0);
                IF COL_LENGTH(N'{sl}.note_reminders', N'recurrence_data') IS NULL
                    ALTER TABLE [{s}].[note_reminders] ADD [recurrence_data] NVARCHAR(200) NULL;
                IF COL_LENGTH(N'{sl}.note_reminders', N'delivery_channel') IS NULL
                    ALTER TABLE [{s}].[note_reminders] ADD [delivery_channel] INT NOT NULL CONSTRAINT [df_note_reminders_delivery_channel] DEFAULT(0);
                IF COL_LENGTH(N'{sl}.note_reminders', N'target_user_id') IS NULL
                    ALTER TABLE [{s}].[note_reminders] ADD [target_user_id] UNIQUEIDENTIFIER NULL;
            END;

            IF OBJECT_ID(N'[{s}].[note_reminder_targets]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[note_reminder_targets]
                (
                    [id]          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [reminder_id] UNIQUEIDENTIFIER NOT NULL,
                    [user_id]     UNIQUEIDENTIFIER NOT NULL,
                    CONSTRAINT [fk_reminder_targets_reminder]
                        FOREIGN KEY ([reminder_id]) REFERENCES [{s}].[note_reminders]([id]) ON DELETE CASCADE,
                    CONSTRAINT [ux_reminder_targets] UNIQUE ([reminder_id], [user_id])
                );
                CREATE INDEX [ix_reminder_targets_reminder] ON [{s}].[note_reminder_targets]([reminder_id]);
                CREATE INDEX [ix_reminder_targets_user] ON [{s}].[note_reminder_targets]([user_id]);
            END;

            IF OBJECT_ID(N'[{s}].[user_notifications]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[user_notifications]
                (
                    [id]          UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [company_id]  INT NOT NULL,
                    [user_id]     UNIQUEIDENTIFIER NOT NULL,
                    [Created]  DATETIME2 NOT NULL,
                    [title]       NVARCHAR(300) NOT NULL,
                    [body]        NVARCHAR(MAX) NULL,
                    [source_type] NVARCHAR(60) NULL,
                    [source_id]   UNIQUEIDENTIFIER NULL,
                    [link]        NVARCHAR(500) NULL,
                    [is_read]     BIT NOT NULL CONSTRAINT [df_user_notifications_is_read] DEFAULT(0),
                    [read_at]     DATETIME2 NULL
                );
                CREATE INDEX [ix_user_notifications_user_unread]
                    ON [{s}].[user_notifications]([user_id], [is_read], [Created] DESC);
            END;

            IF OBJECT_ID(N'[{s}].[note_attachments]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[note_attachments]
                (
                    [id]             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [note_id]        UNIQUEIDENTIFIER NOT NULL,
                    [file_name]      NVARCHAR(255) NOT NULL,
                    [stored_name]    NVARCHAR(255) NOT NULL,
                    [content_type]   NVARCHAR(100) NULL,
                    [file_size]      BIGINT NOT NULL CONSTRAINT [df_note_attachments_file_size] DEFAULT(0),
                    [uploaded_at]    DATETIME2(0) NOT NULL,
                    [description]    NVARCHAR(500) NULL,
                    [binary_content] VARBINARY(MAX) NULL,
                    CONSTRAINT [fk_note_attachments_note_id]
                        FOREIGN KEY ([note_id]) REFERENCES [{s}].[notes]([id])
                );
                CREATE INDEX [ix_note_attachments_note_id] ON [{s}].[note_attachments]([note_id]);
            END;
            ELSE
            BEGIN
                -- Migration: description kolonu ekle
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[note_attachments]') AND name = N'description')
                BEGIN
                    ALTER TABLE [{s}].[note_attachments] ADD [description] NVARCHAR(500) NULL;
                END;

                -- Migration: binary_content kolonu — dosya icerigini DB'de saklar.
                -- NULL: legacy file system fallback (NotesController.DownloadAttachment
                -- once DB'den okur, yoksa wwwroot disindaki note-attachments klasorunden fallback).
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[note_attachments]') AND name = N'binary_content')
                BEGIN
                    ALTER TABLE [{s}].[note_attachments] ADD [binary_content] VARBINARY(MAX) NULL;
                END;
            END;

            IF OBJECT_ID(N'[{s}].[card_groups]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[card_groups]
                (
                    [id]          INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [card_type]   TINYINT NOT NULL,
                    [level]       TINYINT NOT NULL,
                    [parent_id]   INT NULL,
                    [code]        NVARCHAR(20) NOT NULL,
                    [description] NVARCHAR(200) NULL
                );
                CREATE INDEX [ix_card_groups_type_level] ON [{s}].[card_groups]([card_type], [level]);
            END;

            IF OBJECT_ID(N'[{s}].[card_group_mappings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[card_group_mappings]
                (
                    [id]            INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [entity_type]   TINYINT NOT NULL,
                    [entity_id]     NVARCHAR(50) NOT NULL,
                    [level]         TINYINT NOT NULL,
                    [card_group_id] INT NOT NULL,
                    CONSTRAINT [uq_card_group_mappings] UNIQUE ([entity_type], [entity_id], [level])
                );
                CREATE INDEX [ix_card_group_mappings_entity]
                    ON [{s}].[card_group_mappings]([entity_type], [entity_id]);
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureBOMTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            -- ── BOM migration: legacy code-based kolonlar → FK-based schema ──
            -- Eski: ParentMaterialCode, ConfigurationCode (string)
            -- Yeni: ItemId (FK Items), ConfigId (FK ItemConfiguration), ImageRotation (INT, derece)
            IF OBJECT_ID(N'[{s}].[BOM]', N'U') IS NOT NULL
            BEGIN
                -- 1) Yeni FK kolonlari ekle (nullable initially — backfill icin)
                IF COL_LENGTH(N'[{s}].[BOM]', N'ItemId') IS NULL
                    ALTER TABLE [{s}].[BOM] ADD [ItemId] INT NULL;

                IF COL_LENGTH(N'[{s}].[BOM]', N'ConfigId') IS NULL
                    ALTER TABLE [{s}].[BOM] ADD [ConfigId] INT NULL;

                IF COL_LENGTH(N'[{s}].[BOM]', N'ImageRotation') IS NULL
                    ALTER TABLE [{s}].[BOM] ADD [ImageRotation] INT NOT NULL CONSTRAINT [df_BOM_ImageRotation] DEFAULT(0);

                -- 2) Backfill: ParentMaterialCode → ItemId (Items.code lookup)
                -- EXEC ile sarmalayalim (deferred name resolution: yeni eklenen kolonlar)
                IF COL_LENGTH(N'[{s}].[BOM]', N'ParentMaterialCode') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[Items]', N'U') IS NOT NULL
                BEGIN
                    EXEC('
                        UPDATE b SET b.[ItemId] = i.[id]
                        FROM [{s}].[BOM] b
                        INNER JOIN [{s}].[Items] i ON i.[code] = b.[ParentMaterialCode]
                        WHERE b.[ItemId] IS NULL
                    ');
                END;

                -- 3) Backfill: ConfigurationCode → ConfigId (ItemConfiguration.RecordCode lookup)
                IF COL_LENGTH(N'[{s}].[BOM]', N'ConfigurationCode') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[ItemConfiguration]', N'U') IS NOT NULL
                BEGIN
                    EXEC('
                        UPDATE b SET b.[ConfigId] = cfg.[Id]
                        FROM [{s}].[BOM] b
                        INNER JOIN [{s}].[ItemConfiguration] cfg ON cfg.[RecordCode] = b.[ConfigurationCode]
                        WHERE b.[ConfigId] IS NULL AND b.[ConfigurationCode] IS NOT NULL
                    ');
                END;

                -- 4) Eski kolonlari drop et
                IF COL_LENGTH(N'[{s}].[BOM]', N'ParentMaterialCode') IS NOT NULL
                    ALTER TABLE [{s}].[BOM] DROP COLUMN [ParentMaterialCode];

                IF COL_LENGTH(N'[{s}].[BOM]', N'ConfigurationCode') IS NOT NULL
                    ALTER TABLE [{s}].[BOM] DROP COLUMN [ConfigurationCode];
            END;

            IF OBJECT_ID(N'[{s}].[BOM]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[BOM]
                (
                    [Id]                   INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [ItemId]               INT            NOT NULL,
                    [ConfigId]             INT            NULL,
                    [Description]          NVARCHAR(500)  NULL,
                    [ImageData]            VARBINARY(MAX) NULL,
                    [ImageMimeType]        NVARCHAR(100)  NULL,
                    [ImageFitMode]         NVARCHAR(20)   NULL,
                    [ImageRotation]        INT            NOT NULL CONSTRAINT [df_BOM_ImageRotation] DEFAULT(0),
                    [CreatedAt]            DATETIME2      NOT NULL CONSTRAINT [df_product_trees_created_at]  DEFAULT GETDATE(),
                    [UpdatedAt]            DATETIME2      NOT NULL CONSTRAINT [df_product_trees_updated_at]  DEFAULT GETDATE()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[BOM]') AND name = N'ImageFitMode')
                ALTER TABLE [{s}].[BOM] ADD [ImageFitMode] NVARCHAR(20) NULL;

            -- ── BOMLine migration ──
            IF OBJECT_ID(N'[{s}].[BOMLine]', N'U') IS NOT NULL
            BEGIN
                -- 1) Yeni FK kolonlari ekle
                IF COL_LENGTH(N'[{s}].[BOMLine]', N'ItemId') IS NULL
                    ALTER TABLE [{s}].[BOMLine] ADD [ItemId] INT NULL;

                IF COL_LENGTH(N'[{s}].[BOMLine]', N'ConfigId') IS NULL
                    ALTER TABLE [{s}].[BOMLine] ADD [ConfigId] INT NULL;

                -- 2) Backfill: ComponentMaterialCode → ItemId
                IF COL_LENGTH(N'[{s}].[BOMLine]', N'ComponentMaterialCode') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[Items]', N'U') IS NOT NULL
                BEGIN
                    EXEC('
                        UPDATE bl SET bl.[ItemId] = i.[id]
                        FROM [{s}].[BOMLine] bl
                        INNER JOIN [{s}].[Items] i ON i.[code] = bl.[ComponentMaterialCode]
                        WHERE bl.[ItemId] IS NULL
                    ');
                END;

                -- 3) Backfill: ComponentConfigCode → ConfigId
                IF COL_LENGTH(N'[{s}].[BOMLine]', N'ComponentConfigCode') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[ItemConfiguration]', N'U') IS NOT NULL
                BEGIN
                    EXEC('
                        UPDATE bl SET bl.[ConfigId] = cfg.[Id]
                        FROM [{s}].[BOMLine] bl
                        INNER JOIN [{s}].[ItemConfiguration] cfg ON cfg.[RecordCode] = bl.[ComponentConfigCode]
                        WHERE bl.[ConfigId] IS NULL AND bl.[ComponentConfigCode] IS NOT NULL
                    ');
                END;

                -- 4) Eski kolonlari drop et
                IF COL_LENGTH(N'[{s}].[BOMLine]', N'ComponentMaterialCode') IS NOT NULL
                    ALTER TABLE [{s}].[BOMLine] DROP COLUMN [ComponentMaterialCode];

                IF COL_LENGTH(N'[{s}].[BOMLine]', N'ComponentConfigCode') IS NOT NULL
                    ALTER TABLE [{s}].[BOMLine] DROP COLUMN [ComponentConfigCode];
            END;

            IF OBJECT_ID(N'[{s}].[BOMLine]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[BOMLine]
                (
                    [Id]                    INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [BOMId]                 INT             NOT NULL,
                    [ItemId]                INT             NOT NULL,
                    [ConfigId]              INT             NULL,
                    [Quantity]              DECIMAL(18,4)   NOT NULL CONSTRAINT [df_product_tree_lines_qty]   DEFAULT 1,
                    [ScrapRatio]            DECIMAL(18,4)   NOT NULL CONSTRAINT [df_product_tree_lines_scrap] DEFAULT 0,
                    [LineGuid]              UNIQUEIDENTIFIER NOT NULL CONSTRAINT [df_product_tree_lines_guid] DEFAULT NEWID(),
                    CONSTRAINT [fk_product_tree_lines_trees]
                        FOREIGN KEY ([BOMId]) REFERENCES [{s}].[BOM]([Id]) ON DELETE CASCADE
                );
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureMaterialGroupTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[MaterialGroups]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[MaterialGroups]
                (
                    [Id]               INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [GroupCategory]    TINYINT        NOT NULL DEFAULT 1,
                    [GroupCode]        NVARCHAR(10)   NOT NULL,
                    [GroupDescription] NVARCHAR(100)  NULL,
                    CONSTRAINT [uq_material_groups_cat_code] UNIQUE ([GroupCategory], [GroupCode])
                );
            END
            ELSE
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[MaterialGroups]') AND name = N'GroupCategory')
                    ALTER TABLE [{s}].[MaterialGroups] ADD [GroupCategory] TINYINT NOT NULL DEFAULT 1;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[{s}].[MaterialGroups]') AND name = N'uq_material_groups_code')
                    ALTER TABLE [{s}].[MaterialGroups] DROP CONSTRAINT [uq_material_groups_code];
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[{s}].[MaterialGroups]') AND name = N'uq_material_groups_cat_code')
                    ALTER TABLE [{s}].[MaterialGroups] ADD CONSTRAINT [uq_material_groups_cat_code] UNIQUE ([GroupCategory], [GroupCode]);
            END;

            IF OBJECT_ID(N'[{s}].[MaterialGroupMappings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[MaterialGroupMappings]
                (
                    [Id]          INT              NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [ItemId] INT NOT NULL,
                    [SlotOrder]   TINYINT          NOT NULL,
                    [GroupCode]   NVARCHAR(10)     NOT NULL,
                    CONSTRAINT [uq_mat_group_mappings_slot] UNIQUE ([ItemId], [SlotOrder])
                );
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureDesignTemplatesTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[design_templates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[design_templates]
                (
                    [id]           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [name]         NVARCHAR(200)    NOT NULL,
                    [type]         NVARCHAR(50)     NOT NULL,
                    [sub_type]     NVARCHAR(100)    NULL,
                    [description]  NVARCHAR(500)    NULL,
                    [html_content] NVARCHAR(MAX)    NULL,
                    [css_content]  NVARCHAR(MAX)    NULL,
                    [gjs_data]     NVARCHAR(MAX)    NULL,
                    [jsr_content]  NVARCHAR(MAX)    NULL,
                    [is_active]    BIT              NOT NULL CONSTRAINT [df_design_templates_is_active] DEFAULT(1),
                    [Created]   DATETIME2(0)     NOT NULL,
                    [Updated]   DATETIME2(0)     NULL    
                );
                CREATE INDEX [ix_design_templates_type]     ON [{s}].[design_templates]([type]);
                CREATE INDEX [ix_design_templates_sub_type] ON [{s}].[design_templates]([sub_type]);
            END
            ELSE
            BEGIN
                -- Migration: mevcut tabloya sub_type kolonu ekle
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[design_templates]') AND name = N'sub_type')
                BEGIN
                    ALTER TABLE [{s}].[design_templates] ADD [sub_type] NVARCHAR(100) NULL;
                    CREATE INDEX [ix_design_templates_sub_type] ON [{s}].[design_templates]([sub_type]);
                END;
                -- Migration: frx_content → jsr_content (FastReport yerine jsreport)
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[design_templates]') AND name = N'jsr_content')
                    ALTER TABLE [{s}].[design_templates] ADD [jsr_content] NVARCHAR(MAX) NULL;
                -- frx_content varsa silinmeden önce içeriği jsr_content'e taşı (deferred compile için EXEC)
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[design_templates]') AND name = N'frx_content')
                    EXEC(N'UPDATE [{s}].[design_templates] SET [jsr_content] = [frx_content] WHERE [jsr_content] IS NULL AND [frx_content] IS NOT NULL; ALTER TABLE [{s}].[design_templates] DROP COLUMN [frx_content];');
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureFinanceTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var schemaLiteral = _schema.Replace("'", "''");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[Contact]
                (
                    [Id]             INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [CompanyId]      INT            NOT NULL,
                    [AccountType]    TINYINT        NOT NULL DEFAULT 1,
                    [AccountCode]    NVARCHAR(20)   NOT NULL,
                    [AccountTitle]   NVARCHAR(200)  NOT NULL,
                    [TaxNumber]      NVARCHAR(10)   NULL,
                    [IdentityNumber] NVARCHAR(11)   NULL,
                    [TaxOffice]      NVARCHAR(100)  NULL,
                    [Phone]          NVARCHAR(30)   NULL,
                    [Mobile]         NVARCHAR(30)   NULL,
                    [WaPhone]        NVARCHAR(30)   NULL,
                    [WaName]         NVARCHAR(150)  NULL,
                    [Email]          NVARCHAR(200)  NULL,
                    [Website]        NVARCHAR(200)  NULL,
                    [Address]        NVARCHAR(500)  NULL,
                    [PostalCode]     NVARCHAR(20)   NULL,
                    [City]           NVARCHAR(100)  NULL,
                    [District]       NVARCHAR(100)  NULL,
                    [Neighborhood]   NVARCHAR(150)  NULL,
                    [CountryCode]    NVARCHAR(2)    NULL,
                    [ContactPerson]  NVARCHAR(150)  NULL,
                    [IsActive]       BIT            NOT NULL DEFAULT 1,
                    [PriceGroupId]   INT            NULL,
                    [SalesRepresentativeId] INT     NULL,
                    [CreatedAt]      DATETIME2      NOT NULL,
                    CONSTRAINT [uq_contact_accounts_code] UNIQUE ([AccountCode])
                );

                CREATE INDEX [ix_contact_accounts_type]
                    ON [{s}].[Contact]([AccountType]);

                CREATE INDEX [ix_contact_company]
                    ON [{s}].[Contact]([CompanyId]);
            END;

            -- Mevcut DB'ler icin migrate: WaPhone + WaName kolonlari (Faz 1: WhatsApp musteri eslestirmesi)
            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.Contact', N'WaPhone') IS NULL
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [WaPhone] NVARCHAR(30) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.Contact', N'WaName') IS NULL
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [WaName] NVARCHAR(150) NULL;
            END;

            -- Mevcut DB'ler icin migrate: CompanyId kolonu (multi-tenant izolasyon)
            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.Contact', N'CompanyId') IS NULL
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [CompanyId] INT NULL;

                EXEC(N'UPDATE [{s}].[Contact] SET [CompanyId] = 1 WHERE [CompanyId] IS NULL;');

                EXEC(N'ALTER TABLE [{s}].[Contact] ALTER COLUMN [CompanyId] INT NOT NULL;');

                EXEC(N'CREATE INDEX [ix_contact_company] ON [{s}].[Contact]([CompanyId]);');
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────
    // PostalLocality (PTT denormalize katalog) + ContactAddress tablolari
    // ──────────────────────────────────────────────────────────────
    private async Task EnsureAddressTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[PostalLocality]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[PostalLocality]
                (
                    [Id]               INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_postal_locality] PRIMARY KEY,
                    [CountryCode]      NVARCHAR(2)       NOT NULL DEFAULT(N'TR'),
                    [CityCode]         NVARCHAR(10)      NULL,
                    [CityName]         NVARCHAR(100)     NOT NULL,
                    [DistrictName]     NVARCHAR(100)     NOT NULL,
                    [NeighborhoodName] NVARCHAR(150)     NOT NULL,
                    [PostalCode]       NVARCHAR(10)      NULL
                );
                CREATE INDEX [ix_postal_locality_cascade] ON [{s}].[PostalLocality]
                    ([CountryCode],[CityName],[DistrictName],[NeighborhoodName]);
                CREATE INDEX [ix_postal_locality_zip] ON [{s}].[PostalLocality]([PostalCode]);
            END;

            IF OBJECT_ID(N'[{s}].[ContactAddress]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[ContactAddress]
                (
                    [Id]               INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_contact_address] PRIMARY KEY,
                    [ContactId]        INT              NOT NULL,
                    [Name]             NVARCHAR(100)    NOT NULL,
                    [CountryCode]      NVARCHAR(2)      NULL,
                    [CityName]         NVARCHAR(100)    NULL,
                    [DistrictName]     NVARCHAR(100)    NULL,
                    [NeighborhoodName] NVARCHAR(150)    NULL,
                    [PostalCode]       NVARCHAR(10)     NULL,
                    [AddressLine]      NVARCHAR(500)    NULL,
                    [IsDefault]        BIT              NOT NULL DEFAULT(0),
                    [CreatedAt]        DATETIME2        NOT NULL DEFAULT(SYSDATETIME()),
                    CONSTRAINT [fk_contact_address_contact] FOREIGN KEY ([ContactId])
                        REFERENCES [{s}].[Contact]([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [ix_contact_address_contact] ON [{s}].[ContactAddress]([ContactId]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────
    // ContactItem — cari × stok eslestirmesi (cari'nin verdigi kod/ad)
    // ──────────────────────────────────────────────────────────────
    private async Task EnsureContactItemTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[ContactItem]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[ContactItem]
                (
                    [Id]         INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_contact_item] PRIMARY KEY,
                    [ContactId]  INT              NOT NULL,
                    [ItemId]     INT              NOT NULL,
                    [VendorCode] NVARCHAR(60)     NULL,
                    [VendorName] NVARCHAR(200)    NULL,
                    [Notes]      NVARCHAR(500)    NULL,
                    [IsActive]   BIT              NOT NULL CONSTRAINT [df_contact_item_active]  DEFAULT(1),
                    [CreatedAt]  DATETIME2(0)     NOT NULL CONSTRAINT [df_contact_item_created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt]  DATETIME2(0)     NULL,
                    CONSTRAINT [fk_contact_item_contact] FOREIGN KEY ([ContactId])
                        REFERENCES [{s}].[Contact]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [fk_contact_item_item] FOREIGN KEY ([ItemId])
                        REFERENCES [{s}].[Items]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [ux_contact_item_contact_item] ON [{s}].[ContactItem]([ContactId],[ItemId]);
                CREATE INDEX [ix_contact_item_item]         ON [{s}].[ContactItem]([ItemId]) WHERE [IsActive] = 1;
                CREATE INDEX [ix_contact_item_vendor_code]  ON [{s}].[ContactItem]([VendorCode]) WHERE [VendorCode] IS NOT NULL;
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureIntegrationEventTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[integration_event_definitions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[integration_event_definitions]
                (
                    [id]              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [company_id]      INT NOT NULL,
                    [name]            NVARCHAR(200)    NOT NULL,
                    [event_source]    NVARCHAR(100)    NOT NULL,
                    [event_type]      NVARCHAR(50)     NOT NULL,
                    [event_detail]    NVARCHAR(200)    NULL,
                    [sql_command]     NVARCHAR(MAX)    NOT NULL,
                    [stop_on_error]   BIT              NOT NULL DEFAULT(1),
                    [is_active]       BIT              NOT NULL DEFAULT(1),
                    [execution_order] INT              NOT NULL DEFAULT(0),
                    [Created]      DATETIME2(0)     NOT NULL,
                    [Updated]      DATETIME2(0)     NULL    
                );
                CREATE INDEX [ix_ied_lookup] ON [{s}].[integration_event_definitions]
                    ([company_id], [event_source], [event_type], [is_active]);
            END;

            IF OBJECT_ID(N'[{s}].[integration_event_logs]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[integration_event_logs]
                (
                    [id]            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [definition_id] UNIQUEIDENTIFIER NOT NULL,
                    [company_id]    INT NOT NULL,
                    [event_source]  NVARCHAR(100)    NOT NULL,
                    [event_type]    NVARCHAR(50)     NOT NULL,
                    [executed_sql]  NVARCHAR(MAX)    NOT NULL,
                    [success]       BIT              NOT NULL,
                    [error_message] NVARCHAR(MAX)    NULL,
                    [executed_at]   DATETIME2(0)     NOT NULL,
                    [duration_ms]   BIGINT           NOT NULL DEFAULT(0)
                );
                CREATE INDEX [ix_iel_browse] ON [{s}].[integration_event_logs]
                    ([company_id], [executed_at] DESC);
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MigrateIntegrationEventsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        // Each DDL statement runs in its own batch to avoid SQL Server compile-time
        // schema-binding issues when dropping and re-adding a column in the same batch.
        var steps = new List<string>
        {
            // 1a — drop lookup index on definitions (if column is still UNIQUEIDENTIFIER)
            $"""
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_definitions]')
                  AND name = 'company_id' AND system_type_id = TYPE_ID('uniqueidentifier')
            ) AND EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_definitions]') AND name = 'ix_ied_lookup')
                DROP INDEX [ix_ied_lookup] ON [{s}].[integration_event_definitions];
            """,
            // 1b — drop the UNIQUEIDENTIFIER column
            $"""
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_definitions]')
                  AND name = 'company_id' AND system_type_id = TYPE_ID('uniqueidentifier')
            )
                ALTER TABLE [{s}].[integration_event_definitions] DROP COLUMN [company_id];
            """,
            // 1c — add back as INT
            $"""
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_definitions]')
                  AND name = 'company_id'
            )
                ALTER TABLE [{s}].[integration_event_definitions] ADD [company_id] INT NOT NULL DEFAULT(0);
            """,
            // 1d — recreate index
            $"""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_definitions]') AND name = 'ix_ied_lookup')
                CREATE INDEX [ix_ied_lookup] ON [{s}].[integration_event_definitions]
                    ([company_id], [event_source], [event_type], [is_active]);
            """,

            // 2a — drop browse index on logs
            $"""
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_logs]')
                  AND name = 'company_id' AND system_type_id = TYPE_ID('uniqueidentifier')
            ) AND EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_logs]') AND name = 'ix_iel_browse')
                DROP INDEX [ix_iel_browse] ON [{s}].[integration_event_logs];
            """,
            // 2b — drop UNIQUEIDENTIFIER column in logs
            $"""
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_logs]')
                  AND name = 'company_id' AND system_type_id = TYPE_ID('uniqueidentifier')
            )
                ALTER TABLE [{s}].[integration_event_logs] DROP COLUMN [company_id];
            """,
            // 2c — add back as INT
            $"""
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_logs]')
                  AND name = 'company_id'
            )
                ALTER TABLE [{s}].[integration_event_logs] ADD [company_id] INT NOT NULL DEFAULT(0);
            """,
            // 2d — recreate index
            $"""
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_event_logs]') AND name = 'ix_iel_browse')
                CREATE INDEX [ix_iel_browse] ON [{s}].[integration_event_logs]
                    ([company_id], [executed_at] DESC);
            """,

            // 3 — extra columns
            $"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[integration_event_definitions]') AND name='action_type') ALTER TABLE [{s}].[integration_event_definitions] ADD [action_type] NVARCHAR(50) NOT NULL DEFAULT 'SqlCommand';",
            $"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[integration_event_definitions]') AND name='procedure_name') ALTER TABLE [{s}].[integration_event_definitions] ADD [procedure_name] NVARCHAR(200) NULL;",
            $"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[integration_event_definitions]') AND name='parameters_json') ALTER TABLE [{s}].[integration_event_definitions] ADD [parameters_json] NVARCHAR(MAX) NULL;",
            $"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[integration_event_definitions]') AND name='api_config_json') ALTER TABLE [{s}].[integration_event_definitions] ADD [api_config_json] NVARCHAR(MAX) NULL;",
            $"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[integration_event_logs]') AND name='action_type') ALTER TABLE [{s}].[integration_event_logs] ADD [action_type] NVARCHAR(50) NULL DEFAULT 'SqlCommand';",
            $"IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[integration_event_logs]') AND name='response_body') ALTER TABLE [{s}].[integration_event_logs] ADD [response_body] NVARCHAR(MAX) NULL;",
        };

        foreach (var step in steps)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = step;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task EnsureIntegrationApiProfilesTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        // Mevcut id kolonu tipini oku
        string? currentIdType = null;
        await using (var typeCmd = connection.CreateCommand())
        {
            typeCmd.CommandText = $"""
                SELECT t.name
                FROM sys.columns c
                JOIN sys.types t ON c.system_type_id = t.system_type_id AND t.is_user_defined = 0
                WHERE c.object_id = OBJECT_ID(N'[{s}].[integration_api_profiles]') AND c.name = 'id';
                """;
            var result = await typeCmd.ExecuteScalarAsync(cancellationToken);
            currentIdType = result as string;
        }
        Console.WriteLine($"[DB INIT] integration_api_profiles.id current type: {currentIdType ?? "(tablo yok)"}");

        // Adim 1: id kolonu INT ise tabloyu dusur
        if (currentIdType != null && currentIdType.Equals("int", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[DB INIT] integration_api_profiles INT id tespit edildi — tablo yeniden olusturuluyor...");
            await using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = $"DROP TABLE [{s}].[integration_api_profiles];";
            await dropCmd.ExecuteNonQueryAsync(cancellationToken);
            Console.WriteLine("[DB INIT] integration_api_profiles DROP tamamlandi.");
        }

        // Adim 2: Tablo yoksa olustur (DROP sonrasi veya hic olusturulmamissa)
        await using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID(N'[{s}].[integration_api_profiles]'))
                BEGIN
                    CREATE TABLE [{s}].[integration_api_profiles] (
                        [id]               UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() CONSTRAINT PK_integration_api_profiles PRIMARY KEY,
                        [company_id]       INT NOT NULL,
                        [name]             NVARCHAR(200)    NOT NULL,
                        [auth_type]        NVARCHAR(50)     NOT NULL DEFAULT 'None',
                        [base_url]         NVARCHAR(500)    NOT NULL,
                        [auth_config_json] NVARCHAR(MAX)    NULL,
                        [is_active]        BIT              NOT NULL DEFAULT 1,
                        [Created]       DATETIME2        NOT NULL DEFAULT GETDATE(),
                        [Updated]       DATETIME2        NULL     DEFAULT GETDATE()
                    );
                END
                """;
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Adim 3: company_id kolonu UNIQUEIDENTIFIER ise INT'e donustur
        foreach (var step in new[]
        {
            // 3a - company_id UNIQUEIDENTIFIER ise dusur
            $"""
            IF EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_api_profiles]')
                  AND name = 'company_id' AND system_type_id = TYPE_ID('uniqueidentifier')
            )
                ALTER TABLE [{s}].[integration_api_profiles] DROP COLUMN [company_id];
            """,
            // 3b - company_id kolonu yoksa INT olarak ekle
            $"""
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[integration_api_profiles]')
                  AND name = 'company_id'
            )
                ALTER TABLE [{s}].[integration_api_profiles] ADD [company_id] INT NOT NULL DEFAULT({DefaultCompanyId});
            """,
            // 3c - company_id = 0 olan satirlari default company ile guncelle
            $"""
            UPDATE [{s}].[integration_api_profiles]
            SET [company_id] = {DefaultCompanyId}
            WHERE [company_id] = 0;
            """,
        })
        {
            await using var stepCmd = connection.CreateCommand();
            stepCmd.CommandText = step;
            await stepCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        Console.WriteLine("[DB INIT] EnsureIntegrationApiProfilesTableAsync tamamlandi.");
    }

    private async Task EnsureDynamicFieldValuesTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            -- dynamic_field_values tablosu
            IF OBJECT_ID(N'[{s}].[dynamic_field_values]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[dynamic_field_values]
                (
                    [id]                  UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [screen_code]         NVARCHAR(60)     NOT NULL,
                    [entity_id]           UNIQUEIDENTIFIER NOT NULL,
                    [field_definition_id] UNIQUEIDENTIFIER NOT NULL,
                    [field_key]           NVARCHAR(60)     NOT NULL,
                    [text_value]          NVARCHAR(MAX)    NULL,
                    [numeric_value]       DECIMAL(18,4)    NULL,
                    [date_value]          DATETIME2        NULL,
                    [boolean_value]       BIT              NULL,
                    [Created]          DATETIME2(0)     NOT NULL,
                    [Updated]          DATETIME2(0)     NULL    
                );
                CREATE INDEX [ix_dfv_entity] ON [{s}].[dynamic_field_values]
                    ([screen_code], [entity_id]);
            END;

            -- material_card_field_groups: screen_code kolonu ekle
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[FieldGroup]') AND name = N'screen_code')
                ALTER TABLE [{s}].[FieldGroup] ADD [screen_code] NVARCHAR(60) NOT NULL DEFAULT(N'MaterialCards');

            -- material_card_field_settings: screen_code kolonu ekle
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[Field]') AND name = N'screen_code')
                ALTER TABLE [{s}].[Field] ADD [screen_code] NVARCHAR(60) NOT NULL DEFAULT(N'MaterialCards');

            -- material_card_field_settings: layer_key kolonu ekle (multi-layer ekranlar icin: header/line/conditions)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[Field]') AND name = N'layer_key')
                ALTER TABLE [{s}].[Field] ADD [layer_key] NVARCHAR(32) NULL;

            -- material_card_field_groups: layer_key kolonu ekle
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[FieldGroup]') AND name = N'layer_key')
                ALTER TABLE [{s}].[FieldGroup] ADD [layer_key] NVARCHAR(32) NULL;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>sales_quote_attachments — teklife dosya ekleme tablosu.</summary>
    private async Task EnsureDocumentAttachmentsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[sales_quote_attachments]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[sales_quote_attachments]
                (
                    [id]           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [document_id]     UNIQUEIDENTIFIER NOT NULL,
                    [file_name]    NVARCHAR(300)    NOT NULL,
                    [mime_type]    NVARCHAR(120)    NULL,
                    [file_size]    BIGINT           NOT NULL,
                    [content]      VARBINARY(MAX)   NOT NULL,
                    [uploaded_by]  NVARCHAR(120)    NULL,
                    [uploaded_at]  DATETIME2(0)     NOT NULL,
                    [is_active]    BIT              NOT NULL DEFAULT(1)
                );
                CREATE INDEX [ix_sales_quote_attachments_quote] ON [{s}].[sales_quote_attachments]([document_id]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureDocumentTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[Document]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[Document]
                (
                    [id]                INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_document] PRIMARY KEY,
                    [CompanyId]         INT              NOT NULL CONSTRAINT [df_document_CompanyId] DEFAULT({DefaultCompanyId}),
                    [DocumentNumber]    NVARCHAR(15)     NOT NULL,
                    [DocumentTypeId]    INT              NULL,
                    [DocumentDate]      DATETIME2(0)     NOT NULL,
                    [ValidUntil]        DATETIME2(0)     NULL,
                    [ContactId]         INT              NULL,
                    [ContactAddress]    NVARCHAR(500)    NULL,
                    [SalesRepId]        INT              NULL,
                    [currency]          NVARCHAR(5)      NOT NULL DEFAULT(N'TRY'),
                    [SubTotal]          DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [DiscountRate]      DECIMAL(5,2)     NOT NULL DEFAULT(0),
                    [DiscountAmount]    DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [TaxRate]           DECIMAL(5,2)     NOT NULL DEFAULT(20),
                    [TaxAmount]         DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [GrandTotal]        DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [PaymentTerms]      NVARCHAR(500)    NULL,
                    [DeliveryTerms]     NVARCHAR(500)    NULL,
                    [DeliveryAddress]   NVARCHAR(500)    NULL,
                    [status]            NVARCHAR(20)     NOT NULL DEFAULT(N'Draft'),
                    [RevisionNo]        INT              NOT NULL DEFAULT(0),
                    [ParentDocumentId]  INT              NULL,
                    [notes]             NVARCHAR(MAX)    NULL,
                    [CreatedBy]         NVARCHAR(120)    NULL,
                    [Created]           DATETIME2(0)     NOT NULL,
                    [Updated]           DATETIME2(0)     NULL,
                    [IsActive]          BIT              NOT NULL DEFAULT(1)
                );
                CREATE UNIQUE INDEX [ux_document_number] ON [{s}].[Document]([DocumentNumber]);
                CREATE INDEX [ix_document_status] ON [{s}].[Document]([status]);
                CREATE INDEX [ix_document_contact] ON [{s}].[Document]([ContactId]);
                CREATE INDEX [ix_document_company] ON [{s}].[Document]([CompanyId]);
            END;

            -- SalesRepId kolonu ekle (yoksa — eski DB icin)
            IF COL_LENGTH(N'[{s}].[Document]', N'SalesRepId') IS NULL
               AND COL_LENGTH(N'[{s}].[Document]', N'sales_rep_id') IS NULL
                ALTER TABLE [{s}].[Document] ADD [SalesRepId] INT NULL;

            -- DocumentTypeId FK kolonu (Phase 2 konsolidasyon). NULL satirlari
            -- BackfillDocumentTypeIdsAsync tarafindan (document_types seed'inden sonra) doldurulur.
            IF COL_LENGTH(N'[{s}].[Document]', N'DocumentTypeId') IS NULL
               AND COL_LENGTH(N'[{s}].[Document]', N'document_type_id') IS NULL
                ALTER TABLE [{s}].[Document] ADD [DocumentTypeId] INT NULL;

            -- CompanyId FK kolonu — belgenin hangi sirkete ait oldugu.
            -- Mevcut kayitlar DefaultCompanyId (1) ile backfill edilir.
            IF COL_LENGTH(N'[{s}].[Document]', N'CompanyId') IS NULL
               AND COL_LENGTH(N'[{s}].[Document]', N'company_id') IS NULL
            BEGIN
                ALTER TABLE [{s}].[Document]
                ADD [CompanyId] INT NOT NULL
                    CONSTRAINT [df_document_CompanyId] DEFAULT({DefaultCompanyId});
                EXEC(N'UPDATE [{s}].[Document]
                       SET [CompanyId] = {DefaultCompanyId}
                       WHERE [CompanyId] = 0');
            END;
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_document_company'
                           AND object_id = OBJECT_ID(N'[{s}].[Document]'))
               AND COL_LENGTH(N'[{s}].[Document]', N'CompanyId') IS NOT NULL
                CREATE INDEX [ix_document_company] ON [{s}].[Document]([CompanyId]);

            IF OBJECT_ID(N'[{s}].[DocumentLine]', N'U') IS NULL
            BEGIN
                -- Satir tablosu — material/unit/combination/location display degerleri
                -- tutulmaz; id FK'leri ile Item/Unit/ProductConfiguration/Location'dan okunur.
                CREATE TABLE [{s}].[DocumentLine]
                (
                    [id]              INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_document_line] PRIMARY KEY,
                    [document_id]     INT              NOT NULL,
                    [line_no]         INT              NOT NULL DEFAULT(0),
                    [item_id]         INT              NOT NULL,
                    [unit_id]         INT              NULL,
                    [quantity]        DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [unit_price]      DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [discount_rate]   DECIMAL(5,2)     NOT NULL DEFAULT(0),
                    [line_total]      DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [combination_id]  INT              NULL,
                    [location_id]     INT              NULL,
                    [notes]           NVARCHAR(500)    NULL,
                    [notes_pinned]    BIT              NOT NULL DEFAULT(0),
                    -- Revize zinciri: bu satir hangi DocumentLine'dan revize edildi?
                    -- NULL => orijinal satir. Zincir geriye takip edilerek kac
                    -- revize oldugu gorulebilir. Self-referencing FK.
                    [revised_from_id] INT              NULL,
                    CONSTRAINT [fk_document_line_document] FOREIGN KEY ([document_id])
                        REFERENCES [{s}].[Document]([id])
                );
                CREATE INDEX [ix_document_line_document] ON [{s}].[DocumentLine]([document_id]);
                -- Revize index dinamik EXEC — SQL Server batch derleme sirasinda
                -- mevcut (eski) sema uzerinde kolon dogrulama yapiyor ve IF blogu
                -- icinde de olsa "kolon yok" hatasi veriyor. Deferred parsing icin
                -- EXEC(N'...') kullaniyoruz; runtime'da kolon olustugundan calisir.
                EXEC(N'CREATE INDEX [ix_document_line_revised_from] ON [{s}].[DocumentLine]([revised_from_id]) WHERE [revised_from_id] IS NOT NULL;');
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // Faz 2 — Document.contact_name kolonunu kaldirma migration'i.
    // Once contact_id NULL olan satirlarda contact_name = Contact.AccountTitle
    // eslesmesi ile ID backfill edilir; sonra kolon drop edilir.
    //
    // NOT: SQL Server batch-level compile sirasinda kolon referanslari binding
    // edilir — IF ile sarmak yeterli degil. Bu yuzden backfill ve drop komutlari
    // EXEC ile dinamik calistirilir (deferred compile).
    // Idempotent: kolon yoksa no-op.
    private async Task MigrateDocumentContactNameDropAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s  = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[Document]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{sl}.[Document]', N'contact_name') IS NOT NULL
            BEGIN
                -- Backfill: contact_id NULL olan kayitlarda contact_name -> Contact eslesmesi
                EXEC(N'
                    UPDATE d
                        SET d.[ContactId] = c.[Id]
                    FROM [{s}].[Document] d
                    JOIN [{s}].[Contact] c
                      ON c.[AccountTitle] COLLATE Turkish_CI_AI = d.[contact_name] COLLATE Turkish_CI_AI
                     AND c.[IsActive] = 1
                    WHERE d.[ContactId] IS NULL
                      AND d.[contact_name] IS NOT NULL
                      AND LEN(LTRIM(RTRIM(d.[contact_name]))) > 0;
                ');

                -- Kolon kaldir — artik Contact join ile okunuyor
                EXEC(N'ALTER TABLE [{s}].[Document] DROP COLUMN [contact_name];');
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── User Settings ─────────────────────────────────────────────────────────

    private async Task EnsureUserSettingsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[user_settings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[user_settings]
                (
                    [id]            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [user_id]       UNIQUEIDENTIFIER NOT NULL,
                    [setting_key]   NVARCHAR(200)    NOT NULL,
                    [setting_value] NVARCHAR(MAX)    NULL,
                    [Updated]    DATETIME2(0)     NULL    
                );
                CREATE UNIQUE INDEX [ux_user_settings_user_key]
                    ON [{s}].[user_settings]([user_id], [setting_key]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Currencies & Exchange Rates ─────────────────────────────────────────

    private async Task EnsureCurrencyTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[currencies]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[currencies]
                (
                    [id]         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [code]       NVARCHAR(5)       NOT NULL,
                    [name]       NVARCHAR(100)     NOT NULL,
                    [symbol]     NVARCHAR(5)       NULL,
                    [is_active]  BIT               NOT NULL DEFAULT(1),
                    [Created] DATETIME2(0)      NOT NULL DEFAULT(GETDATE()),
                    [Updated] DATETIME2(0)      NULL     DEFAULT(GETDATE())
                );
                CREATE UNIQUE INDEX [ux_currencies_code] ON [{s}].[currencies]([code]);
            END;

            -- Migration: eski exchange_rates tablosu varsa Exchange'e yeniden adlandir (idempotent)
            IF OBJECT_ID(N'[{s}].[exchange_rates]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{s}].[Exchange]', N'U') IS NULL
            BEGIN
                EXEC sp_rename N'[{s}].[exchange_rates]', N'Exchange';
            END;

            -- Yeni kurulum: tablo henuz yoksa son schema ile olustur (CurrencyId FK + date)
            IF OBJECT_ID(N'[{s}].[Exchange]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[Exchange]
                (
                    [id]                     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CurrencyId]             INT               NOT NULL,
                    [date]                   DATE              NOT NULL,
                    [buying_rate]            DECIMAL(18,6)     NOT NULL,
                    [selling_rate]           DECIMAL(18,6)     NOT NULL,
                    [effective_buying_rate]  DECIMAL(18,6)     NOT NULL DEFAULT(0),
                    [effective_selling_rate] DECIMAL(18,6)     NOT NULL DEFAULT(0),
                    [source]                 NVARCHAR(20)      NOT NULL DEFAULT(N'TCMB'),
                    [Created]             DATETIME2(0)      NOT NULL DEFAULT(GETDATE())
                );
            END;

            -- Migration (idempotent): legacy denormalized columns → FK-based schema
            IF OBJECT_ID(N'[{s}].[Exchange]', N'U') IS NOT NULL
            BEGIN
                -- 1) Drop edilecek kolonlara referans veren eski indeksleri drop et
                IF EXISTS (SELECT 1 FROM sys.indexes
                           WHERE object_id = OBJECT_ID(N'[{s}].[Exchange]')
                             AND name = N'ux_exchange_rates_code_date')
                    DROP INDEX [ux_exchange_rates_code_date] ON [{s}].[Exchange];
                IF EXISTS (SELECT 1 FROM sys.indexes
                           WHERE object_id = OBJECT_ID(N'[{s}].[Exchange]')
                             AND name = N'ux_exchange_code_date')
                    DROP INDEX [ux_exchange_code_date] ON [{s}].[Exchange];

                -- 2) Eksik effective alanlari (eski sema icin)
                IF COL_LENGTH(N'[{s}].[Exchange]', N'effective_buying_rate') IS NULL
                    ALTER TABLE [{s}].[Exchange] ADD [effective_buying_rate] DECIMAL(18,6) NOT NULL DEFAULT(0);
                IF COL_LENGTH(N'[{s}].[Exchange]', N'effective_selling_rate') IS NULL
                    ALTER TABLE [{s}].[Exchange] ADD [effective_selling_rate] DECIMAL(18,6) NOT NULL DEFAULT(0);

                -- 3) Yeni FK kolonu (nullable, backfill icin)
                IF COL_LENGTH(N'[{s}].[Exchange]', N'CurrencyId') IS NULL
                    ALTER TABLE [{s}].[Exchange] ADD [CurrencyId] INT NULL;

                -- 4) rate_date → date rename
                IF COL_LENGTH(N'[{s}].[Exchange]', N'rate_date') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[Exchange]', N'date') IS NULL
                    EXEC sp_rename N'[{s}].[Exchange].rate_date', N'date', N'COLUMN';
            END;

            -- 5) Backfill: currency_code (string) → CurrencyId (currencies.id JOIN code).
            -- EXEC ile sarmaliyoruz; deferred name resolution sayesinde batch icinde ALTER ADD'lenen
            -- CurrencyId kolonu parse-time'da sorun cikarmaz.
            IF OBJECT_ID(N'[{s}].[Exchange]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[Exchange]', N'currency_code') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[Exchange]', N'CurrencyId') IS NOT NULL
               AND OBJECT_ID(N'[{s}].[currencies]', N'U') IS NOT NULL
            BEGIN
                EXEC('
                    UPDATE r SET r.[CurrencyId] = c.[id]
                    FROM [{s}].[Exchange] r
                    INNER JOIN [{s}].[currencies] c ON c.[code] = r.[currency_code]
                    WHERE r.[CurrencyId] IS NULL
                ');
            END;

            -- 6) currency_code drop (backfill bittiyse)
            IF OBJECT_ID(N'[{s}].[Exchange]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[Exchange]', N'currency_code') IS NOT NULL
            BEGIN
                ALTER TABLE [{s}].[Exchange] DROP COLUMN [currency_code];
            END;

            -- 7) Yeni unique index (CurrencyId+date) — filtered: orphan (NULL CurrencyId)
            -- kayitlari constraint disinda tutulur. Mapping basarisiz olan eski kayitlar
            -- kalsa bile yeni insert'lerde UNIQUE garantisi saglanir.
            IF OBJECT_ID(N'[{s}].[Exchange]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[Exchange]', N'CurrencyId') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[Exchange]', N'date') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.indexes
                               WHERE object_id = OBJECT_ID(N'[{s}].[Exchange]')
                                 AND name = N'ux_exchange_currency_date')
            BEGIN
                CREATE UNIQUE INDEX [ux_exchange_currency_date]
                    ON [{s}].[Exchange]([CurrencyId], [date])
                    WHERE [CurrencyId] IS NOT NULL;
            END;

            -- 8) FK constraint (data temiz oldugunda)
            IF OBJECT_ID(N'[{s}].[Exchange]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{s}].[currencies]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[Exchange]', N'CurrencyId') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys
                               WHERE name = N'fk_exchange_currency'
                                 AND parent_object_id = OBJECT_ID(N'[{s}].[Exchange]'))
               AND NOT EXISTS (SELECT 1 FROM [{s}].[Exchange] WHERE [CurrencyId] IS NULL)
            BEGIN
                ALTER TABLE [{s}].[Exchange] ADD CONSTRAINT [fk_exchange_currency]
                    FOREIGN KEY ([CurrencyId]) REFERENCES [{s}].[currencies]([id]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SeedCurrenciesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var seeds = new[] {
            ("TRY", "Turk Lirasi",       "₺"),
            ("USD", "Amerikan Dolari",    "$"),
            ("EUR", "Euro",              "€"),
            ("GBP", "Ingiliz Sterlini",  "£"),
        };
        foreach (var (code, name, symbol) in seeds)
        {
            var sql = $"""
                IF NOT EXISTS (SELECT 1 FROM [{s}].[currencies] WHERE [code] = @Code)
                    INSERT INTO [{s}].[currencies] ([code],[name],[symbol],[is_active],[Created],[Updated])
                    VALUES (@Code, @Name, @Symbol, 1, GETDATE(), GETDATE());
                """;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@Code", code);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@Symbol", symbol);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // ── Sales Representatives ─────────────────────────────────────────────────

    private async Task EnsureSalesRepresentativeTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[sales_representatives]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[sales_representatives]
                (
                    [id]         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [rep_code]   NVARCHAR(20)      NOT NULL,
                    [rep_name]   NVARCHAR(200)     NOT NULL,
                    [is_active]  BIT               NOT NULL DEFAULT(1),
                    [Created] DATETIME2(0)      NOT NULL DEFAULT(GETDATE()),
                    [Updated] DATETIME2(0)      NULL     DEFAULT(GETDATE())
                );
                CREATE UNIQUE INDEX [ux_sales_representatives_code]
                    ON [{s}].[sales_representatives]([rep_code]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Sales Quote Line Details + combination_id ────────────────────────────

    private async Task EnsureDocumentLineDetailsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");

        // DocumentLine schema migrasyonu: legacy kolonlari drop et (material_code,
        // material_name, combination_code, is_active) + combination_id ekle + item_id
        // NOT NULL'a cek. Tum adimlar idempotent — kolon yoksa no-op.
        var migrateSql = $"""
            IF OBJECT_ID(N'[{s}].[DocumentLine]', N'U') IS NOT NULL
            BEGIN
                -- combination_id ekle (henuz yoksa)
                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'combination_id') IS NULL
                    ALTER TABLE [{s}].[DocumentLine] ADD [combination_id] INT NULL;

                -- unit_id ekle (henuz yoksa) — unit_name'den turemis degerlerle backfill
                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'unit_id') IS NULL
                    ALTER TABLE [{s}].[DocumentLine] ADD [unit_id] INT NULL;

                -- location_id ekle (henuz yoksa)
                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'location_id') IS NULL
                    ALTER TABLE [{s}].[DocumentLine] ADD [location_id] INT NULL;

                -- notes_pinned ekle (satir notunun belge acilisinda acik gelmesi)
                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'notes_pinned') IS NULL
                    ALTER TABLE [{s}].[DocumentLine] ADD [notes_pinned] BIT NOT NULL CONSTRAINT [df_document_line_notes_pinned] DEFAULT(0);

                -- revised_from_id ekle (revize zinciri — satir hangi satirdan revize edildi?)
                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'revised_from_id') IS NULL
                BEGIN
                    ALTER TABLE [{s}].[DocumentLine] ADD [revised_from_id] INT NULL;
                END;
                -- Filtered index — revize edilmis satirlari hizli listelemek icin
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'ix_document_line_revised_from'
                                AND [object_id] = OBJECT_ID(N'[{s}].[DocumentLine]'))
                BEGIN
                    EXEC(N'CREATE INDEX [ix_document_line_revised_from] ON [{s}].[DocumentLine]([revised_from_id]) WHERE [revised_from_id] IS NOT NULL;');
                END;

                -- Eski unit_name'den unit_id backfill (ayni schema'daki Unit tablosu)
                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'unit_name') IS NOT NULL
                   AND COL_LENGTH(N'{sl}.[DocumentLine]', N'unit_id') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[Unit]', N'U') IS NOT NULL
                BEGIN
                    EXEC(N'
                        UPDATE l
                           SET l.[unit_id] = u.[Id]
                          FROM [{s}].[DocumentLine] l
                          JOIN [{s}].[Unit] u
                            ON u.[UnitCode] COLLATE Turkish_CI_AI = l.[unit_name] COLLATE Turkish_CI_AI
                            OR u.[UnitName] COLLATE Turkish_CI_AI = l.[unit_name] COLLATE Turkish_CI_AI
                         WHERE l.[unit_id] IS NULL AND l.[unit_name] IS NOT NULL;
                    ');
                END;

                -- Legacy kolonlari drop et — batch-level kolon binding problemini
                -- dinamik EXEC ile asiyoruz.
                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'unit_name') IS NOT NULL
                    EXEC(N'ALTER TABLE [{s}].[DocumentLine] DROP COLUMN [unit_name];');

                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'material_code') IS NOT NULL
                    EXEC(N'ALTER TABLE [{s}].[DocumentLine] DROP COLUMN [material_code];');

                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'material_name') IS NOT NULL
                    EXEC(N'ALTER TABLE [{s}].[DocumentLine] DROP COLUMN [material_name];');

                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'combination_code') IS NOT NULL
                    EXEC(N'ALTER TABLE [{s}].[DocumentLine] DROP COLUMN [combination_code];');

                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'is_active') IS NOT NULL
                BEGIN
                    -- Default constraint'i once kaldir, sonra kolonu drop et
                    DECLARE @dcName sysname;
                    SELECT @dcName = dc.[name]
                      FROM sys.default_constraints dc
                      INNER JOIN sys.columns c
                        ON c.[object_id] = dc.[parent_object_id]
                       AND c.[column_id] = dc.[parent_column_id]
                     WHERE dc.[parent_object_id] = OBJECT_ID(N'[{s}].[DocumentLine]')
                       AND c.[name] = N'is_active';
                    IF @dcName IS NOT NULL
                        EXEC(N'ALTER TABLE [{s}].[DocumentLine] DROP CONSTRAINT [' + @dcName + N'];');
                    EXEC(N'ALTER TABLE [{s}].[DocumentLine] DROP COLUMN [is_active];');
                END;

                -- item_id NOT NULL'a cek (null kayit yoksa — varsa temizle)
                IF COL_LENGTH(N'{sl}.[DocumentLine]', N'item_id') IS NOT NULL
                   AND EXISTS (SELECT 1 FROM sys.columns
                                WHERE [object_id] = OBJECT_ID(N'[{s}].[DocumentLine]')
                                  AND [name] = N'item_id' AND [is_nullable] = 1)
                BEGIN
                    EXEC(N'DELETE FROM [{s}].[DocumentLine] WHERE [item_id] IS NULL;');
                    EXEC(N'ALTER TABLE [{s}].[DocumentLine] ALTER COLUMN [item_id] INT NOT NULL;');
                END;
            END;
            """;
        await using (var cmd1 = connection.CreateCommand()) { cmd1.CommandText = migrateSql; await cmd1.ExecuteNonQueryAsync(cancellationToken); }

        // Detay tablosu
        var createSql = $"""
            IF OBJECT_ID(N'[{s}].[sales_quote_line_details]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[sales_quote_line_details]
                (
                    [id]             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [quote_line_id]  INT               NOT NULL,
                    [feature_name]   NVARCHAR(200)     NOT NULL,
                    [value_code]     NVARCHAR(100)     NOT NULL,
                    [value_name]     NVARCHAR(200)     NOT NULL,
                    [description]    NVARCHAR(500)     NULL,
                    [line_order]     INT               NOT NULL DEFAULT(0),
                    CONSTRAINT [fk_sqld_quote_line] FOREIGN KEY ([quote_line_id])
                        REFERENCES [{s}].[DocumentLine]([id]) ON DELETE CASCADE
                );
                CREATE INDEX [ix_sqld_quote_line] ON [{s}].[sales_quote_line_details]([quote_line_id]);
            END;
            """;
        await using (var cmd2 = connection.CreateCommand()) { cmd2.CommandText = createSql; await cmd2.ExecuteNonQueryAsync(cancellationToken); }
    }

    // ── ItemUnits (Items + Unit eslestirme + carpan) ──────────────────────────

    private async Task EnsureItemUnitsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            -- Legacy table rename: stock_unit_conversions -> ItemUnits (idempotent)
            IF OBJECT_ID(N'[{s}].[ItemUnits]', N'U') IS NULL
               AND OBJECT_ID(N'[{s}].[stock_unit_conversions]', N'U') IS NOT NULL
            BEGIN
                EXEC sp_rename N'[{s}].[stock_unit_conversions]', N'ItemUnits';
            END;

            IF OBJECT_ID(N'[{s}].[ItemUnits]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[ItemUnits]
                (
                    [Id]         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ItemId]     INT               NOT NULL,
                    [LineNo]     INT               NOT NULL,
                    [UnitId]     INT               NOT NULL,
                    [Multiplier] DECIMAL(18,6)     NOT NULL DEFAULT(1)
                );
                CREATE UNIQUE INDEX [ux_ItemUnits_ItemId_LineNo]
                    ON [{s}].[ItemUnits]([ItemId], [LineNo]);
            END;

            -- FK ItemUnits.ItemId -> Items.Id
            IF OBJECT_ID(N'[{s}].[ItemUnits]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{s}].[Items]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[ItemUnits]', N'ItemId') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE [name] = N'FK_ItemUnits_Items'
                     AND [parent_object_id] = OBJECT_ID(N'[{s}].[ItemUnits]'))
            BEGIN
                ALTER TABLE [{s}].[ItemUnits]
                WITH NOCHECK
                ADD CONSTRAINT [FK_ItemUnits_Items]
                    FOREIGN KEY ([ItemId]) REFERENCES [{s}].[Items]([Id]) ON DELETE CASCADE;
            END;

            -- FK ItemUnits.UnitId -> Unit.Id
            IF OBJECT_ID(N'[{s}].[ItemUnits]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{s}].[Unit]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[ItemUnits]', N'UnitId') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE [name] = N'FK_ItemUnits_Unit'
                     AND [parent_object_id] = OBJECT_ID(N'[{s}].[ItemUnits]'))
            BEGIN
                ALTER TABLE [{s}].[ItemUnits]
                WITH NOCHECK
                ADD CONSTRAINT [FK_ItemUnits_Unit]
                    FOREIGN KEY ([UnitId]) REFERENCES [{s}].[Unit]([Id]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Location Types (dinamik tip sozlugu) ──────────────────────────────────

    private async Task EnsureLocationTypesTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        // Seed sadece yeni tablo olusturuldugunda eklenir. Aksi halde silinen seed
        // satirlari her uygulama baslatmada geri gelir.
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[location_types]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[location_types]
                (
                    [id]         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [code]       NVARCHAR(50)  NOT NULL,
                    [name]       NVARCHAR(100) NOT NULL,
                    [sort_order] INT           NOT NULL DEFAULT(0),
                    [is_active]  BIT           NOT NULL DEFAULT(1)
                );
                CREATE UNIQUE INDEX [ux_location_types_code]
                    ON [{s}].[location_types]([code]);

                INSERT INTO [{s}].[location_types]([code],[name],[sort_order],[is_active]) VALUES (N'FACTORY', N'Fabrika', 10, 1);
                INSERT INTO [{s}].[location_types]([code],[name],[sort_order],[is_active]) VALUES (N'SECTION', N'Bolum', 20, 1);
                INSERT INTO [{s}].[location_types]([code],[name],[sort_order],[is_active]) VALUES (N'SHELF', N'Raf', 30, 1);
                INSERT INTO [{s}].[location_types]([code],[name],[sort_order],[is_active]) VALUES (N'BIN', N'Hucre', 40, 1);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Item Locations (malzeme - lokasyon cok-cogu) ──────────────────────────

    private async Task EnsureItemLocationsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[item_locations]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[item_locations]
                (
                    [id]          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [item_id]     INT NOT NULL,
                    [location_id] INT NOT NULL,
                    [is_default]  BIT NOT NULL DEFAULT(0),
                    [sort_order]  INT NOT NULL DEFAULT(0)
                );
                CREATE UNIQUE INDEX [ux_item_locations_item_location]
                    ON [{s}].[item_locations]([item_id], [location_id]);
                -- Her malzemenin en fazla bir varsayilan lokasyonu olabilir
                CREATE UNIQUE INDEX [ux_item_locations_item_default]
                    ON [{s}].[item_locations]([item_id])
                    WHERE [is_default] = 1;
                CREATE INDEX [ix_item_locations_location]
                    ON [{s}].[item_locations]([location_id]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Document Types & Report Templates ──────────────────────────────────────

    private async Task EnsureDocumentTypesTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[document_types]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[document_types]
                (
                    [id]                   INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_document_types] PRIMARY KEY,
                    [code]                 NVARCHAR(50)     NOT NULL,
                    [name]                 NVARCHAR(200)    NOT NULL,
                    [sql_view_name]        NVARCHAR(128)    NULL,
                    [required_key_column]  NVARCHAR(100)    NULL,
                    [description]          NVARCHAR(500)    NULL,
                    [is_active]            BIT              NOT NULL DEFAULT(1),
                    [Created]           DATETIME2(0)     NOT NULL,
                    [Updated]           DATETIME2(0)     NULL    
                );
                CREATE UNIQUE INDEX [ux_document_types_code] ON [{s}].[document_types]([code]);
            END;

            -- Migration: mevcut DB'lere required_key_column kolonunu ekle
            IF OBJECT_ID(N'[{s}].[document_types]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[document_types]', N'required_key_column') IS NULL
            BEGIN
                ALTER TABLE [{s}].[document_types] ADD [required_key_column] NVARCHAR(100) NULL;
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureReportTemplatesTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[report_templates]
                (
                    [id]               INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_report_templates] PRIMARY KEY,
                    [name]             NVARCHAR(200)    NOT NULL,
                    [document_type_id] INT              NOT NULL,
                    [frx_file_path]    NVARCHAR(500)    NULL,
                    [frx_content]      VARBINARY(MAX)   NULL,
                    [description]      NVARCHAR(500)    NULL,
                    [sql_view_name]    NVARCHAR(150)    NULL,
                    [key_column]       NVARCHAR(100)    NULL,
                    [is_default]       BIT              NOT NULL DEFAULT(0),
                    [is_active]        BIT              NOT NULL DEFAULT(1),
                    [Created]       DATETIME2(0)     NOT NULL,
                    [Updated]       DATETIME2(0)     NULL    ,
                    CONSTRAINT [fk_report_templates_document_type]
                        FOREIGN KEY ([document_type_id]) REFERENCES [{s}].[document_types]([id])
                );
                CREATE INDEX [ix_report_templates_document_type] ON [{s}].[report_templates]([document_type_id]);
            END;

            -- Eski DB'lere frx_content kolonunu ekle (idempotent)
            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_templates]', N'frx_content') IS NULL
            BEGIN
                ALTER TABLE [{s}].[report_templates] ADD [frx_content] VARBINARY(MAX) NULL;
            END;

            -- Per-template SQL view override (opsiyonel; bos ise document_types.sql_view_name kullanilir)
            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_templates]', N'sql_view_name') IS NULL
            BEGIN
                ALTER TABLE [{s}].[report_templates] ADD [sql_view_name] NVARCHAR(150) NULL;
            END;

            -- Per-template key column override (recordId ile eslesecek view kolonu)
            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_templates]', N'key_column') IS NULL
            BEGIN
                ALTER TABLE [{s}].[report_templates] ADD [key_column] NVARCHAR(100) NULL;
            END;

            -- Cikti secenekleri (Baski onizle / PDF kaydet / Mail gonder + mail varsayilanlari) JSON
            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_templates]', N'output_options_json') IS NULL
            BEGIN
                ALTER TABLE [{s}].[report_templates] ADD [output_options_json] NVARCHAR(MAX) NULL;
            END;

            -- Default siralama: kolon adi + yon (ASC|DESC). Generation sirasinda
            -- view sorgusuna ORDER BY [order_column] order_direction olarak eklenir.
            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_templates]', N'order_column') IS NULL
            BEGIN
                ALTER TABLE [{s}].[report_templates] ADD [order_column] NVARCHAR(150) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_templates]', N'order_direction') IS NULL
            BEGIN
                ALTER TABLE [{s}].[report_templates] ADD [order_direction] NVARCHAR(4) NULL;
            END;

            -- Jasper akisindan kalan kolonlari temizle (default constraint'i de oncesinde kaldir)
            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_templates]', N'engine') IS NOT NULL
            BEGIN
                DECLARE @cn_engine NVARCHAR(200);
                SELECT @cn_engine = dc.[name]
                FROM sys.default_constraints dc
                JOIN sys.columns c ON c.[object_id] = dc.[parent_object_id] AND c.[column_id] = dc.[parent_column_id]
                WHERE dc.[parent_object_id] = OBJECT_ID(N'[{s}].[report_templates]') AND c.[name] = N'engine';
                IF @cn_engine IS NOT NULL EXEC(N'ALTER TABLE [{s}].[report_templates] DROP CONSTRAINT [' + @cn_engine + N']');
                ALTER TABLE [{s}].[report_templates] DROP COLUMN [engine];
            END;

            IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_templates]', N'jrxml_content') IS NOT NULL
            BEGIN
                ALTER TABLE [{s}].[report_templates] DROP COLUMN [jrxml_content];
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Coklu SQL view destegi icin source tablosu. Bir sablonun birden fazla
    /// data source'u olabilir (primary + detail + sibling). Frx'te her source
    /// ayri bir node olarak gorunur: [Belge.X], [Kombinasyon.X], [Cari.X] vs.
    /// Bos tablo + NULL sources = eski tek-source akis (geriye uyumlu).
    /// </summary>
    private async Task EnsureReportTemplateSourcesTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[report_template_sources]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[report_template_sources]
                (
                    [id]                  INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_report_template_sources] PRIMARY KEY,
                    [template_id]         INT              NOT NULL,
                    [source_name]         NVARCHAR(100)    NOT NULL,
                    [view_name]           NVARCHAR(150)    NOT NULL,
                    [key_column]          NVARCHAR(100)    NOT NULL,
                    [parent_source_name]  NVARCHAR(100)    NULL,
                    [parent_key_column]   NVARCHAR(100)    NULL,
                    [is_primary]          BIT              NOT NULL DEFAULT(0),
                    [display_order]       INT              NOT NULL DEFAULT(0),
                    [Created]          DATETIME2(0)     NOT NULL,
                    CONSTRAINT [fk_rts_template]
                        FOREIGN KEY ([template_id]) REFERENCES [{s}].[report_templates]([id]) ON DELETE CASCADE
                );
                CREATE INDEX [ix_rts_template]
                    ON [{s}].[report_template_sources]([template_id]);
                CREATE UNIQUE INDEX [ux_rts_template_sourcename]
                    ON [{s}].[report_template_sources]([template_id], [source_name]);
                -- Her sablonda en fazla BIR primary source olmali
                CREATE UNIQUE INDEX [ux_rts_primary]
                    ON [{s}].[report_template_sources]([template_id])
                    WHERE [is_primary] = 1;
            END;

            -- Per-source siralama (data source modal'inda view secildikten
            -- sonra kullanici siralama kolonu + yon belirler)
            IF OBJECT_ID(N'[{s}].[report_template_sources]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_template_sources]', N'sort_column') IS NULL
            BEGIN
                ALTER TABLE [{s}].[report_template_sources] ADD [sort_column] NVARCHAR(150) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[report_template_sources]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[report_template_sources]', N'sort_direction') IS NULL
            BEGIN
                ALTER TABLE [{s}].[report_template_sources] ADD [sort_direction] NVARCHAR(4) NULL;
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Zamanlanmis gorevler tablosu + run history — Scheduler (Worker) bu tabloyu
    /// polling ile tarayip NextRunAt gelen gorevleri dispatch eder. TaskType,
    /// ParametersJson, ScheduleType, ScheduleExpression ile extensibility saglanir.
    /// </summary>
    private async Task EnsureScheduledTasksTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[scheduled_tasks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[scheduled_tasks]
                (
                    [id]                    INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_scheduled_tasks] PRIMARY KEY,
                    [name]                  NVARCHAR(200)  NOT NULL,
                    [description]           NVARCHAR(500)  NULL,
                    [task_type]             INT            NOT NULL DEFAULT(0),
                    [parameters_json]       NVARCHAR(MAX)  NULL,
                    [schedule_type]         INT            NOT NULL DEFAULT(0),
                    [schedule_expression]   NVARCHAR(200)  NULL,
                    [schedule_description]  NVARCHAR(200)  NULL,
                    [is_enabled]            BIT            NOT NULL DEFAULT(1),
                    [is_running]            BIT            NOT NULL DEFAULT(0),
                    [last_run_at]           DATETIME2(0)   NULL,
                    [last_run_status]       INT            NULL,
                    [last_run_message]      NVARCHAR(1000) NULL,
                    [last_run_duration_ms]  INT            NULL,
                    [next_run_at]           DATETIME2(0)   NULL,
                    [company_id]            INT            NULL,
                    [PrerequisiteTaskId]    INT            NULL,
                    [Created]               DATETIME2(0)   NOT NULL,
                    [Updated]               DATETIME2(0)   NULL
                );
                CREATE INDEX [ix_scheduled_tasks_due]
                    ON [{s}].[scheduled_tasks]([is_enabled],[next_run_at]);
            END;

            -- Code kolonu kaldirma migration'i (idempotent):
            -- Id zaten unique PK; kullanici-yuzu icin ayrica kod tutmaya gerek yok.
            IF EXISTS (SELECT 1 FROM sys.indexes
                       WHERE object_id = OBJECT_ID(N'[{s}].[scheduled_tasks]')
                         AND name = N'ux_scheduled_tasks_code')
                DROP INDEX [ux_scheduled_tasks_code] ON [{s}].[scheduled_tasks];
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'code') IS NOT NULL
                ALTER TABLE [{s}].[scheduled_tasks] DROP COLUMN [code];

            -- Migration: eski tabloya yeni kolonlari ekle (idempotent)
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'task_type') IS NULL
                ALTER TABLE [{s}].[scheduled_tasks] ADD [task_type] INT NOT NULL DEFAULT(0);
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'parameters_json') IS NULL
                ALTER TABLE [{s}].[scheduled_tasks] ADD [parameters_json] NVARCHAR(MAX) NULL;
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'schedule_type') IS NULL
                ALTER TABLE [{s}].[scheduled_tasks] ADD [schedule_type] INT NOT NULL DEFAULT(0);
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'schedule_expression') IS NULL
                ALTER TABLE [{s}].[scheduled_tasks] ADD [schedule_expression] NVARCHAR(200) NULL;
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'is_running') IS NULL
                ALTER TABLE [{s}].[scheduled_tasks] ADD [is_running] BIT NOT NULL DEFAULT(0);
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'last_run_duration_ms') IS NULL
                ALTER TABLE [{s}].[scheduled_tasks] ADD [last_run_duration_ms] INT NULL;
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'company_id') IS NULL
                ALTER TABLE [{s}].[scheduled_tasks] ADD [company_id] INT NULL;
            IF COL_LENGTH(N'[{s}].[scheduled_tasks]', N'PrerequisiteTaskId') IS NULL
                ALTER TABLE [{s}].[scheduled_tasks] ADD [PrerequisiteTaskId] INT NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[{s}].[scheduled_tasks]')
                  AND name = 'ix_scheduled_tasks_due')
                CREATE INDEX [ix_scheduled_tasks_due]
                    ON [{s}].[scheduled_tasks]([is_enabled],[next_run_at]);

            -- Run history tablosu
            IF OBJECT_ID(N'[{s}].[scheduled_task_runs]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[scheduled_task_runs]
                (
                    [id]           INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_scheduled_task_runs] PRIMARY KEY,
                    [task_id]      INT            NOT NULL,
                    [task_code]    NVARCHAR(60)   NOT NULL,
                    [started_at]   DATETIME2(0)   NOT NULL,
                    [completed_at] DATETIME2(0)   NULL,
                    [status]       INT            NOT NULL DEFAULT(2),
                    [message]      NVARCHAR(2000) NULL,
                    [duration_ms]  INT            NULL,
                    [trigger]      INT            NOT NULL DEFAULT(0),
                    [executed_command] NVARCHAR(MAX) NULL,
                    CONSTRAINT [fk_scheduled_task_runs_task]
                        FOREIGN KEY ([task_id]) REFERENCES [{s}].[scheduled_tasks]([id]) ON DELETE CASCADE
                );
                CREATE INDEX [ix_scheduled_task_runs_task]
                    ON [{s}].[scheduled_task_runs]([task_id], [started_at] DESC);
                CREATE INDEX [ix_scheduled_task_runs_code]
                    ON [{s}].[scheduled_task_runs]([task_code], [started_at] DESC);
            END;

            -- Migration: eski tabloya executed_command kolonunu ekle (idempotent)
            IF COL_LENGTH(N'[{s}].[scheduled_task_runs]', N'executed_command') IS NULL
                ALTER TABLE [{s}].[scheduled_task_runs] ADD [executed_command] NVARCHAR(MAX) NULL;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Document.document_type_id NULL olan tum satirlari 'satis_teklifi' belge turune
    /// baglar. Document tablosu ve document_types seed'inden sonra calismali — bu
    /// yuzden init akisinda SeedDocumentTypesAsync'ten hemen sonra cagirilir.
    /// <summary>
    /// Lisans yapilandirma tablosu — tek satir (id=1). Aktif lisans key + son dogrulama sonucu cache'lenir.
    /// </summary>
    private async Task EnsureLicenseConfigTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[license_config]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[license_config]
                (
                    [id]                INT           NOT NULL CONSTRAINT [pk_license_config] PRIMARY KEY,
                    [license_key]       NVARCHAR(MAX) NULL,
                    [secret_encrypted]  NVARCHAR(MAX) NULL,
                    [is_valid]          BIT           NOT NULL DEFAULT(0),
                    [expiry_date]       DATE          NULL,
                    [concurrent_limit]  INT           NULL,
                    [total_user_limit]  INT           NULL,
                    [last_error]        NVARCHAR(500) NULL,
                    [last_validated_at] DATETIME2(0)  NULL,
                    [Created]        DATETIME2(0)  NOT NULL,
                    [Updated]        DATETIME2(0)  NULL    
                );
            END;

            -- Migration: eski tabloya secret_encrypted kolonunu ekle (idempotent)
            IF COL_LENGTH(N'[{s}].[license_config]', N'secret_encrypted') IS NULL
                ALTER TABLE [{s}].[license_config] ADD [secret_encrypted] NVARCHAR(MAX) NULL;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// WhatsApp Cloud API yapilandirma tablosu — tek satir (id=1). Token DPAPI sifreli saklanir.
    /// Ayrica safety rules + send log tablolari.
    /// </summary>
    private async Task EnsureWhatsAppConfigTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var schemaLiteral = _schema.Replace("'", "''");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[whatsapp_config]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[whatsapp_config]
                (
                    [id]                       INT           NOT NULL CONSTRAINT [pk_whatsapp_config] PRIMARY KEY,
                    [provider]                 INT           NOT NULL DEFAULT(0),
                    [access_token_encrypted]   NVARCHAR(MAX) NULL,
                    [phone_number_id]          NVARCHAR(64)  NULL,
                    [business_account_id]      NVARCHAR(64)  NULL,
                    [display_phone_number]     NVARCHAR(32)  NULL,
                    [webhook_verify_token]     NVARCHAR(128) NULL,
                    [web_qr_bridge_url]        NVARCHAR(256) NULL,
                    [is_enabled]               BIT           NOT NULL DEFAULT(0),
                    [last_successful_send_at]  DATETIME2(0)  NULL,
                    [last_error]               NVARCHAR(500) NULL,
                    [Created]               DATETIME2(0)  NOT NULL,
                    [Updated]               DATETIME2(0)  NULL    
                );
            END;
            -- Migration: eski tabloya provider + web_qr_bridge_url kolonlari ekle
            IF COL_LENGTH(N'[{s}].[whatsapp_config]', N'provider') IS NULL
                ALTER TABLE [{s}].[whatsapp_config] ADD [provider] INT NOT NULL DEFAULT(0);
            IF COL_LENGTH(N'[{s}].[whatsapp_config]', N'web_qr_bridge_url') IS NULL
                ALTER TABLE [{s}].[whatsapp_config] ADD [web_qr_bridge_url] NVARCHAR(256) NULL;

            -- Safety rules — tek-row config
            IF OBJECT_ID(N'[{s}].[whatsapp_safety_rules]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[whatsapp_safety_rules]
                (
                    [id]                              INT          NOT NULL CONSTRAINT [pk_whatsapp_safety_rules] PRIMARY KEY,
                    [max_per_minute]                  INT          NOT NULL DEFAULT(5),
                    [max_per_hour]                    INT          NOT NULL DEFAULT(60),
                    [max_per_day]                     INT          NOT NULL DEFAULT(300),
                    [max_per_recipient_per_day]       INT          NOT NULL DEFAULT(3),
                    [min_delay_seconds]               INT          NOT NULL DEFAULT(3),
                    [max_delay_seconds]               INT          NOT NULL DEFAULT(15),
                    [respect_quiet_hours]             BIT          NOT NULL DEFAULT(1),
                    [quiet_hours_start_hour]          INT          NOT NULL DEFAULT(20),
                    [quiet_hours_end_hour]            INT          NOT NULL DEFAULT(8),
                    [max_consecutive_failures]        INT          NOT NULL DEFAULT(5),
                    [failure_cooldown_minutes]        INT          NOT NULL DEFAULT(30),
                    [warmup_days]                     INT          NOT NULL DEFAULT(7),
                    [warmup_max_per_day]              INT          NOT NULL DEFAULT(50),
                    [max_identical_messages_per_day]  INT          NOT NULL DEFAULT(10),
                    [Created]                      DATETIME2(0) NOT NULL,
                    [Updated]                      DATETIME2(0) NULL    
                );
                INSERT INTO [{s}].[whatsapp_safety_rules]
                    ([id],[Created],[Updated])
                VALUES (1, GETUTCDATE(), GETUTCDATE());
            END;

            -- Send log — her gönderim girişimini audit'e kaydeder
            IF OBJECT_ID(N'[{s}].[whatsapp_send_log]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[whatsapp_send_log]
                (
                    [id]            BIGINT        IDENTITY(1,1) NOT NULL CONSTRAINT [pk_whatsapp_send_log] PRIMARY KEY,
                    [sent_at]       DATETIME2(0)  NOT NULL,
                    [to_phone]      NVARCHAR(32)  NULL,
                    [message_hash]  NVARCHAR(64)  NULL,
                    [message_id]    NVARCHAR(128) NULL,
                    [success]       BIT           NOT NULL,
                    [error_message] NVARCHAR(500) NULL,
                    [block_reason]  NVARCHAR(300) NULL
                );
                CREATE INDEX [ix_whatsapp_send_log_sent_at] ON [{s}].[whatsapp_send_log]([sent_at] DESC);
                CREATE INDEX [ix_whatsapp_send_log_recipient_today] ON [{s}].[whatsapp_send_log]([to_phone],[sent_at]) WHERE [success]=1;
                CREATE INDEX [ix_whatsapp_send_log_hash_today] ON [{s}].[whatsapp_send_log]([message_hash],[sent_at]) WHERE [success]=1;
            END;

            -- WhatsApp inbox/giden — Bridge'ten poll'lanan birebir mesajlar (gruplar haric)
            IF OBJECT_ID(N'[{s}].[wa_inbox]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[wa_inbox]
                (
                    [id]             BIGINT        IDENTITY(1,1) NOT NULL CONSTRAINT [pk_wa_inbox] PRIMARY KEY,
                    [bridge_msg_id]  NVARCHAR(128) NULL,                  -- whatsapp-web.js msg.id._serialized; UNIQUE for dedup
                    [direction]      TINYINT       NOT NULL,              -- 0=incoming, 1=outgoing
                    [contact_phone]  NVARCHAR(32)  NOT NULL,              -- normalize: digits, no '+'
                    [contact_id]     INT           NULL,                  -- Contact.Id eslestirme (phone -> Contact.WaPhone/Mobile/Phone)
                    [contact_name]   NVARCHAR(150) NULL,                  -- WA pushname
                    [body]           NVARCHAR(MAX) NULL,
                    [media_type]     NVARCHAR(20)  NULL,                  -- chat|image|video|audio|document|sticker|location
                    [has_media]      BIT           NOT NULL DEFAULT 0,
                    [received_at]    DATETIME2(0)  NOT NULL,              -- Bridge'in verdiği timestamp
                    [Created]     DATETIME2(0)  NOT NULL,              -- DB'ye yazıldığı an
                    [read_at]        DATETIME2(0)  NULL,                  -- kullanıcı okuyunca set edilir (sohbet acılınca)
                    [media_path]     NVARCHAR(500) NULL,                  -- wwwroot/uploads/whatsapp/yyyy/MM/<id>.<ext>
                    [media_mime]     NVARCHAR(100) NULL,
                    [media_filename] NVARCHAR(200) NULL,
                    [media_size]     INT           NULL
                );
                CREATE UNIQUE INDEX [ux_wa_inbox_bridge_msg_id] ON [{s}].[wa_inbox]([bridge_msg_id]) WHERE [bridge_msg_id] IS NOT NULL;
                CREATE INDEX [ix_wa_inbox_phone_received] ON [{s}].[wa_inbox]([contact_phone],[received_at] DESC);
                CREATE INDEX [ix_wa_inbox_unread] ON [{s}].[wa_inbox]([contact_phone]) WHERE [direction]=0 AND [read_at] IS NULL;
            END;

            -- (Kaldirildi) WhatsApp 2024+ protokolu LID identifier'lari da kullaniyor (15+ basamak).
            -- LID'leri telefon-disi olarak silmek artik dogru degil; modern WA mesajlari LID ile geliyor
            -- ve resolve sirasinda gercek telefonu cikartilamayanlar dogal olarak LID ile kaydediliyor.
            -- Eski kayitlari el ile temizlemek istersen UI'dan sohbeti silebilirsin.

            -- Medya destek kolonlari (image/video/audio/document)
            IF OBJECT_ID(N'[{s}].[wa_inbox]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.wa_inbox', N'media_path') IS NULL
            BEGIN
                ALTER TABLE [{s}].[wa_inbox] ADD [media_path] NVARCHAR(500) NULL;
            END;
            IF OBJECT_ID(N'[{s}].[wa_inbox]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.wa_inbox', N'media_mime') IS NULL
            BEGIN
                ALTER TABLE [{s}].[wa_inbox] ADD [media_mime] NVARCHAR(100) NULL;
            END;
            IF OBJECT_ID(N'[{s}].[wa_inbox]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.wa_inbox', N'media_filename') IS NULL
            BEGIN
                ALTER TABLE [{s}].[wa_inbox] ADD [media_filename] NVARCHAR(200) NULL;
            END;
            IF OBJECT_ID(N'[{s}].[wa_inbox]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.wa_inbox', N'media_size') IS NULL
            BEGIN
                ALTER TABLE [{s}].[wa_inbox] ADD [media_size] INT NULL;
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Sistem Ayarlari (Gate) sifre tablosu — tek satir (id=1). PBKDF2 hash saklar.
    /// İlk acilista GatePasswordService.EnsureSeededAsync ile seed edilir.
    /// </summary>
    private async Task EnsureGateCredentialsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[gate_credentials]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[gate_credentials]
                (
                    [id]                   INT           NOT NULL CONSTRAINT [pk_gate_credentials] PRIMARY KEY,
                    [password_hash]        NVARCHAR(500) NOT NULL,
                    [last_changed_at]      DATETIME2(0)  NOT NULL,
                    [last_changed_from_ip] NVARCHAR(64)  NULL,
                    [Created]           DATETIME2(0)  NOT NULL
                );
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Document.document_type_id NULL olan tum satirlari 'satis_teklifi' belge turune baglar.
    /// Idempotent: zaten bagli olan satirlari etkilemez.
    /// </summary>
    private async Task BackfillDocumentTypeIdsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[Document]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{s}].[document_types]', N'U') IS NOT NULL
            BEGIN
                DECLARE @QuoteTypeId INT =
                    (SELECT TOP 1 [id] FROM [{s}].[document_types] WHERE [code] = N'satis_teklifi');
                IF @QuoteTypeId IS NOT NULL
                    UPDATE [{s}].[Document]
                       SET [DocumentTypeId] = @QuoteTypeId
                     WHERE [DocumentTypeId] IS NULL;
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // Her belge tipi icin: (Code, Name, SqlViewName, RequiredKeyColumn, Description)
    // RequiredKeyColumn: basim sirasinda URL'den gelen recordId'nin view'da eslesecegi kolon adi.
    //   Document context'li tipler (fatura/irsaliye/teklif) BelgeId bekler.
    //   Entity context'li tipler (urun/raf) Items.id veya Location.id gibi 'id' bekler.
    private static readonly (string Code, string Name, string? SqlViewName, string? RequiredKeyColumn, string? Description)[] DefaultDocumentTypes =
    [
        ("fatura",        "Fatura",        "vw_Invoice",        "BelgeId", "Satis faturasi sablonu"),
        ("irsaliye",      "Irsaliye",      "vw_DeliveryNote",   "BelgeId", "Sevk irsaliyesi sablonu"),
        ("urun_barkodu",  "Urun Barkodu",  "vw_ProductBarcode", "id",      "Urun barkod etiketi"),
        ("raf_etiketi",   "Raf Etiketi",   "vw_ShelfLabel",     "id",      "Depo raf etiketi"),
        ("satis_teklifi", "Satis Teklifi", "vw_ReportDocument", "BelgeId", "Satis teklifi sablonu"),
        ("satis_siparisi", "Satis Siparisi", "vw_ReportDocument", "BelgeId", "Satis siparisi sablonu (tekliften donusturuldu)"),
    ];

    private async Task SeedDocumentTypesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        // Migration: satis_teklifi eskiden vw_Document'a point ediyordu;
        // yeni vw_ReportDocument turkce semayi + widget kolonlarini tasiyor.
        await using (var migCmd = connection.CreateCommand())
        {
            migCmd.CommandText = $"""
                UPDATE [{s}].[document_types]
                   SET [sql_view_name] = 'vw_ReportDocument', [Updated] = GETDATE()
                 WHERE [code] = 'satis_teklifi' AND [sql_view_name] = 'vw_Document';
                """;
            await migCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Migration: mevcut DB'lerde required_key_column bos olanlari default'a set et.
        // Yeni kayitlar INSERT ile dogru deger alir; eski kayitlar burada heal edilir.
        foreach (var (code, _, _, reqKey, _) in DefaultDocumentTypes)
        {
            if (string.IsNullOrWhiteSpace(reqKey)) continue;
            await using var updCmd = connection.CreateCommand();
            updCmd.CommandText = $"""
                UPDATE [{s}].[document_types]
                   SET [required_key_column] = @ReqKey, [Updated] = GETDATE()
                 WHERE [code] = @Code AND ([required_key_column] IS NULL OR [required_key_column] = N'');
                """;
            updCmd.Parameters.AddWithValue("@Code",   code);
            updCmd.Parameters.AddWithValue("@ReqKey", reqKey);
            await updCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (code, name, viewName, reqKey, desc) in DefaultDocumentTypes)
        {
            var commandText = $"""
                IF NOT EXISTS (SELECT 1 FROM [{s}].[document_types] WHERE [code] = @Code)
                    INSERT INTO [{s}].[document_types] ([code],[name],[sql_view_name],[required_key_column],[description],[is_active],[Created],[Updated])
                    VALUES (@Code, @Name, @ViewName, @ReqKey, @Description, 1, GETDATE(), GETDATE());
                """;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = commandText;
            cmd.Parameters.AddWithValue("@Code",        code);
            cmd.Parameters.AddWithValue("@Name",        name);
            cmd.Parameters.AddWithValue("@ViewName",    (object?)viewName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ReqKey",      (object?)reqKey   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)desc     ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task EnsureReportDataViewsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        // vw_Invoice: satis teklifi verilerinden fatura gorunumu
        var views = new (string ViewName, string Body)[]
        {
            ("vw_Invoice", $"""
                SELECT q.[id], q.[DocumentNumber] AS [DocumentNumber],
                       q.[DocumentDate] AS [DocumentDate], ca.[AccountTitle] AS [CustomerName],
                       q.[ContactAddress] AS [CustomerAddress], q.[currency],
                       q.[SubTotal] AS [SubTotal], q.[DiscountRate] AS [DiscountRate],
                       q.[DiscountAmount] AS [DiscountAmount], q.[TaxRate] AS [TaxRate],
                       q.[TaxAmount] AS [TaxAmount], q.[GrandTotal] AS [GrandTotal],
                       q.[PaymentTerms] AS [PaymentTerms], q.[notes] AS [Notes],
                       ql.[line_no] AS [LineNo], mi.[Code] AS [MaterialCode],
                       mi.[Name] AS [MaterialName], mu.[UnitName] AS [UnitName],
                       ql.[quantity] AS [Quantity], ql.[unit_price] AS [UnitPrice],
                       ql.[discount_rate] AS [LineDiscountRate], ql.[line_total] AS [LineTotal]
                FROM [{s}].[Document] q
                LEFT JOIN [{s}].[Contact] ca ON ca.[Id] = q.[ContactId]
                LEFT JOIN [{s}].[DocumentLine] ql ON ql.[document_id] = q.[id]
                LEFT JOIN [{s}].[Items] mi ON mi.[Id] = ql.[item_id]
                LEFT JOIN [{s}].[Unit] mu ON mu.[Id] = ql.[unit_id]
                WHERE q.[IsActive] = 1
                """),
            ("vw_DeliveryNote", $"""
                SELECT q.[id], q.[DocumentNumber] AS [DocumentNumber],
                       q.[DocumentDate] AS [DocumentDate], ca.[AccountTitle] AS [CustomerName],
                       q.[ContactAddress] AS [CustomerAddress], q.[DeliveryAddress] AS [DeliveryAddress],
                       q.[DeliveryTerms] AS [DeliveryTerms],
                       ql.[line_no] AS [LineNo], mi.[Code] AS [MaterialCode],
                       mi.[Name] AS [MaterialName], mu.[UnitName] AS [UnitName],
                       ql.[quantity] AS [Quantity]
                FROM [{s}].[Document] q
                LEFT JOIN [{s}].[Contact] ca ON ca.[Id] = q.[ContactId]
                LEFT JOIN [{s}].[DocumentLine] ql ON ql.[document_id] = q.[id]
                LEFT JOIN [{s}].[Items] mi ON mi.[Id] = ql.[item_id]
                LEFT JOIN [{s}].[Unit] mu ON mu.[Id] = ql.[unit_id]
                WHERE q.[IsActive] = 1
                """),
            ("vw_ProductBarcode", $"""
                SELECT m.[Id] AS [id], m.[Code] AS [ProductCode],
                       m.[Name] AS [ProductName],
                       m.[Code] AS [BarcodeValue],
                       N'' AS [UnitName]
                FROM [{s}].[Items] m
                WHERE m.[IsActive] = 1
                """),
            ("vw_ShelfLabel", $"""
                SELECT m.[Id] AS [id], m.[Code] AS [ProductCode],
                       m.[Name] AS [ProductName],
                       m.[Code] AS [BarcodeValue],
                       N'' AS [UnitName]
                FROM [{s}].[Items] m
                WHERE m.[IsActive] = 1
                """),
            ("vw_Document", $"""
                SELECT q.[id], q.[DocumentNumber] AS [DocumentNumber],
                       q.[DocumentDate] AS [DocumentDate], q.[ValidUntil] AS [ValidUntil],
                       ca.[AccountTitle] AS [CustomerName], q.[ContactAddress] AS [CustomerAddress],
                       q.[currency], q.[SubTotal] AS [SubTotal],
                       q.[DiscountRate] AS [DiscountRate], q.[DiscountAmount] AS [DiscountAmount],
                       q.[TaxRate] AS [TaxRate], q.[TaxAmount] AS [TaxAmount],
                       q.[GrandTotal] AS [GrandTotal],
                       q.[PaymentTerms] AS [PaymentTerms], q.[DeliveryTerms] AS [DeliveryTerms],
                       q.[DeliveryAddress] AS [DeliveryAddress], q.[notes] AS [Notes],
                       q.[status] AS [Status], q.[RevisionNo] AS [RevisionNo],
                       ql.[line_no] AS [LineNo], mi.[Code] AS [MaterialCode],
                       mi.[Name] AS [MaterialName], mu.[UnitName] AS [UnitName],
                       ql.[quantity] AS [Quantity], ql.[unit_price] AS [UnitPrice],
                       ql.[discount_rate] AS [LineDiscountRate], ql.[line_total] AS [LineTotal]
                FROM [{s}].[Document] q
                LEFT JOIN [{s}].[Contact] ca ON ca.[Id] = q.[ContactId]
                LEFT JOIN [{s}].[DocumentLine] ql ON ql.[document_id] = q.[id]
                LEFT JOIN [{s}].[Items] mi ON mi.[Id] = ql.[item_id]
                LEFT JOIN [{s}].[Unit] mu ON mu.[Id] = ql.[unit_id]
                WHERE q.[IsActive] = 1
                """),
            ("vw_MaterialCards", $"""
                SELECT m.[Id] AS [Id], m.[Code] AS [MaterialCode], m.[Name] AS [MaterialName],
                       m.[IsActive] AS [IsActive],
                       m.[CreateDate] AS [CreatedDate], m.[ModifyDate] AS [ModifiedDate]
                FROM [{s}].[Items] m
                """),
        };

        foreach (var (viewName, body) in views)
        {
            try
            {
                var commandText = $"""
                    EXEC(N'CREATE OR ALTER VIEW [{s}].[{viewName}] AS {body.Replace("'", "''")}');
                    """;
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = commandText;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqlException ex)
            {
                Console.Error.WriteLine($"[DB INIT WARN] View '{viewName}' olusturulamadi: {ex.Message}");
            }
        }
    }

    private async Task EnsurePriceListTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s  = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");

        var sql = $"""
            -- Legacy column rename: PriceGroup (group_code -> Code, group_name -> Name, is_active -> IsActive)
            IF OBJECT_ID(N'[{s}].[PriceGroup]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'[{s}].[PriceGroup]', N'group_code') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceGroup]', N'Code') IS NULL
                    EXEC sp_rename N'[{s}].[PriceGroup].[group_code]', N'Code', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[PriceGroup]', N'group_name') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceGroup]', N'Name') IS NULL
                    EXEC sp_rename N'[{s}].[PriceGroup].[group_name]', N'Name', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[PriceGroup]', N'is_active') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceGroup]', N'IsActive') IS NULL
                    EXEC sp_rename N'[{s}].[PriceGroup].[is_active]', N'IsActive', N'COLUMN';
                -- created_at/updated_at rename: Initialize basinda generic rename ile yapiliyor

                -- CompanyId migration: tek-sirket DB'lerde mevcut satirlari company_id=1 ile backfill et
                IF COL_LENGTH(N'[{s}].[PriceGroup]', N'CompanyId') IS NULL
                BEGIN
                    ALTER TABLE [{s}].[PriceGroup] ADD [CompanyId] INT NOT NULL CONSTRAINT [df_price_groups_company_id] DEFAULT(1);

                    -- Eski tek-kolon UNIQUE index'i drop et (yeni composite ix yerine gelecek)
                    IF EXISTS (SELECT 1 FROM sys.indexes
                               WHERE object_id = OBJECT_ID(N'[{s}].[PriceGroup]')
                                 AND name = N'ux_price_groups_code')
                        DROP INDEX [ux_price_groups_code] ON [{s}].[PriceGroup];
                END;
            END;

            IF OBJECT_ID(N'[{s}].[PriceGroup]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[PriceGroup]
                (
                    [id]            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CompanyId]     INT NOT NULL,
                    [Code]          NVARCHAR(50)  NOT NULL,
                    [Name]          NVARCHAR(150) NOT NULL,
                    [description]   NVARCHAR(500) NULL,
                    [IsActive]      BIT NOT NULL CONSTRAINT [df_price_groups_is_active] DEFAULT(1),
                    [AllowsBuying]  BIT NOT NULL CONSTRAINT [df_price_groups_allows_buying]  DEFAULT(1),
                    [AllowsSelling] BIT NOT NULL CONSTRAINT [df_price_groups_allows_selling] DEFAULT(1),
                    [AllowsCost]    BIT NOT NULL CONSTRAINT [df_price_groups_allows_cost]    DEFAULT(1),
                    [Created]       DATETIME2 NOT NULL,
                    [Updated]       DATETIME2 NULL
                );
            END;

            -- Composite UNIQUE: ayni sirket icinde Code unique, farkli sirketler ayni kodu kullanabilir
            IF OBJECT_ID(N'[{s}].[PriceGroup]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.indexes
                               WHERE object_id = OBJECT_ID(N'[{s}].[PriceGroup]')
                                 AND name = N'ux_price_groups_company_code')
            BEGIN
                CREATE UNIQUE INDEX [ux_price_groups_company_code] ON [{s}].[PriceGroup]([CompanyId],[Code]);
            END;

            -- AllowsBuying/Selling/Cost kolonlari: idempotent ALTER ADD (mevcut DB'lerde
            -- default 1 ile gelir → backward compatible: tum eski gruplar tum tipleri kabul eder).
            IF OBJECT_ID(N'[{s}].[PriceGroup]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceGroup]', N'AllowsBuying') IS NULL
                ALTER TABLE [{s}].[PriceGroup] ADD [AllowsBuying]  BIT NOT NULL CONSTRAINT [df_price_groups_allows_buying]  DEFAULT(1);
            IF OBJECT_ID(N'[{s}].[PriceGroup]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceGroup]', N'AllowsSelling') IS NULL
                ALTER TABLE [{s}].[PriceGroup] ADD [AllowsSelling] BIT NOT NULL CONSTRAINT [df_price_groups_allows_selling] DEFAULT(1);
            IF OBJECT_ID(N'[{s}].[PriceGroup]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceGroup]', N'AllowsCost') IS NULL
                ALTER TABLE [{s}].[PriceGroup] ADD [AllowsCost]    BIT NOT NULL CONSTRAINT [df_price_groups_allows_cost]    DEFAULT(1);

            -- ── PriceList migration: legacy denormalized columns → FK-based schema ──
            -- Eski schema: price_group_id, item_id (NULL), material_code, material_name, combination_code, combination_name, currency
            -- Yeni schema: GroupId, ItemId (NOT NULL), ConfigId (FK ItemConfiguration), CurrencyId (FK currencies)
            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
            BEGIN
                -- 1) Eski lookup index'lerini drop et (drop edilecek kolonlara referans veriyorlar)
                IF EXISTS (SELECT 1 FROM sys.indexes
                           WHERE object_id = OBJECT_ID(N'[{s}].[PriceList]')
                             AND name = N'ix_price_list_entries_group_mat')
                    DROP INDEX [ix_price_list_entries_group_mat] ON [{s}].[PriceList];

                IF EXISTS (SELECT 1 FROM sys.indexes
                           WHERE object_id = OBJECT_ID(N'[{s}].[PriceList]')
                             AND name = N'ix_price_list_entries_lookup')
                    DROP INDEX [ix_price_list_entries_lookup] ON [{s}].[PriceList];

                -- 2) Yeni FK kolonlari ekle (nullable initially — backfill icin)
                IF COL_LENGTH(N'[{s}].[PriceList]', N'CurrencyId') IS NULL
                    ALTER TABLE [{s}].[PriceList] ADD [CurrencyId] INT NULL;

                IF COL_LENGTH(N'[{s}].[PriceList]', N'ConfigId') IS NULL
                    ALTER TABLE [{s}].[PriceList] ADD [ConfigId] INT NULL;

                -- 3) Backfill: material_code → item_id (eski item_id NULL ise Items.code lookup)
                -- NOT: Kolonlar bu migration tarafindan drop edilebilir; parse-time
                -- resolution problemini onlemek icin UPDATE'i EXEC ile sarmalayalim.
                IF COL_LENGTH(N'[{s}].[PriceList]', N'material_code') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceList]', N'item_id') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[Items]', N'U') IS NOT NULL
                BEGIN
                    EXEC('
                        UPDATE p SET p.[item_id] = i.[id]
                        FROM [{s}].[PriceList] p
                        INNER JOIN [{s}].[Items] i ON i.[code] = p.[material_code]
                        WHERE p.[item_id] IS NULL
                    ');
                END;

                -- 4) Backfill: currency (string) → CurrencyId (currencies.id JOIN code)
                -- NOT: CurrencyId yukarida ayni batch'te ALTER ADD ile eklendi.
                -- SQL Server deferred name resolution parse-time'da kolonu goremez,
                -- bu yuzden UPDATE'i EXEC ile sarmalayip runtime'a erteliyoruz.
                IF COL_LENGTH(N'[{s}].[PriceList]', N'currency') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[currencies]', N'U') IS NOT NULL
                BEGIN
                    EXEC('
                        UPDATE p SET p.[CurrencyId] = c.[id]
                        FROM [{s}].[PriceList] p
                        INNER JOIN [{s}].[currencies] c ON c.[code] = p.[currency]
                        WHERE p.[CurrencyId] IS NULL
                    ');
                END;

                -- 5) Backfill: combination_code → ConfigId (ItemConfiguration.Id JOIN RecordCode)
                IF COL_LENGTH(N'[{s}].[PriceList]', N'combination_code') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[ItemConfiguration]', N'U') IS NOT NULL
                BEGIN
                    EXEC('
                        UPDATE p SET p.[ConfigId] = cfg.[Id]
                        FROM [{s}].[PriceList] p
                        INNER JOIN [{s}].[ItemConfiguration] cfg ON cfg.[RecordCode] = p.[combination_code]
                        WHERE p.[ConfigId] IS NULL AND p.[combination_code] IS NOT NULL
                    ');
                END;

                -- 6) Rename kalan kolonlar
                IF COL_LENGTH(N'[{s}].[PriceList]', N'price_group_id') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceList]', N'GroupId') IS NULL
                    EXEC sp_rename N'[{s}].[PriceList].[price_group_id]', N'GroupId', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[PriceList]', N'item_id') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceList]', N'ItemId') IS NULL
                    EXEC sp_rename N'[{s}].[PriceList].[item_id]', N'ItemId', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[PriceList]', N'is_active') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceList]', N'IsActive') IS NULL
                    EXEC sp_rename N'[{s}].[PriceList].[is_active]', N'IsActive', N'COLUMN';

                -- 7) Eski denormalized kolonlari drop et
                IF COL_LENGTH(N'[{s}].[PriceList]', N'material_name') IS NOT NULL
                    ALTER TABLE [{s}].[PriceList] DROP COLUMN [material_name];

                IF COL_LENGTH(N'[{s}].[PriceList]', N'combination_name') IS NOT NULL
                    ALTER TABLE [{s}].[PriceList] DROP COLUMN [combination_name];

                IF COL_LENGTH(N'[{s}].[PriceList]', N'material_code') IS NOT NULL
                    ALTER TABLE [{s}].[PriceList] DROP COLUMN [material_code];

                IF COL_LENGTH(N'[{s}].[PriceList]', N'combination_code') IS NOT NULL
                    ALTER TABLE [{s}].[PriceList] DROP COLUMN [combination_code];

                -- currency: drop edebilmek icin once default constraint kaldirilmali
                IF COL_LENGTH(N'[{s}].[PriceList]', N'currency') IS NOT NULL
                BEGIN
                    DECLARE @df_currency NVARCHAR(200);
                    SELECT @df_currency = dc.[name]
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
                    WHERE c.[object_id] = OBJECT_ID(N'[{s}].[PriceList]')
                      AND c.[name] = N'currency';
                    IF @df_currency IS NOT NULL
                        EXEC('ALTER TABLE [{s}].[PriceList] DROP CONSTRAINT [' + @df_currency + ']');
                    ALTER TABLE [{s}].[PriceList] DROP COLUMN [currency];
                END;

                -- 8) valid_from → ValidFrom, valid_to → ValidTo rename
                IF COL_LENGTH(N'[{s}].[PriceList]', N'valid_from') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceList]', N'ValidFrom') IS NULL
                    EXEC sp_rename N'[{s}].[PriceList].[valid_from]', N'ValidFrom', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[PriceList]', N'valid_to') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[PriceList]', N'ValidTo') IS NULL
                    EXEC sp_rename N'[{s}].[PriceList].[valid_to]', N'ValidTo', N'COLUMN';

                -- 8b) created_at/updated_at rename: Initialize basinda generic rename ile yapiliyor

                -- 9) PriceType ve Price kolonlarini ekle (nullable, backfill icin)
                IF COL_LENGTH(N'[{s}].[PriceList]', N'PriceType') IS NULL
                    ALTER TABLE [{s}].[PriceList] ADD [PriceType] NVARCHAR(10) NULL;
                IF COL_LENGTH(N'[{s}].[PriceList]', N'Price') IS NULL
                    ALTER TABLE [{s}].[PriceList] ADD [Price] DECIMAL(18,4) NULL;
            END;

            -- 10) Backfill: mevcut satirlari 's' (selling) olarak isaretle (Price=selling_price)
            -- EXEC ile sarmaliyoruz cunku PriceType/Price ayni batch icinde ALTER ADD'lendi.
            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'selling_price') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'PriceType') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'Price') IS NOT NULL
            BEGIN
                EXEC('
                    UPDATE [{s}].[PriceList]
                    SET [PriceType] = N''s'', [Price] = [selling_price]
                    WHERE [PriceType] IS NULL OR [Price] IS NULL
                ');
            END;

            -- 10b) Legacy data conversion: 'Buying'/'Selling' string'lerini 'b'/'s' kisaltmasiyla degistir
            -- (Onceki migration sirasinda PascalCase ile yazilmis kayitlar varsa)
            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'PriceType') IS NOT NULL
            BEGIN
                UPDATE [{s}].[PriceList] SET [PriceType] = N'b' WHERE [PriceType] = N'Buying';
                UPDATE [{s}].[PriceList] SET [PriceType] = N's' WHERE [PriceType] = N'Selling';
                -- Tek harf de buyuk yazilmis olabilir ('B','S','M') — hepsini lowercase'e indir.
                -- COLLATE BIN ile karsilastirma yapiyoruz cunku default CI collation'da
                -- 'B' = LOWER('B') TRUE doner; sadece BIN'de gercek buyuk/kucuk farki anlasilir.
                -- (idempotent; uygulama hep lowercase saklar, eski insert'lerden kalanlar duzelir.)
                UPDATE [{s}].[PriceList] SET [PriceType] = LOWER([PriceType])
                    WHERE [PriceType] COLLATE Latin1_General_BIN
                        <> LOWER([PriceType]) COLLATE Latin1_General_BIN;
            END;

            -- 11) Her 's' (selling) satiri icin paralel 'b' (buying) satiri ekle (Price=buying_price)
            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'buying_price') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'PriceType') IS NOT NULL
            BEGIN
                EXEC('
                    INSERT INTO [{s}].[PriceList]
                        ([GroupId],[ItemId],[ConfigId],[CurrencyId],[PriceType],[Price],
                         [ValidFrom],[ValidTo],[IsActive],[Created],[Updated])
                    SELECT [GroupId],[ItemId],[ConfigId],[CurrencyId],N''b'',[buying_price],
                           [ValidFrom],[ValidTo],[IsActive],GETDATE(),GETDATE()
                    FROM [{s}].[PriceList]
                    WHERE [PriceType] = N''s''
                      AND NOT EXISTS (
                          SELECT 1 FROM [{s}].[PriceList] b
                          WHERE b.[GroupId] = [{s}].[PriceList].[GroupId]
                            AND b.[ItemId] = [{s}].[PriceList].[ItemId]
                            AND ISNULL(b.[ConfigId], -1) = ISNULL([{s}].[PriceList].[ConfigId], -1)
                            AND b.[CurrencyId] = [{s}].[PriceList].[CurrencyId]
                            AND b.[ValidFrom] = [{s}].[PriceList].[ValidFrom]
                            AND b.[PriceType] = N''b''
                      )
                ');
            END;

            -- 12) buying_price ve selling_price drop (default constraint dahil)
            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'buying_price') IS NOT NULL
            BEGIN
                DECLARE @df_buy NVARCHAR(200);
                SELECT @df_buy = dc.[name]
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
                WHERE c.[object_id] = OBJECT_ID(N'[{s}].[PriceList]')
                  AND c.[name] = N'buying_price';
                IF @df_buy IS NOT NULL
                    EXEC('ALTER TABLE [{s}].[PriceList] DROP CONSTRAINT [' + @df_buy + ']');
                ALTER TABLE [{s}].[PriceList] DROP COLUMN [buying_price];
            END;

            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'selling_price') IS NOT NULL
            BEGIN
                DECLARE @df_sell NVARCHAR(200);
                SELECT @df_sell = dc.[name]
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
                WHERE c.[object_id] = OBJECT_ID(N'[{s}].[PriceList]')
                  AND c.[name] = N'selling_price';
                IF @df_sell IS NOT NULL
                    EXEC('ALTER TABLE [{s}].[PriceList] DROP CONSTRAINT [' + @df_sell + ']');
                ALTER TABLE [{s}].[PriceList] DROP COLUMN [selling_price];
            END;

            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[PriceList]
                (
                    [id]          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [GroupId]     INT NOT NULL,
                    [ItemId]      INT NOT NULL,
                    [ConfigId]    INT NULL,
                    [CurrencyId]  INT NOT NULL,
                    [PriceType]   NVARCHAR(10)  NOT NULL,
                    [Price]       DECIMAL(18,4) NOT NULL CONSTRAINT [df_pricelist_price] DEFAULT(0),
                    [ValidFrom]   DATE NOT NULL,
                    [ValidTo]     DATE NULL,
                    [IsActive]    BIT NOT NULL CONSTRAINT [df_pricelist_is_active] DEFAULT(1),
                    [Created]     DATETIME2 NOT NULL,
                    [Updated]     DATETIME2 NULL    ,
                    CONSTRAINT [fk_pricelist_pricegroup]
                        FOREIGN KEY ([GroupId]) REFERENCES [{s}].[PriceGroup]([id])
                );
            END;

            -- Lookup index (yeni schema'ya gore: PriceType ekli)
            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.indexes
                               WHERE object_id = OBJECT_ID(N'[{s}].[PriceList]')
                                 AND name = N'ix_pricelist_lookup')
            BEGIN
                CREATE INDEX [ix_pricelist_lookup]
                    ON [{s}].[PriceList]([GroupId],[ItemId],[ConfigId],[PriceType],[ValidFrom]);
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureFormsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        // ── Batch 1: DDL (CREATE + ALTER) ───────────────────────────────
        // DDL ve DML'i AYNI batch icinde yapamayiz! SQL Server deferred name
        // resolution parse-time'da kolonlari kontrol eder; BaseTable ALTER ile
        // eklendigi halde sonraki UPDATE'te referansi parser'i patlatir.
        // Cozum: Iki ayri ExecuteNonQueryAsync cagrisi.
        var ddlSql = """
            IF OBJECT_ID(N'dbo.Forms', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Forms
                (
                    [Id]             INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [FormCode]       NVARCHAR(50)   NOT NULL,
                    [FormName]       NVARCHAR(200)  NOT NULL,
                    [Module]         NVARCHAR(50)   NOT NULL,
                    [SubModule]      NVARCHAR(50)   NULL,
                    [SortOrder]      INT            NOT NULL DEFAULT(0),
                    [IsActive]       BIT            NOT NULL DEFAULT(1),
                    [BaseTable]      NVARCHAR(120)  NULL,
                    [BaseRecordKey]  NVARCHAR(60)   NULL
                );
                CREATE UNIQUE INDEX [ux_forms_code] ON dbo.Forms([FormCode]);
            END;

            -- Faz H: Flattened View altyapisi — BaseTable + BaseRecordKey kolonlari
            -- Idempotent: kolonlar yoksa eklenir. Bu kolonlar, WidgetTra'nin pivot
            -- edildigi dinamik view'un (v_Flat_<FormCode>) base tablosuna
            -- hangi SQL tablosunun bagli oldugunu tanimlar.
            IF OBJECT_ID(N'dbo.Forms', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Forms', N'BaseTable') IS NULL
            BEGIN
                ALTER TABLE dbo.Forms ADD [BaseTable] NVARCHAR(120) NULL;
            END;
            IF OBJECT_ID(N'dbo.Forms', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.Forms', N'BaseRecordKey') IS NULL
            BEGIN
                ALTER TABLE dbo.Forms ADD [BaseRecordKey] NVARCHAR(60) NULL;
            END;
            """;
        await using (var ddlCmd = connection.CreateCommand())
        {
            ddlCmd.CommandText = ddlSql;
            await ddlCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // ── Batch 2: Seed UPDATE'leri ───────────────────────────────────
        // Ayri batch'te calisir — parser artik BaseTable kolonunu gorur.
        // EXEC sp_executesql ile de cozulebilir ama iki ayri komut daha temiz.
        var seedSql = """
            -- Seed + migration: mevcut 3 core form icin base table mapping.
            -- Idempotent: hem bos (seed) hem eski tablo isimleri (migration) kapsamli.
            UPDATE dbo.Forms
               SET [BaseTable] = N'dbo.Contact',
                   [BaseRecordKey] = N'AccountCode'
             WHERE [FormCode] = N'CONTACTS'
               AND ([BaseTable] IS NULL OR [BaseTable] = N''
                    OR [BaseTable] IN (N'dbo.ContactAccounts', N'dbo.Contacts'));

            UPDATE dbo.Forms
               SET [BaseTable] = N'dbo.Items',
                   [BaseRecordKey] = N'code'
             WHERE [FormCode] = N'ITEMS'
               AND ([BaseTable] IS NULL OR [BaseTable] = N''
                    OR [BaseTable] IN (N'dbo.Item', N'dbo.stock_cards')
                    OR [BaseRecordKey] = N'material_code');

            UPDATE dbo.Forms
               SET [BaseTable] = N'dbo.Document',
                   [BaseRecordKey] = N'document_number'
             WHERE [FormCode] = N'SALES_QUOTE_EDIT'
               AND ([BaseTable] IS NULL OR [BaseTable] = N''
                    OR [BaseTable] IN (N'dbo.sales_quotes', N'dbo.Documents'));

            UPDATE dbo.Forms
               SET [BaseTable] = N'dbo.DocumentLine',
                   [BaseRecordKey] = N'id'
             WHERE [FormCode] = N'SALES_QUOTE_LINES'
               AND ([BaseTable] IS NULL OR [BaseTable] = N''
                    OR [BaseTable] IN (N'dbo.sales_quote_lines', N'dbo.DocumentLines'));
            """;
        await using (var seedCmd = connection.CreateCommand())
        {
            seedCmd.CommandText = seedSql;
            await seedCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task SeedFormsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        // Module  = sol menü ana grubu   (Lojistik, Finans, Satış ...)
        // SubModule = sol menü maddesi  (Malzeme Kartları, Cari Hesaplar ...)
        //             null ise form doğrudan modül altında görünür
        // Name    = sayfa tipi          (Liste, Düzenleme, Yeni — veya tek sayfalı formun adı)
        var forms = new (string Code, string Name, string Module, string? SubModule, int Sort)[]
        {
            // ── Genel ────────────────────────────────────────────────────────
            ("NOTES",               "Notlar",                           "Genel",                null,                       100),

            // ── Onay İşlemleri ───────────────────────────────────────────────
            ("EINVOICE",            "e-Fatura",                         "Onay İşlemleri",       "Elektronik Belgeler",      200),
            ("EARCHIVE",            "e-Arşiv",                          "Onay İşlemleri",       "Elektronik Belgeler",      210),
            ("EDISPATCH",           "e-İrsaliye",                       "Onay İşlemleri",       "Elektronik Belgeler",      220),

            // ── Lojistik ─────────────────────────────────────────────────────
            ("ITEMS",               "Liste",                            "Lojistik",             "Malzeme Kartları",         300),
            ("MATERIAL_CARD_EDIT",  "Düzenleme",                        "Lojistik",             "Malzeme Kartları",         305),
            ("PRODUCT_CONFIG",      "Özellik ve Kombinasyon",           "Lojistik",             "Ürün Konfigürasyonu",      310),
            ("PRODUCT_FEATURE_EDIT","Özellik Düzenleme",                "Lojistik",             "Ürün Konfigürasyonu",      315),
            ("PRODUCT_COMBINATIONS","Kombinasyon Üretici",              "Lojistik",             "Ürün Konfigürasyonu",      320),

            // ── Satış ─────────────────────────────────────────────────────────
            ("SALES_QUOTE",         "Liste",                            "Satış",                "Satış Teklifi",            400),
            ("SALES_QUOTE_NEW",     "Yeni",                             "Satış",                "Satış Teklifi",            405),
            ("SALES_QUOTE_EDIT",    "Üst Bilgi",                        "Satış",                "Satış Teklifi",            410),
            ("SALES_QUOTE_LINES",   "Kalem Bilgisi",                    "Satış",                "Satış Teklifi",            415),
            ("SALES_ORDER",         "Liste",                            "Satış",                "Satış Siparişi",           420),
            ("SALES_ORDER_NEW",     "Yeni",                             "Satış",                "Satış Siparişi",           425),
            ("SALES_ORDER_EDIT",    "Üst Bilgi",                        "Satış",                "Satış Siparişi",           430),
            ("SALES_ORDER_LINES",   "Kalem Bilgisi",                    "Satış",                "Satış Siparişi",           435),

            // ── Üretim ───────────────────────────────────────────────────────
            ("PRODUCT_TREES",       "Ürün Ağacı",                       "Üretim",               null,                       500),
            ("WORK_ORDERS",         "Liste",                            "Üretim",               "İş Emirleri",              510),
            ("WORK_ORDER_EDIT",     "Düzenleme",                        "Üretim",               "İş Emirleri",              515),
            ("OPERATIONS",          "Liste",                            "Üretim",               "Operasyonlar",             520),
            ("OPERATION_EDIT",      "Düzenleme",                        "Üretim",               "Operasyonlar",             525),
            ("ROUTINGS",            "Liste",                            "Üretim",               "Rotalar",                  530),
            ("ROUTING_EDIT",        "Düzenleme",                        "Üretim",               "Rotalar",                  535),
            ("PERSONNEL",           "Liste",                            "Üretim",               "Personel",                 540),
            ("PERSONNEL_EDIT",      "Düzenleme",                        "Üretim",               "Personel",                 545),

            // ── Finans ───────────────────────────────────────────────────────
            ("CONTACTS",            "Liste",                            "Finans",               "Cari Hesaplar",            600),
            ("CONTACT_EDIT",        "Düzenleme",                        "Finans",               "Cari Hesaplar",            605),

            // ── Genel Tanımlamalar ────────────────────────────────────────────
            ("SALES_REPS",          "Satış Temsilcileri",               "Genel Tanımlamalar",   null,                       700),
            ("CURRENCIES",          "Döviz Tanımlamaları",              "Genel Tanımlamalar",   null,                       710),
            ("LOCATIONS",           "Lokasyon Tanımlamaları",           "Genel Tanımlamalar",   null,                       720),
            ("MEASURE_UNITS",       "Ölçü Birimi Tanımlama",            "Genel Tanımlamalar",   null,                       730),
            ("MATERIAL_GROUPS",     "Grup Tanımlamaları",               "Genel Tanımlamalar",   null,                       740),
            ("MACHINES",            "Makine Tanımlamaları",             "Genel Tanımlamalar",   null,                       750),
            ("MACHINE_TYPES",       "Makine Tipleri",                   "Genel Tanımlamalar",   null,                       755),
            ("PRICE_LIST",          "Liste",                            "Genel Tanımlamalar",   "Fiyat Listesi",            750),
            ("PRICE_GROUPS",        "Gruplar",                          "Genel Tanımlamalar",   "Fiyat Listesi",            755),
            ("CARD_GROUPS",         "Kart Grupları",                    "Genel Tanımlamalar",   null,                       760),

            // ── Tasarım ──────────────────────────────────────────────────────
            ("DOC_TEMPLATES",       "Belge Şablonları",                 "Tasarım",              null,                       800),

            // ── Ayarlar ──────────────────────────────────────────────────────
            ("COMPANY_SETTINGS",    "Şirket Tanımlama",                 "Ayarlar",              null,                       900),
            ("INTEGRATOR_SETTINGS", "Entegratör Ayarları",              "Ayarlar",              null,                       910),
            ("MAIL_SETTINGS",       "Mail Ayarları",                    "Ayarlar",              null,                       920),
            ("ERP_SETTINGS",        "ERP Bağlantı Ayarları",            "Ayarlar",              null,                       930),
            ("INTEGRATION_EVENTS",  "Entegrasyon Tanımları",            "Ayarlar",              null,                       940),
            ("VIEW_SETTINGS",       "Alan ve Widget Tanımlamaları",     "Ayarlar",              null,                       950),
            ("SETUP_DEFINITIONS",   "Şirket ve Kullanıcı Tanımlamaları","Ayarlar",              null,                       960),
        };

        foreach (var (code, name, module, subModule, sort) in forms)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                MERGE dbo.Forms WITH (HOLDLOCK) AS target
                USING (SELECT @Code AS FormCode) AS source ON target.[FormCode] = source.FormCode
                WHEN MATCHED THEN
                    UPDATE SET [FormName] = @Name, [Module] = @Module, [SubModule] = @SubModule, [SortOrder] = @Sort
                WHEN NOT MATCHED THEN
                    INSERT ([FormCode],[FormName],[Module],[SubModule],[SortOrder])
                    VALUES (@Code, @Name, @Module, @SubModule, @Sort);
                """;
            cmd.Parameters.Add(new SqlParameter("@Code", code));
            cmd.Parameters.Add(new SqlParameter("@Name", name));
            cmd.Parameters.Add(new SqlParameter("@Module", module));
            cmd.Parameters.Add(new SqlParameter("@SubModule", (object?)subModule ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Sort", sort));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// EAV widget sistemi — yeni tablolar:
    ///   - WidgetMas: widget tanimlari (master). FormId → dbo.Forms.Id FK.
    ///     ParentId → self-FK (grup hiyerarsisi icin, DataType='group' satirlar root).
    ///   - WidgetTra: widget degerleri (transaction). WidgetId → WidgetMas.Id FK.
    ///     Value tek nvarchar(max) — "her sey metindir" kurali.
    ///
    /// Eski material_card_field_groups / settings / options / dynamic_field_values
    /// tablolari dokunulmaz; bu yeni tablolar paralel calisir.
    /// Idempotent: OBJECT_ID guard ile tekrar calismaya dayanikli.
    /// </summary>
    private async Task EnsureWidgetEavTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        // ── Batch 1: CREATE TABLE + ALTER TABLE (kolon ekleme) ──────────
        // CREATE INDEX ParentRecordId'yi referans eden index Batch 2'de.
        // Sebep: SQL Server parser DDL batch'inde bile kolon referanslarini
        // parse-time'da dogrular. ALTER ile eklenecek kolon ayni batch'te CREATE
        // INDEX tarafindan referans edilirse → Invalid column name error.
        var ddlSql = $"""
            -- WidgetMas (widget tanimlari)
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[WidgetMas]
                (
                    [Id]           INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_WidgetMas] PRIMARY KEY,
                    [CompanyId]    INT NOT NULL CONSTRAINT [df_WidgetMas_Company] DEFAULT(0),
                    [FormId]       INT            NOT NULL,
                    [ParentId]     INT            NULL,
                    [WidgetCode]   NVARCHAR(100)  NOT NULL,
                    [Label]        NVARCHAR(255)  NOT NULL,
                    [DataType]     NVARCHAR(30)   NOT NULL,
                    [MaxLength]      INT            NULL,
                    [MinLength]      INT            NULL,
                    [ExpectedLength] INT            NULL,
                    [MinValue]       DECIMAL(18,4)  NULL,
                    [MaxValue]       DECIMAL(18,4)  NULL,
                    [SortOrder]    INT            NOT NULL CONSTRAINT [df_WidgetMas_Sort] DEFAULT(0),
                    [OptionsJSON]  NVARCHAR(MAX)  NULL,
                    [RulesJSON]    NVARCHAR(MAX)  NULL,
                    [IsPlainField] BIT            NOT NULL CONSTRAINT [df_WidgetMas_Plain]    DEFAULT(0),
                    [IsRequired]   BIT            NOT NULL CONSTRAINT [df_WidgetMas_Req]     DEFAULT(0),
                    [IsListable]   BIT            NOT NULL CONSTRAINT [df_WidgetMas_Listable] DEFAULT(1),
                    [IsActive]     BIT            NOT NULL CONSTRAINT [df_WidgetMas_Active] DEFAULT(1),
                    [ColorType]    INT            NOT NULL CONSTRAINT [df_WidgetMas_ColorType] DEFAULT(0),
                    [ColorValue]   NVARCHAR(100)  NULL,
                    [ColSpan]      INT            NOT NULL CONSTRAINT [df_WidgetMas_ColSpan] DEFAULT(12),
                    [LabelStyle]   NVARCHAR(20)   NOT NULL CONSTRAINT [df_WidgetMas_LabelStyle] DEFAULT('standard'),
                    [CreatedAt]    DATETIME2(0)   NOT NULL CONSTRAINT [df_WidgetMas_Created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt]    DATETIME2(0)   NOT NULL CONSTRAINT [df_WidgetMas_Updated] DEFAULT(SYSUTCDATETIME()),
                    CONSTRAINT [fk_WidgetMas_Form]
                        FOREIGN KEY ([FormId]) REFERENCES [{s}].[Forms]([Id]),
                    CONSTRAINT [fk_WidgetMas_Parent]
                        FOREIGN KEY ([ParentId]) REFERENCES [{s}].[WidgetMas]([Id])
                );
                CREATE UNIQUE INDEX [ux_WidgetMas_FormCode]
                    ON [{s}].[WidgetMas]([CompanyId], [FormId], [WidgetCode]);
                CREATE INDEX [ix_WidgetMas_Parent]
                    ON [{s}].[WidgetMas]([ParentId]);
            END;

            -- WidgetTra (widget degerleri)
            IF OBJECT_ID(N'[{s}].[WidgetTra]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[WidgetTra]
                (
                    [Id]              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_WidgetTra] PRIMARY KEY,
                    [WidgetId]        INT           NOT NULL,
                    [RecordId]        NVARCHAR(60)  NOT NULL,
                    [ParentRecordId]  NVARCHAR(60)  NULL,
                    [Value]           NVARCHAR(MAX) NULL,
                    [CreatedAt]       DATETIME2(0)  NOT NULL CONSTRAINT [df_WidgetTra_Created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt]       DATETIME2(0)  NOT NULL CONSTRAINT [df_WidgetTra_Updated] DEFAULT(SYSUTCDATETIME()),
                    CONSTRAINT [fk_WidgetTra_Widget]
                        FOREIGN KEY ([WidgetId]) REFERENCES [{s}].[WidgetMas]([Id])
                );
                CREATE UNIQUE INDEX [ux_WidgetTra_Record]
                    ON [{s}].[WidgetTra]([WidgetId], [RecordId]);
                CREATE INDEX [ix_WidgetTra_Record_Widget]
                    ON [{s}].[WidgetTra]([RecordId], [WidgetId]);
            END;

            -- Master-Detail: ParentRecordId kolonu (Faz E — grid widget)
            -- Idempotent: kolon yoksa eklenir, eski WidgetTra'larda mevcut veriyi kirmaz.
            IF OBJECT_ID(N'[{s}].[WidgetTra]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetTra]', N'ParentRecordId') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetTra] ADD [ParentRecordId] NVARCHAR(60) NULL;
            END;

            -- Rule Engine: RulesJSON kolonu (Faz G — kural ve formul motoru)
            -- Idempotent. JSON object shape: visibleIf + disabledIf + formula slotlari.
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'RulesJSON') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [RulesJSON] NVARCHAR(MAX) NULL;
            END;

            -- IsPlainField kolonu (Faz — sadece alan modu)
            -- Idempotent. true ise widget grup wrapper olmadan sade label+input olarak render edilir.
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'IsPlainField') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [IsPlainField] BIT NOT NULL CONSTRAINT [df_WidgetMas_Plain] DEFAULT(0);
            END;

            -- IsRequired kolonu (zorunlu alan modu)
            -- Idempotent. true ise DynamicWidgetRenderer save sirasinda bos birakilmasina izin vermez.
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'IsRequired') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [IsRequired] BIT NOT NULL CONSTRAINT [df_WidgetMas_Req] DEFAULT(0);
            END;

            -- IsListable kolonu (kart listesinde gosterim)
            -- Idempotent. false ise widget SmartCard kart chiplerinde gozukmez (masterWidgets'tan filtrelenir).
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'IsListable') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [IsListable] BIT NOT NULL CONSTRAINT [df_WidgetMas_Listable] DEFAULT(1);
            END;

            -- CompanyId kolonu (sirket bazli izolasyon)
            -- Idempotent. Mevcut satirlar '00000000-0000-0000-0000-000000000000' (Guid.Empty) alir.
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'CompanyId') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas]
                    ADD [CompanyId] INT NOT NULL
                        CONSTRAINT [df_WidgetMas_Company] DEFAULT(0);
            END;

            -- Kisitlama kolonlari (MinLength, ExpectedLength, MinValue, MaxValue)
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'MinLength') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [MinLength] INT NULL;
            END;
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'ExpectedLength') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [ExpectedLength] INT NULL;
            END;
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'MinValue') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [MinValue] DECIMAL(18,4) NULL;
            END;
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'MaxValue') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [MaxValue] DECIMAL(18,4) NULL;
            END;

            -- Semantik Renk Mimarisi: ColorType + ColorValue kolonlari (Semantik Hibrit Renk)
            -- ColorType: 0=Statik token, 1=Dinamik SQL (baska bir widget'in degerinden okunur)
            -- ColorValue: token kelimesi (slate/blue/emerald/amber/red/indigo) veya widget WidgetCode'u
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'ColorType') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas]
                    ADD [ColorType] INT NOT NULL CONSTRAINT [df_WidgetMas_ColorType] DEFAULT(0);
            END;
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'ColorValue') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas] ADD [ColorValue] NVARCHAR(100) NULL;
            END;

            -- ColSpan kolonu — form uzerinde kaplayacagi 24-col grid span'i
            -- (1-24). Varsayilan 12 (1/2 satir). Renderer CSS grid-column'a cevirir.
            -- Data migration: kolon ilk eklendiginde mevcut plain field satirlarini
            -- 24'e (tam satir) cek — eski davranislari korunsun.
            DECLARE @ColSpanJustAdded BIT = 0;
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'ColSpan') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas]
                    ADD [ColSpan] INT NOT NULL CONSTRAINT [df_WidgetMas_ColSpan] DEFAULT(12);
                SET @ColSpanJustAdded = 1;
            END;
            IF @ColSpanJustAdded = 1
            BEGIN
                -- Plain field satirlar eskiden tam genislik render ediliyordu.
                -- ColSpan=24 ile ayni gorunum korunur.
                UPDATE [{s}].[WidgetMas]
                   SET [ColSpan] = 24
                 WHERE [IsPlainField] = 1
                   AND [ColSpan] = 12;
            END;

            -- 12→24 ölçek migrasyonu: eski 12-col sistemden 24-col sisteme geçiş.
            -- MAX(ColSpan) = 12 ise eski sistem — tüm değerleri x2 yaparak 24-col'a
            -- upgrade et. MAX > 12 ise yeni sistem zaten aktif, no-op.
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'ColSpan') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [{s}].[WidgetMas] WHERE [ColSpan] > 12)
               AND EXISTS (SELECT 1 FROM [{s}].[WidgetMas] WHERE [ColSpan] > 0)
            BEGIN
                UPDATE [{s}].[WidgetMas]
                   SET [ColSpan] = CASE WHEN [ColSpan] * 2 > 24 THEN 24 ELSE [ColSpan] * 2 END
                 WHERE [ColSpan] BETWEEN 1 AND 12;
            END;

            -- LabelStyle kolonu — etiket gorunum stili: "standard" / "modern" / "inline".
            -- "inline" eski IsPlainField=true davranisinin yerini alir (160px sol
            -- etiket + sag input). Yeni admin UI bu uc segmentten secim yaptirir.
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'LabelStyle') IS NULL
            BEGIN
                ALTER TABLE [{s}].[WidgetMas]
                    ADD [LabelStyle] NVARCHAR(20) NOT NULL
                        CONSTRAINT [df_WidgetMas_LabelStyle] DEFAULT('standard');
            END;

            -- IsPlainField → LabelStyle='inline' veri migrasyonu (idempotent).
            -- Eski "Sade alan" toggle'i kaldirildi; davranis LabelStyle uzerine
            -- birlesti. Sadece LabelStyle='standard' satirlari guncellenir —
            -- 'modern' olarak ayarlanmis olanlara dokunulmaz. IsPlainField kolonu
            -- DB'de korunuyor (eski okuyucular icin), yalnizca renderer artik
            -- bakmiyor. Service tarafi yeni kayitlarda IsPlainField'i LabelStyle
            -- ile senkron tutar.
            IF OBJECT_ID(N'[{s}].[WidgetMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'IsPlainField') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetMas]', N'LabelStyle') IS NOT NULL
            BEGIN
                UPDATE [{s}].[WidgetMas]
                   SET [LabelStyle] = N'inline'
                 WHERE [IsPlainField] = 1
                   AND [LabelStyle] = N'standard';
            END;

            -- Unique index guncelle: eski (FormId, WidgetCode) → yeni (CompanyId, FormId, WidgetCode)
            -- Sadece eski indeks varsa ve CompanyId henuz dahil edilmemisse yeniden olustur.
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'ux_WidgetMas_FormCode'
                  AND object_id = OBJECT_ID(N'[{s}].[WidgetMas]'))
            AND NOT EXISTS (
                SELECT 1
                FROM sys.index_columns ic
                JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                WHERE ic.object_id = OBJECT_ID(N'[{s}].[WidgetMas]')
                  AND c.name = N'CompanyId'
                  AND ic.index_id = (
                      SELECT index_id FROM sys.indexes
                      WHERE name = N'ux_WidgetMas_FormCode'
                        AND object_id = OBJECT_ID(N'[{s}].[WidgetMas]')
                  ))
            BEGIN
                DROP INDEX [ux_WidgetMas_FormCode] ON [{s}].[WidgetMas];
                CREATE UNIQUE INDEX [ux_WidgetMas_FormCode]
                    ON [{s}].[WidgetMas]([CompanyId], [FormId], [WidgetCode]);
            END;
            """;

        await using (var ddlCmd = connection.CreateCommand())
        {
            ddlCmd.CommandText = ddlSql;
            await ddlCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // ── Batch 2: ParentRecordId kolonuna bagli filtered index ───────
        // Ayri batch cunku parser [ParentRecordId] kolonunu parse-time'da ister;
        // ALTER ile eklense bile ayni batch'te tanimlanmissa gorunmez.
        var indexSql = $"""
            IF OBJECT_ID(N'[{s}].[WidgetTra]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[WidgetTra]', N'ParentRecordId') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.indexes
                               WHERE object_id = OBJECT_ID(N'[{s}].[WidgetTra]')
                                 AND name = N'ix_WidgetTra_Parent')
            BEGIN
                CREATE INDEX [ix_WidgetTra_Parent]
                    ON [{s}].[WidgetTra]([ParentRecordId], [WidgetId])
                    WHERE [ParentRecordId] IS NOT NULL;
            END;
            """;

        await using (var indexCmd = connection.CreateCommand())
        {
            indexCmd.CommandText = indexSql;
            await indexCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// SQL View tabanli jenerik Rehber (Lookup/LOV) altyapisi.
    ///   - GuideMas: rehber tanim tablosu. Her satir bir SQL View'a baglanir.
    ///   - v_GuideContacts / v_GuideItems / v_GuideDocuments: baslangic 3 view.
    ///   - Seed: CUSTOMERS / ITEMS / SALES_QUOTES rehberleri.
    ///
    /// Idempotent: OBJECT_ID guard + CREATE OR ALTER VIEW + IF NOT EXISTS INSERT.
    /// </summary>
    private async Task EnsureGuideTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        // 1) GuideMas tablo
        var createTableSql = $"""
            IF OBJECT_ID(N'[{s}].[GuideMas]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[GuideMas]
                (
                    [Id]                INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_GuideMas] PRIMARY KEY,
                    [GuideCode]         NVARCHAR(60)   NOT NULL,
                    [GuideLabel]        NVARCHAR(200)  NOT NULL,
                    [ViewName]          NVARCHAR(200)  NOT NULL,
                    [ValueColumn]       NVARCHAR(60)   NOT NULL,
                    [DisplayColumn]     NVARCHAR(60)   NOT NULL,
                    [GridColumnsJson]   NVARCHAR(MAX)  NOT NULL,
                    [DefaultSortColumn] NVARCHAR(60)   NULL,
                    [IsActive]          BIT            NOT NULL CONSTRAINT [df_GuideMas_Active] DEFAULT(1),
                    [CreatedAt]         DATETIME2(0)   NOT NULL CONSTRAINT [df_GuideMas_Created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt]         DATETIME2(0)   NOT NULL CONSTRAINT [df_GuideMas_Updated] DEFAULT(SYSUTCDATETIME())
                );
                CREATE UNIQUE INDEX [ux_GuideMas_GuideCode] ON [{s}].[GuideMas]([GuideCode]);
            END;

            -- Rehber bazli varsayilan filtre (SQL WHERE fragment) — idempotent ALTER.
            -- Bu rehberi kullanan tum form alanlarinda otomatik uygulanir.
            IF OBJECT_ID(N'[{s}].[GuideMas]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[GuideMas]', N'DefaultFilterJson') IS NULL
            BEGIN
                ALTER TABLE [{s}].[GuideMas] ADD [DefaultFilterJson] NVARCHAR(MAX) NULL;
            END;
            """;

        await using (var cmd1 = connection.CreateCommand())
        {
            cmd1.CommandText = createTableSql;
            await cmd1.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2) View'lar — CREATE OR ALTER ayri batch (SQL Server syntax gereği)
        // Naming standardı: cbv_Guide_{EntityName}
        // cbv = CalibraHub View, Guide = Rehber tipi, _ ayirici

        // View 1: Cari Hesap Rehberi (Contacts → PascalCase kolonlar)
        // SalesRepId — cari secildiginde teklif/siparis ekraninin sales rep alanini otomatik doldurur.
        var v1 = $"""
            CREATE OR ALTER VIEW [{s}].[cbv_Guide_Contacts] AS
            SELECT
                [Id],
                CAST([AccountCode]  AS NVARCHAR(100)) AS [AccountCode],
                CAST([AccountTitle] AS NVARCHAR(300)) AS [AccountTitle],
                CAST([Phone]        AS NVARCHAR(50))  AS [Phone],
                CAST([City]         AS NVARCHAR(100)) AS [City],
                CAST([TaxNumber]    AS NVARCHAR(50))  AS [TaxNumber],
                [SalesRepresentativeId]              AS [SalesRepId]
            FROM [{s}].[Contact]
            WHERE [IsActive] = 1;
            """;
        await using (var cmd2 = connection.CreateCommand())
        {
            cmd2.CommandText = v1;
            await cmd2.ExecuteNonQueryAsync(cancellationToken);
        }

        // View 2: Malzeme Karti Rehberi (Items — PascalCase, Description sutunu kaldirildi)
        var v2 = $"""
            CREATE OR ALTER VIEW [{s}].[cbv_Guide_Items] AS
            SELECT
                [Id]                                AS [Id],
                CAST([Code] AS NVARCHAR(100))       AS [MaterialCode],
                CAST([Name] AS NVARCHAR(300))       AS [MaterialName]
            FROM [{s}].[Items]
            WHERE [IsActive] = 1;
            """;
        await using (var cmd3 = connection.CreateCommand())
        {
            cmd3.CommandText = v2;
            await cmd3.ExecuteNonQueryAsync(cancellationToken);
        }

        // View 2b: Mamul Rehberi (sadece TypeId=1 = FinishedGood) — is emri / uretim ekranlarinda
        // hammadde/yari mamul yerine sadece bitmis urunlerin secilmesi icin.
        var v2b = $"""
            CREATE OR ALTER VIEW [{s}].[cbv_Guide_Items_Finished] AS
            SELECT
                [Id]                                AS [Id],
                CAST([Code] AS NVARCHAR(100))       AS [MaterialCode],
                CAST([Name] AS NVARCHAR(300))       AS [MaterialName]
            FROM [{s}].[Items]
            WHERE [IsActive] = 1 AND [TypeId] = 1;
            """;
        await using (var cmd3b = connection.CreateCommand())
        {
            cmd3b.CommandText = v2b;
            await cmd3b.ExecuteNonQueryAsync(cancellationToken);
        }

        // View 3: Satis Teklifi Rehberi (sales_quotes → snake_case → PascalCase alias)
        // Cari ismi Contact.AccountTitle'dan cekilir — contact_name kolonu kaldirildi.
        var v3 = $"""
            CREATE OR ALTER VIEW [{s}].[cbv_Guide_Documents] AS
            SELECT
                CAST(q.[DocumentNumber]  AS NVARCHAR(100)) AS [DocumentNumber],
                CAST(ca.[AccountTitle]    AS NVARCHAR(300)) AS [CustomerName],
                CAST(q.[DocumentDate]    AS DATE)          AS [DocumentDate],
                CAST(q.[GrandTotal]      AS DECIMAL(18,2)) AS [GrandTotal],
                CAST(q.[status]           AS NVARCHAR(30))  AS [Status]
            FROM [{s}].[Document] q
            LEFT JOIN [{s}].[Contact] ca ON ca.[Id] = q.[ContactId]
            WHERE q.[IsActive] = 1;
            """;
        await using (var cmd4 = connection.CreateCommand())
        {
            cmd4.CommandText = v3;
            await cmd4.ExecuteNonQueryAsync(cancellationToken);
        }

        // ── DocumentLine tablosuna source_line_id kolonu (idempotent) ──
        // Kalem bazli kaynak iz: tekliften sipariş clone edilirken her satirin
        // kaynak teklif satiriyla 1-1 baglantisini tutar. revised_from_id ile
        // ayni pattern (self-referencing nullable INT FK).
        var addSourceLineCol = $"""
            IF OBJECT_ID(N'[{s}].[DocumentLine]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[DocumentLine]', N'source_line_id') IS NULL
            BEGIN
                ALTER TABLE [{s}].[DocumentLine] ADD [source_line_id] INT NULL;
                CREATE INDEX [IX_DocumentLine_source_line_id]
                    ON [{s}].[DocumentLine] ([source_line_id]);
            END;
            """;
        await using (var cmdSL = connection.CreateCommand())
        {
            cmdSL.CommandText = addSourceLineCol;
            await cmdSL.ExecuteNonQueryAsync(cancellationToken);
        }

        // ── document_attachments tablosu ensure ──
        // Belge basligi (teklif/siparis/fatura) icin yuklenen dosyalarin TEK tablosu.
        // INT FK Document.id'ye baglanir; eski sales_quote_attachments (Guid) deprecated
        // birakilir (legacy data ihtimaline karsi silinmez). Synonym ile bu tablo butun
        // olarak farkli bir veri kaynagina yonlendirilebilir.
        var ensureDocAtt = $"""
            IF OBJECT_ID(N'[{s}].[document_attachments]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[document_attachments] (
                    [id]            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [document_id]   INT NOT NULL,
                    [file_name]     NVARCHAR(255) NOT NULL,
                    [mime_type]     NVARCHAR(150) NULL,
                    [file_size]     BIGINT NOT NULL DEFAULT 0,
                    [content]       VARBINARY(MAX) NOT NULL,
                    [uploaded_by]   NVARCHAR(150) NULL,
                    [uploaded_at]   DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    [is_active]     BIT NOT NULL DEFAULT 1
                );
                CREATE INDEX [IX_document_attachments_doc]
                    ON [{s}].[document_attachments] ([document_id], [is_active]);
            END;
            """;
        await using (var cmdDA = connection.CreateCommand())
        {
            cmdDA.CommandText = ensureDocAtt;
            await cmdDA.ExecuteNonQueryAsync(cancellationToken);
        }

        // ── document_source tablosu ensure (cbv_Guide_SalesOrders icin pre-req) ──
        // Runtime'da SqlDocumentSourceRepository.EnsureSchemaAsync da bunu yapar; burada
        // initializer seviyesinde garanti altina alınır ki view query'leri patlamasın.
        var ensureDocSrc = $"""
            IF OBJECT_ID(N'[{s}].[document_source]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[document_source] (
                    [id]                  INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [document_id]         INT NOT NULL,
                    [source_document_id]  INT NOT NULL,
                    [created_at]          DATETIME2 NOT NULL DEFAULT GETDATE()
                );
                CREATE UNIQUE INDEX [IX_document_source_pair]
                    ON [{s}].[document_source] ([document_id], [source_document_id]);
                CREATE INDEX [IX_document_source_src]
                    ON [{s}].[document_source] ([source_document_id]);
            END;
            """;
        await using (var cmdDS = connection.CreateCommand())
        {
            cmdDS.CommandText = ensureDocSrc;
            await cmdDS.ExecuteNonQueryAsync(cancellationToken);
        }

        // View 4: Satis Siparisi Rehberi — SADECE TEKLIFTEN OLUSTURULAN siparisler.
        // EXISTS document_source ile filtre (manuel "Yeni Siparis" girisleri haricte tutulur).
        // ValueColumn=Code, DisplayColumn=Name standart rehber kuralina uygun.
        var v4 = $"""
            CREATE OR ALTER VIEW [{s}].[cbv_Guide_SalesOrders] AS
            SELECT
                q.[id] AS [Id],
                CAST(q.[DocumentNumber] AS NVARCHAR(100)) AS [Code],
                CAST(ISNULL(ca.[AccountTitle], N'(musterisiz)') AS NVARCHAR(300)) AS [Name],
                CAST(q.[DocumentDate] AS DATE) AS [DocumentDate],
                CAST(q.[GrandTotal] AS DECIMAL(18,2)) AS [GrandTotal],
                CAST(q.[status] AS NVARCHAR(30)) AS [Status]
            FROM [{s}].[Document] q
            LEFT JOIN [{s}].[Contact] ca ON ca.[Id] = q.[ContactId]
            INNER JOIN [{s}].[document_types] dt
                ON dt.[id] = q.[DocumentTypeId] AND dt.[code] = N'satis_siparisi'
            WHERE q.[IsActive] = 1
              AND EXISTS (SELECT 1 FROM [{s}].[document_source] ds WHERE ds.[document_id] = q.[id]);
            """;
        await using (var cmd4b = connection.CreateCommand())
        {
            cmd4b.CommandText = v4;
            await cmd4b.ExecuteNonQueryAsync(cancellationToken);
        }

        // 3) Seed satirlar — idempotent
        var seedSql = $"""
            IF NOT EXISTS (SELECT 1 FROM [{s}].[GuideMas] WHERE [GuideCode] = N'CUSTOMERS')
                INSERT INTO [{s}].[GuideMas] ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],[GridColumnsJson],[DefaultSortColumn])
                VALUES (N'CUSTOMERS', N'Cari Hesap Rehberi', N'cbv_Guide_Contacts',
                        N'AccountCode', N'AccountTitle',
                        N'["AccountCode","AccountTitle","Phone","City","TaxNumber","SalesRepId"]',
                        N'AccountCode');
            ELSE
                UPDATE [{s}].[GuideMas]
                SET [GridColumnsJson] = N'["AccountCode","AccountTitle","Phone","City","TaxNumber","SalesRepId"]'
                WHERE [GuideCode] = N'CUSTOMERS';

            -- ITEMS: cbv_Guide_Items view'inda sadece Id, MaterialCode, MaterialName var
            -- (Description sutunu kaldirildi — line 6279). GridColumnsJson view ile uyumlu
            -- olmali yoksa search query 207 'Invalid column name' verir.
            IF NOT EXISTS (SELECT 1 FROM [{s}].[GuideMas] WHERE [GuideCode] = N'ITEMS')
                INSERT INTO [{s}].[GuideMas] ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],[GridColumnsJson],[DefaultSortColumn])
                VALUES (N'ITEMS', N'Malzeme Karti Rehberi', N'cbv_Guide_Items',
                        N'MaterialCode', N'MaterialName',
                        N'["Id","MaterialCode","MaterialName"]',
                        N'MaterialCode');

            -- ViewName bazli normalize: cbv_Guide_Items'a baglanan TUM GuideMas
            -- kayitlari (auto-discovery dahil) ayni GridColumnsJson + value/display'a
            -- sahip olur. Boylece duplikat satirlardan herhangi birine resolve edilse
            -- de schema dogru kolon listesini doner.
            UPDATE [{s}].[GuideMas]
            SET [GridColumnsJson] = N'["Id","MaterialCode","MaterialName"]',
                [ValueColumn]     = N'MaterialCode',
                [DisplayColumn]   = N'MaterialName',
                [DefaultSortColumn] = N'MaterialCode'
            WHERE [ViewName] = N'cbv_Guide_Items';

            -- ITEMS_FINISHED: Mamul rehberi (TypeId=1) — is emri / uretim ekranlari icin
            IF NOT EXISTS (SELECT 1 FROM [{s}].[GuideMas] WHERE [GuideCode] = N'ITEMS_FINISHED')
                INSERT INTO [{s}].[GuideMas] ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],[GridColumnsJson],[DefaultSortColumn])
                VALUES (N'ITEMS_FINISHED', N'Mamul Rehberi', N'cbv_Guide_Items_Finished',
                        N'MaterialCode', N'MaterialName',
                        N'["Id","MaterialCode","MaterialName"]',
                        N'MaterialCode');

            -- Legacy view v_GuideItems varsa kolon adlari icin tazele (material_* → code/name/description)
            IF OBJECT_ID(N'[{s}].[v_GuideItems]', N'V') IS NOT NULL
            BEGIN
                EXEC(N'
                    CREATE OR ALTER VIEW [{s}].[v_GuideItems] AS
                    SELECT
                        [Id]                                AS [Id],
                        CAST([Code] AS NVARCHAR(100))       AS [MaterialCode],
                        CAST([Name] AS NVARCHAR(300))       AS [MaterialName]
                    FROM [{s}].[Items]
                    WHERE [IsActive] = 1;
                ');
            END;

            IF NOT EXISTS (SELECT 1 FROM [{s}].[GuideMas] WHERE [GuideCode] = N'SALES_QUOTES')
                INSERT INTO [{s}].[GuideMas] ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],[GridColumnsJson],[DefaultSortColumn])
                VALUES (N'SALES_QUOTES', N'Satis Teklifi Rehberi', N'cbv_Guide_Documents',
                        N'DocumentNumber', N'CustomerName',
                        N'["DocumentNumber","CustomerName","DocumentDate","GrandTotal","Status"]',
                        N'DocumentDate');

            -- SALES_ORDERS: Tekliften olusturulmus satis siparisleri rehberi.
            -- ValueColumn=Code, DisplayColumn=Name standart kurallarina uygun.
            IF NOT EXISTS (SELECT 1 FROM [{s}].[GuideMas] WHERE [GuideCode] = N'SALES_ORDERS')
                INSERT INTO [{s}].[GuideMas] ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],[GridColumnsJson],[DefaultSortColumn])
                VALUES (N'SALES_ORDERS', N'Satis Siparisi Rehberi (Tekliften)', N'cbv_Guide_SalesOrders',
                        N'Code', N'Name',
                        N'["Code","Name","DocumentDate","GrandTotal","Status"]',
                        N'DocumentDate');
            """;

        await using (var cmd5 = connection.CreateCommand())
        {
            cmd5.CommandText = seedSql;
            await cmd5.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// integrator_settings tablosunu GUID PK'dan INT IDENTITY PK'ya tasir,
    /// company_id uzerinde UNIQUE kisitlama ekler (sirket basina tek kayit),
    /// incoming_documents.integrator_settings_id kolonunu da INT'e donusturur.
    /// Idempotent: id kolonu zaten INT ise atlaner.
    /// </summary>
    private async Task MigrateIntegratorSettingsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");
        var sql = $"""
            -- Sadece id kolonu UNIQUEIDENTIFIER ise calis
            IF OBJECT_ID(N'[{s}].[integrator_settings]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns c
                   JOIN sys.types t ON c.system_type_id = t.system_type_id
                   WHERE c.object_id = OBJECT_ID(N'[{s}].[integrator_settings]')
                     AND c.name = N'id' AND t.name = N'uniqueidentifier')
            BEGIN
                -- 1) incoming_documents uzerindeki FK'yi dusur
                IF EXISTS (SELECT 1 FROM sys.foreign_keys
                    WHERE name = N'fk_incoming_documents_integrator_settings_id'
                      AND parent_object_id = OBJECT_ID(N'[{s}].[incoming_documents]'))
                    ALTER TABLE [{s}].[incoming_documents]
                        DROP CONSTRAINT [fk_incoming_documents_integrator_settings_id];

                -- 2) incoming_documents.integrator_settings_id kolonunu INT NULL olarak yeniden olustur
                IF OBJECT_ID(N'[{s}].[incoming_documents]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'{sl}.incoming_documents', N'integrator_settings_id') IS NOT NULL
                BEGIN
                    ALTER TABLE [{s}].[incoming_documents] DROP COLUMN [integrator_settings_id];
                    ALTER TABLE [{s}].[incoming_documents] ADD [integrator_settings_id] INT NULL;
                END

                -- 3) integrator_settings uzerindeki tum FK ve index'leri dusur
                DECLARE @dropFks NVARCHAR(MAX) = N'';
                SELECT @dropFks = @dropFks
                    + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))
                    + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
                    + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N'; '
                FROM sys.foreign_keys fk
                WHERE fk.referenced_object_id = OBJECT_ID(N'[{s}].[integrator_settings]');
                IF LEN(@dropFks) > 0 EXEC sp_executesql @dropFks;

                IF EXISTS (SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[{s}].[integrator_settings]')
                      AND name = N'ux_integrator_settings_company_id_name')
                    DROP INDEX [ux_integrator_settings_company_id_name]
                        ON [{s}].[integrator_settings];

                IF EXISTS (SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'[{s}].[integrator_settings]')
                      AND name = N'ux_integrator_settings_name')
                    DROP INDEX [ux_integrator_settings_name]
                        ON [{s}].[integrator_settings];

                -- 4) Tabloyu dusur ve INT IDENTITY PK ile yeniden olustur
                DROP TABLE [{s}].[integrator_settings];

                CREATE TABLE [{s}].[integrator_settings]
                (
                    [id]                                INT IDENTITY(1,1) NOT NULL,
                    [company_id]                        INT NOT NULL,
                    [provider]                          NVARCHAR(50) NOT NULL,
                    [name]                              NVARCHAR(100) NOT NULL DEFAULT N'',
                    [base_url]                          NVARCHAR(300) NOT NULL,
                    [company_tax_number]                NVARCHAR(20) NOT NULL DEFAULT N'',
                    [username]                          NVARCHAR(120) NOT NULL,
                    [secret]                            NVARCHAR(1024) NOT NULL,
                    [polling_interval_seconds]          INT NOT NULL DEFAULT(120),
                    [max_records_per_pull]              INT NOT NULL DEFAULT(200),
                    [log_retention_days]                INT NOT NULL DEFAULT(30),
                    [include_received_documents_in_pull] BIT NOT NULL DEFAULT(0),
                    [mark_downloaded_documents_as_received] BIT NOT NULL DEFAULT(0),
                    [include_issued_einvoice_in_pull]   BIT NOT NULL DEFAULT(0),
                    [include_issued_earchive_in_pull]   BIT NOT NULL DEFAULT(0),
                    [include_issued_edispatch_in_pull]  BIT NOT NULL DEFAULT(0),
                    [is_active]                         BIT NOT NULL DEFAULT(1),
                    [Created]                        DATETIME2 NOT NULL DEFAULT(GETDATE()),
                    [Updated]                        DATETIME2 NULL     DEFAULT(GETDATE()),
                    [app_str]                           NVARCHAR(100) NULL,
                    [source]                            NVARCHAR(20) NULL,
                    [app_version]                       NVARCHAR(20) NULL,
                    [schedule_enabled]                  BIT NOT NULL DEFAULT(0),
                    [timeout_seconds]                   INT NOT NULL DEFAULT(30),
                    [lookback_days]                     INT NOT NULL DEFAULT(30),
                    CONSTRAINT [pk_integrator_settings] PRIMARY KEY ([id]),
                    CONSTRAINT [ck_integrator_settings_polling]
                        CHECK ([polling_interval_seconds] >= 10),
                    CONSTRAINT [ck_integrator_settings_max_records]
                        CHECK ([max_records_per_pull] >= 1 AND [max_records_per_pull] <= 5000),
                    CONSTRAINT [ck_integrator_settings_log_retention]
                        CHECK ([log_retention_days] >= 1 AND [log_retention_days] <= 3650)
                );

                -- 5) Sirket basina tek kayit: company_id uzerinde UNIQUE index
                CREATE UNIQUE INDEX [ux_integrator_settings_company_id]
                    ON [{s}].[integrator_settings]([company_id]);

                -- 6) incoming_documents FK'sini yeniden ekle
                IF OBJECT_ID(N'[{s}].[incoming_documents]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'{sl}.incoming_documents', N'integrator_settings_id') IS NOT NULL
                    ALTER TABLE [{s}].[incoming_documents]
                        ADD CONSTRAINT [fk_incoming_documents_integrator_settings_id]
                        FOREIGN KEY ([integrator_settings_id])
                        REFERENCES [{s}].[integrator_settings]([id]);
            END

            -- integrator_settings tablosu yoksa yeni schema ile olustur
            IF OBJECT_ID(N'[{s}].[integrator_settings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[integrator_settings]
                (
                    [id]                                INT IDENTITY(1,1) NOT NULL,
                    [company_id]                        INT NOT NULL,
                    [provider]                          NVARCHAR(50) NOT NULL,
                    [name]                              NVARCHAR(100) NOT NULL DEFAULT N'',
                    [base_url]                          NVARCHAR(300) NOT NULL,
                    [company_tax_number]                NVARCHAR(20) NOT NULL DEFAULT N'',
                    [username]                          NVARCHAR(120) NOT NULL,
                    [secret]                            NVARCHAR(1024) NOT NULL,
                    [polling_interval_seconds]          INT NOT NULL DEFAULT(120),
                    [max_records_per_pull]              INT NOT NULL DEFAULT(200),
                    [log_retention_days]                INT NOT NULL DEFAULT(30),
                    [include_received_documents_in_pull] BIT NOT NULL DEFAULT(0),
                    [mark_downloaded_documents_as_received] BIT NOT NULL DEFAULT(0),
                    [include_issued_einvoice_in_pull]   BIT NOT NULL DEFAULT(0),
                    [include_issued_earchive_in_pull]   BIT NOT NULL DEFAULT(0),
                    [include_issued_edispatch_in_pull]  BIT NOT NULL DEFAULT(0),
                    [is_active]                         BIT NOT NULL DEFAULT(1),
                    [Created]                        DATETIME2 NOT NULL DEFAULT(GETDATE()),
                    [Updated]                        DATETIME2 NULL     DEFAULT(GETDATE()),
                    [app_str]                           NVARCHAR(100) NULL,
                    [source]                            NVARCHAR(20) NULL,
                    [app_version]                       NVARCHAR(20) NULL,
                    [schedule_enabled]                  BIT NOT NULL DEFAULT(0),
                    [timeout_seconds]                   INT NOT NULL DEFAULT(30),
                    [lookback_days]                     INT NOT NULL DEFAULT(30),
                    CONSTRAINT [pk_integrator_settings] PRIMARY KEY ([id]),
                    CONSTRAINT [ck_integrator_settings_polling]
                        CHECK ([polling_interval_seconds] >= 10),
                    CONSTRAINT [ck_integrator_settings_max_records]
                        CHECK ([max_records_per_pull] >= 1 AND [max_records_per_pull] <= 5000),
                    CONSTRAINT [ck_integrator_settings_log_retention]
                        CHECK ([log_retention_days] >= 1 AND [log_retention_days] <= 3650)
                );

                CREATE UNIQUE INDEX [ux_integrator_settings_company_id]
                    ON [{s}].[integrator_settings]([company_id]);
            END
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MigrateItemsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");
        // Legacy tablo/kolon isimleri C# variable olarak tutulur — SQL literal olarak gorunmez,
        // bu sayede Initializer genelinde yapilan tablo/kolon rename bulk replace'i bu metoda dokunmaz.
        var li   = "Items";                        // legacy Items (sonradan Item'a rename edilecek)
        var lsq  = "sales_quote_lines";            // legacy (sonradan DocumentLine'a rename edilecek)
        var lp   = "price_list_entries";           // legacy (sonradan PriceList'e rename edilecek)
        var lsc  = "stock_card_id";                // legacy kolon (sonradan item_id'ye rename edilecek)
        var lscp = "StockCardId";                  // legacy PascalCase kolon (MaterialGroupMappings)
        var sql = $"""
            -- 1) MaterialDefinitions varsa drop
            IF OBJECT_ID(N'[{s}].[MaterialDefinitions]', N'U') IS NOT NULL
            BEGIN
                DECLARE @fk1 NVARCHAR(MAX)=N'';
                SELECT @fk1=@fk1+N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(fk.parent_object_id))+N' DROP CONSTRAINT '+QUOTENAME(fk.name)+N'; '
                FROM sys.foreign_keys fk WHERE fk.referenced_object_id=OBJECT_ID(N'[{s}].[MaterialDefinitions]');
                IF LEN(@fk1)>0 EXEC sp_executesql @fk1;
                DROP TABLE [{s}].[MaterialDefinitions];
            END;

            -- 2) Items GUID PK ise drop
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id
                   WHERE c.object_id=OBJECT_ID(N'[{s}].[{li}]') AND c.name=N'id' AND t.name=N'int')
            BEGIN
                DECLARE @fk2 NVARCHAR(MAX)=N'';
                SELECT @fk2=@fk2+N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(fk.parent_object_id))+N' DROP CONSTRAINT '+QUOTENAME(fk.name)+N'; '
                FROM sys.foreign_keys fk WHERE fk.referenced_object_id=OBJECT_ID(N'[{s}].[{li}]');
                IF LEN(@fk2)>0 EXEC sp_executesql @fk2;
                DROP TABLE [{s}].[{li}];
            END;

            -- 3) stock_card_property_mappings GUID item_id ise drop
            IF OBJECT_ID(N'[{s}].[stock_card_property_mappings]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id
                   WHERE c.object_id=OBJECT_ID(N'[{s}].[stock_card_property_mappings]') AND c.name=N'{lsc}' AND t.name=N'uniqueidentifier')
            BEGIN
                DECLARE @fk3 NVARCHAR(MAX)=N'';
                SELECT @fk3=@fk3+N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(fk.parent_object_id))+N' DROP CONSTRAINT '+QUOTENAME(fk.name)+N'; '
                FROM sys.foreign_keys fk WHERE fk.parent_object_id=OBJECT_ID(N'[{s}].[stock_card_property_mappings]');
                IF LEN(@fk3)>0 EXEC sp_executesql @fk3;
                DROP TABLE [{s}].[stock_card_property_mappings];
            END;

            -- 4) MaterialGroupMappings GUID ItemId ise drop
            IF OBJECT_ID(N'[{s}].[MaterialGroupMappings]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id
                   WHERE c.object_id=OBJECT_ID(N'[{s}].[MaterialGroupMappings]') AND c.name=N'{lscp}' AND t.name=N'uniqueidentifier')
                DROP TABLE [{s}].[MaterialGroupMappings];

            -- 4.5) Items tablosuna eksik kolonlari ekle (legacy kolon eklemeleri — rename sonrasi da calisacak sekilde COL_LENGTH guard ile)
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{sl}.{li}', N'description') IS NULL
               AND COL_LENGTH(N'{sl}.{li}', N'material_description') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [description] NVARCHAR(500) NULL;
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{sl}.{li}', N'type_id') IS NULL
               AND COL_LENGTH(N'{sl}.{li}', N'material_type_id') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [type_id] INT NULL;
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL AND COL_LENGTH(N'{sl}.{li}', N'track_combinations') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [track_combinations] BIT NOT NULL DEFAULT(0);
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL AND COL_LENGTH(N'{sl}.{li}', N'created_by_user_id') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [created_by_user_id] INT NULL;
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL AND COL_LENGTH(N'{sl}.{li}', N'modified_at') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [modified_at] DATETIME2 NULL;
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL AND COL_LENGTH(N'{sl}.{li}', N'modified_by_user_id') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [modified_by_user_id] INT NULL;
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL AND COL_LENGTH(N'{sl}.{li}', N'image_data') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [image_data] VARBINARY(MAX) NULL;
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL AND COL_LENGTH(N'{sl}.{li}', N'image_mime_type') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [image_mime_type] NVARCHAR(50) NULL;

            -- 5-7) Iliskili tablolardaki item_id GUID ise: kolon drop+add ile degistir
            IF OBJECT_ID(N'[{s}].[{lsq}]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id
                   WHERE c.object_id=OBJECT_ID(N'[{s}].[{lsq}]') AND c.name=N'{lsc}' AND t.name=N'uniqueidentifier')
            BEGIN
                ALTER TABLE [{s}].[{lsq}] DROP COLUMN [{lsc}];
                ALTER TABLE [{s}].[{lsq}] ADD [{lsc}] INT NULL;
            END;

            IF OBJECT_ID(N'[{s}].[{lp}]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id
                   WHERE c.object_id=OBJECT_ID(N'[{s}].[{lp}]') AND c.name=N'{lsc}' AND t.name=N'uniqueidentifier')
            BEGIN
                ALTER TABLE [{s}].[{lp}] DROP COLUMN [{lsc}];
                ALTER TABLE [{s}].[{lp}] ADD [{lsc}] INT NULL;
            END;

            -- price_list_entries: price → buying_price + selling_price
            IF OBJECT_ID(N'[{s}].[{lp}]', N'U') IS NOT NULL
               AND EXISTS     (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[{lp}]') AND name=N'price')
               AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[{lp}]') AND name=N'buying_price')
            BEGIN
                ALTER TABLE [{s}].[{lp}] ADD [buying_price]  DECIMAL(18,4) NOT NULL CONSTRAINT [df_ple_buying_price]  DEFAULT(0);
                ALTER TABLE [{s}].[{lp}] ADD [selling_price] DECIMAL(18,4) NOT NULL CONSTRAINT [df_ple_selling_price] DEFAULT(0);
                EXEC(N'UPDATE [{s}].[{lp}] SET [selling_price] = [price]');
            END;
            -- [price] kolonunu DECLARE ile ayrı blokta sil (SQL Server DECLARE-in-IF kısıtı)
            IF OBJECT_ID(N'[{s}].[{lp}]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'[{s}].[{lp}]') AND name=N'price')
            BEGIN
                EXEC(N'
                    DECLARE @dfName NVARCHAR(200);
                    SELECT @dfName = dc.name FROM sys.default_constraints dc
                        JOIN sys.columns c ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
                        WHERE dc.parent_object_id=OBJECT_ID(N''[{s}].[{lp}]'') AND c.name=N''price'';
                    IF @dfName IS NOT NULL EXEC(N''ALTER TABLE [{s}].[{lp}] DROP CONSTRAINT ['' + @dfName + N'']'');
                    ALTER TABLE [{s}].[{lp}] DROP COLUMN [price];
                ');
            END;

            IF OBJECT_ID(N'[{s}].[stock_unit_conversions]', N'U') IS NOT NULL
               AND EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON c.system_type_id=t.system_type_id
                   WHERE c.object_id=OBJECT_ID(N'[{s}].[stock_unit_conversions]') AND c.name=N'{lsc}' AND t.name=N'uniqueidentifier')
            BEGIN
                ALTER TABLE [{s}].[stock_unit_conversions] DROP COLUMN [{lsc}];
                ALTER TABLE [{s}].[stock_unit_conversions] ADD [{lsc}] INT NOT NULL DEFAULT(0);
            END;

            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 17 tablonun eski isimden yeni isme sp_rename ile toplu yeniden adlandirilmasi (Phase 1).
    /// Her blok idempotent: eski tablo VAR ve yeni tablo YOK ise rename, aksi halde atla.
    /// MigrateItemsTableAsync (legacy Items yapisal fix) SONRASINDA ve CREATE TABLE metotlarindan ONCE calisir.
    /// </summary>
    private async Task MigrateTableRenamesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        // Legacy tablo isimleri — SQL literal olarak tutulmaz, C# variable uzerinden sp_rename'e aktarilir.
        var legacyRenames = new (string OldName, string NewName)[]
        {
            // stock_cards (legacy) → Items; Item (tekil, eski) → Items (cogul, yeni hedef)
            ("stock_cards",                 "Items"),
            ("Item",                        "Items"),
            ("Contacts",             "Contact"),
            ("measure_unit_definitions",    "Unit"),
            ("configuration_properties",    "Feature"),
            ("configuration_property_values","FeatureValue"),
            ("sales_quotes",                "Document"),
            ("sales_quote_lines",           "DocumentLine"),
            ("Locations",          "Location"),
            ("BOMs",                "BOM"),
            ("BOMLines",            "BOMLine"),
            ("material_card_field_groups",  "FieldGroup"),
            ("material_card_field_settings","Field"),
            ("departments",                 "Department"),
            ("users",                       "User"),
            ("company",                     "Company"),
            ("price_groups",                "PriceGroup"),
            ("price_list_entries",          "PriceList"),
        };

        var sb = new System.Text.StringBuilder();
        foreach (var (oldName, newName) in legacyRenames)
        {
            sb.AppendLine($"""
                IF OBJECT_ID(N'[{s}].[{oldName}]', N'U') IS NOT NULL
                   AND OBJECT_ID(N'[{s}].[{newName}]', N'U') IS NULL
                BEGIN
                    EXEC sp_rename N'[{s}].[{oldName}]', N'{newName}';
                END;
                """);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Kolon sp_rename'leri (Phase 2): Document + DocumentLine + PriceList + FK kolon rename'leri.
    /// Her blok idempotent: eski kolon VAR ve yeni kolon YOK ise rename, aksi halde atla.
    /// MigrateTableRenamesAsync (tablo rename) SONRASINDA calisir — artik tablolar yeni isimde.
    /// </summary>
    private async Task MigrateColumnRenamesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        // Format: (Tablo, eski_kolon, yeni_kolon)
        var columnRenames = new (string Table, string OldCol, string NewCol)[]
        {
            // Document (eski sales_quotes) — generic document kolonlari
            ("Document",     "quote_number",      "document_number"),
            ("Document",     "quote_date",        "document_date"),
            ("Document",     "customer_id",       "contact_id"),
            ("Document",     "customer_name",     "contact_name"),
            ("Document",     "customer_address",  "contact_address"),
            ("Document",     "parent_quote_id",   "parent_document_id"),
            // DocumentLine (eski sales_quote_lines)
            ("DocumentLine", "quote_id",          "document_id"),
            ("DocumentLine", "stock_card_id",     "item_id"),
            // PriceList (eski price_list_entries)
            ("PriceList",    "stock_card_id",     "item_id"),
            ("PriceList",    "contact_account_id","contact_id"),
            // Stock unit conversions (tablo ismi kaldi, kolon rename)
            ("stock_unit_conversions",        "stock_card_id", "item_id"),
            // stock_card_property_mappings (tablo ismi kaldi, kolon rename)
            ("stock_card_property_mappings",  "stock_card_id", "item_id"),
            // FeatureValue (eski configuration_property_values)
            ("FeatureValue",  "property_id",    "feature_id"),
            // BOMLine (eski ProductTreeLines) — FK kolon rename
            ("BOMLine",       "ProductTreeId",  "BOMId"),
        };

        // Her kolon rename'i ayri batch'te calistir — bir tanesi patlarsa digerleri devam edebilsin.
        var allRenames = new List<(string Table, string OldCol, string NewCol)>(columnRenames);
        // PascalCase kolon rename: MaterialGroupMappings.StockCardId -> ItemId
        allRenames.Add(("MaterialGroupMappings", "StockCardId", "ItemId"));

        foreach (var (table, oldCol, newCol) in allRenames)
        {
            var sql = $"""
                IF OBJECT_ID(N'[{s}].[{table}]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[{table}]', N'{oldCol}') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[{table}]', N'{newCol}') IS NULL
                BEGIN
                    EXEC sp_rename N'[{s}].[{table}].[{oldCol}]', N'{newCol}', N'COLUMN';
                END;
                """;
            try
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                Console.Error.WriteLine($"[DB INIT WARN] Column rename atlandi: {table}.{oldCol} -> {newCol} — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Screen code slug'larinin eski → yeni degerlere migrate edilmesi (Phase 2 slug rename).
    /// screen_layout_definitions tablosundaki mevcut kayitlari yeni slug'lara UPDATE eder.
    /// Idempotent: sadece eski slug varsa update, yeni slug varsa no-op.
    /// </summary>
    private async Task MigrateScreenCodesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var screenCodeRenames = new (string OldSlug, string NewSlug)[]
        {
            ("contact_accounts",         "contacts"),
            ("sales_quotes",             "documents"),
            ("warehouse_locations",      "locations"),
            ("measure_unit_definitions", "units"),
            ("material_cards",           "items"),
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"IF OBJECT_ID(N'[{s}].[screen_layout_definitions]', N'U') IS NOT NULL");
        sb.AppendLine("BEGIN");
        foreach (var (oldSlug, newSlug) in screenCodeRenames)
        {
            // UNIQUE index var — eski slug varken yeni slug yoksa guvenle rename
            sb.AppendLine($"""
                    IF EXISTS (SELECT 1 FROM [{s}].[screen_layout_definitions] WHERE [screen_code] = N'{oldSlug}')
                       AND NOT EXISTS (SELECT 1 FROM [{s}].[screen_layout_definitions] WHERE [screen_code] = N'{newSlug}')
                    BEGIN
                        UPDATE [{s}].[screen_layout_definitions]
                            SET [screen_code] = N'{newSlug}', [Updated] = SYSUTCDATETIME()
                            WHERE [screen_code] = N'{oldSlug}';
                    END;
                """);
        }
        sb.AppendLine("END;");

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Phase 3: Document / DocumentLine / document_types / report_templates
    /// tablolarinin PK ve FK kolonlarini UNIQUEIDENTIFIER'dan INT IDENTITY'e tasir.
    ///
    /// Strateji: Eger Document.id kolonu hala uniqueidentifier ise, Document ekosistemini
    /// (DocumentLine + Document + report_templates + document_types) tamamen DROP eder.
    /// Sonraki EnsureDocumentTypesTableAsync + EnsureDocumentTablesAsync + SeedDocumentTypesAsync
    /// cagrilari yeni schemada (INT IDENTITY) tabloyu olusturur.
    ///
    /// NOT: Bu metod mevcut Document/DocumentLine satirlarini SILER. Dev/test DB icin
    /// kabul edilebilir; production icin proper data migration (GUID -> INT mapping + FK
    /// cozme) yazilmasi gerekir.
    /// </summary>
    private async Task MigrateDocumentPkToIntAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            -- Kontrol: Document.id tipi hala uniqueidentifier mi?
            IF OBJECT_ID(N'[{s}].[Document]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns c
                   JOIN sys.types t ON c.system_type_id = t.system_type_id
                   WHERE c.object_id = OBJECT_ID(N'[{s}].[Document]')
                     AND c.name = N'id'
                     AND t.name = N'uniqueidentifier'
               )
            BEGIN
                -- Bagli FK'leri drop et (constraint isimlerinden bagimsiz)
                DECLARE @drop NVARCHAR(MAX) = N'';
                SELECT @drop = @drop + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))
                             + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
                             + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N'; '
                  FROM sys.foreign_keys fk
                 WHERE fk.referenced_object_id IN (
                        OBJECT_ID(N'[{s}].[Document]'),
                        OBJECT_ID(N'[{s}].[document_types]')
                 );
                IF LEN(@drop) > 0 EXEC sp_executesql @drop;

                -- Tablolari drop et (LineDetail -> Line -> Document)
                IF OBJECT_ID(N'[{s}].[sales_quote_line_details]', N'U') IS NOT NULL
                    DROP TABLE [{s}].[sales_quote_line_details];
                IF OBJECT_ID(N'[{s}].[DocumentLine]', N'U') IS NOT NULL
                    DROP TABLE [{s}].[DocumentLine];
                IF OBJECT_ID(N'[{s}].[Document]', N'U') IS NOT NULL
                    DROP TABLE [{s}].[Document];
                IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
                    DROP TABLE [{s}].[report_templates];
                IF OBJECT_ID(N'[{s}].[document_types]', N'U') IS NOT NULL
                    DROP TABLE [{s}].[document_types];

                PRINT '[DB INIT] Document ekosistemi GUID PK''den INT IDENTITY''ye migrate ediliyor — tablolar drop edildi, yeniden olusturulacak.';
            END;

            -- Eger document_types hala GUID PK'li ise (Document yoksa ama document_types kalmissa) onu da drop et
            IF OBJECT_ID(N'[{s}].[document_types]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.columns c
                   JOIN sys.types t ON c.system_type_id = t.system_type_id
                   WHERE c.object_id = OBJECT_ID(N'[{s}].[document_types]')
                     AND c.name = N'id'
                     AND t.name = N'uniqueidentifier'
               )
            BEGIN
                DECLARE @drop2 NVARCHAR(MAX) = N'';
                SELECT @drop2 = @drop2 + N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id))
                              + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
                              + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N'; '
                  FROM sys.foreign_keys fk
                 WHERE fk.referenced_object_id = OBJECT_ID(N'[{s}].[document_types]');
                IF LEN(@drop2) > 0 EXEC sp_executesql @drop2;

                IF OBJECT_ID(N'[{s}].[report_templates]', N'U') IS NOT NULL
                    DROP TABLE [{s}].[report_templates];
                DROP TABLE [{s}].[document_types];
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Contacts tablosuna yeni sütunlar ekler (idempotent).</summary>
    private async Task EnsureContactColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'PriceGroupId')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [PriceGroupId] INT NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'IdentityNumber')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [IdentityNumber] NVARCHAR(11) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'District')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [District] NVARCHAR(100) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'CountryCode')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [CountryCode] NVARCHAR(2) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'Mobile')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [Mobile] NVARCHAR(30) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'Website')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [Website] NVARCHAR(200) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'PostalCode')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [PostalCode] NVARCHAR(20) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'ContactPerson')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [ContactPerson] NVARCHAR(150) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'Neighborhood')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [Neighborhood] NVARCHAR(150) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID(N'[{s}].[Contact]')
                                 AND name = N'SalesRepresentativeId')
            BEGIN
                ALTER TABLE [{s}].[Contact] ADD [SalesRepresentativeId] INT NULL;
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureOrgChartTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        var sql = $"""
            IF OBJECT_ID(N'[{s}].[org_charts]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[org_charts]
                (
                    [id]         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [company_id] INT NOT NULL,
                    [name]       NVARCHAR(200) NOT NULL,
                    [is_default] BIT NOT NULL CONSTRAINT [df_org_charts_is_default] DEFAULT(0),
                    [Created] DATETIME2 NOT NULL,
                    [Updated] DATETIME2 NULL    
                );
                CREATE INDEX [ix_org_charts_company] ON [{s}].[org_charts]([company_id]);
            END;

            IF OBJECT_ID(N'[{s}].[org_chart_nodes]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[org_chart_nodes]
                (
                    [id]              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [chart_id]        UNIQUEIDENTIFIER NOT NULL,
                    [user_id]         UNIQUEIDENTIFIER NOT NULL,
                    [parent_user_id]  UNIQUEIDENTIFIER NULL,
                    [position_title]  NVARCHAR(200) NULL,
                    [sort_order]      INT NOT NULL CONSTRAINT [df_org_chart_nodes_sort] DEFAULT(0),
                    CONSTRAINT [fk_org_chart_nodes_chart]
                        FOREIGN KEY ([chart_id]) REFERENCES [{s}].[org_charts]([id]),
                    CONSTRAINT [uq_org_chart_nodes_chart_user]
                        UNIQUE ([chart_id], [user_id])
                );
                CREATE INDEX [ix_org_chart_nodes_chart] ON [{s}].[org_chart_nodes]([chart_id]);
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// FldSet — form sabit alanlarinin rehber eslestirmesi ve ayarlari.
    /// Her satir bir formun (dbo.Forms) belirli bir HTML input alanini tanimlar.
    /// GuideCode dolu ise runtime'da o alana lookup davranisi uygulanir.
    /// </summary>
    private async Task EnsureFieldSettingsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        var sql = $"""
            IF OBJECT_ID(N'[{s}].[FldSet]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[FldSet]
                (
                    [Id]         INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_FldSet] PRIMARY KEY,
                    [FormId]     INT            NOT NULL,
                    [FieldKey]   NVARCHAR(120)  NOT NULL,
                    [FieldLabel] NVARCHAR(200)  NOT NULL,
                    [GuideCode]  NVARCHAR(60)   NULL,
                    [ViewName]   NVARCHAR(128)  NULL,
                    [FilterJson] NVARCHAR(MAX)  NULL,
                    [IsRequired] BIT            NOT NULL CONSTRAINT [df_FldSet_Req] DEFAULT(0),
                    [FormatJson] NVARCHAR(MAX)  NULL,
                    [IsActive]   BIT            NOT NULL CONSTRAINT [df_FldSet_Active] DEFAULT(1),
                    [SortOrder]  INT            NOT NULL CONSTRAINT [df_FldSet_Sort] DEFAULT(0),
                    [CreatedAt]  DATETIME2(0)   NOT NULL CONSTRAINT [df_FldSet_Created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt]  DATETIME2(0)   NOT NULL CONSTRAINT [df_FldSet_Updated] DEFAULT(SYSUTCDATETIME())
                );
                CREATE UNIQUE INDEX [ux_FldSet_FormField] ON [{s}].[FldSet]([FormId], [FieldKey]);
                CREATE INDEX [ix_FldSet_Guide] ON [{s}].[FldSet]([GuideCode]) WHERE [GuideCode] IS NOT NULL;
                -- ix_FldSet_View asagidaki "Migration" blogunda EXEC sp_executesql ile olusturulur
                -- (boylece ad-hoc batch parser hem fresh-install hem mevcut tablo durumunda calisir).
            END;

            -- ── Migration: ViewName kolonu (idempotent — mevcut tablolar icin) ──
            -- EXEC sp_executesql ile deferred parsing: ALTER ile CREATE INDEX ayni batch'te
            -- olunca SQL Server parser yeni kolonu goremez — EXEC dynamic SQL ile bu sorun
            -- onlenir (her statement kendi batch'inde parse edilir).
            IF NOT EXISTS (
                SELECT 1 FROM sys.columns
                WHERE object_id = OBJECT_ID(N'[{s}].[FldSet]') AND name = N'ViewName'
            )
                EXEC sp_executesql N'ALTER TABLE [{s}].[FldSet] ADD [ViewName] NVARCHAR(128) NULL';

            -- ── Migration: ix_FldSet_View index ──
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = N'ix_FldSet_View' AND object_id = OBJECT_ID(N'[{s}].[FldSet]')
            )
                EXEC sp_executesql N'CREATE INDEX [ix_FldSet_View] ON [{s}].[FldSet]([ViewName]) WHERE [ViewName] IS NOT NULL';
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // ── Migration: GuideMas → FldSet defaults backfill (idempotent) ──
        // Sadece ViewName/FormatJson NULL ise GuideMas'tan kopyalar — kullanici override'lari korunur.
        // GuideMas tablosu PR 3'te dusurulecek; bu blok o zaman no-op olur (IF OBJECT_ID kontrolu).
        var migrateSql = $$"""
            IF OBJECT_ID(N'[{{s}}].[GuideMas]', N'U') IS NOT NULL
            BEGIN
                -- 1) ViewName backfill: GuideCode dolu, ViewName bos olanlara GuideMas.ViewName yaz
                UPDATE f
                SET    f.[ViewName] = gm.[ViewName]
                FROM   [{{s}}].[FldSet] f
                INNER  JOIN [{{s}}].[GuideMas] gm ON gm.[GuideCode] = f.[GuideCode]
                WHERE  f.[ViewName] IS NULL AND f.[GuideCode] IS NOT NULL;

                -- 2) FormatJson icine valueColumn ekle (eksikse) — GuideMas.ValueColumn'dan
                UPDATE f
                SET    f.[FormatJson] = JSON_MODIFY(
                                            ISNULL(f.[FormatJson], N'{}'),
                                            '$.valueColumn',
                                            gm.[ValueColumn]
                                        )
                FROM   [{{s}}].[FldSet] f
                INNER  JOIN [{{s}}].[GuideMas] gm ON gm.[GuideCode] = f.[GuideCode]
                WHERE  f.[GuideCode] IS NOT NULL
                  AND  ISNULL(NULLIF(LTRIM(RTRIM(gm.[ValueColumn])), N''), N'') <> N''
                  AND  (f.[FormatJson] IS NULL OR JSON_VALUE(f.[FormatJson], '$.valueColumn') IS NULL);

                -- 3) FormatJson icine displayColumn ekle (eksikse) — GuideMas.DisplayColumn'dan
                UPDATE f
                SET    f.[FormatJson] = JSON_MODIFY(
                                            ISNULL(f.[FormatJson], N'{}'),
                                            '$.displayColumn',
                                            gm.[DisplayColumn]
                                        )
                FROM   [{{s}}].[FldSet] f
                INNER  JOIN [{{s}}].[GuideMas] gm ON gm.[GuideCode] = f.[GuideCode]
                WHERE  f.[GuideCode] IS NOT NULL
                  AND  ISNULL(NULLIF(LTRIM(RTRIM(gm.[DisplayColumn])), N''), N'') <> N''
                  AND  (f.[FormatJson] IS NULL OR JSON_VALUE(f.[FormatJson], '$.displayColumn') IS NULL);
            END;
            """;

        await using var migrateCmd = connection.CreateCommand();
        migrateCmd.CommandText = migrateSql;
        await migrateCmd.ExecuteNonQueryAsync(cancellationToken);

        // ── Seed: bilinen rehber-uyumlu alanlar ──────────────────────────────
        // Her form icin: otomatik kesfedilmis istenmeyen kayitlari temizle,
        // sadece tanimli rehber-uyumlu alanlari birak/ekle.
        // cbv_Guide_Items view'inde Description sutunu yok (line 6279) — visibleColumns
        // sadece view'da var olan kolonlari icermeli yoksa search 207 hatasi verir.
        var itemsFmt = """{"visibleColumns":["MaterialCode","MaterialName"]}""";
        var seedSql = $"""
            DECLARE @FormId INT;

            -- SALES_QUOTE_LINES: sadece 'materialCode' olmali
            SELECT @FormId = [Id] FROM dbo.Forms WHERE [FormCode] = N'SALES_QUOTE_LINES';
            IF @FormId IS NOT NULL
            BEGIN
                -- Bilinen alan disindaki tum kayitlari kaldir
                DELETE FROM [{s}].[FldSet]
                WHERE [FormId] = @FormId
                  AND [FieldKey] NOT IN (N'materialCode');

                -- materialCode yoksa ekle (varsayilan: ITEMS rehberi)
                IF NOT EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'materialCode'
                )
                    INSERT INTO [{s}].[FldSet] ([FormId],[FieldKey],[FieldLabel],[GuideCode],[FormatJson],[IsActive],[SortOrder])
                    VALUES (@FormId, N'materialCode', N'Stok Kodu', N'ITEMS',
                            N'{itemsFmt}', 1, 10);
                -- varsa ama GuideCode NULL ise varsayilani ata (kullanici ozellestirilmisini koruma)
                ELSE IF EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'materialCode' AND [GuideCode] IS NULL
                )
                    UPDATE [{s}].[FldSet]
                    SET [GuideCode] = N'ITEMS',
                        [FormatJson] = N'{itemsFmt}'
                    WHERE [FormId] = @FormId AND [FieldKey] = N'materialCode';
                -- varsa, GuideCode dolu ama FormatJson NULL ise varsayilan kolon gorunurlugunu ata
                ELSE IF EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'materialCode' AND [GuideCode] IS NOT NULL AND [FormatJson] IS NULL
                )
                    UPDATE [{s}].[FldSet]
                    SET [FormatJson] = N'{itemsFmt}'
                    WHERE [FormId] = @FormId AND [FieldKey] = N'materialCode';
            END;

            -- PRODUCT_TREES: 'parentCode' (Mamul Kodu) -> ITEMS rehberi
            SELECT @FormId = [Id] FROM dbo.Forms WHERE [FormCode] = N'PRODUCT_TREES';
            IF @FormId IS NOT NULL
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'parentCode'
                )
                    INSERT INTO [{s}].[FldSet] ([FormId],[FieldKey],[FieldLabel],[GuideCode],[FormatJson],[IsActive],[SortOrder])
                    VALUES (@FormId, N'parentCode', N'Mamul Kodu', N'ITEMS',
                            N'{itemsFmt}', 1, 10);
                ELSE IF EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'parentCode' AND [GuideCode] IS NULL
                )
                    UPDATE [{s}].[FldSet]
                    SET [GuideCode] = N'ITEMS',
                        [FormatJson] = N'{itemsFmt}'
                    WHERE [FormId] = @FormId AND [FieldKey] = N'parentCode';
                ELSE IF EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'parentCode' AND [GuideCode] IS NOT NULL AND [FormatJson] IS NULL
                )
                    UPDATE [{s}].[FldSet]
                    SET [FormatJson] = N'{itemsFmt}'
                    WHERE [FormId] = @FormId AND [FieldKey] = N'parentCode';

                -- PRODUCT_TREES: 'componentCode' (Bilesen Kodu) -> ITEMS rehberi
                IF NOT EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'componentCode'
                )
                    INSERT INTO [{s}].[FldSet] ([FormId],[FieldKey],[FieldLabel],[GuideCode],[FormatJson],[IsActive],[SortOrder])
                    VALUES (@FormId, N'componentCode', N'Bilesen Kodu', N'ITEMS',
                            N'{itemsFmt}', 1, 20);
                ELSE IF EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'componentCode' AND [GuideCode] IS NULL
                )
                    UPDATE [{s}].[FldSet]
                    SET [GuideCode] = N'ITEMS',
                        [FormatJson] = N'{itemsFmt}'
                    WHERE [FormId] = @FormId AND [FieldKey] = N'componentCode';
                ELSE IF EXISTS (
                    SELECT 1 FROM [{s}].[FldSet]
                    WHERE [FormId] = @FormId AND [FieldKey] = N'componentCode' AND [GuideCode] IS NOT NULL AND [FormatJson] IS NULL
                )
                    UPDATE [{s}].[FldSet]
                    SET [FormatJson] = N'{itemsFmt}'
                    WHERE [FormId] = @FormId AND [FieldKey] = N'componentCode';
            END;
            """;
        await using var seedCmd = connection.CreateCommand();
        seedCmd.CommandText = seedSql;
        await seedCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Dinamik Raporlama Modulu: RptView / RptViewCol / RptDef / RptDefRole / RptViewRole / RptRunLog ──

    private async Task EnsureRptViewTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[RptView]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[RptView]
                (
                    [Id]            INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_RptView] PRIMARY KEY,
                    [Code]          NVARCHAR(60)  NOT NULL,
                    [Name]          NVARCHAR(200) NOT NULL,
                    [SqlObjectName] NVARCHAR(120) NOT NULL,
                    [Description]   NVARCHAR(500) NULL,
                    [IsActive]      BIT           NOT NULL CONSTRAINT [df_RptView_Active]  DEFAULT(1),
                    [CreatedAt]     DATETIME2(0)  NOT NULL CONSTRAINT [df_RptView_Created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt]     DATETIME2(0)  NOT NULL CONSTRAINT [df_RptView_Updated] DEFAULT(SYSUTCDATETIME())
                );
                CREATE UNIQUE INDEX [ux_RptView_Code] ON [{s}].[RptView]([Code]);
                CREATE UNIQUE INDEX [ux_RptView_SqlObjectName] ON [{s}].[RptView]([SqlObjectName]) WHERE [IsActive] = 1;
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureRptViewColumnTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[RptViewCol]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[RptViewCol]
                (
                    [Id]               INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_RptViewCol] PRIMARY KEY,
                    [ViewId]           INT            NOT NULL,
                    [ColName]          NVARCHAR(120)  NOT NULL,
                    [DisplayName]      NVARCHAR(200)  NOT NULL,
                    [DataType]         TINYINT        NOT NULL,
                    [IsFilterable]     BIT            NOT NULL CONSTRAINT [df_RptViewCol_Filt] DEFAULT(1),
                    [IsGroupable]      BIT            NOT NULL CONSTRAINT [df_RptViewCol_Grp]  DEFAULT(0),
                    [IsAggregatable]   BIT            NOT NULL CONSTRAINT [df_RptViewCol_Agg]  DEFAULT(0),
                    [DefaultAggregate] TINYINT        NULL,
                    [Ordinal]          INT            NOT NULL CONSTRAINT [df_RptViewCol_Ord]  DEFAULT(0),
                    [ContextBinding]   TINYINT        NULL,
                    CONSTRAINT [fk_RptViewCol_View] FOREIGN KEY ([ViewId])
                        REFERENCES [{s}].[RptView]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [ux_RptViewCol_ViewCol] ON [{s}].[RptViewCol]([ViewId],[ColName]);
                CREATE INDEX [ix_RptViewCol_View] ON [{s}].[RptViewCol]([ViewId],[Ordinal]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureRptDefinitionTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[RptDef]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[RptDef]
                (
                    [Id]          INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_RptDef] PRIMARY KEY,
                    [Code]        NVARCHAR(60)     NOT NULL,
                    [Name]        NVARCHAR(200)    NOT NULL,
                    [ViewId]      INT              NOT NULL,
                    [Category]    TINYINT          NOT NULL,
                    [ConfigJson]  NVARCHAR(MAX)    NOT NULL,
                    [OwnerUserId] UNIQUEIDENTIFIER NOT NULL,
                    [IsShared]    BIT              NOT NULL CONSTRAINT [df_RptDef_Shared]  DEFAULT(0),
                    [IsActive]    BIT              NOT NULL CONSTRAINT [df_RptDef_Active]  DEFAULT(1),
                    [CreatedAt]   DATETIME2(0)     NOT NULL CONSTRAINT [df_RptDef_Created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt]   DATETIME2(0)     NOT NULL CONSTRAINT [df_RptDef_Updated] DEFAULT(SYSUTCDATETIME()),
                    CONSTRAINT [fk_RptDef_View] FOREIGN KEY ([ViewId]) REFERENCES [{s}].[RptView]([Id])
                );
                CREATE UNIQUE INDEX [ux_RptDef_Code]  ON [{s}].[RptDef]([Code]);
                CREATE INDEX [ix_RptDef_Owner] ON [{s}].[RptDef]([OwnerUserId]) WHERE [IsActive] = 1;
                CREATE INDEX [ix_RptDef_View]  ON [{s}].[RptDef]([ViewId]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureRptDefinitionRoleTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[RptDefRole]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[RptDefRole]
                (
                    [Id]        INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_RptDefRole] PRIMARY KEY,
                    [DefId]     INT     NOT NULL,
                    [Role]      TINYINT NOT NULL,
                    [CanView]   BIT     NOT NULL CONSTRAINT [df_RptDefRole_V] DEFAULT(1),
                    [CanEdit]   BIT     NOT NULL CONSTRAINT [df_RptDefRole_E] DEFAULT(0),
                    [CanDelete] BIT     NOT NULL CONSTRAINT [df_RptDefRole_D] DEFAULT(0),
                    CONSTRAINT [fk_RptDefRole_Def] FOREIGN KEY ([DefId])
                        REFERENCES [{s}].[RptDef]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [ux_RptDefRole_DefRole] ON [{s}].[RptDefRole]([DefId],[Role]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureRptViewRoleTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[RptViewRole]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[RptViewRole]
                (
                    [Id]        INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_RptViewRole] PRIMARY KEY,
                    [ViewId]    INT     NOT NULL,
                    [Role]      TINYINT NOT NULL,
                    [CanQuery]  BIT     NOT NULL CONSTRAINT [df_RptViewRole_Q] DEFAULT(1),
                    [CanDesign] BIT     NOT NULL CONSTRAINT [df_RptViewRole_D] DEFAULT(0),
                    CONSTRAINT [fk_RptViewRole_View] FOREIGN KEY ([ViewId])
                        REFERENCES [{s}].[RptView]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [ux_RptViewRole_ViewRole] ON [{s}].[RptViewRole]([ViewId],[Role]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureRptRunLogTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[RptRunLog]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[RptRunLog]
                (
                    [Id]         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_RptRunLog] PRIMARY KEY,
                    [DefId]      INT              NULL,
                    [ViewId]     INT              NOT NULL,
                    [UserId]     UNIQUEIDENTIFIER NOT NULL,
                    [CompanyId]  INT              NULL,
                    [StartedAt]  DATETIME2(3)     NOT NULL CONSTRAINT [df_RptRunLog_Started] DEFAULT(SYSUTCDATETIME()),
                    [DurationMs] INT              NULL,
                    [RowCount]   INT              NULL,
                    [Error]      NVARCHAR(2000)   NULL,
                    [SqlHash]    BINARY(32)       NULL
                );
                CREATE INDEX [ix_RptRunLog_DefDate]  ON [{s}].[RptRunLog]([DefId],[StartedAt] DESC);
                CREATE INDEX [ix_RptRunLog_UserDate] ON [{s}].[RptRunLog]([UserId],[StartedAt] DESC);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Mevcut FastReport VIEW'larini (vw_Invoice, vw_DeliveryNote vb.) RptView registry'ye idempotent olarak seed eder.
    /// Her VIEW icin: 1) RptView row'u, 2) INFORMATION_SCHEMA'dan kesfedilen RptViewCol satirlari (varsayilan
    /// IsFilterable/IsGroupable/IsAggregatable heuristigi ile), 3) SystemAdmin icin CanQuery=CanDesign=1 RptViewRole satiri.
    /// </summary>
    private async Task SeedRptViewRegistryAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");

        // Migration: SALES_QUOTE eskiden vw_Document'a point ediyordu; yeni vw_ReportDocument kullanilacak.
        await using (var migCmd = connection.CreateCommand())
        {
            migCmd.CommandText = $"""
                UPDATE [{s}].[RptView]
                   SET [SqlObjectName] = 'vw_ReportDocument'
                 WHERE [Code] = 'SALES_QUOTE' AND [SqlObjectName] = 'vw_Document';
                """;
            await migCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var seedViews = new (string Code, string Name, string SqlObjectName, string Description)[]
        {
            ("INVOICE",       "Fatura",         "vw_Invoice",        "Satis faturasi VIEW'i"),
            ("DELIVERY_NOTE", "Irsaliye",       "vw_DeliveryNote",   "Sevk irsaliyesi VIEW'i"),
            ("PROD_BARCODE",  "Urun Barkodu",   "vw_ProductBarcode", "Urun barkod etiketi VIEW'i"),
            ("SHELF_LABEL",   "Raf Etiketi",    "vw_ShelfLabel",     "Depo raf etiketi VIEW'i"),
            ("SALES_QUOTE",   "Satis Teklifi",  "vw_ReportDocument", "Satis teklifi VIEW'i")
        };

        foreach (var (code, name, sqlObj, desc) in seedViews)
        {
            // 1) RptView row
            await using var mergeCmd = connection.CreateCommand();
            mergeCmd.CommandText = $"""
                MERGE [{s}].[RptView] AS T
                USING (SELECT @Code AS Code) AS S ON T.[Code] = S.Code
                WHEN NOT MATCHED THEN
                    INSERT ([Code],[Name],[SqlObjectName],[Description],[IsActive])
                    VALUES (@Code,@Name,@SqlObj,@Desc,1);
                SELECT [Id] FROM [{s}].[RptView] WHERE [Code] = @Code;
                """;
            mergeCmd.Parameters.AddWithValue("@Code", code);
            mergeCmd.Parameters.AddWithValue("@Name", name);
            mergeCmd.Parameters.AddWithValue("@SqlObj", sqlObj);
            mergeCmd.Parameters.AddWithValue("@Desc", desc);
            var viewIdObj = await mergeCmd.ExecuteScalarAsync(cancellationToken);
            if (viewIdObj is null) continue;
            var viewId = Convert.ToInt32(viewIdObj);

            // 2) RptViewRole: SystemAdmin tam yetki
            await using var roleCmd = connection.CreateCommand();
            roleCmd.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM [{s}].[RptViewRole] WHERE [ViewId] = @ViewId AND [Role] = 1)
                BEGIN
                    INSERT INTO [{s}].[RptViewRole] ([ViewId],[Role],[CanQuery],[CanDesign])
                    VALUES (@ViewId, 1, 1, 1);
                END;
                """;
            roleCmd.Parameters.AddWithValue("@ViewId", viewId);
            await roleCmd.ExecuteNonQueryAsync(cancellationToken);

            // 3) RptViewCol seed — sadece RptViewCol bos ise (tekrar seed bozmasin)
            await using var colCheckCmd = connection.CreateCommand();
            colCheckCmd.CommandText = $"SELECT COUNT(1) FROM [{s}].[RptViewCol] WHERE [ViewId] = @ViewId;";
            colCheckCmd.Parameters.AddWithValue("@ViewId", viewId);
            var existingCols = Convert.ToInt32(await colCheckCmd.ExecuteScalarAsync(cancellationToken));
            if (existingCols > 0) continue;

            await using var discoverCmd = connection.CreateCommand();
            discoverCmd.CommandText = """
                SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, ORDINAL_POSITION
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @ViewName
                ORDER BY ORDINAL_POSITION;
                """;
            discoverCmd.Parameters.AddWithValue("@Schema", _schema);
            discoverCmd.Parameters.AddWithValue("@ViewName", sqlObj);

            var discovered = new List<(string ColName, string SqlType, int Ordinal)>();
            await using (var rdr = await discoverCmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await rdr.ReadAsync(cancellationToken))
                {
                    discovered.Add((rdr.GetString(0), rdr.GetString(1), rdr.GetInt32(3)));
                }
            }

            foreach (var (colName, sqlType, ordinal) in discovered)
            {
                var (dataType, isNumeric, isDateish) = MapSqlType(sqlType);
                await using var insertColCmd = connection.CreateCommand();
                insertColCmd.CommandText = $"""
                    INSERT INTO [{s}].[RptViewCol]
                        ([ViewId],[ColName],[DisplayName],[DataType],[IsFilterable],[IsGroupable],
                         [IsAggregatable],[DefaultAggregate],[Ordinal],[ContextBinding])
                    VALUES
                        (@ViewId,@ColName,@Display,@DataType,@Filt,@Grp,@Agg,@Def,@Ord,@Ctx);
                    """;
                insertColCmd.Parameters.AddWithValue("@ViewId", viewId);
                insertColCmd.Parameters.AddWithValue("@ColName", colName);
                insertColCmd.Parameters.AddWithValue("@Display", colName);
                insertColCmd.Parameters.AddWithValue("@DataType", (byte)dataType);
                insertColCmd.Parameters.AddWithValue("@Filt", true);
                insertColCmd.Parameters.AddWithValue("@Grp", isDateish || (!isNumeric && dataType == 1));
                insertColCmd.Parameters.AddWithValue("@Agg", isNumeric);
                insertColCmd.Parameters.AddWithValue("@Def",
                    isNumeric ? (object)(byte)3 /* Sum */ : DBNull.Value);
                insertColCmd.Parameters.AddWithValue("@Ord", ordinal);
                insertColCmd.Parameters.AddWithValue("@Ctx", (byte)InferContextBinding(colName));
                await insertColCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    // DataType enum: 1=String, 2=Integer, 3=Decimal, 4=Date, 5=DateTime, 6=Boolean
    private static (byte DataType, bool IsNumeric, bool IsDateish) MapSqlType(string sqlType) =>
        sqlType.ToLowerInvariant() switch
        {
            "int" or "smallint" or "tinyint" or "bigint" => ((byte)2, true, false),
            "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => ((byte)3, true, false),
            "date" => ((byte)4, false, true),
            "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => ((byte)5, false, true),
            "bit" => ((byte)6, false, false),
            _ => ((byte)1, false, false)
        };

    // ContextBinding enum: 0=None, 1=CompanyId, 2=UserId, 3=OwnerUserId
    private static byte InferContextBinding(string colName) =>
        colName.ToLowerInvariant() switch
        {
            "companyid" or "company_id" => 1,
            "userid" or "user_id" => 2,
            "owneruserid" or "owner_user_id" or "createdby" or "created_by" => 3,
            _ => 0
        };

    /// <summary>
    /// Tum tablolardaki Updated kolonlarini nullable yapar. Ilk INSERT sonrasi UPDATE
    /// yapilmadigi surece NULL kalabilir; sadece guncellemede deger atanir.
    /// Idempotent — already nullable ise skip eder.
    /// </summary>
    private async Task MakeUpdatedNullableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            DECLARE @schemaName NVARCHAR(128) = N'{s}';
            DECLARE @t NVARCHAR(200);
            DECLARE @sqlAlter NVARCHAR(500);

            DECLARE updated_nullable_cursor CURSOR FAST_FORWARD FOR
                SELECT t.[name]
                FROM sys.tables t
                INNER JOIN sys.schemas sch ON sch.[schema_id] = t.[schema_id]
                INNER JOIN sys.columns c ON c.[object_id] = t.[object_id]
                WHERE sch.[name] = @schemaName
                  AND c.[name] = N'Updated'
                  AND c.[is_nullable] = 0;

            OPEN updated_nullable_cursor;
            FETCH NEXT FROM updated_nullable_cursor INTO @t;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @sqlAlter = N'ALTER TABLE ' + QUOTENAME(@schemaName) + N'.' + QUOTENAME(@t)
                              + N' ALTER COLUMN [Updated] DATETIME2 NULL';
                EXEC (@sqlAlter);
                FETCH NEXT FROM updated_nullable_cursor INTO @t;
            END;

            CLOSE updated_nullable_cursor;
            DEALLOCATE updated_nullable_cursor;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Tum tablolardaki created_at/updated_at kolonlarini Created/Updated'e donusturur (idempotent).
    /// Yeni install'larda no-op (kolonlar zaten yeni adda); mevcut DB'lerde tek seferde rename eder.
    /// String'lerde parantezsiz isim kullanilir, boylece Initializer SQL'inde [Created]/[Updated]
    /// replace_all ile bu metoda etki etmez.
    /// </summary>
    private async Task RenameLegacyTimestampColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            DECLARE @schemaName NVARCHAR(128) = N'{s}';
            DECLARE @t NVARCHAR(200);
            DECLARE @tblId INT;
            DECLARE @rn NVARCHAR(400);
            DECLARE @oldC NVARCHAR(50) = N'created_at';
            DECLARE @oldU NVARCHAR(50) = N'updated_at';

            DECLARE rename_cursor CURSOR FAST_FORWARD FOR
                SELECT t.[name] FROM sys.tables t
                INNER JOIN sys.schemas sch ON sch.[schema_id] = t.[schema_id]
                WHERE sch.[name] = @schemaName AND (
                    EXISTS (SELECT 1 FROM sys.columns c WHERE c.[object_id] = t.[object_id] AND c.[name] = @oldC)
                 OR EXISTS (SELECT 1 FROM sys.columns c WHERE c.[object_id] = t.[object_id] AND c.[name] = @oldU)
                );

            OPEN rename_cursor;
            FETCH NEXT FROM rename_cursor INTO @t;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @tblId = OBJECT_ID(QUOTENAME(@schemaName) + N'.' + QUOTENAME(@t));
                IF @tblId IS NOT NULL
                BEGIN
                    IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.[object_id] = @tblId AND c.[name] = @oldC)
                       AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.[object_id] = @tblId AND c.[name] = N'Created')
                    BEGIN
                        SET @rn = QUOTENAME(@schemaName) + N'.' + QUOTENAME(@t) + N'.' + QUOTENAME(@oldC);
                        EXEC sp_rename @objname = @rn, @newname = N'Created', @objtype = N'COLUMN';
                    END;

                    IF EXISTS (SELECT 1 FROM sys.columns c WHERE c.[object_id] = @tblId AND c.[name] = @oldU)
                       AND NOT EXISTS (SELECT 1 FROM sys.columns c WHERE c.[object_id] = @tblId AND c.[name] = N'Updated')
                    BEGIN
                        SET @rn = QUOTENAME(@schemaName) + N'.' + QUOTENAME(@t) + N'.' + QUOTENAME(@oldU);
                        EXEC sp_rename @objname = @rn, @newname = N'Updated', @objtype = N'COLUMN';
                    END;
                END;
                FETCH NEXT FROM rename_cursor INTO @t;
            END;

            CLOSE rename_cursor;
            DEALLOCATE rename_cursor;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Faz 0 — uretim modulu icin ortak altyapi tablolari:
    /// - CompanyParameter: sirket bazli form/modul parametreleri
    /// - Numerator: belge sayaclari (atomik MERGE ile)
    /// - StockMovement: stok hareketi cekirdek tablosu (Faz 4'te genisletilecek)
    /// Idempotent — her startup'ta guvenle calisir.
    /// </summary>
    private async Task EnsureProductionInfrastructureAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var schemaForSql = _schema.Replace("]", "]]");
        var sql = $@"
            -- ===== CompanyParameter =====
            IF OBJECT_ID(N'[{schemaForSql}].[CompanyParameter]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[CompanyParameter]
                (
                    [Id]         INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_CompanyParameter] PRIMARY KEY,
                    [CompanyId]  INT NOT NULL,
                    [FormCode]   VARCHAR(40) NOT NULL,
                    [ParamKey]   VARCHAR(60) NOT NULL,
                    [ParamValue] NVARCHAR(400) NULL,
                    [DataType]   TINYINT NOT NULL CONSTRAINT [df_CompanyParameter_DataType] DEFAULT(1),
                    [UpdatedAt]  DATETIME2 NULL,
                    [UpdatedBy]  INT NULL,
                    CONSTRAINT [CK_CompanyParameter_DataType] CHECK ([DataType] BETWEEN 1 AND 5)
                );
                CREATE UNIQUE INDEX [ux_CompanyParameter_Comp_Form_Key]
                    ON [{schemaForSql}].[CompanyParameter]([CompanyId], [FormCode], [ParamKey]);
            END;

            -- ===== Numerator =====
            IF OBJECT_ID(N'[{schemaForSql}].[Numerator]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Numerator]
                (
                    [Id]           INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Numerator] PRIMARY KEY,
                    [CompanyId]    INT NOT NULL,
                    [EntityType]   VARCHAR(40) NOT NULL,
                    [CurrentValue] INT NOT NULL CONSTRAINT [df_Numerator_CurrentValue] DEFAULT(0),
                    [LastResetAt]  DATETIME2 NULL
                );
                CREATE UNIQUE INDEX [ux_Numerator_Comp_Entity]
                    ON [{schemaForSql}].[Numerator]([CompanyId], [EntityType]);
            END;

            -- ===== StockMovement (cekirdek; rezervasyon/lot Faz 4'te) =====
            IF OBJECT_ID(N'[{schemaForSql}].[StockMovement]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[StockMovement]
                (
                    [Id]           INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_StockMovement] PRIMARY KEY,
                    [CompanyId]    INT NOT NULL,
                    [MovementType] TINYINT NOT NULL,
                    [ItemId]       INT NOT NULL,
                    [ConfigId]     INT NULL,
                    [Quantity]     DECIMAL(18,4) NOT NULL CONSTRAINT [df_StockMovement_Quantity] DEFAULT(0),
                    [UnitId]       INT NULL,
                    [LocationId]   INT NULL,
                    [RefType]      VARCHAR(20) NOT NULL CONSTRAINT [df_StockMovement_RefType] DEFAULT('MANUAL'),
                    [RefId]        INT NULL,
                    [RefLineId]    INT NULL,
                    [MovementDate] DATETIME2 NOT NULL CONSTRAINT [df_StockMovement_Date] DEFAULT(SYSUTCDATETIME()),
                    [BatchNo]      NVARCHAR(50) NULL,
                    [LotNo]        NVARCHAR(50) NULL,
                    [CreatedBy]    INT NULL,
                    [Created]      DATETIME2 NOT NULL CONSTRAINT [df_StockMovement_Created] DEFAULT(SYSUTCDATETIME()),
                    CONSTRAINT [CK_StockMovement_Type] CHECK ([MovementType] BETWEEN 1 AND 4)
                );
                CREATE INDEX [ix_StockMovement_Comp_Item]
                    ON [{schemaForSql}].[StockMovement]([CompanyId],[ItemId]);
                CREATE INDEX [ix_StockMovement_Ref]
                    ON [{schemaForSql}].[StockMovement]([RefType],[RefId]);
            END;

            -- ===== WorkOrder (Faz 1: 1 mamul/emir) =====
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrder]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[WorkOrder]
                (
                    [Id]                  INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_WorkOrder] PRIMARY KEY,
                    [CompanyId]           INT NOT NULL,
                    [OrderNumber]         NVARCHAR(50) NOT NULL,
                    [OrderDate]           DATETIME2 NOT NULL CONSTRAINT [df_WorkOrder_OrderDate] DEFAULT(SYSUTCDATETIME()),
                    [ItemId]              INT NOT NULL,
                    [ConfigId]            INT NULL,
                    [PlannedQuantity]     DECIMAL(18,4) NOT NULL CONSTRAINT [df_WorkOrder_PlannedQty] DEFAULT(0),
                    [ProducedQuantity]    DECIMAL(18,4) NOT NULL CONSTRAINT [df_WorkOrder_ProducedQty] DEFAULT(0),
                    [ScrapQuantity]       DECIMAL(18,4) NOT NULL CONSTRAINT [df_WorkOrder_ScrapQty] DEFAULT(0),
                    [UnitId]              INT NULL,
                    [PlannedStartDate]    DATETIME2 NULL,
                    [PlannedEndDate]      DATETIME2 NULL,
                    [ActualStartDate]     DATETIME2 NULL,
                    [ActualEndDate]       DATETIME2 NULL,
                    [Status]              TINYINT NOT NULL CONSTRAINT [df_WorkOrder_Status] DEFAULT(0),
                    [Priority]            TINYINT NOT NULL CONSTRAINT [df_WorkOrder_Priority] DEFAULT(1),
                    [AssignedUserId]      UNIQUEIDENTIFIER NULL,
                    [WarehouseLocationId] INT NULL,
                    [RevisionNo]          INT NOT NULL CONSTRAINT [df_WorkOrder_RevisionNo] DEFAULT(0),
                    [ParentWorkOrderId]   INT NULL,
                    [RevisedFromId]       INT NULL,
                    [Notes]               NVARCHAR(MAX) NULL,
                    [CreatedBy]           UNIQUEIDENTIFIER NULL,
                    [Created]             DATETIME2 NOT NULL CONSTRAINT [df_WorkOrder_Created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedBy]           UNIQUEIDENTIFIER NULL,
                    [Updated]             DATETIME2 NULL,
                    [IsActive]            BIT NOT NULL CONSTRAINT [df_WorkOrder_IsActive] DEFAULT(1),
                    CONSTRAINT [CK_WorkOrder_Status]   CHECK ([Status] BETWEEN 0 AND 5),
                    CONSTRAINT [CK_WorkOrder_Priority] CHECK ([Priority] BETWEEN 0 AND 2)
                );
                CREATE UNIQUE INDEX [ux_WorkOrder_Comp_Number]
                    ON [{schemaForSql}].[WorkOrder]([CompanyId], [OrderNumber]);
                CREATE INDEX [ix_WorkOrder_Status]
                    ON [{schemaForSql}].[WorkOrder]([CompanyId], [Status]);
                CREATE INDEX [ix_WorkOrder_Item]
                    ON [{schemaForSql}].[WorkOrder]([CompanyId], [ItemId]);
            END;

            -- ===== WorkOrderSource (Faz 1: bolme + toplama icin many-to-many) =====
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrderSource]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[WorkOrderSource]
                (
                    [Id]                INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_WorkOrderSource] PRIMARY KEY,
                    [WorkOrderId]       INT NOT NULL,
                    [SourceDocumentId]  INT NOT NULL,
                    [SourceLineId]      INT NOT NULL,
                    [AllocatedQuantity] DECIMAL(18,4) NOT NULL CONSTRAINT [df_WorkOrderSource_Qty] DEFAULT(0),
                    [Created]           DATETIME2 NOT NULL CONSTRAINT [df_WorkOrderSource_Created] DEFAULT(SYSUTCDATETIME())
                );
                CREATE INDEX [ix_WorkOrderSource_WorkOrder]
                    ON [{schemaForSql}].[WorkOrderSource]([WorkOrderId]);
                CREATE INDEX [ix_WorkOrderSource_SourceLine]
                    ON [{schemaForSql}].[WorkOrderSource]([SourceLineId]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrder]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{schemaForSql}].[WorkOrderSource]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE [name] = N'FK_WorkOrderSource_WorkOrder'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[WorkOrderSource]'))
            BEGIN
                ALTER TABLE [{schemaForSql}].[WorkOrderSource]
                WITH NOCHECK
                ADD CONSTRAINT [FK_WorkOrderSource_WorkOrder]
                    FOREIGN KEY ([WorkOrderId])
                    REFERENCES [{schemaForSql}].[WorkOrder]([Id]) ON DELETE CASCADE;
            END;

            -- ===== Migration: WorkOrder.AssignedUserId/CreatedBy/UpdatedBy INT -> UNIQUEIDENTIFIER =====
            -- User.id UNIQUEIDENTIFIER oldugu icin (int yanlis tasarim). Tablo yeni, veri yok varsayilir,
            -- bu yuzden DROP + ADD ile tip degistirilir.
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrder]', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.columns
                           WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[WorkOrder]')
                             AND name = N'AssignedUserId' AND system_type_id = TYPE_ID(N'int'))
                BEGIN
                    ALTER TABLE [{schemaForSql}].[WorkOrder] DROP COLUMN [AssignedUserId];
                    ALTER TABLE [{schemaForSql}].[WorkOrder] ADD [AssignedUserId] UNIQUEIDENTIFIER NULL;
                END;
                IF EXISTS (SELECT 1 FROM sys.columns
                           WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[WorkOrder]')
                             AND name = N'CreatedBy' AND system_type_id = TYPE_ID(N'int'))
                BEGIN
                    ALTER TABLE [{schemaForSql}].[WorkOrder] DROP COLUMN [CreatedBy];
                    ALTER TABLE [{schemaForSql}].[WorkOrder] ADD [CreatedBy] UNIQUEIDENTIFIER NULL;
                END;
                IF EXISTS (SELECT 1 FROM sys.columns
                           WHERE object_id = OBJECT_ID(N'[{schemaForSql}].[WorkOrder]')
                             AND name = N'UpdatedBy' AND system_type_id = TYPE_ID(N'int'))
                BEGIN
                    ALTER TABLE [{schemaForSql}].[WorkOrder] DROP COLUMN [UpdatedBy];
                    ALTER TABLE [{schemaForSql}].[WorkOrder] ADD [UpdatedBy] UNIQUEIDENTIFIER NULL;
                END;
            END;

            -- ===== Operation (uretim operasyon sozlugu) — Faz 3 routing temeli =====
            -- DATETIME (DATETIME2 degil) kullaniliyor: kullanici tercihi.
            IF OBJECT_ID(N'[{schemaForSql}].[Operation]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Operation]
                (
                    [Id]               INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Operation] PRIMARY KEY,
                    [CompanyId]        INT NOT NULL,
                    [Code]             NVARCHAR(50)  NOT NULL,
                    [Name]             NVARCHAR(200) NOT NULL,
                    [Description]      NVARCHAR(500) NULL,
                    [StandardDuration] DECIMAL(10,4) NULL,
                    [DurationUnit]     TINYINT NOT NULL CONSTRAINT [df_Operation_DurUnit]   DEFAULT(1),
                    [HourlyRate]       DECIMAL(18,4) NULL,
                    [SortOrder]        INT NOT NULL    CONSTRAINT [df_Operation_SortOrder]  DEFAULT(0),
                    [IsActive]         BIT NOT NULL    CONSTRAINT [df_Operation_IsActive]   DEFAULT(1),
                    [Created]          DATETIME NOT NULL CONSTRAINT [df_Operation_Created]  DEFAULT(GETUTCDATE()),
                    [Updated]          DATETIME NULL,
                    CONSTRAINT [CK_Operation_DurationUnit] CHECK ([DurationUnit] IN (1, 2))
                );
                CREATE UNIQUE INDEX [ux_Operation_Comp_Code]
                    ON [{schemaForSql}].[Operation]([CompanyId], [Code]);
            END;

            -- ===== Routing (uretim rotasi — operasyon dizisi sablonu) =====
            IF OBJECT_ID(N'[{schemaForSql}].[Routing]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Routing]
                (
                    [Id]          INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Routing] PRIMARY KEY,
                    [CompanyId]   INT NOT NULL,
                    [Code]        NVARCHAR(50)  NOT NULL,
                    [Name]        NVARCHAR(200) NOT NULL,
                    [ItemId]      INT NULL,
                    [ConfigId]    INT NULL,
                    [Description] NVARCHAR(500) NULL,
                    [IsActive]    BIT NOT NULL CONSTRAINT [df_Routing_IsActive] DEFAULT(1),
                    [Created]     DATETIME NOT NULL CONSTRAINT [df_Routing_Created] DEFAULT(GETUTCDATE()),
                    [Updated]     DATETIME NULL
                );
                CREATE UNIQUE INDEX [ux_Routing_Comp_Code]
                    ON [{schemaForSql}].[Routing]([CompanyId], [Code]);
                CREATE INDEX [ix_Routing_Comp_Item]
                    ON [{schemaForSql}].[Routing]([CompanyId], [ItemId]);
            END;

            -- ===== RoutingOperation (rota ici operasyon adimlari) =====
            IF OBJECT_ID(N'[{schemaForSql}].[RoutingOperation]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[RoutingOperation]
                (
                    [Id]               INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_RoutingOperation] PRIMARY KEY,
                    [RoutingId]        INT NOT NULL,
                    [Sequence]         INT NOT NULL,
                    [OperationId]      INT NOT NULL,
                    [MachineId]        INT NULL,
                    [OverrideDuration] DECIMAL(10,4) NULL,
                    [DurationUnit]     TINYINT NOT NULL CONSTRAINT [df_RoutingOp_DurUnit] DEFAULT(1),
                    [Notes]            NVARCHAR(500) NULL,
                    CONSTRAINT [FK_RoutingOperation_Routing]
                        FOREIGN KEY ([RoutingId])
                        REFERENCES [{schemaForSql}].[Routing]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [CK_RoutingOp_DurationUnit] CHECK ([DurationUnit] IN (1, 2))
                );
                CREATE UNIQUE INDEX [ux_RoutingOp_Routing_Seq]
                    ON [{schemaForSql}].[RoutingOperation]([RoutingId], [Sequence]);
                CREATE INDEX [ix_RoutingOp_Operation]
                    ON [{schemaForSql}].[RoutingOperation]([OperationId]);
            END;

            -- ===== OperationMachineTime (operasyon × makine × urun bazli sure) =====
            IF OBJECT_ID(N'[{schemaForSql}].[OperationMachineTime]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[OperationMachineTime]
                (
                    [Id]              INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_OpMachineTime] PRIMARY KEY,
                    [CompanyId]       INT NOT NULL,
                    [OperationId]     INT NOT NULL,
                    [MachineId]       INT NOT NULL,
                    [ItemId]          INT NULL,
                    [Quantity]        DECIMAL(18,4) NOT NULL CONSTRAINT [df_OpMT_Quantity] DEFAULT(1),
                    [DurationPerUnit] DECIMAL(10,4) NOT NULL,
                    [DurationUnit]    TINYINT NOT NULL CONSTRAINT [df_OpMT_DurUnit]   DEFAULT(1),
                    [IsActive]        BIT NOT NULL    CONSTRAINT [df_OpMT_IsActive]  DEFAULT(1),
                    [Created]         DATETIME NOT NULL CONSTRAINT [df_OpMT_Created] DEFAULT(GETUTCDATE()),
                    [Updated]         DATETIME NULL,
                    CONSTRAINT [CK_OpMT_DurationUnit] CHECK ([DurationUnit] IN (1, 2))
                );
                -- Filtered indexler: ItemId dolu (urune ozel) ve NULL (genel) durumlari ayri unique olur.
                CREATE UNIQUE INDEX [ux_OpMT_Op_Machine_Item]
                    ON [{schemaForSql}].[OperationMachineTime]([CompanyId], [OperationId], [MachineId], [ItemId])
                    WHERE [ItemId] IS NOT NULL;
                CREATE UNIQUE INDEX [ux_OpMT_Op_Machine_Generic]
                    ON [{schemaForSql}].[OperationMachineTime]([CompanyId], [OperationId], [MachineId])
                    WHERE [ItemId] IS NULL;
                CREATE INDEX [ix_OpMT_Operation]
                    ON [{schemaForSql}].[OperationMachineTime]([OperationId]);
            END;

            -- Idempotent migration: Quantity kolonu mevcut tabloda yoksa ekle.
            IF OBJECT_ID(N'[{schemaForSql}].[OperationMachineTime]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[OperationMachineTime]', N'Quantity') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[OperationMachineTime]
                    ADD [Quantity] DECIMAL(18,4) NOT NULL CONSTRAINT [df_OpMT_Quantity] DEFAULT(1);
            END;

            -- ===== Faz 3a (revize): Personnel tablosu — uretim personneli =====
            -- User'dan ayri tablo. PIN/NFC, vardiya, departman ve operator flag burada.
            -- Sistem kullanicisi olan personnellerde UserId baglar; kullanicisi olmayan
            -- (sadece tablette giren) operatorlerde NULL kalir.
            IF OBJECT_ID(N'[{schemaForSql}].[Personnel]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Personnel]
                (
                    [Id]                   INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_Personnel] PRIMARY KEY,
                    [CompanyId]            INT NOT NULL,
                    [Code]                 NVARCHAR(30) NOT NULL,
                    [FullName]             NVARCHAR(100) NOT NULL,
                    [Title]                NVARCHAR(80) NULL,
                    [Department]           NVARCHAR(80) NULL,
                    [PinCode]              NVARCHAR(10) NULL,
                    [CardNo]               NVARCHAR(50) NULL,
                    [IsProductionOperator] BIT NOT NULL CONSTRAINT [df_Personnel_IsOperator] DEFAULT(0),
                    [IsActive]             BIT NOT NULL CONSTRAINT [df_Personnel_IsActive]   DEFAULT(1),
                    [UserId]               UNIQUEIDENTIFIER NULL,
                    [Phone]                NVARCHAR(40) NULL,
                    [Email]                NVARCHAR(120) NULL,
                    [Notes]                NVARCHAR(500) NULL,
                    [Created]              DATETIME NOT NULL CONSTRAINT [df_Personnel_Created] DEFAULT(GETUTCDATE()),
                    [Updated]              DATETIME NULL
                );
                CREATE UNIQUE INDEX [ux_Personnel_Comp_Code]
                    ON [{schemaForSql}].[Personnel]([CompanyId], [Code]);
                -- CardNo sirket icinde benzersiz, bos olabilir (filtered).
                CREATE UNIQUE INDEX [ux_Personnel_Comp_CardNo]
                    ON [{schemaForSql}].[Personnel]([CompanyId], [CardNo])
                    WHERE [CardNo] IS NOT NULL;
                -- PIN sirket icinde benzersiz olmali (cakismayi engellemek icin), bos olabilir.
                CREATE UNIQUE INDEX [ux_Personnel_Comp_Pin]
                    ON [{schemaForSql}].[Personnel]([CompanyId], [PinCode])
                    WHERE [PinCode] IS NOT NULL;
                CREATE INDEX [ix_Personnel_UserId]
                    ON [{schemaForSql}].[Personnel]([UserId])
                    WHERE [UserId] IS NOT NULL;
            END;
            -- FK: Personnel.UserId → User.id (idempotent attach)
            IF OBJECT_ID(N'[{schemaForSql}].[Personnel]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE [name] = N'FK_Personnel_User'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[Personnel]'))
            BEGIN
                ALTER TABLE [{schemaForSql}].[Personnel]
                    ADD CONSTRAINT [FK_Personnel_User]
                    FOREIGN KEY ([UserId]) REFERENCES [{schemaForSql}].[User]([id]);
            END;

            -- ===== Faz 3a revize: User'dan PIN/Card/IsProductionOperator alanlarini KALDIR =====
            -- Bu alanlar Personnel'e tasindi. Idempotent rollback.
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [name] = N'ux_User_CardNo'
                  AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[User]'))
            BEGIN
                EXEC(N'DROP INDEX [ux_User_CardNo] ON [{schemaForSql}].[User];');
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[User]', N'pin_code') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[User] DROP COLUMN [pin_code];
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[User]', N'card_no') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[User] DROP COLUMN [card_no];
            END;
            -- is_production_operator default constraint ile birlikte kaldirilmali.
            IF EXISTS (
                SELECT 1 FROM sys.default_constraints
                WHERE [name] = N'df_User_IsOperator'
                  AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[User]'))
            BEGIN
                ALTER TABLE [{schemaForSql}].[User] DROP CONSTRAINT [df_User_IsOperator];
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[User]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[User]', N'is_production_operator') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[User] DROP COLUMN [is_production_operator];
            END;

            -- ===== Faz 3a: WorkOrder.RoutingId (Release auto-explosion icin) =====
            -- Mevcut WorkOrder kayitlari korunur, RoutingId nullable olarak eklenir.
            -- Yeni emirlerde service katmaninda zorunlu hale getirilir; Release sirasinda
            -- bos kalmissa Item bazinda otomatik aranir. FK Routing tablosuna baglar.
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrder]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[WorkOrder]', N'RoutingId') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[WorkOrder] ADD [RoutingId] INT NULL;
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrder]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[WorkOrder]', N'RoutingId') IS NOT NULL
               AND OBJECT_ID(N'[{schemaForSql}].[Routing]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.foreign_keys
                   WHERE [name] = N'FK_WorkOrder_Routing'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[WorkOrder]'))
            BEGIN
                ALTER TABLE [{schemaForSql}].[WorkOrder]
                    ADD CONSTRAINT [FK_WorkOrder_Routing]
                    FOREIGN KEY ([RoutingId])
                    REFERENCES [{schemaForSql}].[Routing]([Id]);
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrder]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[WorkOrder]', N'RoutingId') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [name] = N'ix_WorkOrder_RoutingId'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[WorkOrder]'))
            BEGIN
                EXEC(N'
                    CREATE INDEX [ix_WorkOrder_RoutingId]
                        ON [{schemaForSql}].[WorkOrder]([RoutingId])
                        WHERE [RoutingId] IS NOT NULL;
                ');
            END;

            -- ===== Faz 3a: WorkOrderOperation (is emri operasyon adimlari) =====
            -- Release sirasinda Routing'in operasyonlari kopyalanir, shop-floor uzerinden takip edilir.
            -- StartedBy/CompletedBy referansi Personnel.Id (INT) — User degil, ciddi bicimde uretim katindaki
            -- operatorler bu hareketleri kayda gecirir.
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrderOperation]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[WorkOrderOperation]
                (
                    [Id]                    INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_WorkOrderOperation] PRIMARY KEY,
                    [WorkOrderId]           INT NOT NULL,
                    [Sequence]              INT NOT NULL,
                    [OperationId]           INT NOT NULL,
                    [MachineId]             INT NULL,
                    [PlannedDuration]       DECIMAL(10,4) NULL,
                    [DurationUnit]          TINYINT NOT NULL CONSTRAINT [df_WOO_DurUnit] DEFAULT(1),
                    [ActualDuration]        DECIMAL(10,4) NULL,
                    [ProducedQuantity]      DECIMAL(18,4) NOT NULL CONSTRAINT [df_WOO_Produced] DEFAULT(0),
                    [ScrapQuantity]         DECIMAL(18,4) NOT NULL CONSTRAINT [df_WOO_Scrap] DEFAULT(0),
                    [Status]                TINYINT NOT NULL CONSTRAINT [df_WOO_Status] DEFAULT(0),
                    [StartedByPersonnelId]  INT NULL,
                    [StartedAt]             DATETIME NULL,
                    [CompletedByPersonnelId] INT NULL,
                    [CompletedAt]           DATETIME NULL,
                    [Notes]                 NVARCHAR(500) NULL,
                    CONSTRAINT [FK_WorkOrderOperation_WorkOrder]
                        FOREIGN KEY ([WorkOrderId])
                        REFERENCES [{schemaForSql}].[WorkOrder]([Id]) ON DELETE CASCADE,
                    CONSTRAINT [CK_WorkOrderOperation_Status]
                        CHECK ([Status] IN (0,1,2,3)),
                    CONSTRAINT [CK_WorkOrderOperation_DurUnit]
                        CHECK ([DurationUnit] IN (1,2))
                );
                CREATE UNIQUE INDEX [ux_WorkOrderOperation_WO_Seq]
                    ON [{schemaForSql}].[WorkOrderOperation]([WorkOrderId], [Sequence]);
                CREATE INDEX [ix_WorkOrderOperation_Machine]
                    ON [{schemaForSql}].[WorkOrderOperation]([MachineId])
                    WHERE [MachineId] IS NOT NULL;
                CREATE INDEX [ix_WorkOrderOperation_Status]
                    ON [{schemaForSql}].[WorkOrderOperation]([Status]);
            END;

            -- ===== Faz 3a revize: WorkOrderOperation StartedBy/CompletedBy alanlari =====
            -- User (UNIQUEIDENTIFIER) yerine Personnel (INT) referansi. Idempotent migration:
            -- eski kolonlar varsa kaldirilir (tablo yeni olustugu icin veri yok), yeni kolonlar eklenir.
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrderOperation]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[WorkOrderOperation]', N'StartedByUserId') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[WorkOrderOperation] DROP COLUMN [StartedByUserId];
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrderOperation]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[WorkOrderOperation]', N'CompletedByUserId') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[WorkOrderOperation] DROP COLUMN [CompletedByUserId];
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrderOperation]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[WorkOrderOperation]', N'StartedByPersonnelId') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[WorkOrderOperation] ADD [StartedByPersonnelId] INT NULL;
            END;
            IF OBJECT_ID(N'[{schemaForSql}].[WorkOrderOperation]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{schemaForSql}].[WorkOrderOperation]', N'CompletedByPersonnelId') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[WorkOrderOperation] ADD [CompletedByPersonnelId] INT NULL;
            END;
            ";

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureDocLayoutTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[DocLayout]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[DocLayout]
                (
                    [Id]          INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_DocLayout] PRIMARY KEY,
                    [Code]        NVARCHAR(60)      NOT NULL,
                    [Name]        NVARCHAR(200)     NOT NULL,
                    [DocType]     NVARCHAR(60)      NOT NULL,
                    [Description] NVARCHAR(500)     NULL,
                    [LayoutJson]  NVARCHAR(MAX)     NOT NULL,
                    [PageW]       DECIMAL(8,2)      NOT NULL CONSTRAINT [df_DocLayout_PageW]   DEFAULT(210),
                    [PageH]       DECIMAL(8,2)      NOT NULL CONSTRAINT [df_DocLayout_PageH]   DEFAULT(297),
                    [MarginTop]   DECIMAL(6,2)      NOT NULL CONSTRAINT [df_DocLayout_MT]      DEFAULT(10),
                    [MarginBot]   DECIMAL(6,2)      NOT NULL CONSTRAINT [df_DocLayout_MB]      DEFAULT(10),
                    [MarginLeft]  DECIMAL(6,2)      NOT NULL CONSTRAINT [df_DocLayout_ML]      DEFAULT(15),
                    [MarginRight] DECIMAL(6,2)      NOT NULL CONSTRAINT [df_DocLayout_MR]      DEFAULT(10),
                    [OwnerUserId] UNIQUEIDENTIFIER  NOT NULL,
                    [IsDefault]   BIT               NOT NULL CONSTRAINT [df_DocLayout_Default] DEFAULT(0),
                    [IsActive]    BIT               NOT NULL CONSTRAINT [df_DocLayout_Active]  DEFAULT(1),
                    [CreatedAt]   DATETIME2(0)      NOT NULL CONSTRAINT [df_DocLayout_Created] DEFAULT(SYSUTCDATETIME()),
                    [UpdatedAt]   DATETIME2(0)      NOT NULL CONSTRAINT [df_DocLayout_Updated] DEFAULT(SYSUTCDATETIME())
                );
                CREATE UNIQUE INDEX [ux_DocLayout_Code]    ON [{s}].[DocLayout]([Code]);
                CREATE INDEX [ix_DocLayout_DocType] ON [{s}].[DocLayout]([DocType]) WHERE [IsActive] = 1;
                CREATE INDEX [ix_DocLayout_Owner]   ON [{s}].[DocLayout]([OwnerUserId]) WHERE [IsActive] = 1;
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureDocLayoutDsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sql = $"""
            IF OBJECT_ID(N'[{s}].[DocLayoutDs]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[DocLayoutDs]
                (
                    [Id]          INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_DocLayoutDs] PRIMARY KEY,
                    [LayoutId]    INT               NOT NULL,
                    [Alias]       NVARCHAR(60)      NOT NULL,
                    [Role]        NVARCHAR(20)      NOT NULL,
                    [ViewId]      INT               NULL,
                    [AdHocSql]    NVARCHAR(MAX)     NULL,
                    [JoinOn]      NVARCHAR(200)     NULL,
                    [ParentAlias] NVARCHAR(60)      NULL,
                    [Ordinal]     INT               NOT NULL CONSTRAINT [df_DocLayoutDs_Ord] DEFAULT(0),
                    CONSTRAINT [fk_DocLayoutDs_Layout] FOREIGN KEY ([LayoutId])
                        REFERENCES [{s}].[DocLayout]([Id]) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX [ux_DocLayoutDs_Alias] ON [{s}].[DocLayoutDs]([LayoutId],[Alias]);
                CREATE INDEX [ix_DocLayoutDs_Layout] ON [{s}].[DocLayoutDs]([LayoutId]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
