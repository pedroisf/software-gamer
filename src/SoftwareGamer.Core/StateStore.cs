using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace SoftwareGamer.Core;

public sealed class StateStore
{
    private readonly string file;
    private readonly string mutexName;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string DirectoryPath { get; }
    public string QuarantinePath => Path.Combine(DirectoryPath, "Quarantine");
    public StateStore(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftwareGamer");
        Directory.CreateDirectory(DirectoryPath);
        file = Path.Combine(DirectoryPath, "state.json");
        mutexName = "SoftwareGamer.State." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(file))))[..20];
    }
    public AppState Read() => Use(s => s, save: false);
    public T Use<T>(Func<AppState, T> action, bool save = true)
    {
        using var mutex = new Mutex(false, mutexName);
        bool locked;
        try { locked = mutex.WaitOne(TimeSpan.FromSeconds(15)); } catch (AbandonedMutexException) { locked = true; }
        if (!locked) throw new IOException("Outra operação está em andamento. Tente novamente.");
        try
        {
            var state = Load();
            var result = action(state);
            if (save) Save(state);
            return result;
        }
        finally { mutex.ReleaseMutex(); }
    }
    // Call only inside Use: write-ahead record must reach disk before changing the OS/files.
    public void Save(AppState state)
    {
        var tmp = file + ".new";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Json);
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(bytes); stream.Flush(true); }
        File.Move(tmp, file, overwrite: true);
    }
    private AppState Load()
    {
        if (!File.Exists(file)) return new AppState();
        try
        {
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(file)) ?? throw new InvalidDataException("Estado vazio.");
            if (state.SchemaVersion != 1) throw new InvalidDataException("Versão do histórico não suportada. Não serão aplicados ajustes.");
            return state;
        }
        catch (JsonException e) { throw new InvalidDataException("Histórico inválido. Preserve state.json e procure suporte; ajustes foram bloqueados.", e); }
    }
}
