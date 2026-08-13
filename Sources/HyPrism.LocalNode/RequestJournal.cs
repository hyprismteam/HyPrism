// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;

namespace HyPrism.LocalNode;

/// <summary>
/// Records unimplemented client requests without writing request bodies or credentials
/// </summary>
public sealed class RequestJournal
{
    private readonly string _path;
    private readonly Lock _writeLock = new();

    /// <summary>
    /// Creates a journal under the Local Node data directory
    /// </summary>
    /// <param name="dataDirectory">Directory owned by the Local Node instance</param>
    public RequestJournal(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "unimplemented-requests.ndjson");
    }

    /// <summary>
    /// Appends request metadata while intentionally excluding headers and request bodies
    /// </summary>
    /// <param name="method">HTTP request method</param>
    /// <param name="path">Request path</param>
    /// <param name="queryParameterNames">Query parameter names, excluding their values</param>
    public void Append(string method, string path, ICollection<string> queryParameterNames)
    {
        var entry = JsonSerializer.Serialize(new
        {
            occurredAt = DateTimeOffset.UtcNow,
            method,
            path,
            queryParameterNames = queryParameterNames.OrderBy(name => name, StringComparer.Ordinal).ToArray()
        });

        lock (_writeLock)
        {
            File.AppendAllText(_path, entry + Environment.NewLine);
        }
    }
}
