namespace CalibraHub.Designer;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Tek örnek kontrolü
        using var mutex = new System.Threading.Mutex(true, "CalibraHub.Designer.SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "CalibraHub Designer zaten çalışıyor.",
                "CalibraHub Designer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var server = new LocalApiServer();

        // HTTP dinleyiciyi arka planda başlat
        var cts = new System.Threading.CancellationTokenSource();
        Task.Run(() => server.StartAsync(cts.Token));

        Application.Run(new TrayApplication(server));

        cts.Cancel();
    }
}
