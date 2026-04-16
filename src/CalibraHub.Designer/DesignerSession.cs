namespace CalibraHub.Designer;

/// <summary>
/// .frx dosyasını temp klasörüne indirir, FastReport Designer ile açar.
/// Dosya değiştiğinde (kullanıcı kaydedince) CalibraHub API'ya otomatik gönderir.
/// </summary>
public sealed class DesignerSession : IDisposable
{
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "CalibraHub.Designer.log");
    private static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] [Session] {msg}\n"); } catch { }
    }

    private readonly string _templateId;
    private readonly string _calibraUrl;
    private readonly string _tempPath;
    private readonly CalibraHubClient _client;
    private FileSystemWatcher? _watcher;
    private DateTime _lastUpload = DateTime.MinValue;

    public DesignerSession(string templateId, string calibraUrl, TemplateData data)
    {
        _templateId = templateId;
        _calibraUrl = calibraUrl;
        _client     = new CalibraHubClient();

        // Temp dosyası
        var dir  = Path.Combine(Path.GetTempPath(), "CalibraHub.Designer");
        Directory.CreateDirectory(dir);
        var safe = string.Concat(data.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        _tempPath = Path.Combine(dir, $"{safe}_{templateId[..Math.Min(8, templateId.Length)]}.frx");

        // FRX içeriğini temp'e yaz
        var content = string.IsNullOrWhiteSpace(data.FrxContent)
            ? MinimalFrx(data.Name)
            : data.FrxContent;
        File.WriteAllText(_tempPath, content);
        Log($"Temp dosya yazildi: {_tempPath}");
    }

    public void Open()
    {
        // FileSystemWatcher kur
        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_tempPath)!, Path.GetFileName(_tempPath))
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;

        Log("FastReport Designer aciliyor...");

        // FastReport Designer Community Edition ile ac
        var designerExe = FindDesignerExe();
        if (designerExe is null)
        {
            Log("Designer.exe bulunamadi! Notepad ile aciliyor...");
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName  = "notepad.exe",
                    Arguments = $"\"{_tempPath}\"",
                });
            }
            catch (Exception ex)
            {
                Log($"Notepad HATA: {ex.Message}");
            }
            return;
        }

        // Turkce dil dosyasini kontrol et ve yoksa indir
        EnsureTurkishLanguage(Path.GetDirectoryName(designerExe)!);

        // Designer config'ine Turkce dili ayarla
        EnsureDesignerConfig(Path.GetDirectoryName(designerExe)!);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName  = designerExe,
                Arguments = $"\"{_tempPath}\"",
            });
            Log($"Designer acildi: {designerExe}");
        }
        catch (Exception ex)
        {
            Log($"Designer HATA: {ex.Message}");
            MessageBox.Show(
                $"Designer açılamadı:\n{ex.Message}\n\nDosya konumu:\n{_tempPath}",
                "CalibraHub Designer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OpenWithShellExecute()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = _tempPath,
                UseShellExecute = true
            });
            Log("Shell execute ile acildi.");
        }
        catch (Exception ex)
        {
            Log($"Shell execute HATA: {ex.Message}");
            MessageBox.Show(
                $"FastReport Designer açılamadı:\n{ex.Message}\n\n" +
                $"Dosya şu konumda:\n{_tempPath}",
                "CalibraHub Designer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if ((DateTime.Now - _lastUpload).TotalSeconds < 2) return;
        _lastUpload = DateTime.Now;
        await Task.Delay(500);
        await UploadAsync();
    }

    private async Task UploadAsync()
    {
        try
        {
            var frx = await File.ReadAllTextAsync(_tempPath);
            await _client.SaveTemplateAsync(_calibraUrl, _templateId, frx);
            Log("Upload basarili.");
        }
        catch (Exception ex)
        {
            Log($"Upload HATA: {ex.Message}");
        }
    }

    public string? ReadCurrentFrx()
    {
        try { return File.ReadAllText(_tempPath); }
        catch { return null; }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
    }

    /// <summary>Turkce dil dosyasi yoksa GitHub'dan indirir.</summary>
    private static void EnsureTurkishLanguage(string designerDir)
    {
        var frlPath = Path.Combine(designerDir, "Turkish.frl");
        if (File.Exists(frlPath)) return;

        Log("Turkce dil dosyasi indiriliyor...");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var content = http.GetStringAsync(
                "https://raw.githubusercontent.com/FastReports/FastReport/master/Localization/Turkish.frl"
            ).GetAwaiter().GetResult();
            File.WriteAllText(frlPath, content);
            Log("Turkish.frl indirildi.");
        }
        catch (Exception ex)
        {
            Log($"Turkce dil dosyasi indirilemedi: {ex.Message}");
        }
    }

    /// <summary>Designer config dosyasina Turkce dili ayarlar.</summary>
    private static void EnsureDesignerConfig(string designerDir)
    {
        var configPath = Path.Combine(designerDir, "Designer.exe.config");
        if (!File.Exists(configPath)) return;

        try
        {
            var content = File.ReadAllText(configPath);
            // Eger zaten Language ayari varsa dokunma
            if (content.Contains("Language") && content.Contains("Turkish")) return;

            // FastReport Designer config'i XML — userSettings icine language eklemek yerine
            // Designer'in kendi ayar dosyasini kontrol edelim
            var settingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FastReport");
            Directory.CreateDirectory(settingsDir);

            var settingsFile = Path.Combine(settingsDir, "Designer.settings");
            if (!File.Exists(settingsFile))
            {
                File.WriteAllText(settingsFile, $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <Settings>
                      <Language>Turkish</Language>
                      <LanguageFolder>{designerDir}</LanguageFolder>
                    </Settings>
                    """);
                Log("Designer.settings olusturuldu (Turkish).");
            }
        }
        catch (Exception ex)
        {
            Log($"Designer config hatasi: {ex.Message}");
        }
    }

    /// <summary>Designer.exe'yi bilinen konumlarda arar.</summary>
    private static string? FindDesignerExe()
    {
        // 1. Proje icerisindeki tools klasoru
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "FastReportDesigner", "Designer.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "FastReportDesigner", "Designer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FastReport", "Designer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FastReport", "Designer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastReport", "Designer.exe"),
        };

        foreach (var path in candidates)
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    private static string MinimalFrx(string reportName) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Report ScriptLanguage="CSharp" ReportInfo.Name="{reportName}" ReportInfo.Created="{DateTime.Now:MM/dd/yyyy HH:mm:ss}" ReportInfo.Modified="{DateTime.Now:MM/dd/yyyy HH:mm:ss}" ReportInfo.CreatorVersion="2026.1.8">
          <Dictionary/>
          <ReportPage Name="Page1" Landscape="false" PaperWidth="210" PaperHeight="297" MarginLeft="10" MarginRight="10" MarginTop="10" MarginBottom="10">
            <ReportTitleBand Name="ReportTitle1" Top="0" Width="718.2" Height="37.8">
              <TextObject Name="Text1" Left="0" Top="0" Width="718.2" Height="37.8" Text="{reportName}" Font="Arial, 18pt, style=Bold"/>
            </ReportTitleBand>
            <DataBand Name="Data1" Top="41.8" Width="718.2" Height="30">
              <TextObject Name="Text2" Left="0" Top="0" Width="718.2" Height="30" Text="[Data.ProductCode]" Font="Arial, 10pt"/>
            </DataBand>
            <PageFooterBand Name="PageFooter1" Top="75.8" Width="718.2" Height="18.9">
              <TextObject Name="Text3" Left="0" Top="0" Width="718.2" Height="18.9" Text="Sayfa [PageN] / [TotalPages]" HorzAlign="Right" Font="Arial, 8pt"/>
            </PageFooterBand>
          </ReportPage>
        </Report>
        """;
}
