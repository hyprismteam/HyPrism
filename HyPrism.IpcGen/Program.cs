using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using HyPrism.IpcGen;

// MSBuildLocator must be called before any Roslyn MSBuild API is touched.
MSBuildLocator.RegisterDefaults();

#region CLI args
string? projectPath = null;
string? outputPath  = null;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--project" && i + 1 < args.Length) projectPath = args[++i];
    if (args[i] == "--output"  && i + 1 < args.Length) outputPath  = args[++i];
}

if (projectPath is null || outputPath is null)
{
    Console.Error.WriteLine("Usage: HyPrism.IpcGen --project <HyPrism.csproj> --output <ipc.ts>");
    return 1;
}

#endregion

#region Hash-based cache
var ipcServicePath = Path.Combine(Path.GetDirectoryName(projectPath)!,
    "Services", "Core", "Ipc", "IpcService.cs");

var hashFile = Path.Combine(Path.GetDirectoryName(outputPath)!, ".ipcgen.hash");

if (File.Exists(ipcServicePath) && File.Exists(outputPath))
{
    var currentHash = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(ipcServicePath)));

    if (File.Exists(hashFile) && File.ReadAllText(hashFile).Trim() == currentHash)
    {
        Console.WriteLine("[IpcGen] ipc.ts is up-to-date — skipping.");
        return 0;
    }
}

#endregion

#region Load the project via Roslyn MSBuildWorkspace
Console.WriteLine("[IpcGen] Loading project...");

using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
{
    // Suppress design-time build warnings that are harmless here
    ["DesignTimeBuild"] = "true",
});

workspace.RegisterWorkspaceFailedHandler(e =>
    Console.Error.WriteLine($"[IpcGen] workspace warning: {e.Diagnostic.Message}"));

var project = await workspace.OpenProjectAsync(projectPath);
var compilation = await project.GetCompilationAsync();

if (compilation is null)
{
    Console.Error.WriteLine("[IpcGen] Could not compile project.");
    return 1;
}

#endregion

#region Collect IPC methods and map types
Console.WriteLine("[IpcGen] Collecting IPC handlers...");

var mapper    = new CSharpTypeMapper();
var collector = new IpcMethodCollector(compilation, mapper);
var methods   = collector.Collect();

if (methods.Count == 0)
{
    Console.Error.WriteLine("[IpcGen] No [IpcInvoke/IpcSend/IpcEvent] methods found. " +
        "Did IpcService inherit from IpcServiceBase?");
    return 1;
}

mapper.FlushQueue();

Console.WriteLine($"[IpcGen] Found {methods.Count} channels, " +
    $"{mapper.Interfaces.Count} TypeScript interfaces.");

#endregion

#region Generate ipc.ts
Console.WriteLine($"[IpcGen] Writing {outputPath}...");

var ts = TypeScriptEmitter.Emit(methods, mapper.Interfaces);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, ts);

// Update hash cache
if (File.Exists(ipcServicePath))
{
    var newHash = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(ipcServicePath)));
    File.WriteAllText(hashFile, newHash);
}

Console.WriteLine("[IpcGen] Done.");
return 0;

#endregion
