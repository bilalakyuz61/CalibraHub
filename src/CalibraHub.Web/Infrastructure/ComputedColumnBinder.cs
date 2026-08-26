using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Web.Infrastructure;

/// <summary>
/// Hesaplanan kolonları bir SmartBoard'a bağlayan ortak yardımcı.
///
/// Neden ayrı sınıf: bağlama işi board başına tekrar eder (tanımları oku → sayfadaki
/// anahtarlar için değerleri oku → master widget + kart hücresi üret). Her controller'a
/// kopyalansaydı biçimlendirme ve hata davranışı zamanla ayrışırdı — bu projede
/// kopyalanan ~40 satırlık blokların gerçek bug ürettiği kayıtlı.
/// </summary>
public sealed class ComputedColumnBinder
{
    public const string GroupKey = "hesaplanan";
    public const string GroupLabel = "Hesaplanan";

    private readonly IComputedColumnRepository _repo;
    private readonly ILogger _logger;

    public ComputedColumnBinder(IComputedColumnRepository repo, ILogger logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>Board için tanımlar + sayfadaki anahtarların değerleri.</summary>
    public async Task<ComputedColumnBinding> LoadAsync(
        string entityKind, string boardKey, IReadOnlyCollection<int> keys, CancellationToken ct)
    {
        // Tanımları OKUMAK da patlayabilir (tablo henüz yok, izin yok, bağlantı düştü).
        // Bu hiçbir zaman liste ekranını düşürmemeli: hesaplanan kolon bir EKLENTİDİR,
        // listenin çekirdek verisi değil. Hata loglanır, board kolonsuz devam eder.
        IReadOnlyList<ComputedColumnDto> columns;
        try
        {
            columns = await _repo.GetForBoardAsync(entityKind, boardKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HesaplananKolon] Tanımlar okunamadı (entity={Entity}, board={Board}) — " +
                                 "liste hesaplanan kolon olmadan devam ediyor.", entityKind, boardKey);
            return new ComputedColumnBinding(Array.Empty<ComputedColumnDto>(),
                new Dictionary<int, IReadOnlyDictionary<int, ComputedCellValue>>());
        }
        var values = new Dictionary<int, IReadOnlyDictionary<int, ComputedCellValue>>();
        foreach (var c in columns)
        {
            var (map, error) = await _repo.ReadValuesAsync(c, keys, ct);
            if (error is not null)
            {
                // Bozuk/yavaş tanım YALNIZ kendi kolonunu düşürür; liste çalışmaya devam eder.
                _logger.LogError("[HesaplananKolon] '{Label}' okunamadı (view={View}): {Error}",
                    c.Label, c.ViewName, error);
            }
            values[c.Id] = map;
        }
        return new ComputedColumnBinding(columns, values);
    }
}

/// <summary>Bir board için yüklenmiş tanımlar ve değerler.</summary>
public sealed class ComputedColumnBinding
{
    public IReadOnlyList<ComputedColumnDto> Columns { get; }
    private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, ComputedCellValue>> _values;

    public ComputedColumnBinding(
        IReadOnlyList<ComputedColumnDto> columns,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, ComputedCellValue>> values)
    {
        Columns = columns;
        _values = values;
    }

    public bool Any => Columns.Count > 0;

    /// <summary>Master widget şablonları (filtre/sütun panelinin okuduğu liste).</summary>
    public IEnumerable<object> MasterWidgets() => Columns.Select(c => (object)new Dictionary<string, object?>
    {
        ["id"] = $"w_calc_{c.Id}",
        ["dbId"] = (int?)null,
        ["isPlainField"] = false,
        ["type"] = "data",
        ["dataType"] = MapDataType(c.DataType),
        ["label"] = c.Label,
        ["group"] = ComputedColumnBinder.GroupKey,
        ["groupLabel"] = ComputedColumnBinder.GroupLabel,
        ["source"] = "computed",
    });

    /// <summary>Tek kaydın hesaplanan hücreleri.</summary>
    public IEnumerable<object> CellsFor(int key) => Columns.Select(c =>
    {
        ComputedCellValue? cell = null;
        if (_values.TryGetValue(c.Id, out var byKey) && byKey.TryGetValue(key, out var found)) cell = found;
        return (object)new Dictionary<string, object?>
        {
            ["id"] = $"w_calc_{c.Id}",
            ["type"] = "data",
            ["dataType"] = MapDataType(c.DataType),
            ["label"] = c.Label,
            ["value"] = Format(c, cell),
            ["detail"] = cell?.Unit,
            ["source"] = "computed",
        };
    });

    /// <summary>
    /// Sıralama HAM değer üzerinden yapıldığı için tip doğru olmalı: süre "2 sa 15 dk"
    /// diye basılsa bile numeric kalır, yoksa metin sıralaması "10 dk" ile "9 sa"yı
    /// yanlış dizer.
    /// </summary>
    public static string MapDataType(string? dataType) => (dataType ?? "number").ToLowerInvariant() switch
    {
        "date" => "date",
        "text" => "text",
        "bool" => "boolean",
        _ => "numeric",
    };

    /// <summary>
    /// Ham değeri gösterilecek metne çevirir. Değer yoksa tanımdaki politika uygulanır:
    /// sayıda genelde 0, tarihte tire anlamlıdır — bu yüzden tipe göre sabit değil,
    /// tanımın parçası.
    /// </summary>
    public static string Format(ComputedColumnDto column, ComputedCellValue? cell)
    {
        var raw = cell?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (column.NullDisplay ?? "dash").ToLowerInvariant() switch
            {
                "zero" => "0",
                "empty" => string.Empty,
                _ => "—",
            };
        }

        var tr = new System.Globalization.CultureInfo("tr-TR");
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var type = (column.DataType ?? "number").ToLowerInvariant();

        if (type is "number" or "decimal" or "money"
            && decimal.TryParse(raw, System.Globalization.NumberStyles.Any, inv, out var dec))
            return dec.ToString("N2", tr);

        if (type == "date"
            && DateTime.TryParse(raw, inv, System.Globalization.DateTimeStyles.None, out var dt))
            return dt.ToString("dd.MM.yyyy");

        if (type == "duration"
            && decimal.TryParse(raw, System.Globalization.NumberStyles.Any, inv, out var secs))
        {
            var total = (int)secs;
            var h = total / 3600;
            var mn = (total % 3600) / 60;
            return h > 0 ? $"{h} sa {mn} dk" : $"{mn} dk";
        }

        return raw;
    }
}
