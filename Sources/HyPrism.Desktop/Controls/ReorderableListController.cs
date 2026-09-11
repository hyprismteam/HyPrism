// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Coordinates pointer capture, drag preview movement, and drop index calculation for a list.
/// </summary>
public sealed class ReorderableListController
{
    private const double DragThreshold = 5;
    private readonly Control _itemsHost;
    private readonly Control _layoutRoot;
    private readonly Control _preview;
    private readonly Control _fallbackWidthSource;
    private readonly string _rowClass;
    private Control? _dragHandle;
    private Button? _dragRow;
    private string? _draggedId;
    private Point _dragStart;
    private Point _dragStartInLayout;
    private Point _previewOrigin;
    private int _targetIndex = -1;
    private bool _isDragActive;

    public ReorderableListController(
        Control itemsHost,
        Control layoutRoot,
        Control preview,
        Control fallbackWidthSource,
        string rowClass = "instancesListItem")
    {
        _itemsHost = itemsHost;
        _layoutRoot = layoutRoot;
        _preview = preview;
        _fallbackWidthSource = fallbackWidthSource;
        _rowClass = rowClass;
    }

    public void Begin(
        Control handle,
        string itemId,
        PointerPressedEventArgs args,
        double fallbackHeight,
        Action preparePreview)
    {
        if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        _dragHandle = handle;
        _dragRow = handle.FindAncestorOfType<Button>();
        _draggedId = itemId;
        _dragStart = args.GetPosition(_itemsHost);
        _dragStartInLayout = args.GetPosition(_layoutRoot);
        _previewOrigin = _dragRow?.TranslatePoint(default, _layoutRoot) ?? default;
        _targetIndex = -1;
        _isDragActive = false;
        preparePreview();
        _preview.Width = _dragRow?.Bounds.Width ?? _fallbackWidthSource.Bounds.Width;
        _preview.Height = _dragRow?.Bounds.Height ?? fallbackHeight;
        args.Pointer.Capture(handle);
        args.Handled = true;
    }

    public void Move(PointerEventArgs args)
    {
        if (_dragHandle is null || _draggedId is null)
            return;

        var position = args.GetPosition(_itemsHost);
        if (!_isDragActive)
        {
            var delta = position - _dragStart;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) < DragThreshold)
                return;

            _isDragActive = true;
            _dragRow?.Classes.Add("dragging");
            _preview.IsVisible = true;
        }

        var pointerInLayout = args.GetPosition(_layoutRoot);
        if (_preview.RenderTransform is TranslateTransform transform)
        {
            transform.X = _previewOrigin.X + pointerInLayout.X - _dragStartInLayout.X + 10;
            transform.Y = _previewOrigin.Y + pointerInLayout.Y - _dragStartInLayout.Y + 8;
        }

        _targetIndex = GetDropTargetIndex(position.Y);
        args.Handled = true;
    }

    public void Complete(
        PointerReleasedEventArgs args,
        Action<string, int> moveItem,
        Action clearPreview)
    {
        if (_isDragActive && _targetIndex >= 0 && _draggedId is not null)
            moveItem(_draggedId, _targetIndex);

        args.Pointer.Capture(null);
        Reset(clearPreview);
        args.Handled = true;
    }

    private int GetDropTargetIndex(double pointerY)
    {
        var rows = _itemsHost.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains(_rowClass))
            .Select(button => new
            {
                Button = button,
                Origin = button.TranslatePoint(default, _itemsHost)
            })
            .Where(item => item.Origin.HasValue)
            .OrderBy(item => item.Origin!.Value.Y)
            .ToList();

        for (var index = 0; index < rows.Count; index++)
        {
            var midpoint = rows[index].Origin!.Value.Y + rows[index].Button.Bounds.Height / 2;
            if (pointerY < midpoint)
                return index;
        }

        return Math.Max(0, rows.Count - 1);
    }

    private void Reset(Action clearPreview)
    {
        _dragRow?.Classes.Remove("dragging");
        _preview.IsVisible = false;
        clearPreview();
        _dragHandle = null;
        _dragRow = null;
        _draggedId = null;
        _targetIndex = -1;
        _isDragActive = false;
    }
}
