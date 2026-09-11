// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Avalonia.Platform;
using HyPrism.Core.Accounts;
using HyPrism.Core.Infrastructure;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Localization;

namespace HyPrism.Desktop.Platform;

internal sealed class OAuthCallbackPageRenderer : IOAuthCallbackPageRenderer
{
    private static readonly Uri LogoUri = new(
        "avares://HyPrism.Desktop/Assets/Images/preview_logo.png");

    private static readonly Lazy<string> LogoMarkup = new(BuildLogoMarkup);
    private readonly IDesktopSettingsStore _settings;

    public OAuthCallbackPageRenderer(IDesktopSettingsStore settings)
    {
        _settings = settings;
    }

    public string Render(bool success, string message)
    {
        var localizer = new StringLocalizer(_settings.Language);
        var title = localizer[success
            ? "oauth.callback.successTitle"
            : "oauth.callback.failureTitle"];
        var resultMessage = success
            ? localizer["oauth.callback.successMessage"]
            : message;
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedMessage = WebUtility.HtmlEncode(resultMessage);
        var encodedLanguage = WebUtility.HtmlEncode(localizer.CurrentLanguage);
        var statusClass = success ? "success" : "failure";
        var statusIcon = success
            ? "<path d=\"m7.5 12.5 3 3 6-7\"/>"
            : "<path d=\"m9 9 6 6m0-6-6 6\"/>";
        var closeHint = success
            ? localizer["oauth.callback.closeHint"]
            : localizer["oauth.callback.failureHint"];
        var closeScript = success
            ? "<script>window.setTimeout(() => window.close(), 1200);</script>"
            : string.Empty;

        return $$"""
            <!doctype html>
            <html lang="{{encodedLanguage}}">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="color-scheme" content="dark">
              <meta name="referrer" content="no-referrer">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
              <title>HyPrism | {{encodedTitle}}</title>
              <style>
                :root {
                  color-scheme: dark;
                  font-family: Inter, "Segoe UI", system-ui, -apple-system, BlinkMacSystemFont, sans-serif;
                  background: #08090a;
                  color: #f7f7f8;
                }

                * {
                  box-sizing: border-box;
                }

                body {
                  min-width: 280px;
                  min-height: 100vh;
                  min-height: 100svh;
                  margin: 0;
                  overflow-x: hidden;
                  background: #08090a;
                }

                .page {
                  width: 100%;
                  min-height: 100vh;
                  min-height: 100svh;
                  display: grid;
                  grid-template-rows: 1fr auto;
                  padding: 32px 32px 26px;
                }

                .content {
                  place-self: center;
                  width: min(100%, 540px);
                  text-align: center;
                  animation: content-in 420ms cubic-bezier(0.22, 1, 0.36, 1) both;
                }

                .brand {
                  display: flex;
                  align-items: center;
                  justify-content: center;
                  padding-top: 32px;
                  opacity: 0.68;
                  animation: brand-in 520ms 100ms cubic-bezier(0.22, 1, 0.36, 1) both;
                }

                .brand img {
                  display: block;
                  width: min(100%, 132px);
                  height: auto;
                }

                .brand-fallback {
                  font-size: 18px;
                  font-weight: 700;
                  letter-spacing: -0.04em;
                }

                .brand-fallback small {
                  display: block;
                  margin-top: -2px;
                  font-size: 8px;
                  font-weight: 500;
                  letter-spacing: 0.24em;
                  text-transform: uppercase;
                  color: #a8a9ae;
                }

                .status-icon {
                  width: 64px;
                  height: 64px;
                  margin: 0 auto 22px;
                  display: grid;
                  place-items: center;
                  border-radius: 50%;
                }

                .status-icon.success {
                  color: #a9e8be;
                  background: rgba(104, 205, 142, 0.12);
                  border: 1px solid rgba(104, 205, 142, 0.3);
                  box-shadow: 0 0 32px rgba(104, 205, 142, 0.1);
                }

                .status-icon.failure {
                  color: #ff9696;
                  background: rgba(231, 131, 131, 0.12);
                  border: 1px solid rgba(231, 131, 131, 0.3);
                  box-shadow: 0 0 32px rgba(231, 131, 131, 0.1);
                }

                .status-icon svg {
                  width: 30px;
                  height: 30px;
                  fill: none;
                  stroke: currentColor;
                  stroke-width: 2;
                  stroke-linecap: round;
                  stroke-linejoin: round;
                }

                h1 {
                  margin: 0;
                  font-size: clamp(26px, 5vw, 32px);
                  line-height: 1.2;
                  letter-spacing: -0.035em;
                }

                .message {
                  max-width: 390px;
                  margin: 13px auto 0;
                  color: #a8a9ae;
                  font-size: 16px;
                  line-height: 1.6;
                }

                .close-hint {
                  max-width: 390px;
                  margin: 26px auto 0;
                  color: #73757d;
                  font-size: 14px;
                  font-weight: 500;
                  line-height: 1.5;
                }

                @keyframes content-in {
                  from {
                    opacity: 0;
                    transform: translateY(8px);
                  }
                }

                @keyframes brand-in {
                  from {
                    opacity: 0;
                  }
                }

                @media (max-width: 520px) {
                  .page {
                    padding: 24px 18px 20px;
                  }

                  .brand {
                    padding-top: 24px;
                  }
                }

                @media (prefers-reduced-motion: reduce) {
                  .content,
                  .brand {
                    animation: none;
                  }
                }
              </style>
              {{closeScript}}
            </head>
            <body>
              <div class="page">
                <main class="content" role="status" aria-live="polite">
                  <div class="status-icon {{statusClass}}" aria-hidden="true">
                    <svg viewBox="0 0 24 24">{{statusIcon}}</svg>
                  </div>
                  <h1>{{encodedTitle}}</h1>
                  <p class="message">{{encodedMessage}}</p>
                  <p class="close-hint">{{WebUtility.HtmlEncode(closeHint)}}</p>
                </main>
                <footer class="brand">{{LogoMarkup.Value}}</footer>
              </div>
            </body>
            </html>
            """;
    }

    private static string BuildLogoMarkup()
    {
        try
        {
            using var stream = AssetLoader.Open(LogoUri);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var base64 = Convert.ToBase64String(buffer.ToArray());
            return $"<img src=\"data:image/png;base64,{base64}\" alt=\"HyPrism Launcher\">";
        }
        catch (Exception exception)
        {
            Logger.Warning("OAuthCallback", $"Could not load callback logo: {exception.Message}");
            return "<div class=\"brand-fallback\">HyPrism<small>Launcher</small></div>";
        }
    }
}
