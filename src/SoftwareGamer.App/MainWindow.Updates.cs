using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using SoftwareGamer.Core;

namespace SoftwareGamer.App;

public partial class MainWindow
{
    private readonly HttpClient updateHttp = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly HashSet<Version> updatePrompts = [];
    private AvailableUpdate? pendingUpdate;
    private bool checkingUpdates, windowClosed;

    private void InitializeUpdates()
    {
        VersionText.Text = $"v{AppRelease.Version.ToString(3)}";
        UpdateStatus.Text = $"Versão instalada: {AppRelease.Version.ToString(3)}.";
        UpdateCheck.IsChecked = store.Read().CheckForUpdates;
        updateTimer.Tick += async (_, _) => await CheckUpdates(false);
        Activated += (_, _) => TryShowUpdate();
        Closed += (_, _) =>
        {
            windowClosed = true;
            updateTimer.Stop(); updateLifetime.Cancel(); updateHttp.Dispose();
        };
    }

    private async Task StartUpdateChecks()
    {
        updateTimer.Start();
        try { await Task.Delay(TimeSpan.FromSeconds(3), updateLifetime.Token); }
        catch (OperationCanceledException) { return; }
        await CheckUpdates(false);
    }

    private async Task CheckUpdates(bool manual)
    {
        if (checkingUpdates || windowClosed || smokeOutput is not null || (!manual && UpdateCheck.IsChecked != true)) return;
        checkingUpdates = true;
        UpdateStatus.Text = "Consultando novas versões...";
        try
        {
            var release = await new UpdateClient(updateHttp).CheckAsync(AppRelease.Version, updateLifetime.Token);
            if (windowClosed || (!manual && UpdateCheck.IsChecked != true)) return;
            if (release is null)
            {
                UpdateStatus.Text = $"Você está na versão mais recente disponível: {AppRelease.Version.ToString(3)}.";
                return;
            }
            UpdateStatus.Text = $"Nova versão disponível: {release.Version.ToString(3)}. Você escolhe quando baixar.";
            if (manual) updatePrompts.Remove(release.Version);
            if (!updatePrompts.Contains(release.Version)) pendingUpdate = release;
            TryShowUpdate();
        }
        catch (Exception) when (!windowClosed)
        {
            // Offline/API failures never prevent startup or open an unsolicited error dialog.
            UpdateStatus.Text = "Não foi possível consultar agora. Verifique sua conexão e tente novamente mais tarde.";
        }
        catch (Exception) when (windowClosed) { }
        finally { checkingUpdates = false; }
    }

    private void TryShowUpdate()
    {
        if (pendingUpdate is not { } release || !IsActive || busy || ticking || windowClosed) return;
        pendingUpdate = null;
        if (!updatePrompts.Add(release.Version)) return;
        var preview = release.IsPrerelease ? " (pré-release para testes)" : "";
        var choice = MessageBox.Show(this,
            $"A versão {release.Version.ToString(3)}{preview} está disponível. Você está usando {AppRelease.Version.ToString(3)}.\n\nDeseja baixar a atualização agora?\n\nSim abre a página oficial no navegador. Baixe o instalador ou a versão portátil e feche o aplicativo antes de instalar. Nenhum arquivo será baixado ou executado automaticamente.",
            "Atualização disponível", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);
        if (choice != MessageBoxResult.Yes) return;
        try { Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { UpdateStatus.Text = "Não foi possível abrir o navegador. Use Repositório e versões para acessar o download."; }
    }

    private async void CheckUpdatesClick(object sender, RoutedEventArgs e) => await CheckUpdates(true);
    private async void UpdatePreferenceClick(object sender, RoutedEventArgs e)
    {
        var enabled = UpdateCheck.IsChecked == true;
        await Work(() =>
        {
            store.Use(state => { state.CheckForUpdates = enabled; return true; });
            if (!enabled) pendingUpdate = null;
            UpdateStatus.Text = enabled ? "Avisos automáticos ativados." : "Avisos automáticos desativados. Você pode verificar manualmente.";
            return Task.CompletedTask;
        });
        UpdateCheck.IsChecked = store.Read().CheckForUpdates;
        if (enabled && UpdateCheck.IsChecked == true) await CheckUpdates(false);
    }
}
