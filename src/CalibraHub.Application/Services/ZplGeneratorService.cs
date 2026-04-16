using System.Data;
using System.Text;

namespace CalibraHub.Application.Services;

public sealed class ZplGeneratorService
{
    /// <summary>
    /// DataTable satirlarindan ZPL etiket komutlari uretir.
    /// Beklenen kolonlar: ProductCode, ProductName, BarcodeValue
    /// </summary>
    public string Generate(DataTable data)
    {
        if (data.Rows.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        foreach (DataRow row in data.Rows)
        {
            var productCode = row.Table.Columns.Contains("ProductCode") ? row["ProductCode"]?.ToString() ?? "" : "";
            var productName = row.Table.Columns.Contains("ProductName") ? row["ProductName"]?.ToString() ?? "" : "";
            var barcodeValue = row.Table.Columns.Contains("BarcodeValue") ? row["BarcodeValue"]?.ToString() ?? "" : "";

            sb.AppendLine("^XA");
            sb.AppendLine($"^FO50,30^A0N,30,30^FD{Escape(productCode)}^FS");
            sb.AppendLine($"^FO50,70^A0N,25,25^FD{Escape(productName)}^FS");
            if (!string.IsNullOrEmpty(barcodeValue))
            {
                sb.AppendLine($"^FO50,110^BY2^BCN,80,Y,N,N^FD{Escape(barcodeValue)}^FS");
            }
            sb.AppendLine("^XZ");
        }

        return sb.ToString();
    }

    private static string Escape(string value) =>
        value.Replace("^", "").Replace("~", "");
}
