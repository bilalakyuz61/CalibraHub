namespace CalibraHub.Application.Contracts;

/// <summary>
/// Faz D Adim 1 — Legacy dinamik alan verilerinin yeni EAV tablolarina
/// (WidgetMas + WidgetTra) tasinmasi sonrasi olusturulan rapor.
///
/// Migration idempotent: tekrar calistirilirsa "mevcut" kayitlar skip edilir.
/// Kullanicinin elimizdeki verisi asla silinmez/overwrite edilmez.
/// </summary>
public sealed class LegacyMigrationReport
{
    public int GroupsMigrated { get; set; }
    public int GroupsSkipped  { get; set; }
    public int FieldsMigrated { get; set; }
    public int FieldsSkipped  { get; set; }
    public int ValuesMigrated { get; set; }
    public int ValuesSkipped  { get; set; }
    public List<string> Warnings { get; } = new();

    public int Total =>
        GroupsMigrated + GroupsSkipped +
        FieldsMigrated + FieldsSkipped +
        ValuesMigrated + ValuesSkipped;
}
