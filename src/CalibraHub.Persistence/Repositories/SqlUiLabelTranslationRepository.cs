using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlUiLabelTranslationRepository : IUiLabelTranslationRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _tableName;

    public SqlUiLabelTranslationRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        var schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{schema}].[UiLabelTranslation]";
    }

    public async Task<IReadOnlyCollection<UiLabelTranslation>> GetByLanguageAsync(
        string languageCode,
        CancellationToken cancellationToken)
    {
        var items = new List<UiLabelTranslation>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [FormKey], [LabelKey], [LanguageCode], [LabelText], [Updated]
            FROM {_tableName}
            WHERE [LanguageCode] = @LanguageCode
            ORDER BY [FormKey], [LabelKey];
            """;
        command.Parameters.Add(new SqlParameter("@LanguageCode", languageCode));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapTranslation(reader));
        }

        return items;
    }

    public async Task<IReadOnlyCollection<UiLabelTranslation>> GetByFormAndLanguageAsync(
        string formKey,
        string languageCode,
        CancellationToken cancellationToken)
    {
        var items = new List<UiLabelTranslation>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [FormKey], [LabelKey], [LanguageCode], [LabelText], [Updated]
            FROM {_tableName}
            WHERE [FormKey] = @FormKey
              AND [LanguageCode] = @LanguageCode
            ORDER BY [LabelKey];
            """;
        command.Parameters.Add(new SqlParameter("@FormKey", formKey));
        command.Parameters.Add(new SqlParameter("@LanguageCode", languageCode));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapTranslation(reader));
        }

        return items;
    }

    public async Task ReplaceFormLanguageAsync(
        string formKey,
        string languageCode,
        IReadOnlyCollection<UiLabelTranslation> translations,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"""
                DELETE FROM {_tableName}
                WHERE [FormKey] = @FormKey
                  AND [LanguageCode] = @LanguageCode;
                """;
            deleteCommand.Parameters.Add(new SqlParameter("@FormKey", formKey));
            deleteCommand.Parameters.Add(new SqlParameter("@LanguageCode", languageCode));
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var translation in translations)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                INSERT INTO {_tableName}
                    ([Id], [FormKey], [LabelKey], [LanguageCode], [LabelText], [Updated])
                VALUES
                    (@Id, @FormKey, @LabelKey, @LanguageCode, @LabelText, @Updated);
                """;
            insertCommand.Parameters.Add(new SqlParameter("@Id", translation.Id));
            insertCommand.Parameters.Add(new SqlParameter("@FormKey", translation.FormKey));
            insertCommand.Parameters.Add(new SqlParameter("@LabelKey", translation.LabelKey));
            insertCommand.Parameters.Add(new SqlParameter("@LanguageCode", translation.LanguageCode));
            insertCommand.Parameters.Add(new SqlParameter("@LabelText", translation.LabelText));
            insertCommand.Parameters.Add(new SqlParameter("@Updated", translation.Updated));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static UiLabelTranslation MapTranslation(SqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            FormKey = reader.GetString(1),
            LabelKey = reader.GetString(2),
            LanguageCode = reader.GetString(3),
            LabelText = reader.GetString(4),
            Updated = reader.GetFieldValue<DateTime>(5)
        };
}
