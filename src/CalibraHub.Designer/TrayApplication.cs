namespace CalibraHub.Designer;

/// <summary>Sistem tepsisi uygulaması — arka planda çalışır.</summary>
public sealed class TrayApplication : ApplicationContext
{
    private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "CalibraHub.Designer.log");
    private static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
    }

    private readonly NotifyIcon _tray;
    private readonly LocalApiServer _server;
    private readonly CalibraHubClient _client = new();
    private readonly List<DesignerSession> _sessions = new();

    public TrayApplication(LocalApiServer server)
    {
        _server = server;
        _server.OnOpenTemplate += OnOpenTemplate;

        _tray = new NotifyIcon
        {
            Icon    = SystemIcons.Application,
            Text    = "CalibraHub Designer — Hazır",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _tray.DoubleClick += (_, _) =>
            MessageBox.Show(
                $"CalibraHub Designer çalışıyor.\nPort: {LocalApiServer.Port}",
                "CalibraHub Designer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

        Log("TrayApplication baslatildi.");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("CalibraHub Designer").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Durum", null, (_, _) =>
            MessageBox.Show($"Hazır — port {LocalApiServer.Port}", "CalibraHub Designer",
                MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => ExitApp());
        return menu;
    }

    private void OnOpenTemplate(string templateId, string calibraUrl)
    {
        Log($"OnOpenTemplate tetiklendi: id={templateId}, url={calibraUrl}");
        // ThreadPool'dan direkt async calistir — BeginInvoke'a gerek yok
        _ = Task.Run(() => OpenTemplateAsync(templateId, calibraUrl));
    }

    private async Task OpenTemplateAsync(string templateId, string calibraUrl)
    {
        Log($"OpenTemplateAsync basladi: id={templateId}");
        try
        {
            Log("GetTemplateAsync cagiriliyor...");
            var data = await _client.GetTemplateAsync(calibraUrl, templateId);
            Log($"GetTemplateAsync sonuc: {(data is null ? "NULL" : $"name={data.Name}, frx={data.FrxContent?.Length ?? 0} chars")}");

            if (data is null)
            {
                Log("Sablon bulunamadi!");
                return;
            }

            var session = new DesignerSession(templateId, calibraUrl, data);
            _sessions.Add(session);
            Log("session.Open() cagiriliyor...");
            session.Open();
            Log("session.Open() tamamlandi.");
        }
        catch (Exception ex)
        {
            Log($"HATA: {ex}");
        }
    }

    private void ExitApp()
    {
        _server.Stop();
        foreach (var s in _sessions) s.Dispose();
        _sessions.Clear();
        _tray.Visible = false;
        Application.Exit();
    }
}
