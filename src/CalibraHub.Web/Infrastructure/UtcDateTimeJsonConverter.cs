using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalibraHub.Web.Infrastructure;

/// <summary>
/// SQL Server DATETIME kolonları ADO.NET'e Kind=Unspecified olarak gelir.
/// System.Text.Json bu değerleri 'Z' suffix'i olmadan serialize eder, JavaScript
/// ise bu stringleri local time olarak parse eder — UTC+3'te 3 saat kayma oluşur.
/// Bu converter tüm DateTime değerlerini UTC olarak işaretleyerek 'Z' suffix'i ile
/// serialize eder; frontend yeni Date("...Z") ile doğru yerel saati gösterir.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.SpecifyKind(reader.GetDateTime(), DateTimeKind.Utc);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
