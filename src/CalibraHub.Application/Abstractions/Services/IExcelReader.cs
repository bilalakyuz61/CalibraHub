namespace CalibraHub.Application.Abstractions.Services;

/// <summary>Bir çalışma kitabındaki sayfa (xlsx) — CSV için tek sanal sayfa.</summary>
public sealed record ExcelSheet(string Name, int RowCount);

/// <summary>Seçili sayfanın başlık + veri satırları (hücreler formatlı string).</summary>
public sealed record ExcelTable(
    string SheetName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>İndirilebilir boş şablon (Excel) için tek bir kolon tanımı.
/// DataType ("string"/"decimal"/"int"/"date"/"bool") + MaxLength hücre veri doğrulaması üretir.</summary>
public sealed record ExcelTemplateColumn(string Header, string? Hint, bool Required, IReadOnlyList<string>? AllowedValues = null, bool CanBeMatchKey = false, string DataType = "string", int? MaxLength = null);

/// <summary>
/// Excel (.xlsx) ve CSV dosyalarını salt-okunur ayrıştırır. ClosedXML tabanlı (Infrastructure).
/// AI içermez; yapısal tablo okuma.
/// </summary>
public interface IExcelReader
{
    /// <summary>Dosyadaki sayfaları listele. CSV için tek "Sheet1".</summary>
    IReadOnlyList<ExcelSheet> ListSheets(byte[] data, string fileName);

    /// <summary>
    /// Seçili sayfayı oku. <paramref name="sheetName"/> null ise ilk sayfa.
    /// <paramref name="headerRowIndex"/> 1 tabanlı başlık satırı; veriler bir altından başlar.
    /// </summary>
    ExcelTable Read(byte[] data, string fileName, string? sheetName, int headerRowIndex);

    /// <summary>Verilen kolon başlıklarıyla boş bir .xlsx üretir (kullanıcı doldurup geri yükler).</summary>
    byte[] WriteTemplate(string sheetName, IReadOnlyList<ExcelTemplateColumn> columns);
}
