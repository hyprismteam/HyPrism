using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElectronNET.API;
using HyPrism.Services.Core.Ipc.Attributes;
using HyPrism.Services.Core.Infrastructure;

namespace HyPrism.Services.Core.Ipc;

/// <summary>
/// Base class for IPC services.  Discovers all methods decorated with
/// [IpcInvoke], [IpcSend], and [IpcEvent] via reflection and wires them
/// to the Electron IPC bus automatically.
///
/// Handler method conventions:
///   [IpcInvoke("channel")] public T  Handle()           — no-arg invoke
///   [IpcInvoke("channel")] public T  Handle(TReq req)   — typed-arg invoke
///   [IpcInvoke("channel")] public Task&lt;T&gt; Handle(...)  — async invoke
///   [IpcSend("channel")]   public void Handle()          — fire-and-forget
///   [IpcSend("channel")]   public void Handle(TReq req)  — fire-and-forget with arg
///   [IpcEvent("channel")]  public void Subscribe(Action&lt;T&gt; emit) — push event
/// </summary>
public abstract class IpcServiceBase(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;

    private static int _registered;

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    #region Public entry point

    public void RegisterAll()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            Logger.Warning("IPC", "RegisterAll called more than once; skipping duplicate registration");
            return;
        }

        Logger.Info("IPC", "Registering IPC handlers...");

        var methods = GetType().GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var method in methods)
        {
            if (method.GetCustomAttribute<IpcInvokeAttribute>() is { } inv)
                BindInvoke(method, inv.Channel, inv.TimeoutMs);
            else if (method.GetCustomAttribute<IpcSendAttribute>() is { } snd)
                BindSend(method, snd.Channel);
            else if (method.GetCustomAttribute<IpcEventAttribute>() is { } evt)
                BindEvent(method, evt.Channel);
        }

        Logger.Success("IPC", "All IPC handlers registered");
    }

    #endregion

    #region Invoke  (request → reply)

    private void BindInvoke(MethodInfo method, string channel, int timeoutMs)
    {
        var replyChannel = channel + ":reply";
        var paramType = GetSingleParameterType(method);

        Electron.IpcMain.On(channel, async (args) =>
        {
            try
            {
                object? result = paramType is null
                    ? await InvokeMethodAsync(method, null)
                    : await InvokeMethodAsync(method, DeserializeArg(args, paramType));

                ReplyJson(replyChannel, result);
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"[{channel}] handler threw: {ex.Message}");
                ReplyRaw(replyChannel, "null");
            }
        });
    }

    #endregion

    #region Send  (fire-and-forget)

    private void BindSend(MethodInfo method, string channel)
    {
        var paramType = GetSingleParameterType(method);

        Electron.IpcMain.On(channel, (args) =>
        {
            try
            {
                object? arg = paramType is null ? null : DeserializeArg(args, paramType);
                _ = InvokeMethodAsync(method, arg);   // intentionally fire-and-forget
            }
            catch (Exception ex)
            {
                Logger.Error("IPC", $"[{channel}] send handler threw: {ex.Message}");
            }
        });
    }

    #endregion

    #region Event  (C# → JS push)

    private void BindEvent(MethodInfo method, string channel)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 1
            || !parameters[0].ParameterType.IsGenericType
            || parameters[0].ParameterType.GetGenericTypeDefinition() != typeof(Action<>))
        {
            Logger.Warning("IPC",
                $"[IpcEvent] method '{method.Name}' must accept exactly one Action<T> parameter — skipped");
            return;
        }

        var dataType = parameters[0].ParameterType.GetGenericArguments()[0];

        // Build an Action<T> delegate that sends data as JSON to the renderer
        var emitDelegate = BuildEmitDelegate(dataType, channel);

        try
        {
            method.Invoke(this, [emitDelegate]);
        }
        catch (Exception ex)
        {
            Logger.Error("IPC", $"[{channel}] event subscription threw: {ex.Message}");
        }
    }

    #endregion

    #region Helpers

    /// <summary>Returns the type of the single non-CancellationToken parameter, or null if none.</summary>
    private static Type? GetSingleParameterType(MethodInfo method)
    {
        var relevant = method.GetParameters()
            .Where(p => p.ParameterType != typeof(CancellationToken))
            .ToArray();
        return relevant.Length == 1 ? relevant[0].ParameterType : null;
    }

    /// <summary>Invokes a sync or async method and always returns a Task.</summary>
    private async Task<object?> InvokeMethodAsync(MethodInfo method, object? arg)
    {
        object?[] callArgs = arg is null ? [] : [arg];
        var raw = method.Invoke(this, callArgs);

        if (raw is Task task)
        {
            await task.ConfigureAwait(false);

            // Extract Task<T>.Result via reflection
            var resultProp = task.GetType().GetProperty("Result");
            return resultProp?.GetValue(task);
        }

        return raw;
    }

    /// <summary>Deserializes IPC args (which may be a raw JSON string, a JsonElement, or null) to <paramref name="targetType"/>.</summary>
    private static object? DeserializeArg(object? args, Type targetType)
    {
        if (args is null) return GetDefault(targetType);

        string json = args switch
        {
            string s => s,
            System.Text.Json.JsonElement je => je.GetRawText(),
            _ => JsonSerializer.Serialize(args, JsonOpts)
        };

        // String parameters: the renderer sends JSON.stringify("value") → unwrap outer quotes
        if (targetType == typeof(string))
        {
            if (json.Length >= 2 && json[0] == '"' && json[^1] == '"')
            {
                try { return JsonSerializer.Deserialize<string>(json); } catch { /* fall through */ }
            }
            return json;
        }

        return JsonSerializer.Deserialize(json, targetType, JsonOpts);
    }

    private static object? GetDefault(Type t) =>
        t.IsValueType ? Activator.CreateInstance(t) : null;

    private static void ReplyJson(string channel, object? data)
    {
        var win = Electron.WindowManager.BrowserWindows.FirstOrDefault();
        if (win is null) return;
        Electron.IpcMain.Send(win, channel, JsonSerializer.Serialize(data, JsonOpts));
    }

    private static void ReplyRaw(string channel, string raw)
    {
        var win = Electron.WindowManager.BrowserWindows.FirstOrDefault();
        if (win is null) return;
        Electron.IpcMain.Send(win, channel, raw);
    }

    /// <summary>
    /// Builds an Action&lt;T&gt; delegate (closed over channel) that serialises T to JSON
    /// and sends it to the renderer window.  Uses runtime code-gen to avoid boxing overhead.
    /// </summary>
    private static Delegate BuildEmitDelegate(Type dataType, string channel)
    {
        var actionType = typeof(Action<>).MakeGenericType(dataType);
        return Delegate.CreateDelegate(actionType,
            new EmitHelper(channel),
            typeof(EmitHelper).GetMethod(nameof(EmitHelper.Emit))!
                .MakeGenericMethod(dataType));
    }

    private sealed class EmitHelper(string channel)
    {
        public void Emit<T>(T data)
        {
            try { ReplyJson(channel, data); } catch { /* swallow */ }
        }
    }
    #endregion
}