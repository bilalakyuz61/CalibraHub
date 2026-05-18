namespace CalibraHub.Web.Models.Shared;

/// <summary>
/// _ValidationField.cshtml partial'i icin view model (rapor §6.1 cozumu).
///
/// Form input + label + a11y attribute + hata mesaji'ni tek componente toplar.
/// </summary>
public sealed class ValidationFieldModel
{
    /// <summary>ModelState key + input name (zorunlu). Ornek: nameof(Input.Email) = "Email".</summary>
    public required string For { get; init; }

    /// <summary>Kullaniciya gosterilen label.</summary>
    public string? Label { get; init; }

    /// <summary>Input id (varsayilan: For ile ayni).</summary>
    public string? InputId { get; init; }

    /// <summary>Input type (text/email/password/number/tel/url/date). Varsayilan: text.</summary>
    public string InputType { get; init; } = "text";

    /// <summary>Mevcut deger (server-side render).</summary>
    public string? Value { get; init; }

    /// <summary>Placeholder metni (kisa, ornek deger).</summary>
    public string? Placeholder { get; init; }

    /// <summary>Help text — input altinda kucuk yardim metni.</summary>
    public string? HelpText { get; init; }

    /// <summary>Zorunlu mu? Required attribute + aria-required + label * isareti.</summary>
    public bool Required { get; init; }

    /// <summary>autocomplete hint (email/name/tel/new-password vb. — browser doldurmaya yardimci).</summary>
    public string? Autocomplete { get; init; }

    /// <summary>autofocus — sayfa acildiginda bu input'a focus ver.</summary>
    public bool Autofocus { get; init; }

    /// <summary>readonly mode.</summary>
    public bool Readonly { get; init; }

    /// <summary>Maksimum karakter sayisi.</summary>
    public int? Maxlength { get; init; }

    /// <summary>Single-line input yerine textarea kullan.</summary>
    public bool IsTextarea { get; init; }

    /// <summary>Textarea satir sayisi (sadece IsTextarea=true icin).</summary>
    public int? Rows { get; init; }
}
