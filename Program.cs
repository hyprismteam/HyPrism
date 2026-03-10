using ElectronNET;
using ElectronNET.API;
using ElectronNET.API.Entities;
using HyPrism.Services.Core.Infrastructure;
using HyPrism.Services.Core.Ipc;
using HyPrism.Services.Game.Instance;
using HyPrism.Services.User;
using Microsoft.Extensions.DependencyInjection;

using Serilog;
using System.Runtime;
using System.Text;

namespace HyPrism;

class Program
{
    static async Task Main(string[] args)
    {
        // Memory optimization
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GCSettings.LatencyMode = GCLatencyMode.Interactive;

        // Initialize Logger
        var appDir = UtilityService.GetEffectiveAppDir();
        var logsDir = Path.Combine(appDir, "Logs");
        Directory.CreateDirectory(logsDir);

        var logFileName = $"{DateTime.Now:dd-MM-yyyy_HH-mm-ss}.log";
        var logFilePath = Path.Combine(logsDir, logFileName);

        try
        {
            File.WriteAllText(logFilePath, """
 .-..-.      .---.       _                
 : :; :      : .; :     :_;               
 :    :.-..-.:  _.'.--. .-. .--. ,-.,-.,-.
 : :: :: :; :: :   : ..': :`._-.': ,. ,. :
 :_;:_;`._. ;:_;   :_;  :_;`.__.':_;:_;:_;
        .-. :                             
        `._.'                     launcher

""" + Environment.NewLine);
        }
        catch { /* Ignore */ }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.File(
                path: logFilePath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                retainedFileCountLimit: 20
            )
            .CreateLogger();

        // Intercept Console.Out/Error FIRST — before anything touches
        // ElectronNetRuntime, because the RuntimeController getter itself
        // writes diagnostic messages (GatherBuildInfo, Probe scored, etc.)
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        Logger.CaptureOriginalConsole();
        Console.SetOut(new ElectronLogInterceptor(originalOut, isError: false));
        Console.SetError(new ElectronLogInterceptor(originalErr, isError: true));

        // Now safe to access the runtime controller
        var runtimeController = ElectronNetRuntime.RuntimeController;

        try
        {
            Logger.Info("Boot", "Starting HyPrism (Electron.NET)...");
            Logger.Info("Boot", $"App Directory: {appDir}");

            // Initialize DI container
            var services = Bootstrapper.Initialize();
            
            // Perform async initialization (fetch CurseForge key if needed)
            await Bootstrapper.InitializeAsync(services);

            ElectronNetRuntime.ElectronExtraArguments = string.Join(" ",
                "--in-process-gpu",
                "--num-raster-threads=1",
                "--renderer-process-limit=1",
                "--disable-features=SpareRendererForSitePerProcess,BackForwardCache,Translate,AutofillServerCommunication",
                "--disable-background-networking",
                "--aggressive-cache-discard",
                "--disable-dev-shm-usage",
                "--js-flags=--max-old-space-size=256"
            );

            // Start Electron runtime and wait for socket bridge
            Logger.Info("Boot", "Starting Electron runtime...");
            await runtimeController.Start();
            await runtimeController.WaitReadyTask;
            Logger.Info("Boot", "Electron runtime ready");

            // Create window & register IPC
            await ElectronBootstrap(services);

            // Keep alive until Electron quits
            await runtimeController.WaitStoppedTask;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application crashed unexpectedly");
            Logger.Error("Crash", $"Application crashed: {ex.Message}");
            Console.WriteLine(ex.ToString());

            await runtimeController.Stop().ConfigureAwait(false);
            await runtimeController.WaitStoppedTask
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task ElectronBootstrap(IServiceProvider services)
    {
        var wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");

        static string? ResolveAppIconPath()
        {
            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "wwwroot", "icon.png"),
                Path.Combine(baseDir, "Build", "icon.png"),
                Path.Combine(baseDir, "icon.png"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "Build", "icon.png")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "Build", "icon.png")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "Build", "icon.png")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "icon.png")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Build", "icon.png")),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        // Register IPC handlers BEFORE creating window to ensure they're ready
        // when the frontend starts making IPC calls during initialization
        var ipcService = services.GetRequiredService<IpcService>();
        ipcService.RegisterAll();

        // Run instance migrations (delegated to the dedicated migration service)
        var migrationService = services.GetRequiredService<IInstanceMigrationService>();
        migrationService.MigrateLegacyData();
        migrationService.MigrateVersionFoldersToIdFolders();
        migrationService.MigrateBranchSubdirectoriesToFlat();

        // Repair legacy profile mods symlink/junction if present and ensure
        // mods are stored in instance-local UserData/Mods.
        var profileManagementService = services.GetRequiredService<IProfileManagementService>();
        profileManagementService.InitializeProfileModsSymlink();

        // Resolve icon path for the window
        // On Windows/Linux, BrowserWindowOptions.Icon sets the window icon.
        // On macOS, Icon is ignored by Electron; the dock icon must be set
        // programmatically via Electron.App.Dock.SetIcon().
        var iconPath = ResolveAppIconPath();

        #pragma warning disable 

        var mainWindow = await Electron.WindowManager.CreateWindowAsync(
            new BrowserWindowOptions
            {
                Width = 1280,
                Height = 800,
                MinWidth = 1024,
                MinHeight = 700,
                Frame = true,
                Show = false,
                Center = true,
                Title = "HyPrism",
                AutoHideMenuBar = true,
                BackgroundColor = "#0D0D10",
                Icon = iconPath ?? string.Empty,
                WebPreferences = new WebPreferences
                {
                    // DevTools protocol has a persistent overhead even when the panel is
                    // closed — disable it in production to free that memory.
                    DevTools = false,

                    // No WebGL usage in the frontend (confirmed — no three.js / pixi / etc.).
                    // Disabling prevents Chromium from initialising WebGL contexts.
                    Webgl = false,

                    // Webview tags allow embedding arbitrary web content inside the window.
                    // Not used; disabling removes the associated process isolation overhead.
                    WebviewTag = false,
                }
            },
            $"file://{Path.Combine(wwwroot, "index.html")}"
        );
        
        #pragma warning restore

        // Set macOS dock icon (BrowserWindowOptions.Icon is a no-op on macOS)
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                {
                    Electron.Dock.SetIcon(iconPath);
                    Logger.Info("Boot", $"macOS dock icon set to {iconPath}");
                }
                else
                {
                    Logger.Warning("Boot", "macOS dock icon not set: icon.png not found in expected app paths");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("Boot", $"Failed to set dock icon: {ex.Message}");
            }
        }

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            void NavigateTo(string page)
            {
                try
                {
                    var script = $"window.dispatchEvent(new CustomEvent('hyprism:menu:navigate', {{ detail: {{ page: '{page}' }} }}));";
                    _ = mainWindow.WebContents.ExecuteJavaScriptAsync<object>(script, false);
                }
                catch (Exception ex)
                {
                    Logger.Warning("Boot", $"Failed to dispatch menu navigation: {ex.Message}");
                }
            }

            var appMenu = new MenuItem[]
            {
                new()
                {
                    Label = "HyPrism",
                    Submenu = new[]
                    {
                        new MenuItem { Label = "Settings", Accelerator = "CommandOrControl+,", Click = () => NavigateTo("settings") },
                        new MenuItem { Label = "Instances", Accelerator = "CommandOrControl+2", Click = () => NavigateTo("instances") },
                        new MenuItem { Label = "About HyPrism", Click = () => NavigateTo("settings") },
                        new MenuItem { Label = "Quit HyPrism", Accelerator = "CommandOrControl+Q", Click = () => Electron.App.Quit() }
                    }
                },
                new()
                {
                    Label = "Edit",
                    Submenu = new[]
                    {
                        new MenuItem { Role = MenuRole.undo },
                        new MenuItem { Role = MenuRole.redo },
                        new MenuItem { Type = MenuType.separator },
                        new MenuItem { Role = MenuRole.cut },
                        new MenuItem { Role = MenuRole.copy },
                        new MenuItem { Role = MenuRole.paste },
                        new MenuItem { Role = MenuRole.selectAll }
                    }
                },
                new()
                {
                    Label = "Window",
                    Submenu = new[]
                    {
                        new MenuItem { Label = "Minimize", Accelerator = "CommandOrControl+M", Click = () => mainWindow.Minimize() },
                        new MenuItem { Label = "Close", Accelerator = "CommandOrControl+W", Click = () => mainWindow.Close() }
                    }
                }
            };

            Electron.Menu.SetApplicationMenu(appMenu);
        }
        else
        {
            Electron.Menu.SetApplicationMenu([]);
        }
        // Quit when all windows closed
        Electron.App.WindowAllClosed += () => Electron.App.Quit();

        // Show after ready
        mainWindow.OnReadyToShow += () =>
        {
            try
            {
                mainWindow.Center();
            }
            catch (Exception ex)
            {
                Logger.Warning("Boot", $"Failed to center window on startup: {ex.Message}");
            }
            mainWindow.Show();

            // Check for launcher updates after the window exists so IPC events can be delivered.
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1200);
                    var updateService = services.GetRequiredService<HyPrism.Services.Core.App.IUpdateService>();
                    await updateService.CheckForLauncherUpdatesAsync();
                }
                catch (Exception ex)
                {
                    Logger.Warning("Update", $"Startup update check failed: {ex.Message}");
                }
            });
        };

        Logger.Success("Boot", "Electron window created, IPC handlers registered");
    }
}

/// <summary>
/// Intercepts Console.Out / Console.Error to capture Electron.NET framework
/// messages (prefixed with <c>||</c>, <c>[StartCore]</c>, <c>[StartInternal]</c>,
/// <c>BridgeConnector</c> etc.) and routes them through <see cref="Logger"/>.
/// </summary>
file sealed class ElectronLogInterceptor : TextWriter
{
    private readonly TextWriter _original;
    private readonly bool _isError;

    // Noise patterns to suppress entirely
    private static readonly string[] SuppressPatterns =
    [
        "GetVSyncParametersIfAvailable()",
        "Passthrough is not supported",
        "viz.mojom.Compositor",
        "gpu_channel_manager",
        "sandboxed_process_launcher",
        "Fontconfig error",
        "Mesa warning",
        "MESA-LOADER",
        "libEGL warning",
        "DRI driver",
    ];

    // Patterns that indicate debug-level info
    private static readonly string[] DebugPatterns =
    [
        "[StartCore]",
        "[StartInternal]",
        "BridgeConnector",
        "Socket.IO",
        "engine.io",
        "DevTools listening",
        "GatherBuildInfo",
        "Probe scored",
        "launch origin",
        "testhost",
        "RuntimeController",
        "Evaluated StartupMethod",
        "package mode",
        "UnpackedDotnetFirst",
    ];

    // Patterns that indicate warnings
    private static readonly string[] WarningPatterns =
    [
        "ERROR:",
        "FATAL:",
        "(electron)",
        "Electron Helper",
        "crash",
    ];

    public ElectronLogInterceptor(TextWriter original, bool isError)
    {
        _original = original;
        _isError = isError;
    }

    public override Encoding Encoding => _original.Encoding;

    public override void WriteLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var line = value.Trim();

        // Strip "|| " prefix that Electron.NET adds
        if (line.StartsWith("|| "))
            line = line[3..];

        if (string.IsNullOrWhiteSpace(line))
            return;

        // Suppress noise
        foreach (var pattern in SuppressPatterns)
        {
            if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return;
        }

        // Route through Logger
        if (_isError || MatchesAny(line, WarningPatterns))
        {
            Logger.Warning("Electron", line, logToConsole: false);
        }
        else if (MatchesAny(line, DebugPatterns))
        {
            Logger.Debug("Electron", line);
        }
        else
        {
            Logger.Info("Electron", line, logToConsole: false);
        }
    }

    public override void Write(string? value)
    {
        // Electron.NET framework uses WriteLine predominantly;
        // buffer partial writes for a complete line
        if (!string.IsNullOrEmpty(value))
            WriteLine(value);
    }

    private static bool MatchesAny(string line, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
