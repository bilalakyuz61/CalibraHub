namespace CalibraHub.Domain.Enums;

/// <summary>
/// SQL View Yönetimi — bir ViewDefinition kaydının türü.
/// SystemExtend: CalibraDatabaseInitializer'da kod-tanımlı, her açılışta ensure edilen mevcut bir
///   view'ı (join/hesaplanan alan ile) genişletir. Non-breaking guard bu türde zorunludur.
/// Custom: sıfırdan, tamamen kullanıcı tanımlı yeni bir view.
/// </summary>
public enum ViewDefinitionKind
{
    SystemExtend = 0,
    Custom = 1,
}
