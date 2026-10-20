using System.Windows;
using System.Windows.Media;

namespace DmxPro.App.Controls;

/// <summary>
/// The Shows page audio lane: the song as a block starting at <see cref="OffsetSeconds"/> with its waveform, and the
/// parts of the show without music (before the song starts, after it ends) shaded.
/// </summary>
/// <remarks>
/// Fast with long songs: the waveform is one frozen <see cref="StreamGeometry"/> (one min/max column per pixel, from
/// <see cref="Peaks"/>) built only for the drawn window around the visible part (see
/// <see cref="TimelineViewportElement"/>). It is rebuilt only when zoom, scroll window, size, song or offset change;
/// the playhead moving during playback never touches it (the playhead is a separate transformed element).
/// </remarks>
public sealed class WaveformView : TimelineViewportElement
{
    /// <summary>Gap between the lane's edges and the song block.</summary>
    private const double BlockInset = 2;

    /// <summary>Identifies the <see cref="Peaks"/> dependency property.</summary>
    public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
        nameof(Peaks), typeof(float[]), typeof(WaveformView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPeaksChanged));

    /// <summary>Identifies the <see cref="PeaksPerSecond"/> dependency property.</summary>
    public static readonly DependencyProperty PeaksPerSecondProperty = RenderProperty(nameof(PeaksPerSecond), 100);

    /// <summary>Identifies the <see cref="DurationSeconds"/> dependency property.</summary>
    public static readonly DependencyProperty DurationSecondsProperty = RenderProperty(nameof(DurationSeconds), 0.0);

    /// <summary>Identifies the <see cref="OffsetSeconds"/> dependency property.</summary>
    public static readonly DependencyProperty OffsetSecondsProperty = RenderProperty(nameof(OffsetSeconds), 0.0);

    /// <summary>Identifies the <see cref="WaveBrush"/> dependency property.</summary>
    public static readonly DependencyProperty WaveBrushProperty = RenderProperty<Brush?>(nameof(WaveBrush), null);

    /// <summary>Identifies the <see cref="BlockBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BlockBrushProperty = RenderProperty<Brush?>(nameof(BlockBrush), null);

    /// <summary>Identifies the <see cref="BlockBorderBrush"/> dependency property.</summary>
    public static readonly DependencyProperty BlockBorderBrushProperty = RenderProperty<Brush?>(nameof(BlockBorderBrush), null);

    /// <summary>Identifies the <see cref="ShadeBrush"/> dependency property.</summary>
    public static readonly DependencyProperty ShadeBrushProperty = RenderProperty<Brush?>(nameof(ShadeBrush), null);

    // Reused column buffer (one amplitude per pixel column of the drawn window).
    private float[] _columns = [];

    // 1 / loudest peak, so quiet songs still fill the lane.
    private double _gain = 1;

    private StreamGeometry? _geometry;
    private (double From, double To, double Pps, double Offset, double Height, int PeaksPerSecond, float[]? Peaks) _geometryKey;

    /// <summary>Waveform envelope: peak amplitude 0..1 per bucket (<see cref="PeaksPerSecond"/> buckets per second).</summary>
    public float[]? Peaks
    {
        get => (float[]?)GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public int PeaksPerSecond
    {
        get => (int)GetValue(PeaksPerSecondProperty);
        set => SetValue(PeaksPerSecondProperty, value);
    }

    /// <summary>Song length; 0 = unknown (no block drawn).</summary>
    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    /// <summary>Show time at which the song starts (may be negative: the intro is skipped).</summary>
    public double OffsetSeconds
    {
        get => (double)GetValue(OffsetSecondsProperty);
        set => SetValue(OffsetSecondsProperty, value);
    }

    public Brush? WaveBrush
    {
        get => (Brush?)GetValue(WaveBrushProperty);
        set => SetValue(WaveBrushProperty, value);
    }

    public Brush? BlockBrush
    {
        get => (Brush?)GetValue(BlockBrushProperty);
        set => SetValue(BlockBrushProperty, value);
    }

    public Brush? BlockBorderBrush
    {
        get => (Brush?)GetValue(BlockBorderBrushProperty);
        set => SetValue(BlockBorderBrushProperty, value);
    }

    /// <summary>Fill of the show parts without music.</summary>
    public Brush? ShadeBrush
    {
        get => (Brush?)GetValue(ShadeBrushProperty);
        set => SetValue(ShadeBrushProperty, value);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var pps = SafePixelsPerSecond;
        if (pps <= 0 || !TryBeginRender(out var from, out var to))
        {
            return;
        }

        var height = ActualHeight;
        var offset = double.IsFinite(OffsetSeconds) ? OffsetSeconds : 0;
        var duration = double.IsFinite(DurationSeconds) && DurationSeconds > 0 ? DurationSeconds : 0;
        var songStart = offset * pps;
        var songEnd = (offset + duration) * pps;

        // No music before the song starts and after it ends (or anywhere, when the length is unknown).
        if (ShadeBrush is { } shade)
        {
            var shadeBefore = duration > 0 ? songStart : to;
            FillSpan(drawingContext, shade, from, Math.Min(to, shadeBefore), 0, height);
            if (duration > 0)
            {
                FillSpan(drawingContext, shade, Math.Max(from, songEnd), to, 0, height);
            }
        }

        if (duration <= 0)
        {
            return;
        }

        var blockFrom = Math.Max(from, songStart);
        var blockTo = Math.Min(to, songEnd);
        if (blockTo <= blockFrom)
        {
            return;
        }

        var blockHeight = Math.Max(0, height - (2 * BlockInset));
        if (BlockBrush is { } block)
        {
            FillSpan(drawingContext, block, blockFrom, blockTo, BlockInset, blockHeight);
        }

        if (WaveBrush is { } wave && GetGeometry(blockFrom, blockTo, pps, offset, height) is { } geometry)
        {
            drawingContext.DrawGeometry(wave, null, geometry);
        }

        if (BlockBorderBrush is { } border)
        {
            // Edges drawn separately: a stroked rectangle would also draw the window's clip edges.
            FillSpan(drawingContext, border, blockFrom, blockTo, BlockInset, 1);
            FillSpan(drawingContext, border, blockFrom, blockTo, height - BlockInset - 1, 1);
            if (songStart >= from && songStart <= to)
            {
                drawingContext.DrawRectangle(border, null, new Rect(Math.Round(songStart), BlockInset, 1, blockHeight));
            }

            if (songEnd >= from && songEnd <= to)
            {
                drawingContext.DrawRectangle(border, null, new Rect(Math.Round(songEnd) - 1, BlockInset, 1, blockHeight));
            }
        }
    }

    private static void FillSpan(DrawingContext drawingContext, Brush brush, double from, double to, double top, double height)
    {
        if (to > from && height > 0)
        {
            drawingContext.DrawRectangle(brush, null, new Rect(from, top, to - from, height));
        }
    }

    private static DependencyProperty RenderProperty<T>(string name, T defaultValue) => DependencyProperty.Register(
        name, typeof(T), typeof(WaveformView),
        new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnPeaksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (WaveformView)d;
        var loudest = 0f;
        if (e.NewValue is float[] peaks)
        {
            foreach (var peak in peaks)
            {
                if (peak > loudest && float.IsFinite(peak))
                {
                    loudest = peak;
                }
            }
        }

        view._gain = loudest > 0.001f ? Math.Min(1 / loudest, 20) : 1;
        view._geometry = null;
    }

    /// <summary>The waveform for the pixel span [from, to): the cached one when nothing changed, else a new frozen one.</summary>
    private StreamGeometry? GetGeometry(double from, double to, double pps, double offset, double height)
    {
        var peaks = Peaks;
        var peaksPerSecond = PeaksPerSecond;
        if (peaks is null || peaks.Length == 0 || peaksPerSecond <= 0)
        {
            return null;
        }

        var key = (from, to, pps, offset, height, peaksPerSecond, peaks);
        if (_geometry is not null && _geometryKey == key)
        {
            return _geometry;
        }

        var start = Math.Floor(from);
        var count = (int)Math.Ceiling(to - start);
        if (count <= 0)
        {
            return null;
        }

        if (_columns.Length < count)
        {
            _columns = new float[count];
        }

        // Peak of every bucket a pixel column covers (at least one bucket, so zoomed in it steps smoothly).
        var bucketsPerPixel = peaksPerSecond / pps;
        var lastBucket = peaks.Length - 1;
        for (var i = 0; i < count; i++)
        {
            var songTime = ((start + i) / pps) - offset;
            var first = (int)Math.Floor(songTime * peaksPerSecond);
            var last = (int)Math.Ceiling((songTime * peaksPerSecond) + bucketsPerPixel) - 1;
            first = Math.Clamp(first, 0, lastBucket);
            last = Math.Clamp(Math.Max(last, first), 0, lastBucket);
            var max = 0f;
            for (var b = first; b <= last; b++)
            {
                var peak = peaks[b];
                if (peak > max)
                {
                    max = peak;
                }
            }

            _columns[i] = max;
        }

        var middle = height / 2;
        var half = Math.Max(0, middle - BlockInset - 2);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // Top edge left to right, bottom edge back: one closed shape, mirrored around the middle (peaks are absolute).
            context.BeginFigure(new Point(start, middle - Amplitude(_columns[0], half)), isFilled: true, isClosed: true);
            for (var i = 1; i < count; i++)
            {
                context.LineTo(new Point(start + i, middle - Amplitude(_columns[i], half)), isStroked: false, isSmoothJoin: false);
            }

            for (var i = count - 1; i >= 0; i--)
            {
                context.LineTo(new Point(start + i, middle + Amplitude(_columns[i], half)), isStroked: false, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        _geometry = geometry;
        _geometryKey = key;
        return geometry;
    }

    /// <summary>Half-height of a column in pixels; at least half a pixel so silence still shows a thin line.</summary>
    private double Amplitude(float peak, double half) =>
        Math.Max(0.5, Math.Min(1, (float.IsFinite(peak) ? peak : 0) * _gain) * half);
}
