using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Bakım/kalibrasyon periyodu birimi. Periyot değeri (MaintenancePeriodDays/CalibrationPeriodDays —
/// adı geçmişten "Days" ama artık seçilen birimdeki <b>değer</b>i tutar) bu birimle yorumlanır:
/// Days → AddDays, Months → AddMonths, Years → AddYears (takvim doğru hesap).
/// </summary>
public enum AssetPeriodUnit : byte
{
    [Description("Gün")]
    Days = 0,

    [Description("Ay")]
    Months = 1,

    [Description("Yıl")]
    Years = 2,
}
