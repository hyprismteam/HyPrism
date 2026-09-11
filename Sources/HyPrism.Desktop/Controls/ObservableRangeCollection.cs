// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace HyPrism.Desktop.Controls;

internal sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void AddRange(IReadOnlyList<T> items)
    {
        if (items.Count == 0)
            return;

        var startIndex = Count;
        foreach (var item in items)
            Items.Add(item);

        RaisePropertiesChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            items.ToList(),
            startIndex));
    }

    public void ReplaceRange(IEnumerable<T> items)
    {
        var replacement = items as IReadOnlyList<T> ?? items.ToList();
        Items.Clear();
        foreach (var item in replacement)
            Items.Add(item);

        RaisePropertiesChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    private void RaisePropertiesChanged()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
    }
}
