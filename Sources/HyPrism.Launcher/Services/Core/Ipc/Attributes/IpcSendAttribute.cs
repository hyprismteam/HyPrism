namespace HyPrism.Services.Core.Ipc.Attributes;

/// <summary>
/// Marks a method as a fire-and-forget IPC handler (no reply sent).
/// The method must return void. Optional parameter is the typed input DTO.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IpcSendAttribute(string channel) : Attribute
{
    public string Channel { get; } = channel;
}
