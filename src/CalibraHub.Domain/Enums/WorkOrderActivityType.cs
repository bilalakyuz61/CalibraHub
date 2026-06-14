using System.ComponentModel;

namespace CalibraHub.Domain.Enums;

/// <summary>
/// Üretim sahası aktivite tipi (2026-05-20 — Faz 1 MVP).
///
/// Her WorkOrderOperation üzerinde yapılan eylemler ayrı satır olarak loglanır:
/// Setup/Production/MaterialWait/Breakdown/... Aynı anda tek bir aktif aktivite olur
/// (filtered unique index ile garanti). Yeni aktivite başlatılırken eski otomatik
/// kapatılır (EndedAt = SYSUTCDATETIME()).
///
/// Production aktivitesi miktar/fire ister (Quantity zorunlu); diğerleri yalnız süre.
/// Operasyon "Bitir/Kısmi Bitir" yalnız Production aktivitesi aktifken kullanılabilir.
///
/// İleride yeni tip eklenebilir — enum byte tabanlı (256'ya kadar). Frontend
/// menüsü bu enum'un Description metadata'sından beslenir (manual kayıt yok).
/// </summary>
public enum WorkOrderActivityType : byte
{
    /// <summary>Hazırlık — kalıp değişimi, takım/sarf hazırlığı, ayar.</summary>
    [Description("Hazırlık")]
    Setup = 0,

    /// <summary>Fiili üretim — miktar/fire kaydı yalnız bu tipte tutulur.</summary>
    [Description("Üretim")]
    Production = 1,

    /// <summary>Malzeme bekleme — hammadde/yarımamul gelmedi.</summary>
    [Description("Malzeme Bekleme")]
    MaterialWait = 2,

    /// <summary>Makine arızası — bakım/onarım bekliyor.</summary>
    [Description("Arıza")]
    MachineBreakdown = 3,

    /// <summary>Kalite kontrol — numune/test/onay süreci.</summary>
    [Description("Kalite Kontrol")]
    QualityCheck = 4,

    /// <summary>Mola — yemek/kahve/dinlenme.</summary>
    [Description("Mola")]
    Break = 5,

    /// <summary>Vardiya değişimi — personel devir teslim (Faz 3 entegrasyonu).</summary>
    [Description("Vardiya Değişimi")]
    ShiftChange = 6,

    /// <summary>Planlı durma — haftalık bakım, yağ değişimi, planlı temizlik.</summary>
    [Description("Planlı Durma")]
    PlannedDowntime = 7,

    /// <summary>Diğer — Notes alanı zorunlu (sebep ne ise yazılmalı).</summary>
    [Description("Diğer")]
    Other = 99,
}
