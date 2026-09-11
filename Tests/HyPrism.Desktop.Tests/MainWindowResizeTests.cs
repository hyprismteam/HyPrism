// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HyPrism.Desktop.Shell;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class MainWindowResizeTests
{
    private const double MinWidth = 1024;
    private const double MinHeight = 700;
    private const double MaxSize = double.PositiveInfinity;
    private const double StartWidth = 1280;
    private const double StartHeight = 800;

    [Fact]
    public void EastResize_GrowsWithoutMovingTheWindow()
    {
        var (size, offset) = MainWindow.CalculateResize(
            WindowEdge.East, new Size(StartWidth, StartHeight), new Vector(200, 40),
            MinWidth, MaxSize, MinHeight, MaxSize);

        Assert.Equal(1480, size.Width);
        Assert.Equal(StartHeight, size.Height);
        Assert.Equal(default, offset);
    }

    [Fact]
    public void WestResize_MovesTheWindowByTheActualDelta()
    {
        var (size, offset) = MainWindow.CalculateResize(
            WindowEdge.West, new Size(StartWidth, StartHeight), new Vector(96, 0),
            MinWidth, MaxSize, MinHeight, MaxSize);

        Assert.Equal(StartWidth - 96, size.Width);
        Assert.Equal(96, offset.X);
        Assert.Equal(0, offset.Y);
    }

    [Fact]
    public void WestResize_StopsAtMinimumWithoutFurtherMovement()
    {
        // Dragging 900 DIPs past the minimum width: the window size clamps at
        // MinWidth and the position offset must stay fixed instead of following
        // the cursor (the native modal loop drifted the window in this case)
        var (size, offset) = MainWindow.CalculateResize(
            WindowEdge.West, new Size(StartWidth, StartHeight), new Vector(900, 0),
            MinWidth, MaxSize, MinHeight, MaxSize);

        Assert.Equal(MinWidth, size.Width);
        Assert.Equal(StartWidth - MinWidth, offset.X);
    }

    [Fact]
    public void NorthResize_StopsAtMinimumWithoutFurtherMovement()
    {
        var (size, offset) = MainWindow.CalculateResize(
            WindowEdge.North, new Size(StartWidth, StartHeight), new Vector(0, 500),
            MinWidth, MaxSize, MinHeight, MaxSize);

        Assert.Equal(MinHeight, size.Height);
        Assert.Equal(StartHeight - MinHeight, offset.Y);
        Assert.Equal(StartWidth, size.Width);
    }

    [Fact]
    public void SouthEastResize_GrowsWithoutMovingTheWindow()
    {
        var (size, offset) = MainWindow.CalculateResize(
            WindowEdge.SouthEast, new Size(StartWidth, StartHeight), new Vector(-150, 220),
            MinWidth, MaxSize, MinHeight, MaxSize);

        // Upward drag on the south edge shrinks the height but cannot move the window
        Assert.Equal(StartWidth - 150, size.Width);
        Assert.Equal(StartHeight + 220, size.Height);
        Assert.Equal(default, offset);
    }

    [Fact]
    public void NorthWestResize_ClampsBothAxesIndependently()
    {
        var (size, offset) = MainWindow.CalculateResize(
            WindowEdge.NorthWest, new Size(StartWidth, StartHeight), new Vector(900, 80),
            MinWidth, MaxSize, MinHeight, MaxSize);

        Assert.Equal(MinWidth, size.Width);
        Assert.Equal(StartHeight - 80, size.Height);
        Assert.Equal(StartWidth - MinWidth, offset.X);
        Assert.Equal(80, offset.Y);
    }

    [Fact]
    public void SouthResize_ShrinksClampedAtMinimumHeight()
    {
        var (size, offset) = MainWindow.CalculateResize(
            WindowEdge.South, new Size(StartWidth, StartHeight), new Vector(0, -400),
            MinWidth, MaxSize, MinHeight, MaxSize);

        Assert.Equal(MinHeight, size.Height);
        Assert.Equal(default, offset);
    }
}
