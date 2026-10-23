using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SoftwareGamer.Core;

namespace SoftwareGamer.App;

public partial class MainWindow : Window
{
    private readonly StateStore store;
    private readonly CleanerService cleaner;
    private readonly BoosterService booster;
    private readonly string? smokeOutput;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private CancellationTokenSource? operation;
    private bool busy, ticking, refreshing;
    private GameProfile? editing;
    private List<CandidateRow> candidates = [];
    private readonly HashSet<string> autoAttempted = [];
    private DateTime cpuAt = DateTime.UtcNow;
    private TimeSpan cpuUsed = Process.GetCurrentProcess().TotalProcessorTime;
    private bool suppressAuto;
    public MainWindow(StateStore store, CleanerService cleaner, string? smokeOutput)
    {
        InitializeComponent();
        (this.store, this.cleaner, this.smokeOutput) = (store, cleaner, smokeOutput);
        booster = new(store);
        PowerCombo.Items.Add(new PowerScheme("", "Manter plano atual (recomendado)"));
        try { foreach (var scheme in WindowsSystem.PowerSchemes()) PowerCombo.Items.Add(scheme); }
        catch (Exception e) { Status(e.Message); }
        PowerCombo.SelectedIndex = 0;
        RefreshState(); RefreshSystem();
        timer.Tick += async (_, _) => await Tick();
        Loaded += async (_, _) =>
        {
            if (smokeOutput is not null) await Smoke();
            else timer.Start();
        };
        Closing += OnClosing;
    }
    private void Status(string text) => StatusText.Text = text;
    private async Task Work(Func<Task> action)
    {
        if (busy || ticking) { Status("Aguarde a operação atual ou solicite seu cancelamento."); return; }
        busy = true; operation = new CancellationTokenSource();
        try { await action(); }
        catch (OperationCanceledException) { Status("Operação cancelada. Itens já processados continuam no histórico."); }
        catch (Exception e) { Status(e.Message); if (smokeOutput is null) MessageBox.Show(this, e.Message, "Operação interrompida", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally
        {
            busy = false; operation.Dispose(); operation = null;
            try { RefreshState(); } catch (Exception e) { Status(e.Message); }
        }
    }
    private bool Confirm(string message) => smokeOutput is null && MessageBox.Show(this, message, "Confirme a alteração", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private void RefreshState()
    {
        var state = store.Read();
        var selected = editing?.Id;
        refreshing = true;
        ProfilesList.ItemsSource = state.Profiles;
        ProfilesList.SelectedItem = state.Profiles.FirstOrDefault(p => p.Id == selected);
        refreshing = false;
        ChangesGrid.ItemsSource = state.Changes.AsEnumerable().Reverse().ToArray();
        QuarantineGrid.ItemsSource = state.Quarantine.AsEnumerable().Reverse().ToArray();
        FreedText.Text = Format.Bytes(state.PurgedBytes);
        ChangesText.Text = state.Changes.Count(c => c.Pending && c.Kind != "StartupRecovery").ToString();
        AutoCheck.IsChecked = state.AutoProfiles;
    }
    private void RefreshSystem()
    {
        var memory = WindowsSystem.Memory();
        MemoryText.Text = $"{Format.Bytes(memory.RamUsed)} / {Format.Bytes(memory.RamTotal)}";
        SourceText.Text = WindowsSystem.PowerSource();
        var active = WindowsSystem.ActivePowerScheme();
        var scheme = PowerCombo.Items.OfType<PowerScheme>().FirstOrDefault(s => s.Id == active)?.Name ?? active;
        var disk = new DriveInfo(Path.GetPathRoot(store.DirectoryPath)!);
        SystemText.Text = $"Plano atual: {scheme}\nDisco {disk.Name}: {Format.Bytes(disk.AvailableFreeSpace)} livres · {Environment.OSVersion.VersionString}";
        using var proc = Process.GetCurrentProcess();
        var now = DateTime.UtcNow; var used = proc.TotalProcessorTime;
        var percent = (used - cpuUsed).TotalSeconds / Math.Max(0.1, (now - cpuAt).TotalSeconds) * 100;
        cpuUsed = used; cpuAt = now;
        RuntimeText.Text = $"Este aplicativo: {Format.Bytes(proc.WorkingSet64)} de RAM · CPU {percent:N1}% de um núcleo lógico (janela ~3 s)";
    }
    private async Task Tick()
    {
        if (busy || ticking) return;
        ticking = true;
        try
        {
            RefreshSystem();
            await Task.Run(booster.ReconcileRunning);
            var state = store.Read();
            if (state.AutoProfiles && !suppressAuto && !state.Changes.Any(c => c.Pending))
            {
                var processes = await Task.Run(WindowsSystem.Processes);
                foreach (var profile in state.Profiles.Where(p => p.AboveNormal || p.PowerSchemeId is not null))
                {
                    var game = processes.FirstOrDefault(p => string.Equals(p.Path, profile.ExecutablePath, StringComparison.OrdinalIgnoreCase));
                    if (game is null || !autoAttempted.Add(game.Identity)) continue;
                    App.EnsureWatchdog();
                    await Task.Run(() => booster.Apply(profile, game, App.ExecutablePath));
                    Status($"Perfil aplicado: {profile.Name}. Os ajustes serão restaurados ao encerrar o jogo."); break;
                }
            }
            // Do not replace the profile editor's data source on every timer tick.
            ChangesGrid.ItemsSource = store.Read().Changes.AsEnumerable().Reverse().ToArray();
            ChangesText.Text = store.Read().Changes.Count(c => c.Pending && c.Kind != "StartupRecovery").ToString();
        }
        catch (Exception e) { Status(e.Message); }
        finally { ticking = false; }
    }
    private void GoCleaner(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = 1;
    private void GoProfiles(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = 2;
    private async void ScanClick(object sender, RoutedEventArgs e) => await Scan();
    private async Task Scan() => await Work(async () =>
    {
        Status("Analisando temporários; nenhum arquivo será alterado...");
        var result = await Task.Run(() => cleaner.Scan(operation!.Token));
        candidates = result.Files.Select(x => new CandidateRow(x)).ToList(); FilesGrid.ItemsSource = candidates;
        ScanSummary.Text = $"{candidates.Count} candidatos · {Format.Bytes(candidates.Sum(x => x.Item.Identity.Length))} estimados · {result.Ignored} ignorados. " + string.Join(" ", result.Notes);
        Status("Análise concluída. Selecione explicitamente o que deseja preservar em quarentena.");
    });
    private void CancelClick(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void SelectAllClick(object sender, RoutedEventArgs e) { foreach (var c in candidates) c.Selected = true; FilesGrid.Items.Refresh(); }
    private void SelectNoneClick(object sender, RoutedEventArgs e) { foreach (var c in candidates) c.Selected = false; FilesGrid.Items.Refresh(); }
    private async void QuarantineClick(object sender, RoutedEventArgs e)
    {
        FilesGrid.CommitEdit();
        var selected = candidates.Where(c => c.Selected).Select(c => c.Item).ToArray();
        if (selected.Length == 0) { Status("Selecione pelo menos um arquivo."); return; }
        if (!Confirm($"Mover {selected.Length} arquivos ({Format.Bytes(selected.Sum(c => c.Identity.Length))}) para quarentena?\n\nIsso não libera espaço ainda. Você poderá restaurar os arquivos em Recuperação. Arquivos ocupados ou alterados serão ignorados.")) return;
        await Work(async () =>
        {
            var profiles = store.Read().Profiles;
            var running = await Task.Run(WindowsSystem.Processes);
            if (running.Any(p => profiles.Any(g => string.Equals(p.Path, g.ExecutablePath, StringComparison.OrdinalIgnoreCase)))) throw new IOException("Encerre os jogos cadastrados antes de limpar temporários.");
            Status("Movendo itens selecionados com verificação de identidade...");
            var messages = await Task.Run(() => cleaner.Quarantine(selected, operation!.Token));
            candidates.Clear(); FilesGrid.ItemsSource = null;
            Status($"Lote concluído: {messages.Count(m => m.StartsWith("Preservado:"))} preservados. Revise Recuperação.");
            MessageBox.Show(this, string.Join("\n", messages.Take(20)) + (messages.Count > 20 ? "\n… demais itens no histórico." : ""), "Resultado da limpeza");
        });
    }
    private void ProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        if (ProfilesList.SelectedItem is not GameProfile p) return;
        editing = p; ProfileName.Text = p.Name; ProfilePath.Text = p.ExecutablePath;
        PriorityCheck.IsChecked = p.AboveNormal;
        PowerCombo.SelectedItem = PowerCombo.Items.OfType<PowerScheme>().FirstOrDefault(s => s.Id == (p.PowerSchemeId ?? "")) ?? PowerCombo.Items[0];
    }
    private async void AddProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Executável Windows (*.exe)|*.exe", Title = "Selecione o executável do jogo" };
        if (dialog.ShowDialog(this) != true) return;
        await Work(async () =>
        {
            var hash = await Task.Run(() => WindowsSystem.ExecutableHash(dialog.FileName));
            editing = new() { Name = Path.GetFileNameWithoutExtension(dialog.FileName), ExecutablePath = dialog.FileName, ExecutableHash = hash };
            ProfileName.Text = editing.Name; ProfilePath.Text = editing.ExecutablePath; PriorityCheck.IsChecked = false; PowerCombo.SelectedIndex = 0;
            Status("Executável selecionado. Escolha os ajustes opcionais e salve o perfil.");
        });
    }
    private async void SaveProfileClick(object sender, RoutedEventArgs e) => await Work(() =>
    {
        if (editing is null || string.IsNullOrWhiteSpace(ProfileName.Text)) throw new InvalidOperationException("Adicione um executável e informe o nome do perfil.");
        var p = new GameProfile { Id = editing.Id, Name = ProfileName.Text.Trim(), ExecutablePath = editing.ExecutablePath, ExecutableHash = editing.ExecutableHash,
            AboveNormal = PriorityCheck.IsChecked == true, PowerSchemeId = (PowerCombo.SelectedItem as PowerScheme)?.Id is { Length: > 0 } id ? id : null };
        store.Use(state => { if (state.Profiles.Any(x => x.Id != p.Id && string.Equals(x.ExecutablePath, p.ExecutablePath, StringComparison.OrdinalIgnoreCase))) throw new IOException("Este executável já possui perfil."); state.Profiles.RemoveAll(x => x.Id == p.Id); state.Profiles.Add(p); return true; });
        editing = p; autoAttempted.Clear(); Status("Perfil salvo. Nenhum ajuste foi aplicado neste momento."); return Task.CompletedTask;
    });
    private async void DeleteProfileClick(object sender, RoutedEventArgs e)
    {
        if (editing is null || !Confirm("Excluir este perfil? Ajustes ativos serão restaurados primeiro.")) return;
        await Work(() => { booster.RestoreAll(); store.Use(state => { state.Profiles.RemoveAll(p => p.Id == editing.Id); return true; }); editing = null; ProfileName.Clear(); ProfilePath.Clear(); Status("Perfil excluído."); return Task.CompletedTask; });
    }
    private async void LaunchClick(object sender, RoutedEventArgs e) => await Work(() =>
    {
        if (editing is null) throw new InvalidOperationException("Selecione um perfil.");
        if (WindowsSystem.ExecutableHash(editing.ExecutablePath) != editing.ExecutableHash) throw new IOException("Executável alterado. Cadastre o perfil novamente.");
        Process.Start(new ProcessStartInfo(editing.ExecutablePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(editing.ExecutablePath) });
        Status("Jogo iniciado. Use Aplicar ao processo ou habilite os perfis automáticos."); return Task.CompletedTask;
    });
    private async void RefreshProcessesClick(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var list = await Task.Run(WindowsSystem.Processes);
        ProcessCombo.ItemsSource = list; if (editing is not null) ProcessCombo.SelectedItem = list.FirstOrDefault(p => string.Equals(p.Path, editing.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        Status("Processos atualizados. Apenas o executável correspondente ao perfil pode receber ajustes.");
    });
    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (editing is null || ProcessCombo.SelectedItem is not GameProcess game) { Status("Salve um perfil e selecione seu processo em execução."); return; }
        var profile = store.Read().Profiles.FirstOrDefault(p => p.Id == editing.Id);
        if (profile is null) { Status("Salve o perfil antes de aplicar."); return; }
        if (!Confirm($"Aplicar o perfil '{profile.Name}' ao PID {game.Pid}?\n\nPrioridade: {(profile.AboveNormal ? "AboveNormal" : "manter atual")}\nEnergia: {(profile.PowerSchemeId is null ? "manter atual" : "trocar para o plano escolhido, apenas em AC")}\n\nNão há garantia de ganho. Mudanças externas serão preservadas na restauração.")) return;
        await Work(async () => { App.EnsureWatchdog(); await Task.Run(() => booster.Apply(profile, game, App.ExecutablePath)); autoAttempted.Add(game.Identity); Status("Perfil processado. Revise os ajustes em Recuperação."); });
    }
    private async void AutoClick(object sender, RoutedEventArgs e)
    {
        var enabled = AutoCheck.IsChecked == true;
        if (enabled && !Confirm("Aplicar automaticamente os ajustes que você salvou quando os executáveis cadastrados forem detectados?\n\nFunciona somente enquanto o Software Gamer estiver aberto. Será mantido no máximo um perfil com ajustes ativos.")) { AutoCheck.IsChecked = false; return; }
        await Work(() => { store.Use(state => { state.AutoProfiles = enabled; return true; }); suppressAuto = false; autoAttempted.Clear(); Status(enabled ? "Perfis automáticos ativados." : "Perfis automáticos desativados. Ajustes já ativos podem ser restaurados em Recuperação."); return Task.CompletedTask; });
    }
    private async void RestoreAllClick(object sender, RoutedEventArgs e) => await Work(() => { suppressAuto = true; booster.RestoreAll(); Status("Restauração executada. Conflitos foram preservados; automação pausada nesta sessão."); return Task.CompletedTask; });
    private async void RestoreOneClick(object sender, RoutedEventArgs e)
    { if (ChangesGrid.SelectedItem is ChangeRecord c) await Work(() => { suppressAuto = true; booster.RestoreOne(c.Id); Status("Ajuste revisado. Veja o estado e o detalhe no histórico."); return Task.CompletedTask; }); }
    private async void KeepCurrentClick(object sender, RoutedEventArgs e)
    {
        if (ChangesGrid.SelectedItem is not ChangeRecord c || !Confirm("Manter o estado atual e encerrar a tentativa de restaurar este conflito?\nNenhuma configuração do Windows será sobrescrita.")) return;
        await Work(() => { suppressAuto = true; booster.KeepCurrent(c.Id); Status("Estado atual preservado e conflito encerrado."); return Task.CompletedTask; });
    }
    private async void RestoreFileClick(object sender, RoutedEventArgs e)
    { if (QuarantineGrid.SelectedItem is QuarantineRecord q) await Work(() => { Status(cleaner.Restore(q.Id)); return Task.CompletedTask; }); }
    private async void RestoreFilesClick(object sender, RoutedEventArgs e)
    {
        if (!Confirm("Restaurar todos os arquivos disponíveis? Nenhum arquivo existente será sobrescrito.")) return;
        await Work(() =>
        {
            var failures = new List<string>(); var count = 0;
            foreach (var q in store.Read().Quarantine.Where(q => q.Status == "Quarantined"))
                try { cleaner.Restore(q.Id); count++; } catch (Exception ex) { failures.Add(ex.Message); }
            Status($"{count} arquivos restaurados; {failures.Count} conflitos/erros." + (failures.Count > 0 ? " " + failures[0] : "")); return Task.CompletedTask;
        });
    }
    private async void PurgeClick(object sender, RoutedEventArgs e)
    {
        if (QuarantineGrid.SelectedItem is not QuarantineRecord q || !Confirm($"Excluir PERMANENTEMENTE este item da quarentena?\n\n{q.OriginalPath}\n{q.SizeLabel}\n\nEsta ação não pode ser desfeita pelo aplicativo.")) return;
        await Work(() => { Status(cleaner.Purge(q.Id)); return Task.CompletedTask; });
    }
    private void OpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var uri = (sender as Button)?.Tag as string;
        if (uri is "ms-settings:storagesense" or "ms-settings:gaming-gamemode" or "ms-settings:gaming-gamedvr" or "ms-settings:powersleep")
            try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ex) { Status(ex.Message); }
    }
    private void OpenRepositoryClick(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://github.com/pedroisf/software-gamer/releases") { UseShellExecute = true });
    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Diagnóstico JSON|*.json", FileName = "SoftwareGamer-diagnostico.json" };
        if (dialog.ShowDialog(this) != true) return;
        await Work(async () =>
        {
            var s = store.Read();
            var data = new { version = "0.1.0", os = Environment.OSVersion.VersionString, is64Bit = Environment.Is64BitProcess,
                pendingChanges = s.Changes.Count(c => c.Pending), conflicts = s.Changes.Count(c => c.Status == "Conflict"),
                quarantined = s.Quarantine.Count(q => q.Status == "Quarantined"), profileCount = s.Profiles.Count, power = WindowsSystem.PowerSource() };
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })); Status("Diagnóstico salvo no local escolhido. Nenhum dado foi enviado.");
        });
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (smokeOutput is not null) return;
        if (busy || ticking) { operation?.Cancel(); e.Cancel = true; Status("Aguarde a operação terminar antes de fechar."); return; }
        timer.Stop();
        try { booster.RestoreAll(WindowsSystem.CurrentOwner); }
        catch (Exception ex) { MessageBox.Show(this, "A recuperação continuará pelo processo de proteção e no próximo logon, quando registrado. " + ex.Message, "Restauração pendente"); }
    }
    private async Task Smoke()
    {
        try
        {
            Directory.CreateDirectory(smokeOutput!);
            await Scan();
            foreach (var i in new[] { 0, 1, 2, 3, 4 })
            {
                Tabs.SelectedIndex = i; UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var bmp = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var output = File.Create(Path.Combine(smokeOutput!, $"screen-{i}.png")); encoder.Save(output);
            }
            File.WriteAllText(Path.Combine(smokeOutput!, "smoke-result.json"), JsonSerializer.Serialize(new { rendered = 5, candidates = candidates.Count }));
            if (candidates.Count != 1) { Application.Current.Shutdown(4); return; }
            Close();
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(smokeOutput!, "smoke-error.txt"), ex.ToString()); Application.Current.Shutdown(1); }
    }
    private sealed class CandidateRow(CleanupCandidate item)
    {
        public CleanupCandidate Item { get; } = item;
        public bool Selected { get; set; }
    }
}
