namespace CalibraHub.Domain.Enums;

/// <summary>
/// RptViewCol.ContextBinding — VIEW kolonunun oturum baglamina nasil bagli oldugunu isaretler.
/// ReportEngineService context enjeksiyonu icin bu bayragi kullanarak WHERE filtrelerini uretir.
/// </summary>
public enum ReportContextBinding
{
    None = 0,
    CompanyId = 1,
    UserId = 2,
    OwnerUserId = 3
}
