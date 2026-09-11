// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using HyPrism.Desktop.Controls;
using Xunit;

namespace HyPrism.Desktop.Tests;

public sealed class NoteCardTests
{
    [AvaloniaFact]
    public void NoteAndImportantVariantsApplyAccentSurfaceIconAndTitle()
    {
        var note = new NoteCard { Title = "Note" };
        note.Classes.Add("note");
        var important = new NoteCard { Title = "Important" };
        important.Classes.Add("important");
        var window = new Window
        {
            Width = 420,
            Height = 260,
            Content = new StackPanel { Children = { note, important } }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new CornerRadius(14), note.CornerRadius);
        Assert.Equal(
            Color.Parse("#1A79B0F4"),
            Assert.IsAssignableFrom<ISolidColorBrush>(note.Background).Color);
        Assert.Equal(
            Color.Parse("#1AB79DF8"),
            Assert.IsAssignableFrom<ISolidColorBrush>(important.Background).Color);

        var noteIcon = Assert.IsAssignableFrom<PathIcon>(
            note.GetTemplateChildren().OfType<PathIcon>().Single());
        var importantIcon = Assert.IsAssignableFrom<PathIcon>(
            important.GetTemplateChildren().OfType<PathIcon>().Single());
        Assert.Equal(
            window.FindResource("InfoIcon"),
            noteIcon.Data);
        Assert.Equal(
            window.FindResource("FeedbackIcon"),
            importantIcon.Data);
        Assert.Equal(
            Color.Parse("#79B0F4"),
            Assert.IsAssignableFrom<ISolidColorBrush>(noteIcon.Foreground).Color);
        Assert.Equal(
            Color.Parse("#B79DF8"),
            Assert.IsAssignableFrom<ISolidColorBrush>(importantIcon.Foreground).Color);

        var noteTitle = note.GetTemplateChildren()
            .OfType<TextBlock>()
            .Single(text => text.Classes.Contains("noteCardTitle"));
        Assert.Equal("Note", noteTitle.Text);
        Assert.Equal(
            Color.Parse("#79B0F4"),
            Assert.IsAssignableFrom<ISolidColorBrush>(noteTitle.Foreground).Color);

        window.Close();
    }
}
