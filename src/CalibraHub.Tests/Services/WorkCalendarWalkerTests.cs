using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Calendar;
using Xunit;

namespace CalibraHub.Tests.Services;

/// <summary>
/// WorkCalendarWalker.WalkBackward — MRP'nin geriye tarihlemesi (2026-08-29).
/// Kapalı saatler ve tatiller SAYILMAZ; düz AddMinutes(-x) ile aradaki fark tam olarak
/// buradaki senaryolardır (hafta sonu / tatil / gün içi boşluk).
///
/// Gün konvansiyonu: Sunday=0 … Saturday=6 (CalibraHub kanonu).
/// Referans hafta: 2026-09-28 Pazartesi … 2026-10-02 Cuma.
/// </summary>
public sealed class WorkCalendarWalkerTests
{
    /// <summary>Pzt–Cum 08:00–17:00 (günde 540 dk). Hafta sonu kapalı.</summary>
    private static WorkCalendarWalker WeekdayNineToFive(params string[] holidays)
    {
        var windows = new List<MachineWorkWindowDto>();
        for (byte day = 1; day <= 5; day++)   // Pazartesi(1) … Cuma(5)
            windows.Add(new MachineWorkWindowDto(Id: 0, MachineId: 1, DayOfWeek: day,
                StartMinute: 8 * 60, EndMinute: 17 * 60));
        var hol = holidays.Select((d, i) => new HolidayDto(i + 1, d, "Tatil")).ToList();
        return new WorkCalendarWalker(windows, hol);
    }

    [Fact]
    public void GunIcinde_KalanSureKadarGeriGider()
    {
        var w = WeekdayNineToFive();
        // Salı 17:00'den 120 dk geri → aynı gün 15:00
        var start = w.WalkBackward(new DateTime(2026, 9, 29, 17, 0, 0), 120m);
        Assert.Equal(new DateTime(2026, 9, 29, 15, 0, 0), start);
    }

    [Fact]
    public void GunAsiminda_OncekiIsGunununSonunaSarkar()
    {
        var w = WeekdayNineToFive();
        // Salı 17:00'den 600 dk geri: Salı'da 540 dk var, kalan 60 dk Pazartesi'nin sonundan.
        var start = w.WalkBackward(new DateTime(2026, 9, 29, 17, 0, 0), 600m);
        Assert.Equal(new DateTime(2026, 9, 28, 16, 0, 0), start);
    }

    [Fact]
    public void HaftaSonu_SayilmazVeAtlanir()
    {
        var w = WeekdayNineToFive();
        // Pazartesi(2026-10-05) 17:00'den 600 dk geri: Pzt 540 + kalan 60 → Cuma(10-02) 16:00.
        // Cumartesi/Pazar hiç sayılmaz.
        var start = w.WalkBackward(new DateTime(2026, 10, 5, 17, 0, 0), 600m);
        Assert.Equal(new DateTime(2026, 10, 2, 16, 0, 0), start);
    }

    [Fact]
    public void Tatil_TamGunKapaliSayilir()
    {
        // 2026-09-29 Salı resmî tatil → Salı hiç sayılmaz.
        var w = WeekdayNineToFive("2026-09-29");
        // Çarşamba 17:00'den 600 dk geri: Çar 540 + kalan 60 → (Salı atlanır) Pazartesi 16:00.
        var start = w.WalkBackward(new DateTime(2026, 9, 30, 17, 0, 0), 600m);
        Assert.Equal(new DateTime(2026, 9, 28, 16, 0, 0), start);
    }

    [Fact]
    public void TamGunSuresi_GununBasinaOturur()
    {
        var w = WeekdayNineToFive();
        // Salı 17:00'den 540 dk (tam bir iş günü) geri → Salı 08:00
        var start = w.WalkBackward(new DateTime(2026, 9, 29, 17, 0, 0), 540m);
        Assert.Equal(new DateTime(2026, 9, 29, 8, 0, 0), start);
    }

    [Fact]
    public void MesaiDisiBitis_AcikSaatlerdenGeriSayar()
    {
        var w = WeekdayNineToFive();
        // Bitiş Salı 22:00 (mesai dışı): o günün açık kısmı 08:00–17:00'dir; 60 dk geri → 16:00.
        var start = w.WalkBackward(new DateTime(2026, 9, 29, 22, 0, 0), 60m);
        Assert.Equal(new DateTime(2026, 9, 29, 16, 0, 0), start);
    }

    [Fact]
    public void TakvimYok_BitisAynenDoner()
    {
        // Pencere tanımlı değilse uydurma tarih üretilmez — çağıran bunu kullanıcıya açıklar.
        var w = new WorkCalendarWalker([], []);
        Assert.False(w.HasCalendar);
        var end = new DateTime(2026, 9, 29, 17, 0, 0);
        Assert.Equal(end, w.WalkBackward(end, 600m));
    }

    [Fact]
    public void SifirSure_BitisAynenDoner()
    {
        var w = WeekdayNineToFive();
        var end = new DateTime(2026, 9, 29, 17, 0, 0);
        Assert.Equal(end, w.WalkBackward(end, 0m));
    }

    [Fact]
    public void CakisanPencereler_BirlestirilirCiftSayilmaz()
    {
        // İki makine aynı gün örtüşen pencerelerde: 08:00–17:00 ve 12:00–20:00.
        // Fabrika 08:00–20:00 (720 dk) çalışır; 9+8=17 saat DEĞİL.
        var windows = new List<MachineWorkWindowDto>
        {
            new(0, 1, 2, 8 * 60, 17 * 60),
            new(0, 2, 2, 12 * 60, 20 * 60),
        };
        var w = new WorkCalendarWalker(windows, []);
        // Salı 20:00'den 720 dk geri → tam gün başı 08:00 (çift sayılsaydı daha ileri çıkardı).
        var start = w.WalkBackward(new DateTime(2026, 9, 29, 20, 0, 0), 720m);
        Assert.Equal(new DateTime(2026, 9, 29, 8, 0, 0), start);
    }
}
