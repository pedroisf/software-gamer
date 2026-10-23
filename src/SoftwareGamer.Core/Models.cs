namespace SoftwareGamer.Core;

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public List<GameProfile> Profiles { get; set; } = [];
    public List<ChangeRecord> Changes { get; set; } = [];
    public List<QuarantineRecord> Quarantine { get; set; } = [];
    public long PurgedBytes { get; set; }
    public bool AutoProfiles { get; set; }
}

public sealed class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Meu jogo";
    public string ExecutablePath { get; set; } = "";
    public string ExecutableHash { get; set; } = "";
    public bool AboveNormal { get; set; }
    public string? PowerSchemeId { get; set; }
    public override string ToString() => Name;
}

public sealed class ChangeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Resource { get; set; } = "";
    public string? Before { get; set; }
    public string Applied { get; set; } = "";
    public string Status { get; set; } = "Prepared";
    public string Detail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Pending => Status is "Prepared" or "Applied" or "Conflict";
}

public sealed record FileIdentity(uint Volume, ulong FileId, long Length, long LastWrite, uint Links);
public sealed record CleanupCandidate(string Root, string Path, FileIdentity Identity, DateTimeOffset ScannedAt)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string SizeLabel => Format.Bytes(Identity.Length);
}
public sealed record ScanResult(IReadOnlyList<CleanupCandidate> Files, int Ignored, IReadOnlyList<string> Notes);
public sealed class QuarantineRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Root { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string StoredName { get; set; } = "";
    public FileIdentity Identity { get; set; } = new(0, 0, 0, 0, 0);
    public string Status { get; set; } = "Prepared";
    public string Detail { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SizeLabel => Format.Bytes(Identity.Length);
}
public sealed record GameProcess(int Pid, long StartedTicks, string Name, string Path)
{
    public string Identity => $"{Pid}:{StartedTicks}";
    public override string ToString() => $"{Name} · PID {Pid}";
}
public sealed record PowerScheme(string Id, string Name)
{
    public override string ToString() => Name;
}
public static class Format
{
    public static string Bytes(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):N2} GiB" :
        bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):N1} MiB" :
        bytes >= 1L << 10 ? $"{bytes / 1024d:N1} KiB" : $"{bytes} B";
}
