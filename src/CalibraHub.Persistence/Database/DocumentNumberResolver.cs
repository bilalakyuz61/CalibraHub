using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using Microsoft.Data.SqlClient;

namespace CalibraHub.Persistence.Database;

/// <summary>
/// Belge numarası üretiminin TEK kaynağı — açık bir transaction içinden çağrılır.
///
/// NEDEN AYRI SINIF (2026-08-25): üretim fişi numarası iki ayrı yerde üretiliyordu.
/// <c>SqlStockDocRepository</c> (bileşen sarfı) numarayı DocumentNumberRule'dan çözüyordu;
/// <c>SqlWorkOrderOperationRepository</c> (mamul girişi) ise satır içi MAX+1 kullanıyordu,
/// çünkü orada numara servisi yoktu. İkisi bugün aynı sonucu veriyordu — ama YALNIZCA
/// "uretim_fisi" için bir numara kuralı tanımlı olmadığı sürece. Kural tanımlandığı gün
/// aynı iş emrinin sarf fişi kurala uyan, mamul giriş fişi ise "UF-2026-0001" biçiminde
/// numara alacaktı: aynı belge türünde iki farklı seri, kimsenin fark etmeyeceği bir ayrışma.
///
/// Artık iki yol da buradan geçiyor; kural bir kez tanımlandığında ikisi birden uyar.
///
/// Yedek (kural yoksa) sorguda <c>UPDLOCK, HOLDLOCK</c> kullanılır: iki eşzamanlı işlem
/// aynı MAX değerini okuyup aynı numarayı üretmesin. Birleştirmeden önce bu kilit yalnız
/// iki yoldan BİRİNDE vardı — birleştirme, daha güvenli olanı ikisine de taşıdı.
/// </summary>
public static class DocumentNumberResolver
{
    /// <summary>
    /// Belge türü koduna göre numara üretir. Önce tanımlı kural denenir; kural yoksa
    /// (ya da boş dönerse) <c>{prefix}-{yıl}-{sıra}</c> yedeği kullanılır.
    /// </summary>
    /// <param name="schema">Şema adı (kaçışsız — burada kaçırılır).</param>
    /// <param name="typeCode">DocumentType.Code (ör. "uretim_fisi").</param>
    /// <param name="prefix">Yedek biçimin öneki (ör. "UF").</param>
    public static async Task<string> ResolveAsync(
        SqlConnection conn,
        SqlTransaction tx,
        string schema,
        IDocumentNumberService numberService,
        string typeCode,
        string prefix,
        int? createdById,
        DateTime docDate,
        CancellationToken ct)
    {
        var s = schema.Replace("]", "]]");

        await using (var typeCmd = conn.CreateCommand())
        {
            typeCmd.Transaction = tx;
            typeCmd.CommandText = $"SELECT [Id] FROM [{s}].[DocumentType] WHERE [Code] = @Code;";
            typeCmd.Parameters.AddWithValue("@Code", typeCode);
            var typeIdObj = await typeCmd.ExecuteScalarAsync(ct);
            if (typeIdObj is int typeId)
            {
                var ruleNo = await numberService.GenerateNextAsync(
                    new DocumentNumberContext(typeId, null, null, createdById, null, docDate), ct);
                if (!string.IsNullOrWhiteSpace(ruleNo)) return ruleNo;
            }
        }

        var year = DateTime.Now.Year;
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // SUBSTRING başlangıcı önek uzunluğundan türetilir ("UF-2026-" → 9). Sabit sayı
        // yazılsaydı farklı uzunlukta bir önek sessizce yanlış sırayı okurdu.
        cmd.CommandText = $"""
            SELECT ISNULL(MAX(TRY_CAST(SUBSTRING([DocumentNumber], LEN(@Prefix) + 7, 10) AS INT)), 0) + 1
            FROM [{s}].[Document] WITH (UPDLOCK, HOLDLOCK)
            WHERE [DocumentNumber] LIKE @Prefix + '-' + CAST(@Year AS NVARCHAR(4)) + '-%';
            """;
        cmd.Parameters.AddWithValue("@Prefix", prefix);
        cmd.Parameters.AddWithValue("@Year", year);
        var seq = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        return $"{prefix}-{year}-{seq:D4}";
    }
}
