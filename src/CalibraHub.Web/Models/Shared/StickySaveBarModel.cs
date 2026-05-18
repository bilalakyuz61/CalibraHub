namespace CalibraHub.Web.Models.Shared;

/// <summary>
/// _StickySaveBar.cshtml partial'i icin view model (rapor §6.2 cozumu).
///
/// Form altinda yapisik kalan Kaydet/Iptal cubugu. Uzun edit form'larda
/// (DocumentEdit 4872 satir, MaterialCardEdit 4800 satir) kullanici Save
/// butonunu kaybetmesin diye.
/// </summary>
public sealed class StickySaveBarModel
{
    /// <summary>Ana kaydet butonu yazisi (varsayilan: "Kaydet").</summary>
    public string SaveLabel { get; init; } = "Kaydet";

    /// <summary>Iptal butonu yazisi (varsayilan: "İptal").</summary>
    public string CancelLabel { get; init; } = "İptal";

    /// <summary>Iptal butonu URL'i. Bossa data-action="cancel" attribute'lu buton uretilir.</summary>
    public string? CancelUrl { get; init; }

    /// <summary>"Kaydet ve Yeni" butonu gosterilsin mi?</summary>
    public bool ShowSaveAndNew { get; init; }

    /// <summary>"Kaydet ve Yeni" butonu yazisi.</summary>
    public string SaveAndNewLabel { get; init; } = "Kaydet ve Yeni";

    /// <summary>Submit edilecek form ID — partial form disinda render edilirse zorunlu.</summary>
    public string? FormId { get; init; }

    /// <summary>Submit butonunun name attribute'u (varsayilan: "submitAction"). Backend bu deger ile "save" vs "save-and-new" ayrimi yapar.</summary>
    public string SubmitButtonName { get; init; } = "submitAction";

    /// <summary>Ctrl+S klavye kisayolu ile Kaydet'i tetikle.</summary>
    public bool EnableCtrlS { get; init; } = true;
}
