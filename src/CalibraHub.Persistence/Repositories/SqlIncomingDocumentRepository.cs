using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Domain.Entities;
using CalibraHub.Domain.Enums;
using CalibraHub.Persistence.Database;
using CalibraHub.Persistence.Options;
using Microsoft.Data.SqlClient;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace CalibraHub.Persistence.Repositories;

public sealed class SqlIncomingDocumentRepository : IIncomingDocumentRepository
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly string _schema;
    private readonly string _tableName;
    private readonly string _ebelgeMasTableName;
    private readonly string _ebelgeMasTaxTableName;
    private readonly string _ebelgeKalemTableName;
    private readonly string _ebelgeKalemTaxTableName;

    public SqlIncomingDocumentRepository(
        SqlServerConnectionFactory connectionFactory,
        CalibraDatabaseOptions options)
    {
        _connectionFactory = connectionFactory;
        _schema = string.IsNullOrWhiteSpace(options.Schema) ? "dbo" : options.Schema.Trim();
        _tableName = $"[{_schema}].[IncomingDocument]";
        _ebelgeMasTableName = $"[{_schema}].[CBT_EBELGEMAS]";
        _ebelgeMasTaxTableName = $"[{_schema}].[CBT_EBELGEMASTAX]";
        _ebelgeKalemTableName = $"[{_schema}].[CBT_EBELGEKALEM]";
        _ebelgeKalemTaxTableName = $"[{_schema}].[CBT_EBELGEKALEMTAX]";
    }

    public async Task<bool> ExistsByEnvelopeIdAsync(string envelopeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envelopeId))
        {
            return false;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        if (await LegacyTablesExistAsync(connection, cancellationToken))
        {
            await using var legacyCommand = connection.CreateCommand();
            legacyCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM {_ebelgeMasTableName}
                WHERE [UUID] = @EnvelopeId;
                """;
            legacyCommand.Parameters.Add(CreateParameter("@EnvelopeId", envelopeId));

            var legacyResult = await legacyCommand.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(legacyResult) > 0;
        }

        if (!await TableExistsAsync(connection, "IncomingDocument", cancellationToken))
        {
            return false;
        }

        return await ExistsInIncomingDocumentsAsync(connection, envelopeId, cancellationToken);
    }

    public async Task<bool> ExistsByDocumentNumberAndRecipientAsync(
        string documentNumber,
        string recipientTaxNumber,
        DocumentKind kind,
        CancellationToken cancellationToken)
    {
        var normalizedDocumentNumber = documentNumber?.Trim();
        var normalizedRecipientTaxNumber = recipientTaxNumber?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedDocumentNumber) || string.IsNullOrWhiteSpace(normalizedRecipientTaxNumber))
        {
            return false;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        if (await LegacyTablesExistAsync(connection, cancellationToken))
        {
            var hasBelgeTipiColumn = await ColumnExistsAsync(connection, "CBT_EBELGEMAS", "BELGE_TIPI", cancellationToken);

            await using var legacyCommand = connection.CreateCommand();
            if (hasBelgeTipiColumn)
            {
                legacyCommand.CommandText = $"""
                    SELECT COUNT(1)
                    FROM {_ebelgeMasTableName}
                    WHERE LTRIM(RTRIM(ISNULL([FATIRS_NO], N''))) = @DocumentNumber
                      AND LTRIM(RTRIM(ISNULL([CARIKOD], N''))) = @CariKod
                      AND [BELGE_TIPI] = @BelgeTipi;
                    """;
                legacyCommand.Parameters.Add(CreateParameter("@BelgeTipi", MapBelgeTipi(kind)));
            }
            else
            {
                legacyCommand.CommandText = $"""
                    SELECT COUNT(1)
                    FROM {_ebelgeMasTableName}
                    WHERE LTRIM(RTRIM(ISNULL([FATIRS_NO], N''))) = @DocumentNumber
                      AND LTRIM(RTRIM(ISNULL([CARIKOD], N''))) = @CariKod
                      AND (
                            LTRIM(RTRIM(ISNULL([FTIRSIP], N''))) = @Ftirsip
                            OR ISNULL([TIPI], 0) = @Tipi
                          );
                    """;
                legacyCommand.Parameters.Add(CreateParameter("@Ftirsip", MapFtirsip(kind)));
                legacyCommand.Parameters.Add(CreateParameter("@Tipi", (short)MapTipi(kind)));
            }

            legacyCommand.Parameters.Add(CreateParameter("@DocumentNumber", normalizedDocumentNumber));
            legacyCommand.Parameters.Add(CreateParameter("@CariKod", normalizedRecipientTaxNumber));

            var legacyResult = await legacyCommand.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(legacyResult) > 0;
        }

        if (!await TableExistsAsync(connection, "IncomingDocument", cancellationToken))
        {
            return false;
        }

        return await ExistsInIncomingDocumentsByDocumentAndRecipientAsync(
            connection,
            normalizedDocumentNumber,
            normalizedRecipientTaxNumber,
            kind,
            cancellationToken);
    }

    public async Task AddAsync(IncomingDocument document, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var hasLegacyTables = await LegacyTablesExistAsync(connection, cancellationToken);
        var hasIncomingDocumentsTable = await TableExistsAsync(connection, "IncomingDocument", cancellationToken);
        var hasLegacyBelgeTipi = hasLegacyTables &&
                                 await ColumnExistsAsync(connection, "CBT_EBELGEMAS", "BELGE_TIPI", cancellationToken);
        var hasLegacyMasTax = hasLegacyTables &&
                              await TableExistsAsync(connection, "CBT_EBELGEMASTAX", cancellationToken);
        var hasLegacyKalemTax = hasLegacyTables &&
                                await TableExistsAsync(connection, "CBT_EBELGEKALEMTAX", cancellationToken);
        var hasLegacyPayloadRaw = hasLegacyTables &&
                                await ColumnExistsAsync(connection, "CBT_EBELGEMAS", "PAYLOAD_RAW", cancellationToken);

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (hasLegacyTables)
        {
            await InsertLegacyEBelgeRowsAsync(
                connection,
                transaction,
                document,
                hasLegacyBelgeTipi,
                hasLegacyMasTax,
                hasLegacyKalemTax,
                hasLegacyPayloadRaw,
                cancellationToken);
        }
        else if (hasIncomingDocumentsTable)
        {
            await InsertIncomingDocumentAsync(connection, transaction, document, cancellationToken);
        }
        else
        {
            throw new InvalidOperationException(
                $"Ne {_schema}.CBT_EBELGE* ne de {_schema}.IncomingDocument tablolari bulundu.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<IncomingDocument>> GetPendingApprovalsAsync(bool? isProcessed, CancellationToken cancellationToken)
    {
        var documents = new List<IncomingDocument>();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        if (await LegacyTablesExistAsync(connection, cancellationToken))
        {
            var hasBelgeTipiColumn = await ColumnExistsAsync(connection, "CBT_EBELGEMAS", "BELGE_TIPI", cancellationToken);
            var belgeTipiSelectSql = hasBelgeTipiColumn
                ? "[BELGE_TIPI]"
                : "CAST(NULL AS TINYINT) AS [BELGE_TIPI]";

            var hasPayloadRawColumn = await ColumnExistsAsync(connection, "CBT_EBELGEMAS", "PAYLOAD_RAW", cancellationToken);
            var payloadRawSelectSql = hasPayloadRawColumn
                ? "CAST(SUBSTRING([PAYLOAD_RAW], 1, 1000) AS NVARCHAR(MAX))"
                : "CAST('{}' AS NVARCHAR(MAX))";

            var isProcessedFilterSql = isProcessed.HasValue
                ? (isProcessed.Value ? " AND ISNULL([ISLENDI], 0) = 1" : " AND ISNULL([ISLENDI], 0) = 0")
                : string.Empty;

            await using var legacyCommand = connection.CreateCommand();
            legacyCommand.CommandText = $"""
                SELECT [INCKEYNO], [FATIRS_NO], [FTIRSIP], [TIPI], {belgeTipiSelectSql}, [UUID], [SENDERVNO], [CARI_VERGINUMARASI], [CARI_TCKIMLIKNO], [TARIH], [DURUM], ISNULL([SENDERNAME], N'') AS [SENDERNAME], ISNULL([CARI_ISIM], N'') AS [CARI_ISIM], {payloadRawSelectSql} AS [PAYLOAD_RAW], ISNULL([ISLENDI], 0) AS [ISLENDI]
                FROM {_ebelgeMasTableName}
                WHERE ISNULL([DURUM], 0) = 0 {isProcessedFilterSql}
                ORDER BY [TARIH] DESC, [INCKEYNO] DESC;
                """;

            await using var legacyReader = await legacyCommand.ExecuteReaderAsync(cancellationToken);
            while (await legacyReader.ReadAsync(cancellationToken))
            {
                documents.Add(MapLegacyIncomingDocument(legacyReader));
            }
        }

        if (documents.Count == 0 && await TableExistsAsync(connection, "IncomingDocument", cancellationToken))
        {
            var hasProcessedColumn = await ColumnExistsAsync(connection, "IncomingDocument", "IsProcessed", cancellationToken);
            var isProcessedSelectSql = hasProcessedColumn ? "[IsProcessed]" : "CAST(0 AS BIT) AS [IsProcessed]";
            var isProcessedFilterSql = string.Empty;
            if (isProcessed.HasValue && hasProcessedColumn)
            {
                isProcessedFilterSql = isProcessed.Value ? " AND [IsProcessed] = 1" : " AND [IsProcessed] = 0";
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT [Id], [IntegratorSettingsId], [EnvelopeId], [DocumentNumber], [Kind], [IssueDate], [SenderTaxNumber], [RecipientTaxNumber], CAST(SUBSTRING([PayloadRaw], 1, 1000) AS NVARCHAR(MAX)) AS [PayloadRaw], [ApprovalStatus], [ImportedAt], ISNULL([SenderName], N'') AS [SenderName], {isProcessedSelectSql}
                FROM {_tableName}
                WHERE [ApprovalStatus] = @ApprovalStatus {isProcessedFilterSql}
                ORDER BY [ImportedAt] DESC;
                """;
            command.Parameters.Add(new SqlParameter("@ApprovalStatus", ApprovalStatus.Pending.ToString()));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                documents.Add(MapIncomingDocument(reader));
            }
        }

        return documents;
    }

    public async Task<IncomingDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        if (await LegacyTablesExistAsync(connection, cancellationToken))
        {
            // Legacy tablolarda tekil belge sorgusu: deterministic GUID ile eşleştir
            var hasBelgeTipiColumn = await ColumnExistsAsync(connection, "CBT_EBELGEMAS", "BELGE_TIPI", cancellationToken);
            var belgeTipiSelectSql = hasBelgeTipiColumn
                ? "[BELGE_TIPI]"
                : "CAST(NULL AS TINYINT) AS [BELGE_TIPI]";

            var hasPayloadRawColumn = await ColumnExistsAsync(connection, "CBT_EBELGEMAS", "PAYLOAD_RAW", cancellationToken);
            var payloadRawSelectSql = hasPayloadRawColumn
                ? "[PAYLOAD_RAW]"
                : "CAST('{}' AS NVARCHAR(MAX)) AS [PAYLOAD_RAW]";

            await using var legacyCommand = connection.CreateCommand();
            legacyCommand.CommandText = $"""
                SELECT [INCKEYNO], [FATIRS_NO], [FTIRSIP], [TIPI], {belgeTipiSelectSql}, [UUID], [SENDERVNO], [CARI_VERGINUMARASI], [CARI_TCKIMLIKNO], [TARIH], [DURUM], ISNULL([SENDERNAME], N'') AS [SENDERNAME], ISNULL([CARI_ISIM], N'') AS [CARI_ISIM], {payloadRawSelectSql}, ISNULL([ISLENDI], 0) AS [ISLENDI]
                FROM {_ebelgeMasTableName};
                """;

            await using var legacyReader = await legacyCommand.ExecuteReaderAsync(cancellationToken);
            while (await legacyReader.ReadAsync(cancellationToken))
            {
                var doc = MapLegacyIncomingDocument(legacyReader);
                if (doc.Id == id)
                {
                    return doc;
                }
            }

            return null;
        }

        if (!await TableExistsAsync(connection, "IncomingDocument", cancellationToken))
        {
            return null;
        }

        var hasProcessedColumn = await ColumnExistsAsync(connection, "IncomingDocument", "IsProcessed", cancellationToken);
        var isProcessedSelectSql = hasProcessedColumn ? "[IsProcessed]" : "CAST(0 AS BIT) AS [IsProcessed]";

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT [Id], [IntegratorSettingsId], [EnvelopeId], [DocumentNumber], [Kind], [IssueDate], [SenderTaxNumber], [RecipientTaxNumber], [PayloadRaw], [ApprovalStatus], [ImportedAt], ISNULL([SenderName], N'') AS [SenderName], {isProcessedSelectSql}
            FROM {_tableName}
            WHERE [Id] = @Id;
            """;
        command.Parameters.Add(new SqlParameter("@Id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapIncomingDocument(reader);
        }

        return null;
    }

    private async Task<bool> ExistsInIncomingDocumentsAsync(
        SqlConnection connection,
        string envelopeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(1)
            FROM {_tableName}
            WHERE [EnvelopeId] = @EnvelopeId;
            """;
        command.Parameters.Add(new SqlParameter("@EnvelopeId", envelopeId));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }

    private async Task<bool> ExistsInIncomingDocumentsByDocumentAndRecipientAsync(
        SqlConnection connection,
        string documentNumber,
        string recipientTaxNumber,
        DocumentKind kind,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(1)
            FROM {_tableName}
            WHERE [DocumentNumber] = @DocumentNumber
              AND [RecipientTaxNumber] = @RecipientTaxNumber
              AND [Kind] = @Kind;
            """;
        command.Parameters.Add(CreateParameter("@DocumentNumber", documentNumber));
        command.Parameters.Add(CreateParameter("@RecipientTaxNumber", recipientTaxNumber));
        command.Parameters.Add(CreateParameter("@Kind", kind.ToString()));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) > 0;
    }

    private async Task<bool> LegacyTablesExistAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var requiredTables = new[]
        {
            "CBT_EBELGEMAS",
            "CBT_EBELGEKALEM"
        };

        foreach (var table in requiredTables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN OBJECT_ID(@QualifiedName, 'U') IS NULL THEN 0 ELSE 1 END;
                """;
            command.Parameters.Add(CreateParameter("@QualifiedName", $"{_schema}.{table}"));

            var exists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
            if (!exists)
            {
                return false;
            }
        }

        return true;
    }

    private async Task InsertIncomingDocumentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IncomingDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {_tableName}
                ([Id], [IntegratorSettingsId], [EnvelopeId], [DocumentNumber], [Kind], [IssueDate], [SenderTaxNumber], [SenderName], [RecipientTaxNumber], [PayloadRaw], [ApprovalStatus], [ImportedAt])
            VALUES
                (@Id, @IntegratorSettingsId, @EnvelopeId, @DocumentNumber, @Kind, @IssueDate, @SenderTaxNumber, @SenderName, @RecipientTaxNumber, @PayloadRaw, @ApprovalStatus, @ImportedAt);
            """;

        command.Parameters.Add(CreateParameter("@Id", document.Id));
        command.Parameters.Add(CreateParameter("@IntegratorSettingsId", document.IntegratorSettingsId));
        command.Parameters.Add(CreateParameter("@EnvelopeId", document.EnvelopeId));
        command.Parameters.Add(CreateParameter("@DocumentNumber", document.DocumentNumber));
        command.Parameters.Add(CreateParameter("@Kind", document.Kind.ToString()));
        command.Parameters.Add(CreateParameter("@IssueDate", document.IssueDate.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(CreateParameter("@SenderTaxNumber", document.SenderTaxNumber));
        command.Parameters.Add(CreateParameter("@SenderName", (object?)document.SenderName ?? DBNull.Value));
        command.Parameters.Add(CreateParameter("@RecipientTaxNumber", document.RecipientTaxNumber));
        command.Parameters.Add(CreateParameter("@PayloadRaw", document.PayloadRaw));
        command.Parameters.Add(CreateParameter("@ApprovalStatus", document.ApprovalStatus.ToString()));
        command.Parameters.Add(CreateParameter("@ImportedAt", document.ImportedAt));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertLegacyEBelgeRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IncomingDocument document,
        bool hasLegacyBelgeTipi,
        bool hasLegacyMasTax,
        bool hasLegacyKalemTax,
        bool hasLegacyPayloadRaw,
        CancellationToken cancellationToken)
    {
        var projection = BuildLegacyDocumentProjection(document);
        var ftirsip = MapFtirsip(document.Kind);
        var tipi = (short)MapTipi(document.Kind);
        var belgeTipi = (byte)MapBelgeTipi(document.Kind);
        var vatBuckets = BuildVatBuckets(projection.HeaderTaxes);
        var totalVatAmount = projection.HeaderTaxes
            .Where(IsVatTax)
            .Sum(x => x.TaxAmount ?? 0m);
        var totalSpecialTax = projection.HeaderTaxes
            .Where(IsSpecialConsumptionTax)
            .Sum(x => x.TaxAmount ?? 0m);
        var firstSpecialTax = projection.HeaderTaxes.FirstOrDefault(IsSpecialConsumptionTax);
        var orderReference = projection.OrderReferences.FirstOrDefault();
        var payableAmount = projection.PayableAmount ?? projection.TaxInclusiveAmount ?? projection.GrossAmount;
        var grossAmount = projection.GrossAmount ?? projection.TaxExclusiveAmount ?? payableAmount;
        var currencyType = MapCurrencyType(projection.CurrencyCode);
        string masCols = "[FATIRS_NO], [CARIKOD], [FTIRSIP], [CARI_ISIM], [TARIH], [DOVIZTIP], [DOVIZTUTAR], [GENELTOPLAM], [BRUTTUTAR], [DOVIZBRUTTUTAR], [KDV], [DOVIZKDV], [OTV], [DOVIZOTV], [KDV1O], [KDV1T], [KDV2O], [KDV2T], [KDV3O], [KDV3T], [KDV4O], [KDV4T], [KDV5O], [KDV5T], [KDV6O], [KDV6T], [KDV7O], [KDV7T], [CARI_VERGINUMARASI], [CARI_TCKIMLIKNO], [CARI_VERGIDAIRESI], [CARI_ADRES], [CARI_ULKEKODU], [CARI_IL], [CARI_ILCE], [DURUM], [UUID], [SENDERNAME], [SENDERVNO], [RESPONSECODE], [YEDEK11], [YEDEK12], [PROFILEID], [SUBE_KODU], [SIPARISNO], [SIPARIS_TARIH], [TIPI], [OTVTIP], [C_YEDEK1], [C_YEDEK2], [D_YEDEK1], [D_YEDEK2], [F_YEDEK1], [F_YEDEK2], [I_YEDEK1], [I_YEDEK2], [S_YEDEK1], [S_YEDEK2], [TAXEXEMPTIONREASON], [TAXEXEMPTIONREASONCODE], [PAYMENTMEANSCODE], [DLVTERMSID], [KULSUBEKODU]";
        string masVals = "@FatirsNo, @CariKod, @Ftirsip, @CariIsim, @Tarih, @DovizTip, @DovizTutar, @GenelToplam, @BrutTutar, @DovizBrutTutar, @Kdv, @DovizKdv, @Otv, @DovizOtv, @Kdv1O, @Kdv1T, @Kdv2O, @Kdv2T, @Kdv3O, @Kdv3T, @Kdv4O, @Kdv4T, @Kdv5O, @Kdv5T, @Kdv6O, @Kdv6T, @Kdv7O, @Kdv7T, @CariVergiNo, @CariTckn, @CariVergiDairesi, @CariAdres, @CariUlkeKodu, @CariIl, @CariIlce, @Durum, @Uuid, @SenderName, @SenderVno, @ResponseCode, @Yedek11, @Yedek12, @ProfileId, @SubeKodu, @SiparisNo, @SiparisTarih, @Tipi, @OtvTip, @CYedek1, @CYedek2, @DYedek1, @DYedek2, @FYedek1, @FYedek2, @IYedek1, @IYedek2, @SYedek1, @SYedek2, @TaxExemptionReason, @TaxExemptionReasonCode, @PaymentMeansCode, @DlvTermsId, @KulSubeKodu";

        if (hasLegacyBelgeTipi)
        {
            masCols += ", [BELGE_TIPI]";
            masVals += ", @BelgeTipi";
        }

        if (hasLegacyPayloadRaw)
        {
            masCols += ", [PAYLOAD_RAW]";
            masVals += ", @PayloadRaw";
        }

        await using var masCommand = connection.CreateCommand();
        masCommand.Transaction = transaction;

        masCommand.CommandText = $"""
            INSERT INTO {_ebelgeMasTableName}
                ({masCols})
            VALUES
                ({masVals});

            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        masCommand.Parameters.Add(CreateParameter("@FatirsNo", ToDbValue(document.DocumentNumber, 17)));
        masCommand.Parameters.Add(CreateParameter("@CariKod", ToDbValue(projection.CustomerCode ?? document.RecipientTaxNumber, 15)));
        masCommand.Parameters.Add(CreateParameter("@Ftirsip", ToDbValue(ftirsip, 1)));
        masCommand.Parameters.Add(CreateParameter("@CariIsim", ToDbValue(projection.CustomerName, 100)));
        masCommand.Parameters.Add(CreateParameter("@Tarih", document.IssueDate.ToDateTime(TimeOnly.MinValue)));
        masCommand.Parameters.Add(CreateParameter("@DovizTip", currencyType));
        masCommand.Parameters.Add(CreateParameter("@DovizTutar", grossAmount));
        masCommand.Parameters.Add(CreateParameter("@GenelToplam", payableAmount));
        masCommand.Parameters.Add(CreateParameter("@BrutTutar", grossAmount));
        masCommand.Parameters.Add(CreateParameter("@DovizBrutTutar", projection.ForeignGrossAmount ?? projection.ForeignPayableAmount ?? grossAmount));
        masCommand.Parameters.Add(CreateParameter("@Kdv", totalVatAmount));
        masCommand.Parameters.Add(CreateParameter("@DovizKdv", projection.ForeignVatAmount ?? totalVatAmount));
        masCommand.Parameters.Add(CreateParameter("@Otv", totalSpecialTax));
        masCommand.Parameters.Add(CreateParameter("@DovizOtv", projection.ForeignSpecialTaxAmount ?? totalSpecialTax));
        masCommand.Parameters.Add(CreateParameter("@Kdv1O", vatBuckets.ElementAtOrDefault(0)?.Percent));
        masCommand.Parameters.Add(CreateParameter("@Kdv1T", vatBuckets.ElementAtOrDefault(0)?.Amount));
        masCommand.Parameters.Add(CreateParameter("@Kdv2O", vatBuckets.ElementAtOrDefault(1)?.Percent));
        masCommand.Parameters.Add(CreateParameter("@Kdv2T", vatBuckets.ElementAtOrDefault(1)?.Amount));
        masCommand.Parameters.Add(CreateParameter("@Kdv3O", vatBuckets.ElementAtOrDefault(2)?.Percent));
        masCommand.Parameters.Add(CreateParameter("@Kdv3T", vatBuckets.ElementAtOrDefault(2)?.Amount));
        masCommand.Parameters.Add(CreateParameter("@Kdv4O", vatBuckets.ElementAtOrDefault(3)?.Percent));
        masCommand.Parameters.Add(CreateParameter("@Kdv4T", vatBuckets.ElementAtOrDefault(3)?.Amount));
        masCommand.Parameters.Add(CreateParameter("@Kdv5O", vatBuckets.ElementAtOrDefault(4)?.Percent));
        masCommand.Parameters.Add(CreateParameter("@Kdv5T", vatBuckets.ElementAtOrDefault(4)?.Amount));
        masCommand.Parameters.Add(CreateParameter("@Kdv6O", vatBuckets.ElementAtOrDefault(5)?.Percent));
        masCommand.Parameters.Add(CreateParameter("@Kdv6T", vatBuckets.ElementAtOrDefault(5)?.Amount));
        masCommand.Parameters.Add(CreateParameter("@Kdv7O", vatBuckets.ElementAtOrDefault(6)?.Percent));
        masCommand.Parameters.Add(CreateParameter("@Kdv7T", vatBuckets.ElementAtOrDefault(6)?.Amount));
        masCommand.Parameters.Add(CreateParameter("@CariVergiNo", ToDbValue(projection.CustomerTaxNumber, 15)));
        masCommand.Parameters.Add(CreateParameter("@CariTckn", ToDbValue(projection.CustomerIdentityNumber, 15)));
        masCommand.Parameters.Add(CreateParameter("@CariVergiDairesi", ToDbValue(projection.CustomerTaxOffice, 50)));
        masCommand.Parameters.Add(CreateParameter("@CariAdres", ToDbValue(projection.CustomerAddress, 100)));
        masCommand.Parameters.Add(CreateParameter("@CariUlkeKodu", ToDbValue(projection.CustomerCountryCode, 4)));
        masCommand.Parameters.Add(CreateParameter("@CariIl", ToDbValue(projection.CustomerCity, 50)));
        masCommand.Parameters.Add(CreateParameter("@CariIlce", ToDbValue(projection.CustomerDistrict, 50)));
        masCommand.Parameters.Add(CreateParameter("@Durum", (short)0));
        masCommand.Parameters.Add(CreateParameter("@Uuid", ToDbValue(document.EnvelopeId, 200)));
        masCommand.Parameters.Add(CreateParameter("@SenderName", ToDbValue(projection.SupplierName, 200)));
        masCommand.Parameters.Add(CreateParameter("@SenderVno", ToDbValue(projection.SupplierTaxNumber ?? projection.SupplierIdentityNumber ?? document.SenderTaxNumber, 50)));
        masCommand.Parameters.Add(CreateParameter("@ResponseCode", projection.ResponseCode));
        masCommand.Parameters.Add(CreateParameter("@Yedek11", ToDbValue(projection.Note, 250)));
        masCommand.Parameters.Add(CreateParameter("@Yedek12", ToDbValue(projection.DocumentTypeCode, 250)));
        masCommand.Parameters.Add(CreateParameter("@ProfileId", projection.LegacyProfileId));
        masCommand.Parameters.Add(CreateParameter("@SubeKodu", projection.BranchCode));
        masCommand.Parameters.Add(CreateParameter("@SiparisNo", ToDbValue(orderReference?.Id, 50)));
        masCommand.Parameters.Add(CreateParameter("@SiparisTarih", orderReference?.IssueDate));
        masCommand.Parameters.Add(CreateParameter("@Tipi", tipi));
        masCommand.Parameters.Add(CreateParameter("@OtvTip", ToDbValue(firstSpecialTax?.TaxTypeCode, 10)));
        masCommand.Parameters.Add(CreateParameter("@CYedek1", DBNull.Value));
        masCommand.Parameters.Add(CreateParameter("@CYedek2", DBNull.Value));
        masCommand.Parameters.Add(CreateParameter("@DYedek1", projection.DueDate));
        masCommand.Parameters.Add(CreateParameter("@DYedek2", projection.DeliveryDate));
        masCommand.Parameters.Add(CreateParameter("@FYedek1", projection.AllowanceTotalAmount));
        masCommand.Parameters.Add(CreateParameter("@FYedek2", projection.TaxExclusiveAmount));
        masCommand.Parameters.Add(CreateParameter("@IYedek1", projection.Lines.Count));
        masCommand.Parameters.Add(CreateParameter("@IYedek2", projection.HeaderTaxes.Count));
        masCommand.Parameters.Add(CreateParameter("@SYedek1", ToDbValue(projection.ProfileCode, 50)));
        masCommand.Parameters.Add(CreateParameter("@SYedek2", ToDbValue(projection.CurrencyCode, 50)));
        masCommand.Parameters.Add(CreateParameter("@TaxExemptionReason", ToDbValue(projection.TaxExemptionReason, 500)));
        masCommand.Parameters.Add(CreateParameter("@TaxExemptionReasonCode", projection.TaxExemptionReasonCode));
        masCommand.Parameters.Add(CreateParameter("@PaymentMeansCode", ToDbValue(projection.PaymentMeansCode, 5)));
        masCommand.Parameters.Add(CreateParameter("@DlvTermsId", ToDbValue(projection.DeliveryTermsId, 5)));
        masCommand.Parameters.Add(CreateParameter("@KulSubeKodu", projection.UserBranchCode));
        if (hasLegacyBelgeTipi)
        {
            masCommand.Parameters.Add(CreateParameter("@BelgeTipi", belgeTipi));
        }
        if (hasLegacyPayloadRaw)
        {
            masCommand.Parameters.Add(CreateParameter("@PayloadRaw", document.PayloadRaw));
        }

        var insertedMasInc = Convert.ToInt32(await masCommand.ExecuteScalarAsync(cancellationToken));

        foreach (var line in projection.Lines)
        {
            await using var kalemCommand = connection.CreateCommand();
            kalemCommand.Transaction = transaction;
            kalemCommand.CommandText = $"""
                INSERT INTO {_ebelgeKalemTableName}
                    ([EBELGEMAS], [STOK_KODU], [STOK_ADI], [STRA_BF], [STRA_DOVTIP], [STRA_GCMIK], [STRA_DOVFIAT], [OLCUBR], [KDV], [IRSALIYENO], [IRSALIYE_TARIH], [ISKTUT], [ISKACIK], [ACIKLAMA], [C_YEDEK1], [C_YEDEK2], [D_YEDEK1], [D_YEDEK2], [F_YEDEK1], [F_YEDEK2], [I_YEDEK1], [I_YEDEK2], [S_YEDEK1], [S_YEDEK2], [URETICI_KODU], [ALICI_STOK_KODU], [OIVORAN], [OIVTUTAR], [TAXEXEMPTIONREASON], [TAXEXEMPTIONREASONCODE], [IRSKONT], [SIPARISNO], [SIPKONT], [SIPARIS_TARIH], [DLVADDSTREET], [DLVADDBUILDNAME], [DLVADDBUILDNUMBER], [DLVADDCITYNAME], [DLVADDCOUNTRY], [DLVSHPGOODCUSTOMID], [DLVSHPTRAMODECODE], [DLVADDPOSTALZONE])
                VALUES
                    (@EbelgeMas, @StokKodu, @StokAdi, @StraBf, @StraDovTip, @StraGcMik, @StraDovFiat, @OlcuBr, @Kdv, @IrsaliyeNo, @IrsaliyeTarih, @IskTut, @IskAcik, @Aciklama, @CYedek1, @CYedek2, @DYedek1, @DYedek2, @FYedek1, @FYedek2, @IYedek1, @IYedek2, @SYedek1, @SYedek2, @UreticiKodu, @AliciStokKodu, @OivOran, @OivTutar, @TaxExemptionReason, @TaxExemptionReasonCode, @IrsKont, @SiparisNo, @SipKont, @SiparisTarih, @DlvAddStreet, @DlvAddBuildName, @DlvAddBuildNumber, @DlvAddCityName, @DlvAddCountry, @DlvShpGoodCustomId, @DlvShpTraModeCode, @DlvAddPostalZone);
                """;
            kalemCommand.Parameters.Add(CreateParameter("@EbelgeMas", insertedMasInc));
            kalemCommand.Parameters.Add(CreateParameter("@StokKodu", ToDbValue(line.StockCode, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@StokAdi", ToDbValue(line.StockName, 100)));
            kalemCommand.Parameters.Add(CreateParameter("@StraBf", line.UnitPrice));
            kalemCommand.Parameters.Add(CreateParameter("@StraDovTip", currencyType));
            kalemCommand.Parameters.Add(CreateParameter("@StraGcMik", line.Quantity));
            kalemCommand.Parameters.Add(CreateParameter("@StraDovFiat", projection.ForeignUnitPriceAvailable ? line.UnitPrice : null));
            kalemCommand.Parameters.Add(CreateParameter("@OlcuBr", ToDbValue(MapLegacyUnitCode(line.UnitCode), 2)));
            kalemCommand.Parameters.Add(CreateParameter("@Kdv", line.VatPercent));
            kalemCommand.Parameters.Add(CreateParameter("@IrsaliyeNo", ToDbValue(line.DispatchNumber, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@IrsaliyeTarih", line.DispatchDate));
            kalemCommand.Parameters.Add(CreateParameter("@IskTut", line.DiscountAmount));
            kalemCommand.Parameters.Add(CreateParameter("@IskAcik", ToDbValue(line.DiscountDescription, 100)));
            kalemCommand.Parameters.Add(CreateParameter("@Aciklama", ToDbValue(line.Description, 4000)));
            kalemCommand.Parameters.Add(CreateParameter("@CYedek1", DBNull.Value));
            kalemCommand.Parameters.Add(CreateParameter("@CYedek2", DBNull.Value));
            kalemCommand.Parameters.Add(CreateParameter("@DYedek1", line.OrderDate));
            kalemCommand.Parameters.Add(CreateParameter("@DYedek2", line.DispatchDate));
            kalemCommand.Parameters.Add(CreateParameter("@FYedek1", line.LineExtensionAmount));
            kalemCommand.Parameters.Add(CreateParameter("@FYedek2", line.TotalTaxAmount));
            kalemCommand.Parameters.Add(CreateParameter("@IYedek1", line.LineId));
            kalemCommand.Parameters.Add(CreateParameter("@IYedek2", line.Taxes.Count));
            kalemCommand.Parameters.Add(CreateParameter("@SYedek1", ToDbValue(line.SecondaryCode, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@SYedek2", ToDbValue(line.Note, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@UreticiKodu", ToDbValue(line.ManufacturerCode, 100)));
            kalemCommand.Parameters.Add(CreateParameter("@AliciStokKodu", ToDbValue(line.BuyerStockCode, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@OivOran", line.SpecialTaxPercent));
            kalemCommand.Parameters.Add(CreateParameter("@OivTutar", line.SpecialTaxAmount));
            kalemCommand.Parameters.Add(CreateParameter("@TaxExemptionReason", ToDbValue(line.TaxExemptionReason, 500)));
            kalemCommand.Parameters.Add(CreateParameter("@TaxExemptionReasonCode", line.TaxExemptionReasonCode));
            kalemCommand.Parameters.Add(CreateParameter("@IrsKont", line.DispatchNumber is null ? 0 : 1));
            kalemCommand.Parameters.Add(CreateParameter("@SiparisNo", ToDbValue(line.OrderNumber, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@SipKont", line.OrderNumber is null ? 0 : 1));
            kalemCommand.Parameters.Add(CreateParameter("@SiparisTarih", line.OrderDate));
            kalemCommand.Parameters.Add(CreateParameter("@DlvAddStreet", ToDbValue(line.DeliveryStreet, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@DlvAddBuildName", ToDbValue(line.DeliveryBuildingName, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@DlvAddBuildNumber", ToDbValue(line.DeliveryBuildingNumber, 10)));
            kalemCommand.Parameters.Add(CreateParameter("@DlvAddCityName", ToDbValue(line.DeliveryCityName, 30)));
            kalemCommand.Parameters.Add(CreateParameter("@DlvAddCountry", ToDbValue(line.DeliveryCountry, 50)));
            kalemCommand.Parameters.Add(CreateParameter("@DlvShpGoodCustomId", ToDbValue(line.DeliveryCustomsId, 35)));
            kalemCommand.Parameters.Add(CreateParameter("@DlvShpTraModeCode", ToDbValue(line.TransportModeCode, 10)));
            kalemCommand.Parameters.Add(CreateParameter("@DlvAddPostalZone", ToDbValue(line.DeliveryPostalZone, 10)));
            await kalemCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (hasLegacyMasTax)
        {
            foreach (var tax in projection.HeaderTaxes)
            {
                await using var masTaxCommand = connection.CreateCommand();
                masTaxCommand.Transaction = transaction;
                masTaxCommand.CommandText = $"""
                    INSERT INTO {_ebelgeMasTaxTableName}
                        ([EBELGEMASINC], [TAXABLEAMOUNT], [TAXAMOUNT], [CALCULATIONSEQUENCENUMERIC], [TRANSACTIONCURRENCYTAXAMOUNT], [TAXPERCENT], [BASEUNITMEASURE], [PERUNITAMOUNT], [TAXEXEMPTIONREASON], [NAME], [TAXTYPECODE], [TAXEXEMPTIONREASONCODE])
                    VALUES
                        (@EbelgeMasInc, @TaxableAmount, @TaxAmount, @SequenceNo, @TransactionCurrencyTaxAmount, @TaxPercent, @BaseUnitMeasure, @PerUnitAmount, @TaxExemptionReason, @Name, @TaxTypeCode, @TaxExemptionReasonCode);
                    """;
                masTaxCommand.Parameters.Add(CreateParameter("@EbelgeMasInc", insertedMasInc));
                masTaxCommand.Parameters.Add(CreateParameter("@TaxableAmount", tax.TaxableAmount));
                masTaxCommand.Parameters.Add(CreateParameter("@TaxAmount", tax.TaxAmount));
                masTaxCommand.Parameters.Add(CreateParameter("@SequenceNo", tax.SequenceNumber));
                masTaxCommand.Parameters.Add(CreateParameter("@TransactionCurrencyTaxAmount", tax.TransactionCurrencyTaxAmount));
                masTaxCommand.Parameters.Add(CreateParameter("@TaxPercent", tax.TaxPercent));
                masTaxCommand.Parameters.Add(CreateParameter("@BaseUnitMeasure", tax.BaseUnitMeasure));
                masTaxCommand.Parameters.Add(CreateParameter("@PerUnitAmount", tax.PerUnitAmount));
                masTaxCommand.Parameters.Add(CreateParameter("@TaxExemptionReason", ToDbValue(tax.TaxExemptionReason, 500)));
                masTaxCommand.Parameters.Add(CreateParameter("@Name", ToDbValue(tax.Name, 200)));
                masTaxCommand.Parameters.Add(CreateParameter("@TaxTypeCode", ToDbValue(tax.TaxTypeCode, 50)));
                masTaxCommand.Parameters.Add(CreateParameter("@TaxExemptionReasonCode", tax.TaxExemptionReasonCode));
                await masTaxCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (hasLegacyKalemTax)
        {
            foreach (var line in projection.Lines)
            {
                foreach (var tax in line.Taxes)
                {
                    await using var kalemTaxCommand = connection.CreateCommand();
                    kalemTaxCommand.Transaction = transaction;
                    kalemTaxCommand.CommandText = $"""
                        INSERT INTO {_ebelgeKalemTaxTableName}
                            ([EBELGEMASINC], [EBELGELINEID], [TAXABLEAMOUNT], [TAXAMOUNT], [CALCULATIONSEQUENCENUMERIC], [TRANSACTIONCURRENCYTAXAMOUNT], [TAXPERCENT], [BASEUNITMEASURE], [PERUNITAMOUNT], [TAXEXEMPTIONREASON], [NAME], [TAXTYPECODE], [TAXEXEMPTIONREASONCODE])
                        VALUES
                            (@EbelgeMasInc, @LineId, @TaxableAmount, @TaxAmount, @SequenceNo, @TransactionCurrencyTaxAmount, @TaxPercent, @BaseUnitMeasure, @PerUnitAmount, @TaxExemptionReason, @Name, @TaxTypeCode, @TaxExemptionReasonCode);
                        """;
                    kalemTaxCommand.Parameters.Add(CreateParameter("@EbelgeMasInc", insertedMasInc));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@LineId", line.LineId));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@TaxableAmount", tax.TaxableAmount));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@TaxAmount", tax.TaxAmount));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@SequenceNo", tax.SequenceNumber));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@TransactionCurrencyTaxAmount", tax.TransactionCurrencyTaxAmount));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@TaxPercent", tax.TaxPercent));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@BaseUnitMeasure", tax.BaseUnitMeasure));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@PerUnitAmount", tax.PerUnitAmount));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@TaxExemptionReason", ToDbValue(tax.TaxExemptionReason, 500)));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@Name", ToDbValue(tax.Name, 200)));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@TaxTypeCode", ToDbValue(tax.TaxTypeCode, 50)));
                    kalemTaxCommand.Parameters.Add(CreateParameter("@TaxExemptionReasonCode", tax.TaxExemptionReasonCode));
                    await kalemTaxCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
    }

    private static LegacyDocumentProjection BuildLegacyDocumentProjection(IncomingDocument document)
    {
        var fallback = CreateFallbackLegacyDocumentProjection(document);
        if (string.IsNullOrWhiteSpace(document.PayloadRaw))
        {
            return fallback;
        }

        var rawPayload = document.PayloadRaw.TrimStart();
        if (!rawPayload.StartsWith('<'))
        {
            return fallback;
        }

        try
        {
            var xml = XDocument.Parse(document.PayloadRaw, LoadOptions.PreserveWhitespace);
            var root = xml.Root;
            if (root is null)
            {
                return fallback;
            }

            return BuildLegacyDocumentProjection(root, document, fallback);
        }
        catch
        {
            return fallback;
        }
    }

    private static LegacyDocumentProjection BuildLegacyDocumentProjection(
        XElement root,
        IncomingDocument document,
        LegacyDocumentProjection fallback)
    {
        var supplierParty = FindFirstDescendant(root, "AccountingSupplierParty") ?? FindFirstDescendant(root, "DespatchSupplierParty");
        var customerParty = FindFirstDescendant(root, "AccountingCustomerParty") ?? FindFirstDescendant(root, "DeliveryCustomerParty");
        var customerAddress = FindFirstDescendant(customerParty, "PostalAddress") ?? FindFirstDescendant(root, "DeliveryAddress");
        var deliveryAddress = FindFirstDescendant(root, "DeliveryAddress") ?? customerAddress;

        var customerIdentifier = ResolvePartyIdentifier(customerParty) ?? document.RecipientTaxNumber;
        var supplierIdentifier = ResolvePartyIdentifier(supplierParty) ?? document.SenderTaxNumber;
        var (customerTaxNumber, customerIdentityNumber) = SplitTaxOrIdentityNumber(customerIdentifier);
        var (supplierTaxNumber, supplierIdentityNumber) = SplitTaxOrIdentityNumber(supplierIdentifier);

        var orderReferences = ParseOrderReferences(root);
        var deliveryDate = ParseDate(GetPathValue(root, "Delivery", "ActualDeliveryDate"));
        var dueDate = ParseDate(GetDirectChildValue(root, "DueDate"));
        var currencyCode = FirstNotEmpty(
            GetDirectChildValue(root, "DocumentCurrencyCode"),
            GetDirectChildValue(root, "PricingCurrencyCode"),
            GetDirectChildValue(root, "TaxCurrencyCode"));
        var headerTaxes = ParseTaxSubtotals(GetDirectChildren(root, "TaxTotal"));
        var lines = ParseLegacyLines(root, fallback, deliveryAddress, orderReferences, currencyCode);

        var grossAmount = FirstNonNull(
            GetPathDecimal(root, "LegalMonetaryTotal", "LineExtensionAmount"),
            fallback.GrossAmount);
        var taxExclusiveAmount = FirstNonNull(
            GetPathDecimal(root, "LegalMonetaryTotal", "TaxExclusiveAmount"),
            fallback.TaxExclusiveAmount);
        var taxInclusiveAmount = FirstNonNull(
            GetPathDecimal(root, "LegalMonetaryTotal", "TaxInclusiveAmount"),
            fallback.TaxInclusiveAmount);
        var payableAmount = FirstNonNull(
            GetPathDecimal(root, "LegalMonetaryTotal", "PayableAmount"),
            taxInclusiveAmount,
            fallback.PayableAmount);
        var allowanceTotalAmount = FirstNonNull(
            GetPathDecimal(root, "LegalMonetaryTotal", "AllowanceTotalAmount"),
            fallback.AllowanceTotalAmount);

        var documentTypeCode = FirstNotEmpty(
            GetDirectChildValue(root, "InvoiceTypeCode"),
            GetDirectChildValue(root, "DespatchAdviceTypeCode"));

        var supplierAddress = FindFirstDescendant(supplierParty, "PostalAddress");

        return new LegacyDocumentProjection(
            CustomerCode: supplierIdentifier,
            CustomerName: ResolvePartyName(supplierParty),
            CustomerTaxNumber: supplierTaxNumber,
            CustomerIdentityNumber: supplierIdentityNumber,
            CustomerTaxOffice: ResolvePartyTaxOffice(supplierParty),
            CustomerAddress: BuildAddressText(supplierAddress),
            CustomerCountryCode: ResolveCountryCode(supplierAddress),
            CustomerCity: GetDirectChildValue(supplierAddress, "CityName"),
            CustomerDistrict: GetDirectChildValue(supplierAddress, "CitySubdivisionName"),
            SupplierName: ResolvePartyName(customerParty),
            SupplierTaxNumber: customerTaxNumber,
            SupplierIdentityNumber: customerIdentityNumber,
            CurrencyCode: string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode,
            ProfileCode: GetDirectChildValue(root, "ProfileID"),
            LegacyProfileId: null,
            DocumentTypeCode: documentTypeCode,
            ResponseCode: ParseInt(GetDirectChildValue(root, "ResponseCode")),
            PaymentMeansCode: GetPathValue(root, "PaymentMeans", "PaymentMeansCode"),
            DeliveryTermsId: GetPathValue(root, "DeliveryTerms", "ID"),
            BranchCode: null,
            UserBranchCode: null,
            DueDate: dueDate,
            DeliveryDate: deliveryDate,
            Note: GetDirectChildValue(root, "Note"),
            TaxExemptionReason: headerTaxes.Select(x => x.TaxExemptionReason).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            TaxExemptionReasonCode: headerTaxes.Select(x => x.TaxExemptionReasonCode).FirstOrDefault(x => x.HasValue),
            GrossAmount: grossAmount,
            TaxExclusiveAmount: taxExclusiveAmount,
            TaxInclusiveAmount: taxInclusiveAmount,
            PayableAmount: payableAmount,
            AllowanceTotalAmount: allowanceTotalAmount,
            ForeignGrossAmount: IsForeignCurrency(currencyCode) ? grossAmount : null,
            ForeignPayableAmount: IsForeignCurrency(currencyCode) ? payableAmount : null,
            ForeignVatAmount: IsForeignCurrency(currencyCode) ? headerTaxes.Where(IsVatTax).Sum(x => x.TaxAmount ?? 0m) : null,
            ForeignSpecialTaxAmount: IsForeignCurrency(currencyCode) ? headerTaxes.Where(IsSpecialConsumptionTax).Sum(x => x.TaxAmount ?? 0m) : null,
            ForeignUnitPriceAvailable: IsForeignCurrency(currencyCode),
            OrderReferences: orderReferences,
            HeaderTaxes: headerTaxes,
            Lines: lines);
    }

    private static LegacyDocumentProjection CreateFallbackLegacyDocumentProjection(IncomingDocument document)
    {
        var (customerTaxNumber, customerIdentityNumber) = SplitTaxOrIdentityNumber(document.RecipientTaxNumber);
        var (supplierTaxNumber, supplierIdentityNumber) = SplitTaxOrIdentityNumber(document.SenderTaxNumber);
        var fallbackLine = new LegacyLineData(
            LineId: 1,
            StockCode: document.DocumentNumber,
            StockName: document.DocumentNumber,
            Quantity: 1m,
            UnitCode: "AD",
            UnitPrice: 0m,
            LineExtensionAmount: 0m,
            VatPercent: 0m,
            TotalTaxAmount: 0m,
            DiscountAmount: null,
            DiscountDescription: null,
            Description: $"CalibraHub otomatik belge satiri: {document.DocumentNumber}",
            ManufacturerCode: document.DocumentNumber,
            BuyerStockCode: null,
            SecondaryCode: document.DocumentNumber,
            Note: null,
            SpecialTaxPercent: null,
            SpecialTaxAmount: null,
            TaxExemptionReason: null,
            TaxExemptionReasonCode: null,
            OrderNumber: null,
            OrderDate: null,
            DispatchNumber: null,
            DispatchDate: null,
            DeliveryStreet: null,
            DeliveryBuildingName: null,
            DeliveryBuildingNumber: null,
            DeliveryCityName: null,
            DeliveryCountry: null,
            DeliveryCustomsId: null,
            TransportModeCode: null,
            DeliveryPostalZone: null,
            Taxes:
            [
                new LegacyTaxData(1m, 0m, 0m, null, 0m, null, null, null, "KDV", "0015", null)
            ]);

        return new LegacyDocumentProjection(
            CustomerCode: document.SenderTaxNumber,
            CustomerName: document.SenderName,
            CustomerTaxNumber: supplierTaxNumber,
            CustomerIdentityNumber: supplierIdentityNumber,
            CustomerTaxOffice: null,
            CustomerAddress: null,
            CustomerCountryCode: null,
            CustomerCity: null,
            CustomerDistrict: null,
            SupplierName: null,
            SupplierTaxNumber: customerTaxNumber,
            SupplierIdentityNumber: customerIdentityNumber,
            CurrencyCode: null,
            ProfileCode: null,
            LegacyProfileId: null,
            DocumentTypeCode: null,
            ResponseCode: null,
            PaymentMeansCode: null,
            DeliveryTermsId: null,
            BranchCode: null,
            UserBranchCode: null,
            DueDate: null,
            DeliveryDate: null,
            Note: null,
            TaxExemptionReason: null,
            TaxExemptionReasonCode: null,
            GrossAmount: 0m,
            TaxExclusiveAmount: 0m,
            TaxInclusiveAmount: 0m,
            PayableAmount: 0m,
            AllowanceTotalAmount: null,
            ForeignGrossAmount: null,
            ForeignPayableAmount: null,
            ForeignVatAmount: null,
            ForeignSpecialTaxAmount: null,
            ForeignUnitPriceAvailable: false,
            OrderReferences: [],
            HeaderTaxes:
            [
                new LegacyTaxData(1m, 0m, 0m, null, 0m, null, null, null, "KDV", "0015", null)
            ],
            Lines: [fallbackLine]);
    }

    private static List<LegacyLineData> ParseLegacyLines(
        XElement root,
        LegacyDocumentProjection fallback,
        XElement? defaultDeliveryAddress,
        IReadOnlyList<LegacyOrderReferenceData> orderReferences,
        string? currencyCode)
    {
        var lineElements = GetDirectChildren(root, "InvoiceLine")
            .Concat(GetDirectChildren(root, "DespatchLine"))
            .ToArray();
        if (lineElements.Length == 0)
        {
            return fallback.Lines.ToList();
        }

        var lines = new List<LegacyLineData>(lineElements.Length);
        for (var index = 0; index < lineElements.Length; index++)
        {
            var lineElement = lineElements[index];
            var quantityElement = FindFirstChild(lineElement, "InvoicedQuantity") ?? FindFirstChild(lineElement, "DeliveredQuantity");
            var quantity = ParseDecimal(quantityElement?.Value) ?? 0m;
            var unitCode = quantityElement?.Attribute("unitCode")?.Value ?? "AD";
            var lineTaxes = ParseTaxSubtotals(GetDirectChildren(lineElement, "TaxTotal"));
            var firstVatTax = lineTaxes.FirstOrDefault(IsVatTax);
            var firstSpecialTax = lineTaxes.FirstOrDefault(IsSpecialConsumptionTax);
            var allowanceCharges = GetDirectChildren(lineElement, "AllowanceCharge").ToArray();
            var allowanceAmount = allowanceCharges
                .Select(x => ParseDecimal(GetDirectChildValue(x, "Amount")))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .DefaultIfEmpty(0m)
                .Sum();
            var allowanceDescription = JoinDistinct("; ",
                allowanceCharges
                    .Select(x => FirstNotEmpty(
                        GetDirectChildValue(x, "AllowanceChargeReason"),
                        GetDirectChildValue(x, "AllowanceChargeReasonCode")))
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
            var orderReference = ParseOrderReferences(lineElement).FirstOrDefault() ?? orderReferences.FirstOrDefault();
            var dispatchReference = ParseDocumentReferences(lineElement, "DespatchDocumentReference").FirstOrDefault()
                                 ?? ParseDocumentReferences(root, "DespatchDocumentReference").FirstOrDefault();
            var lineDeliveryAddress = FindFirstDescendant(lineElement, "DeliveryAddress") ?? defaultDeliveryAddress;
            var item = FindFirstDescendant(lineElement, "Item");
            var description = JoinDistinct(" | ",
                GetDirectChildren(item, "Description")
                    .Select(x => x.Value?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Concat(new[] { GetDirectChildValue(item, "Name") }));
            var lineId = ParseInt(GetDirectChildValue(lineElement, "ID")) ?? (index + 1);

            lines.Add(new LegacyLineData(
                LineId: lineId,
                StockCode: FirstNotEmpty(
                    GetPathValue(lineElement, "Item", "SellersItemIdentification", "ID"),
                    GetPathValue(lineElement, "Item", "ManufacturersItemIdentification", "ID"),
                    GetPathValue(lineElement, "Item", "StandardItemIdentification", "ID"),
                    GetDirectChildValue(item, "Name"),
                    fallback.Lines.First().StockCode),
                StockName: FirstNotEmpty(
                    GetDirectChildValue(item, "Name"),
                    GetPathValue(lineElement, "Item", "SellersItemIdentification", "ID"),
                    fallback.Lines.First().StockName),
                Quantity: quantity,
                UnitCode: unitCode,
                UnitPrice: ParseDecimal(GetPathValue(lineElement, "Price", "PriceAmount")) ?? 0m,
                LineExtensionAmount: ParseDecimal(GetDirectChildValue(lineElement, "LineExtensionAmount")),
                VatPercent: FirstNonNull(firstVatTax?.TaxPercent, CalculateTaxPercent(firstVatTax)),
                TotalTaxAmount: lineTaxes.Sum(x => x.TaxAmount ?? 0m),
                DiscountAmount: allowanceAmount == 0m ? null : allowanceAmount,
                DiscountDescription: string.IsNullOrWhiteSpace(allowanceDescription) ? null : allowanceDescription,
                Description: description,
                ManufacturerCode: FirstNotEmpty(
                    GetPathValue(lineElement, "Item", "ManufacturersItemIdentification", "ID"),
                    GetPathValue(lineElement, "Item", "SellersItemIdentification", "ID")),
                BuyerStockCode: GetPathValue(lineElement, "Item", "BuyersItemIdentification", "ID"),
                SecondaryCode: GetPathValue(lineElement, "Item", "StandardItemIdentification", "ID"),
                Note: GetDirectChildValue(lineElement, "Note"),
                SpecialTaxPercent: FirstNonNull(firstSpecialTax?.TaxPercent, CalculateTaxPercent(firstSpecialTax)),
                SpecialTaxAmount: firstSpecialTax?.TaxAmount,
                TaxExemptionReason: lineTaxes.Select(x => x.TaxExemptionReason).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                TaxExemptionReasonCode: lineTaxes.Select(x => x.TaxExemptionReasonCode).FirstOrDefault(x => x.HasValue),
                OrderNumber: orderReference?.Id,
                OrderDate: orderReference?.IssueDate,
                DispatchNumber: dispatchReference?.Id,
                DispatchDate: dispatchReference?.IssueDate,
                DeliveryStreet: GetDirectChildValue(lineDeliveryAddress, "StreetName"),
                DeliveryBuildingName: GetDirectChildValue(lineDeliveryAddress, "BuildingName"),
                DeliveryBuildingNumber: GetDirectChildValue(lineDeliveryAddress, "BuildingNumber"),
                DeliveryCityName: GetDirectChildValue(lineDeliveryAddress, "CityName"),
                DeliveryCountry: FirstNotEmpty(
                    GetPathValue(lineDeliveryAddress, "Country", "IdentificationCode"),
                    GetPathValue(lineDeliveryAddress, "Country", "Name")),
                DeliveryCustomsId: GetPathValue(lineElement, "Delivery", "Shipment", "GoodsItem", "CustomsImportClassifiedIndicator"),
                TransportModeCode: GetPathValue(lineElement, "Delivery", "Shipment", "ShipmentStage", "TransportModeCode"),
                DeliveryPostalZone: GetDirectChildValue(lineDeliveryAddress, "PostalZone"),
                Taxes: lineTaxes.Count == 0
                    ? fallback.Lines.First().Taxes
                    : lineTaxes));
        }

        return lines;
    }

    private static List<LegacyTaxData> ParseTaxSubtotals(IEnumerable<XElement> taxTotals)
    {
        var result = new List<LegacyTaxData>();
        var sequence = 1m;

        foreach (var taxTotal in taxTotals)
        {
            foreach (var subtotal in GetDirectChildren(taxTotal, "TaxSubtotal"))
            {
                var tax = new LegacyTaxData(
                    SequenceNumber: ParseDecimal(GetDirectChildValue(subtotal, "CalculationSequenceNumeric")) ?? sequence++,
                    TaxableAmount: ParseDecimal(GetDirectChildValue(subtotal, "TaxableAmount")),
                    TaxAmount: ParseDecimal(GetDirectChildValue(subtotal, "TaxAmount")),
                    TransactionCurrencyTaxAmount: ParseDecimal(GetDirectChildValue(subtotal, "TransactionCurrencyTaxAmount")),
                    TaxPercent: FirstNonNull(
                        ParseDecimal(GetPathValue(subtotal, "TaxCategory", "Percent")),
                        ParseDecimal(GetDirectChildValue(subtotal, "Percent"))),
                    BaseUnitMeasure: ParseDecimal(GetDirectChildValue(subtotal, "BaseUnitMeasure")),
                    PerUnitAmount: ParseDecimal(GetDirectChildValue(subtotal, "PerUnitAmount")),
                    TaxExemptionReason: FirstNotEmpty(
                        GetPathValue(subtotal, "TaxCategory", "TaxExemptionReason"),
                        GetDirectChildValue(subtotal, "TaxExemptionReason")),
                    Name: FirstNotEmpty(
                        GetPathValue(subtotal, "TaxCategory", "TaxScheme", "Name"),
                        GetDirectChildValue(subtotal, "Name")),
                    TaxTypeCode: FirstNotEmpty(
                        GetPathValue(subtotal, "TaxCategory", "TaxScheme", "TaxTypeCode"),
                        GetDirectChildValue(subtotal, "TaxTypeCode")),
                    TaxExemptionReasonCode: FirstNonNull(
                        ParseInt(GetPathValue(subtotal, "TaxCategory", "TaxExemptionReasonCode")),
                        ParseInt(GetDirectChildValue(subtotal, "TaxExemptionReasonCode"))));

                if (!tax.TaxAmount.HasValue &&
                    !tax.TaxableAmount.HasValue &&
                    string.IsNullOrWhiteSpace(tax.Name) &&
                    string.IsNullOrWhiteSpace(tax.TaxTypeCode))
                {
                    continue;
                }

                result.Add(tax);
            }
        }

        return result;
    }

    private static IReadOnlyList<LegacyOrderReferenceData> ParseOrderReferences(XElement root) =>
        ParseDocumentReferences(root, "OrderReference");

    private static IReadOnlyList<LegacyOrderReferenceData> ParseDocumentReferences(XElement root, string elementName)
    {
        return FindDescendants(root, elementName)
            .Select(x => new LegacyOrderReferenceData(
                Id: GetDirectChildValue(x, "ID"),
                IssueDate: ParseDate(GetDirectChildValue(x, "IssueDate"))))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) || x.IssueDate.HasValue)
            .ToArray();
    }

    private static string? ResolvePartyIdentifier(XElement? party)
    {
        if (party is null)
        {
            return null;
        }

        return FirstNotEmpty(
            FindDescendants(party, "CompanyID").Select(x => x.Value?.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            FindDescendants(party, "PartyIdentification")
                .Select(x => GetDirectChildValue(x, "ID"))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string? ResolvePartyName(XElement? party)
    {
        if (party is null)
        {
            return null;
        }

        return FirstNotEmpty(
            FindDescendants(party, "PartyName").Select(x => GetDirectChildValue(x, "Name")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            FindDescendants(party, "RegistrationName").Select(x => x.Value?.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            FindDescendants(party, "FirstName").Select(x => x.Value?.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            FindDescendants(party, "FamilyName").Select(x => x.Value?.Trim()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string? ResolvePartyTaxOffice(XElement? party)
    {
        if (party is null)
        {
            return null;
        }

        return FirstNotEmpty(
            FindDescendants(party, "TaxScheme").Select(x => GetDirectChildValue(x, "Name")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
            FindDescendants(party, "TaxScheme").Select(x => GetDirectChildValue(x, "TaxTypeCode")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string? ResolveCountryCode(XElement? address)
    {
        var code = GetPathValue(address, "Country", "IdentificationCode");
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        var name = GetPathValue(address, "Country", "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Trim().ToUpperInvariant() switch
        {
            "TURKIYE" => "TR",
            "TÜRKIYE" => "TR",
            "TÜRKİYE" => "TR",
            "TURKEY" => "TR",
            _ => name.Trim().Length <= 4 ? name.Trim() : name.Trim()[..4]
        };
    }

    private static string? BuildAddressText(XElement? address)
    {
        if (address is null)
        {
            return null;
        }

        return JoinDistinct(", ",
            new[]
            {
                GetDirectChildValue(address, "StreetName"),
                GetDirectChildValue(address, "BuildingName"),
                GetDirectChildValue(address, "BuildingNumber"),
                GetDirectChildValue(address, "Room"),
                GetDirectChildValue(address, "CitySubdivisionName"),
                GetDirectChildValue(address, "CityName"),
                GetDirectChildValue(address, "PostalZone"),
                GetPathValue(address, "Country", "Name")
            });
    }

    private static string? GetPathValue(XElement? root, params string[] path)
    {
        var node = Navigate(root, path);
        return node?.Value?.Trim();
    }

    private static decimal? GetPathDecimal(XElement? root, params string[] path) =>
        ParseDecimal(GetPathValue(root, path));

    private static XElement? Navigate(XElement? root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            current = FindFirstChild(current, segment);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static IEnumerable<XElement> GetDirectChildren(XContainer? root, string localName)
    {
        if (root is null)
        {
            return Enumerable.Empty<XElement>();
        }

        return root.Elements().Where(x => string.Equals(x.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement? FindFirstChild(XContainer? root, string localName) =>
        GetDirectChildren(root, localName).FirstOrDefault();

    private static IEnumerable<XElement> FindDescendants(XContainer? root, string localName)
    {
        if (root is null)
        {
            return Enumerable.Empty<XElement>();
        }

        return root.Descendants().Where(x => string.Equals(x.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
    }

    private static XElement? FindFirstDescendant(XContainer? root, string localName) =>
        FindDescendants(root, localName).FirstOrDefault();

    private static string? GetDirectChildValue(XElement? root, string localName) =>
        FindFirstChild(root, localName)?.Value?.Trim();

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static (string? TaxNumber, string? IdentityNumber) SplitTaxOrIdentityNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var normalized = value.Trim();
        return normalized.Length switch
        {
            11 => (null, normalized),
            10 => (normalized, null),
            _ => (normalized, null)
        };
    }

    private static string? MapLegacyUnitCode(string? unitCode)
    {
        if (string.IsNullOrWhiteSpace(unitCode))
        {
            return null;
        }

        return unitCode.Trim().ToUpperInvariant() switch
        {
            "C62" => "AD",
            "NIU" => "AD",
            "KGM" => "KG",
            "MTR" => "MT",
            "LTR" => "LT",
            var code when code.Length <= 2 => code,
            var code => code[..2]
        };
    }

    private static short? MapCurrencyType(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            return null;
        }

        return currencyCode.Trim().ToUpperInvariant() switch
        {
            "TRY" => 0,
            _ => null
        };
    }

    private static bool IsForeignCurrency(string? currencyCode) =>
        !string.IsNullOrWhiteSpace(currencyCode) &&
        !string.Equals(currencyCode.Trim(), "TRY", StringComparison.OrdinalIgnoreCase);

    private static decimal? FirstNonNull(params decimal?[] values) =>
        values.FirstOrDefault(x => x.HasValue);

    private static int? FirstNonNull(params int?[] values) =>
        values.FirstOrDefault(x => x.HasValue);

    private static bool IsVatTax(LegacyTaxData tax) =>
        string.Equals(tax.TaxTypeCode, "0015", StringComparison.OrdinalIgnoreCase) ||
        (tax.Name?.Contains("KDV", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsSpecialConsumptionTax(LegacyTaxData tax) =>
        string.Equals(tax.TaxTypeCode, "0071", StringComparison.OrdinalIgnoreCase) ||
        (tax.Name?.Contains("OTV", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (tax.Name?.Contains("ÖTV", StringComparison.OrdinalIgnoreCase) ?? false);

    private static decimal? CalculateTaxPercent(LegacyTaxData? tax)
    {
        if (tax?.TaxAmount is null || tax.TaxableAmount is null || tax.TaxableAmount.Value == 0m)
        {
            return null;
        }

        return Math.Round((tax.TaxAmount.Value / tax.TaxableAmount.Value) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<LegacyVatBucketData> BuildVatBuckets(IReadOnlyCollection<LegacyTaxData> taxes)
    {
        return taxes
            .Where(IsVatTax)
            .Select(x => new
            {
                Percent = FirstNonNull(x.TaxPercent, CalculateTaxPercent(x)),
                Amount = x.TaxAmount ?? 0m
            })
            .Where(x => x.Percent.HasValue)
            .GroupBy(x => x.Percent!.Value)
            .Select(x => new LegacyVatBucketData(x.Key, x.Sum(y => y.Amount)))
            .OrderBy(x => x.Percent)
            .Take(7)
            .ToArray();
    }

    private static string? JoinDistinct(string separator, IEnumerable<string?> values)
    {
        var items = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return items.Length == 0 ? null : string.Join(separator, items);
    }

    private static string? FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _schemaCache = new();

    private async Task<bool> TableExistsAsync(
        SqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"TABLE_{_schema}_{tableName}";
        if (_schemaCache.TryGetValue(cacheKey, out var exists)) return exists;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN OBJECT_ID(@QualifiedName, 'U') IS NULL THEN 0 ELSE 1 END;
            """;
        command.Parameters.Add(CreateParameter("@QualifiedName", $"{_schema}.{tableName}"));

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        _schemaCache.TryAdd(cacheKey, result);
        return result;
    }

    private async Task<bool> ColumnExistsAsync(
        SqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"COLUMN_{_schema}_{tableName}_{columnName}";
        if (_schemaCache.TryGetValue(cacheKey, out var exists)) return exists;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN COL_LENGTH(@QualifiedName, @ColumnName) IS NULL THEN 0 ELSE 1 END;
            """;
        command.Parameters.Add(CreateParameter("@QualifiedName", $"{_schema}.{tableName}"));
        command.Parameters.Add(CreateParameter("@ColumnName", columnName));

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        _schemaCache.TryAdd(cacheKey, result);
        return result;
    }

    private static object ToDbValue(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
        {
            return DBNull.Value;
        }

        if (normalized.Length > maxLength)
        {
            normalized = normalized[..maxLength];
        }

        return normalized;
    }

    private static SqlParameter CreateParameter(string name, object? value) =>
        new()
        {
            ParameterName = name,
            Value = value ?? DBNull.Value
        };

    private static string MapFtirsip(DocumentKind kind) =>
        kind switch
        {
            DocumentKind.EDispatch => "I",
            DocumentKind.EArchive => "A",
            _ => "F"
        };

    private static int MapTipi(DocumentKind kind) =>
        kind switch
        {
            DocumentKind.EDispatch => 2,
            DocumentKind.EArchive => 3,
            _ => 1
        };

    private static int MapBelgeTipi(DocumentKind kind) =>
        kind switch
        {
            DocumentKind.EDispatch => 2,
            DocumentKind.EArchive => 3,
            _ => 1
        };

    private sealed record LegacyDocumentProjection(
        string? CustomerCode,
        string? CustomerName,
        string? CustomerTaxNumber,
        string? CustomerIdentityNumber,
        string? CustomerTaxOffice,
        string? CustomerAddress,
        string? CustomerCountryCode,
        string? CustomerCity,
        string? CustomerDistrict,
        string? SupplierName,
        string? SupplierTaxNumber,
        string? SupplierIdentityNumber,
        string? CurrencyCode,
        string? ProfileCode,
        byte? LegacyProfileId,
        string? DocumentTypeCode,
        int? ResponseCode,
        string? PaymentMeansCode,
        string? DeliveryTermsId,
        short? BranchCode,
        short? UserBranchCode,
        DateTime? DueDate,
        DateTime? DeliveryDate,
        string? Note,
        string? TaxExemptionReason,
        int? TaxExemptionReasonCode,
        decimal? GrossAmount,
        decimal? TaxExclusiveAmount,
        decimal? TaxInclusiveAmount,
        decimal? PayableAmount,
        decimal? AllowanceTotalAmount,
        decimal? ForeignGrossAmount,
        decimal? ForeignPayableAmount,
        decimal? ForeignVatAmount,
        decimal? ForeignSpecialTaxAmount,
        bool ForeignUnitPriceAvailable,
        IReadOnlyList<LegacyOrderReferenceData> OrderReferences,
        IReadOnlyList<LegacyTaxData> HeaderTaxes,
        IReadOnlyList<LegacyLineData> Lines);

    private sealed record LegacyOrderReferenceData(
        string? Id,
        DateTime? IssueDate);

    private sealed record LegacyTaxData(
        decimal SequenceNumber,
        decimal? TaxableAmount,
        decimal? TaxAmount,
        decimal? TransactionCurrencyTaxAmount,
        decimal? TaxPercent,
        decimal? BaseUnitMeasure,
        decimal? PerUnitAmount,
        string? TaxExemptionReason,
        string? Name,
        string? TaxTypeCode,
        int? TaxExemptionReasonCode);

    private sealed record LegacyVatBucketData(
        decimal Percent,
        decimal Amount);

    private sealed record LegacyLineData(
        int LineId,
        string? StockCode,
        string? StockName,
        decimal Quantity,
        string? UnitCode,
        decimal UnitPrice,
        decimal? LineExtensionAmount,
        decimal? VatPercent,
        decimal? TotalTaxAmount,
        decimal? DiscountAmount,
        string? DiscountDescription,
        string? Description,
        string? ManufacturerCode,
        string? BuyerStockCode,
        string? SecondaryCode,
        string? Note,
        decimal? SpecialTaxPercent,
        decimal? SpecialTaxAmount,
        string? TaxExemptionReason,
        int? TaxExemptionReasonCode,
        string? OrderNumber,
        DateTime? OrderDate,
        string? DispatchNumber,
        DateTime? DispatchDate,
        string? DeliveryStreet,
        string? DeliveryBuildingName,
        string? DeliveryBuildingNumber,
        string? DeliveryCityName,
        string? DeliveryCountry,
        string? DeliveryCustomsId,
        string? TransportModeCode,
        string? DeliveryPostalZone,
        IReadOnlyList<LegacyTaxData> Taxes);

    private static IncomingDocument MapLegacyIncomingDocument(SqlDataReader reader)
    {
        var incKeyNo = reader.GetInt32(0);
        var documentNumber = reader.IsDBNull(1) ? $"BELGE-{incKeyNo}" : reader.GetString(1);
        var ftirsip = reader.IsDBNull(2) ? null : reader.GetString(2);
        var tipi = reader.IsDBNull(3) ? (short?)null : reader.GetInt16(3);
        var belgeTipi = reader.IsDBNull(4) ? (byte?)null : reader.GetByte(4);
        var uuid = reader.IsDBNull(5) ? null : reader.GetString(5);
        var senderVno = reader.IsDBNull(6) ? null : reader.GetString(6);
        var cariVergiNo = reader.IsDBNull(7) ? null : reader.GetString(7);
        var cariTckn = reader.IsDBNull(8) ? null : reader.GetString(8);
        var tarih = reader.IsDBNull(9) ? DateTime.Now : reader.GetDateTime(9);
        var durum = reader.IsDBNull(10) ? (short)0 : reader.GetInt16(10);
        var senderName = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetString(11) : null;
        var cariIsim = reader.FieldCount > 12 && !reader.IsDBNull(12) ? reader.GetString(12) : null;
        var payloadRaw = reader.FieldCount > 13 && !reader.IsDBNull(13) ? reader.GetString(13) : "{}";
        var isProcessed = reader.FieldCount > 14 && !reader.IsDBNull(14) && Convert.ToInt32(reader.GetValue(14)) == 1;

        var kind = MapLegacyKind(belgeTipi, ftirsip, tipi);
        var envelopeId = string.IsNullOrWhiteSpace(uuid) ? $"CBT-{incKeyNo}" : uuid;

        // CARI sütunlarında SENDER(Gönderen) bilgisi tutulduğu için eşleştirmeleri yer değiştiriyoruz:
        var recipientTaxNumber = string.IsNullOrWhiteSpace(senderVno) ? "0000000000" : senderVno;
        var senderTaxNumber = !string.IsNullOrWhiteSpace(cariVergiNo)
            ? cariVergiNo
            : (!string.IsNullOrWhiteSpace(cariTckn) ? cariTckn : "0000000000");

        var importedAt = tarih;

        var document = new IncomingDocument
        {
            Id = BuildDeterministicGuid($"CBT_EBELGEMAS:{incKeyNo}"),
            IntegratorSettingsId = 0,
            EnvelopeId = envelopeId,
            DocumentNumber = documentNumber,
            Kind = kind,
            IssueDate = DateOnly.FromDateTime(tarih),
            SenderTaxNumber = senderTaxNumber,
            SenderName = string.IsNullOrWhiteSpace(cariIsim) ? null : cariIsim,
            RecipientTaxNumber = recipientTaxNumber,
            PayloadRaw = payloadRaw,
            ImportedAt = importedAt,
            IsProcessed = isProcessed
        };

        if (durum == 1)
        {
            document.MarkApproved();
        }
        else if (durum == 2)
        {
            document.MarkRejected();
        }

        return document;
    }

    private static DocumentKind MapLegacyKind(byte? belgeTipi, string? ftirsip, short? tipi)
    {
        if (belgeTipi is not null)
        {
            return belgeTipi.Value switch
            {
                3 => DocumentKind.EArchive,
                2 => DocumentKind.EDispatch,
                _ => DocumentKind.EInvoice
            };
        }

        return MapLegacyKind(ftirsip, tipi);
    }

    private static DocumentKind MapLegacyKind(string? ftirsip, short? tipi)
    {
        if (tipi == 3 || string.Equals(ftirsip, "A", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentKind.EArchive;
        }

        if (tipi == 2 || string.Equals(ftirsip, "I", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentKind.EDispatch;
        }

        return DocumentKind.EInvoice;
    }

    private static Guid BuildDeterministicGuid(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private static IncomingDocument MapIncomingDocument(SqlDataReader reader)
    {
        var kindRaw = reader.GetString(4);
        if (!Enum.TryParse(kindRaw, true, out DocumentKind kind) || !Enum.IsDefined(kind))
        {
            kind = DocumentKind.EInvoice;
        }

        var approvalStatusRaw = reader.GetString(9);
        if (!Enum.TryParse(approvalStatusRaw, true, out ApprovalStatus approvalStatus) || !Enum.IsDefined(approvalStatus))
        {
            approvalStatus = ApprovalStatus.Pending;
        }

        var senderName = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetString(11) : null;
        var isProcessed = reader.FieldCount > 12 && !reader.IsDBNull(12) && Convert.ToInt32(reader.GetValue(12)) == 1;

        var document = new IncomingDocument
        {
            Id = reader.GetGuid(0),
            IntegratorSettingsId = reader.GetInt32(1),
            EnvelopeId = reader.GetString(2),
            DocumentNumber = reader.GetString(3),
            Kind = kind,
            IssueDate = DateOnly.FromDateTime(reader.GetDateTime(5)),
            SenderTaxNumber = reader.GetString(6),
            SenderName = string.IsNullOrWhiteSpace(senderName) ? null : senderName,
            RecipientTaxNumber = reader.GetString(7),
            PayloadRaw = reader.GetString(8),
            ImportedAt = reader.GetFieldValue<DateTime>(10),
            IsProcessed = isProcessed
        };

        if (approvalStatus == ApprovalStatus.Approved)
        {
            document.MarkApproved();
        }
        else if (approvalStatus == ApprovalStatus.Rejected)
        {
            document.MarkRejected();
        }

        return document;
    }

    public async Task UpdateIsProcessedAsync(Guid id, bool isProcessed, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        if (await LegacyTablesExistAsync(connection, cancellationToken))
        {
            var document = await GetByIdAsync(id, cancellationToken);
            if (document != null)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"UPDATE {_ebelgeMasTableName} SET [ISLENDI] = @IsProcessed WHERE [UUID] = @EnvelopeId";
                command.Parameters.Add(CreateParameter("@IsProcessed", isProcessed ? 1 : 0));
                command.Parameters.Add(CreateParameter("@EnvelopeId", document.EnvelopeId));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        else if (await TableExistsAsync(connection, "IncomingDocument", cancellationToken))
        {
            var hasProcessedColumn = await ColumnExistsAsync(connection, "IncomingDocument", "IsProcessed", cancellationToken);
            if (hasProcessedColumn)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"UPDATE {_tableName} SET [IsProcessed] = @IsProcessed WHERE [Id] = @Id";
                command.Parameters.Add(CreateParameter("@IsProcessed", isProcessed ? 1 : 0));
                command.Parameters.Add(CreateParameter("@Id", id));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
