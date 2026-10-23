using System.Diagnostics;
using Microsoft.Win32;

namespace SoftwareGamer.Core;

public sealed class BoosterService(StateStore store)
{
    public const string StartupName = "SoftwareGamerRecovery";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public void Apply(GameProfile profile, GameProcess game, string appPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!string.Equals(game.Path, profile.ExecutablePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("O executável não corresponde ao perfil.");
        if (WindowsSystem.ExecutableHash(game.Path) != profile.ExecutableHash) throw new InvalidOperationException("O jogo foi atualizado ou alterado. Remova e cadastre novamente o perfil para revisar sua identidade.");
        if (profile.PowerSchemeId is not null && !WindowsSystem.OnAcPower()) throw new InvalidOperationException("O ajuste de energia só pode ser aplicado na tomada. Mantenha o plano atual na bateria.");
        using var process = WindowsSystem.OpenInstance(game.Identity) ?? throw new InvalidOperationException("O processo terminou. Atualize a lista.");
        store.Use(state =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (state.Changes.Any(c => c.Pending)) throw new InvalidOperationException("Restaure ou resolva os ajustes anteriores antes de aplicar outro perfil.");
            var owner = WindowsSystem.CurrentOwner;
            try
            {
                if (profile.PowerSchemeId is not null && profile.PowerSchemeId != WindowsSystem.ActivePowerScheme())
                {
                    using var run = Registry.CurrentUser.CreateSubKey(RunKey, true);
                    var existing = run.GetValue(StartupName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (existing is not null) throw new IOException("Já existe uma recuperação registrada. Use Restaurar tudo antes de continuar.");
                    var command = $"\"{appPath}\" --recover";
                    ApplyChange(state, new() { Owner = owner, SessionId = game.Identity, Kind = "StartupRecovery", Resource = StartupName, Before = null, Applied = command },
                        () => { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); run.SetValue(StartupName, command, RegistryValueKind.String); });
                    var before = WindowsSystem.ActivePowerScheme();
                    ApplyChange(state, new() { Owner = owner, SessionId = game.Identity, Kind = "PowerScheme", Resource = "UserPowerScheme", Before = before, Applied = profile.PowerSchemeId },
                        () => { WindowsSystem.SetPowerScheme(profile.PowerSchemeId); if (WindowsSystem.ActivePowerScheme() != profile.PowerSchemeId) throw new IOException("O Windows não confirmou a troca do plano."); });
                }
                if (profile.AboveNormal && process.PriorityClass != ProcessPriorityClass.AboveNormal)
                {
                    var before = process.PriorityClass.ToString();
                    // Do not demote/reconfigure a process that another program has already boosted.
                    if (process.PriorityClass != ProcessPriorityClass.Normal) throw new IOException("O processo já usa uma prioridade diferente de Normal. O perfil não irá sobrescrevê-la.");
                    ApplyChange(state, new() { Owner = owner, SessionId = game.Identity, Kind = "Priority", Resource = game.Identity, Before = before, Applied = "AboveNormal" },
                        () => { process.PriorityClass = ProcessPriorityClass.AboveNormal; if (process.PriorityClass != ProcessPriorityClass.AboveNormal) throw new IOException("Prioridade não confirmada."); });
                }
            }
            catch { RestoreRecords(state, owner); store.Save(state); throw; }
            return true;
        });
    }
    private void ApplyChange(AppState state, ChangeRecord c, Action apply)
    {
        state.Changes.Add(c); store.Save(state);
        apply(); c.Status = "Applied"; store.Save(state);
    }
    public void RestoreAll(string? owner = null) => store.Use(state => { RestoreRecords(state, owner); return true; });
    public void KeepCurrent(string id) => store.Use(state =>
    {
        var c = state.Changes.Single(x => x.Id == id);
        if (c.Status != "Conflict" || c.Kind == "StartupRecovery") throw new InvalidOperationException("Somente conflitos de ajustes podem ser encerrados desta forma.");
        c.Status = "Released"; c.Detail = "Usuário optou por manter o estado atual; restauração encerrada.";
        CleanupRegistration(state); return true;
    });
    public void RestoreOne(string id) => store.Use(state =>
    {
        var c = state.Changes.Single(x => x.Id == id);
        if (c.Kind == "StartupRecovery" && state.Changes.Any(x => x.Pending && x.Kind == "PowerScheme")) throw new IOException("Restaure o plano de energia antes de remover sua recuperação no logon.");
        Restore(c); store.Save(state);
        CleanupRegistration(state); return true;
    });
    public void ReconcileRunning()
    {
        store.Use(state =>
        {
            var before = string.Join('|', state.Changes.Select(c => c.Status));
            foreach (var group in state.Changes.Where(c => c.Pending && c.Kind != "StartupRecovery").GroupBy(c => c.SessionId))
            {
                using var process = WindowsSystem.OpenInstance(group.Key);
                if (process is null || process.HasExited)
                    foreach (var c in group.Reverse()) Restore(c);
                else if (!WindowsSystem.OnAcPower())
                    foreach (var c in group.Where(c => c.Kind == "PowerScheme")) Restore(c);
            }
            CleanupRegistration(state);
            if (before != string.Join('|', state.Changes.Select(c => c.Status))) store.Save(state);
            return true;
        }, save: false);
    }
    private void RestoreRecords(AppState state, string? owner)
    {
        foreach (var c in state.Changes.Where(c => c.Pending && c.Kind != "StartupRecovery" && (owner is null || c.Owner == owner)).Reverse())
        { Restore(c); store.Save(state); }
        CleanupRegistration(state);
    }
    private static void CleanupRegistration(AppState state)
    {
        if (!state.Changes.Any(c => c.Pending && c.Kind == "PowerScheme"))
            foreach (var c in state.Changes.Where(c => c.Pending && c.Kind == "StartupRecovery")) Restore(c);
    }
    private static void Restore(ChangeRecord c)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!c.Pending) return;
        try
        {
            string? current;
            switch (c.Kind)
            {
                case "PowerScheme":
                    current = WindowsSystem.ActivePowerScheme();
                    if (current == c.Before) { c.Status = "Restored"; break; }
                    if (!RestorePolicy.CanRestore(current, c.Applied)) { Conflict(c); break; }
                    WindowsSystem.SetPowerScheme(c.Before!); c.Status = WindowsSystem.ActivePowerScheme() == c.Before ? "Restored" : "Conflict"; break;
                case "Priority":
                    using (var p = WindowsSystem.OpenInstance(c.Resource))
                    {
                        if (p is null || p.HasExited) { c.Status = "Restored"; break; }
                        current = p.PriorityClass.ToString();
                        if (current == c.Before) { c.Status = "Restored"; break; }
                        if (!RestorePolicy.CanRestore(current, c.Applied)) { Conflict(c); break; }
                        if (c.Before != "Normal" || c.Applied != "AboveNormal") throw new InvalidDataException("Registro de prioridade não autorizado.");
                        p.PriorityClass = ProcessPriorityClass.Normal; c.Status = p.PriorityClass == ProcessPriorityClass.Normal ? "Restored" : "Conflict";
                    }
                    break;
                case "StartupRecovery":
                    using (var run = Registry.CurrentUser.CreateSubKey(RunKey, true))
                    {
                        current = run.GetValue(StartupName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (current is null) { c.Status = "Restored"; break; }
                        if (!RestorePolicy.CanRestore(current, c.Applied)) { Conflict(c); break; }
                        run.DeleteValue(StartupName, false); c.Status = "Restored";
                    }
                    break;
                default: throw new InvalidDataException("Tipo de restauração desconhecido.");
            }
            if (c.Status == "Restored") c.Detail = "Estado anterior restaurado ou recurso já encerrado.";
        }
        catch (Exception e) { c.Status = "Conflict"; c.Detail = e.Message; }
    }
    private static void Conflict(ChangeRecord c) { c.Status = "Conflict"; c.Detail = "O estado atual foi alterado fora do perfil. Ele foi preservado; revise manualmente nas configurações do Windows."; }
}
