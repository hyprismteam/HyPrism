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
    private const double TargetFramesPerSecond = 60;
    private static readonly TimeSpan TargetFrameInterval =
        TimeSpan.FromSeconds(1 / TargetFramesPerSecond);
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
    private int _staticSceneBuildCount;
    private TimeSpan? _animationStartedAt;
    private TimeSpan? _nextAnimationFrameAt;
    private double _animationSeconds;
    private Action<TimeSpan>? _animationFrameCallback;
    private TopLevel? _topLevel;
    private ChartScene? _scene;
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

    public static readonly StyledProperty<FontFamily> LabelFontFamilyProperty =
        AvaloniaProperty.Register<StorageDonutChart, FontFamily>(
            nameof(LabelFontFamily),
            FontFamily.Default);

    public static readonly StyledProperty<bool> IsAnimationEnabledProperty =
        AvaloniaProperty.Register<StorageDonutChart, bool>(nameof(IsAnimationEnabled), true);

    static StorageDonutChart()
    {
        AffectsRender<StorageDonutChart>(
            ItemsProperty,
            TrackBrushProperty,
            HoleBrushProperty,
            SeparatorBrushProperty,
            SeparatorThicknessProperty,
            InnerRadiusRatioProperty,
            LabelFontFamilyProperty);
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

    public FontFamily LabelFontFamily
    {
        get => GetValue(LabelFontFamilyProperty);
        set => SetValue(LabelFontFamilyProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get => GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    internal int LoadedParticleIconCount => _particleIcons.Count;
    internal int StaticSceneBuildCount => _staticSceneBuildCount;
    internal int CachedParticleCount => _scene?.Particles.Count ?? 0;
    internal bool IsParticleAnimationRunning => _animationFrameCallback is not null;

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
        _topLevel = TopLevel.GetTopLevel(this);
        _scene = null;
        StartAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopAnimation();
        _topLevel = null;
        _scene = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        _scene = null;
        base.OnSizeChanged(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty ||
            change.Property == TrackBrushProperty ||
            change.Property == HoleBrushProperty ||
            change.Property == SeparatorBrushProperty ||
            change.Property == SeparatorThicknessProperty ||
            change.Property == InnerRadiusRatioProperty ||
            change.Property == LabelFontFamilyProperty)
        {
            _scene = null;
        }

        if (change.Property == IsAnimationEnabledProperty)
        {
            if (IsAnimationEnabled)
                StartAnimation();
            else
                StopAnimation();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        _scene ??= BuildScene();
        _scene.Underlay.Draw(context);
        for (var index = 0; index < _scene.Particles.Count; index++)
            _scene.Particles[index].Draw(context, _scene.Center, _animationSeconds);
        _scene.Overlay.Draw(context);
    }

    internal static double GetPreferredLabelFontSize(double share)
        => Math.Clamp(5 + (18 * Math.Sqrt(Math.Clamp(share, 0, 1))), 6, 18);

    private ChartScene BuildScene()
    {
        _staticSceneBuildCount++;
        var underlay = new DrawingGroup();
        var overlay = new DrawingGroup();
        var diameter = Math.Min(Bounds.Width, Bounds.Height);
        if (diameter <= 0)
            return new ChartScene(underlay, overlay, default, []);

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var separatorThickness = Math.Clamp(SeparatorThickness, 0, diameter / 12);
        var radius = Math.Max(0, (diameter - separatorThickness) / 2);
        if (radius <= 0)
            return new ChartScene(underlay, overlay, center, []);

        var circleBounds = CircleBounds(center, radius);
        var separatorPen = SeparatorBrush is null || separatorThickness <= 0
            ? null
            : new Pen(SeparatorBrush, separatorThickness, lineJoin: PenLineJoin.Round);
        var visibleItems = Items.Where(item => item.Bytes > 0).ToArray();
        var total = visibleItems.Sum(item => (double)item.Bytes);
        var slices = BuildSlices(visibleItems, total);

        using (var drawingContext = underlay.Open())
        {
            if (TrackBrush is not null)
                drawingContext.DrawEllipse(TrackBrush, null, circleBounds);

            foreach (var slice in slices)
            {
                if (slice.SweepAngle >= 360)
                {
                    drawingContext.DrawEllipse(slice.Segment.Brush, separatorPen, circleBounds);
                    continue;
                }

                drawingContext.DrawGeometry(
                    slice.Segment.Brush,
                    separatorPen,
                    CreateSectorGeometry(center, radius, slice.StartAngle, slice.SweepAngle));
            }
        }

        var innerRadius = radius * Math.Clamp(InnerRadiusRatio, 0.25, 0.75);
        using (var drawingContext = overlay.Open())
        {
            if (HoleBrush is not null)
                drawingContext.DrawEllipse(HoleBrush, separatorPen, CircleBounds(center, innerRadius));
            DrawLabels(drawingContext, center, radius, innerRadius, slices);
        }

        return new ChartScene(
            underlay,
            overlay,
            center,
            BuildParticles(radius, innerRadius, slices));
    }

    private static IReadOnlyList<SliceLayout> BuildSlices(
        IReadOnlyList<StorageDonutSegment> visibleItems,
        double total)
    {
        if (total <= 0)
            return [];

        if (visibleItems.Count == 1)
            return [new SliceLayout(visibleItems[0], -90, 360)];

        var slices = new List<SliceLayout>(visibleItems.Count);
        var currentAngle = -90d;
        foreach (var item in visibleItems)
        {
            var sweepAngle = 360 * item.Bytes / total;
            slices.Add(new SliceLayout(item, currentAngle, sweepAngle));
            currentAngle += sweepAngle;
        }

        return slices;
    }

    private static StreamGeometry CreateSectorGeometry(
        Point center,
        double radius,
        double startAngle,
        double sweepAngle)
    {
        var geometry = new StreamGeometry();
        using var geometryContext = geometry.Open();
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
        return geometry;
    }

    private void DrawLabels(
        DrawingContext context,
        Point center,
        double outerRadius,
        double innerRadius,
        IReadOnlyList<SliceLayout> slices)
    {
        foreach (var slice in slices)
        {
            var share = slice.SweepAngle / 360;
            if (share < 0.01)
                continue;

            var ringWidth = outerRadius - innerRadius;
            var normalizedShare = Math.Clamp(share / 0.12, 0, 1);
            var radiusFactor = 0.8 - (0.25 * normalizedShare);
            var labelRadius = innerRadius + (ringWidth * radiusFactor);
            var availableWidth = Math.Max(
                0,
                (2 * labelRadius * Math.Sin(slice.SweepAngle * Math.PI / 360)) - 4);
            var fontSize = GetPreferredLabelFontSize(share);
            var text = CreateLabelText(slice, fontSize);
            while (fontSize > 5.5 && text.Width > availableWidth)
            {
                fontSize = Math.Max(5.5, fontSize - 0.5);
                text = CreateLabelText(slice, fontSize);
            }

            if (text.Width > availableWidth)
                continue;

            var labelCenter = PointOnCircle(
                center,
                labelRadius,
                slice.StartAngle + (slice.SweepAngle / 2));
            context.DrawText(
                text,
                new Point(labelCenter.X - (text.Width / 2), labelCenter.Y - (text.Height / 2)));
        }
    }

    private FormattedText CreateLabelText(SliceLayout slice, double fontSize)
        => new(
            slice.Segment.Percentage,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(LabelFontFamily, FontStyle.Normal, FontWeight.Medium),
            fontSize,
            GetLabelBrush(slice.Segment.Brush));

    private static IBrush GetLabelBrush(IBrush segmentBrush)
    {
        if (segmentBrush is not ISolidColorBrush solidBrush)
            return Brushes.White;

        var color = solidBrush.Color;
        var luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;
        return luminance > 0.72 ? Brushes.Black : Brushes.White;
    }

    private IReadOnlyList<Particle> BuildParticles(
        double outerRadius,
        double innerRadius,
        IReadOnlyList<SliceLayout> slices)
    {
        var ringWidth = outerRadius - innerRadius;
        if (ringWidth < 18)
            return [];

        var particles = new List<Particle>();
        foreach (var slice in slices)
        {
            var share = slice.SweepAngle / 360;
            if (share < 0.025 ||
                !_particleIcons.TryGetValue(slice.Segment.IconKind, out var icon))
            {
                continue;
            }

            var particleCount = Math.Clamp((int)Math.Round(share * 20), 1, 14);
            for (var index = 0; index < particleCount; index++)
            {
                particles.Add(new Particle(
                    slice.Segment.IconKind,
                    index,
                    icon,
                    slice.StartAngle,
                    slice.SweepAngle,
                    innerRadius,
                    ringWidth));
            }
        }

        return particles;
    }

    private void StartAnimation()
    {
        if (_topLevel is null || !IsAnimationEnabled || _animationFrameCallback is not null)
            return;

        _animationStartedAt = null;
        _nextAnimationFrameAt = null;
        var animationVersion = ++_animationVersion;
        _animationFrameCallback = timestamp => OnAnimationFrame(timestamp, animationVersion);
        _topLevel.RequestAnimationFrame(_animationFrameCallback);
    }

    private void StopAnimation()
    {
        _animationVersion++;
        _animationFrameCallback = null;
        _animationStartedAt = null;
        _nextAnimationFrameAt = null;
    }

    private void OnAnimationFrame(TimeSpan timestamp, int animationVersion)
    {
        if (animationVersion != _animationVersion ||
            _animationFrameCallback is null ||
            !this.IsAttachedToVisualTree())
        {
            return;
        }

        if (!IsAnimationEnabled)
        {
            StopAnimation();
            return;
        }

        if (IsEffectivelyVisible)
        {
            _animationStartedAt ??= timestamp;
            _nextAnimationFrameAt ??= timestamp;
            if (timestamp >= _nextAnimationFrameAt.Value)
            {
                _animationSeconds = (timestamp - _animationStartedAt.Value).TotalSeconds;
                do
                {
                    _nextAnimationFrameAt += TargetFrameInterval;
                }
                while (_nextAnimationFrameAt <= timestamp);

                InvalidateVisual();
            }
        }
        else
        {
            _animationStartedAt = null;
            _nextAnimationFrameAt = null;
        }

        _topLevel?.RequestAnimationFrame(_animationFrameCallback);
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

    private sealed record SliceLayout(
        StorageDonutSegment Segment,
        double StartAngle,
        double SweepAngle);

    private sealed record ChartScene(
        DrawingGroup Underlay,
        DrawingGroup Overlay,
        Point Center,
        IReadOnlyList<Particle> Particles);

    private sealed class Particle
    {
        private readonly StorageDonutIconKind _iconKind;
        private readonly int _index;
        private readonly Geometry _icon;
        private readonly Point _iconCenter;
        private readonly double _phase;
        private readonly double _speed;
        private readonly double _startAngle;
        private readonly double _sweepAngle;
        private readonly double _innerRadius;
        private readonly double _ringWidth;
        private readonly double _angularMargin;
        private int _cycle = int.MinValue;
        private double _anchorAngle;
        private double _angularDrift;
        private double _anchorRadius;
        private double _radialDrift;
        private double _scale;

        public Particle(
            StorageDonutIconKind iconKind,
            int index,
            Geometry icon,
            double startAngle,
            double sweepAngle,
            double innerRadius,
            double ringWidth)
        {
            _iconKind = iconKind;
            _index = index;
            _icon = icon;
            _iconCenter = icon.Bounds.Center;
            _phase = GetParticlePhase(iconKind, index);
            _speed = 0.15 + (Noise(iconKind, index, 2) * 0.13);
            _startAngle = startAngle;
            _sweepAngle = sweepAngle;
            _innerRadius = innerRadius;
            _ringWidth = ringWidth;
            _angularMargin = Math.Clamp(10 / sweepAngle, 0.05, 0.3);
        }

        public void Draw(DrawingContext context, Point chartCenter, double animationSeconds)
        {
            var age = (animationSeconds * _speed) + _phase;
            var cycle = (int)Math.Floor(age);
            if (cycle != _cycle)
                UpdateCycle(cycle);

            var life = age - cycle;
            var fadeIn = SmoothStep(Math.Clamp(life / 0.18, 0, 1));
            var fadeOut = SmoothStep(Math.Clamp((1 - life) / 0.24, 0, 1));
            var brushIndex = Math.Clamp(
                (int)Math.Round(fadeIn * fadeOut * (ParticleBrushes.Count - 1)),
                0,
                ParticleBrushes.Count - 1);
            if (brushIndex == 0)
                return;

            var easedLife = SmoothStep(life);
            var point = PointOnCircle(
                chartCenter,
                _anchorRadius + (_radialDrift * easedLife),
                _anchorAngle + (_angularDrift * easedLife));
            var transform = Matrix.CreateTranslation(-_iconCenter.X, -_iconCenter.Y)
                * Matrix.CreateScale(_scale, _scale)
                * Matrix.CreateTranslation(point.X, point.Y);
            using (context.PushTransform(transform))
                context.DrawGeometry(ParticleBrushes[brushIndex], null, _icon);
        }

        private void UpdateCycle(int cycle)
        {
            _cycle = cycle;
            var anchorAngularPosition = Lerp(
                _angularMargin,
                1 - _angularMargin,
                Noise(_iconKind, _index, 3, cycle));
            _anchorAngle = _startAngle + (_sweepAngle * anchorAngularPosition);
            _angularDrift = (Noise(_iconKind, _index, 4, cycle) - 0.5) *
                Math.Min(14, _sweepAngle * 0.16);
            var anchorRadiusPosition = Lerp(
                0.15,
                0.85,
                Noise(_iconKind, _index, 5, cycle));
            _anchorRadius = _innerRadius + (_ringWidth * anchorRadiusPosition);
            _radialDrift = (Noise(_iconKind, _index, 6, cycle) - 0.5) * _ringWidth * 0.24;
            var size = 6.5 + (Noise(_iconKind, _index, 7, cycle) * 3);
            _scale = size / Math.Max(_icon.Bounds.Width, _icon.Bounds.Height);
        }
    }
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
