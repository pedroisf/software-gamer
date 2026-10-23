using System.Net;
using System.Text.Json;
using SoftwareGamer.Core;

static class UpdateTests
{
    public static void Run(Action<string, Action> test)
    {
        var current = new Version(0, 1, 1);
        void Assert(bool condition) { if (!condition) throw new Exception("Update assertion failed"); }
        void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected update failure"); }
        object Release(string tag = "v0.1.2", bool draft = false, bool preview = true, bool asset = true, string? download = null, string state = "uploaded") => new
        {
            tag_name = tag, draft, prerelease = preview,
            html_url = "https://untrusted.example/ignored",
            assets = asset ? new[] { new { name = "SoftwareGamer-Setup-x64.exe", state,
                browser_download_url = download ?? $"{AppRelease.RepositoryUrl}/releases/download/{tag}/SoftwareGamer-Setup-x64.exe" } } : []
        };
        AvailableUpdate? Find(params object[] releases) => UpdateClient.FindNewer(JsonSerializer.Serialize(releases), current);
        test("updates detect newer preview and derive official page", () => {
            var u = Find(Release()); Assert(u?.Version == new Version(0,1,2));
            Assert(u!.IsPrerelease && u.Page.AbsoluteUri == AppRelease.RepositoryUrl + "/releases/tag/v0.1.2");
        });
        test("updates do not offer same or older version", () => Assert(Find(Release("v0.1.1"), Release("v0.1.0")) is null));
        test("updates compare versions numerically regardless of order", () => Assert(Find(Release("v0.2.0"), Release("v0.10.0"), Release("v0.3.0"))?.Version == new Version(0,10,0)));
        test("updates ignore drafts and incomplete uploads", () => Assert(Find(Release(draft:true), Release(asset:false), Release(state:"starter")) is null));
        test("updates reject downloads outside the official repository", () => Assert(Find(Release(download:"https://github.com/other/repo/releases/download/v0.1.2/SoftwareGamer-Setup-x64.exe")) is null));
        test("updates ignore malformed and unsupported tags", () => Assert(Find(Release("nightly"), Release("v0.2.0-rc1"), Release("v0.2.0.1")) is null));
        test("updates handle empty and unexpected release entries", () => Assert(Find(new { something="else" }, "unexpected") is null && Find() is null));
        test("stable channel does not offer prereleases", () => Assert(UpdateClient.FindNewer(JsonSerializer.Serialize(new[] { Release("v2.0.0") }), new Version(1,0,0)) is null));
        test("preview channel accepts a newer stable release", () => Assert(Find(Release("v1.0.0", preview:false))?.IsPrerelease == false));
        test("malformed update response fails without inventing a version", () => Throws(()=>UpdateClient.FindNewer("not-json",current)));
        test("update preference defaults on and persists off", () => {
            var path=Path.Combine(Path.GetTempPath(),"SoftwareGamerUpdateTests",Guid.NewGuid().ToString("N"));
            var store=new StateStore(path); Assert(store.Read().CheckForUpdates);
            store.Use(s=>{s.CheckForUpdates=false;return true;}); Assert(!new StateStore(path).Read().CheckForUpdates);
        });
        test("update HTTP request is anonymous and limited to release metadata", () => {
            using var http=new HttpClient(new Stub((request,_)=> {
                Assert(request.RequestUri!.AbsoluteUri==AppRelease.FeedUrl && request.Method==HttpMethod.Get);
                Assert(request.Headers.Authorization is null && request.Content is null);
                Assert(request.Headers.UserAgent.ToString()=="SoftwareGamer/0.1.1");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(new[]{Release()})) });
            }));
            Assert(new UpdateClient(http).CheckAsync(current).GetAwaiter().GetResult()?.Version==new Version(0,1,2));
        });
        test("rate limit and offline errors are not treated as up to date", () => {
            using var http=new HttpClient(new Stub((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
            Throws(()=>new UpdateClient(http).CheckAsync(current).GetAwaiter().GetResult());
            using var offline=new HttpClient(new Stub((_,_)=>throw new HttpRequestException("offline")));
            Throws(()=>new UpdateClient(offline).CheckAsync(current).GetAwaiter().GetResult());
        });
        test("update cancellation stops an outstanding request", () => {
            using var cancel=new CancellationTokenSource();
            using var http=new HttpClient(new Stub(async (_,token)=>{cancel.Cancel();await Task.Delay(Timeout.Infinite,token);return new HttpResponseMessage(HttpStatusCode.OK);}));
            Throws(()=>new UpdateClient(http).CheckAsync(current,cancel.Token).GetAwaiter().GetResult());
        });
        test("oversized update response is rejected", () => {
            using var http=new HttpClient(new Stub((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new ByteArrayContent(new byte[4*1024*1024+1])})));
            Throws(()=>new UpdateClient(http).CheckAsync(current).GetAwaiter().GetResult());
        });
    }
    private sealed class Stub(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => send(request,token);
    }
}
