using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using System.Text;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlLogisticsConfigurationRepository : ILogisticsConfigurationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly string _schema;
    private readonly string _stockCardsTableName;
    private readonly string _materialCardFieldGroupsTableName;
    private readonly string _materialCardFieldSettingsTableName;
    private readonly string _materialCardFieldOptionsTableName;
    private readonly string _propertiesTableName;
    private readonly string _propertyValuesTableName;
    private readonly string _itemFeatureMappingsTableName;
    private readonly string _itemConfigurationTableName;
    private readonly string _warehouseLocationsTableName;
    private readonly string _measureUnitDefinitionsTableName;
    private readonly string _productTreesTableName;
    private readonly string _productTreeLinesTableName;
    private readonly string _materialGroupsTableName;
    private readonly string _materialGroupMappingsTableName;
    private readonly string _itemUnitsTableName;
    private readonly string _itemLocationsTableName;
    private readonly string _locationTypesTableName;
    private readonly string _machinesTableName;

    public SqlLogisticsConfigurationRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options,
        IDataVisibilityFilter dvFilter)
    {
        _connectionFactory = connectionFactory;
        _dvFilter = dvFilter;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _stockCardsTableName = $"[{_schema}].[Items]";
        _materialCardFieldGroupsTableName = $"[{_schema}].[FieldGroup]";
        _materialCardFieldSettingsTableName = $"[{_schema}].[Field]";
        _materialCardFieldOptionsTableName = $"[{_schema}].[MaterialCardFieldOption]";
        _propertiesTableName = $"[{_schema}].[ItemFeature]";
        _propertyValuesTableName = $"[{_schema}].[FeatureValue]";
        _itemFeatureMappingsTableName = $"[{_schema}].[ItemFeatureMappings]";
        _itemConfigurationTableName = $"[{_schema}].[ItemConfiguration]";
        _warehouseLocationsTableName = $"[{_schema}].[Location]";
        _measureUnitDefinitionsTableName = $"[{_schema}].[Unit]";
        _productTreesTableName = $"[{_schema}].[BOM]";
        _productTreeLinesTableName = $"[{_schema}].[BOMLine]";
        _materialGroupsTableName    = $"[{_schema}].[MaterialGroups]";
        _materialGroupMappingsTableName = $"[{_schema}].[MaterialGroupMappings]";
        _itemUnitsTableName = $"[{_schema}].[ItemUnits]";
        _itemLocationsTableName = $"[{_schema}].[ItemLocation]";
        _locationTypesTableName = $"[{_schema}].[LocationType]";
        _machinesTableName = $"[{_schema}].[Machine]";
    }

    public async Task<IReadOnlyCollection<Item>> GetItemsAsync(CancellationToken cancellationToken)
    {
        var stockCards = new List<Item>();
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var dv = await _dvFilter.BuildAsync(FormCodes.MaterialCardEdit, "sc", "Id", cancellationToken);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT [Id],
                   [Code],
                   [Name],
                   [IsActive],
                   [Created],
                   [Updated],
                   [Combinations],
                   ISNULL([TaxRate], 20) AS [TaxRate],
                   [TypeId],
                   [UnitId],
                   [CompanyId],
                   ISNULL([TrackingType], 'None') AS [TrackingType],
                   ISNULL([MinStock], 0) AS [MinStock],
                   ISNULL([AutoSerial], 0) AS [AutoSerial]
            FROM {_stockCardsTableName} sc
            WHERE sc.[CompanyId] = @CompanyId
            {dv.Sql}
            ORDER BY sc.[Code];
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        foreach (var prm in dv.Parameters) command.Parameters.Add(new SqlParameter(prm.Name, prm.Value));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var stockCard = new Item
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Created = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                Updated = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                Combinations = !reader.IsDBNull(6) && reader.GetBoolean(6),
                TaxRate = reader.IsDBNull(7) ? 20m : reader.GetDecimal(7),
                TypeId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                UnitId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                CompanyId = reader.GetInt32(10),
                TrackingType = reader.IsDBNull(11) ? "None" : reader.GetString(11),
                MinStock = reader.IsDBNull(12) ? 0m : reader.GetDecimal(12),
                AutoSerial = !reader.IsDBNull(13) && reader.GetBoolean(13)
            };

            if (!reader.GetBoolean(3))
            {
                stockCard.Deactivate();
            }

            stockCards.Add(stockCard);
        }

        return stockCards;
    }

    public async Task<IReadOnlyCollection<Item>> GetItemsByIdsAsync(
        IEnumerable<int> ids, CancellationToken cancellationToken)
    {
        // Dedup + filter zero/negative. Bos liste -> bos sonuc (DB hit yok).
        var idList = (ids ?? Array.Empty<int>())
            .Where(i => i > 0)
            .Distinct()
            .ToArray();
        if (idList.Length == 0) return Array.Empty<Item>();

        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // IN (@id0, @id1, ...) — parametre adlari guvenli (sirayla uretilir).
        var paramNames = idList.Select((_, i) => "@id" + i).ToArray();
        command.CommandText = $"""
            SELECT [Id],
                   [Code],
                   [Name],
                   [IsActive],
                   [Created],
                   [Updated],
                   [Combinations],
                   ISNULL([TaxRate], 20) AS [TaxRate],
                   [TypeId],
                   [UnitId],
                   [CompanyId]
            FROM {_stockCardsTableName}
            WHERE [CompanyId] = @CompanyId
              AND [Id] IN ({string.Join(",", paramNames)});
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        for (var i = 0; i < idList.Length; i++)
            command.Parameters.Add(new SqlParameter(paramNames[i], idList[i]));

        var stockCards = new List<Item>(idList.Length);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var stockCard = new Item
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Created = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                Updated = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                Combinations = !reader.IsDBNull(6) && reader.GetBoolean(6),
                TaxRate = reader.IsDBNull(7) ? 20m : reader.GetDecimal(7),
                TypeId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                UnitId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                CompanyId = reader.GetInt32(10)
            };
            if (!reader.GetBoolean(3)) stockCard.Deactivate();
            stockCards.Add(stockCard);
        }
        return stockCards;
    }

    public async Task<(IReadOnlyCollection<Item> Items, int TotalCount)> GetItemsPagedAsync(
        string? search, int offset, int pageSize, CancellationToken cancellationToken, string? groupCode = null)
    {
        var items = new List<Item>();
        var totalCount = 0;
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var dv = await _dvFilter.BuildAsync(FormCodes.MaterialCardEdit, "i", "Id", cancellationToken);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // [Items]'in alias'i lazim cunku EXISTS subquery'de Items.Id'ye disaridan referans verecegiz.
        var where = "WHERE i.[CompanyId] = @CompanyId AND i.[IsActive] = 1";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND (i.[Code] LIKE @Search OR i.[Name] LIKE @Search)";
        if (!string.IsNullOrWhiteSpace(groupCode))
            where += $" AND EXISTS (SELECT 1 FROM {_materialGroupMappingsTableName} mgm WHERE mgm.[ItemId] = i.[Id] AND mgm.[GroupCode] = @GroupCode)";
        where += dv.Sql;

        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT i.[Id], i.[Code], i.[Name],
                       i.[IsActive], i.[Created], i.[Updated],
                       i.[Combinations], ISNULL(i.[TaxRate], 20) AS [TaxRate], i.[TypeId],
                       i.[UnitId], i.[CompanyId],
                       COUNT(*) OVER() AS [_TotalCount]
                FROM {_stockCardsTableName} i
                {where}
                ORDER BY i.[Code]
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;

            cmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.Add(new SqlParameter("@Search", $"%{search}%"));
            if (!string.IsNullOrWhiteSpace(groupCode))
                cmd.Parameters.Add(new SqlParameter("@GroupCode", groupCode));
            cmd.Parameters.Add(new SqlParameter("@Offset", offset));
            cmd.Parameters.Add(new SqlParameter("@PageSize", pageSize));
            foreach (var prm in dv.Parameters) cmd.Parameters.Add(new SqlParameter(prm.Name, prm.Value));

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var card = new Item
                {
                    Id = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    Created = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                    Updated = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                    Combinations = !reader.IsDBNull(6) && reader.GetBoolean(6),
                    TaxRate = reader.IsDBNull(7) ? 20m : reader.GetDecimal(7),
                    TypeId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    UnitId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                    CompanyId = reader.GetInt32(10)
                };
                if (!reader.GetBoolean(3)) card.Deactivate();
                items.Add(card);
                if (totalCount == 0) totalCount = reader.GetInt32(11);
            }
        }

        if (items.Count == 0)
        {
            await using var countCmd = connection.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM {_stockCardsTableName} i {where};";
            countCmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            if (!string.IsNullOrWhiteSpace(search))
                countCmd.Parameters.Add(new SqlParameter("@Search", $"%{search}%"));
            if (!string.IsNullOrWhiteSpace(groupCode))
                countCmd.Parameters.Add(new SqlParameter("@GroupCode", groupCode));
            foreach (var prm in dv.Parameters) countCmd.Parameters.Add(new SqlParameter(prm.Name, prm.Value));
            totalCount = (int)(await countCmd.ExecuteScalarAsync(cancellationToken))!;
        }

        return (items, totalCount);
    }

    public async Task<IReadOnlyCollection<FieldGroup>> GetFieldGroupsAsync(
        CancellationToken cancellationToken)
    {
        var groups = new List<FieldGroup>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [GroupKey], [GroupLabel], [DisplayOrder], [IsActive], [Created], [Updated], [ScreenCode], [LayerKey]
            FROM {_materialCardFieldGroupsTableName}
            ORDER BY [DisplayOrder], [GroupLabel];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var group = new FieldGroup
            {
                Id = reader.GetGuid(0),
                GroupKey = reader.GetString(1),
                GroupLabel = reader.GetString(2),
                DisplayOrder = reader.GetInt32(3),
                Created = reader.GetFieldValue<DateTime>(5),
                ScreenCode = reader.IsDBNull(7) ? "MaterialCards" : reader.GetString(7),
                LayerKey = reader.IsDBNull(8) ? null : reader.GetString(8),
            };

            if (!reader.GetBoolean(4))
            {
                group.SetActive(false);
            }

            groups.Add(group);
        }

        return groups;
    }

    public async Task<IReadOnlyCollection<MaterialCardDynamicFieldDefinition>> GetMaterialCardDynamicFieldDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        var definitions = new List<MaterialCardDynamicFieldDefinition>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id],
                   [GroupId],
                   [FieldKey],
                   [FieldLabel],
                   [DataType],
                   [IsVisible],
                   [IsRequired],
                   [DefaultValue],
                   [DisplayOrder],
                   [ColumnSpan],
                   [IsSystem],
                   [IsActive],
                   [Created],
                   [Updated],
                   [ScreenCode],
                   [LayerKey]
            FROM {_materialCardFieldSettingsTableName}
            ORDER BY [DisplayOrder], [FieldLabel];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var definition = new MaterialCardDynamicFieldDefinition
            {
                Id = reader.GetGuid(0),
                GroupId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                FieldKey = reader.GetString(2),
                FieldLabel = reader.GetString(3),
                DataType = ParseMaterialCardDynamicFieldDataType(reader.IsDBNull(4) ? null : reader.GetString(4)),
                IsVisible = reader.GetBoolean(5),
                IsRequired = reader.GetBoolean(6),
                DefaultValue = reader.IsDBNull(7) ? null : reader.GetString(7),
                DisplayOrder = reader.GetInt32(8),
                ColumnSpan = reader.IsDBNull(9) ? 1 : reader.GetInt32(9),
                IsSystem = !reader.IsDBNull(10) && reader.GetBoolean(10),
                Created = reader.GetFieldValue<DateTime>(12),
                ScreenCode = reader.IsDBNull(14) ? "MaterialCards" : reader.GetString(14),
                LayerKey = reader.IsDBNull(15) ? null : reader.GetString(15),
            };

            if (!reader.GetBoolean(11))
            {
                definition.SetActive(false);
            }

            definitions.Add(definition);
        }

        return definitions;
    }

    public async Task<IReadOnlyCollection<MaterialCardFieldOption>> GetMaterialCardFieldOptionsAsync(
        CancellationToken cancellationToken)
    {
        var options = new List<MaterialCardFieldOption>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [FieldDefinitionId], [OptionKey], [OptionLabel], [SortOrder], [IsActive], [Created], [Updated]
            FROM {_materialCardFieldOptionsTableName}
            ORDER BY [FieldDefinitionId], [SortOrder], [OptionLabel];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var option = new MaterialCardFieldOption
            {
                Id = reader.GetGuid(0),
                FieldDefinitionId = reader.GetGuid(1),
                OptionKey = reader.GetString(2),
                OptionLabel = reader.GetString(3),
                SortOrder = reader.GetInt32(4),
                CreatedAt = reader.GetFieldValue<DateTime>(6)
            };

            if (!reader.GetBoolean(5))
            {
                option.SetActive(false);
            }

            options.Add(option);
        }

        return options;
    }

    public async Task UpsertFieldGroupAsync(FieldGroup group, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF EXISTS (SELECT 1 FROM {_materialCardFieldGroupsTableName} WHERE [id] = @Id)
            BEGIN
                UPDATE {_materialCardFieldGroupsTableName}
                SET
                    [ScreenCode] = @ScreenCode,
                    [LayerKey] = @LayerKey,
                    [GroupKey] = @GroupKey,
                    [GroupLabel] = @GroupLabel,
                    [DisplayOrder] = @DisplayOrder,
                    [IsActive] = @IsActive,
                    [Updated] = @UpdatedAt
                WHERE [id] = @Id;
            END
            ELSE
            BEGIN
                INSERT INTO {_materialCardFieldGroupsTableName}
                    ([id], [ScreenCode], [LayerKey], [GroupKey], [GroupLabel], [DisplayOrder], [IsActive], [Created], [Updated])
                VALUES
                    (@Id, @ScreenCode, @LayerKey, @GroupKey, @GroupLabel, @DisplayOrder, @IsActive, @CreatedAt, @UpdatedAt);
            END;
            """;

        command.Parameters.Add(new SqlParameter("@Id", group.Id));
        command.Parameters.Add(new SqlParameter("@ScreenCode", group.ScreenCode ?? "MaterialCards"));
        command.Parameters.Add(new SqlParameter("@LayerKey", (object?)group.LayerKey ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@GroupKey", group.GroupKey));
        command.Parameters.Add(new SqlParameter("@GroupLabel", group.GroupLabel));
        command.Parameters.Add(new SqlParameter("@DisplayOrder", group.DisplayOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", group.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", group.Created));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertMaterialCardDynamicFieldAsync(
        MaterialCardDynamicFieldDefinition field,
        IReadOnlyCollection<MaterialCardFieldOption> options,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var fieldCommand = connection.CreateCommand())
        {
            fieldCommand.Transaction = transaction;
            fieldCommand.CommandText = $"""
                IF EXISTS (SELECT 1 FROM {_materialCardFieldSettingsTableName} WHERE [id] = @Id)
                BEGIN
                    UPDATE {_materialCardFieldSettingsTableName}
                    SET
                        [ScreenCode] = @ScreenCode,
                        [LayerKey] = @LayerKey,
                        [GroupId] = @GroupId,
                        [FieldKey] = @FieldKey,
                        [FieldLabel] = @FieldLabel,
                        [DataType] = @DataType,
                        [IsVisible] = @IsVisible,
                        [IsRequired] = @IsRequired,
                        [DefaultValue] = @DefaultValue,
                        [DisplayOrder] = @DisplayOrder,
                        [ColumnSpan] = @ColumnSpan,
                        [IsSystem] = @IsSystem,
                        [IsActive] = @IsActive,
                        [Updated] = @UpdatedAt
                    WHERE [id] = @Id;
                END
                ELSE
                BEGIN
                    INSERT INTO {_materialCardFieldSettingsTableName}
                        ([id], [ScreenCode], [LayerKey], [GroupId], [FieldKey], [FieldLabel], [DataType], [IsVisible], [IsRequired], [DefaultValue], [DisplayOrder], [ColumnSpan], [IsSystem], [IsActive], [Created], [Updated])
                    VALUES
                        (@Id, @ScreenCode, @LayerKey, @GroupId, @FieldKey, @FieldLabel, @DataType, @IsVisible, @IsRequired, @DefaultValue, @DisplayOrder, @ColumnSpan, @IsSystem, @IsActive, @CreatedAt, @UpdatedAt);
                END;
                """;

            fieldCommand.Parameters.Add(new SqlParameter("@Id", field.Id));
            fieldCommand.Parameters.Add(new SqlParameter("@ScreenCode", field.ScreenCode ?? "MaterialCards"));
            fieldCommand.Parameters.Add(new SqlParameter("@LayerKey", (object?)field.LayerKey ?? DBNull.Value));
            fieldCommand.Parameters.Add(new SqlParameter("@GroupId", (object?)field.GroupId ?? DBNull.Value));
            fieldCommand.Parameters.Add(new SqlParameter("@FieldKey", field.FieldKey));
            fieldCommand.Parameters.Add(new SqlParameter("@FieldLabel", field.FieldLabel));
            fieldCommand.Parameters.Add(new SqlParameter("@DataType", ToMaterialCardDynamicFieldDataTypeValue(field.DataType)));
            fieldCommand.Parameters.Add(new SqlParameter("@IsVisible", field.IsVisible));
            fieldCommand.Parameters.Add(new SqlParameter("@IsRequired", field.IsRequired));
            fieldCommand.Parameters.Add(new SqlParameter("@DefaultValue", (object?)field.DefaultValue ?? DBNull.Value));
            fieldCommand.Parameters.Add(new SqlParameter("@DisplayOrder", field.DisplayOrder));
            fieldCommand.Parameters.Add(new SqlParameter("@ColumnSpan", field.ColumnSpan));
            fieldCommand.Parameters.Add(new SqlParameter("@IsSystem", field.IsSystem));
            fieldCommand.Parameters.Add(new SqlParameter("@IsActive", field.IsActive));
            fieldCommand.Parameters.Add(new SqlParameter("@CreatedAt", field.Created));
            fieldCommand.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

            await fieldCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var option in options)
        {
            await using var optionCommand = connection.CreateCommand();
            optionCommand.Transaction = transaction;
            optionCommand.CommandText = $"""
                IF EXISTS (SELECT 1 FROM {_materialCardFieldOptionsTableName} WHERE [id] = @Id)
                BEGIN
                    UPDATE {_materialCardFieldOptionsTableName}
                    SET
                        [OptionKey] = @OptionKey,
                        [OptionLabel] = @OptionLabel,
                        [SortOrder] = @SortOrder,
                        [IsActive] = @IsActive,
                        [Updated] = @UpdatedAt
                    WHERE [id] = @Id;
                END
                ELSE
                BEGIN
                    INSERT INTO {_materialCardFieldOptionsTableName}
                        ([id], [FieldDefinitionId], [OptionKey], [OptionLabel], [SortOrder], [IsActive], [Created], [Updated])
                    VALUES
                        (@Id, @FieldDefinitionId, @OptionKey, @OptionLabel, @SortOrder, @IsActive, @CreatedAt, @UpdatedAt);
                END;
                """;

            optionCommand.Parameters.Add(new SqlParameter("@Id", option.Id));
            optionCommand.Parameters.Add(new SqlParameter("@FieldDefinitionId", option.FieldDefinitionId));
            optionCommand.Parameters.Add(new SqlParameter("@OptionKey", option.OptionKey));
            optionCommand.Parameters.Add(new SqlParameter("@OptionLabel", option.OptionLabel));
            optionCommand.Parameters.Add(new SqlParameter("@SortOrder", option.SortOrder));
            optionCommand.Parameters.Add(new SqlParameter("@IsActive", option.IsActive));
            optionCommand.Parameters.Add(new SqlParameter("@CreatedAt", option.CreatedAt));
            optionCommand.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

            await optionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Field>> GetFieldsAsync(
        CancellationToken cancellationToken)
    {
        var settings = new List<Field>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [id], [FieldKey], [FieldLabel], [IsVisible], [IsRequired], [DisplayOrder], [Created], [Updated]
            FROM {_materialCardFieldSettingsTableName}
            ORDER BY [DisplayOrder], [FieldLabel];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            settings.Add(new Field
            {
                Id = reader.GetGuid(0),
                FieldKey = reader.GetString(1),
                FieldLabel = reader.GetString(2),
                IsVisible = reader.GetBoolean(3),
                IsRequired = reader.GetBoolean(4),
                DisplayOrder = reader.GetInt32(5),
                Created = reader.GetFieldValue<DateTime>(6),
                Updated = reader.GetFieldValue<DateTime>(7)
            });
        }

        return settings;
    }

    public async Task UpsertFieldsAsync(
        IReadOnlyCollection<Field> settings,
        CancellationToken cancellationToken)
    {
        if (settings.Count == 0)
        {
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var setting in settings)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                IF EXISTS (SELECT 1 FROM {_materialCardFieldSettingsTableName} WHERE [FieldKey] = @FieldKey)
                BEGIN
                    UPDATE {_materialCardFieldSettingsTableName}
                    SET
                        [FieldLabel] = @FieldLabel,
                        [IsVisible] = @IsVisible,
                        [IsRequired] = @IsRequired,
                        [DisplayOrder] = @DisplayOrder,
                        [Updated] = @UpdatedAt
                    WHERE [FieldKey] = @FieldKey;
                END
                ELSE
                BEGIN
                    INSERT INTO {_materialCardFieldSettingsTableName}
                        ([id], [FieldKey], [FieldLabel], [IsVisible], [IsRequired], [DisplayOrder], [Created], [Updated])
                    VALUES
                        (@Id, @FieldKey, @FieldLabel, @IsVisible, @IsRequired, @DisplayOrder, @CreatedAt, @UpdatedAt);
                END;
                """;

            command.Parameters.Add(new SqlParameter("@Id", setting.Id));
            command.Parameters.Add(new SqlParameter("@FieldKey", setting.FieldKey));
            command.Parameters.Add(new SqlParameter("@FieldLabel", setting.FieldLabel));
            command.Parameters.Add(new SqlParameter("@IsVisible", setting.IsVisible));
            command.Parameters.Add(new SqlParameter("@IsRequired", setting.IsRequired));
            command.Parameters.Add(new SqlParameter("@DisplayOrder", setting.DisplayOrder));
            command.Parameters.Add(new SqlParameter("@CreatedAt", setting.Created));
            command.Parameters.Add(new SqlParameter("@UpdatedAt", setting.Updated));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ItemFeature>> GetPropertiesAsync(CancellationToken cancellationToken)
    {
        var properties = new List<ItemFeature>();

        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 2026-07-05: kolon adi [CreatedAt] degil [Created] — initializer tabloyu
        // [Created] ile kurar (satir 2350); eski ad taze kurulumda "Invalid column
        // name" 500'u uretiyordu (GetSnapshotAsync -> malzeme karti kaydi dahil).
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [Name], [DataType], [UnitOfMeasure], [VisibleInDesign], [IsActive], [Created]
            FROM {_propertiesTableName}
            WHERE [CompanyId] = @CompanyId
            ORDER BY [Name];
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dataTypeRaw = reader.GetString(3);
            if (!Enum.TryParse(dataTypeRaw, true, out ConfigurationFieldDataType dataType) ||
                !Enum.IsDefined(dataType))
            {
                dataType = ConfigurationFieldDataType.Text;
            }

            var property = new ItemFeature
            {
                Id = reader.GetInt32(0),
                CompanyId = reader.GetInt32(1),
                Name = reader.GetString(2),
                DataType = dataType,
                UnitOfMeasure = reader.IsDBNull(4) ? null : reader.GetString(4),
                VisibleInDesign = reader.GetBoolean(5),
                CreatedAt = reader.GetFieldValue<DateTime>(7)
            };

            if (!reader.GetBoolean(6))
            {
                property.Deactivate();
            }

            properties.Add(property);
        }

        return properties;
    }

    public async Task<IReadOnlyCollection<FeatureValue>> GetPropertyValuesAsync(CancellationToken cancellationToken)
    {
        var values = new List<FeatureValue>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [FeatureId], [Code], [Description], [Value], [SortOrder], [IsActive], [Created]
            FROM {_propertyValuesTableName}
            ORDER BY [FeatureId], [SortOrder], [Code];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = new FeatureValue
            {
                Id = reader.GetInt32(0),
                PropertyId = reader.GetInt32(1),
                Code = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Value = reader.GetString(4),
                SortOrder = reader.GetInt32(5),
                CreatedAt = reader.GetFieldValue<DateTime>(7)
            };

            if (!reader.GetBoolean(6))
            {
                value.Deactivate();
            }

            values.Add(value);
        }

        return values;
    }

    public async Task<IReadOnlyCollection<ItemFeatureMapping>> GetStockPropertyMappingsAsync(CancellationToken cancellationToken)
    {
        var mappings = new List<ItemFeatureMapping>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 2026-07-05: [CreatedAt] → [Created] (MigrateColumnRenamesAsync ile senkron)
        command.CommandText = $"""
            SELECT [Id], [ItemId], [FeatureId], [FeatureValueId], [IsActive], [Created],
                   ISNULL([PrintDescriptionInDesign], 1) AS [PrintDescriptionInDesign]
            FROM {_itemFeatureMappingsTableName}
            ORDER BY [Created] DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var mapping = new ItemFeatureMapping
            {
                Id = reader.GetInt32(0),
                ItemId = reader.GetInt32(1),
                FeatureId = reader.GetInt32(2),
                FeatureValueId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                CreatedAt = reader.GetFieldValue<DateTime>(5),
                PrintDescriptionInDesign = reader.GetBoolean(6),
            };

            if (!reader.GetBoolean(4))
            {
                mapping.Deactivate();
            }

            mappings.Add(mapping);
        }

        return mappings;
    }

    public async Task<IReadOnlyCollection<ItemConfiguration>> GetItemConfigurationsAsync(
        CancellationToken cancellationToken)
    {
        var records = new List<ItemConfiguration>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 2026-07-05: [Created] → [Created] (MigrateColumnRenamesAsync ile senkron)
        command.CommandText = $"""
            SELECT [Id], [ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [Created], [VisibleInDesign]
            FROM {_itemConfigurationTableName}
            ORDER BY [RecordType], [ParentId], [RecordCode], [Id];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ItemConfiguration
            {
                Id = reader.GetInt32(0),
                ParentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                RecordType = reader.GetString(2),
                RecordCode = reader.GetString(3),
                RecordName = reader.GetString(4),
                DataType = reader.IsDBNull(5) ? null : reader.GetString(5),
                RelatedMaterialCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                IsActive = reader.GetBoolean(7),
                CreatedDate = reader.GetDateTime(8),
                VisibleInDesign = reader.GetBoolean(9)
            });
        }

        return records;
    }

    public async Task<IReadOnlyCollection<Unit>> GetUnitsAsync(
        CancellationToken cancellationToken)
    {
        var definitions = new List<Unit>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // IntlCode kolonu varsa dahil et
        var useIntlCode = true;
        command.CommandText = $"""
            SELECT [Id], [Code], [Name], [SortOrder], [IsActive], [IntlCode]
            FROM {_measureUnitDefinitionsTableName}
            ORDER BY [SortOrder], [Code];
            """;

        SqlDataReader reader;
        try
        {
            reader = await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (SqlException)
        {
            // IntlCode kolonu henuz yoksa fallback
            useIntlCode = false;
            await using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = $"""
                SELECT [Id], [Code], [Name], [SortOrder], [IsActive]
                FROM {_measureUnitDefinitionsTableName}
                ORDER BY [SortOrder], [Code];
                """;
            reader = await cmd2.ExecuteReaderAsync(cancellationToken);
        }

        await using (reader)
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                definitions.Add(new Unit
                {
                    Id = reader.GetInt32(0),
                    Code = reader.GetString(1),
                    Name = reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                    IsActive = reader.GetBoolean(4),
                    IntlCode = useIntlCode && !reader.IsDBNull(5) ? reader.GetString(5) : null,
                });
            }
        }

        return definitions;
    }

    public async Task<(int Id, string Code)> AddProductFeatureAsync(
        string name,
        string dataType,
        bool isActive,
        string? unitOfMeasure,
        bool visibleInDesign,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DECLARE @NextNo INT;
            DECLARE @GeneratedCode NVARCHAR(100);

            SELECT @NextNo = ISNULL(MAX(TRY_CAST(RIGHT([RecordCode], 3) AS INT)), 0) + 1
            FROM {_itemConfigurationTableName} WITH (UPDLOCK, HOLDLOCK)
            WHERE [RecordType] = N'FEATURE'
              AND [RecordCode] LIKE N'OZ[0-9][0-9][0-9]';

            SET @GeneratedCode = N'OZ' + RIGHT(N'000' + CAST(@NextNo AS NVARCHAR(3)), 3);

            INSERT INTO {_itemConfigurationTableName}
                ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [VisibleInDesign], [Created])
            VALUES
                (NULL, N'FEATURE', @GeneratedCode, @RecordName, @DataType, @UnitOfMeasure, @IsActive, @VisibleInDesign, GETDATE());

            SELECT CAST(SCOPE_IDENTITY() AS INT) AS [Id], @GeneratedCode AS [Code];

            COMMIT TRANSACTION;
            """;

        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@DataType", dataType));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));
        command.Parameters.Add(new SqlParameter("@UnitOfMeasure", (object?)unitOfMeasure ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@VisibleInDesign", visibleInDesign));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Ozellik kaydi olusturulamadi.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }

    public async Task<(int Id, string Code)> AddProductValueAsync(
        int featureId,
        string name,
        bool isActive,
        string? aciklama,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DECLARE @NextNo INT;
            DECLARE @GeneratedCode NVARCHAR(100);

            SELECT @NextNo = ISNULL(MAX(TRY_CAST(RIGHT([RecordCode], 3) AS INT)), 0) + 1
            FROM {_itemConfigurationTableName} WITH (UPDLOCK, HOLDLOCK)
            WHERE [RecordType] = N'VALUE'
              AND [ParentId] = @FeatureId
              AND [RecordCode] LIKE N'DG[0-9][0-9][0-9]';

            SET @GeneratedCode = N'DG' + RIGHT(N'000' + CAST(@NextNo AS NVARCHAR(3)), 3);

            INSERT INTO {_itemConfigurationTableName}
                ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [Created])
            VALUES
                (@FeatureId, N'VALUE', @GeneratedCode, @RecordName, NULL, @Aciklama, @IsActive, GETDATE());

            SELECT CAST(SCOPE_IDENTITY() AS INT) AS [Id], @GeneratedCode AS [Code];

            COMMIT TRANSACTION;
            """;

        command.Parameters.Add(new SqlParameter("@FeatureId", featureId));
        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));
        command.Parameters.Add(new SqlParameter("@Aciklama", (object?)aciklama ?? DBNull.Value));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Deger kaydi olusturulamadi.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }

    public async Task<(int Id, string Code)> AddProductConfigAsync(
        int valueId,
        string relatedMaterialCode,
        string name,
        bool isActive,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DECLARE @NextNo INT;
            DECLARE @GeneratedCode NVARCHAR(100);

            SELECT @NextNo = ISNULL(MAX(TRY_CAST(RIGHT([RecordCode], 3) AS INT)), 0) + 1
            FROM {_itemConfigurationTableName} WITH (UPDLOCK, HOLDLOCK)
            WHERE [RecordType] = N'CONFIG'
              AND [RelatedMaterialCode] = @RelatedMaterialCode
              AND LEFT([RecordCode], LEN(@RelatedMaterialCode) + 1) = @RelatedMaterialCode + N'-';

            SET @GeneratedCode = @RelatedMaterialCode + N'-' + RIGHT(N'000' + CAST(@NextNo AS NVARCHAR(3)), 3);

            INSERT INTO {_itemConfigurationTableName}
                ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [Created])
            VALUES
                (@ValueId, N'CONFIG', @GeneratedCode, @RecordName, NULL, @RelatedMaterialCode, @IsActive, GETDATE());

            SELECT CAST(SCOPE_IDENTITY() AS INT) AS [Id], @GeneratedCode AS [Code];

            COMMIT TRANSACTION;
            """;

        command.Parameters.Add(new SqlParameter("@ValueId", valueId));
        command.Parameters.Add(new SqlParameter("@RelatedMaterialCode", relatedMaterialCode));
        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Yapilandirma kaydi olusturulamadi.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }

    public async Task<(int Id, string Code)> AddProductConfigurationCombinationAsync(
        string relatedMaterialCode,
        string configName,
        IReadOnlyCollection<int> valueIds,
        bool isActive,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder();
        sql.AppendLine("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;");
        sql.AppendLine("BEGIN TRANSACTION;");
        sql.AppendLine("DECLARE @NextNo INT;");
        sql.AppendLine("DECLARE @GeneratedCode NVARCHAR(100);");
        // Global sayac: CMB + 12 haneli sifir-dolgulu sira no (toplam 15 karakter)
        sql.AppendLine($"SELECT @NextNo = ISNULL(MAX(TRY_CAST(SUBSTRING([RecordCode], 4, 12) AS INT)), 0) + 1 FROM {_itemConfigurationTableName} WITH (UPDLOCK, HOLDLOCK) WHERE [RecordType] = N'CONFIG' AND LEFT([RecordCode], 3) = N'CMB' AND LEN([RecordCode]) = 15;");
        sql.AppendLine("SET @GeneratedCode = N'CMB' + RIGHT(REPLICATE(N'0', 12) + CAST(@NextNo AS NVARCHAR(12)), 12);");
        sql.AppendLine($"INSERT INTO {_itemConfigurationTableName} ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [Created]) VALUES (NULL, N'CONFIG', @GeneratedCode, @RecordName, NULL, @RelatedMaterialCode, @IsActive, GETDATE());");
        sql.AppendLine("DECLARE @ConfigId INT = SCOPE_IDENTITY();");

        var pIndex = 0;
        foreach (var valId in valueIds)
        {
            sql.AppendLine($"INSERT INTO {_itemConfigurationTableName} ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [Created]) VALUES (@ConfigId, N'CONFIG', CAST(NEWID() AS NVARCHAR(100)), CAST(@ValueId{pIndex} AS NVARCHAR(255)), NULL, @RelatedMaterialCode, 1, GETDATE());");
            command.Parameters.Add(new SqlParameter($"@ValueId{pIndex}", valId));
            pIndex++;
        }

        sql.AppendLine("SELECT @ConfigId AS [Id], @GeneratedCode AS [Code];");
        sql.AppendLine("COMMIT TRANSACTION;");

        command.CommandText = sql.ToString();
        command.Parameters.Add(new SqlParameter("@RelatedMaterialCode", relatedMaterialCode));
        command.Parameters.Add(new SqlParameter("@RecordName", configName));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Kombinasyon kaydi olusturulamadi.");
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }

    public async Task ReplaceProductFeatureStockLinksAsync(
        int featureId,
        string[] stockCodes,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var sql = new StringBuilder();
        sql.AppendLine("BEGIN TRANSACTION;");
        sql.AppendLine($"""
            DELETE FROM {_itemConfigurationTableName}
            WHERE [RecordType] = N'FEATURE_STOCK'
              AND [ParentId] = @FeatureId;
            """);

        command.Parameters.Add(new SqlParameter("@FeatureId", featureId));

        var normalizedCodes = stockCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < normalizedCodes.Length; index++)
        {
            sql.AppendLine($"""
                INSERT INTO {_itemConfigurationTableName}
                    ([ParentId], [RecordType], [RecordCode], [RecordName], [DataType], [RelatedMaterialCode], [IsActive], [Created])
                VALUES
                    (@FeatureId, N'FEATURE_STOCK', @StockCode{index}, @StockCode{index}, NULL, @StockCode{index}, 1, GETDATE());
                """);

            command.Parameters.Add(new SqlParameter($"@StockCode{index}", normalizedCodes[index]));
        }

        sql.AppendLine("COMMIT TRANSACTION;");
        command.CommandText = sql.ToString();

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Bir stok kartina bagli ozellik listesini ItemFeatureMappings tablosunda gunceller.
    /// Her feature icin: 1 header satiri (FeatureValueId NULL) + 0..N value satiri.
    /// stockCode → ItemId cozumlemesi yapildiktan sonra ItemFeatureMappings WHERE [ItemId] = @ItemId tum satirlari silinir, yeni liste eklenir.
    /// </summary>
    public async Task ReplaceStockFeatureLinksAsync(
        string stockCode,
        (int FeatureId, bool PrintDescriptionInDesign, int[] AllowedValueIds)[] items,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
            throw new ArgumentException("Stok kodu zorunludur.", nameof(stockCode));
        var normalizedCode = stockCode.Trim().ToUpperInvariant();
        var companyId = _connectionFactory.ResolveCurrentCompanyId();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // stockCode → ItemId (sirket bazli)
        int itemId;
        await using (var lookupCmd = connection.CreateCommand())
        {
            lookupCmd.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            lookupCmd.CommandText = $"""
                SELECT TOP (1) [Id] FROM {_stockCardsTableName}
                WHERE UPPER(LTRIM(RTRIM([Code]))) = @StockCode
                  AND [CompanyId] = @CompanyId
                  AND [IsActive] = 1;
                """;
            lookupCmd.Parameters.Add(new SqlParameter("@StockCode", normalizedCode));
            var raw = await lookupCmd.ExecuteScalarAsync(cancellationToken);
            if (raw is null || raw == DBNull.Value)
                throw new ArgumentException($"Aktif stok kartı bulunamadı: {normalizedCode}");
            itemId = Convert.ToInt32(raw);
        }

        // Bu stok'un TUM linklerini sil (header + value-restriction)
        await using (var delCmd = connection.CreateCommand())
        {
            delCmd.CommandText = $"DELETE FROM {_itemFeatureMappingsTableName} WHERE [ItemId] = @ItemId;";
            delCmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var now = DateTime.Now;
        var distinctItems = (items ?? Array.Empty<(int, bool, int[])>())
            .Where(x => x.FeatureId > 0)
            .GroupBy(x => x.FeatureId)
            .Select(g => (
                FeatureId: g.Key,
                PrintDescriptionInDesign: g.Last().PrintDescriptionInDesign,
                AllowedValueIds: (g.Last().AllowedValueIds ?? Array.Empty<int>())
                    .Where(v => v > 0).Distinct().ToArray()))
            .ToArray();

        foreach (var f in distinctItems)
        {
            // Header row (FeatureValueId NULL)
            await using (var headerCmd = connection.CreateCommand())
            {
                headerCmd.CommandText = $"""
                    INSERT INTO {_itemFeatureMappingsTableName}
                        ([ItemId], [FeatureId], [FeatureValueId], [PrintDescriptionInDesign], [IsActive], [Created], [Updated])
                    VALUES
                        (@ItemId, @FeatureId, NULL, @PrintDesc, 1, @CreatedAt, @UpdatedAt);
                    """;
                headerCmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
                headerCmd.Parameters.Add(new SqlParameter("@FeatureId", f.FeatureId));
                headerCmd.Parameters.Add(new SqlParameter("@PrintDesc", f.PrintDescriptionInDesign));
                headerCmd.Parameters.Add(new SqlParameter("@CreatedAt", now));
                headerCmd.Parameters.Add(new SqlParameter("@UpdatedAt", now));
                await headerCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Value-restriction rows
            foreach (var valueId in f.AllowedValueIds)
            {
                await using var valCmd = connection.CreateCommand();
                valCmd.CommandText = $"""
                    INSERT INTO {_itemFeatureMappingsTableName}
                        ([ItemId], [FeatureId], [FeatureValueId], [PrintDescriptionInDesign], [IsActive], [Created], [Updated])
                    VALUES
                        (@ItemId, @FeatureId, @FeatureValueId, @PrintDesc, 1, @CreatedAt, @UpdatedAt);
                    """;
                valCmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
                valCmd.Parameters.Add(new SqlParameter("@FeatureId", f.FeatureId));
                valCmd.Parameters.Add(new SqlParameter("@FeatureValueId", valueId));
                valCmd.Parameters.Add(new SqlParameter("@PrintDesc", f.PrintDescriptionInDesign));
                valCmd.Parameters.Add(new SqlParameter("@CreatedAt", now));
                valCmd.Parameters.Add(new SqlParameter("@UpdatedAt", now));
                await valCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    public async Task UpdateProductFeatureAsync(int id, string name, string dataType, string? unitOfMeasure, bool visibleInDesign, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_itemConfigurationTableName}
            SET [RecordName]          = @RecordName,
                [DataType]            = @DataType,
                [RelatedMaterialCode] = @UnitOfMeasure,
                [VisibleInDesign]     = @VisibleInDesign
            WHERE [Id] = @Id
              AND [RecordType] = N'FEATURE';
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@DataType", dataType));
        command.Parameters.Add(new SqlParameter("@UnitOfMeasure", (object?)unitOfMeasure ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@VisibleInDesign", visibleInDesign));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteProductFeatureAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            BEGIN TRANSACTION;

            DELETE FROM {_itemConfigurationTableName}
            WHERE [RecordType] = N'FEATURE_STOCK'
              AND [ParentId] = @Id;

            DELETE FROM {_itemConfigurationTableName}
            WHERE [RecordType] = N'CONFIG'
              AND [ParentId] IN (
                    SELECT [Id]
                    FROM {_itemConfigurationTableName}
                    WHERE [RecordType] = N'VALUE'
                      AND [ParentId] = @Id);

            DELETE FROM {_itemConfigurationTableName}
            WHERE [RecordType] = N'VALUE'
              AND [ParentId] = @Id;

            DELETE FROM {_itemConfigurationTableName}
            WHERE [Id] = @Id
              AND [RecordType] = N'FEATURE';

            COMMIT TRANSACTION;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteProductValueAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // 1) ItemFeatureMappings — bu FeatureValue'yu kullanan stok kisit satirlarini temizle (FK)
        await using (var clearMappings = connection.CreateCommand())
        {
            clearMappings.CommandText = $"DELETE FROM {_itemFeatureMappingsTableName} WHERE [FeatureValueId] = @Id;";
            clearMappings.Parameters.Add(new SqlParameter("@Id", id));
            await clearMappings.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2) ItemConfiguration'da bu valueId'yi referans eden CONFIG child satirlari
        //    + asil deger kaydi FeatureValue tablosundan — schema migration sonrasi
        //    VALUE rows ItemConfiguration'da degil, FeatureValue'da. Eski kod hala
        //    ItemConfiguration RecordType='VALUE' silmeye calisiyordu — no-op olur,
        //    gerçek deger silinmezdi. Yeni schema'ya uygun:
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            BEGIN TRANSACTION;

            -- CONFIG child kayitlari: RecordName=valueId (string olarak) — bu deger silinince temizlenmeli
            DELETE FROM {_itemConfigurationTableName}
            WHERE [RecordType] = N'CONFIG'
              AND TRY_CAST([RecordName] AS INT) = @Id;

            -- Asil deger
            DELETE FROM {_propertyValuesTableName}
            WHERE [id] = @Id;

            COMMIT TRANSACTION;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateProductValueAsync(int id, string name, string? aciklama, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_itemConfigurationTableName}
            SET [RecordName] = @RecordName,
                [RelatedMaterialCode] = @Aciklama
            WHERE [Id] = @Id
              AND [RecordType] = N'VALUE';
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@RecordName", name));
        command.Parameters.Add(new SqlParameter("@Aciklama", (object?)aciklama ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteProductConfigAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            BEGIN TRANSACTION;

            DELETE FROM {_itemConfigurationTableName}
            WHERE [RecordType] = N'CONFIG' AND [ParentId] = @Id;

            DELETE FROM {_itemConfigurationTableName}
            WHERE [RecordType] = N'CONFIG' AND [Id] = @Id;

            COMMIT TRANSACTION;
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateProductConfigDescriptionAsync(int id, string? description, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_itemConfigurationTableName}
            SET [RecordName] = @RecordName
            WHERE [RecordType] = N'CONFIG' AND [Id] = @Id AND [ParentId] IS NULL;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@RecordName", (object?)description ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Location>> GetLocationsAsync(CancellationToken cancellationToken)
    {
        var locations = new List<Location>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],
                   [ParentId],
                   [LocationTypeCode],
                   [LocationCode],
                   [LocationName],
                   [SortOrder],
                   [MaxWeightCapacity],
                   [VolumeCapacity],
                   [IsActive],
                   ISNULL([IsMachinePark], 0),
                   ISNULL([IsStorageArea], 0),
                   [AllowNegativeBalance]
            FROM {_warehouseLocationsTableName}
            ORDER BY [SortOrder], [LocationTypeCode], [LocationCode];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            locations.Add(new Location
            {
                Id = reader.GetInt32(0),
                ParentId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                LocationTypeCode = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                LocationCode = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                LocationName = reader.IsDBNull(4) ? null : reader.GetString(4),
                SortOrder = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                MaxWeightCapacity = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                VolumeCapacity = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                IsActive = !reader.IsDBNull(8) && reader.GetBoolean(8),
                IsMachinePark = !reader.IsDBNull(9) && reader.GetBoolean(9),
                IsStorageArea = !reader.IsDBNull(10) && reader.GetBoolean(10),
                AllowNegativeBalance = reader.IsDBNull(11) ? (bool?)null : reader.GetBoolean(11)
            });
        }

        return locations;
    }

    public async Task<int> AddItemAsync(Item stockCard, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_stockCardsTableName}
                ([CompanyId], [Code], [Name], [TypeId], [UnitId], [IsActive], [Created], [Combinations], [TaxRate], [TrackingType], [MinStock], [AutoSerial])
            VALUES
                (@CompanyId, @Code, @Name, @TypeId, @UnitId, @IsActive, @Created, @Combinations, @TaxRate, @TrackingType, @MinStock, @AutoSerial);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@Code", stockCard.Code));
        command.Parameters.Add(new SqlParameter("@Name", stockCard.Name));
        command.Parameters.Add(new SqlParameter("@TypeId", (object?)stockCard.TypeId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@UnitId", (object?)stockCard.UnitId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", stockCard.IsActive ? 1 : 0));
        command.Parameters.Add(new SqlParameter("@Created", (object?)stockCard.Created ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Combinations", stockCard.Combinations ? 1 : 0));
        command.Parameters.Add(new SqlParameter("@TaxRate", stockCard.TaxRate));
        command.Parameters.Add(new SqlParameter("@TrackingType", (object?)stockCard.TrackingType ?? "None"));
        command.Parameters.Add(new SqlParameter("@MinStock", stockCard.MinStock));
        command.Parameters.Add(new SqlParameter("@AutoSerial", stockCard.AutoSerial ? 1 : 0));

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateItemAsync(Item stockCard, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_stockCardsTableName}
            SET
                [Code] = @Code,
                [Name] = @Name,
                [TypeId] = @TypeId,
                [UnitId] = @UnitId,
                [Combinations] = @Combinations,
                [TaxRate] = @TaxRate,
                [TrackingType] = @TrackingType,
                [MinStock] = @MinStock,
                [AutoSerial] = @AutoSerial,
                [Updated] = @Updated
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;

        command.Parameters.Add(new SqlParameter("@Id", stockCard.Id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@Code", stockCard.Code));
        command.Parameters.Add(new SqlParameter("@Name", stockCard.Name));
        command.Parameters.Add(new SqlParameter("@TypeId", (object?)stockCard.TypeId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@UnitId", (object?)stockCard.UnitId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Updated", (object?)stockCard.Updated ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Combinations", stockCard.Combinations ? 1 : 0));
        command.Parameters.Add(new SqlParameter("@TaxRate", stockCard.TaxRate));
        command.Parameters.Add(new SqlParameter("@TrackingType", (object?)stockCard.TrackingType ?? "None"));
        // 2026-07-10: @MinStock parametresi eksikti — SQL [MinStock] = @MinStock derken parametre
        // hiç eklenmiyordu (update çağrısı "Must declare the scalar variable" ile düşerdi).
        command.Parameters.Add(new SqlParameter("@MinStock", stockCard.MinStock));
        command.Parameters.Add(new SqlParameter("@AutoSerial", stockCard.AutoSerial ? 1 : 0));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateItemActiveStatusAsync(int stockCardId, bool isActive, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_stockCardsTableName}
            SET
                [IsActive] = @IsActive,
                [Updated] = @Updated
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;

        command.Parameters.Add(new SqlParameter("@Id", stockCardId));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive ? 1 : 0));
        command.Parameters.Add(new SqlParameter("@Updated", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteItemAsync(int stockCardId, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteMappingsCommand = connection.CreateCommand())
        {
            deleteMappingsCommand.Transaction = transaction;
            deleteMappingsCommand.CommandText = $"""
                DELETE FROM {_itemFeatureMappingsTableName}
                WHERE [ItemId] = @ItemId;
                """;
            deleteMappingsCommand.Parameters.Add(new SqlParameter("@ItemId", stockCardId));
            await deleteMappingsCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteMaterialCardCommand = connection.CreateCommand())
        {
            deleteMaterialCardCommand.Transaction = transaction;
            deleteMaterialCardCommand.CommandText = $"""
                DELETE FROM {_stockCardsTableName}
                WHERE [Id] = @ItemId AND [CompanyId] = @CompanyId;
                """;
            deleteMaterialCardCommand.Parameters.Add(new SqlParameter("@ItemId", stockCardId));
            deleteMaterialCardCommand.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await deleteMaterialCardCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateItemConfigurableStatusAsync(int stockCardId, bool isConfigurable, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_stockCardsTableName}
            SET
                [Updated] = @Updated
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;

        command.Parameters.Add(new SqlParameter("@Id", stockCardId));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@Updated", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddLocationAsync(Location location, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_warehouseLocationsTableName}
                ([ParentId], [LocationTypeCode], [LocationCode], [LocationName], [SortOrder], [MaxWeightCapacity], [VolumeCapacity], [IsActive], [IsMachinePark], [IsStorageArea], [AllowNegativeBalance])
            VALUES
                (@ParentId, @LocationTypeCode, @LocationCode, @LocationName, @SortOrder, @MaxWeightCapacity, @VolumeCapacity, @IsActive, @IsMachinePark, @IsStorageArea, @AllowNegativeBalance);
            """;

        command.Parameters.Add(new SqlParameter("@ParentId", (object?)location.ParentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LocationTypeCode", location.LocationTypeCode));
        command.Parameters.Add(new SqlParameter("@LocationCode", location.LocationCode));
        command.Parameters.Add(new SqlParameter("@LocationName", (object?)location.LocationName ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", location.SortOrder));
        command.Parameters.Add(new SqlParameter("@MaxWeightCapacity", (object?)location.MaxWeightCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@VolumeCapacity", (object?)location.VolumeCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", location.IsActive));
        command.Parameters.Add(new SqlParameter("@IsMachinePark", location.IsMachinePark));
        command.Parameters.Add(new SqlParameter("@IsStorageArea", location.IsStorageArea));
        command.Parameters.Add(new SqlParameter("@AllowNegativeBalance", (object?)location.AllowNegativeBalance ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateLocationAsync(Location location, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_warehouseLocationsTableName}
            SET
                [ParentId] = @ParentId,
                [LocationTypeCode] = @LocationTypeCode,
                [LocationCode] = @LocationCode,
                [LocationName] = @LocationName,
                [SortOrder] = @SortOrder,
                [MaxWeightCapacity] = @MaxWeightCapacity,
                [VolumeCapacity] = @VolumeCapacity,
                [IsActive] = @IsActive,
                [IsMachinePark] = @IsMachinePark,
                [IsStorageArea] = @IsStorageArea,
                [AllowNegativeBalance] = @AllowNegativeBalance
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", location.Id));
        command.Parameters.Add(new SqlParameter("@ParentId", (object?)location.ParentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LocationTypeCode", location.LocationTypeCode));
        command.Parameters.Add(new SqlParameter("@LocationCode", location.LocationCode));
        command.Parameters.Add(new SqlParameter("@LocationName", (object?)location.LocationName ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", location.SortOrder));
        command.Parameters.Add(new SqlParameter("@MaxWeightCapacity", (object?)location.MaxWeightCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@VolumeCapacity", (object?)location.VolumeCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", location.IsActive));
        command.Parameters.Add(new SqlParameter("@IsMachinePark", location.IsMachinePark));
        command.Parameters.Add(new SqlParameter("@IsStorageArea", location.IsStorageArea));
        command.Parameters.Add(new SqlParameter("@AllowNegativeBalance", (object?)location.AllowNegativeBalance ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteLocationAsync(int locationId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_warehouseLocationsTableName}
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", locationId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Machine CRUD ─────────────────────────────────────────────────────
    public async Task<IReadOnlyCollection<Machine>> GetMachinesAsync(CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        var dv = await _dvFilter.BuildAsync(FormCodes.Machines, "m", "Id", cancellationToken);
        var rows = new List<Machine>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],
                   [CompanyId],
                   [LocationId],
                   [Code],
                   [Name],
                   [HourlyCapacity],
                   [SortOrder],
                   [IsActive]
            FROM {_machinesTableName} m
            WHERE m.[CompanyId] = @CompanyId
            {dv.Sql}
            ORDER BY m.[SortOrder], m.[Code];
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        foreach (var prm in dv.Parameters) command.Parameters.Add(new SqlParameter(prm.Name, prm.Value));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Machine
            {
                Id = reader.GetInt32(0),
                CompanyId = reader.GetInt32(1),
                LocationId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Code = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Name = reader.IsDBNull(4) ? null : reader.GetString(4),
                HourlyCapacity = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                SortOrder = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                IsActive = !reader.IsDBNull(7) && reader.GetBoolean(7)
            });
        }
        return rows;
    }

    public async Task<int> AddMachineAsync(Machine machine, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_machinesTableName}
                ([CompanyId], [LocationId], [Code], [Name], [HourlyCapacity], [SortOrder], [IsActive])
            VALUES
                (@CompanyId, @LocationId, @Code, @Name, @HourlyCapacity, @SortOrder, @IsActive);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", machine.CompanyId > 0 ? machine.CompanyId : companyId));
        command.Parameters.Add(new SqlParameter("@LocationId", machine.LocationId));
        command.Parameters.Add(new SqlParameter("@Code", machine.Code));
        command.Parameters.Add(new SqlParameter("@Name", (object?)machine.Name ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@HourlyCapacity", (object?)machine.HourlyCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", machine.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", machine.IsActive));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateMachineAsync(Machine machine, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // CompanyId immutable — UPDATE'te dokunulmaz; WHERE'de scope filter olarak kullanilir
        command.CommandText = $"""
            UPDATE {_machinesTableName}
            SET [LocationId]      = @LocationId,
                [Code]            = @Code,
                [Name]            = @Name,
                [HourlyCapacity]  = @HourlyCapacity,
                [SortOrder]       = @SortOrder,
                [IsActive]        = @IsActive
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;
        command.Parameters.Add(new SqlParameter("@Id", machine.Id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@LocationId", machine.LocationId));
        command.Parameters.Add(new SqlParameter("@Code", machine.Code));
        command.Parameters.Add(new SqlParameter("@Name", (object?)machine.Name ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@HourlyCapacity", (object?)machine.HourlyCapacity ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", machine.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", machine.IsActive));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteMachineAsync(int machineId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_machinesTableName} WHERE [Id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", machineId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddUnitAsync(Unit definition, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_measureUnitDefinitionsTableName}
                ([Code], [Name], [IntlCode], [SortOrder], [IsActive], [CreatedById], [Created], [UpdatedById], [Updated])
            VALUES
                (@Code, @Name, @IntlCode, @SortOrder, @IsActive, @CreatedById, SYSUTCDATETIME(), NULL, NULL);
            """;

        command.Parameters.Add(new SqlParameter("@Code", definition.Code));
        command.Parameters.Add(new SqlParameter("@Name", definition.Name));
        command.Parameters.Add(new SqlParameter("@IntlCode", (object?)definition.IntlCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", definition.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", definition.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedById", (object?)definition.CreatedById ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateUnitAsync(Unit definition, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_measureUnitDefinitionsTableName}
            SET
                [Code] = @Code,
                [Name] = @Name,
                [IntlCode] = @IntlCode,
                [SortOrder] = @SortOrder,
                [IsActive] = @IsActive,
                [UpdatedById] = @UpdatedById,
                [Updated] = SYSUTCDATETIME()
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", definition.Id));
        command.Parameters.Add(new SqlParameter("@Code", definition.Code));
        command.Parameters.Add(new SqlParameter("@Name", definition.Name));
        command.Parameters.Add(new SqlParameter("@IntlCode", (object?)definition.IntlCode ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", definition.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", definition.IsActive));
        command.Parameters.Add(new SqlParameter("@UpdatedById", (object?)definition.UpdatedById ?? DBNull.Value));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteUnitAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DELETE FROM {_measureUnitDefinitionsTableName}
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<ItemUnit>> GetItemUnitsAsync(int itemId, CancellationToken cancellationToken)
    {
        var results = new List<ItemUnit>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemUnitsTableAsync(connection, cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[ItemId],[LineNo],[UnitId],[Multiplier]
            FROM {_itemUnitsTableName}
            WHERE [ItemId] = @ItemId
            ORDER BY [LineNo];
            """;
        cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ItemUnit
            {
                Id = reader.GetInt32(0),
                ItemId = reader.GetInt32(1),
                LineNo = reader.GetInt32(2),
                UnitId = reader.GetInt32(3),
                Multiplier = reader.GetDecimal(4),
            });
        }
        return results;
    }

    /// <summary>
    /// Per-company DB'de tablonun var olmadigi durumda idempotent olarak yaratir.
    /// Legacy stock_unit_conversions varsa ItemUnits'e rename eder. Eski schema
    /// (UnitId kolonu yok) tespit edilirse drop edilir ve yeniden yaratilir.
    /// </summary>
    private async Task EnsureItemUnitsTableAsync(SqlConnection connection, CancellationToken ct)
    {
        var s = _schema.Replace("]", "]]");
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            -- Legacy rename: stock_unit_conversions -> ItemUnits
            IF OBJECT_ID(N'[{s}].[ItemUnits]', N'U') IS NULL
               AND OBJECT_ID(N'[{s}].[stock_unit_conversions]', N'U') IS NOT NULL
            BEGIN
                EXEC sp_rename N'[{s}].[stock_unit_conversions]', N'ItemUnits';
            END;

            -- Eski schema (UnitId yok = legacy unit_code string mevcut) ise drop et
            IF OBJECT_ID(N'[{s}].[ItemUnits]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[ItemUnits]', N'UnitId') IS NULL
            BEGIN
                DROP TABLE [{s}].[ItemUnits];
            END;

            -- Yeni schema ile yarat
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
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveItemUnitsAsync(int itemId, IReadOnlyCollection<ItemUnit> conversions, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemUnitsTableAsync(connection, cancellationToken);

        // Mevcut satirlari sil
        await using (var delCmd = connection.CreateCommand())
        {
            delCmd.CommandText = $"DELETE FROM {_itemUnitsTableName} WHERE [ItemId] = @ItemId;";
            delCmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Master birim Items.UnitId'de tutuluyor — bu tablo sadece alternat birimler (1..5).
        var lineNo = 0;
        foreach (var c in conversions)
        {
            lineNo++;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_itemUnitsTableName}
                    ([ItemId],[LineNo],[UnitId],[Multiplier])
                VALUES (@ItemId, @LineNo, @UnitId, @Multiplier);
                """;
            cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
            cmd.Parameters.Add(new SqlParameter("@LineNo", lineNo));
            cmd.Parameters.Add(new SqlParameter("@UnitId", c.UnitId));
            cmd.Parameters.Add(new SqlParameter("@Multiplier", c.Multiplier));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Bir stoga bagli feature'lardan hangileri aktif bir kombinasyonda kullaniliyor — bunlarin
    /// stok-ozellik baglantisi kaldirilamamali.
    /// </summary>
    public async Task<IReadOnlyCollection<int>> GetUsedFeatureIdsInCombinationsAsync(int itemId, CancellationToken cancellationToken)
    {
        var results = new List<int>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT DISTINCT fv.[FeatureId]
            FROM {_itemConfigurationTableName} parent
            JOIN {_itemConfigurationTableName} child ON child.[ParentId] = parent.[Id]
            JOIN [{_schema}].[FeatureValue] fv ON fv.[Id] = TRY_CAST(child.[RecordName] AS INT)
            WHERE parent.[ItemId] = @ItemId
              AND parent.[IsActive] = 1
              AND parent.[RecordType] = N'CONFIG'
              AND child.[RecordType] = N'CONFIG';
            """;
        cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetInt32(0));
        }
        return results;
    }

    /// <summary>
    /// Bir stogun aktif kombinasyonlarinda kullanilan (FeatureId, FeatureValueId) ciftleri.
    /// Bu ciftler kullaniciya kapatilamaz: deger izinli liste cikarilamaz, ozellik linki kaldirilamaz.
    /// </summary>
    public async Task<IReadOnlyCollection<(int FeatureId, int ValueId)>> GetUsedFeatureValueIdsInCombinationsAsync(
        int itemId, CancellationToken cancellationToken)
    {
        var results = new List<(int, int)>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT DISTINCT fv.[FeatureId], fv.[Id]
            FROM {_itemConfigurationTableName} parent
            JOIN {_itemConfigurationTableName} child ON child.[ParentId] = parent.[Id]
            JOIN [{_schema}].[FeatureValue] fv ON fv.[Id] = TRY_CAST(child.[RecordName] AS INT)
            WHERE parent.[ItemId] = @ItemId
              AND parent.[IsActive] = 1
              AND parent.[RecordType] = N'CONFIG'
              AND child.[RecordType] = N'CONFIG';
            """;
        cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }
        return results;
    }

    /// <summary>
    /// Bir stogun aktif kombinasyon (CONFIG record_type, ParentId NULL, IsActive=1) sayisi.
    /// Yeni ozellik eklenirken UI uyarisi icin kullanilir.
    /// </summary>
    public async Task<int> GetCombinationCountForItemAsync(int itemId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*)
            FROM {_itemConfigurationTableName}
            WHERE [ItemId] = @ItemId
              AND [IsActive] = 1
              AND [RecordType] = N'CONFIG'
              AND [ParentId] IS NULL;
            """;
        cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
        var raw = await cmd.ExecuteScalarAsync(cancellationToken);
        return raw == null || raw == DBNull.Value ? 0 : Convert.ToInt32(raw);
    }

    /// <summary>
    /// Bir ozellige bagli stok listesini ItemFeatureMappings tablosunda gunceller.
    /// Her stok icin: 1 header satiri (FeatureValueId NULL) + 0..N value satiri (FeatureValueId set).
    /// Header satiri PrintDescriptionInDesign tasiyici. Mevcut tum linkleri (header + value) silip yeniden yazar.
    /// </summary>
    public async Task ReplaceFeatureStockLinksAsync(
        int featureId,
        (int ItemId, int[] AllowedValueIds, bool PrintDescriptionInDesign)[] items,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // Bu ozellige ait TUM linkleri sil (header + value-restriction)
        await using (var delCmd = connection.CreateCommand())
        {
            delCmd.CommandText = $"DELETE FROM {_itemFeatureMappingsTableName} WHERE [FeatureId] = @FeatureId;";
            delCmd.Parameters.Add(new SqlParameter("@FeatureId", featureId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var now = DateTime.Now;
        var distinctItems = (items ?? Array.Empty<(int, int[], bool)>())
            .Where(x => x.ItemId > 0)
            .GroupBy(x => x.ItemId)
            .Select(g => (
                ItemId: g.Key,
                AllowedValueIds: (g.Last().AllowedValueIds ?? Array.Empty<int>())
                    .Where(v => v > 0).Distinct().ToArray(),
                PrintDescriptionInDesign: g.Last().PrintDescriptionInDesign))
            .ToArray();

        foreach (var item in distinctItems)
        {
            // Header row (FeatureValueId NULL) — PrintDescriptionInDesign tasir
            await using (var headerCmd = connection.CreateCommand())
            {
                headerCmd.CommandText = $"""
                    INSERT INTO {_itemFeatureMappingsTableName}
                        ([ItemId], [FeatureId], [FeatureValueId], [PrintDescriptionInDesign], [IsActive], [Created], [Updated])
                    VALUES
                        (@ItemId, @FeatureId, NULL, @PrintDesc, 1, @CreatedAt, @UpdatedAt);
                    """;
                headerCmd.Parameters.Add(new SqlParameter("@ItemId", item.ItemId));
                headerCmd.Parameters.Add(new SqlParameter("@FeatureId", featureId));
                headerCmd.Parameters.Add(new SqlParameter("@PrintDesc", item.PrintDescriptionInDesign));
                headerCmd.Parameters.Add(new SqlParameter("@CreatedAt", now));
                headerCmd.Parameters.Add(new SqlParameter("@UpdatedAt", now));
                await headerCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Value-restriction rows
            foreach (var valueId in item.AllowedValueIds)
            {
                await using var valCmd = connection.CreateCommand();
                valCmd.CommandText = $"""
                    INSERT INTO {_itemFeatureMappingsTableName}
                        ([ItemId], [FeatureId], [FeatureValueId], [PrintDescriptionInDesign], [IsActive], [Created], [Updated])
                    VALUES
                        (@ItemId, @FeatureId, @FeatureValueId, @PrintDesc, 1, @CreatedAt, @UpdatedAt);
                    """;
                valCmd.Parameters.Add(new SqlParameter("@ItemId", item.ItemId));
                valCmd.Parameters.Add(new SqlParameter("@FeatureId", featureId));
                valCmd.Parameters.Add(new SqlParameter("@FeatureValueId", valueId));
                valCmd.Parameters.Add(new SqlParameter("@PrintDesc", item.PrintDescriptionInDesign));
                valCmd.Parameters.Add(new SqlParameter("@CreatedAt", now));
                valCmd.Parameters.Add(new SqlParameter("@UpdatedAt", now));
                await valCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    // ── Item Locations (malzeme-lokasyon cok-cogu) ─────────────────────────

    private async Task EnsureItemLocationsTableAsync(SqlConnection connection, CancellationToken ct)
    {
        var s = _schema.Replace("]", "]]");
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            -- Migration: item_locations → ItemLocation tablo rename (mevcut DB'ler)
            IF OBJECT_ID(N'[{s}].[item_locations]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{s}].[ItemLocation]', N'U') IS NULL
            BEGIN
                EXEC sp_rename N'[{s}].[item_locations]', N'ItemLocation';
            END;
            -- Migration: kolon rename (snake_case → PascalCase, mevcut DB'ler)
            IF OBJECT_ID(N'[{s}].[ItemLocation]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'[{s}].[ItemLocation]', N'id') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[ItemLocation]', N'Id') IS NULL
                    EXEC sp_rename N'[{s}].[ItemLocation].[id]', N'Id', N'COLUMN';
                IF COL_LENGTH(N'[{s}].[ItemLocation]', N'item_id') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[ItemLocation]', N'ItemId') IS NULL
                    EXEC sp_rename N'[{s}].[ItemLocation].[item_id]', N'ItemId', N'COLUMN';
                IF COL_LENGTH(N'[{s}].[ItemLocation]', N'location_id') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[ItemLocation]', N'LocationId') IS NULL
                    EXEC sp_rename N'[{s}].[ItemLocation].[location_id]', N'LocationId', N'COLUMN';
                IF COL_LENGTH(N'[{s}].[ItemLocation]', N'is_default') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[ItemLocation]', N'IsDefault') IS NULL
                    EXEC sp_rename N'[{s}].[ItemLocation].[is_default]', N'IsDefault', N'COLUMN';
                IF COL_LENGTH(N'[{s}].[ItemLocation]', N'sort_order') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[ItemLocation]', N'SortOrder') IS NULL
                    EXEC sp_rename N'[{s}].[ItemLocation].[sort_order]', N'SortOrder', N'COLUMN';
            END;
            IF OBJECT_ID(N'[{s}].[ItemLocation]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[ItemLocation]
                (
                    [Id]         INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ItemId]     INT NOT NULL,
                    [LocationId] INT NULL,
                    [IsDefault]  BIT NOT NULL DEFAULT(0),
                    [SortOrder]  INT NOT NULL DEFAULT(0)
                );
                CREATE UNIQUE INDEX [ux_item_locations_item_location]
                    ON [{s}].[ItemLocation]([ItemId], [LocationId])
                    WHERE [LocationId] IS NOT NULL;
                CREATE UNIQUE INDEX [ux_item_locations_item_default]
                    ON [{s}].[ItemLocation]([ItemId])
                    WHERE [IsDefault] = 1;
                CREATE INDEX [ix_item_locations_location]
                    ON [{s}].[ItemLocation]([LocationId]);
            END;
            IF COLUMNPROPERTY(OBJECT_ID(N'[{s}].[ItemLocation]'), N'LocationId', 'AllowsNull') = 0
            BEGIN
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[{s}].[ItemLocation]') AND [name] = N'ux_item_locations_item_location')
                    DROP INDEX [ux_item_locations_item_location] ON [{s}].[ItemLocation];
                ALTER TABLE [{s}].[ItemLocation] ALTER COLUMN [LocationId] INT NULL;
                CREATE UNIQUE INDEX [ux_item_locations_item_location]
                    ON [{s}].[ItemLocation]([ItemId], [LocationId])
                    WHERE [LocationId] IS NOT NULL;
            END;

            -- Planlama: depo bazında asgari stok kolonu (initializer yarış/atlama durumuna karşı per-call guard)
            IF OBJECT_ID(N'[{s}].[ItemLocation]', N'U') IS NOT NULL
               AND COL_LENGTH(N'[{s}].[ItemLocation]', N'MinStock') IS NULL
                ALTER TABLE [{s}].[ItemLocation] ADD [MinStock] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_ItemLocation_MinStock] DEFAULT(0);

            -- Planlama: belge bazında malzeme kilidi
            IF OBJECT_ID(N'[{s}].[ItemDocumentLock]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[ItemDocumentLock]
                (
                    [Id]      INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ItemDocumentLock] PRIMARY KEY,
                    [ItemId]  INT NOT NULL,
                    [DocType] NVARCHAR(50) NOT NULL
                );
                CREATE UNIQUE INDEX [UX_ItemDocumentLock_ItemId_DocType]
                    ON [{s}].[ItemDocumentLock]([ItemId], [DocType]);
                CREATE INDEX [IX_ItemDocumentLock_DocType]
                    ON [{s}].[ItemDocumentLock]([DocType]);
            END;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<ItemLocation>> GetItemLocationsAsync(int itemId, CancellationToken cancellationToken)
    {
        var results = new List<ItemLocation>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemLocationsTableAsync(connection, cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[ItemId],[LocationId],[IsDefault],[SortOrder],[MinStock]
            FROM {_itemLocationsTableName}
            WHERE [ItemId] = @ItemId
            ORDER BY [SortOrder], [Id];
            """;
        cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ItemLocation
            {
                Id = reader.GetInt32(0),
                ItemId = reader.GetInt32(1),
                LocationId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                IsDefault = reader.GetBoolean(3),
                SortOrder = reader.GetInt32(4),
                MinStock = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
            });
        }
        return results;
    }

    public async Task NullifyItemLocationsByLocationIdAsync(int locationId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemLocationsTableAsync(connection, cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {_itemLocationsTableName}               SET [LocationId]         = NULL, [IsDefault]  = 0  WHERE [LocationId]         = @id;
            UPDATE [{_schema}].[Asset]                     SET [LocationId]         = NULL                    WHERE [LocationId]         = @id;
            UPDATE [{_schema}].[Document]                  SET [LocationId]         = NULL                    WHERE [LocationId]         = @id;
            UPDATE [{_schema}].[DocumentLine]              SET [LocationId]         = NULL                    WHERE [LocationId]         = @id;
            UPDATE [{_schema}].[WorkOrder]                 SET [WarehouseLocationId] = NULL                   WHERE [WarehouseLocationId] = @id;
            """;
        cmd.Parameters.Add(new SqlParameter("@id", locationId));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task NullifyLocationHistoricalFkRefsAsync(int locationId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE [{_schema}].[AssetAssignment] SET [LocationId] = NULL WHERE [LocationId] = @id;";
        cmd.Parameters.Add(new SqlParameter("@id", locationId));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveItemLocationsAsync(int itemId, IReadOnlyCollection<ItemLocation> locations, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemLocationsTableAsync(connection, cancellationToken);

        // Strateji: sil + yeniden ekle (kuctuk liste, <20 satir)
        await using (var delCmd = connection.CreateCommand())
        {
            delCmd.CommandText = $"DELETE FROM {_itemLocationsTableName} WHERE [ItemId] = @ItemId;";
            delCmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // En fazla bir varsayilan olabilir — ilk true'yu esas al, digerlerini sil
        var defaultSeen = false;
        var sortOrder = 0;
        foreach (var l in locations)
        {
            var isDefault = l.IsDefault && !defaultSeen;
            if (isDefault) defaultSeen = true;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO {_itemLocationsTableName}
                    ([ItemId],[LocationId],[IsDefault],[SortOrder],[MinStock])
                VALUES (@ItemId, @LocationId, @IsDefault, @SortOrder, @MinStock);
                """;
            cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
            cmd.Parameters.Add(new SqlParameter("@LocationId", (object?)l.LocationId ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@IsDefault", isDefault));
            cmd.Parameters.Add(new SqlParameter("@SortOrder", sortOrder++));
            cmd.Parameters.Add(new SqlParameter("@MinStock", l.MinStock));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // ── Planlama: belge bazında malzeme kilidi ───────────────────────

    public async Task<IReadOnlyCollection<string>> GetItemDocumentLocksAsync(int itemId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemLocationsTableAsync(connection, cancellationToken);

        var result = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT [DocType] FROM [{_schema}].[ItemDocumentLock] WHERE [ItemId] = @ItemId;";
        cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetString(0));
        return result;
    }

    public async Task SaveItemDocumentLocksAsync(int itemId, IReadOnlyCollection<string> docTypes, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemLocationsTableAsync(connection, cancellationToken);

        // Strateji: sil + yeniden ekle
        await using (var delCmd = connection.CreateCommand())
        {
            delCmd.CommandText = $"DELETE FROM [{_schema}].[ItemDocumentLock] WHERE [ItemId] = @ItemId;";
            delCmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dt in docTypes ?? [])
        {
            var code = dt?.Trim();
            if (string.IsNullOrWhiteSpace(code) || !seen.Add(code)) continue;

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"INSERT INTO [{_schema}].[ItemDocumentLock] ([ItemId],[DocType]) VALUES (@ItemId, @DocType);";
            cmd.Parameters.Add(new SqlParameter("@ItemId", itemId));
            cmd.Parameters.Add(new SqlParameter("@DocType", code));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyCollection<int>> GetLockedItemIdsByDocTypeAsync(string docType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(docType)) return [];
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemLocationsTableAsync(connection, cancellationToken);

        var result = new List<int>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT [ItemId] FROM [{_schema}].[ItemDocumentLock] WHERE [DocType] = @DocType;";
        cmd.Parameters.Add(new SqlParameter("@DocType", docType.Trim()));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetInt32(0));
        return result;
    }

    // ── Location Types (dinamik tip sozlugu) ─────────────────────────

    private async Task EnsureLocationTypesTableAsync(SqlConnection connection, CancellationToken ct)
    {
        var s = _schema.Replace("]", "]]");
        await using var cmd = connection.CreateCommand();
        // Sadece tabloyu yarat + ilk seed. Tablo zaten varsa seed re-insert yapilmaz
        // (aksi halde silinen seed tipler her GetAll cagrisinda geri gelir).
        // 2026-06-09: location_types → LocationType tablo/kolon rename (PascalCase).
        cmd.CommandText = $"""
            -- Migration: location_types → LocationType tablo rename (mevcut DB'ler)
            IF OBJECT_ID(N'[{s}].[location_types]', N'U') IS NOT NULL
               AND OBJECT_ID(N'[{s}].[LocationType]', N'U') IS NULL
            BEGIN
                EXEC sp_rename N'[{s}].[location_types]', N'LocationType';
            END;
            -- Migration: kolon rename (snake_case → PascalCase, mevcut DB'ler)
            -- Her kolon ayrı kontrol — kısmi migrate edilmiş DB'lerde sp_rename ambiguous
            -- hatası (Err 15248) önlenir.
            IF OBJECT_ID(N'[{s}].[LocationType]', N'U') IS NOT NULL
            BEGIN
                IF COL_LENGTH(N'[{s}].[LocationType]', N'id') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[LocationType]', N'Id') IS NULL
                    EXEC sp_rename N'[{s}].[LocationType].[id]', N'Id', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[LocationType]', N'code') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[LocationType]', N'Code') IS NULL
                    EXEC sp_rename N'[{s}].[LocationType].[code]', N'Code', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[LocationType]', N'name') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[LocationType]', N'Name') IS NULL
                    EXEC sp_rename N'[{s}].[LocationType].[name]', N'Name', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[LocationType]', N'sort_order') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[LocationType]', N'SortOrder') IS NULL
                    EXEC sp_rename N'[{s}].[LocationType].[sort_order]', N'SortOrder', N'COLUMN';

                IF COL_LENGTH(N'[{s}].[LocationType]', N'is_active') IS NOT NULL
                   AND COL_LENGTH(N'[{s}].[LocationType]', N'IsActive') IS NULL
                    EXEC sp_rename N'[{s}].[LocationType].[is_active]', N'IsActive', N'COLUMN';
            END;
            IF OBJECT_ID(N'[{s}].[LocationType]', N'U') IS NULL
            BEGIN
                CREATE TABLE [{s}].[LocationType]
                (
                    [Id]        INT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_LocationType] PRIMARY KEY,
                    [Code]      NVARCHAR(50)  NOT NULL,
                    [Name]      NVARCHAR(100) NOT NULL,
                    [SortOrder] INT           NOT NULL CONSTRAINT [DF_LocationType_SortOrder] DEFAULT(0),
                    [IsActive]  BIT           NOT NULL CONSTRAINT [DF_LocationType_IsActive] DEFAULT(1)
                );
                CREATE UNIQUE INDEX [UX_LocationType_Code]
                    ON [{s}].[LocationType]([Code]);

                -- Seed yalnizca yeni tablo olusturuldugu anda eklenir
                INSERT INTO [{s}].[LocationType]([Code],[Name],[SortOrder],[IsActive]) VALUES (N'FACTORY', N'Fabrika', 10, 1);
                INSERT INTO [{s}].[LocationType]([Code],[Name],[SortOrder],[IsActive]) VALUES (N'SECTION', N'Bolum', 20, 1);
                INSERT INTO [{s}].[LocationType]([Code],[Name],[SortOrder],[IsActive]) VALUES (N'SHELF', N'Raf', 30, 1);
                INSERT INTO [{s}].[LocationType]([Code],[Name],[SortOrder],[IsActive]) VALUES (N'BIN', N'Hucre', 40, 1);
            END;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyCollection<LocationType>> GetLocationTypesAsync(CancellationToken cancellationToken)
    {
        var results = new List<LocationType>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureLocationTypesTableAsync(connection, cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT [Id],[Code],[Name],[SortOrder],[IsActive]
            FROM {_locationTypesTableName}
            ORDER BY [SortOrder], [Name];
            """;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new LocationType
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                SortOrder = reader.GetInt32(3),
                IsActive = reader.GetBoolean(4),
            });
        }
        return results;
    }

    public async Task<int> UpsertLocationTypeAsync(LocationType type, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureLocationTypesTableAsync(connection, cancellationToken);
        await using var cmd = connection.CreateCommand();
        if (type.Id > 0)
        {
            cmd.CommandText = $"""
                UPDATE {_locationTypesTableName}
                SET [Code] = @Code, [Name] = @Name, [SortOrder] = @SortOrder, [IsActive] = @IsActive
                WHERE [Id] = @Id;
                SELECT @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id", type.Id));
        }
        else
        {
            cmd.CommandText = $"""
                INSERT INTO {_locationTypesTableName}([Code],[Name],[SortOrder],[IsActive])
                VALUES (@Code, @Name, @SortOrder, @IsActive);
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
        }
        cmd.Parameters.Add(new SqlParameter("@Code", type.Code));
        cmd.Parameters.Add(new SqlParameter("@Name", type.Name));
        cmd.Parameters.Add(new SqlParameter("@SortOrder", type.SortOrder));
        cmd.Parameters.Add(new SqlParameter("@IsActive", type.IsActive));
        var newId = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(newId);
    }

    public async Task DeleteLocationTypeAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureLocationTypesTableAsync(connection, cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"DELETE FROM {_locationTypesTableName} WHERE [Id] = @Id;";
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountLocationsOfTypeAsync(string code, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {_warehouseLocationsTableName} WHERE [LocationTypeCode] = @Code;";
        cmd.Parameters.Add(new SqlParameter("@Code", code));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<int> RenameLocationTypeCodeAsync(string oldCode, string newCode, CancellationToken cancellationToken)
    {
        if (string.Equals(oldCode, newCode, StringComparison.OrdinalIgnoreCase)) return 0;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE {_warehouseLocationsTableName} SET [LocationTypeCode] = @NewCode WHERE [LocationTypeCode] = @OldCode;";
        cmd.Parameters.Add(new SqlParameter("@OldCode", oldCode));
        cmd.Parameters.Add(new SqlParameter("@NewCode", newCode));
        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> AddPropertyAsync(ItemFeature property, CancellationToken cancellationToken)
    {
        var companyId = property.CompanyId > 0 ? property.CompanyId : _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // 2026-07-05: kolonlar [Created]/[Updated] (MigrateColumnRenamesAsync ile senkron)
        command.CommandText = $"""
            INSERT INTO {_propertiesTableName}
                ([CompanyId], [Name], [DataType], [UnitOfMeasure], [VisibleInDesign], [IsActive], [Created], [Updated])
            VALUES
                (@CompanyId, @Name, @DataType, @UnitOfMeasure, @VisibleInDesign, @IsActive, @CreatedAt, @UpdatedAt);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@Name", property.Name));
        command.Parameters.Add(new SqlParameter("@DataType", property.DataType.ToString()));
        command.Parameters.Add(new SqlParameter("@UnitOfMeasure", (object?)property.UnitOfMeasure ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@VisibleInDesign", property.VisibleInDesign));
        command.Parameters.Add(new SqlParameter("@IsActive", property.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", property.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task UpdateItemFeatureAsync(int id, string name, string dataType, string? unitOfMeasure, bool visibleInDesign, bool isActive, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_propertiesTableName}
            SET [Name] = @Name,
                [DataType] = @DataType,
                [UnitOfMeasure] = @UnitOfMeasure,
                [VisibleInDesign] = @VisibleInDesign,
                [IsActive] = @IsActive,
                [Updated] = @UpdatedAt
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;

        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@Name", name));
        command.Parameters.Add(new SqlParameter("@DataType", dataType));
        command.Parameters.Add(new SqlParameter("@UnitOfMeasure", (object?)unitOfMeasure ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@VisibleInDesign", visibleInDesign));
        command.Parameters.Add(new SqlParameter("@IsActive", isActive));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteItemFeatureAsync(int id, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // FK ItemFeatureMappings_ItemFeature CASCADE degil — once mapping'leri temizle
        await using (var clearMappings = connection.CreateCommand())
        {
            clearMappings.CommandText = $"DELETE FROM {_itemFeatureMappingsTableName} WHERE [FeatureId] = @Id;";
            clearMappings.Parameters.Add(new SqlParameter("@Id", id));
            await clearMappings.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_propertiesTableName} WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddPropertyValueAsync(FeatureValue propertyValue, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_propertyValuesTableName}
                ([FeatureId], [Code], [Description], [Value], [SortOrder], [IsActive], [Created], [Updated])
            VALUES
                (@PropertyId, @Code, @Description, @Value, @SortOrder, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        command.Parameters.Add(new SqlParameter("@PropertyId", propertyValue.PropertyId));
        command.Parameters.Add(new SqlParameter("@Code", propertyValue.Code));
        command.Parameters.Add(new SqlParameter("@Description", propertyValue.Description));
        command.Parameters.Add(new SqlParameter("@Value", propertyValue.Value));
        command.Parameters.Add(new SqlParameter("@SortOrder", propertyValue.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", propertyValue.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", propertyValue.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddStockPropertyMappingAsync(ItemFeatureMapping mapping, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_itemFeatureMappingsTableName}
                ([ItemId], [FeatureId], [FeatureValueId], [IsActive], [Created], [Updated])
            VALUES
                (@ItemId, @FeatureId, @FeatureValueId, @IsActive, @CreatedAt, @UpdatedAt);
            """;

        command.Parameters.Add(new SqlParameter("@ItemId", mapping.ItemId));
        command.Parameters.Add(new SqlParameter("@FeatureId", mapping.FeatureId));
        command.Parameters.Add(new SqlParameter("@FeatureValueId", (object?)mapping.FeatureValueId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", mapping.IsActive));
        command.Parameters.Add(new SqlParameter("@CreatedAt", mapping.CreatedAt));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStockPropertyMappingValueAsync(
        int mappingId,
        int featureValueId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_itemFeatureMappingsTableName}
            SET
                [FeatureValueId] = @FeatureValueId,
                [Updated] = @UpdatedAt
            WHERE [Id] = @Id;
            """;

        command.Parameters.Add(new SqlParameter("@Id", mappingId));
        command.Parameters.Add(new SqlParameter("@FeatureValueId", featureValueId));
        command.Parameters.Add(new SqlParameter("@UpdatedAt", DateTime.Now));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MaterialCardDynamicFieldDataType ParseMaterialCardDynamicFieldDataType(string? rawValue) =>
        rawValue?.Trim().ToUpperInvariant() switch
        {
            "INTEGER" => MaterialCardDynamicFieldDataType.Integer,
            "DECIMAL" => MaterialCardDynamicFieldDataType.Decimal,
            "DATE" => MaterialCardDynamicFieldDataType.Date,
            "BOOLEAN" => MaterialCardDynamicFieldDataType.Boolean,
            "DROPDOWN" => MaterialCardDynamicFieldDataType.Dropdown,
            "MULTISELECT" => MaterialCardDynamicFieldDataType.MultiSelect,
            _ => MaterialCardDynamicFieldDataType.String
        };

    private static string ToMaterialCardDynamicFieldDataTypeValue(MaterialCardDynamicFieldDataType dataType) =>
        dataType switch
        {
            MaterialCardDynamicFieldDataType.Integer => "INTEGER",
            MaterialCardDynamicFieldDataType.Decimal => "DECIMAL",
            MaterialCardDynamicFieldDataType.Date => "DATE",
            MaterialCardDynamicFieldDataType.Boolean => "BOOLEAN",
            MaterialCardDynamicFieldDataType.Dropdown => "DROPDOWN",
            MaterialCardDynamicFieldDataType.MultiSelect => "MULTISELECT",
            _ => "STRING"
        };
        
    public async Task<IReadOnlyCollection<BOM>> GetBOMsAsync(CancellationToken cancellationToken)
    {
        var dv = await _dvFilter.BuildAsync(FormCodes.BomEdit, "t", "Id", cancellationToken);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                t.[Id], t.[ItemId], t.[ConfigId], t.[Description],
                t.[ImageData], t.[ImageMimeType], t.[ImageFitMode], t.[ImageRotation],
                l.[Id]       AS [LineId],
                l.[BOMId],
                l.[ItemId]   AS [LineItemId],
                l.[ConfigId] AS [LineConfigId],
                l.[Quantity],
                l.[ScrapRatio],
                l.[LineGuid],
                t.[RoutingId], r.[Code] AS RoutingCode, r.[Name] AS RoutingName,
                l.[Note]     AS [LineNote]
            FROM {_productTreesTableName} t
            LEFT JOIN {_productTreeLinesTableName} l ON l.[BOMId] = t.[Id]
            LEFT JOIN [{_schema}].[Routing] r ON r.[Id] = t.[RoutingId]
            WHERE t.[IsActive] = 1
            {dv.Sql}
            ORDER BY t.[Id], l.[Id];
            """;

        foreach (var prm in dv.Parameters) command.Parameters.Add(new SqlParameter(prm.Name, prm.Value));
        var treesById = new Dictionary<int, BOM>();
        var linesByTreeId = new Dictionary<int, List<BOMLine>>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var treeId = reader.GetInt32(0);
            if (!treesById.ContainsKey(treeId))
            {
                // Eski kayıtlarda BOM.ItemId NULL kalmış olabilir (kolon sonradan
                // eklendi, NULL allowed). Bu durumda yetim BOM'u atlıyoruz —
                // kullanıcıya gösterilemeyecek bir kart oluşturmak yerine listede
                // hiç görünmesin (boş Master/Detail = ekran patlamış görünümü).
                if (reader.IsDBNull(1)) continue;

                treesById[treeId] = new BOM
                {
                    Id            = treeId,
                    ItemId        = reader.GetInt32(1),
                    ConfigId      = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    Description   = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ImageData     = reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4),
                    ImageMimeType = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ImageFitMode  = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ImageRotation = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    RoutingId     = reader.IsDBNull(15) ? null : reader.GetInt32(15),
                    RoutingCode   = reader.IsDBNull(16) ? null : reader.GetString(16),
                    RoutingName   = reader.IsDBNull(17) ? null : reader.GetString(17),
                };
                linesByTreeId[treeId] = new List<BOMLine>();
            }

            // Line satırı: en az LineId + LineItemId dolu olmalı (NULL = orphan / no-op join row).
            if (!reader.IsDBNull(8) && !reader.IsDBNull(10) && linesByTreeId.ContainsKey(treeId))
            {
                linesByTreeId[treeId].Add(new BOMLine
                {
                    Id         = reader.GetInt32(8),
                    BOMId      = reader.IsDBNull(9)  ? treeId : reader.GetInt32(9),
                    ItemId     = reader.GetInt32(10),
                    ConfigId   = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    Quantity   = reader.IsDBNull(12) ? 0m : reader.GetDecimal(12),
                    ScrapRatio = reader.IsDBNull(13) ? 0m : reader.GetDecimal(13),
                    LineGuid   = reader.IsDBNull(14) ? Guid.Empty : reader.GetGuid(14),
                    Note       = reader.IsDBNull(18) ? null : reader.GetString(18),
                });
            }
        }

        foreach (var (treeId, lines) in linesByTreeId)
            ((List<BOMLine>)treesById[treeId].Lines).AddRange(lines);

        return treesById.Values.ToList();
    }

    public async Task<BOMWithNames?> GetBOMByItemAsync(int itemId, int? configId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var configFilter    = configId.HasValue ? "t.[ConfigId] = @ConfigId" : "t.[ConfigId] IS NULL";
        var configFilterSub = configId.HasValue ? "[ConfigId] = @ConfigId"   : "[ConfigId] IS NULL";

        // Items + ItemConfiguration JOIN ile enriched response — frontend display icin
        // ItemCode/ItemName/ConfigCode field'lari tasinir.
        command.CommandText = $"""
            SELECT
                t.[Id], t.[ItemId], pi.[code] AS ParentItemCode, ISNULL(pi.[name], pi.[code]) AS ParentItemName,
                t.[ConfigId], pcfg.[RecordCode] AS ParentConfigCode,
                t.[Description],
                t.[ImageData], t.[ImageMimeType], t.[ImageFitMode], t.[ImageRotation],
                l.[ItemId]   AS LineItemId,
                ci.[code]    AS LineItemCode,
                ISNULL(ci.[name], ci.[code]) AS LineItemName,
                l.[ConfigId] AS LineConfigId,
                lcfg.[RecordCode] AS LineConfigCode,
                l.[Quantity],
                l.[ScrapRatio],
                t.[RoutingId], r.[Code] AS RoutingCode, r.[Name] AS RoutingName,
                l.[Note]     AS [LineNote]
            FROM {_productTreesTableName} t
            INNER JOIN {_stockCardsTableName} pi ON pi.[id] = t.[ItemId]
            LEFT  JOIN [{_schema}].[ItemConfiguration] pcfg ON pcfg.[Id] = t.[ConfigId]
            LEFT  JOIN {_productTreeLinesTableName} l  ON l.[BOMId] = t.[Id]
            LEFT  JOIN {_stockCardsTableName} ci ON ci.[id] = l.[ItemId]
            LEFT  JOIN [{_schema}].[ItemConfiguration] lcfg ON lcfg.[Id] = l.[ConfigId]
            LEFT  JOIN [{_schema}].[Routing] r ON r.[Id] = t.[RoutingId]
            WHERE t.[ItemId] = @ItemId
              AND t.[IsActive] = 1   -- soft-delete uyumu
              AND {configFilter}
              AND t.[Id] = (
                  SELECT MAX([Id]) FROM {_productTreesTableName}
                  WHERE [ItemId] = @ItemId AND [IsActive] = 1 AND {configFilterSub}
              )
            ORDER BY l.[Id];
            """;

        command.Parameters.Add(new SqlParameter("@ItemId", itemId));
        if (configId.HasValue)
            command.Parameters.Add(new SqlParameter("@ConfigId", configId.Value));

        BOMWithNames? result = null;
        var lines = new List<BOMLineWithName>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (result is null)
            {
                result = new BOMWithNames(
                    Id:            reader.GetInt32(0),
                    ItemId:        reader.GetInt32(1),
                    ItemCode:      reader.GetString(2),
                    ItemName:      reader.GetString(3),
                    ConfigId:      reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    ConfigCode:    reader.IsDBNull(5) ? null : reader.GetString(5),
                    Description:   reader.IsDBNull(6) ? null : reader.GetString(6),
                    ImageData:     reader.IsDBNull(7) ? null : (byte[])reader.GetValue(7),
                    ImageMimeType: reader.IsDBNull(8) ? null : reader.GetString(8),
                    ImageFitMode:  reader.IsDBNull(9) ? null : reader.GetString(9),
                    ImageRotation: reader.GetInt32(10),
                    Lines:         lines,
                    // RoutingId/Code/Name reader indices 18/19/20 (after Quantity=16, ScrapRatio=17)
                    RoutingId:     reader.IsDBNull(18) ? null : reader.GetInt32(18),
                    RoutingCode:   reader.IsDBNull(19) ? null : reader.GetString(19),
                    RoutingName:   reader.IsDBNull(20) ? null : reader.GetString(20));
            }

            if (!reader.IsDBNull(11))
            {
                lines.Add(new BOMLineWithName(
                    ItemId:                reader.GetInt32(11),
                    ComponentMaterialCode: reader.IsDBNull(12) ? "" : reader.GetString(12),
                    ComponentMaterialName: reader.IsDBNull(13) ? "" : reader.GetString(13),
                    ConfigId:              reader.IsDBNull(14) ? null : reader.GetInt32(14),
                    ComponentConfigCode:   reader.IsDBNull(15) ? null : reader.GetString(15),
                    Quantity:              reader.GetDecimal(16),
                    ScrapRatio:            reader.GetDecimal(17),
                    Note:                  reader.IsDBNull(21) ? null : reader.GetString(21)));
            }
        }

        return result;
    }

    public async Task<int> AddBOMAsync(BOM tree, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        int newId;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            // Yeni audit kolonlari (Created/CreatedBy/IsActive) + legacy CreatedAt/UpdatedAt
            // (geri uyum icin paralel yazilir). IsActive default 1.
            cmd.CommandText = $"""
                INSERT INTO {_productTreesTableName}
                    ([ItemId],[ConfigId],[Description],[ImageData],[ImageMimeType],[ImageFitMode],[ImageRotation],
                     [RoutingId],
                     [IsActive],[CreatedById],[Created],[CreatedAt],[UpdatedAt])
                VALUES
                    (@ItemId,@ConfigId,@Description,@ImageData,@ImageMimeType,@ImageFitMode,@ImageRotation,
                     @RoutingId,
                     1,@CreatedById,SYSUTCDATETIME(),GETDATE(),GETDATE());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.Add(new SqlParameter("@ItemId",            tree.ItemId));
            cmd.Parameters.Add(new SqlParameter("@ConfigId",          (object?)tree.ConfigId      ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Description",       (object?)tree.Description   ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageData",         (object?)tree.ImageData     ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.VarBinary });
            cmd.Parameters.Add(new SqlParameter("@ImageMimeType",     (object?)tree.ImageMimeType ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageFitMode",      (object?)tree.ImageFitMode  ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageRotation",     tree.ImageRotation));
            cmd.Parameters.Add(new SqlParameter("@RoutingId",         (object?)tree.RoutingId     ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@CreatedById",       (object?)tree.CreatedById   ?? DBNull.Value));
            newId = (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        }

        foreach (var line in tree.Lines)
        {
            await using var lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = $"""
                INSERT INTO {_productTreeLinesTableName}
                    ([BOMId],[ItemId],[ConfigId],[Quantity],[ScrapRatio],[LineGuid],[Note],[CreatedById],[Created])
                VALUES
                    (@BOMId,@ItemId,@ConfigId,@Qty,@Scrap,@LineGuid,@Note,@CreatedById,SYSUTCDATETIME());
                """;
            lineCmd.Parameters.Add(new SqlParameter("@BOMId",   newId));
            lineCmd.Parameters.Add(new SqlParameter("@ItemId",  line.ItemId));
            lineCmd.Parameters.Add(new SqlParameter("@ConfigId",(object?)line.ConfigId ?? DBNull.Value));
            lineCmd.Parameters.Add(new SqlParameter("@Qty",      line.Quantity));
            lineCmd.Parameters.Add(new SqlParameter("@Scrap",    line.ScrapRatio));
            lineCmd.Parameters.Add(new SqlParameter("@LineGuid", line.LineGuid == Guid.Empty ? Guid.NewGuid() : line.LineGuid));
            lineCmd.Parameters.Add(new SqlParameter("@Note",     (object?)line.Note ?? DBNull.Value));
            lineCmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)line.CreatedById ?? (object?)tree.CreatedById ?? DBNull.Value));
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return newId;
    }

    public async Task UpdateBOMAsync(BOM tree, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            // Updated / UpdatedById yeni audit kolonlari + legacy UpdatedAt geri uyum.
            cmd.CommandText = $"""
                UPDATE {_productTreesTableName}
                SET [ItemId]        = @ItemId,
                    [ConfigId]      = @ConfigId,
                    [Description]   = @Description,
                    [ImageData]     = @ImageData,
                    [ImageMimeType] = @ImageMimeType,
                    [ImageFitMode]  = @ImageFitMode,
                    [ImageRotation] = @ImageRotation,
                    [RoutingId]     = @RoutingId,
                    [Updated]       = SYSUTCDATETIME(),
                    [UpdatedById]   = @UpdatedById,
                    [UpdatedAt]     = GETDATE()
                WHERE [Id] = @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id",            tree.Id));
            cmd.Parameters.Add(new SqlParameter("@ItemId",        tree.ItemId));
            cmd.Parameters.Add(new SqlParameter("@ConfigId",      (object?)tree.ConfigId      ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Description",   (object?)tree.Description   ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageData",     (object?)tree.ImageData     ?? DBNull.Value) { SqlDbType = System.Data.SqlDbType.VarBinary });
            cmd.Parameters.Add(new SqlParameter("@ImageMimeType", (object?)tree.ImageMimeType ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageFitMode",  (object?)tree.ImageFitMode  ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@ImageRotation", tree.ImageRotation));
            cmd.Parameters.Add(new SqlParameter("@RoutingId",     (object?)tree.RoutingId     ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@UpdatedById",   (object?)tree.UpdatedById   ?? DBNull.Value));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = $"DELETE FROM {_productTreeLinesTableName} WHERE [BOMId] = @BOMId;";
            deleteCmd.Parameters.Add(new SqlParameter("@BOMId", tree.Id));
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in tree.Lines)
        {
            await using var lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            // UpdateBOMAsync DELETE+INSERT pattern'i kullaniyor — her save'de tum
            // line'lar yeniden yaratilir. UpdatedById (header) audit'i degisikligi
            // kim yaptigini gosterir; satir CreatedById ayni save'i yapan kullanici.
            lineCmd.CommandText = $"""
                INSERT INTO {_productTreeLinesTableName}
                    ([BOMId],[ItemId],[ConfigId],[Quantity],[ScrapRatio],[LineGuid],[Note],[CreatedById],[Created])
                VALUES
                    (@BOMId,@ItemId,@ConfigId,@Qty,@Scrap,@LineGuid,@Note,@CreatedById,SYSUTCDATETIME());
                """;
            lineCmd.Parameters.Add(new SqlParameter("@BOMId",   tree.Id));
            lineCmd.Parameters.Add(new SqlParameter("@ItemId",  line.ItemId));
            lineCmd.Parameters.Add(new SqlParameter("@ConfigId",(object?)line.ConfigId ?? DBNull.Value));
            lineCmd.Parameters.Add(new SqlParameter("@Qty",      line.Quantity));
            lineCmd.Parameters.Add(new SqlParameter("@Scrap",    line.ScrapRatio));
            lineCmd.Parameters.Add(new SqlParameter("@LineGuid", line.LineGuid == Guid.Empty ? Guid.NewGuid() : line.LineGuid));
            lineCmd.Parameters.Add(new SqlParameter("@Note",     (object?)line.Note ?? DBNull.Value));
            lineCmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)line.CreatedById ?? (object?)tree.UpdatedById ?? DBNull.Value));
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 2026-05-20: Routing.Code -> Routing.Id lookup. Standart rehber kullanildiginda
    /// frontend bridge input.value'ya Code stringini yaziyor; backend save oncesi
    /// int FK'yi cozmek icin bu metodu cagirir. Case-insensitive (collation default).
    /// </summary>
    public async Task<int?> GetRoutingIdByCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP 1 [Id] FROM [{_schema}].[Routing]
            WHERE [Code] = @Code AND [IsActive] = 1;
            """;
        command.Parameters.Add(new SqlParameter("@Code", code.Trim()));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull) return null;
        return Convert.ToInt32(result);
    }

    public async Task DeleteBOMAsync(int id, int? userId, CancellationToken cancellationToken)
    {
        // Soft delete — IsActive=0 + Updated audit izi. Lines tablosu dokunulmaz
        // (header IsActive=0 zaten WHERE filtresi ile satirlari da gizler).
        // Eski is emirleri orphan kalmaz, denetim icin tarih bilgisi korunur
        // (rapor 2026-05-17 madde 3.6).
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_productTreesTableName}
            SET [IsActive]    = 0,
                [Updated]     = SYSUTCDATETIME(),
                [UpdatedById] = @UpdatedById,
                [UpdatedAt]   = GETDATE()
            WHERE [Id] = @Id AND [IsActive] = 1;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@UpdatedById", (object?)userId ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /* ── Kit / Paket Urun ─────────────────────────────────────────── */

    public async Task<ItemKitDto?> GetKitByItemAsync(int itemId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // AKTIF kit = ayni ItemId icin MAX(Id) WHERE IsActive=1 (BOM ile ayni desen).
        // Items + ItemConfiguration JOIN ile enriched — frontend display icin
        // bilesen code/name + config code tasinir.
        command.CommandText = $"""
            SELECT
                k.[Id], k.[ItemId], ki.[code] AS KitItemCode, ISNULL(ki.[name], ki.[code]) AS KitItemName,
                k.[VersionNo], k.[PriceMode], k.[FixedPrice], k.[Description],
                l.[Id]       AS LineId,
                l.[ItemId]   AS LineItemId,
                ci.[code]    AS LineItemCode,
                ISNULL(ci.[name], ci.[code]) AS LineItemName,
                l.[ConfigId] AS LineConfigId,
                lcfg.[RecordCode] AS LineConfigCode,
                l.[Quantity],
                l.[LineGuid],
                l.[Note]     AS LineNote
            FROM [{_schema}].[ItemKit] k
            INNER JOIN {_stockCardsTableName} ki ON ki.[id] = k.[ItemId]
            LEFT  JOIN [{_schema}].[ItemKitLine] l  ON l.[ItemKitId] = k.[Id]
            LEFT  JOIN {_stockCardsTableName} ci ON ci.[id] = l.[ItemId]
            LEFT  JOIN [{_schema}].[ItemConfiguration] lcfg ON lcfg.[Id] = l.[ConfigId]
            WHERE k.[ItemId] = @ItemId
              AND k.[IsActive] = 1
              AND k.[Id] = (
                  SELECT MAX([Id]) FROM [{_schema}].[ItemKit]
                  WHERE [ItemId] = @ItemId AND [IsActive] = 1
              )
            ORDER BY l.[Id];
            """;
        command.Parameters.Add(new SqlParameter("@ItemId", itemId));

        ItemKitDto? result = null;
        var lines = new List<ItemKitLineDto>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (result is null)
            {
                result = new ItemKitDto(
                    Id:         reader.GetInt32(0),
                    ItemId:     reader.GetInt32(1),
                    ItemCode:   reader.GetString(2),
                    ItemName:   reader.GetString(3),
                    VersionNo:  reader.GetInt32(4),
                    PriceMode:  reader.GetString(5),
                    FixedPrice: reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    Description:reader.IsDBNull(7) ? null : reader.GetString(7),
                    Lines:      lines);
            }

            if (!reader.IsDBNull(8))
            {
                lines.Add(new ItemKitLineDto(
                    Id:        reader.GetInt32(8),
                    ItemKitId: result.Id,
                    ItemId:    reader.GetInt32(9),
                    ItemCode:  reader.IsDBNull(10) ? "" : reader.GetString(10),
                    ItemName:  reader.IsDBNull(11) ? "" : reader.GetString(11),
                    ConfigId:  reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    ConfigCode:reader.IsDBNull(13) ? null : reader.GetString(13),
                    Quantity:  reader.GetDecimal(14),
                    LineGuid:  reader.IsDBNull(15) ? Guid.Empty : reader.GetGuid(15),
                    Note:      reader.IsDBNull(16) ? null : reader.GetString(16)));
            }
        }

        return result;
    }

    public async Task<int> AddKitAsync(ItemKit kit, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        int newId;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = $"""
                INSERT INTO [{_schema}].[ItemKit]
                    ([ItemId],[VersionNo],[PriceMode],[FixedPrice],[Description],[IsActive],[CreatedById],[Created])
                VALUES
                    (@ItemId,1,@PriceMode,@FixedPrice,@Description,1,@CreatedById,SYSUTCDATETIME());
                SELECT CAST(SCOPE_IDENTITY() AS INT);
                """;
            cmd.Parameters.Add(new SqlParameter("@ItemId",      kit.ItemId));
            cmd.Parameters.Add(new SqlParameter("@PriceMode",   kit.PriceMode));
            cmd.Parameters.Add(new SqlParameter("@FixedPrice",  (object?)kit.FixedPrice  ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Description", (object?)kit.Description ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)kit.CreatedById ?? DBNull.Value));
            newId = (int)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        }

        foreach (var line in kit.Lines)
            await InsertKitLineAsync(connection, transaction, newId, line, kit.CreatedById, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return newId;
    }

    public async Task UpdateKitAsync(ItemKit kit, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            // VersionNo++ SQL tarafinda (revizyon — sonraki kullanimi etkiler).
            // ItemId degismez (kit karti sabit). Satirlar DELETE+INSERT.
            cmd.CommandText = $"""
                UPDATE [{_schema}].[ItemKit]
                SET [PriceMode]   = @PriceMode,
                    [FixedPrice]  = @FixedPrice,
                    [Description] = @Description,
                    [VersionNo]   = [VersionNo] + 1,
                    [Updated]     = SYSUTCDATETIME(),
                    [UpdatedById] = @UpdatedById
                WHERE [Id] = @Id;
                """;
            cmd.Parameters.Add(new SqlParameter("@Id",          kit.Id));
            cmd.Parameters.Add(new SqlParameter("@PriceMode",   kit.PriceMode));
            cmd.Parameters.Add(new SqlParameter("@FixedPrice",  (object?)kit.FixedPrice  ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@Description", (object?)kit.Description ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@UpdatedById", (object?)kit.UpdatedById ?? DBNull.Value));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCmd = connection.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = $"DELETE FROM [{_schema}].[ItemKitLine] WHERE [ItemKitId] = @ItemKitId;";
            deleteCmd.Parameters.Add(new SqlParameter("@ItemKitId", kit.Id));
            await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in kit.Lines)
            await InsertKitLineAsync(connection, transaction, kit.Id, line, kit.UpdatedById, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task InsertKitLineAsync(
        SqlConnection connection, SqlTransaction transaction, int kitId,
        ItemKitLine line, int? fallbackUserId, CancellationToken cancellationToken)
    {
        await using var lineCmd = connection.CreateCommand();
        lineCmd.Transaction = transaction;
        lineCmd.CommandText = $"""
            INSERT INTO [{_schema}].[ItemKitLine]
                ([ItemKitId],[ItemId],[ConfigId],[Quantity],[LineGuid],[Note],[CreatedById],[Created])
            VALUES
                (@ItemKitId,@ItemId,@ConfigId,@Qty,@LineGuid,@Note,@CreatedById,SYSUTCDATETIME());
            """;
        lineCmd.Parameters.Add(new SqlParameter("@ItemKitId", kitId));
        lineCmd.Parameters.Add(new SqlParameter("@ItemId",    line.ItemId));
        lineCmd.Parameters.Add(new SqlParameter("@ConfigId",  (object?)line.ConfigId ?? DBNull.Value));
        lineCmd.Parameters.Add(new SqlParameter("@Qty",       line.Quantity));
        lineCmd.Parameters.Add(new SqlParameter("@LineGuid",  line.LineGuid == Guid.Empty ? Guid.NewGuid() : line.LineGuid));
        lineCmd.Parameters.Add(new SqlParameter("@Note",      (object?)line.Note ?? DBNull.Value));
        lineCmd.Parameters.Add(new SqlParameter("@CreatedById", (object?)line.CreatedById ?? (object?)fallbackUserId ?? DBNull.Value));
        await lineCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteKitAsync(int itemId, int? userId, CancellationToken cancellationToken)
    {
        // Soft delete — ItemKit.IsActive=0. Lines dokunulmaz (header IsActive=0 zaten
        // WHERE filtresiyle gizler). Gecmis belgeler snapshot tasidigindan etkilenmez.
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE [{_schema}].[ItemKit]
            SET [IsActive]    = 0,
                [Updated]     = SYSUTCDATETIME(),
                [UpdatedById] = @UpdatedById
            WHERE [ItemId] = @ItemId AND [IsActive] = 1;
            """;
        command.Parameters.Add(new SqlParameter("@ItemId", itemId));
        command.Parameters.Add(new SqlParameter("@UpdatedById", (object?)userId ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<int>> GetBOMComponentItemIdsAsync(
        int parentItemId, CancellationToken cancellationToken)
    {
        // En son aktif BOM'un (en yuksek Id) bilesen ItemId'lerini doner.
        // Cycle detection icin domain BFS bunu lazy cagirir.
        if (parentItemId <= 0) return Array.Empty<int>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT DISTINCT l.[ItemId]
            FROM {_productTreeLinesTableName} l
            INNER JOIN {_productTreesTableName} t ON t.[Id] = l.[BOMId]
            WHERE t.[ItemId] = @ParentItemId
              AND t.[IsActive] = 1
              AND t.[Id] = (
                  SELECT MAX([Id]) FROM {_productTreesTableName}
                  WHERE [ItemId] = @ParentItemId AND [IsActive] = 1
              );
            """;
        command.Parameters.Add(new SqlParameter("@ParentItemId", parentItemId));

        var ids = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0)) ids.Add(reader.GetInt32(0));
        }
        return ids;
    }

    public async Task<IReadOnlyCollection<BOMComponentLineRow>> GetBOMComponentLinesAsync(
        int parentItemId, CancellationToken cancellationToken)
    {
        // Explode patlatma icin: line satirlarini Qty+Scrap+ConfigId ile doner.
        // ItemId 0 olan veya orphan line atilir; ayni (ItemId,ConfigId) satiri
        // SaveBOMAsync.EnsureValid sayesinde mukerrer olmamasi gerekir, ama
        // defansif: tekrar gelirse iki ayri satir olarak dondurulur, service
        // aggregate eder.
        if (parentItemId <= 0) return Array.Empty<BOMComponentLineRow>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT l.[ItemId], l.[ConfigId], l.[Quantity], l.[ScrapRatio]
            FROM {_productTreeLinesTableName} l
            INNER JOIN {_productTreesTableName} t ON t.[Id] = l.[BOMId]
            WHERE t.[ItemId] = @ParentItemId
              AND t.[IsActive] = 1
              AND t.[Id] = (
                  SELECT MAX([Id]) FROM {_productTreesTableName}
                  WHERE [ItemId] = @ParentItemId AND [IsActive] = 1
              )
              AND l.[ItemId] IS NOT NULL
              AND l.[ItemId] > 0;
            """;
        command.Parameters.Add(new SqlParameter("@ParentItemId", parentItemId));

        var rows = new List<BOMComponentLineRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new BOMComponentLineRow(
                ItemId:     reader.GetInt32(0),
                ConfigId:   reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Quantity:   reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
                ScrapRatio: reader.IsDBNull(3) ? 0m : reader.GetDecimal(3)));
        }
        return rows;
    }

    public async Task<IReadOnlyCollection<WhereUsedItemDto>> GetWhereUsedAsync(
        int componentItemId, CancellationToken cancellationToken)
    {
        // Where-used (ters arama, 1-seviye): bu bileseni dogrudan kullanan
        // aktif BOM'larin parent bilgisi. Items + ItemConfiguration JOIN ile
        // display field'lari da gelir — UI single-call ile listeyi gosterebilir.
        if (componentItemId <= 0) return Array.Empty<WhereUsedItemDto>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT t.[Id]            AS BOMId,
                   t.[ItemId]        AS ParentItemId,
                   pi.[code]         AS ParentItemCode,
                   ISNULL(pi.[name], pi.[code]) AS ParentItemName,
                   t.[ConfigId]      AS ParentConfigId,
                   pcfg.[RecordCode] AS ParentConfigCode,
                   l.[Quantity],
                   l.[ScrapRatio]
            FROM {_productTreeLinesTableName} l
            INNER JOIN {_productTreesTableName} t  ON t.[Id]  = l.[BOMId]
            INNER JOIN {_stockCardsTableName}  pi  ON pi.[id] = t.[ItemId]
            LEFT  JOIN [{_schema}].[ItemConfiguration] pcfg ON pcfg.[Id] = t.[ConfigId]
            WHERE l.[ItemId]  = @ComponentItemId
              AND t.[IsActive] = 1
            ORDER BY pi.[code];
            """;
        command.Parameters.Add(new SqlParameter("@ComponentItemId", componentItemId));

        var rows = new List<WhereUsedItemDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new WhereUsedItemDto(
                BOMId:            reader.GetInt32(0),
                ParentItemId:     reader.GetInt32(1),
                ParentItemCode:   reader.GetString(2),
                ParentItemName:   reader.GetString(3),
                ParentConfigId:   reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ParentConfigCode: reader.IsDBNull(5) ? null : reader.GetString(5),
                Quantity:         reader.IsDBNull(6) ? 0m : reader.GetDecimal(6),
                ScrapRatio:       reader.IsDBNull(7) ? 0m : reader.GetDecimal(7)));
        }
        return rows;
    }

    public async Task<IReadOnlyCollection<CombinationLookupRow>> GetCombinationsByMaterialCodeAsync(
        string materialCode,
        CancellationToken cancellationToken)
    {
        // Adım 1: Kök CONFIG kayıtlarını çek (ParentId IS NULL)
        var combos = new Dictionary<int, CombinationLookupRow>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = $"""
            SELECT [Id], [RecordCode], [RecordName]
            FROM {_itemConfigurationTableName}
            WHERE [RecordType] = 'CONFIG'
              AND [ParentId] IS NULL
              AND [RelatedMaterialCode] = @mc
              AND [IsActive] = 1
            ORDER BY [RecordCode];
            """;
        var p1 = cmd1.CreateParameter(); p1.ParameterName = "@mc"; p1.Value = materialCode;
        cmd1.Parameters.Add(p1);

        await using (var r1 = await cmd1.ExecuteReaderAsync(cancellationToken))
        {
            while (await r1.ReadAsync(cancellationToken))
            {
                var configId = r1.GetInt32(0);
                combos[configId] = new CombinationLookupRow(
                    configId,
                    r1.GetString(1),
                    r1.IsDBNull(2) ? r1.GetString(1) : r1.GetString(2),
                    new List<CombinationFeatureValueDto>());
            }
        }

        if (combos.Count == 0) return Array.Empty<CombinationLookupRow>();

        // Adım 2: Child CONFIG → FeatureValue → ItemFeature join ile özellik-değer çiftleri.
        // Schema notu (CalibraHub gerçeği):
        //   - parent CONFIG (kombinasyon kodu) → ItemConfiguration, ParentId NULL
        //   - child CONFIG (her özellik için) → ItemConfiguration, ParentId=parent, RecordType='CONFIG',
        //     RecordName=FeatureValue.id (string olarak)
        //   - VALUE master → FeatureValue tablosu (id, feature_id, code, description, value)
        //   - FEATURE master → ItemFeature tablosu (Id, Name)
        // FeatureValueId match/dedup icin kullanilir (CLAUDE.md id tabanli kural).
        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = $"""
            SELECT
                child.[ParentId]                                      AS ConfigId,
                fv.[Id]                                               AS FeatureValueId,
                f.[Name]                                              AS FeatureName,
                COALESCE(NULLIF(fv.[Description], N''), fv.[Value])   AS ValueDesc,
                fv.[Code]                                             AS ValueCode
            FROM {_itemConfigurationTableName} child
            JOIN [{_schema}].[FeatureValue] fv
                ON fv.[Id] = TRY_CAST(child.[RecordName] AS INT)
            JOIN {_propertiesTableName} f
                ON f.[Id] = fv.[FeatureId]
            WHERE child.[RecordType] = 'CONFIG'
              AND child.[ParentId] IN ({string.Join(",", combos.Keys)})
            ORDER BY child.[ParentId], f.[Name];
            """;

        await using (var r2 = await cmd2.ExecuteReaderAsync(cancellationToken))
        {
            while (await r2.ReadAsync(cancellationToken))
            {
                var configId       = r2.GetInt32(0);
                var featureValueId = r2.GetInt32(1);
                var feature        = r2.IsDBNull(2) ? "" : r2.GetString(2);
                var valueDesc      = r2.IsDBNull(3) ? "" : r2.GetString(3);
                var valueCode      = r2.IsDBNull(4) ? "" : r2.GetString(4);
                if (combos.TryGetValue(configId, out var row))
                    ((List<CombinationFeatureValueDto>)row.FeatureValues).Add(
                        new CombinationFeatureValueDto(featureValueId, feature, valueDesc, valueCode));
            }
        }

        return combos.Values.ToList();
    }

    /// <summary>
    /// "Tanımlı Kombinasyonlar" liste ekranı (SmartBoard) için tüm kombinasyonları
    /// parent stok bilgisiyle ve özellik/değer ayrıntısıyla beraber döner.
    /// 2 SQL: kök CONFIG + Items JOIN, sonra child rows + FeatureValue + ItemFeature.
    /// </summary>
    public async Task<IReadOnlyCollection<CombinationListItemDto>> GetAllCombinationsAsync(CancellationToken cancellationToken)
    {
        var rows = new Dictionary<int, (string Code, string? Name, int? ItemId, string? ItemCode, string? ItemName, bool IsActive, DateTime Created, List<CombinationFeatureValueDto> Features)>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // 1) Tüm aktif kök CONFIG (kombinasyon header) + parent Items JOIN
        await using (var cmd1 = connection.CreateCommand())
        {
            cmd1.CommandText = $"""
                SELECT cfg.[Id], cfg.[RecordCode], cfg.[RecordName], cfg.[IsActive], cfg.[Created],
                       i.[Id] AS ItemId, i.[Code] AS ItemCode, i.[Name] AS ItemName
                FROM {_itemConfigurationTableName} cfg
                LEFT JOIN {_stockCardsTableName} i ON i.[Code] = cfg.[RelatedMaterialCode]
                WHERE cfg.[RecordType] = 'CONFIG'
                  AND cfg.[ParentId] IS NULL
                  AND cfg.[IsActive] = 1
                ORDER BY i.[Code], cfg.[RecordCode];
                """;
            await using var r1 = await cmd1.ExecuteReaderAsync(cancellationToken);
            while (await r1.ReadAsync(cancellationToken))
            {
                var configId = r1.GetInt32(0);
                rows[configId] = (
                    Code:     r1.GetString(1),
                    Name:     r1.IsDBNull(2) ? null : r1.GetString(2),
                    ItemId:   r1.IsDBNull(5) ? null : r1.GetInt32(5),
                    ItemCode: r1.IsDBNull(6) ? null : r1.GetString(6),
                    ItemName: r1.IsDBNull(7) ? null : r1.GetString(7),
                    IsActive: r1.GetBoolean(3),
                    Created:  r1.GetDateTime(4),
                    Features: new List<CombinationFeatureValueDto>());
            }
        }

        if (rows.Count == 0) return Array.Empty<CombinationListItemDto>();

        // 2) Child CONFIG (her özellik) → FeatureValue + ItemFeature
        await using (var cmd2 = connection.CreateCommand())
        {
            cmd2.CommandText = $"""
                SELECT
                    child.[ParentId]                                      AS ConfigId,
                    fv.[Id]                                               AS FeatureValueId,
                    f.[Name]                                              AS FeatureName,
                    COALESCE(NULLIF(fv.[Description], N''), fv.[Value])   AS ValueDesc,
                    fv.[Code]                                             AS ValueCode
                FROM {_itemConfigurationTableName} child
                JOIN [{_schema}].[FeatureValue] fv
                    ON fv.[Id] = TRY_CAST(child.[RecordName] AS INT)
                JOIN {_propertiesTableName} f
                    ON f.[Id] = fv.[FeatureId]
                WHERE child.[RecordType] = 'CONFIG'
                  AND child.[ParentId] IN ({string.Join(",", rows.Keys)})
                ORDER BY child.[ParentId], f.[Name];
                """;
            await using var r2 = await cmd2.ExecuteReaderAsync(cancellationToken);
            while (await r2.ReadAsync(cancellationToken))
            {
                var configId       = r2.GetInt32(0);
                var featureValueId = r2.GetInt32(1);
                var feature        = r2.IsDBNull(2) ? "" : r2.GetString(2);
                var valueDesc      = r2.IsDBNull(3) ? "" : r2.GetString(3);
                var valueCode      = r2.IsDBNull(4) ? "" : r2.GetString(4);
                if (rows.TryGetValue(configId, out var row))
                    row.Features.Add(new CombinationFeatureValueDto(featureValueId, feature, valueDesc, valueCode));
            }
        }

        return rows.Select(kv => new CombinationListItemDto(
            ConfigId:      kv.Key,
            Code:          kv.Value.Code,
            Name:          kv.Value.Name,
            ItemId:        kv.Value.ItemId,
            ItemCode:      kv.Value.ItemCode,
            ItemName:      kv.Value.ItemName,
            IsActive:      kv.Value.IsActive,
            CreatedDate:   kv.Value.Created,
            FeatureValues: kv.Value.Features
        )).ToList();
    }

    public async Task<IReadOnlyCollection<MaterialGroup>> GetMaterialGroupsAsync(int? category, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = category.HasValue
            ? $"SELECT [Id],[GroupCategory],[GroupCode],[GroupDescription] FROM {_materialGroupsTableName} WHERE [GroupCategory]=@Cat ORDER BY [GroupCode];"
            : $"SELECT [Id],[GroupCategory],[GroupCode],[GroupDescription] FROM {_materialGroupsTableName} ORDER BY [GroupCategory],[GroupCode];";
        if (category.HasValue)
            command.Parameters.Add(new SqlParameter("@Cat", category.Value));
        var result = new List<MaterialGroup>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new MaterialGroup
            {
                Id              = reader.GetInt32(0),
                GroupCategory   = reader.GetByte(1),
                GroupCode       = reader.GetString(2),
                GroupDescription = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        return result;
    }

    public async Task AddMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {_materialGroupsTableName} ([GroupCategory],[GroupCode],[GroupDescription]) VALUES (@Cat,@Code,@Desc);";
        command.Parameters.Add(new SqlParameter("@Cat",  group.GroupCategory));
        command.Parameters.Add(new SqlParameter("@Code", group.GroupCode));
        command.Parameters.Add(new SqlParameter("@Desc", (object?)group.GroupDescription ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateMaterialGroupAsync(MaterialGroup group, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_materialGroupsTableName} SET [GroupCategory]=@Cat,[GroupCode]=@Code,[GroupDescription]=@Desc WHERE [Id]=@Id;";
        command.Parameters.Add(new SqlParameter("@Id",  group.Id));
        command.Parameters.Add(new SqlParameter("@Cat", group.GroupCategory));
        command.Parameters.Add(new SqlParameter("@Code", group.GroupCode));
        command.Parameters.Add(new SqlParameter("@Desc", (object?)group.GroupDescription ?? DBNull.Value));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteMaterialGroupAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_materialGroupsTableName} WHERE [Id]=@Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<MaterialGroupMappingDto>> GetMaterialGroupMappingsAsync(int stockCardId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT m.[SlotOrder], m.[GroupCode], g.[GroupDescription]
            FROM {_materialGroupMappingsTableName} m
            LEFT JOIN {_materialGroupsTableName} g ON g.[GroupCode] = m.[GroupCode] AND g.[GroupCategory] = m.[SlotOrder]
            WHERE m.[ItemId] = @ItemId
            ORDER BY m.[SlotOrder];
            """;
        command.Parameters.Add(new SqlParameter("@ItemId", stockCardId));
        var result = new List<MaterialGroupMappingDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new MaterialGroupMappingDto(reader.GetByte(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return result;
    }

    /// <summary>
    /// 2026-05-24: ItemUnit batch — filter "Olcu Birimi" alani icin tek IN-clause sorgusu.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<ItemUnit>>> GetItemUnitsBatchAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, IReadOnlyList<ItemUnit>>();
        if (itemIds == null || itemIds.Count == 0) return result;

        var ids = itemIds.Distinct().ToArray();
        var paramNames = new string[ids.Length];
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureItemUnitsTableAsync(connection, cancellationToken);
        await using var cmd = connection.CreateCommand();
        for (int i = 0; i < ids.Length; i++)
        {
            var p = "@ItemId" + i;
            paramNames[i] = p;
            cmd.Parameters.Add(new SqlParameter(p, ids[i]));
        }
        cmd.CommandText = $"""
            SELECT [Id],[ItemId],[LineNo],[UnitId],[Multiplier]
            FROM {_itemUnitsTableName}
            WHERE [ItemId] IN ({string.Join(",", paramNames)})
            ORDER BY [ItemId], [LineNo];
            """;
        var bucket = new Dictionary<int, List<ItemUnit>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemId = reader.GetInt32(1);
            if (!bucket.TryGetValue(itemId, out var list))
            {
                list = new List<ItemUnit>();
                bucket[itemId] = list;
            }
            list.Add(new ItemUnit
            {
                Id = reader.GetInt32(0),
                ItemId = itemId,
                LineNo = reader.GetInt32(2),
                UnitId = reader.GetInt32(3),
                Multiplier = reader.GetDecimal(4),
            });
        }
        foreach (var kvp in bucket) result[kvp.Key] = kvp.Value;
        return result;
    }

    /// <summary>
    /// 2026-05-24: ItemFeatureMapping batch — filter "Ozellikler" alani icin tek IN-clause.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<ItemFeatureMapping>>> GetItemFeatureMappingsBatchAsync(
        IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, IReadOnlyList<ItemFeatureMapping>>();
        if (itemIds == null || itemIds.Count == 0) return result;

        var ids = itemIds.Distinct().ToArray();
        var paramNames = new string[ids.Length];
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        for (int i = 0; i < ids.Length; i++)
        {
            var p = "@ItemId" + i;
            paramNames[i] = p;
            cmd.Parameters.Add(new SqlParameter(p, ids[i]));
        }
        cmd.CommandText = $"""
            SELECT [Id], [ItemId], [FeatureId], [FeatureValueId], [IsActive], [Created],
                   ISNULL([PrintDescriptionInDesign], 1) AS [PrintDescriptionInDesign]
            FROM {_itemFeatureMappingsTableName}
            WHERE [ItemId] IN ({string.Join(",", paramNames)})
            ORDER BY [ItemId], [Id];
            """;
        var bucket = new Dictionary<int, List<ItemFeatureMapping>>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemId = reader.GetInt32(1);
            var mapping = new ItemFeatureMapping
            {
                Id = reader.GetInt32(0),
                ItemId = itemId,
                FeatureId = reader.GetInt32(2),
                FeatureValueId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                CreatedAt = reader.GetFieldValue<DateTime>(5),
                PrintDescriptionInDesign = reader.GetBoolean(6),
            };
            if (!reader.GetBoolean(4)) mapping.Deactivate();
            if (!bucket.TryGetValue(itemId, out var list))
            {
                list = new List<ItemFeatureMapping>();
                bucket[itemId] = list;
            }
            list.Add(mapping);
        }
        foreach (var kvp in bucket) result[kvp.Key] = kvp.Value;
        return result;
    }

    /// <summary>
    /// 2026-05-24: Batch query — N+1 onlemek icin. Ornek 50 item listesi icin
    /// tek IN-clause sorgusu. Bos liste icin bos sozluk doner.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<MaterialGroupMappingDto>>> GetMaterialGroupMappingsBatchAsync(
        IReadOnlyCollection<int> stockCardIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, IReadOnlyList<MaterialGroupMappingDto>>();
        if (stockCardIds == null || stockCardIds.Count == 0) return result;

        // IN clause icin parametrik liste (SQL injection guvenli)
        var ids = stockCardIds.Distinct().ToArray();
        var paramNames = new string[ids.Length];
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        for (int i = 0; i < ids.Length; i++)
        {
            var p = "@ItemId" + i;
            paramNames[i] = p;
            command.Parameters.Add(new SqlParameter(p, ids[i]));
        }
        command.CommandText = $"""
            SELECT m.[ItemId], m.[SlotOrder], m.[GroupCode], g.[GroupDescription]
            FROM {_materialGroupMappingsTableName} m
            LEFT JOIN {_materialGroupsTableName} g ON g.[GroupCode] = m.[GroupCode] AND g.[GroupCategory] = m.[SlotOrder]
            WHERE m.[ItemId] IN ({string.Join(",", paramNames)})
            ORDER BY m.[ItemId], m.[SlotOrder];
            """;

        // ItemId -> mapping list (sıralı)
        var bucket = new Dictionary<int, List<MaterialGroupMappingDto>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemId = reader.GetInt32(0);
            var dto = new MaterialGroupMappingDto(
                reader.GetByte(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
            if (!bucket.TryGetValue(itemId, out var list))
            {
                list = new List<MaterialGroupMappingDto>();
                bucket[itemId] = list;
            }
            list.Add(dto);
        }
        foreach (var kvp in bucket) result[kvp.Key] = kvp.Value;
        return result;
    }

    public async Task SaveMaterialGroupMappingsAsync(int stockCardId, IReadOnlyCollection<(int Slot, string Code)> mappings, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var delCmd = connection.CreateCommand())
        {
            delCmd.Transaction = transaction;
            delCmd.CommandText = $"DELETE FROM {_materialGroupMappingsTableName} WHERE [ItemId]=@ItemId;";
            delCmd.Parameters.Add(new SqlParameter("@ItemId", stockCardId));
            await delCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (slot, code) in mappings)
        {
            await using var insCmd = connection.CreateCommand();
            insCmd.Transaction = transaction;
            insCmd.CommandText = $"INSERT INTO {_materialGroupMappingsTableName} ([ItemId],[SlotOrder],[GroupCode]) VALUES (@ItemId,@Slot,@Code);";
            insCmd.Parameters.Add(new SqlParameter("@ItemId", stockCardId));
            insCmd.Parameters.Add(new SqlParameter("@Slot",        slot));
            insCmd.Parameters.Add(new SqlParameter("@Code",        code));
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

}
