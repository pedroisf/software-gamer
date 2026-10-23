namespace SoftwareGamer.Core;

public static class CleanupPolicy
{
    public const long QuarantineLimit = 2L * 1024 * 1024 * 1024;
    public const int MaximumCandidates = 2000;
    public static readonly TimeSpan MinimumAge = TimeSpan.FromDays(7);
    public static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(10);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".tmp", ".temp" };
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
        { "SoftwareGamer", "saves", "savegames", "credentials", "cookies", "sessions", "profiles", ".git", "packages", "installer", "windowsupdate" };

    public static bool IsWithin(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    public static bool Eligible(string root, string path, DateTimeOffset lastWrite, DateTimeOffset now)
    {
        if (!IsWithin(root, path) || !Extensions.Contains(Path.GetExtension(path)) || now - lastWrite < MinimumAge) return false;
        return !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(ProtectedNames.Contains);
    }
    public static void ValidatePlan(CleanupCandidate item, FileIdentity actual, DateTimeOffset now)
    {
        if (now < item.ScannedAt || now - item.ScannedAt > PlanLifetime) throw new IOException("A análise expirou. Analise os arquivos novamente.");
        if (item.Identity != actual) throw new IOException("O arquivo mudou desde a análise. Nenhuma alteração foi feita.");
        if (!Eligible(item.Root, item.Path, DateTimeOffset.FromFileTime(actual.LastWrite), now)) throw new IOException("Arquivo fora das regras de limpeza.");
    }
}

public static class RestorePolicy
{
    public static bool CanRestore(string? current, string applied) => string.Equals(current, applied, StringComparison.Ordinal);
}
