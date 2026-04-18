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
            await MigrateItemsTableAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateItemsTableAsync completed successfully.");
            await MigrateTableRenamesAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateTableRenamesAsync completed successfully.");
            await MigrateColumnRenamesAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateColumnRenamesAsync completed successfully.");
            await MigrateScreenCodesAsync(connection, cancellationToken);
            Console.WriteLine("[DB INIT] MigrateScreenCodesAsync completed successfully.");
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
            await EnsureDesignTemplatesTableAsync(connection, cancellationToken);
            await EnsureIntegrationEventTablesAsync(connection, cancellationToken);
            await MigrateIntegrationEventsTableAsync(connection, cancellationToken);
            await EnsureIntegrationApiProfilesTableAsync(connection, cancellationToken);
            await EnsureDynamicFieldValuesTableAsync(connection, cancellationToken);
            await EnsureMaterialTaxRateColumnAsync(connection, cancellationToken);
            await EnsureDocumentTablesAsync(connection, cancellationToken);
            await EnsureDocumentAttachmentsTableAsync(connection, cancellationToken);
            await EnsureDocumentTypesTableAsync(connection, cancellationToken);
            await EnsureReportTemplatesTableAsync(connection, cancellationToken);
            await SeedDocumentTypesAsync(connection, cancellationToken);
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
            ("Items", "created_at_utc", "created_at"),
            ("Items", "updated_at_utc", "updated_at"),
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
            ("Items", "CreatedAtUtc", "CreatedAt"),
            ("Items", "UpdatedAtUtc", "UpdatedAt"),
            ("MaterialDefinitions", "CreatedAtUtc", "CreatedAt"),
            ("MaterialDefinitions", "UpdatedAtUtc", "UpdatedAt"),
            ("ProductConfiguration", "CreatedAtUtc", "CreatedAt"),
            ("ProductConfiguration", "UpdatedAtUtc", "UpdatedAt"),
            ("company", "created_at_utc", "created_at"),
            ("company", "updated_at_utc", "updated_at"),
            ("document_approvals", "action_date_utc", "action_date"),
            ("PLT_SISTEM_LOG", "occurred_at_utc", "occurred_at"),
            ("PLT_SISTEM_LOG", "OccurredAtUtc", "occurred_at"),
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
            ("Items", "created_at"),
            ("Items", "updated_at"),
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
                    [is_active] BIT NOT NULL CONSTRAINT [df_users_is_active] DEFAULT(1),
                    CONSTRAINT [fk_users_departments_department_id]
                        FOREIGN KEY ([department_id]) REFERENCES [{schemaForSql}].[Department]([id]),
                    CONSTRAINT [fk_users_users_supervisor_user_id]
                        FOREIGN KEY ([supervisor_user_id]) REFERENCES [{schemaForSql}].[User]([id])
                );

                CREATE UNIQUE INDEX [ux_users_email] ON [{schemaForSql}].[User]([email]);
                CREATE UNIQUE INDEX [ux_users_employee_code] ON [{schemaForSql}].[User]([employee_code]);
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
                    [updated_at] DATETIME2(0)     NOT NULL
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
                    [updated_at] DATETIME2 NOT NULL
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL,
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL,
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL,
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL
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

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NULL
               AND OBJECT_ID(N'[{schemaForSql}].[stock_cards]', N'U') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[stock_cards]'', ''Items'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Item]
                (
                    [id] INT NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [material_code] NVARCHAR(50) NOT NULL,
                    [material_name] NVARCHAR(160) NOT NULL,
                    [material_description] NVARCHAR(500) NULL,
                    [material_type_id] INT NULL,
                    [material_unit] NVARCHAR(40) NULL,
                    [track_combinations] BIT NOT NULL DEFAULT(0),
                    [tax_rate] DECIMAL(5,2) NOT NULL DEFAULT(20),
                    [is_active] BIT NOT NULL CONSTRAINT [df_Items_is_active] DEFAULT(1),
                    [created_at] DATETIME2 NOT NULL,
                    [created_by_user_id] INT NULL,
                    [modified_at] DATETIME2 NULL,
                    [modified_by_user_id] INT NULL,
                    [updated_at] DATETIME2 NOT NULL,
                    [created_by] NVARCHAR(100) NULL,
                    [updated_by] NVARCHAR(100) NULL,
                    [image_data] VARBINARY(MAX) NULL,
                    [image_mime_type] NVARCHAR(50) NULL
                );

                EXEC(N'
                    CREATE UNIQUE INDEX [ux_Items_material_code]
                        ON [{schemaForSql}].[Item]([material_code]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND 1 = 0
            BEGIN
                -- Legacy int->guid migration intentionally disabled.
                -- Runtime compatibility is handled in repository layer.
                SELECT 1 WHERE 1 = 0;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_code') IS NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'code') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item].[code]'', ''material_code'', ''COLUMN'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_name') IS NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'name') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item].[name]'', ''material_name'', ''COLUMN'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_code') IS NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'stock_code') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item].[stock_code]'', ''material_code'', ''COLUMN'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_name') IS NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'stock_name') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item].[stock_name]'', ''material_name'', ''COLUMN'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_unit') IS NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'unit_name') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item].[unit_name]'', ''material_unit'', ''COLUMN'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'is_active') IS NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'active') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item].[active]'', ''is_active'', ''COLUMN'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_at') IS NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_at') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item].[created_at]'', ''created_at'', ''COLUMN'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'updated_at') IS NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'updated_at') IS NOT NULL
            BEGIN
                EXEC(N'
                    EXEC sp_rename ''[{schemaForSql}].[Item].[updated_at]'', ''updated_at'', ''COLUMN'';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_code') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD [material_code] NVARCHAR(50) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_name') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD [material_name] NVARCHAR(160) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'is_active') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD [is_active] BIT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_at') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD [created_at] DATETIME2 NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'updated_at') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD [updated_at] DATETIME2 NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_code') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'code') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [material_code] = LEFT(CONVERT(NVARCHAR(50), [code]), 50)
                    WHERE [material_code] IS NULL OR LTRIM(RTRIM([material_code])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_code') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [material_code] = LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), ''-'', N''''), 50)
                    WHERE [material_code] IS NULL OR LTRIM(RTRIM([material_code])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_name') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'name') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [material_name] = LEFT(CONVERT(NVARCHAR(160), [name]), 160)
                    WHERE [material_name] IS NULL OR LTRIM(RTRIM([material_name])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_name') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_code') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [material_name] = [material_code]
                    WHERE [material_name] IS NULL OR LTRIM(RTRIM([material_name])) = N'''';
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'is_active') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [is_active] = 1
                    WHERE [is_active] IS NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_at') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_at') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [created_at] = TRY_CONVERT(DATETIME2, [created_at])
                    WHERE [created_at] IS NULL AND [created_at] IS NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_at') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [created_at] = GETDATE()
                    WHERE [created_at] IS NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'updated_at') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'updated_at') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [updated_at] = TRY_CONVERT(DATETIME2, [updated_at])
                    WHERE [updated_at] IS NULL AND [updated_at] IS NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'updated_at') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_at') IS NOT NULL
            BEGIN
                EXEC(N'
                    UPDATE [{schemaForSql}].[Item]
                    SET [updated_at] = [created_at]
                    WHERE [updated_at] IS NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_code') IS NOT NULL
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Item]
                    ALTER COLUMN [material_code] NVARCHAR(50) NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_name') IS NOT NULL
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Item]
                    ALTER COLUMN [material_name] NVARCHAR(160) NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'is_active') IS NOT NULL
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Item]
                    ALTER COLUMN [is_active] BIT NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_at') IS NOT NULL
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Item]
                    ALTER COLUMN [created_at] DATETIME2 NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'updated_at') IS NOT NULL
            BEGIN
                EXEC(N'
                    ALTER TABLE [{schemaForSql}].[Item]
                    ALTER COLUMN [updated_at] DATETIME2 NOT NULL;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'is_active') IS NOT NULL
               AND OBJECT_ID(N'[{schemaForSql}].[df_Items_is_active]', N'D') IS NULL
               AND NOT EXISTS (
                   SELECT 1 FROM sys.default_constraints dc
                   JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                   WHERE dc.parent_object_id = OBJECT_ID(N'[{schemaForSql}].[Item]') AND c.name = N'is_active')
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD CONSTRAINT [df_Items_is_active] DEFAULT(1) FOR [is_active];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_code') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_Items_material_code'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[Item]')
               )
            BEGIN
                EXEC(N'
                    IF NOT EXISTS (
                        SELECT 1 FROM [{schemaForSql}].[Item]
                        GROUP BY [material_code] HAVING COUNT(*) > 1
                    )
                    BEGIN
                        CREATE UNIQUE INDEX [ux_Items_material_code]
                            ON [{schemaForSql}].[Item]([material_code]);
                    END
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_unit') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD [material_unit] NVARCHAR(40) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'created_by') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD [created_by] NVARCHAR(100) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'updated_by') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item]
                ADD [updated_by] NVARCHAR(100) NULL;
            END;

            -- Drop obsolete columns (idempotent)
            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'is_configurable') IS NOT NULL
            BEGIN
                EXEC(N'
                    DECLARE @dfName NVARCHAR(256);
                    SELECT @dfName = dc.[name]
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
                    WHERE dc.[parent_object_id] = OBJECT_ID(N''[{schemaForSql}].[Item]'')
                      AND c.[name] = N''is_configurable'';
                    IF @dfName IS NOT NULL
                        EXEC(N''ALTER TABLE [{schemaForSql}].[Item] DROP CONSTRAINT ['' + @dfName + N'']'');
                    ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [is_configurable];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'material_type') IS NOT NULL
            BEGIN
                EXEC(N'
                    DECLARE @dfName NVARCHAR(256);
                    SELECT @dfName = dc.[name]
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
                    WHERE dc.[parent_object_id] = OBJECT_ID(N''[{schemaForSql}].[Item]'')
                      AND c.[name] = N''material_type'';
                    IF @dfName IS NOT NULL
                        EXEC(N''ALTER TABLE [{schemaForSql}].[Item] DROP CONSTRAINT ['' + @dfName + N'']'');
                    ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [material_type];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'sale_ok') IS NOT NULL
            BEGIN
                EXEC(N'
                    DECLARE @dfName NVARCHAR(256);
                    SELECT @dfName = dc.[name]
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
                    WHERE dc.[parent_object_id] = OBJECT_ID(N''[{schemaForSql}].[Item]'')
                      AND c.[name] = N''sale_ok'';
                    IF @dfName IS NOT NULL
                        EXEC(N''ALTER TABLE [{schemaForSql}].[Item] DROP CONSTRAINT ['' + @dfName + N'']'');
                    ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [sale_ok];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'purchase_ok') IS NOT NULL
            BEGIN
                EXEC(N'
                    DECLARE @dfName NVARCHAR(256);
                    SELECT @dfName = dc.[name]
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c ON c.[default_object_id] = dc.[object_id]
                    WHERE dc.[parent_object_id] = OBJECT_ID(N''[{schemaForSql}].[Item]'')
                      AND c.[name] = N''purchase_ok'';
                    IF @dfName IS NOT NULL
                        EXEC(N''ALTER TABLE [{schemaForSql}].[Item] DROP CONSTRAINT ['' + @dfName + N'']'');
                    ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [purchase_ok];
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'barcode') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [barcode];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'category_name') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [category_name];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'purchase_unit_name') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [purchase_unit_name];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'tracking_type') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [tracking_type];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'route_name') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [route_name];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'list_price') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [list_price];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'cost_price') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [cost_price];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'weight') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [weight];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'volume') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [volume];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'sales_description') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [sales_description];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'purchase_description') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [purchase_description];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'default_vendor') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [default_vendor];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'dynamic_attributes_json') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[Item] DROP COLUMN [dynamic_attributes_json];
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL
                );

                CREATE UNIQUE INDEX [ux_material_card_field_groups_group_key]
                    ON [{schemaForSql}].[FieldGroup]([group_key]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Field]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Field]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_material_card_field_settings] PRIMARY KEY,
                    [group_id] UNIQUEIDENTIFIER NULL,
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL,
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
                ADD [group_id] UNIQUEIDENTIFIER NULL;
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

            IF OBJECT_ID(N'[{schemaForSql}].[material_card_field_options]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[material_card_field_options]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_material_card_field_options] PRIMARY KEY,
                    [field_definition_id] UNIQUEIDENTIFIER NOT NULL,
                    [option_key] NVARCHAR(60) NOT NULL,
                    [option_label] NVARCHAR(160) NOT NULL,
                    [sort_order] INT NOT NULL CONSTRAINT [df_material_card_field_options_sort_order] DEFAULT(0),
                    [is_active] BIT NOT NULL CONSTRAINT [df_material_card_field_options_is_active] DEFAULT(1),
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL,
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL
                );

                CREATE UNIQUE INDEX [ux_screen_layout_definitions_screen_code]
                    ON [{schemaForSql}].[screen_layout_definitions]([screen_code]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[Feature]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[Feature]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_configuration_properties] PRIMARY KEY,
                    [code] NVARCHAR(50) NOT NULL,
                    [name] NVARCHAR(120) NOT NULL,
                    [data_type] NVARCHAR(30) NOT NULL,
                    [is_active] BIT NOT NULL CONSTRAINT [df_configuration_properties_is_active] DEFAULT(1),
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL
                );

                EXEC(N'
                    CREATE UNIQUE INDEX [ux_configuration_properties_code]
                        ON [{schemaForSql}].[Feature]([code]);
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[FeatureValue]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[FeatureValue]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_configuration_property_values] PRIMARY KEY,
                    [feature_id] UNIQUEIDENTIFIER NOT NULL,
                    [code] NVARCHAR(30) NOT NULL,
                    [description] NVARCHAR(160) NOT NULL,
                    [value] NVARCHAR(160) NOT NULL,
                    [sort_order] INT NOT NULL CONSTRAINT [df_configuration_property_values_sort_order] DEFAULT(0),
                    [is_active] BIT NOT NULL CONSTRAINT [df_configuration_property_values_is_active] DEFAULT(1),
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL,
                    CONSTRAINT [fk_configuration_property_values_property_id]
                        FOREIGN KEY ([feature_id]) REFERENCES [{schemaForSql}].[Feature]([id])
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

            IF OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[stock_card_property_mappings]
                (
                    [id] INT IDENTITY(1,1) NOT NULL CONSTRAINT [pk_stock_card_property_mappings] PRIMARY KEY,
                    [item_id] INT NOT NULL,
                    [property_id] UNIQUEIDENTIFIER NOT NULL,
                    [property_value_id] UNIQUEIDENTIFIER NULL,
                    [configuration_code] NVARCHAR(120) NULL,
                    [text_value] NVARCHAR(250) NULL,
                    [numeric_value] DECIMAL(18, 4) NULL,
                    [date_value] DATE NULL,
                    [is_active] BIT NOT NULL CONSTRAINT [df_stock_card_property_mappings_is_active] DEFAULT(1),
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL,
                    CONSTRAINT [fk_stock_card_property_mappings_item_id]
                        FOREIGN KEY ([item_id]) REFERENCES [{schemaForSql}].[Item]([id]),
                    CONSTRAINT [fk_stock_card_property_mappings_property_id]
                        FOREIGN KEY ([property_id]) REFERENCES [{schemaForSql}].[Feature]([id]),
                    CONSTRAINT [fk_stock_card_property_mappings_property_value_id]
                        FOREIGN KEY ([property_value_id]) REFERENCES [{schemaForSql}].[FeatureValue]([id])
                );

                CREATE INDEX [ix_stock_card_property_mappings_item_id]
                    ON [{schemaForSql}].[stock_card_property_mappings]([item_id]);

                CREATE INDEX [ix_stock_card_property_mappings_property_id]
                    ON [{schemaForSql}].[stock_card_property_mappings]([property_id]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.stock_card_property_mappings', N'configuration_code') IS NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[stock_card_property_mappings]
                ADD [configuration_code] NVARCHAR(120) NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_stock_card_property_mappings_item_id'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]')
               )
               AND COL_LENGTH(N'{schemaLiteral}.stock_card_property_mappings', N'item_id') IS NOT NULL
            BEGIN
                CREATE INDEX [ix_stock_card_property_mappings_item_id]
                    ON [{schemaForSql}].[stock_card_property_mappings]([item_id]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_stock_card_property_mappings_property_id'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]')
               )
               AND COL_LENGTH(N'{schemaLiteral}.stock_card_property_mappings', N'property_id') IS NOT NULL
            BEGIN
                CREATE INDEX [ix_stock_card_property_mappings_property_id]
                    ON [{schemaForSql}].[stock_card_property_mappings]([property_id]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.stock_card_property_mappings', N'item_id') IS NOT NULL
               AND OBJECT_ID(N'[{schemaForSql}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{schemaLiteral}.[Item]', N'id') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_stock_card_property_mappings_item_id'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[stock_card_property_mappings]')
               )
            BEGIN
                EXEC(N'
                    IF EXISTS (
                        SELECT 1
                        FROM sys.columns sourceColumn
                        INNER JOIN sys.columns targetColumn
                            ON targetColumn.[object_id] = OBJECT_ID(N''[{schemaForSql}].[Item]'')
                           AND targetColumn.[name] = N''id''
                        WHERE sourceColumn.[object_id] = OBJECT_ID(N''[{schemaForSql}].[stock_card_property_mappings]'')
                          AND sourceColumn.[name] = N''item_id''
                          AND sourceColumn.[system_type_id] = targetColumn.[system_type_id]
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM [{schemaForSql}].[stock_card_property_mappings] mapping
                        LEFT JOIN [{schemaForSql}].[Item] cards
                            ON cards.[id] = mapping.[item_id]
                        WHERE cards.[id] IS NULL
                    )
                    BEGIN
                        ALTER TABLE [{schemaForSql}].[stock_card_property_mappings]
                        ADD CONSTRAINT [fk_stock_card_property_mappings_item_id]
                            FOREIGN KEY ([item_id]) REFERENCES [{schemaForSql}].[Item]([id]);
                    END;
                ');
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{schemaForSql}].[ProductConfiguration]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ParentId] INT NULL,
                    [RecordType] NVARCHAR(20) NOT NULL,
                    [RecordCode] NVARCHAR(100) NOT NULL,
                    [RecordName] NVARCHAR(255) NOT NULL,
                    [DataType] NVARCHAR(20) NULL,
                    [RelatedMaterialCode] NVARCHAR(50) NULL,
                    [IsActive] BIT NOT NULL CONSTRAINT [df_ProductConfiguration_IsActive] DEFAULT(1),
                    [CreatedDate] DATETIME NOT NULL CONSTRAINT [df_ProductConfiguration_CreatedDate] DEFAULT(GETDATE())
                );
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.check_constraints
                   WHERE [name] = N'ck_ProductConfiguration_RecordType'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]')
               )
            BEGIN
                ALTER TABLE [{schemaForSql}].[ProductConfiguration]
                DROP CONSTRAINT [ck_ProductConfiguration_RecordType];
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
            BEGIN
                ALTER TABLE [{schemaForSql}].[ProductConfiguration]
                ADD CONSTRAINT [ck_ProductConfiguration_RecordType]
                    CHECK ([RecordType] IN (N'FEATURE', N'VALUE', N'CONFIG', N'FEATURE_STOCK'));
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.foreign_keys
                   WHERE [name] = N'fk_ProductConfiguration_ParentId'
                     AND [parent_object_id] = OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ProductConfiguration] child
                   LEFT JOIN [{schemaForSql}].[ProductConfiguration] parent
                       ON parent.[Id] = child.[ParentId]
                   WHERE child.[ParentId] IS NOT NULL
                     AND parent.[Id] IS NULL
               )
            BEGIN
                ALTER TABLE [{schemaForSql}].[ProductConfiguration]
                ADD CONSTRAINT [fk_ProductConfiguration_ParentId]
                    FOREIGN KEY ([ParentId]) REFERENCES [{schemaForSql}].[ProductConfiguration]([Id]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_ProductConfiguration_RecordType_ParentId'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]')
               )
            BEGIN
                CREATE INDEX [ix_ProductConfiguration_RecordType_ParentId]
                    ON [{schemaForSql}].[ProductConfiguration]([RecordType], [ParentId]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ix_ProductConfiguration_RelatedMaterialCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]')
               )
            BEGIN
                CREATE INDEX [ix_ProductConfiguration_RelatedMaterialCode]
                    ON [{schemaForSql}].[ProductConfiguration]([RelatedMaterialCode]);
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_ProductConfiguration_FeatureStock_Parent_RelatedMaterialCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ProductConfiguration]
                   WHERE [RecordType] = N'FEATURE_STOCK'
                     AND [ParentId] IS NOT NULL
                     AND [RelatedMaterialCode] IS NOT NULL
                   GROUP BY [ParentId], [RelatedMaterialCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_ProductConfiguration_FeatureStock_Parent_RelatedMaterialCode]
                    ON [{schemaForSql}].[ProductConfiguration]([ParentId], [RelatedMaterialCode])
                    WHERE [RecordType] = N'FEATURE_STOCK'
                      AND [ParentId] IS NOT NULL
                      AND [RelatedMaterialCode] IS NOT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_ProductConfiguration_Feature_RecordCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ProductConfiguration]
                   WHERE [RecordType] = N'FEATURE'
                   GROUP BY [RecordCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_ProductConfiguration_Feature_RecordCode]
                    ON [{schemaForSql}].[ProductConfiguration]([RecordCode])
                    WHERE [RecordType] = N'FEATURE';
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_ProductConfiguration_Value_Parent_RecordCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ProductConfiguration]
                   WHERE [RecordType] = N'VALUE'
                     AND [ParentId] IS NOT NULL
                   GROUP BY [ParentId], [RecordCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_ProductConfiguration_Value_Parent_RecordCode]
                    ON [{schemaForSql}].[ProductConfiguration]([ParentId], [RecordCode])
                    WHERE [RecordType] = N'VALUE' AND [ParentId] IS NOT NULL;
            END;

            IF OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]', N'U') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [name] = N'ux_ProductConfiguration_Config_Material_RecordCode'
                     AND [object_id] = OBJECT_ID(N'[{schemaForSql}].[ProductConfiguration]')
               )
               AND NOT EXISTS (
                   SELECT 1
                   FROM [{schemaForSql}].[ProductConfiguration]
                   WHERE [RecordType] = N'CONFIG'
                     AND [RelatedMaterialCode] IS NOT NULL
                   GROUP BY [RelatedMaterialCode], [RecordCode]
                   HAVING COUNT(1) > 1
               )
            BEGIN
                CREATE UNIQUE INDEX [ux_ProductConfiguration_Config_Material_RecordCode]
                    ON [{schemaForSql}].[ProductConfiguration]([RelatedMaterialCode], [RecordCode])
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL
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
                    ([name], [title], [address], [tax_office], [tax_number], [is_active], [created_at], [updated_at])
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

        var groupIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupDefinitions)
        {
            var groupId = Guid.NewGuid();
            await using var groupCommand = connection.CreateCommand();
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
                        [updated_at] = @UpdatedAt
                    WHERE [group_key] = @GroupKey;

                    SELECT [id]
                    FROM {groupsTableName}
                    WHERE [group_key] = @GroupKey;
                END
                ELSE
                BEGIN
                    INSERT INTO {groupsTableName}
                        ([id], [group_key], [group_label], [display_order], [is_active], [created_at], [updated_at])
                    VALUES
                        (@Id, @GroupKey, @GroupLabel, @DisplayOrder, 1, @CreatedAt, @UpdatedAt);

                    SELECT @Id;
                END;
                """;

            groupCommand.Parameters.Add(new SqlParameter("@Id", groupId));
            groupCommand.Parameters.Add(new SqlParameter("@GroupKey", group.Key));
            groupCommand.Parameters.Add(new SqlParameter("@GroupLabel", group.Label));
            groupCommand.Parameters.Add(new SqlParameter("@DisplayOrder", group.DisplayOrder));
            groupCommand.Parameters.Add(new SqlParameter("@CreatedAt", now));
            groupCommand.Parameters.Add(new SqlParameter("@UpdatedAt", now));

            var result = await groupCommand.ExecuteScalarAsync(cancellationToken);
            groupIds[group.Key] = result is Guid parsedId ? parsedId : groupId;
        }

        foreach (var field in MaterialCardFieldCatalog.Definitions.OrderBy(x => x.DisplayOrder))
        {
            var groupKey = "general";
            var dataType = field.DataType;

            await using var command = connection.CreateCommand();
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
                        [updated_at] = @UpdatedAt
                    WHERE [field_key] = @FieldKey;
                END
                ELSE
                BEGIN
                    INSERT INTO {fieldsTableName}
                        ([id], [group_id], [field_key], [field_label], [data_type], [is_visible], [is_required], [default_value], [display_order], [column_span], [is_system], [is_active], [created_at], [updated_at])
                    VALUES
                        (@Id, @GroupId, @FieldKey, @FieldLabel, @DataType, @IsVisible, @IsRequired, NULL, @DisplayOrder, 1, 1, 1, @CreatedAt, @UpdatedAt);
                END;
                """;

            command.Parameters.Add(new SqlParameter("@Id", Guid.NewGuid()));
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
                    DECLARE @FieldId UNIQUEIDENTIFIER;
                    SELECT @FieldId = [id] FROM {fieldsTableName} WHERE [field_key] = @FieldKey;
                    IF @FieldId IS NOT NULL
                        INSERT INTO {optionsTableName} ([id],[field_definition_id],[option_key],[option_label],[sort_order],[is_active],[created_at])
                        VALUES (NEWID(), @FieldId, @OptionKey, @OptionLabel, @SortOrder, 1, GETDATE());
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
            command.CommandText = $"""
                IF NOT EXISTS (SELECT 1 FROM {tableName} WHERE [screen_code] = @ScreenCode)
                BEGIN
                    INSERT INTO {tableName}
                        ([id], [screen_code], [layout_json], [created_at], [updated_at])
                    VALUES
                        (@Id, @ScreenCode, @LayoutJson, @CreatedAt, @UpdatedAt);
                END;
                """;

            command.Parameters.Add(new SqlParameter("@Id", Guid.NewGuid()));
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
                    [updated_at] = @UpdatedAt
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
                    [updated_at] = @UpdatedAt
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
                    [created_at]          DATETIME2 NOT NULL,
                    [updated_at]          DATETIME2 NOT NULL,
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
                    [created_at]       DATETIME2 NOT NULL,
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
            END;

            IF OBJECT_ID(N'[{s}].[note_attachments]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[note_attachments]
                (
                    [id]           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [note_id]      UNIQUEIDENTIFIER NOT NULL,
                    [file_name]    NVARCHAR(255) NOT NULL,
                    [stored_name]  NVARCHAR(255) NOT NULL,
                    [content_type] NVARCHAR(100) NULL,
                    [file_size]    BIGINT NOT NULL CONSTRAINT [df_note_attachments_file_size] DEFAULT(0),
                    [uploaded_at]  DATETIME2(0) NOT NULL,
                    [description]  NVARCHAR(500) NULL,
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
            IF OBJECT_ID(N'[{s}].[BOM]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[BOM]
                (
                    [Id]                   INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [ParentMaterialCode]   NVARCHAR(100)  NOT NULL,
                    [ConfigurationCode]    NVARCHAR(100)  NULL,
                    [Description]          NVARCHAR(500)  NULL,
                    [ImageData]            VARBINARY(MAX) NULL,
                    [ImageMimeType]        NVARCHAR(100)  NULL,
                    [CreatedAt]            DATETIME2      NOT NULL CONSTRAINT [df_product_trees_created_at]  DEFAULT GETDATE(),
                    [UpdatedAt]            DATETIME2      NOT NULL CONSTRAINT [df_product_trees_updated_at]  DEFAULT GETDATE()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[{s}].[BOM]') AND name = N'ImageFitMode')
                ALTER TABLE [{s}].[BOM] ADD [ImageFitMode] NVARCHAR(20) NULL;

            IF OBJECT_ID(N'[{s}].[BOMLine]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[BOMLine]
                (
                    [Id]                    INT             NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [BOMId]         INT             NOT NULL,
                    [ComponentMaterialCode] NVARCHAR(100)   NOT NULL,
                    [ComponentConfigCode]   NVARCHAR(100)   NULL,
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
                    [created_at]   DATETIME2(0)     NOT NULL,
                    [updated_at]   DATETIME2(0)     NOT NULL
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
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[Contact]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[Contact]
                (
                    [Id]             INT            NOT NULL IDENTITY(1,1) PRIMARY KEY,
                    [AccountType]    TINYINT        NOT NULL DEFAULT 1,
                    [AccountCode]    NVARCHAR(20)   NOT NULL,
                    [AccountTitle]   NVARCHAR(200)  NOT NULL,
                    [TaxNumber]      NVARCHAR(10)   NULL,
                    [IdentityNumber] NVARCHAR(11)   NULL,
                    [TaxOffice]      NVARCHAR(100)  NULL,
                    [Phone]          NVARCHAR(30)   NULL,
                    [Email]          NVARCHAR(200)  NULL,
                    [Address]        NVARCHAR(500)  NULL,
                    [City]           NVARCHAR(100)  NULL,
                    [IsActive]       BIT            NOT NULL DEFAULT 1,
                    [PriceGroupId]   INT            NULL,
                    [CreatedAt]      DATETIME2      NOT NULL,
                    CONSTRAINT [uq_contact_accounts_code] UNIQUE ([AccountCode])
                );

                CREATE INDEX [ix_contact_accounts_type]
                    ON [{s}].[Contact]([AccountType]);
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
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
                    [created_at]      DATETIME2(0)     NOT NULL,
                    [updated_at]      DATETIME2(0)     NOT NULL
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
                        [created_at]       DATETIME2        NOT NULL DEFAULT GETDATE(),
                        [updated_at]       DATETIME2        NOT NULL DEFAULT GETDATE()
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
                    [created_at]          DATETIME2(0)     NOT NULL,
                    [updated_at]          DATETIME2(0)     NOT NULL
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
                    [id]                UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [document_number]      NVARCHAR(15)     NOT NULL,
                    [document_date]        DATETIME2(0)     NOT NULL,
                    [valid_until]       DATETIME2(0)     NULL,
                    [contact_id]       INT              NULL,
                    [contact_name]     NVARCHAR(200)    NULL,
                    [contact_address]  NVARCHAR(500)    NULL,
                    [currency]          NVARCHAR(5)      NOT NULL DEFAULT(N'TRY'),
                    [sub_total]         DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [discount_rate]     DECIMAL(5,2)     NOT NULL DEFAULT(0),
                    [discount_amount]   DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [tax_rate]          DECIMAL(5,2)     NOT NULL DEFAULT(20),
                    [tax_amount]        DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [grand_total]       DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [payment_terms]     NVARCHAR(500)    NULL,
                    [delivery_terms]    NVARCHAR(500)    NULL,
                    [delivery_address]  NVARCHAR(500)    NULL,
                    [status]            NVARCHAR(20)     NOT NULL DEFAULT(N'Draft'),
                    [revision_no]       INT              NOT NULL DEFAULT(0),
                    [parent_document_id]   UNIQUEIDENTIFIER NULL,
                    [notes]             NVARCHAR(MAX)    NULL,
                    [created_by]        NVARCHAR(120)    NULL,
                    [created_at]        DATETIME2(0)     NOT NULL,
                    [updated_at]        DATETIME2(0)     NOT NULL,
                    [is_active]         BIT              NOT NULL DEFAULT(1)
                );
                CREATE UNIQUE INDEX [ux_sales_quotes_number] ON [{s}].[Document]([document_number]);
                CREATE INDEX [ix_sales_quotes_status] ON [{s}].[Document]([status]);
                CREATE INDEX [ix_sales_quotes_customer] ON [{s}].[Document]([contact_id]);
            END;

            -- sales_rep_id kolonu ekle (yoksa)
            IF COL_LENGTH(N'[{s}].[Document]', N'sales_rep_id') IS NULL
                ALTER TABLE [{s}].[Document] ADD [sales_rep_id] INT NULL;

            -- document_type_id FK kolonu (Phase 2 konsolidasyon): teklif/siparis/fatura tek tabloda
            IF COL_LENGTH(N'[{s}].[Document]', N'document_type_id') IS NULL
            BEGIN
                ALTER TABLE [{s}].[Document] ADD [document_type_id] UNIQUEIDENTIFIER NULL;
                -- Mevcut satirlari default 'QUOTE' tipine backfill et
                EXEC(N'UPDATE [{s}].[Document]
                       SET [document_type_id] = (SELECT TOP 1 [id] FROM [{s}].[document_types] WHERE [code] = N''QUOTE'')
                       WHERE [document_type_id] IS NULL');
            END;

            IF OBJECT_ID(N'[{s}].[DocumentLine]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[DocumentLine]
                (
                    [id]              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [document_id]        UNIQUEIDENTIFIER NOT NULL,
                    [line_no]         INT              NOT NULL DEFAULT(0),
                    [item_id]   INT              NULL,
                    [material_code]   NVARCHAR(50)     NOT NULL,
                    [material_name]   NVARCHAR(200)    NOT NULL,
                    [unit_name]       NVARCHAR(40)     NULL,
                    [quantity]        DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [unit_price]      DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [discount_rate]   DECIMAL(5,2)     NOT NULL DEFAULT(0),
                    [line_total]      DECIMAL(18,4)    NOT NULL DEFAULT(0),
                    [notes]           NVARCHAR(500)    NULL,
                    [is_active]       BIT              NOT NULL DEFAULT(1),
                    CONSTRAINT [fk_sales_quote_lines_quote] FOREIGN KEY ([document_id])
                        REFERENCES [{s}].[Document]([id])
                );
                CREATE INDEX [ix_sales_quote_lines_quote] ON [{s}].[DocumentLine]([document_id]);
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureMaterialTaxRateColumnAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[Item]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{sl}.[Item]', N'tax_rate') IS NULL
                ALTER TABLE [{s}].[Item] ADD [tax_rate] DECIMAL(5,2) NOT NULL DEFAULT(20);
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
                    [updated_at]    DATETIME2(0)     NOT NULL
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
                    [created_at] DATETIME2(0)      NOT NULL DEFAULT(GETDATE()),
                    [updated_at] DATETIME2(0)      NOT NULL DEFAULT(GETDATE())
                );
                CREATE UNIQUE INDEX [ux_currencies_code] ON [{s}].[currencies]([code]);
            END;

            IF OBJECT_ID(N'[{s}].[exchange_rates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[exchange_rates]
                (
                    [id]             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [currency_code]  NVARCHAR(5)       NOT NULL,
                    [rate_date]      DATE              NOT NULL,
                    [buying_rate]    DECIMAL(18,6)     NOT NULL,
                    [selling_rate]   DECIMAL(18,6)     NOT NULL,
                    [effective_buying_rate]  DECIMAL(18,6) NOT NULL DEFAULT(0),
                    [effective_selling_rate] DECIMAL(18,6) NOT NULL DEFAULT(0),
                    [source]         NVARCHAR(20)      NOT NULL DEFAULT(N'TCMB'),
                    [created_at]     DATETIME2(0)      NOT NULL DEFAULT(GETDATE())
                );
                CREATE UNIQUE INDEX [ux_exchange_rates_code_date]
                    ON [{s}].[exchange_rates]([currency_code], [rate_date]);
            END;

            IF OBJECT_ID(N'[{s}].[exchange_rates]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{sl}.exchange_rates', N'effective_buying_rate') IS NULL
            BEGIN
                ALTER TABLE [{s}].[exchange_rates] ADD [effective_buying_rate] DECIMAL(18,6) NOT NULL DEFAULT(0);
                ALTER TABLE [{s}].[exchange_rates] ADD [effective_selling_rate] DECIMAL(18,6) NOT NULL DEFAULT(0);
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
                    INSERT INTO [{s}].[currencies] ([code],[name],[symbol],[is_active],[created_at],[updated_at])
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
                    [created_at] DATETIME2(0)      NOT NULL DEFAULT(GETDATE()),
                    [updated_at] DATETIME2(0)      NOT NULL DEFAULT(GETDATE())
                );
                CREATE UNIQUE INDEX [ux_sales_representatives_code]
                    ON [{s}].[sales_representatives]([rep_code]);
            END;
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Sales Quote Line Details + combination_code ────────────────────────────

    private async Task EnsureDocumentLineDetailsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var sl = _schema.Replace("'", "''");

        // combination_code kolonu ekle
        var addColSql = $"""
            IF OBJECT_ID(N'[{s}].[DocumentLine]', N'U') IS NOT NULL
               AND COL_LENGTH(N'{sl}.[DocumentLine]', N'combination_code') IS NULL
                ALTER TABLE [{s}].[DocumentLine] ADD [combination_code] NVARCHAR(100) NULL;
            """;
        await using (var cmd1 = connection.CreateCommand()) { cmd1.CommandText = addColSql; await cmd1.ExecuteNonQueryAsync(cancellationToken); }

        // Detay tablosu
        var createSql = $"""
            IF OBJECT_ID(N'[{s}].[sales_quote_line_details]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[sales_quote_line_details]
                (
                    [id]             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [quote_line_id]  UNIQUEIDENTIFIER  NOT NULL,
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

    // ── Stock Unit Conversions ─────────────────────────────────────────────────

    private async Task EnsureStockUnitConversionsTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        var commandText = $"""
            IF OBJECT_ID(N'[{s}].[stock_unit_conversions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[stock_unit_conversions]
                (
                    [id]            INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [item_id] INT               NOT NULL,
                    [line_no]       INT               NOT NULL,
                    [unit_code]     NVARCHAR(20)      NOT NULL,
                    [multiplier]    DECIMAL(18,6)     NOT NULL DEFAULT(1)
                );
                CREATE UNIQUE INDEX [ux_stock_unit_conversions_card_line]
                    ON [{s}].[stock_unit_conversions]([item_id], [line_no]);
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
                    [id]            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [code]          NVARCHAR(50)     NOT NULL,
                    [name]          NVARCHAR(200)    NOT NULL,
                    [sql_view_name] NVARCHAR(128)    NULL,
                    [description]   NVARCHAR(500)    NULL,
                    [is_active]     BIT              NOT NULL DEFAULT(1),
                    [created_at]    DATETIME2(0)     NOT NULL,
                    [updated_at]    DATETIME2(0)     NOT NULL
                );
                CREATE UNIQUE INDEX [ux_document_types_code] ON [{s}].[document_types]([code]);
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
                    [id]               UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [name]             NVARCHAR(200)    NOT NULL,
                    [document_type_id] UNIQUEIDENTIFIER NOT NULL,
                    [frx_file_path]    NVARCHAR(500)    NULL,
                    [frx_content]      VARBINARY(MAX)   NULL,
                    [description]      NVARCHAR(500)    NULL,
                    [is_default]       BIT              NOT NULL DEFAULT(0),
                    [is_active]        BIT              NOT NULL DEFAULT(1),
                    [created_at]       DATETIME2(0)     NOT NULL,
                    [updated_at]       DATETIME2(0)     NOT NULL,
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
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static readonly (Guid Id, string Code, string Name, string? SqlViewName, string? Description)[] DefaultDocumentTypes =
    [
        (Guid.Parse("a1000001-0000-0000-0000-000000000001"), "fatura",        "Fatura",        "vw_Invoice",        "Satis faturasi sablonu"),
        (Guid.Parse("a1000001-0000-0000-0000-000000000002"), "irsaliye",      "Irsaliye",      "vw_DeliveryNote",   "Sevk irsaliyesi sablonu"),
        (Guid.Parse("a1000001-0000-0000-0000-000000000003"), "urun_barkodu",  "Urun Barkodu",  "vw_ProductBarcode", "Urun barkod etiketi"),
        (Guid.Parse("a1000001-0000-0000-0000-000000000004"), "raf_etiketi",   "Raf Etiketi",   "vw_ShelfLabel",     "Depo raf etiketi"),
        (Guid.Parse("a1000001-0000-0000-0000-000000000005"), "satis_teklifi", "Satis Teklifi", "vw_Document",     "Satis teklifi sablonu"),
    ];

    private async Task SeedDocumentTypesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var s = _schema.Replace("]", "]]");
        foreach (var (id, code, name, viewName, desc) in DefaultDocumentTypes)
        {
            var commandText = $"""
                IF NOT EXISTS (SELECT 1 FROM [{s}].[document_types] WHERE [code] = @Code)
                    INSERT INTO [{s}].[document_types] ([id],[code],[name],[sql_view_name],[description],[is_active],[created_at],[updated_at])
                    VALUES (@Id, @Code, @Name, @ViewName, @Description, 1, GETDATE(), GETDATE());
                """;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = commandText;
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Code", code);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@ViewName", (object?)viewName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Description", (object?)desc ?? DBNull.Value);
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
                SELECT q.[id], q.[document_number] AS [DocumentNumber],
                       q.[document_date] AS [DocumentDate], q.[contact_name] AS [CustomerName],
                       q.[contact_address] AS [CustomerAddress], q.[currency],
                       q.[sub_total] AS [SubTotal], q.[discount_rate] AS [DiscountRate],
                       q.[discount_amount] AS [DiscountAmount], q.[tax_rate] AS [TaxRate],
                       q.[tax_amount] AS [TaxAmount], q.[grand_total] AS [GrandTotal],
                       q.[payment_terms] AS [PaymentTerms], q.[notes] AS [Notes],
                       ql.[line_no] AS [LineNo], ql.[material_code] AS [MaterialCode],
                       ql.[material_name] AS [MaterialName], ql.[unit_name] AS [UnitName],
                       ql.[quantity] AS [Quantity], ql.[unit_price] AS [UnitPrice],
                       ql.[discount_rate] AS [LineDiscountRate], ql.[line_total] AS [LineTotal]
                FROM [{s}].[Document] q
                LEFT JOIN [{s}].[DocumentLine] ql ON ql.[document_id] = q.[id] AND ql.[is_active] = 1
                WHERE q.[is_active] = 1
                """),
            ("vw_DeliveryNote", $"""
                SELECT q.[id], q.[document_number] AS [DocumentNumber],
                       q.[document_date] AS [DocumentDate], q.[contact_name] AS [CustomerName],
                       q.[contact_address] AS [CustomerAddress], q.[delivery_address] AS [DeliveryAddress],
                       q.[delivery_terms] AS [DeliveryTerms],
                       ql.[line_no] AS [LineNo], ql.[material_code] AS [MaterialCode],
                       ql.[material_name] AS [MaterialName], ql.[unit_name] AS [UnitName],
                       ql.[quantity] AS [Quantity]
                FROM [{s}].[Document] q
                LEFT JOIN [{s}].[DocumentLine] ql ON ql.[document_id] = q.[id] AND ql.[is_active] = 1
                WHERE q.[is_active] = 1
                """),
            ("vw_ProductBarcode", $"""
                SELECT m.[Id] AS [id], m.[MaterialCode] AS [ProductCode],
                       m.[MaterialName] AS [ProductName],
                       m.[MaterialCode] AS [BarcodeValue],
                       N'' AS [UnitName]
                FROM [{s}].[Item] m
                WHERE m.[isactive] = 1
                """),
            ("vw_ShelfLabel", $"""
                SELECT m.[Id] AS [id], m.[MaterialCode] AS [ProductCode],
                       m.[MaterialName] AS [ProductName],
                       m.[MaterialCode] AS [BarcodeValue],
                       N'' AS [UnitName]
                FROM [{s}].[Item] m
                WHERE m.[isactive] = 1
                """),
            ("vw_Document", $"""
                SELECT q.[id], q.[document_number] AS [DocumentNumber],
                       q.[document_date] AS [DocumentDate], q.[valid_until] AS [ValidUntil],
                       q.[contact_name] AS [CustomerName], q.[contact_address] AS [CustomerAddress],
                       q.[currency], q.[sub_total] AS [SubTotal],
                       q.[discount_rate] AS [DiscountRate], q.[discount_amount] AS [DiscountAmount],
                       q.[tax_rate] AS [TaxRate], q.[tax_amount] AS [TaxAmount],
                       q.[grand_total] AS [GrandTotal],
                       q.[payment_terms] AS [PaymentTerms], q.[delivery_terms] AS [DeliveryTerms],
                       q.[delivery_address] AS [DeliveryAddress], q.[notes] AS [Notes],
                       q.[status] AS [Status], q.[revision_no] AS [RevisionNo],
                       ql.[line_no] AS [LineNo], ql.[material_code] AS [MaterialCode],
                       ql.[material_name] AS [MaterialName], ql.[unit_name] AS [UnitName],
                       ql.[quantity] AS [Quantity], ql.[unit_price] AS [UnitPrice],
                       ql.[discount_rate] AS [LineDiscountRate], ql.[line_total] AS [LineTotal]
                FROM [{s}].[Document] q
                LEFT JOIN [{s}].[DocumentLine] ql ON ql.[document_id] = q.[id] AND ql.[is_active] = 1
                WHERE q.[is_active] = 1
                """),
            ("vw_MaterialCards", $"""
                SELECT m.[Id], m.[MaterialCode], m.[MaterialName],
                       ISNULL(m.[MaterialDescription], N'') AS [MaterialDescription],
                       m.[isactive] AS [IsActive],
                       m.[CreatedDate], m.[ModifiedDate]
                FROM [{s}].[Item] m
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
            IF OBJECT_ID(N'[{s}].[PriceGroup]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[PriceGroup]
                (
                    [id]          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [group_code]  NVARCHAR(50)  NOT NULL,
                    [group_name]  NVARCHAR(150) NOT NULL,
                    [description] NVARCHAR(500) NULL,
                    [is_active]   BIT NOT NULL CONSTRAINT [df_price_groups_is_active] DEFAULT(1),
                    [created_at]  DATETIME2 NOT NULL,
                    [updated_at]  DATETIME2 NOT NULL
                );
                CREATE UNIQUE INDEX [ux_price_groups_code] ON [{s}].[PriceGroup]([group_code]);
            END;

            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[PriceList]
                (
                    [id]               INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [price_group_id]   INT NOT NULL,
                    [item_id]    INT           NULL,
                    [material_code]    NVARCHAR(60)  NOT NULL,
                    [material_name]    NVARCHAR(200) NULL,
                    [combination_code] NVARCHAR(100) NULL,
                    [combination_name] NVARCHAR(300) NULL,
                    [currency]         NVARCHAR(10)  NOT NULL CONSTRAINT [df_price_list_entries_currency] DEFAULT(N'TRY'),
                    [buying_price]     DECIMAL(18,4) NOT NULL CONSTRAINT [df_price_list_entries_buying_price]  DEFAULT(0),
                    [selling_price]    DECIMAL(18,4) NOT NULL CONSTRAINT [df_price_list_entries_selling_price] DEFAULT(0),
                    [valid_from]       DATE NOT NULL,
                    [valid_to]         DATE NULL,
                    [is_active]        BIT NOT NULL CONSTRAINT [df_price_list_entries_is_active] DEFAULT(1),
                    [created_at]       DATETIME2 NOT NULL,
                    [updated_at]       DATETIME2 NOT NULL,
                    CONSTRAINT [fk_price_list_entries_price_groups]
                        FOREIGN KEY ([price_group_id]) REFERENCES [{s}].[PriceGroup]([id])
                );
                CREATE INDEX [ix_price_list_entries_group_mat] ON [{s}].[PriceList]([price_group_id],[material_code]);
            END;

            -- Idempotent: combination_code / combination_name ekle (mevcut tablo icin)
            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'combination_code') IS NULL
            BEGIN
                ALTER TABLE [{s}].[PriceList] ADD [combination_code] NVARCHAR(100) NULL;
            END;

            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[PriceList]', N'combination_name') IS NULL
            BEGIN
                ALTER TABLE [{s}].[PriceList] ADD [combination_name] NVARCHAR(300) NULL;
            END;

            -- Composite index (grup + stok + kombinasyon + tarih)
            IF OBJECT_ID(N'[{s}].[PriceList]', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.indexes
                               WHERE object_id = OBJECT_ID(N'[{s}].[PriceList]')
                                 AND name = N'ix_price_list_entries_lookup')
            BEGIN
                CREATE INDEX [ix_price_list_entries_lookup]
                    ON [{s}].[PriceList]([price_group_id],[item_id],[combination_code],[valid_from]);
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
               SET [BaseTable] = N'dbo.Item',
                   [BaseRecordKey] = N'material_code'
             WHERE [FormCode] = N'ITEMS'
               AND ([BaseTable] IS NULL OR [BaseTable] = N''
                    OR [BaseTable] IN (N'dbo.Items', N'dbo.stock_cards'));

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
            ("SALES_QUOTE_EDIT",    "Düzenleme",                        "Satış",                "Satış Teklifi",            410),
            ("SALES_QUOTE_LINES",   "Stok Satırları",                   "Satış",                "Satış Teklifi",            415),

            // ── Üretim ───────────────────────────────────────────────────────
            ("PRODUCT_TREES",       "Ürün Ağacı",                       "Üretim",               null,                       500),

            // ── Finans ───────────────────────────────────────────────────────
            ("CONTACTS",            "Liste",                            "Finans",               "Cari Hesaplar",            600),
            ("CONTACT_EDIT",        "Düzenleme",                        "Finans",               "Cari Hesaplar",            605),

            // ── Genel Tanımlamalar ────────────────────────────────────────────
            ("SALES_REPS",          "Satış Temsilcileri",               "Genel Tanımlamalar",   null,                       700),
            ("CURRENCIES",          "Döviz Tanımlamaları",              "Genel Tanımlamalar",   null,                       710),
            ("LOCATIONS",           "Lokasyon Tanımlamaları",           "Genel Tanımlamalar",   null,                       720),
            ("MEASURE_UNITS",       "Ölçü Birimi Tanımlama",            "Genel Tanımlamalar",   null,                       730),
            ("MATERIAL_GROUPS",     "Grup Tanımlamaları",               "Genel Tanımlamalar",   null,                       740),
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
        var v1 = $"""
            CREATE OR ALTER VIEW [{s}].[cbv_Guide_Contacts] AS
            SELECT
                [Id],
                CAST([AccountCode]  AS NVARCHAR(100)) AS [AccountCode],
                CAST([AccountTitle] AS NVARCHAR(300)) AS [AccountTitle],
                CAST([Phone]        AS NVARCHAR(50))  AS [Phone],
                CAST([City]         AS NVARCHAR(100)) AS [City],
                CAST([TaxNumber]    AS NVARCHAR(50))  AS [TaxNumber]
            FROM [{s}].[Contact]
            WHERE [IsActive] = 1;
            """;
        await using (var cmd2 = connection.CreateCommand())
        {
            cmd2.CommandText = v1;
            await cmd2.ExecuteNonQueryAsync(cancellationToken);
        }

        // View 2: Malzeme Karti Rehberi (Items → snake_case → PascalCase alias)
        var v2 = $"""
            CREATE OR ALTER VIEW [{s}].[cbv_Guide_Items] AS
            SELECT
                [id]                                          AS [Id],
                CAST([material_code]        AS NVARCHAR(100)) AS [MaterialCode],
                CAST([material_name]        AS NVARCHAR(300)) AS [MaterialName],
                CAST([material_description] AS NVARCHAR(500)) AS [Description]
            FROM [{s}].[Item]
            WHERE [is_active] = 1;
            """;
        await using (var cmd3 = connection.CreateCommand())
        {
            cmd3.CommandText = v2;
            await cmd3.ExecuteNonQueryAsync(cancellationToken);
        }

        // View 3: Satis Teklifi Rehberi (sales_quotes → snake_case → PascalCase alias)
        var v3 = $"""
            CREATE OR ALTER VIEW [{s}].[cbv_Guide_Documents] AS
            SELECT
                CAST([document_number]  AS NVARCHAR(100)) AS [DocumentNumber],
                CAST([contact_name] AS NVARCHAR(300)) AS [CustomerName],
                CAST([document_date]    AS DATE)          AS [DocumentDate],
                CAST([grand_total]   AS DECIMAL(18,2)) AS [GrandTotal],
                CAST([status]        AS NVARCHAR(30))  AS [Status]
            FROM [{s}].[Document]
            WHERE [is_active] = 1;
            """;
        await using (var cmd4 = connection.CreateCommand())
        {
            cmd4.CommandText = v3;
            await cmd4.ExecuteNonQueryAsync(cancellationToken);
        }

        // 3) Seed satirlar — idempotent
        var seedSql = $"""
            IF NOT EXISTS (SELECT 1 FROM [{s}].[GuideMas] WHERE [GuideCode] = N'CUSTOMERS')
                INSERT INTO [{s}].[GuideMas] ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],[GridColumnsJson],[DefaultSortColumn])
                VALUES (N'CUSTOMERS', N'Cari Hesap Rehberi', N'cbv_Guide_Contacts',
                        N'AccountCode', N'AccountTitle',
                        N'["AccountCode","AccountTitle","Phone","City","TaxNumber"]',
                        N'AccountCode');
            ELSE
                UPDATE [{s}].[GuideMas]
                SET [GridColumnsJson] = N'["AccountCode","AccountTitle","Phone","City","TaxNumber"]'
                WHERE [GuideCode] = N'CUSTOMERS';

            IF NOT EXISTS (SELECT 1 FROM [{s}].[GuideMas] WHERE [GuideCode] = N'ITEMS')
                INSERT INTO [{s}].[GuideMas] ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],[GridColumnsJson],[DefaultSortColumn])
                VALUES (N'ITEMS', N'Malzeme Karti Rehberi', N'cbv_Guide_Items',
                        N'MaterialCode', N'MaterialName',
                        N'["Id","MaterialCode","MaterialName","Description"]',
                        N'MaterialCode');
            ELSE
                UPDATE [{s}].[GuideMas]
                SET [GridColumnsJson] = N'["Id","MaterialCode","MaterialName","Description"]'
                WHERE [GuideCode] = N'ITEMS'
                  AND [GridColumnsJson] NOT LIKE N'%"Id"%';

            IF NOT EXISTS (SELECT 1 FROM [{s}].[GuideMas] WHERE [GuideCode] = N'SALES_QUOTES')
                INSERT INTO [{s}].[GuideMas] ([GuideCode],[GuideLabel],[ViewName],[ValueColumn],[DisplayColumn],[GridColumnsJson],[DefaultSortColumn])
                VALUES (N'SALES_QUOTES', N'Satis Teklifi Rehberi', N'cbv_Guide_Documents',
                        N'DocumentNumber', N'CustomerName',
                        N'["DocumentNumber","CustomerName","DocumentDate","GrandTotal","Status"]',
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
                    [created_at]                        DATETIME2 NOT NULL DEFAULT(GETDATE()),
                    [updated_at]                        DATETIME2 NOT NULL DEFAULT(GETDATE()),
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
                    [created_at]                        DATETIME2 NOT NULL DEFAULT(GETDATE()),
                    [updated_at]                        DATETIME2 NOT NULL DEFAULT(GETDATE()),
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
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL AND COL_LENGTH(N'{sl}.{li}', N'material_description') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [material_description] NVARCHAR(500) NULL;
            IF OBJECT_ID(N'[{s}].[{li}]', N'U') IS NOT NULL AND COL_LENGTH(N'{sl}.{li}', N'material_type_id') IS NULL
                ALTER TABLE [{s}].[{li}] ADD [material_type_id] INT NULL;
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
            // stock_cards (legacy, eski Items'tan onceki) → Item  (once Items'a donusturulmus olabilir, ikinci blok onu da yakalar)
            ("stock_cards",                 "Item"),
            ("Items",                       "Item"),
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
        };

        var sb = new System.Text.StringBuilder();
        foreach (var (table, oldCol, newCol) in columnRenames)
        {
            sb.AppendLine($"""
                IF OBJECT_ID(N'[{s}].[{table}]', N'U') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[{table}]', N'{oldCol}') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[{table}]', N'{newCol}') IS NULL
                BEGIN
                    EXEC sp_rename N'[{s}].[{table}].[{oldCol}]', N'{newCol}', N'COLUMN';
                END;
                """);
        }

        // PascalCase kolon rename: MaterialGroupMappings.StockCardId -> ItemId
        sb.AppendLine($"""
            IF OBJECT_ID(N'[{s}].[MaterialGroupMappings]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[MaterialGroupMappings]', N'StockCardId') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[MaterialGroupMappings]', N'ItemId') IS NULL
            BEGIN
                EXEC sp_rename N'[{s}].[MaterialGroupMappings].[StockCardId]', N'ItemId', N'COLUMN';
            END;
            """);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync(cancellationToken);
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
                            SET [screen_code] = N'{newSlug}', [updated_at] = SYSUTCDATETIME()
                            WHERE [screen_code] = N'{oldSlug}';
                    END;
                """);
        }
        sb.AppendLine("END;");

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sb.ToString();
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
                    [created_at] DATETIME2 NOT NULL,
                    [updated_at] DATETIME2 NOT NULL
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
            END;
            """;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // ── Seed: bilinen rehber-uyumlu alanlar ──────────────────────────────
        // Her form icin: otomatik kesfedilmis istenmeyen kayitlari temizle,
        // sadece tanimli rehber-uyumlu alanlari birak/ekle.
        var itemsFmt = """{"visibleColumns":["MaterialCode","MaterialName","Description"]}""";
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

        var seedViews = new (string Code, string Name, string SqlObjectName, string Description)[]
        {
            ("INVOICE",       "Fatura",         "vw_Invoice",        "Satis faturasi VIEW'i"),
            ("DELIVERY_NOTE", "Irsaliye",       "vw_DeliveryNote",   "Sevk irsaliyesi VIEW'i"),
            ("PROD_BARCODE",  "Urun Barkodu",   "vw_ProductBarcode", "Urun barkod etiketi VIEW'i"),
            ("SHELF_LABEL",   "Raf Etiketi",    "vw_ShelfLabel",     "Depo raf etiketi VIEW'i"),
            ("SALES_QUOTE",   "Satis Teklifi",  "vw_Document",     "Satis teklifi VIEW'i")
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
}
