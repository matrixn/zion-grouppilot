using System.Text;
using System.Text.Json;
using GroupPilot.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form { Text = "GroupPilot local browser verification", Width = 900, Height = 650,
            ShowInTaskbar = false, Opacity = 0 };
        using var view = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Shown += async (_, _) =>
        {
            try
            {
                await VerifyAsync(view).WaitAsync(TimeSpan.FromSeconds(45));
                Console.WriteLine("PASS: WebView2 local publishing fixture (insertion, single click, confirmation, approval, uncertain result, restriction).");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { form.Close(); }
        };
        Application.Run(form);
        return Environment.ExitCode;
    }

    private static async Task VerifyAsync(WebView2 view)
    {
        // This profile is isolated from GroupPilot. Every resource is served locally: no Facebook request can leave the fixture.
        var profile = Directory.CreateTempSubdirectory("grouppilot-browser-smoke-").FullName;
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
        await view.EnsureCoreWebView2Async(environment);
        var core = view.CoreWebView2;
        var html = "";
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) => args.Response = environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK", "Content-Type: text/html; charset=utf-8");
        const string groupUrl = "https://www.facebook.com/groups/local-verification/";
        const string postUrl = "https://www.facebook.com/local-source/posts/123/";
        async Task Navigate(string outcome)
        {
            html = """
                <!doctype html><html><body><div role="dialog"><h2>Create post</h2>
                <div role="textbox" contenteditable="true" style="width:600px;height:100px"></div>
                <button onclick="window.clicks=(window.clicks||0)+1;
                if(window.outcome){const signal=document.createElement('div');signal.role='status';signal.textContent=window.outcome;document.body.append(signal)}
                this.closest('[role=dialog]').remove()">Post</button></div></body></html>
                """;
            var loaded = new TaskCompletionSource();
            void Complete(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (args.IsSuccess) loaded.TrySetResult();
                else loaded.TrySetException(new Exception("Local fixture navigation failed."));
            }
            core.NavigationCompleted += Complete;
            try { core.Navigate(groupUrl); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { core.NavigationCompleted -= Complete; }
            await core.ExecuteScriptAsync($"window.outcome={JsonSerializer.Serialize(outcome)}");
        }
        async Task Expect(string operation, string expected)
        {
            var request = JsonSerializer.Serialize(new { operation, token = "fixture-only", groupUrl, postUrl });
            using var response = JsonDocument.Parse(await core.ExecuteScriptAsync(FacebookPublishingScript.Source + $"({request})"));
            var status = response.RootElement.GetProperty("status").GetString();
            if (status != expected) throw new Exception($"{operation}: expected {expected}, got {response.RootElement}");
        }
        foreach (var scenario in new[] { ("Your post was published.", "published"), ("Your post is pending approval.", "approval"), ("", "waiting") })
        {
            await Navigate(scenario.Item1);
            await Expect("prepare", "waiting");
            await Expect("prepare", "ready"); // Uses Chromium's real contenteditable/execCommand, not the jsdom shim.
            await Expect("submit", "submitted");
            await Expect("submit", "uncertain");
            if (await core.ExecuteScriptAsync("window.clicks") != "1") throw new Exception("Publish was clicked more than once.");
            await Expect("observe", scenario.Item2);
        }
        await Navigate("");
        await core.ExecuteScriptAsync("document.body.insertAdjacentHTML('beforeend','<div role=alert>You are temporarily blocked</div>')");
        await Expect("prepare", "restricted");
    }
}
