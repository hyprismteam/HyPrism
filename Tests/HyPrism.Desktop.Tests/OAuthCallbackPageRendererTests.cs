// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Headless.XUnit;
using HyPrism.Desktop.Features.Settings;
using HyPrism.Desktop.Platform;
using Moq;
using System.Net;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class OAuthCallbackPageRendererTests
{
    [AvaloniaFact]
    public void SuccessfulPageUsesLauncherBrandingAndExplainsThatTheWindowCanClose()
    {
        var renderer = CreateRenderer("en-US");

        var html = renderer.Render(
            success: true,
            "Your official Hytale account is connected to HyPrism");

        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#08090a", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);
        Assert.Contains("Authorization successful", html, StringComparison.Ordinal);
        Assert.Contains("official Hytale account", html, StringComparison.Ordinal);
        Assert.Contains("You can close this window", html, StringComparison.Ordinal);
        Assert.Contains("window.close()", html, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 520px)", html, StringComparison.Ordinal);
        Assert.Contains("<footer class=\"brand\">", html, StringComparison.Ordinal);
        Assert.Contains("width: min(100%, 132px)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("gradient(", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"card\"", html, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void FailurePageEscapesMessageAndDoesNotAttemptToCloseTheWindow()
    {
        var renderer = CreateRenderer("en-US");

        var html = renderer.Render(success: false, "Failure <script>alert('x')</script>");

        Assert.Contains("Authorization failed", html, StringComparison.Ordinal);
        Assert.Contains("Failure &lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("window.close()", html, StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData("en-US", "en-US", "Authorization successful", "Your official Hytale account", "You can close this window")]
    [InlineData("be-BY", "be-BY", "Аўтарызацыя завершана", "Ваш афіцыйны ўліковы запіс Hytale", "Вы можаце закрыць гэтае акно")]
    [InlineData("de-DE", "de-DE", "Autorisierung erfolgreich", "Dein offizielles Hytale-Konto", "Du kannst dieses Fenster schließen")]
    [InlineData("es-ES", "es-ES", "Autorización completada", "Tu cuenta oficial de Hytale", "Puedes cerrar esta ventana")]
    [InlineData("fr-FR", "fr-FR", "Autorisation réussie", "Votre compte Hytale officiel", "Vous pouvez fermer cette fenêtre")]
    [InlineData("it-IT", "it-IT", "Autorizzazione completata", "Il tuo account Hytale ufficiale", "Puoi chiudere questa finestra")]
    [InlineData("ja-JP", "ja-JP", "認証に成功しました", "Hytale公式アカウント", "このウィンドウを閉じて")]
    [InlineData("ko-KR", "ko-KR", "인증 성공", "공식 Hytale 계정", "이 창을 닫고")]
    [InlineData("pt-BR", "pt-BR", "Autorização concluída", "Sua conta oficial do Hytale", "Você pode fechar esta janela")]
    [InlineData("ru-RU", "ru-RU", "Авторизация завершена", "Ваш официальный аккаунт Hytale", "Вы можете закрыть это окно")]
    [InlineData("tr-TR", "tr-TR", "Yetkilendirme başarılı", "Resmî Hytale hesabınız", "Bu pencereyi kapatıp")]
    [InlineData("uk-UA", "uk-UA", "Авторизацію завершено", "Ваш офіційний обліковий запис Hytale", "Ви можете закрити це вікно")]
    [InlineData("zh-CN", "zh-CN", "授权成功", "您的 Hytale 官方账号", "您可以关闭此窗口")]
    [InlineData("xx-XX", "en-US", "Authorization successful", "Your official Hytale account", "You can close this window")]
    public void SuccessfulPageUsesTheCurrentLauncherLanguage(
        string language,
        string expectedLanguage,
        string expectedTitle,
        string expectedMessage,
        string expectedHint)
    {
        var renderer = CreateRenderer(language);

        var html = renderer.Render(success: true, "Core fallback message");
        var decodedHtml = WebUtility.HtmlDecode(html);

        Assert.Contains(expectedTitle, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, decodedHtml, StringComparison.Ordinal);
        Assert.Contains(expectedHint, decodedHtml, StringComparison.Ordinal);
        Assert.Contains($"<html lang=\"{expectedLanguage}\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Core fallback message", html, StringComparison.Ordinal);
    }

    private static OAuthCallbackPageRenderer CreateRenderer(string language)
    {
        var settings = new Mock<IDesktopSettingsStore>();
        settings.SetupGet(value => value.Language).Returns(language);
        return new OAuthCallbackPageRenderer(settings.Object);
    }
}
