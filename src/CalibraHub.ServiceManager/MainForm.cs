using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Windows.Forms;

namespace CalibraHub.ServiceManager;

/// <summary>
/// CalibraHub Servis Yoneticisi — Web/Worker/Grafana Windows servislerini
/// tek pencereden Start/Stop/Restart yapar. UAC ile elevated calisir.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly ServiceDef[] ManagedServices =
    {
        new("CalibraHub Web",          "ASP.NET Core web sunucusu (port 61001)"),
        new("CalibraHub Worker",       "Arka plan gorev calistiricisi"),
        new("CalibraHub Grafana",      "Dashboard motoru (port 61005)"),
        new("CalibraHubWhatsAppBridge","WhatsApp Bridge (Node.js sidecar, port 61100)"),
    };

    private readonly DataGridView _grid = new();
    private readonly Button _btnStart   = new();
    private readonly Button _btnStop    = new();
    private readonly Button _btnRestart = new();
    private readonly Button _btnRefresh = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblStatus = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    public MainForm()
    {
        Text = "CalibraHub — Servis Yoneticisi";
        Size = new Size(820, 420);
        MinimumSize = new Size(640, 320);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        BuildLayout();
        LoadStatuses();

        _timer.Interval = 3000;
        _timer.Tick += (_, _) => LoadStatuses();
        _timer.Start();
    }

    // ── Layout ──────────────────────────────────────────────────────────────
    private void BuildLayout()
    {
        // Grid
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ReadOnly = true;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.RowTemplate.Height = 28;
        _grid.Columns.Add("ServiceName",  "Servis Adi");
        _grid.Columns.Add("Status",       "Durum");
        _grid.Columns.Add("StartType",    "Baslangic");
        _grid.Columns.Add("Description",  "Aciklama");
        _grid.Columns["ServiceName"]!.FillWeight = 28;
        _grid.Columns["Status"]!.FillWeight      = 16;
        _grid.Columns["StartType"]!.FillWeight   = 16;
        _grid.Columns["Description"]!.FillWeight = 40;
        _grid.SelectionChanged += (_, _) => UpdateButtonStates();

        // Buttons
        _btnStart.Text   = "▶  Baslat";
        _btnStop.Text    = "■  Durdur";
        _btnRestart.Text = "↻  Yeniden Baslat";
        _btnRefresh.Text = "Yenile";
        foreach (var b in new[] { _btnStart, _btnStop, _btnRestart, _btnRefresh })
        {
            b.AutoSize = true;
            b.Padding = new Padding(8, 4, 8, 4);
            b.FlatStyle = FlatStyle.System;
        }
        _btnStart.Click   += (_, _) => DoServiceAction(ServiceAction.Start);
        _btnStop.Click    += (_, _) => DoServiceAction(ServiceAction.Stop);
        _btnRestart.Click += (_, _) => DoServiceAction(ServiceAction.Restart);
        _btnRefresh.Click += (_, _) => LoadStatuses();

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(8, 6, 8, 6),
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.FromArgb(245, 247, 250),
        };
        btnPanel.Controls.Add(_btnStart);
        btnPanel.Controls.Add(_btnStop);
        btnPanel.Controls.Add(_btnRestart);
        var spacer = new Label { Width = 16 }; btnPanel.Controls.Add(spacer);
        btnPanel.Controls.Add(_btnRefresh);

        // Status strip
        _lblStatus.Spring = true;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _status.Items.Add(_lblStatus);
        _status.SizingGrip = false;

        // Compose
        Controls.Add(_grid);
        Controls.Add(btnPanel);
        Controls.Add(_status);
    }

    // ── Veri yukleme ────────────────────────────────────────────────────────
    private void LoadStatuses()
    {
        var prevSelected = _grid.CurrentRow?.Cells["ServiceName"].Value as string;
        _grid.SuspendLayout();
        _grid.Rows.Clear();

        foreach (var svc in ManagedServices)
        {
            var (status, startType) = QueryService(svc.Name);
            var row = new DataGridViewRow();
            row.CreateCells(_grid, svc.Name, status, startType, svc.Description);

            row.DefaultCellStyle.ForeColor = status switch
            {
                "Calisiyor"     => Color.FromArgb(22, 101, 52),  // yesil-koyu
                "Durdu"         => Color.FromArgb(120, 53, 15),  // amber-koyu
                "Yuklu degil"   => Color.Gray,
                _               => Color.Black,
            };
            _grid.Rows.Add(row);
        }

        // Sececek satiri koru
        if (prevSelected != null)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if ((row.Cells["ServiceName"].Value as string) == prevSelected)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells["ServiceName"];
                    break;
                }
            }
        }
        else if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
        }

        _grid.ResumeLayout();
        UpdateButtonStates();
        _lblStatus.Text = $"Son guncelleme: {DateTime.Now:HH:mm:ss}  |  Yonetilen servis: {ManagedServices.Length}";
    }

    private static (string Status, string StartType) QueryService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            var status = sc.Status switch
            {
                ServiceControllerStatus.Running         => "Calisiyor",
                ServiceControllerStatus.Stopped         => "Durdu",
                ServiceControllerStatus.Paused          => "Duraklatildi",
                ServiceControllerStatus.StartPending    => "Baslatiliyor...",
                ServiceControllerStatus.StopPending     => "Durduruluyor...",
                ServiceControllerStatus.ContinuePending => "Devam ettiriliyor...",
                ServiceControllerStatus.PausePending    => "Duraklatiliyor...",
                _                                        => sc.Status.ToString()
            };
            var startType = sc.StartType switch
            {
                ServiceStartMode.Automatic => "Otomatik",
                ServiceStartMode.Manual    => "Manuel",
                ServiceStartMode.Disabled  => "Devre disi",
                ServiceStartMode.Boot      => "Boot",
                ServiceStartMode.System    => "Sistem",
                _                          => sc.StartType.ToString()
            };
            return (status, startType);
        }
        catch (InvalidOperationException)
        {
            return ("Yuklu degil", "—");
        }
        catch (Win32Exception ex)
        {
            return ($"Hata: {ex.Message}", "—");
        }
    }

    // ── Buton state ─────────────────────────────────────────────────────────
    private void UpdateButtonStates()
    {
        var status = _grid.CurrentRow?.Cells["Status"].Value as string ?? "";
        var installed = status != "Yuklu degil" && !status.StartsWith("Hata", StringComparison.Ordinal);
        var running   = status == "Calisiyor";
        var stopped   = status == "Durdu";

        _btnStart.Enabled   = installed && stopped;
        _btnStop.Enabled    = installed && running;
        _btnRestart.Enabled = installed && running;
    }

    // ── Aksiyon ─────────────────────────────────────────────────────────────
    private enum ServiceAction { Start, Stop, Restart }

    private void DoServiceAction(ServiceAction action)
    {
        if (_grid.CurrentRow?.Cells["ServiceName"].Value is not string svcName) return;

        SetUiBusy(true);
        try
        {
            using var sc = new ServiceController(svcName);
            switch (action)
            {
                case ServiceAction.Start:
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    break;
                case ServiceAction.Stop:
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    break;
                case ServiceAction.Restart:
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Servis islemi basarisiz:\n\n{ex.Message}",
                "CalibraHub — Hata",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetUiBusy(false);
            LoadStatuses();
        }
    }

    private void SetUiBusy(bool busy)
    {
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _btnStart.Enabled = _btnStop.Enabled = _btnRestart.Enabled = _btnRefresh.Enabled = !busy;
        if (!busy) UpdateButtonStates();
    }

    private sealed record ServiceDef(string Name, string Description);
}
