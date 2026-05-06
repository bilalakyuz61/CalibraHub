namespace CalibraHub.Domain.Enums;

/// <summary>
/// CompanyParameter.DataType — sirket parametre degerinin yorumlanma turu.
/// ParamValue NVARCHAR(400) olarak saklanir, okuma/yazma sirasinda bu enum'a gore parse edilir.
/// </summary>
public enum CompanyParameterDataType : byte
{
    String = 1,
    Int = 2,
    Bool = 3,
    Date = 4,
    LookupId = 5,
}
