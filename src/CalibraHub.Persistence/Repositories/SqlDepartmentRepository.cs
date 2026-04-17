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
            SELECT [id], [company_id], [code], [name], [parent_department_id], [is_active]
            FROM {_tableName}
            ORDER BY [company_id], [code];
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var department = new Department
            {
                Id = reader.GetGuid(0),
                CompanyId = reader.GetInt32(1),
                Code = reader.GetString(2),
                Name = reader.GetString(3),
                ParentDepartmentId = reader.IsDBNull(4) ? null : reader.GetGuid(4)
            };

            if (!reader.GetBoolean(5))
            {
                department.Deactivate();
            }

            departments.Add(department);
        }

        return departments;
    }

    public async Task AddAsync(Department department, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([id], [company_id], [code], [name], [parent_department_id], [is_active])
            VALUES
                (@Id, @CompanyId, @Code, @Name, @ParentDepartmentId, @IsActive);
            """;
        command.Parameters.Add(new SqlParameter("@Id", department.Id));
        command.Parameters.Add(new SqlParameter("@CompanyId", department.CompanyId));
        command.Parameters.Add(new SqlParameter("@Code", department.Code));
        command.Parameters.Add(new SqlParameter("@Name", department.Name));
        command.Parameters.Add(new SqlParameter("@ParentDepartmentId", (object?)department.ParentDepartmentId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@IsActive", department.IsActive));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
