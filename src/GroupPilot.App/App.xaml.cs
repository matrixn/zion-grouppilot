using GroupPilot.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;

namespace GroupPilot_App;

public partial class App : Application
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GroupPilot");

    private MainWindow? window;
    private ServiceProvider? services;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Directory.CreateDirectory(DataDirectory);
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(DataDirectory, "logs", "app-.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(DataDirectory, "grouppilot.db") };
        services = new ServiceCollection()
            .AddDbContextFactory<PilotDb>(options => options.UseSqlite(connection.ToString()))
            .AddSingleton<Storage>()
            .AddSingleton<PilotViewModel>()
            .AddSingleton<MainWindow>()
            .BuildServiceProvider();

        window = services.GetRequiredService<MainWindow>();
        window.Closed += (_, _) =>
        {
            services.Dispose();
            Log.CloseAndFlush();
        };
        window.Activate();
    }
}
