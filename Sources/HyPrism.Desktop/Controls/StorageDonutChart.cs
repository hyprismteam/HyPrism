// Copyright (C) 2026 HyPrism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;

namespace HyPrism.Desktop.Controls;

/// <summary>
/// Draws proportional storage usage as a filled donut chart
/// </summary>
public sealed class StorageDonutChart : Control
{
    private static readonly IReadOnlyList<IBrush> ParticleBrushes = Enumerable.Range(0, 33)
        .Select(index => (IBrush)new ImmutableSolidColorBrush(Color.FromArgb(
            (byte)(index * 4),
            255,
            255,
            255)))
        .ToArray();

    private static readonly IReadOnlyDictionary<StorageDonutIconKind, string> ParticleIconKeys =
        new Dictionary<StorageDonutIconKind, string>
        {
            [StorageDonutIconKind.Instances] = "StorageInstancesRoundedIcon",
            [StorageDonutIconKind.Images] = "StorageImagesRoundedIcon",
            [StorageDonutIconKind.Mods] = "StorageModsRoundedIcon",
            [StorageDonutIconKind.News] = "StorageNewsRoundedIcon",
            [StorageDonutIconKind.Logs] = "StorageLogsRoundedIcon",
            [StorageDonutIconKind.Other] = "StorageOtherRoundedIcon"
        };

    private int _animationVersion;
    private TimeSpan? _animationStartedAt;
    private double _animationSeconds;
    private IReadOnlyDictionary<StorageDonutIconKind, Geometry> _particleIcons =
        new Dictionary<StorageDonutIconKind, Geometry>();

    public static readonly StyledProperty<IReadOnlyList<StorageDonutSegment>> ItemsProperty =
        AvaloniaProperty.Register<StorageDonutChart, IReadOnlyList<StorageDonutSegment>>(
            nameof(Items),
            Array.Empty<StorageDonutSegment>());

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<StorageDonutChart, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> HoleBrushProperty =
        AvaloniaProperty.Register<StorageDonutChart, IBrush?>(nameof(HoleBrush));

    public static readonly StyledProperty<IBrush?> SeparatorBrushProperty =
        AvaloniaProperty.Register<StorageDonutChart, IBrush?>(nameof(SeparatorBrush));

    public static readonly StyledProperty<double> SeparatorThicknessProperty =
        AvaloniaProperty.Register<StorageDonutChart, double>(nameof(SeparatorThickness), 2);

    public static readonly StyledProperty<double> InnerRadiusRatioProperty =
        AvaloniaProperty.Register<StorageDonutChart, double>(nameof(InnerRadiusRatio), 0.5);

    static StorageDonutChart()
    {
        AffectsRender<StorageDonutChart>(
            ItemsProperty,
            TrackBrushProperty,
            HoleBrushProperty,
            SeparatorBrushProperty,
            SeparatorThicknessProperty,
            InnerRadiusRatioProperty);
    }

    public IReadOnlyList<StorageDonutSegment> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? HoleBrush
    {
        get => GetValue(HoleBrushProperty);
        set => SetValue(HoleBrushProperty, value);
    }

    public IBrush? SeparatorBrush
    {
        get => GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    public double SeparatorThickness
    {
        get => GetValue(SeparatorThicknessProperty);
        set => SetValue(SeparatorThicknessProperty, value);
    }

    public double InnerRadiusRatio
    {
        get => GetValue(InnerRadiusRatioProperty);
        set => SetValue(InnerRadiusRatioProperty, value);
    }

    internal int LoadedParticleIconCount => _particleIcons.Count;

    internal static double GetParticlePhase(StorageDonutIconKind iconKind, int index)
        => Noise(iconKind, index, 1);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _particleIcons = ParticleIconKeys
            .Select(pair =>
                this.TryFindResource(pair.Value, out var value) && value is Geometry geometry
                    ? new KeyValuePair<StorageDonutIconKind, Geometry>(pair.Key, geometry)
                    : default)
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        StartAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _animationVersion++;
        _animationStartedAt = null;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var diameter = Math.Min(Bounds.Width, Bounds.Height);
        if (diameter <= 0)
            return;

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var separatorThickness = Math.Clamp(SeparatorThickness, 0, diameter / 12);
        var radius = Math.Max(0, (diameter - separatorThickness) / 2);
        if (radius <= 0)
            return;

        var circleBounds = CircleBounds(center, radius);
        if (TrackBrush is not null)
            context.DrawEllipse(TrackBrush, null, circleBounds);

        var visibleItems = Items.Where(item => item.Bytes > 0).ToArray();
        var total = visibleItems.Sum(item => (double)item.Bytes);
        var separatorPen = SeparatorBrush is null || separatorThickness <= 0
            ? null
            : new Pen(SeparatorBrush, separatorThickness, lineJoin: PenLineJoin.Round);

        var labels = new List<SliceLabel>(visibleItems.Length);
        if (total > 0 && visibleItems.Length == 1)
        {
            context.DrawEllipse(visibleItems[0].Brush, separatorPen, circleBounds);
            labels.Add(new SliceLabel(visibleItems[0], -90, 360));
        }
        else if (total > 0)
        {
            var currentAngle = -90d;
            foreach (var item in visibleItems)
            {
                var sweepAngle = 360 * item.Bytes / total;
                DrawSector(context, center, radius, currentAngle, sweepAngle, item.Brush, separatorPen);
                labels.Add(new SliceLabel(item, currentAngle, sweepAngle));
                currentAngle += sweepAngle;
            }
        }

        var innerRadius = radius * Math.Clamp(InnerRadiusRatio, 0.25, 0.75);
        DrawParticles(context, center, radius, innerRadius, labels, _animationSeconds, _particleIcons);
        if (HoleBrush is not null)
            context.DrawEllipse(HoleBrush, separatorPen, CircleBounds(center, innerRadius));

        DrawLabels(context, center, radius, innerRadius, labels);
    }

    private static void DrawSector(
        DrawingContext context,
        Point center,
        double radius,
        double startAngle,
        double sweepAngle,
        IBrush brush,
        IPen? separatorPen)
    {
        if (sweepAngle <= 0)
            return;

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(center, isFilled: true);
            geometryContext.LineTo(PointOnCircle(center, radius, startAngle), isStroked: true);
            geometryContext.ArcTo(
                PointOnCircle(center, radius, startAngle + sweepAngle),
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: sweepAngle > 180,
                SweepDirection.Clockwise,
                isStroked: true);
            geometryContext.LineTo(center, isStroked: true);
            geometryContext.EndFigure(isClosed: true);
        }

        context.DrawGeometry(brush, separatorPen, geometry);
    }

    private static void DrawLabels(
        DrawingContext context,
        Point center,
        double outerRadius,
        double innerRadius,
        IReadOnlyList<SliceLabel> labels)
    {
        foreach (var label in labels)
        {
            var share = label.SweepAngle / 360;
            if (share < 0.01)
                continue;

            var ringWidth = outerRadius - innerRadius;
            var normalizedShare = Math.Clamp(share / 0.12, 0, 1);
            var radiusFactor = 0.8 - (0.25 * normalizedShare);
            var labelRadius = innerRadius + (ringWidth * radiusFactor);
            var availableWidth = Math.Max(
                0,
                (2 * labelRadius * Math.Sin(label.SweepAngle * Math.PI / 360)) - 4);
            var fontSize = GetPreferredLabelFontSize(share);
            var text = CreateLabelText(label, fontSize);
            while (fontSize > 5.5 && text.Width > availableWidth)
            {
                fontSize = Math.Max(5.5, fontSize - 0.5);
                text = CreateLabelText(label, fontSize);
            }

            if (text.Width > availableWidth)
                continue;

            var labelCenter = PointOnCircle(
                center,
                labelRadius,
                label.StartAngle + (label.SweepAngle / 2));
            context.DrawText(
                text,
                new Point(labelCenter.X - (text.Width / 2), labelCenter.Y - (text.Height / 2)));
        }
    }

    internal static double GetPreferredLabelFontSize(double share)
        => Math.Clamp(5 + (18 * Math.Sqrt(Math.Clamp(share, 0, 1))), 6, 18);

    private static FormattedText CreateLabelText(SliceLabel label, double fontSize)
        => new(
            label.Segment.Percentage,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Medium),
            fontSize,
            GetLabelBrush(label.Segment.Brush));

    private static IBrush GetLabelBrush(IBrush segmentBrush)
    {
        if (segmentBrush is not ISolidColorBrush solidBrush)
            return Brushes.White;

        var color = solidBrush.Color;
        var luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;
        return luminance > 0.72 ? Brushes.Black : Brushes.White;
    }

    private void StartAnimation()
    {
        _animationStartedAt = null;
        var animationVersion = ++_animationVersion;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(
            timestamp => OnAnimationFrame(timestamp, animationVersion));
    }

    private void OnAnimationFrame(TimeSpan timestamp, int animationVersion)
    {
        if (animationVersion != _animationVersion || !this.IsAttachedToVisualTree())
            return;

        if (IsEffectivelyVisible)
        {
            _animationStartedAt ??= timestamp;
            _animationSeconds = (timestamp - _animationStartedAt.Value).TotalSeconds;
            InvalidateVisual();
        }
        else
        {
            _animationStartedAt = null;
        }

        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(
            nextTimestamp => OnAnimationFrame(nextTimestamp, animationVersion));
    }

    private static void DrawParticles(
        DrawingContext context,
        Point center,
        double outerRadius,
        double innerRadius,
        IReadOnlyList<SliceLabel> labels,
        double animationSeconds,
        IReadOnlyDictionary<StorageDonutIconKind, Geometry> particleIcons)
    {
        var ringWidth = outerRadius - innerRadius;
        if (ringWidth < 18)
            return;

        foreach (var label in labels)
        {
            var share = label.SweepAngle / 360;
            if (share < 0.025 || !particleIcons.TryGetValue(label.Segment.IconKind, out var icon))
                continue;

            var particleCount = Math.Clamp((int)Math.Round(share * 20), 1, 14);
            for (var index = 0; index < particleCount; index++)
            {
                var phase = GetParticlePhase(label.Segment.IconKind, index);
                var speed = 0.15 + (Noise(label.Segment.IconKind, index, 2) * 0.13);
                var age = (animationSeconds * speed) + phase;
                var cycle = (int)Math.Floor(age);
                var life = age - cycle;
                var easedLife = SmoothStep(life);
                var fadeIn = SmoothStep(Math.Clamp(life / 0.18, 0, 1));
                var fadeOut = SmoothStep(Math.Clamp((1 - life) / 0.24, 0, 1));
                var opacity = fadeIn * fadeOut;
                var brushIndex = Math.Clamp(
                    (int)Math.Round(opacity * (ParticleBrushes.Count - 1)),
                    0,
                    ParticleBrushes.Count - 1);
                var angularMargin = Math.Clamp(10 / label.SweepAngle, 0.05, 0.3);
                var anchorAngularPosition = Lerp(
                    angularMargin,
                    1 - angularMargin,
                    Noise(label.Segment.IconKind, index, 3, cycle));
                var anchorAngle = label.StartAngle + (label.SweepAngle * anchorAngularPosition);
                var angularDrift = (Noise(label.Segment.IconKind, index, 4, cycle) - 0.5) *
                    Math.Min(14, label.SweepAngle * 0.16);
                var angle = anchorAngle + (angularDrift * easedLife);
                var anchorRadiusPosition = Lerp(
                    0.15,
                    0.85,
                    Noise(label.Segment.IconKind, index, 5, cycle));
                var radialDrift = (Noise(label.Segment.IconKind, index, 6, cycle) - 0.5) *
                    ringWidth * 0.24;
                var particleRadius = innerRadius +
                    (ringWidth * anchorRadiusPosition) + (radialDrift * easedLife);
                var point = PointOnCircle(center, particleRadius, angle);
                var size = 6.5 + (Noise(label.Segment.IconKind, index, 7, cycle) * 3);
                DrawParticleIcon(context, point, size, icon, ParticleBrushes[brushIndex]);
            }
        }
    }

    private static void DrawParticleIcon(
        DrawingContext context,
        Point center,
        double size,
        Geometry icon,
        IBrush brush)
    {
        var bounds = icon.Bounds;
        var scale = size / Math.Max(bounds.Width, bounds.Height);
        var transform = Matrix.CreateTranslation(-bounds.Center.X, -bounds.Center.Y)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(center.X, center.Y);
        using (context.PushTransform(transform))
            context.DrawGeometry(brush, null, icon);
    }

    private static double Noise(
        StorageDonutIconKind iconKind,
        int index,
        int salt,
        int cycle = 0)
    {
        var value = ((uint)iconKind + 1) * 0x9E3779B9u;
        value ^= ((uint)index + 1) * 0x85EBCA6Bu;
        value ^= ((uint)salt + 1) * 0xC2B2AE35u;
        value ^= ((uint)cycle + 1) * 0x27D4EB2Fu;
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value / (double)uint.MaxValue;
    }

    private static double Lerp(double start, double end, double amount)
        => start + ((end - start) * amount);

    private static double SmoothStep(double value)
        => value * value * (3 - (2 * value));

    private static Rect CircleBounds(Point center, double radius)
        => new(center.X - radius, center.Y - radius, radius * 2, radius * 2);

    private static Point PointOnCircle(Point center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new Point(
            center.X + (radius * Math.Cos(radians)),
            center.Y + (radius * Math.Sin(radians)));
    }

    private sealed record SliceLabel(StorageDonutSegment Segment, double StartAngle, double SweepAngle);
}

/// <summary>
/// Provides one labeled slice for <see cref="StorageDonutChart"/>
/// </summary>
public sealed record StorageDonutSegment(
    string Label,
    long Bytes,
    string DisplaySize,
    string Percentage,
    IBrush Brush,
    StorageDonutIconKind IconKind,
    string? Count = null)
{
    public bool HasCount => Count is not null;
}

public enum StorageDonutIconKind
{
    Instances,
    Images,
    Mods,
    News,
    Logs,
    Other
}
