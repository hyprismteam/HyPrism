using System.Text;

namespace HyPrism.IpcGen;

/// <summary>
/// Emits <c>Frontend/src/lib/ipc.ts</c> from a resolved list of
/// <see cref="IpcMethod"/> descriptors and accumulated <see cref="TsInterface"/> definitions.
/// <para>Output structure (in order):</para>
/// <list type="number">
///   <item>ASCII banner + auto-generated warning</item>
///   <item>Electron boilerplate + exported <c>send</c>, <c>on</c>, <c>invoke&lt;T&gt;</c> helpers</item>
///   <item>TypeScript <c>export interface</c> blocks (one per C# record / class)</item>
///   <item>Per-domain <c>const _domain = { ... }</c> objects</item>
///   <item>Unified <c>export const ipc = { ... }</c> and backwards-compat type aliases</item>
/// </list>
/// </summary>
internal static class TypeScriptEmitter
{
    // Domains whose names conflict with JS globals
    private static readonly Dictionary<string, string> DomainAliases = new()
    {
        ["window"]  = "windowCtl",
        ["console"] = "consoleCtl",
    };

    public static string Emit(List<IpcMethod> methods, List<TsInterface> interfaces)
    {
        var sb = new StringBuilder();

        EmitHeader(sb);
        EmitCoreHelpers(sb);
        EmitInterfaces(sb, interfaces);
        EmitDomains(sb, methods);
        EmitExport(sb, methods);

        return sb.ToString();
    }

    #region Private emit helpers

    private static void EmitHeader(StringBuilder sb)
    {
        sb.AppendLine("""
            /*
             .-..-.      .---.       _
             : :; :      : .; :     :_;
             :    :.-..-.:  _.'.--. .-. .--. ,-.,-.,-.
             : :: :: :; :: :   : ..': :`._-.': ,. ,. :
             :_;:_;`._. ;:_;   :_;  :_; `.__.':_;:_;:_;
                    .-. :
                    `._.'             HyPrism.IpcGen (Roslyn)

             AUTO-GENERATED — DO NOT EDIT BY HAND.
             Source of truth: IpcService.cs  (attributes + method signatures)
             Re-generate: dotnet build  (target GenerateIpcTs)
            */

            """);
    }

    private static void EmitCoreHelpers(StringBuilder sb)
    {
        sb.AppendLine("""
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            declare global {
              interface Window {
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                require: (module: string) => any;
              }
            }

            const { ipcRenderer } = window.require('electron');

            export function send(channel: string, data?: unknown): void {
              ipcRenderer.send(channel, JSON.stringify(data));
            }

            export function on<T>(channel: string, cb: (data: T) => void): () => void {
              const handler = (_: unknown, raw: string) => {
                try { cb(JSON.parse(raw) as T); } catch { /* ignore */ }
              };
              ipcRenderer.on(channel, handler);
              return () => ipcRenderer.removeListener(channel, handler);
            }

            export function invoke<T>(channel: string, data?: unknown, timeoutMs = 10_000): Promise<T> {
              return new Promise<T>((resolve, reject) => {
                const replyChannel = channel + ':reply';
                let done = false;

                const timer = timeoutMs > 0
                  ? setTimeout(() => {
                      if (!done) { done = true; reject(new Error(`IPC timeout: ${channel}`)); }
                    }, timeoutMs)
                  : null;

                ipcRenderer.once(replyChannel, (_: unknown, raw: string) => {
                  if (done) return;
                  done = true;
                  if (timer !== null) clearTimeout(timer);
                  try { resolve(JSON.parse(raw) as T); } catch (e) { reject(e); }
                });

                send(channel, data);
              });
            }

            """);
    }

    private static void EmitInterfaces(StringBuilder sb, List<TsInterface> interfaces)
    {
        foreach (var iface in interfaces)
        {
            sb.AppendLine($"export interface {iface.Name} {{");
            foreach (var field in iface.Fields)
            {
                var opt = field.Optional ? "?" : "";
                sb.AppendLine($"  {field.Name}{opt}: {field.TsType};");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    private static void EmitDomains(StringBuilder sb, List<IpcMethod> methods)
    {
        var grouped = methods
            .GroupBy(m => m.Channel.Split(':')[1])
            .OrderBy(g => g.Key);

        foreach (var domain in grouped)
        {
            var varName = DomainAliases.TryGetValue(domain.Key, out var alias)
                ? $"_{alias}"
                : $"_{domain.Key}";

            sb.AppendLine($"const {varName} = {{");

            foreach (var m in domain)
            {
                var action = m.Channel.Split(':')[2];
                EmitMethod(sb, m, action);
            }

            sb.AppendLine("};");
            sb.AppendLine();
        }
    }

    private static void EmitMethod(StringBuilder sb, IpcMethod m, string action)
    {
        switch (m.Kind)
        {
            case IpcKind.Invoke:
            {
                var inputParam = m.InputTsType is not null ? $"data: {m.InputTsType}" : "";
                var inputArg   = m.InputTsType is not null ? "data" : "{}";
                var timeout    = m.TimeoutMs != 10_000 ? $", {m.TimeoutMs}" : "";
                sb.AppendLine(
                    $"  {action}: ({inputParam}) => invoke<{m.ResponseTsType}>('{m.Channel}', {inputArg}{timeout}),");
                break;
            }
            case IpcKind.Send:
            {
                var inputParam = m.InputTsType is not null ? $"data: {m.InputTsType}" : "";
                var inputArg   = m.InputTsType is not null ? "data" : "";
                sb.AppendLine(
                    $"  {action}: ({inputParam}) => send('{m.Channel}'{(inputArg.Length > 0 ? ", " + inputArg : "")}),");
                break;
            }
            case IpcKind.Event:
            {
                var capAction = char.ToUpperInvariant(action[0]) + action[1..];
                sb.AppendLine(
                    $"  on{capAction}: (cb: (data: {m.ResponseTsType}) => void) => on<{m.ResponseTsType}>('{m.Channel}', cb),");
                break;
            }
        }
    }

    private static void EmitExport(StringBuilder sb, List<IpcMethod> methods)
    {
        var domains = methods
            .Select(m => m.Channel.Split(':')[1])
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        sb.AppendLine("export const ipc = {");
        foreach (var domain in domains)
        {
            var varName = DomainAliases.TryGetValue(domain, out var alias) ? alias : domain;
            var localVar = $"_{varName}";
            sb.AppendLine($"  {varName}: {localVar},");
        }
        sb.AppendLine("};");
        sb.AppendLine();

        // Backwards-compatible type aliases for well-known renamed types
        sb.AppendLine("// Type aliases (C# model names → frontend-expected names)");
        sb.AppendLine("export type Profile = IpcProfile;");
        sb.AppendLine("export type NewsItem = NewsItemResponse;");
        sb.AppendLine("export type ModScreenshot = CurseForgeScreenshot;");
    }
    #endregion
}