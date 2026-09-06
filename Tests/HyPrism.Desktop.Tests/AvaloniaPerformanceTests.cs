// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyPrism.Desktop.Controls;
using HyPrism.Desktop.Features.Instances;
using HyPrism.Desktop.Shell;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class AvaloniaPerformanceTests
{
    [AvaloniaFact]
    public void MainWindowInitialVisualTreeStaysWithinBudget()
    {
        var window = new MainWindow();

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var visualCount = window.GetVisualDescendants().Count();
            var deferredPages = window.GetVisualDescendants()
                .OfType<DeferredContentControl>()
                .ToList();

            Assert.InRange(visualCount, 1, 160);
            Assert.Equal(4, deferredPages.Count);
            Assert.All(deferredPages, page => Assert.Null(page.Content));
            Assert.DoesNotContain(window.GetVisualDescendants(), visual => visual is InstancesView);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DeferredContentIsRealizedOnceAndRetained()
    {
        var child = new Border();
        var deferred = new DeferredContentControl
        {
            DeferredContent = child
        };
        var window = new Window { Content = deferred };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.False(deferred.IsVisible);
            Assert.Null(deferred.Content);

            deferred.IsActive = true;
            Assert.True(deferred.IsVisible);
            Assert.Same(child, deferred.Content);

            deferred.IsActive = false;
            Assert.False(deferred.IsVisible);
            Assert.Same(child, deferred.Content);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InstanceContentListsUseVirtualizingPanels()
    {
        var view = new InstancesView();
        var window = new Window { Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var lists = view.GetVisualDescendants()
                .OfType<ListBox>()
                .Where(list => list.Classes.Contains("instanceVirtualizedList"))
                .ToList();

            Assert.Equal(3, lists.Count);
            Assert.All(lists, list =>
            {
                var panel = Assert.IsType<VirtualizingStackPanel>(list.ItemsPanel!.Build());
                Assert.Equal(1, panel.CacheLength);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void RangeReplacementRaisesSingleResetNotification()
    {
        var collection = new ObservableRangeCollection<int>();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);

        collection.ReplaceRange(Enumerable.Range(0, 1_000));

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
        Assert.Equal(1_000, collection.Count);
    }
}
