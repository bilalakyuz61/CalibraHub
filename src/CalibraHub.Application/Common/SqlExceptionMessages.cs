using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Common;

/// <summary>
/// SQL Server yabancı anahtar (FK) kısıt ihlali (SqlException.Number == 547) için
/// merkezi tespit + kullanıcı dostu mesaj üretimi.
///
/// CLAUDE.md "sessiz kırık" kuralı: silme/güncelleme akışlarında bu hata işlenmemiş
/// 500 olarak dışarı sızmamalı; sunucuya gerçek kısıt adıyla loglanmalı, istemciye
/// anlaşılır ama SQL detayı sızdırmayan bir mesaj dönmeli. Bu sınıf TEK kaynak —
/// controller'lar mantığı kopyalamaz, <see cref="TryHandle"/> ile tek satırda kullanır.
/// Ayrıca <see cref="CalibraHub.Web.Middleware.ApiExceptionMiddleware"/> genel ağ
/// olarak aynı mesaj üretimini kullanır (kontrolörün hiç yakalamadığı durumlar için).
/// </summary>
public static class SqlExceptionMessages
{
    public const int ForeignKeyViolationErrorNumber = 547;

    /// <summary>ex bir FK/REFERENCE kısıt ihlali mi? (SqlException.Number == 547)</summary>
    public static bool IsForeignKeyViolation(this Exception? ex) =>
        ex is SqlException { Number: ForeignKeyViolationErrorNumber };

    // Çocuk tablo adı → kullanıcıya gösterilecek Türkçe bağlam ifadesi. Bulunamayan
    // tablo için jenerik mesaja düşülür (bkz. BuildUserMessage) — bu sözlük yalnız
    // zenginleştirme amaçlıdır, eksik girdi hata değildir.
    private static readonly IReadOnlyDictionary<string, string> TableContext =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Items"] = "malzeme kartlarında",
            ["ItemUnits"] = "malzeme birim çarpanlarında",
            ["ItemKitLine"] = "kit (paket ürün) kalemlerinde",
            ["ItemLocation"] = "malzeme-lokasyon eşlemelerinde",
            ["ItemFeatureMappings"] = "malzeme özellik eşlemelerinde",
            ["OpMachineTime"] = "makine/ürün sürelerinde",
            ["OperationMachineTime"] = "makine/ürün sürelerinde",
            ["RoutingOperation"] = "rota operasyonlarında",
            ["WorkOrderOperation"] = "iş emri operasyonlarında",
            ["WorkOrder"] = "iş emirlerinde",
            ["Document"] = "belgelerde",
            ["DocumentLine"] = "belge kalemlerinde",
            ["InventoryCount"] = "sayım fişlerinde",
            ["InventoryCountLine"] = "sayım kalemlerinde",
            ["Personnel"] = "personel kayıtlarında",
            ["Machine"] = "makinelerde",
            ["Asset"] = "varlık kayıtlarında",
            ["AssetAssignment"] = "varlık atamalarında",
            ["OrgChartNode"] = "organizasyon şemasında",
            ["PriceList"] = "fiyat listelerinde",
            ["Contact"] = "cari kartlarında",
            ["FldSet"] = "alan ayarlarında",
        };

    private static readonly Regex ChildTablePattern =
        new("table \"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// FK ihlalinden kullanıcıya gösterilecek Türkçe mesajı üretir. SQL Server'ın
    /// (locale'e bağlı olabilen) mesaj metninden çocuk tablo adını çıkarabilirse
    /// bağlam ekler; çıkaramazsa jenerik ama anlaşılır mesaja düşer. SQL metni /
    /// kısıt adı asla döndürülen metne dahil edilmez.
    /// </summary>
    public static string BuildUserMessage(SqlException ex)
    {
        var table = ExtractChildTable(ex.Message);
        var context = table is not null && TableContext.TryGetValue(table, out var label)
            ? $" ({label} kullanılıyor)"
            : string.Empty;
        return $"Bu kayıt başka kayıtlarda kullanıldığı için silinemez/güncellenemez{context}. Önce bağlı kayıtları güncelleyin.";
    }

    private static string? ExtractChildTable(string sqlMessage)
    {
        var match = ChildTablePattern.Match(sqlMessage ?? string.Empty);
        if (!match.Success) return null;
        var raw = match.Groups[1].Value; // "dbo.TableName"
        var dot = raw.LastIndexOf('.');
        return dot >= 0 ? raw[(dot + 1)..] : raw;
    }

    /// <summary>
    /// Controller catch bloklarında tek satırlık kullanım: <paramref name="ex"/> bir FK
    /// ihlali ise gerçek kısıt detayını (orijinal SqlException) uyarı seviyesinde loglar
    /// ve kullanıcı mesajını <paramref name="userMessage"/>'a yazar, true döner. Değilse
    /// false döner — çağıran taraf kendi genel hata yoluna devam eder.
    /// </summary>
    public static bool TryHandle(Exception ex, ILogger logger, string context, out string userMessage)
    {
        if (ex is SqlException { Number: ForeignKeyViolationErrorNumber } sqlEx)
        {
            logger.LogWarning(sqlEx, "[FK Violation] {Context}: {SqlMessage}", context, sqlEx.Message);
            userMessage = BuildUserMessage(sqlEx);
            return true;
        }
        userMessage = string.Empty;
        return false;
    }
}
