using System.Threading;
using System.Windows.Forms;

namespace CalibraHub.ServiceManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Single-instance — kullanici servisleri ayni anda iki UI'dan yonetmesin
        using var mutex = new Mutex(true, "CalibraHub.ServiceManager.SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "CalibraHub Servis Yoneticisi zaten calisiyor.",
                "CalibraHub",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.Run(new MainForm());
    }
}
