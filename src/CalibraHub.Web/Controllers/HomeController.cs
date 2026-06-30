using System.Diagnostics;
using CalibraHub.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly IWebHostEnvironment _env;

    public HomeController(IWebHostEnvironment env)
    {
        _env = env;
    }

    public IActionResult Index()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        // UseExceptionHandler tarafindan set edilen exception feature'larini oku
        var exFeature  = HttpContext.Features.Get<IExceptionHandlerFeature>();
        var pathFeature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        var ex = exFeature?.Error;

        var model = new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            Path = pathFeature?.Path,
            IsDevelopment = _env.IsDevelopment(),
        };

        if (ex != null)
        {
            model.ExceptionType = ex.GetType().Name;
            model.Message = _env.IsDevelopment() ? ex.Message : null;
            model.StackTrace = ex.ToString();
            model.Hint = GetHint(ex);
        }

        // 500 status set et (middleware zaten set etmis olsa da garanti)
        Response.StatusCode = 500;
        return View(model);
    }

    /// <summary>Bilinen hata tipleri icin kullanici-dostu Turkce ipucu.</summary>
    private static string? GetHint(Exception ex)
    {
        var name = ex.GetType().Name;
        var msg = ex.Message ?? string.Empty;
        if (name == "InvalidOperationException" && msg.Contains("view", StringComparison.OrdinalIgnoreCase) && msg.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return "Bir Razor view dosyasi bulunamadi. Controller action'i ile view dosya ismi eslesmiyor olabilir.";
        if (name == "SqlException")
            return "Veri tabani sorgusu basarisiz. Baglanti/tablo/kolon durumunu kontrol edin.";
        if (name == "NullReferenceException")
            return "Eksik veri veya baslatilmamis bir nesne referansi. Hata genellikle bir alanin beklenmedik sekilde bos olmasindan kaynaklanir.";
        if (name == "UnauthorizedAccessException")
            return "Yetkisiz erisim. Oturumunuz sona ermis olabilir veya bu kaynaga erisim izniniz yok.";
        if (name == "HttpRequestException")
            return "Dis bir servise yapilan HTTP istegi basarisiz. Ag baglantisi veya hedef URL kontrol edilmeli.";
        return null;
    }
}
