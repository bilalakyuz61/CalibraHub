using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// Varlık Yönetimi veri erişimi — Asset + AssetEvent tabloları. Machine deseni
/// (SqlLogisticsConfigurationRepository) ile aynı: per-company DB, CompanyId filtreli.
/// </summary>
public sealed class SqlAssetRepository : IAssetRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly IDataVisibilityFilter _dvFilter;
    private readonly string _assetTable;
    private readonly string _eventTable;
    private readonly string _assignmentTable;

    public SqlAssetRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options, IDataVisibilityFilter dvFilter)
    {
        _connectionFactory = connectionFactory;
        _dvFilter = dvFilter;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _assetTable = $"[{schema}].[Asset]";
        _eventTable = $"[{schema}].[AssetEvent]";
        _assignmentTable = $"[{schema}].[AssetAssignment]";
    }

    private const string AssetColumns = """
        [Id],[CompanyId],[AssetCode],[AssetName],[Description],[Kind],
        [LocationId],[DepartmentId],[AssignedPersonnelId],[MachineId],
        [SerialNo],[AcquisitionDate],[WarrantyExpiryDate],
        [MaintenancePeriodDays],[LastMaintenanceDate],[NextMaintenanceDate],[MaintenanceRemindedFor],
        [CalibrationPeriodDays],[LastCalibrationDate],[NextCalibrationDate],[CalibrationRemindedFor],
        [Status],[SortOrder],[IsActive],[Created],[Updated],[CreatedById],[UpdatedById],
        [IsMaintained],[IsCalibrated],
        [IpAddress],[Hostname],[OperatingSystem],[MacAddress],[NetworkDomain],[PlateNo],
        [IsAssignable],[MaintenancePeriodUnit],[CalibrationPeriodUnit]
        """;

    // ── Asset ─────────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<Asset>> GetAssetsAsync(CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Satır görünürlük kuralları (row-level security) — tek tablo, alias 'a'.
        var dv = await _dvFilter.BuildAsync(FormCodes.Assets, "a", "Id", cancellationToken);
        command.CommandText = $"""
            SELECT {AssetColumns}
            FROM {_assetTable} a
            WHERE [CompanyId] = @CompanyId
            {dv.Sql}
            ORDER BY [SortOrder], [AssetName];
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        foreach (var prm in dv.Parameters) command.Parameters.Add(new SqlParameter(prm.Name, prm.Value));
        return await ReadAssetsAsync(command, cancellationToken);
    }

    public async Task<Asset?> GetAssetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP 1 {AssetColumns}
            FROM {_assetTable}
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        var rows = await ReadAssetsAsync(command, cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<Asset?> GetAssetByMachineIdAsync(int machineId, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP 1 {AssetColumns}
            FROM {_assetTable}
            WHERE [MachineId] = @MachineId AND [CompanyId] = @CompanyId;
            """;
        command.Parameters.Add(new SqlParameter("@MachineId", machineId));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        var rows = await ReadAssetsAsync(command, cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<int> AddAssetAsync(Asset asset, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_assetTable}
                ([CompanyId],[AssetCode],[AssetName],[Description],[Kind],
                 [LocationId],[DepartmentId],[AssignedPersonnelId],[MachineId],
                 [SerialNo],[AcquisitionDate],[WarrantyExpiryDate],
                 [MaintenancePeriodDays],[LastMaintenanceDate],[NextMaintenanceDate],[MaintenanceRemindedFor],
                 [CalibrationPeriodDays],[LastCalibrationDate],[NextCalibrationDate],[CalibrationRemindedFor],
                 [Status],[SortOrder],[IsActive],[IsMaintained],[IsCalibrated],
                 [IpAddress],[Hostname],[OperatingSystem],[MacAddress],[NetworkDomain],[PlateNo],[IsAssignable],
                 [MaintenancePeriodUnit],[CalibrationPeriodUnit],[Created],[CreatedById])
            VALUES
                (@CompanyId,@AssetCode,@AssetName,@Description,@Kind,
                 @LocationId,@DepartmentId,@AssignedPersonnelId,@MachineId,
                 @SerialNo,@AcquisitionDate,@WarrantyExpiryDate,
                 @MaintenancePeriodDays,@LastMaintenanceDate,@NextMaintenanceDate,@MaintenanceRemindedFor,
                 @CalibrationPeriodDays,@LastCalibrationDate,@NextCalibrationDate,@CalibrationRemindedFor,
                 @Status,@SortOrder,@IsActive,@IsMaintained,@IsCalibrated,
                 @IpAddress,@Hostname,@OperatingSystem,@MacAddress,@NetworkDomain,@PlateNo,@IsAssignable,
                 @MaintenancePeriodUnit,@CalibrationPeriodUnit,SYSUTCDATETIME(),@CreatedById);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        BindAssetParameters(command, asset, companyId, isInsert: true);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task UpdateAssetAsync(Asset asset, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_assetTable}
            SET [AssetCode]              = @AssetCode,
                [AssetName]              = @AssetName,
                [Description]            = @Description,
                [Kind]                   = @Kind,
                [LocationId]             = @LocationId,
                [DepartmentId]           = @DepartmentId,
                [AssignedPersonnelId]    = @AssignedPersonnelId,
                [MachineId]              = @MachineId,
                [SerialNo]               = @SerialNo,
                [AcquisitionDate]        = @AcquisitionDate,
                [WarrantyExpiryDate]     = @WarrantyExpiryDate,
                [MaintenancePeriodDays]  = @MaintenancePeriodDays,
                [LastMaintenanceDate]    = @LastMaintenanceDate,
                [NextMaintenanceDate]    = @NextMaintenanceDate,
                [MaintenanceRemindedFor] = @MaintenanceRemindedFor,
                [CalibrationPeriodDays]  = @CalibrationPeriodDays,
                [LastCalibrationDate]    = @LastCalibrationDate,
                [NextCalibrationDate]    = @NextCalibrationDate,
                [CalibrationRemindedFor] = @CalibrationRemindedFor,
                [Status]                 = @Status,
                [SortOrder]              = @SortOrder,
                [IsActive]               = @IsActive,
                [IsMaintained]           = @IsMaintained,
                [IsCalibrated]           = @IsCalibrated,
                [IpAddress]              = @IpAddress,
                [Hostname]               = @Hostname,
                [OperatingSystem]        = @OperatingSystem,
                [MacAddress]             = @MacAddress,
                [NetworkDomain]          = @NetworkDomain,
                [PlateNo]                = @PlateNo,
                [IsAssignable]           = @IsAssignable,
                [MaintenancePeriodUnit]  = @MaintenancePeriodUnit,
                [CalibrationPeriodUnit]  = @CalibrationPeriodUnit,
                [Updated]                = SYSUTCDATETIME(),
                [UpdatedById]            = @UpdatedById
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;
        BindAssetParameters(command, asset, companyId, isInsert: false);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAssetAsync(int id, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var delEvents = connection.CreateCommand())
        {
            delEvents.Transaction = tx;
            delEvents.CommandText = $"DELETE FROM {_eventTable} WHERE [AssetId] = @Id AND [CompanyId] = @CompanyId;";
            delEvents.Parameters.Add(new SqlParameter("@Id", id));
            delEvents.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await delEvents.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var delAssignments = connection.CreateCommand())
        {
            delAssignments.Transaction = tx;
            delAssignments.CommandText = $"DELETE FROM {_assignmentTable} WHERE [AssetId] = @Id AND [CompanyId] = @CompanyId;";
            delAssignments.Parameters.Add(new SqlParameter("@Id", id));
            delAssignments.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await delAssignments.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var delAsset = connection.CreateCommand())
        {
            delAsset.Transaction = tx;
            delAsset.CommandText = $"DELETE FROM {_assetTable} WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
            delAsset.Parameters.Add(new SqlParameter("@Id", id));
            delAsset.Parameters.Add(new SqlParameter("@CompanyId", companyId));
            await delAsset.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
    }

    // ── AssetEvent ────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<AssetEvent>> GetEventsByAssetAsync(int assetId, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],[CompanyId],[AssetId],[EventType],[EventDate],
                   [PerformedByPersonnelId],[PerformedByText],[Cost],[Result],[Notes],
                   [NextDueDate],[DocumentUrl],[Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_eventTable}
            WHERE [AssetId] = @AssetId AND [CompanyId] = @CompanyId
            ORDER BY [EventDate] DESC, [Id] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@AssetId", assetId));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        var rows = new List<AssetEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new AssetEvent
            {
                Id = reader.GetInt32(0),
                CompanyId = reader.GetInt32(1),
                AssetId = reader.GetInt32(2),
                EventType = (AssetEventType)reader.GetByte(3),
                EventDate = reader.GetDateTime(4),
                PerformedByPersonnelId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                PerformedByText = reader.IsDBNull(6) ? null : reader.GetString(6),
                Cost = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                Result = (AssetEventResult)reader.GetByte(8),
                Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
                NextDueDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                DocumentUrl = reader.IsDBNull(11) ? null : reader.GetString(11),
                Created = reader.GetDateTime(12),
                Updated = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                CreatedById = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                UpdatedById = reader.IsDBNull(15) ? null : reader.GetInt32(15),
            });
        }
        return rows;
    }

    public async Task<AssetEvent?> GetEventByIdAsync(int id, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id],[CompanyId],[AssetId],[EventType],[EventDate],
                   [PerformedByPersonnelId],[PerformedByText],[Cost],[Result],[Notes],
                   [NextDueDate],[DocumentUrl],[Created],[Updated],[CreatedById],[UpdatedById]
            FROM {_eventTable}
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new AssetEvent
        {
            Id = reader.GetInt32(0),
            CompanyId = reader.GetInt32(1),
            AssetId = reader.GetInt32(2),
            EventType = (AssetEventType)reader.GetByte(3),
            EventDate = reader.GetDateTime(4),
            PerformedByPersonnelId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            PerformedByText = reader.IsDBNull(6) ? null : reader.GetString(6),
            Cost = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
            Result = (AssetEventResult)reader.GetByte(8),
            Notes = reader.IsDBNull(9) ? null : reader.GetString(9),
            NextDueDate = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            DocumentUrl = reader.IsDBNull(11) ? null : reader.GetString(11),
            Created = reader.GetDateTime(12),
            Updated = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            CreatedById = reader.IsDBNull(14) ? null : reader.GetInt32(14),
            UpdatedById = reader.IsDBNull(15) ? null : reader.GetInt32(15),
        };
    }

    public async Task<int> AddEventAsync(AssetEvent assetEvent, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_eventTable}
                ([CompanyId],[AssetId],[EventType],[EventDate],[PerformedByPersonnelId],[PerformedByText],
                 [Cost],[Result],[Notes],[NextDueDate],[DocumentUrl],[Created],[CreatedById])
            VALUES
                (@CompanyId,@AssetId,@EventType,@EventDate,@PerformedByPersonnelId,@PerformedByText,
                 @Cost,@Result,@Notes,@NextDueDate,@DocumentUrl,SYSUTCDATETIME(),@CreatedById);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", assetEvent.CompanyId > 0 ? assetEvent.CompanyId : companyId));
        command.Parameters.Add(new SqlParameter("@AssetId", assetEvent.AssetId));
        command.Parameters.Add(new SqlParameter("@EventType", (byte)assetEvent.EventType));
        command.Parameters.Add(new SqlParameter("@EventDate", assetEvent.EventDate));
        command.Parameters.Add(new SqlParameter("@PerformedByPersonnelId", (object?)assetEvent.PerformedByPersonnelId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@PerformedByText", (object?)assetEvent.PerformedByText ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Cost", (object?)assetEvent.Cost ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Result", (byte)assetEvent.Result));
        command.Parameters.Add(new SqlParameter("@Notes", (object?)assetEvent.Notes ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@NextDueDate", (object?)assetEvent.NextDueDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@DocumentUrl", (object?)assetEvent.DocumentUrl ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CreatedById", (object?)assetEvent.CreatedById ?? DBNull.Value));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task DeleteEventAsync(int id, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_eventTable} WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Zimmet hareketi (AssetAssignment) ─────────────────────────

    private const string AssignmentColumns = """
        [Id],[CompanyId],[AssetId],[PersonnelId],[AssignDate],[ReturnDate],
        [AssignNote],[ReturnNote],[DocumentNo],[Created],[Updated],[CreatedById],[UpdatedById],
        [DepartmentId],[LocationId]
        """;

    private static AssetAssignment MapAssignment(SqlDataReader r) => new()
    {
        Id = r.GetInt32(0),
        CompanyId = r.GetInt32(1),
        AssetId = r.GetInt32(2),
        PersonnelId = r.IsDBNull(3) ? null : r.GetInt32(3),
        AssignDate = r.GetDateTime(4),
        ReturnDate = r.IsDBNull(5) ? null : r.GetDateTime(5),
        AssignNote = r.IsDBNull(6) ? null : r.GetString(6),
        ReturnNote = r.IsDBNull(7) ? null : r.GetString(7),
        DocumentNo = r.IsDBNull(8) ? null : r.GetString(8),
        Created = r.GetDateTime(9),
        Updated = r.IsDBNull(10) ? null : r.GetDateTime(10),
        CreatedById = r.IsDBNull(11) ? null : r.GetInt32(11),
        UpdatedById = r.IsDBNull(12) ? null : r.GetInt32(12),
        DepartmentId = r.IsDBNull(13) ? null : r.GetInt32(13),
        LocationId = r.IsDBNull(14) ? null : r.GetInt32(14),
    };

    public async Task<IReadOnlyCollection<AssetAssignment>> GetAssignmentsByAssetAsync(int assetId, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {AssignmentColumns} FROM {_assignmentTable}
            WHERE [AssetId] = @AssetId AND [CompanyId] = @CompanyId
            ORDER BY [AssignDate] DESC, [Id] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@AssetId", assetId));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        var rows = new List<AssetAssignment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(MapAssignment(reader));
        return rows;
    }

    public async Task<IReadOnlyCollection<AssetAssignment>> GetAllAssignmentsAsync(CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Satır görünürlük kuralları (row-level security) — atama tablosu, alias 'aa'.
        var dv = await _dvFilter.BuildAsync(FormCodes.Assets, "aa", "Id", cancellationToken);
        command.CommandText = $"""
            SELECT {AssignmentColumns} FROM {_assignmentTable} aa
            WHERE [CompanyId] = @CompanyId
            {dv.Sql}
            ORDER BY [AssignDate] DESC, [Id] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        foreach (var prm in dv.Parameters) command.Parameters.Add(new SqlParameter(prm.Name, prm.Value));
        var rows = new List<AssetAssignment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(MapAssignment(reader));
        return rows;
    }

    public async Task<AssetAssignment?> GetActiveAssignmentAsync(int assetId, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP 1 {AssignmentColumns} FROM {_assignmentTable}
            WHERE [AssetId] = @AssetId AND [CompanyId] = @CompanyId AND [ReturnDate] IS NULL
            ORDER BY [Id] DESC;
            """;
        command.Parameters.Add(new SqlParameter("@AssetId", assetId));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapAssignment(reader) : null;
    }

    public async Task<AssetAssignment?> GetAssignmentByIdAsync(int id, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TOP 1 {AssignmentColumns} FROM {_assignmentTable} WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapAssignment(reader) : null;
    }

    public async Task<int> AddAssignmentAsync(AssetAssignment a, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_assignmentTable}
                ([CompanyId],[AssetId],[PersonnelId],[DepartmentId],[LocationId],[AssignDate],[ReturnDate],[AssignNote],[ReturnNote],[DocumentNo],[Created],[CreatedById])
            VALUES
                (@CompanyId,@AssetId,@PersonnelId,@DepartmentId,@LocationId,@AssignDate,@ReturnDate,@AssignNote,@ReturnNote,@DocumentNo,SYSUTCDATETIME(),@CreatedById);
            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", a.CompanyId > 0 ? a.CompanyId : companyId));
        command.Parameters.Add(new SqlParameter("@AssetId", a.AssetId));
        command.Parameters.Add(new SqlParameter("@PersonnelId", (object?)a.PersonnelId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@DepartmentId", (object?)a.DepartmentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LocationId", (object?)a.LocationId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@AssignDate", a.AssignDate));
        command.Parameters.Add(new SqlParameter("@ReturnDate", (object?)a.ReturnDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@AssignNote", (object?)a.AssignNote ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@ReturnNote", (object?)a.ReturnNote ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@DocumentNo", (object?)a.DocumentNo ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CreatedById", (object?)a.CreatedById ?? DBNull.Value));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task CloseAssignmentAsync(int assignmentId, DateTime returnDate, string? returnNote, int? userKey, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_assignmentTable}
            SET [ReturnDate] = @ReturnDate, [ReturnNote] = @ReturnNote, [Updated] = SYSUTCDATETIME(), [UpdatedById] = @UserKey
            WHERE [Id] = @Id AND [CompanyId] = @CompanyId;
            """;
        command.Parameters.Add(new SqlParameter("@ReturnDate", returnDate));
        command.Parameters.Add(new SqlParameter("@ReturnNote", (object?)returnNote ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@UserKey", (object?)userKey ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Id", assignmentId));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Hatırlatma ────────────────────────────────────────────────

    public async Task<IReadOnlyCollection<Asset>> GetAssetsWithDueRemindersAsync(DateTime threshold, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Satır görünürlük kuralları (row-level security) — tek tablo, alias 'a'.
        var dv = await _dvFilter.BuildAsync(FormCodes.Assets, "a", "Id", cancellationToken);
        command.CommandText = $"""
            SELECT {AssetColumns}
            FROM {_assetTable} a
            WHERE [CompanyId] = @CompanyId AND [IsActive] = 1 AND [Status] <> @Disposed AND [Status] <> @Retired
              AND (
                    ([NextMaintenanceDate] IS NOT NULL AND [NextMaintenanceDate] <= @Threshold
                        AND ([MaintenanceRemindedFor] IS NULL OR [MaintenanceRemindedFor] <> [NextMaintenanceDate]))
                 OR ([NextCalibrationDate] IS NOT NULL AND [NextCalibrationDate] <= @Threshold
                        AND ([CalibrationRemindedFor] IS NULL OR [CalibrationRemindedFor] <> [NextCalibrationDate]))
              )
            {dv.Sql}
            ORDER BY [NextMaintenanceDate], [NextCalibrationDate];
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        command.Parameters.Add(new SqlParameter("@Threshold", threshold));
        command.Parameters.Add(new SqlParameter("@Disposed", (byte)AssetStatus.Disposed));
        command.Parameters.Add(new SqlParameter("@Retired", (byte)AssetStatus.Retired));
        foreach (var prm in dv.Parameters) command.Parameters.Add(new SqlParameter(prm.Name, prm.Value));
        return await ReadAssetsAsync(command, cancellationToken);
    }

    public async Task MarkMaintenanceRemindedAsync(int assetId, DateTime remindedFor, CancellationToken cancellationToken)
        => await MarkRemindedAsync("MaintenanceRemindedFor", assetId, remindedFor, cancellationToken);

    public async Task MarkCalibrationRemindedAsync(int assetId, DateTime remindedFor, CancellationToken cancellationToken)
        => await MarkRemindedAsync("CalibrationRemindedFor", assetId, remindedFor, cancellationToken);

    private async Task MarkRemindedAsync(string column, int assetId, DateTime remindedFor, CancellationToken cancellationToken)
    {
        var companyId = _connectionFactory.ResolveCurrentCompanyId();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {_assetTable} SET [{column}] = @Date WHERE [Id] = @Id AND [CompanyId] = @CompanyId;";
        command.Parameters.Add(new SqlParameter("@Date", remindedFor));
        command.Parameters.Add(new SqlParameter("@Id", assetId));
        command.Parameters.Add(new SqlParameter("@CompanyId", companyId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static void BindAssetParameters(SqlCommand command, Asset a, int companyId, bool isInsert)
    {
        if (!isInsert)
            command.Parameters.Add(new SqlParameter("@Id", a.Id));
        command.Parameters.Add(new SqlParameter("@CompanyId", a.CompanyId > 0 ? a.CompanyId : companyId));
        command.Parameters.Add(new SqlParameter("@AssetCode", a.AssetCode));
        command.Parameters.Add(new SqlParameter("@AssetName", a.AssetName));
        command.Parameters.Add(new SqlParameter("@Description", (object?)a.Description ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Kind", (byte)a.Kind));
        command.Parameters.Add(new SqlParameter("@LocationId", (object?)a.LocationId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@DepartmentId", (object?)a.DepartmentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@AssignedPersonnelId", (object?)a.AssignedPersonnelId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@MachineId", (object?)a.MachineId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@SerialNo", (object?)a.SerialNo ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@AcquisitionDate", (object?)a.AcquisitionDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@WarrantyExpiryDate", (object?)a.WarrantyExpiryDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@MaintenancePeriodDays", (object?)a.MaintenancePeriodDays ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LastMaintenanceDate", (object?)a.LastMaintenanceDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@NextMaintenanceDate", (object?)a.NextMaintenanceDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@MaintenanceRemindedFor", (object?)a.MaintenanceRemindedFor ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CalibrationPeriodDays", (object?)a.CalibrationPeriodDays ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@LastCalibrationDate", (object?)a.LastCalibrationDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@NextCalibrationDate", (object?)a.NextCalibrationDate ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@CalibrationRemindedFor", (object?)a.CalibrationRemindedFor ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Status", (byte)a.Status));
        command.Parameters.Add(new SqlParameter("@SortOrder", a.SortOrder));
        command.Parameters.Add(new SqlParameter("@IsActive", a.IsActive));
        command.Parameters.Add(new SqlParameter("@IsMaintained", a.IsMaintained));
        command.Parameters.Add(new SqlParameter("@IsCalibrated", a.IsCalibrated));
        command.Parameters.Add(new SqlParameter("@IpAddress", (object?)a.IpAddress ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@Hostname", (object?)a.Hostname ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@OperatingSystem", (object?)a.OperatingSystem ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@MacAddress", (object?)a.MacAddress ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@NetworkDomain", (object?)a.NetworkDomain ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@PlateNo", (object?)a.PlateNo ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsAssignable", a.IsAssignable));
        command.Parameters.Add(new SqlParameter("@MaintenancePeriodUnit", (byte)a.MaintenancePeriodUnit));
        command.Parameters.Add(new SqlParameter("@CalibrationPeriodUnit", (byte)a.CalibrationPeriodUnit));
        if (isInsert)
            command.Parameters.Add(new SqlParameter("@CreatedById", (object?)a.CreatedById ?? DBNull.Value));
        else
            command.Parameters.Add(new SqlParameter("@UpdatedById", (object?)a.UpdatedById ?? DBNull.Value));
    }

    private static async Task<List<Asset>> ReadAssetsAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        var rows = new List<Asset>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Asset
            {
                Id = reader.GetInt32(0),
                CompanyId = reader.GetInt32(1),
                AssetCode = reader.GetString(2),
                AssetName = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                Kind = (AssetKind)reader.GetByte(5),
                LocationId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                DepartmentId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                AssignedPersonnelId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                MachineId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                SerialNo = reader.IsDBNull(10) ? null : reader.GetString(10),
                AcquisitionDate = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                WarrantyExpiryDate = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
                MaintenancePeriodDays = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                LastMaintenanceDate = reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                NextMaintenanceDate = reader.IsDBNull(15) ? null : reader.GetDateTime(15),
                MaintenanceRemindedFor = reader.IsDBNull(16) ? null : reader.GetDateTime(16),
                CalibrationPeriodDays = reader.IsDBNull(17) ? null : reader.GetInt32(17),
                LastCalibrationDate = reader.IsDBNull(18) ? null : reader.GetDateTime(18),
                NextCalibrationDate = reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                CalibrationRemindedFor = reader.IsDBNull(20) ? null : reader.GetDateTime(20),
                Status = (AssetStatus)reader.GetByte(21),
                SortOrder = reader.GetInt32(22),
                IsActive = reader.GetBoolean(23),
                Created = reader.GetDateTime(24),
                Updated = reader.IsDBNull(25) ? null : reader.GetDateTime(25),
                CreatedById = reader.IsDBNull(26) ? null : reader.GetInt32(26),
                UpdatedById = reader.IsDBNull(27) ? null : reader.GetInt32(27),
                IsMaintained = reader.GetBoolean(28),
                IsCalibrated = reader.GetBoolean(29),
                IpAddress = reader.IsDBNull(30) ? null : reader.GetString(30),
                Hostname = reader.IsDBNull(31) ? null : reader.GetString(31),
                OperatingSystem = reader.IsDBNull(32) ? null : reader.GetString(32),
                MacAddress = reader.IsDBNull(33) ? null : reader.GetString(33),
                NetworkDomain = reader.IsDBNull(34) ? null : reader.GetString(34),
                PlateNo = reader.IsDBNull(35) ? null : reader.GetString(35),
                IsAssignable = reader.GetBoolean(36),
                MaintenancePeriodUnit = (AssetPeriodUnit)reader.GetByte(37),
                CalibrationPeriodUnit = (AssetPeriodUnit)reader.GetByte(38),
            });
        }
        return rows;
    }
}
