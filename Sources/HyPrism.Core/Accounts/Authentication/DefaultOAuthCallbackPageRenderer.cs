// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;

namespace HyPrism.Core.Accounts;

internal sealed class DefaultOAuthCallbackPageRenderer : IOAuthCallbackPageRenderer
{
    public static DefaultOAuthCallbackPageRenderer Instance { get; } = new();

    private DefaultOAuthCallbackPageRenderer()
    {
    }

    /// <summary>Renders the OAuth callback result as a complete HTML page</summary>
    /// <param name="success">Whether authorization completed successfully</param>
    /// <param name="message">Message shown to the user</param>
    /// <returns>The rendered HTML page</returns>
    public string Render(bool success, string message)
    {
        var title = success ? "Authorization successful" : "Authorization failed";
        var encodedMessage = WebUtility.HtmlEncode(message);
        var closeHint = success
            ? "<p>You can close this window and return to the launcher</p>"
            : string.Empty;
        var closeScript = success
            ? "<script>window.setTimeout(() => window.close(), 1200);</script>"
            : string.Empty;

        return $"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
               $"<title>{title}</title>{closeScript}</head>" +
               $"<body><main><h1>{title}</h1><p>{encodedMessage}</p>{closeHint}</main></body></html>";
    }
}
