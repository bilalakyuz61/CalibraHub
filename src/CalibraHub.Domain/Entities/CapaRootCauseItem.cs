using System.ComponentModel;
using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>DÖF yapısal kök neden analiz satırı — 5 Neden / Ishikawa (6M) / 8D tekniklerinden
/// biriyle üretilen tekil bulgu satırı. CapaAction deseninin analoğu (Faz 2, 2026-08-03).</summary>
[Description("DÖF yapısal kök neden analiz satırı — 5 Neden / Ishikawa / 8D tekniği tekil bulgusu.")]
public sealed class CapaRootCauseItem
{
    public int Id { get; init; }

    [Description("Bağlı DÖF kaydı. FK -> Capa.Id.")]
    public int CapaId { get; init; }

    [Description("Analiz tekniği: 5 Neden / Ishikawa / 8D.")]
    public RootCauseMethod Method { get; set; } = RootCauseMethod.FiveWhy;

    [Description("Ishikawa 6M kategorisi. Yalnız Method=Ishikawa'da dolu, 5 Neden'de NULL.")]
    public IshikawaCategory? Category { get; set; }

    [Description("5 Neden'de seviye (1..n), Ishikawa'da kategori içi sıra.")]
    public int Sequence { get; set; }

    public string Text { get; set; } = string.Empty;

    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; set; }
    public DateTime? Updated { get; set; }
}
