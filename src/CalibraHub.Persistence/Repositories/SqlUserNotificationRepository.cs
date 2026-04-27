using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlUserNotificationRepository : IUserNotificationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _table;

    public SqlUserNotificationRepository(SqlServerConnectionFactory connectionFactory, CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _table = $"[{schema}].[user_notifications]";
    }

    public async Task AddAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {_table}
                ([id],[company_id],[user_id],[created_at],[title],[body],
                 [source_type],[source_id],[link],[is_read],[read_at])
            VALUES
                (@Id,@CompanyId,@UserId,@CreatedAt,@Title,@Body,
                 @SourceType,@SourceId,@Link,0,NULL);
            """;
        cmd.Parameters.Add(new SqlParameter("@Id",         notification.Id));
        cmd.Parameters.Add(new SqlParameter("@CompanyId",  notification.CompanyId));
        cmd.Parameters.Add(new SqlParameter("@UserId",     notification.UserId));
        cmd.Parameters.Add(new SqlParameter("@CreatedAt",  notification.CreatedAt));
        cmd.Parameters.Add(new SqlParameter("@Title",      notification.Title));
        cmd.Parameters.Add(new SqlParameter("@Body",       (object?)notification.Body ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SourceType", (object?)notification.SourceType ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@SourceId",   (object?)notification.SourceId ?? DBNull.Value));
        cmd.Parameters.Add(new SqlParameter("@Link",       (object?)notification.Link ?? DBNull.Value));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<UserNotification>> GetRecentAsync(Guid userId, int take, CancellationToken cancellationToken)
    {
        if (take <= 0 || take > 200) take = 30;

        var list = new List<UserNotification>();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT TOP (@Take)
                   [id],[company_id],[user_id],[created_at],[title],[body],
                   [source_type],[source_id],[link],[is_read],[read_at]
            FROM {_table}
            WHERE [user_id] = @UserId
            ORDER BY [is_read] ASC, [created_at] DESC;
            """;
        cmd.Parameters.Add(new SqlParameter("@Take",   take));
        cmd.Parameters.Add(new SqlParameter("@UserId", userId));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken)) list.Add(Map(r));
        return list;
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {_table} WHERE [user_id] = @UserId AND [is_read] = 0;";
        cmd.Parameters.Add(new SqlParameter("@UserId", userId));
        var v = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(v);
    }

    public async Task MarkReadAsync(Guid notificationId, Guid userId, DateTime readAt, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [is_read] = 1, [read_at] = @ReadAt WHERE [id] = @Id AND [user_id] = @UserId;";
        cmd.Parameters.Add(new SqlParameter("@Id",     notificationId));
        cmd.Parameters.Add(new SqlParameter("@UserId", userId));
        cmd.Parameters.Add(new SqlParameter("@ReadAt", readAt));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(Guid userId, DateTime readAt, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"UPDATE {_table} SET [is_read] = 1, [read_at] = @ReadAt WHERE [user_id] = @UserId AND [is_read] = 0;";
        cmd.Parameters.Add(new SqlParameter("@UserId", userId));
        cmd.Parameters.Add(new SqlParameter("@ReadAt", readAt));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static UserNotification Map(SqlDataReader r)
    {
        var n = new UserNotification
        {
            Id         = r.GetGuid(0),
            CompanyId  = r.GetInt32(1),
            UserId     = r.GetGuid(2),
            CreatedAt  = r.GetDateTime(3),
            Title      = r.GetString(4),
            Body       = r.IsDBNull(5) ? null : r.GetString(5),
            SourceType = r.IsDBNull(6) ? null : r.GetString(6),
            SourceId   = r.IsDBNull(7) ? (Guid?)null : r.GetGuid(7),
            Link       = r.IsDBNull(8) ? null : r.GetString(8),
        };
        if (r.GetBoolean(9))
        {
            n.MarkRead(r.IsDBNull(10) ? DateTime.Now : r.GetDateTime(10));
        }
        return n;
    }
}
