// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

namespace HyPrism.Core.Accounts;

/// <summary>
/// Renders the browser page returned after an interactive OAuth callback
/// </summary>
public interface IOAuthCallbackPageRenderer
{
    /// <summary>
    /// Builds a complete HTML document for the callback result
    /// </summary>
    /// <param name="success">Whether authorization completed successfully</param>
    /// <param name="message">Result details suitable for presenting to the user</param>
    /// <returns>A complete HTML document</returns>
    string Render(bool success, string message);
}
