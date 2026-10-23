using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
using SoftwareGamer.Core;

namespace SoftwareGamer.App;

public partial class App : Application
{
    private Mutex? instance;
    private static Process? watchdog;
    private static bool watchdogConfirmed;
    internal static string ExecutablePath => Environment.ProcessPath ?? throw new IOException("Executável indisponível.");
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            if (e.Args.Length == 3 && e.Args[0] == "--watchdog")
            {
                using var ready = EventWaitHandle.OpenExisting(e.Args[2]);
                using var parent = WindowsSystem.OpenInstance(e.Args[1]);
                ready.Set();
                parent?.WaitForExit();
                new BoosterService(new StateStore()).RestoreAll(e.Args[1]);
                Shutdown(); return;
            }
            var smoke = e.Args.Length == 2 && e.Args[0] == "--smoke";
            // asInvoker does not request elevation, but Windows may supply an elevated
            // token (for example through the launching process). This is not a startup error.
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? "user";
            instance = new Mutex(true, @"Local\SoftwareGamer.UI." + sid, out var created);
            if (!created) { Shutdown(e.Args.Contains("--uninstall-recover") ? 3 : 0); return; }
            if (e.Args.Contains("--recover") || e.Args.Contains("--uninstall-recover"))
            {
                var recoveryStore = new StateStore();
                new BoosterService(recoveryStore).RestoreAll();
                if (e.Args.Contains("--uninstall-recover")) new CleanerService(recoveryStore).Reconcile();
                var state = recoveryStore.Read();
                var blocked = state.Changes.Any(c => c.Pending) || (e.Args.Contains("--uninstall-recover") && state.Quarantine.Any(q => q.Status is "Prepared" or "Quarantined" or "RestorePrepared" or "PurgePrepared" or "Conflict"));
                if (blocked) MessageBox.Show("Existem ajustes pendentes ou arquivos em quarentena. Abra o Software Gamer e revise Recuperação antes de desinstalar.", "Revisão necessária");
                Shutdown(blocked ? 2 : 0); return;
            }
            if (SystemParameters.HighContrast)
            {
                Resources["BackgroundBrush"] = SystemColors.WindowBrush; Resources["PanelBrush"] = SystemColors.WindowBrush;
                Resources["InputBrush"] = SystemColors.ControlBrush; Resources["TextBrush"] = SystemColors.WindowTextBrush;
                Resources["MutedBrush"] = SystemColors.WindowTextBrush; Resources["AccentBrush"] = SystemColors.HighlightBrush;
            }
            StateStore store;
            CleanerService cleaner;
            if (smoke)
            {
                var root = Path.Combine(Path.GetTempPath(), "SoftwareGamerSmoke", Guid.NewGuid().ToString("N"));
                var temp = Path.Combine(root, "Temp"); Directory.CreateDirectory(temp);
                File.WriteAllText(Path.Combine(temp, "exemplo-antigo.tmp"), new string('x', 16384));
                File.SetLastWriteTimeUtc(Path.Combine(temp, "exemplo-antigo.tmp"), DateTime.UtcNow.AddDays(-10));
                store = new StateStore(Path.Combine(root, "State")); cleaner = new CleanerService(store, temp);
            }
            else
            {
                store = new StateStore(); cleaner = new CleanerService(store);
                new BoosterService(store).RestoreAll(); cleaner.Reconcile();
            }
            var window = new MainWindow(store, cleaner, smoke ? e.Args[1] : null);
            // Exercise the real startup path in CI, without the fixture-only smoke branch.
            if (e.Args.Length == 2 && e.Args[0] == "--startup-check")
                window.ContentRendered += (_, _) =>
                {
                    Directory.CreateDirectory(e.Args[1]);
                    using var identity = WindowsIdentity.GetCurrent();
                    File.WriteAllText(Path.Combine(e.Args[1], "startup-result.json"), System.Text.Json.JsonSerializer.Serialize(new
                    {
                        visible = window.IsVisible,
                        administratorToken = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
                        version = AppRelease.Version.ToString(3)
                    }));
                    window.Dispatcher.BeginInvoke(new Action(window.Close));
                };
            MainWindow = window; ShutdownMode = ShutdownMode.OnMainWindowClose; window.Show();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Software Gamer — operação interrompida", MessageBoxButton.OK, MessageBoxImage.Warning); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
    internal static void EnsureWatchdog()
    {
        if (watchdogConfirmed && watchdog is not null && !watchdog.HasExited) return;
        watchdogConfirmed = false;
        var eventName = @"Local\SoftwareGamer.Ready." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--watchdog"); start.ArgumentList.Add(WindowsSystem.CurrentOwner); start.ArgumentList.Add(eventName);
        watchdog?.Dispose();
        watchdog = Process.Start(start) ?? throw new IOException("Não foi possível iniciar a recuperação.");
        if (!ready.WaitOne(TimeSpan.FromSeconds(10)))
        {
            if (!watchdog.HasExited) watchdog.Kill();
            throw new IOException("A recuperação não confirmou disponibilidade. Nenhum perfil foi aplicado.");
        }
        watchdogConfirmed = true;
    }
}
