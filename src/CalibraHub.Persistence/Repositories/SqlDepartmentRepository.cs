using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlDepartmentRepository : IDepartmentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlDepartmentRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[Department]";
    }

    public async Task<IReadOnlyCollection<Department>> GetAllAsync(CancellationToken cancellationToken)
    {
        var departments = new List<Department>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [Name], [ParentDepartmentId], [IsActive]
            FROM {_tableName}
            ORDER BY [CompanyId], [Name];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var department = new Department
            {
                Id = reader.GetInt32(0),
                CompanyId = reader.GetInt32(1),
                Name = reader.GetString(2),
                ParentDepartmentId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            };
            if (!reader.GetBoolean(4)) department.Deactivate();
            departments.Add(department);
        }

        return departments;
    }

    public async Task<Department?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [CompanyId], [Name], [ParentDepartmentId], [IsActive]
            FROM {_tableName}
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var dept = new Department
        {
            Id = reader.GetInt32(0),
            CompanyId = reader.GetInt32(1),
            Name = reader.GetString(2),
            ParentDepartmentId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        };
        if (!reader.GetBoolean(4)) dept.Deactivate();
        return dept;
    }

    public async Task<int> AddAsync(Department department, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([CompanyId], [Name], [ParentDepartmentId], [IsActive])
            OUTPUT INSERTED.[Id]
            VALUES
                (@CompanyId, @Name, @ParentDepartmentId, @IsActive);
            """;
        command.Parameters.Add(new SqlParameter("@CompanyId", department.CompanyId));
        command.Parameters.Add(new SqlParameter("@Name", department.Name));
        command.Parameters.Add(new SqlParameter("@ParentDepartmentId", (object?)department.ParentDepartmentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", department.IsActive));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int id ? id : Convert.ToInt32(result);
    }

    public async Task UpdateAsync(Department department, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_tableName}
            SET
                [Name] = @Name,
                [ParentDepartmentId] = @ParentDepartmentId,
                [IsActive] = @IsActive
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", department.Id));
        command.Parameters.Add(new SqlParameter("@Name", department.Name));
        command.Parameters.Add(new SqlParameter("@ParentDepartmentId", (object?)department.ParentDepartmentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", department.IsActive));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {_tableName} WHERE [Id] = @Id;";
        command.Parameters.Add(new SqlParameter("@Id", id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
