using System.Text;
using System.Text.Json;
using GroupPilot.Core;
using GroupPilot.Infrastructure;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "GroupPilot local Playwright verification", Width = 900, Height = 650,
            ShowInTaskbar = false, Opacity = 0 };
        using var view = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Shown += async (_, _) =>
        {
            try { await VerifyAsync(view).WaitAsync(TimeSpan.FromSeconds(120)); }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { form.Close(); }
        };
        Application.Run(form);
        return Environment.ExitCode;
    }

    private static async Task VerifyAsync(WebView2 view)
    {
        var profile = Directory.CreateTempSubdirectory("grouppilot-playwright-smoke-").FullName;
        var port = WebViewAutomationConnection.FindLoopbackPort();
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile,
            options: new() { AdditionalBrowserArguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={port}" });
        await view.EnsureCoreWebView2Async(environment);
        var core = view.CoreWebView2;
        var scenario = "success";
        var clicks = 0;
        var trustedClicks = 0;
        var reloads = 0;
        var submitted = false;
        const string groupUrl = "https://www.facebook.com/groups/local-verification/";
        const string postUrl = "https://www.facebook.com/local-source/posts/123/";
        const string article = "<article><a href='" + postUrl + "'>Source</a><a aria-label='Just now' href='" + groupUrl + "posts/456/'>time</a></article>";
        // Every resource is served locally using an isolated profile. No Facebook request leaves this fixture.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebMessageReceived += (_, args) =>
        {
            if (args.TryGetWebMessageAsString() is { } message && message.StartsWith("submit:"))
            {
                clicks++; submitted = true;
                if (message == "submit:trusted") trustedClicks++;
            }
        };
        core.WebResourceRequested += (_, args) =>
        {
            var hasResult = submitted && scenario == "reload";
            if (hasResult) reloads++;
            var confirmation = scenario switch { "success" or "delayed" or "disabled" => "Your post was published.", "approval" => "Your post is pending approval.", _ => "" };
            var composer = "<div role='dialog'><h2>Create post</h2><div role='textbox' contenteditable='true' style='width:600px;height:100px'>" +
                (scenario == "occupied" ? "An unrelated draft" : "") + "</div><button id='publish' " + (scenario == "disabled" ? "disabled" : "") + ">Post</button></div>";
            var html = "<!doctype html><html><body><main>" + (hasResult ? article : scenario == "delayed"
                ? "<button id='entry'>Write something...</button>" : composer) + "</main><script>" +
                "function bind(){document.querySelector('#publish')?.addEventListener('click',e=>{" +
                "chrome.webview.postMessage('submit:'+(e.isTrusted?'trusted':'synthetic'));" +
                "e.target.closest('[role=dialog]').remove();" +
                (confirmation.Length > 0 ? "const status=document.createElement('div');status.role='status';status.textContent=" + JsonSerializer.Serialize(confirmation) + ";document.body.append(status);" : "") +
                (scenario == "feed" ? "document.querySelector('main').insertAdjacentHTML('beforeend'," + JsonSerializer.Serialize(article) + ");" : "") + "});}" +
                (scenario == "delayed" ? "document.querySelector('#entry').onclick=()=>{document.querySelector('main').innerHTML='<div role=dialog>Loading...</div>';setTimeout(()=>{document.querySelector('main').innerHTML=" + JsonSerializer.Serialize(composer) + ";bind()},350)};" : "bind();") +
                (scenario == "disabled" ? "setTimeout(()=>document.querySelector('#publish').disabled=false,600);" : "") +
                (scenario == "restricted" ? "document.body.insertAdjacentHTML('beforeend','<div role=alert>You are temporarily blocked</div>');" : "") +
                "</script></body></html>";
            args.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK", "Content-Type: text/html; charset=utf-8");
        };
        using var targetInfo = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.getTargetInfo", "{}"));
        using var connection = new WebViewAutomationConnection();
        var page = await connection.ConnectAsync(port, targetInfo.RootElement.GetProperty("targetInfo").GetProperty("targetId").GetString()!);
        var publication = new CampaignPublication(1, 1, 1, groupUrl, "Local fixture", postUrl);
        foreach (var test in new[] { ("success", TargetStatus.Published), ("approval", TargetStatus.AwaitingApproval),
            ("feed", TargetStatus.Published), ("reload", TargetStatus.Published), ("delayed", TargetStatus.Published),
            ("disabled", TargetStatus.Published), ("occupied", TargetStatus.Failed), ("restricted", TargetStatus.Failed), ("unknown", TargetStatus.NeedsReview) })
        {
            scenario = test.Item1; clicks = 0; trustedClicks = 0; submitted = false; reloads = 0;
            var checkpoints = 0;
            var stages = new List<string>();
            var publisher = new PlaywrightGroupPublisher(page, stages.Add, new(5000, 3500, 750, 1200));
            var outcome = await publisher.PublishAsync(publication, () => { checkpoints++; return Task.CompletedTask; }, default);
            if (outcome.Status != test.Item2) throw new Exception($"{scenario}: expected {test.Item2}, got {outcome}. Stages: {string.Join("; ", stages)}");
            var expectedClicks = test.Item2 == TargetStatus.Failed ? 0 : 1;
            if (clicks != expectedClicks || trustedClicks != expectedClicks || checkpoints != expectedClicks)
                throw new Exception($"{scenario}: clicks={clicks}, trusted={trustedClicks}, checkpoints={checkpoints}");
            if (scenario == "reload" && reloads == 0) throw new Exception("The result was not verified after reloading.");
            Console.WriteLine($"PASS {scenario}: {outcome.Status}; trusted clicks={trustedClicks}");
        }
        scenario = "reload"; submitted = true; clicks = 0;
        var recovered = await new PlaywrightGroupPublisher(page, timeouts: new(2500)).VerifyAsync(publication, DateTimeOffset.UtcNow.AddSeconds(-30), default);
        if (recovered?.Status != TargetStatus.Published || clicks != 0) throw new Exception("Read-only recovery failed.");
        Console.WriteLine("PASS recovery: existing group post verified without clicking Publish");
    }
}
