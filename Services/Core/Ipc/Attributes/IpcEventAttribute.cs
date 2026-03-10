namespace HyPrism.Services.Core.Ipc.Attributes;

/// <summary>
/// Marks a method as a push-event subscription (C# → JS).
/// The method must accept exactly one parameter of type Action&lt;T&gt;;
/// T is used by HyPrism.IpcGen to emit the TypeScript event data type.
/// The base class will call this method once, supplying an emit delegate
/// that pushes data to the renderer via the given channel.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IpcEventAttribute(string channel) : Attribute
{
    public string Channel { get; } = channel;
}
