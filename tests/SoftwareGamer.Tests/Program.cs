using System.Diagnostics;
using System.Runtime.InteropServices;
using SoftwareGamer.Core;

if (args.Contains("--hold")) { Thread.Sleep(45000); return 0; }
var passed = 0; var failed = 0; var skipped = 0;
var root = Path.Combine(Path.GetTempPath(), "SoftwareGamerTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Test(string name, Action action, bool windowsOnly = false)
{
    if (windowsOnly && !OperatingSystem.IsWindows()) { Console.WriteLine($"SKIP {name} (Windows)"); skipped++; return; }
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}\n{ex}"); }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection"); }
string Dir() { var d = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
string Old(string d, string name = "old.tmp") { var p = Path.Combine(d, name); File.WriteAllText(p, "preserve this content"); File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-10)); return p; }
(StateStore Store, CleanerService Cleaner, string Temp) Fixture()
{ var d = Dir(); var tmp = Path.Combine(d, "Temp"); Directory.CreateDirectory(tmp); var store = new StateStore(Path.Combine(d, "State")); return (store, new(store, tmp), tmp); }
Process Child()
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(typeof(GameProfile).Assembly.Location.Replace("SoftwareGamer.Core.dll", "SoftwareGamer.Tests.dll"));
    start.ArgumentList.Add("--hold");
    return Process.Start(start)!;
}

Test("path containment rejects sibling prefix", () => Assert(!CleanupPolicy.IsWithin(Path.Combine(root, "Temp"), Path.Combine(root, "Temp-other", "x.tmp"))));
Test("path containment rejects traversal", () => Assert(!CleanupPolicy.IsWithin(Path.Combine(root, "Temp"), Path.Combine(root, "Temp", "..", "x.tmp"))));
Test("root itself is not a candidate", () => Assert(!CleanupPolicy.IsWithin(root, root)));
Test("old temporary file eligible", () => Assert(CleanupPolicy.Eligible(root, Path.Combine(root, "x.tmp"), DateTimeOffset.UtcNow.AddDays(-8), DateTimeOffset.UtcNow)));
Test("recent file blocked", () => Assert(!CleanupPolicy.Eligible(root, Path.Combine(root, "x.tmp"), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow)));
Test("non-temporary data blocked", () => Assert(!CleanupPolicy.Eligible(root, Path.Combine(root, "save.dat"), DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow)));
Test("save folders excluded", () => Assert(!CleanupPolicy.Eligible(root, Path.Combine(root, "saves", "x.tmp"), DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow)));
Test("expired scan blocked", () => { var id = new FileIdentity(1, 1, 5, DateTime.UtcNow.AddDays(-10).ToFileTimeUtc(), 1); Throws(() => CleanupPolicy.ValidatePlan(new(root, Path.Combine(root,"x.tmp"), id, DateTimeOffset.UtcNow.AddMinutes(-11)), id, DateTimeOffset.UtcNow)); });
Test("changed identity blocked", () => { var id = new FileIdentity(1, 1, 5, DateTime.UtcNow.AddDays(-10).ToFileTimeUtc(), 1); Throws(() => CleanupPolicy.ValidatePlan(new(root, Path.Combine(root,"x.tmp"), id, DateTimeOffset.UtcNow), id with { FileId = 2 }, DateTimeOffset.UtcNow)); });
Test("external setting changes are preserved", () => Assert(!RestorePolicy.CanRestore("user-new", "app-set")));
Test("owned settings can restore", () => Assert(RestorePolicy.CanRestore("app-set", "app-set")));
Test("state roundtrip retains absence and pending records", () => { var s = new StateStore(Dir()); s.Use(x => { x.Changes.Add(new() { Before = null, Kind = "StartupRecovery" }); return true; }); Assert(s.Read().Changes.Single().Before is null && s.Read().Changes.Single().Pending); });
Test("invalid journal fails closed", () => { var d=Dir(); File.WriteAllText(Path.Combine(d,"state.json"),"{"); Throws(() => new StateStore(d).Read()); });
Test("future schema fails closed", () => { var d=Dir(); File.WriteAllText(Path.Combine(d,"state.json"),"{\"SchemaVersion\":999}"); Throws(() => new StateStore(d).Read()); });
Test("state concurrent updates do not lose records", () => { var s = new StateStore(Dir()); Parallel.For(0, 30, i => s.Use(x => { x.Profiles.Add(new() { Name=i.ToString() }); return true; })); Assert(s.Read().Profiles.Count==30); });

Test("scan includes old tmp only", () => { var f=Fixture(); Old(f.Temp); Old(f.Temp,"save.dat"); File.WriteAllText(Path.Combine(f.Temp,"fresh.tmp"),"new"); var scan=f.Cleaner.Scan(); Assert(scan.Files.Count==1, string.Join(";",scan.Notes)); }, true);
Test("quarantine and restore preserve content and identity", () => { var f=Fixture(); var p=Old(f.Temp); var item=f.Cleaner.Scan().Files.Single(); var m=f.Cleaner.Quarantine([item]); Assert(!File.Exists(p), string.Join(";",m)); Assert(f.Store.Read().PurgedBytes==0); var q=f.Store.Read().Quarantine.Single(); f.Cleaner.Restore(q.Id); Assert(File.ReadAllText(p)=="preserve this content"); using var h=WindowsFile.Open(f.Temp,p,false); Assert(h.Identity==item.Identity); }, true);
Test("recreated original is never overwritten", () => { var f=Fixture(); var p=Old(f.Temp); f.Cleaner.Quarantine(f.Cleaner.Scan().Files); File.WriteAllText(p,"new user data"); Throws(()=>f.Cleaner.Restore(f.Store.Read().Quarantine.Single().Id)); Assert(File.ReadAllText(p)=="new user data"); },true);
Test("purge only deletes selected quarantine item", () => { var f=Fixture(); Old(f.Temp); var keep=Old(f.Temp,"keep.tmp"); f.Cleaner.Quarantine(f.Cleaner.Scan().Files.Where(x=>x.Name=="old.tmp")); var q=f.Store.Read().Quarantine.Single(); f.Cleaner.Purge(q.Id); Assert(File.Exists(keep)); Assert(!File.Exists(Path.Combine(f.Store.QuarantinePath,q.StoredName))); Assert(f.Store.Read().PurgedBytes==q.Identity.Length); },true);
Test("file in use is never forced closed", () => { var f=Fixture(); var p=Old(f.Temp); var items=f.Cleaner.Scan().Files; using var open=File.Open(p,FileMode.Open,FileAccess.ReadWrite,FileShare.None); f.Cleaner.Quarantine(items); Assert(File.Exists(p)); },true);
Test("modified file after scan rejected", () => { var f=Fixture(); var p=Old(f.Temp); var items=f.Cleaner.Scan().Files; File.AppendAllText(p,"changed"); f.Cleaner.Quarantine(items); Assert(File.Exists(p) && f.Store.Read().Quarantine.Count==0); },true);
Test("symlink cannot reach external sentinel", () => { var f=Fixture(); var external=Dir(); var p=Old(external); Directory.CreateSymbolicLink(Path.Combine(f.Temp,"link"),external); Assert(f.Cleaner.Scan().Files.Count==0); Throws(()=>WindowsFile.Open(f.Temp,Path.Combine(f.Temp,"link","old.tmp"),true)); Assert(File.Exists(p)); },true);
Test("replacement of ancestor after scan rejected", () => { var f=Fixture(); var sub=Path.Combine(f.Temp,"sub"); Directory.CreateDirectory(sub); Old(sub); var scan=f.Cleaner.Scan(); Directory.Move(sub,Path.Combine(f.Temp,"old-sub")); var external=Dir(); var p=Old(external); Directory.CreateSymbolicLink(sub,external); f.Cleaner.Quarantine(scan.Files); Assert(File.Exists(p) && f.Store.Read().Quarantine.Count==0); },true);
Test("hardlinked files rejected", () => { var f=Fixture(); var p=Old(f.Temp); Assert(Native.CreateHardLinkW(Path.Combine(f.Temp,"linked.tmp"),p,IntPtr.Zero)); Assert(f.Cleaner.Scan().Files.Count==0); },true);
Test("readonly file excluded", () => { var f=Fixture(); var p=Old(f.Temp); File.SetAttributes(p,FileAttributes.ReadOnly); Assert(f.Cleaner.Scan().Files.Count==0); File.SetAttributes(p,FileAttributes.Normal); },true);
Test("unknown root rejected at execution", () => { var f=Fixture(); var other=Dir(); var p=Old(other); using var h=WindowsFile.Open(other,p,false); var item=new CleanupCandidate(other,p,h.Identity,DateTimeOffset.UtcNow); f.Cleaner.Quarantine([item]); Assert(File.Exists(p) && f.Store.Read().Quarantine.Count==0); },true);
Test("quarantine budget enforced", () => { var f=Fixture(); Old(f.Temp); f.Store.Use(s=>{s.Quarantine.Add(new(){Status="Quarantined",Identity=new(0,0,CleanupPolicy.QuarantineLimit,0,1)});return true;}); f.Cleaner.Quarantine(f.Cleaner.Scan().Files); Assert(File.Exists(Path.Combine(f.Temp,"old.tmp"))); },true);
Test("power APIs enumerate and read without mutation", () => { Assert(Guid.TryParse(WindowsSystem.ActivePowerScheme(),out _)); Assert(WindowsSystem.PowerSchemes().Count>0); },true);
Test("priority restores own child process", () =>
{
    using var child=Child(); var s=new StateStore(Dir()); var service=new BoosterService(s);
    try
    {
        child.PriorityClass=ProcessPriorityClass.Normal; var path=child.MainModule!.FileName;
        var game=new GameProcess(child.Id,child.StartTime.ToUniversalTime().Ticks,"test",path);
        service.Apply(new(){ExecutablePath=path,ExecutableHash=WindowsSystem.ExecutableHash(path),AboveNormal=true},game,Environment.ProcessPath!);
        Assert(child.PriorityClass==ProcessPriorityClass.AboveNormal);
        service.RestoreAll(); Assert(child.PriorityClass==ProcessPriorityClass.Normal && !s.Read().Changes.Any(c=>c.Pending));
    }
    finally { service.RestoreAll(); if(!child.HasExited)child.Kill(); }
},true);
Test("priority restoration preserves an external change", () =>
{
    using var child=Child(); var s=new StateStore(Dir()); var service=new BoosterService(s);
    try
    {
        child.PriorityClass=ProcessPriorityClass.Normal; var path=child.MainModule!.FileName;
        var game=new GameProcess(child.Id,child.StartTime.ToUniversalTime().Ticks,"test",path);
        service.Apply(new(){ExecutablePath=path,ExecutableHash=WindowsSystem.ExecutableHash(path),AboveNormal=true},game,Environment.ProcessPath!);
        child.PriorityClass=ProcessPriorityClass.Idle; service.RestoreAll();
        Assert(child.PriorityClass==ProcessPriorityClass.Idle && s.Read().Changes.Single().Status=="Conflict");
        service.KeepCurrent(s.Read().Changes.Single().Id); Assert(!s.Read().Changes.Any(c=>c.Pending));
    }
    finally { if(!child.HasExited)child.Kill(); }
},true);
Test("PID reuse identity mismatch returns no process", () => { using var self=Process.GetCurrentProcess(); Assert(WindowsSystem.OpenInstance($"{self.Id}:1") is null); },true);
Console.WriteLine($"RESULT passed={passed} failed={failed} skipped={skipped}");
Console.WriteLine($"Test fixtures: {root}");
return failed==0 ? 0 : 1;

static class Native
{
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateHardLinkW(string newName,string existing,IntPtr security);
}
