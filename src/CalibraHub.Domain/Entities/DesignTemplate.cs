using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

public sealed class DesignTemplate : Entity
{
    public required string Name { get; set; }

    /// <summary>"document" | "email" | "report" | "dashboard"</summary>
    public required string Type { get; set; }

    /// <summary>
    /// Belge türü için alt tür.
    /// Örnek: "sales_order" | "purchase_order" | "sales_request" | "sales_offer" | "invoice" | "delivery_note"
    /// </summary>
    public string? SubType { get; set; }

    public string? Description { get; set; }
    public string? HtmlContent { get; set; }
    public string? CssContent { get; set; }

    /// <summary>GrapesJS JSON (components + styles)</summary>
    public string? GjsData { get; set; }

    /// <summary>jsreport Handlebars HTML şablonu — yalnızca Type == "report" şablonlarında kullanılır.</summary>
    public string? JsrContent { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
