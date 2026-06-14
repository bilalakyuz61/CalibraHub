using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Bakım/kalibrasyon/muayene sonucu. Bilgi amaçlı; raporlama ve rozet rengi için kullanılır.
/// </summary>
public enum AssetEventResult : byte
{
    [Description("—")]
    None = 0,

    [Description("Başarılı")]
    Passed = 1,

    [Description("Başarısız")]
    Failed = 2,

    [Description("Şartlı")]
    Conditional = 3,
}
