using System.IO;
using System.IO.Compression;

namespace UmaLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Updater.ApplyPending();
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
