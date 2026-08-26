namespace CalibraHub.Application.Contracts;

/// <summary>
/// Hesaplanan Kolon tanımı — bir liste ekranına, SQL VIEW'dan beslenen salt-okunur kolon.
///
/// Sözleşmenin özü: bu kayıt SQL TAŞIMAZ, yalnız tanımlayıcı taşır. Sorgu view'ın içindedir
/// (ViewBuilder'ın doğrulamalarından geçer); buradaki alanlar <c>sys</c> kataloğuna karşı
/// denetlenebilen adlardır. Serbest SQL fragmanı saklamak depolanmış injection demekti.
/// </summary>
/// <param name="EntityKind">Item | Contact | Document — satırları bu varlık olan listelerde çıkar.</param>
/// <param name="UnitColumn">
/// Satır bazında birim/para birimi taşıyan kolon. Birim malzemeden malzemeye değiştiği için
/// tanım zamanı sabiti OLAMAZ; boş bırakılırsa değer birimsiz basılır.
/// </param>
/// <param name="BoardKeys">Boş = varlığın tüm listeleri; dolu = virgülle ayrılmış boardKey listesi.</param>
/// <param name="NullDisplay">empty | dash | zero — view'da o kayıt için satır yoksa ne görünecek.</param>
public sealed record ComputedColumnDto(
    int Id,
    string Label,
    string EntityKind,
    string ViewName,
    string KeyColumn,
    string ValueColumn,
    string? UnitColumn,
    string DataType,
    string? FormatJson,
    string NullDisplay,
    string? BoardKeys,
    int TimeoutSec,
    int SortOrder,
    bool IsActive);

public sealed record SaveComputedColumnRequest(
    int Id,
    string Label,
    string EntityKind,
    string ViewName,
    string KeyColumn,
    string ValueColumn,
    string? UnitColumn,
    string DataType,
    string? FormatJson,
    string NullDisplay,
    string? BoardKeys,
    int TimeoutSec,
    int SortOrder,
    bool IsActive);

/// <summary>Tanım ekranındaki kaynak seçicisi: kullanılabilir view'lar ve kolonları.</summary>
public sealed record ComputedColumnSourceDto(
    string ViewName,
    IReadOnlyList<ComputedColumnSourceFieldDto> Columns);

/// <param name="SqlType">Ham SQL tipi — arayüz veri tipini bundan ÖN-SEÇER (int → number, datetime → date).</param>
public sealed record ComputedColumnSourceFieldDto(string Name, string SqlType);

/// <summary>
/// Kaydetmeden önce çalıştırılan deneme okuması. Süre ölçülür: kötü yazılmış bir view'ın
/// liste ekranını kilitlemesi ancak burada fark edilebilir.
/// </summary>
public sealed record ComputedColumnPreviewDto(
    bool Ok,
    string? Error,
    int ElapsedMs,
    IReadOnlyList<ComputedColumnPreviewRowDto> Rows);

public sealed record ComputedColumnPreviewRowDto(string Key, string? Value, string? Unit);
