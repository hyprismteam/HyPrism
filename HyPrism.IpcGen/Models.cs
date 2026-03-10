namespace HyPrism.IpcGen;

/// <summary>Kinds of IPC channels.</summary>
internal enum IpcKind { Invoke, Send, Event }

/// <summary>A single discovered IPC handler method.</summary>
internal record IpcMethod(
    IpcKind Kind,
    string Channel,
    int TimeoutMs,          // only meaningful for Invoke
    string? InputTsType,    // null if no input parameter
    string ResponseTsType); // "void" for Send; event data type for Event

/// <summary>A TypeScript interface to emit.</summary>
internal record TsInterface(string Name, List<TsField> Fields);

/// <summary>A field inside a TypeScript interface.</summary>
internal record TsField(string Name, string TsType, bool Optional);
