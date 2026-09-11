// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Shell;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class DeferredContentControlTests
{
    [AvaloniaFact]
    public void PreWarm_BuildsTheContentOnceAndHidesItAgain()
    {
        var creations = 0;
        var template = new FuncDataTemplate<object>((_, _) =>
        {
            creations++;
            return new Border();
        }, true);
        var control = new DeferredContentControl
        {
            ContentTemplate = template,
            DeferredContent = new object()
        };
        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = new StackPanel { Children = { control } }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, creations);

        control.BeginPreWarm();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, creations);
        Assert.NotNull(control.Content);
        Assert.True(control.IsVisible);

        control.EndPreWarm();
        Dispatcher.UIThread.RunJobs();

        Assert.False(control.IsVisible);
        Assert.Equal(1, creations);

        control.IsActive = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(control.IsVisible);
        Assert.Equal(1, creations);
    }

    [AvaloniaFact]
    public async Task WarmUpDeferredSections_RealizesEveryControlWithoutActivatingIt()
    {
        var creations = 0;
        var template = new FuncDataTemplate<object>((_, _) =>
        {
            creations++;
            return new Border();
        }, true);
        var first = new DeferredContentControl { ContentTemplate = template, DeferredContent = "first" };
        var second = new DeferredContentControl { ContentTemplate = template, DeferredContent = "second" };
        var alreadyActive = new DeferredContentControl
        {
            ContentTemplate = template,
            DeferredContent = "third",
            IsActive = true
        };
        var window = new Window
        {
            Width = 300,
            Height = 200,
            Content = new StackPanel { Children = { first, second, alreadyActive } }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        await MainWindow.WarmUpDeferredSections(window);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, creations);
        Assert.NotNull(first.Content);
        Assert.NotNull(second.Content);
        Assert.False(first.IsVisible);
        Assert.False(second.IsVisible);
        Assert.True(alreadyActive.IsVisible);
    }
}
