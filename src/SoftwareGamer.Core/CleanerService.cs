namespace SoftwareGamer.Core;

public sealed class CleanerService(StateStore store, string? temporaryRoot = null)
{
    // Production never accepts an arbitrary root supplied by UI/environment variables.
    public string Root { get; } = temporaryRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
    public ScanResult Scan(CancellationToken cancellationToken = default)
    {
        var result = new List<CleanupCandidate>(); var ignored = 0; var visited = 0;
        var notes = new List<string>();
        var stack = new Stack<(string Path, int Depth)>(); stack.Push((Root, 0));
        while (stack.TryPop(out var dir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dir.Depth > 12) { ignored++; continue; }
            List<Microsoft.Win32.SafeHandles.SafeFileHandle>? handles = null;
            try
            {
                handles = WindowsFile.LockDirectory(dir.Path);
                foreach (var path in Directory.EnumerateFileSystemEntries(dir.Path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++visited > 50000 || result.Count >= CleanupPolicy.MaximumCandidates)
                    { notes.Add("Limite da análise atingido. Revise este lote antes de uma nova análise."); return new(result, ignored, notes); }
                    try
                    {
                        var attr = File.GetAttributes(path);
                        if ((attr & FileAttributes.ReparsePoint) != 0) { ignored++; continue; }
                        if ((attr & FileAttributes.Directory) != 0) { stack.Push((path, dir.Depth + 1)); continue; }
                        if (!CleanupPolicy.Eligible(Root, path, File.GetLastWriteTimeUtc(path), DateTimeOffset.UtcNow)) { ignored++; continue; }
                        using var f = WindowsFile.Open(Root, path, false);
                        result.Add(new(Root, path, f.Identity, DateTimeOffset.UtcNow));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { ignored++; }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { ignored++; if (dir.Depth == 0) notes.Add(e.Message); }
            finally { handles?.ForEach(h => h.Dispose()); }
        }
        return new(result, ignored, notes);
    }

    public IReadOnlyList<string> Quarantine(IEnumerable<CleanupCandidate> candidates, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(store.QuarantinePath);
        return store.Use<IReadOnlyList<string>>(state =>
        {
            var messages = new List<string>();
            foreach (var item in candidates.Take(CleanupPolicy.MaximumCandidates))
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    if (!string.Equals(Path.GetFullPath(item.Root), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)) throw new IOException("Raiz não autorizada.");
                    if (state.Quarantine.Where(q => q.Status is "Prepared" or "Quarantined").Sum(q => q.Identity.Length) + item.Identity.Length > CleanupPolicy.QuarantineLimit)
                        throw new IOException("Quarentena atingiu 2 GiB. Restaure ou exclua itens pela tela de recuperação.");
                    using var f = WindowsFile.Open(Root, item.Path, true);
                    CleanupPolicy.ValidatePlan(item, f.Identity, DateTimeOffset.UtcNow);
                    var record = new QuarantineRecord { Root = Root, OriginalPath = item.Path, Identity = f.Identity };
                    record.StoredName = record.Id + ".bin";
                    state.Quarantine.Add(record); store.Save(state);
                    f.Rename(Path.Combine(store.QuarantinePath, record.StoredName));
                    record.Status = "Quarantined"; store.Save(state);
                    messages.Add($"Preservado: {item.Name}");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { messages.Add($"Ignorado: {item.Name} — {e.Message}"); }
            }
            return messages;
        });
    }

    public void Reconcile() => store.Use(state =>
    {
        foreach (var q in state.Quarantine.Where(q => q.Status is "Prepared" or "RestorePrepared" or "PurgePrepared"))
        {
            var stored = StoredPath(q);
            if (File.Exists(stored))
            {
                using var f = WindowsFile.Open(store.QuarantinePath, stored, false);
                q.Status = f.Identity == q.Identity ? "Quarantined" : "Conflict";
            }
            else if (File.Exists(q.OriginalPath))
            {
                using var f = WindowsFile.Open(q.Root, q.OriginalPath, false);
                q.Status = f.Identity == q.Identity ? (q.Status == "RestorePrepared" ? "Restored" : "NotMoved") : "Conflict";
            }
            else if (q.Status == "PurgePrepared") { q.Status = "Purged"; state.PurgedBytes += q.Identity.Length; }
            else { q.Status = "Conflict"; q.Detail = "Arquivo não localizado após operação interrompida."; }
        }
        return true;
    });

    public string Restore(string id) => store.Use(state =>
    {
        var q = state.Quarantine.Single(x => x.Id == id);
        if (q.Status != "Quarantined") return "Item não está disponível para restauração.";
        if (!string.Equals(Path.GetFullPath(q.Root), Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase) || !CleanupPolicy.IsWithin(Root, q.OriginalPath)) throw new IOException("Destino original inválido.");
        using var f = WindowsFile.Open(store.QuarantinePath, StoredPath(q), true);
        if (f.Identity != q.Identity) throw new IOException("O arquivo em quarentena foi alterado.");
        // Native rename is no-replace: a concurrently recreated original is never overwritten.
        q.Status = "RestorePrepared"; store.Save(state);
        try { f.Rename(q.OriginalPath); }
        catch { q.Status = "Quarantined"; store.Save(state); throw; }
        q.Status = "Restored"; return "Arquivo restaurado sem sobrescrever outros arquivos.";
    });

    public string Purge(string id) => store.Use(state =>
    {
        var q = state.Quarantine.Single(x => x.Id == id);
        if (q.Status != "Quarantined") return "Item não está disponível para exclusão.";
        using (var f = WindowsFile.Open(store.QuarantinePath, StoredPath(q), true))
        {
            if (f.Identity != q.Identity) throw new IOException("O arquivo em quarentena foi alterado.");
            q.Status = "PurgePrepared"; store.Save(state);
            try { f.Delete(); }
            catch { q.Status = "Quarantined"; store.Save(state); throw; }
        }
        q.Status = "Purged"; state.PurgedBytes += q.Identity.Length;
        return "Exclusão permanente concluída. O tamanho contabilizado é lógico; o espaço físico pode variar.";
    });

    private string StoredPath(QuarantineRecord q)
    {
        if (!Guid.TryParseExact(q.Id, "N", out _) || q.StoredName != q.Id + ".bin") throw new IOException("Identidade da quarentena inválida.");
        return Path.Combine(store.QuarantinePath, q.StoredName);
    }
}
