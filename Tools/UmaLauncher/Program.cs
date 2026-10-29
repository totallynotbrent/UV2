using System.IO;
using System.IO.Compression;

namespace UmaLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // --control <dir>: attach the control window to a player that
            // started itself (a direct UmaLive.exe run) — the player publishes
            // status.json in its own folder; this window reads it and writes
            // commands without owning the player process.
            if (args.Length >= 2 && args[0] == "--control")
            {
                Updater.ApplyPending();
                ApplicationConfiguration.Initialize();
                Application.Run(new ControlForm(args[1], null, owns: false));
                return;
            }
            Updater.ApplyPending();
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
