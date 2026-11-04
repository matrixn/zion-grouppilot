using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;

namespace GroupPilot.Infrastructure;

public sealed class WebViewAutomationConnection : IDisposable
{
    private IPlaywright? driver;

    public static int FindLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async Task<IPage> ConnectAsync(int port, string expectedTargetId)
    {
        if (driver is not null)
            throw new InvalidOperationException("This automation connection is already in use.");
        driver = await Playwright.CreateAsync();
        var browser = await driver.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}", new() { Timeout = 15000 });
        // Match the target ID obtained directly from the app's own WebView, never the first available tab.
        foreach (var context in browser.Contexts)
        foreach (var page in context.Pages)
        {
            var session = await context.NewCDPSessionAsync(page);
            try
            {
                var info = await session.SendAsync("Target.getTargetInfo");
                if (info?.GetProperty("targetInfo").GetProperty("targetId").GetString() == expectedTargetId)
                    return page;
            }
            finally { await session.DetachAsync(); }
        }
        throw new InvalidOperationException("Playwright could not identify the app's Facebook WebView. No other page was selected.");
    }

    // Disposing the driver disconnects automation; it must not close the user's WebView/browser context.
    public void Dispose() { driver?.Dispose(); driver = null; }
}
