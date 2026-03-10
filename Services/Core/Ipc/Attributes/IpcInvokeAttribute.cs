namespace HyPrism.Services.Core.Ipc.Attributes;

/// <summary>
/// Marks a method as an IPC invoke handler (request → response).
/// The method must return T or Task&lt;T&gt;; the return type is used by HyPrism.IpcGen
/// to emit the corresponding TypeScript response type.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IpcInvokeAttribute(string channel, int timeoutMs = 10_000) : Attribute
{
    public string Channel { get; } = channel;
    public int TimeoutMs { get; } = timeoutMs;
}
