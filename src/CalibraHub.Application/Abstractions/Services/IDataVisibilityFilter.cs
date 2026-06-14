namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// 2026-06-12 — Satır görünürlük filtresi. Bir entity okuma sorgusuna, mevcut kullanıcıya göre
/// eklenecek WHERE parçasını + parametrelerini üretir (kısıtlama modeli).
///
/// **Kullanım:** Repository, kendi WHERE'ine üretilen <see cref="DataVisibilityPredicate.Sql"/>'i
/// ekler ve <see cref="DataVisibilityPredicate.Parameters"/>'i komuta bağlar:
/// <code>
///   var dv = await _dvFilter.BuildAsync("CONTACTS", "q", "id", ct);
///   where += dv.Sql;
///   foreach (var p in dv.Parameters) cmd.Parameters.AddWithValue(p.Name, p.Value);
/// </code>
///
/// **Sıfır maliyet hızlı yolu:** Forma kural yoksa veya kullanıcı SystemAdmin ise
/// <see cref="DataVisibilityPredicate.Empty"/> döner — sorgu bugünküyle aynı kalır.
/// </summary>
public interface IDataVisibilityFilter
{
    /// <summary>
    /// Verilen FormCode (entity) için mevcut kullanıcıya göre WHERE parçası üretir.
    /// </summary>
    /// <param name="formCode">Entity anchor — PermissionDef.FormCode uzayı (örn. 'CONTACTS').</param>
    /// <param name="tableAlias">Ana entity tablosunun sorgudaki alias'ı (örn. 'q'). Kolon predikatları bununla nitelenir.</param>
    /// <param name="idColumn">Entity PK kolon adı (örn. 'id'). Widget kuralları için yasaklı id filtresi bununla kurulur.</param>
    Task<DataVisibilityPredicate> BuildAsync(string formCode, string tableAlias, string idColumn, CancellationToken ct);

    /// <summary>Admin bir formun kurallarını değiştirince ilgili cache satırını temizler.</summary>
    void InvalidateCache(string formCode);
}

/// <summary>WHERE parçası + ona ait parametreler. Boşsa filtre uygulanmaz.</summary>
public sealed record DataVisibilityPredicate(string Sql, IReadOnlyList<DataVisibilityParam> Parameters)
{
    public static readonly DataVisibilityPredicate Empty = new(string.Empty, System.Array.Empty<DataVisibilityParam>());
    public bool IsEmpty => string.IsNullOrEmpty(Sql);
}

/// <summary>Parametre adı + değeri (caller AddWithValue ile bağlar).</summary>
public sealed record DataVisibilityParam(string Name, object Value);
