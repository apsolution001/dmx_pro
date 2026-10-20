using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DmxPro.App.ViewModels;
using DmxPro.Core.Stage;

namespace DmxPro.App.Controls;

/// <summary>
/// The drawing of one light on the <see cref="StageCanvas"/>: a glow visual (beam cone + soft radial glow, drawn under
/// every body) and a body visual (fixture housing, lens, selection outline, name).
/// </summary>
/// <remarks>
/// Each light owns a handful of unfrozen brushes created once. The drawings are recorded only when the zoom, the names
/// or the selection change (<see cref="Render"/>); a new frame just sets the colours of those brushes
/// (<see cref="UpdateColor"/>), which WPF forwards to the render thread without re-recording anything or allocating.
/// Both visuals share one <see cref="TranslateTransform"/>, so moving a light is a single property change.
/// </remarks>
internal sealed class StageLightVisual
{
    /// <summary>Housing radius in metres (a typical PAR is ~0.3–0.4 m wide).</summary>
    private const double BodyRadiusMetres = 0.18;

    private const double GlowRadiusMetres = 1.0;

    private const double BeamLengthMetres = 1.6;

    private const double NameFontSize = 11;

    private static readonly Color LensOff = Color.FromRgb(0x2E, 0x32, 0x38);
    private static readonly Brush HousingBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x22, 0x27)));
    private static readonly Pen HousingPen = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x7D))), 1));
    private static readonly Brush NameBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD5, 0xD9, 0xDF)));
    private static readonly Brush NameBackground = Frozen(new SolidColorBrush(Color.FromArgb(0xA0, 0x12, 0x14, 0x17)));
    private static readonly Typeface NameTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly GradientStop _glowCenter = new(Colors.Transparent, 0);
    private readonly GradientStop _glowMiddle = new(Colors.Transparent, 0.4);
    private readonly GradientStop _glowEdge = new(Colors.Transparent, 1);
    private readonly GradientStop _beamStart = new(Colors.Transparent, 0);
    private readonly GradientStop _beamEnd = new(Colors.Transparent, 1);
    private readonly RadialGradientBrush _glowBrush;
    private readonly LinearGradientBrush _beamBrush;
    private readonly SolidColorBrush _lensBrush = new(LensOff);
    private StageLightState _last;
    private bool _hasColor;

    public StageLightVisual(StageLightViewModel light)
    {
        Light = light;
        _glowBrush = new RadialGradientBrush(new GradientStopCollection { _glowCenter, _glowMiddle, _glowEdge });
        _beamBrush = new LinearGradientBrush(new GradientStopCollection { _beamStart, _beamEnd }, new Point(0.5, 0), new Point(0.5, 1));
        Glow.Transform = Position;
        Body.Transform = Position;
    }

    public StageLightViewModel Light { get; }

    public DrawingVisual Glow { get; } = new();

    public DrawingVisual Body { get; } = new();

    /// <summary>Centre of the light in world pixels (stage top-left = 0,0).</summary>
    public TranslateTransform Position { get; } = new();

    /// <summary>True while the visuals are in the canvas layers (the light is placed).</summary>
    public bool IsShown { get; set; }

    /// <summary>Housing radius in pixels at a zoom of <paramref name="scale"/> px per metre.</summary>
    public static double BodyRadius(double scale) => Math.Clamp(BodyRadiusMetres * scale, 5, 40);

    /// <summary>Records both drawings for the given zoom. Not per frame: zoom, names, selection or rotation changes.</summary>
    public void Render(double scale, bool showNames, Pen? selectionPen, double pixelsPerDip)
    {
        var body = BodyRadius(scale);
        var glow = Math.Clamp(GlowRadiusMetres * scale, 14, 500);
        var beamLength = Math.Clamp(BeamLengthMetres * scale, 20, 800);
        var beamHalf = beamLength * 0.35;

        using (var dc = Glow.RenderOpen())
        {
            var rotation = Light.Rotation;
            var rotated = rotation != 0;
            if (rotated)
            {
                dc.PushTransform(Frozen(new RotateTransform(rotation)));
            }

            // Downstage (towards the audience) is +Y on screen; 0° points the beam there.
            var cone = new StreamGeometry();
            using (var g = cone.Open())
            {
                g.BeginFigure(new Point(-body * 0.6, 0), isFilled: true, isClosed: true);
                g.LineTo(new Point(body * 0.6, 0), isStroked: false, isSmoothJoin: false);
                g.LineTo(new Point(beamHalf, beamLength), isStroked: false, isSmoothJoin: false);
                g.LineTo(new Point(-beamHalf, beamLength), isStroked: false, isSmoothJoin: false);
            }

            cone.Freeze();
            dc.DrawGeometry(_beamBrush, null, cone);
            if (rotated)
            {
                dc.Pop();
            }

            dc.DrawEllipse(_glowBrush, null, default, glow, glow);
        }

        using (var dc = Body.RenderOpen())
        {
            var side = body * 2;
            dc.DrawRoundedRectangle(HousingBrush, HousingPen, new Rect(-body, -body, side, side), body * 0.35, body * 0.35);
            dc.DrawEllipse(_lensBrush, null, default, body * 0.68, body * 0.68);
            if (selectionPen is not null)
            {
                var ring = body + 4;
                dc.DrawRoundedRectangle(null, selectionPen, new Rect(-ring, -ring, ring * 2, ring * 2), ring * 0.4, ring * 0.4);
            }

            if (showNames)
            {
                var text = new FormattedText(
                    Light.Name,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    NameTypeface,
                    NameFontSize,
                    NameBrush,
                    pixelsPerDip)
                {
                    MaxTextWidth = Math.Max(60, scale * 1.6),
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                var top = body + 4;
                var width = text.Width;
                dc.DrawRoundedRectangle(NameBackground, null, new Rect(-width / 2 - 3, top - 1, width + 6, text.Height + 2), 2, 2);
                dc.DrawText(text, new Point(-width / 2, top));
            }
        }
    }

    /// <summary>Applies <see cref="StageLightViewModel.Current"/> to the brushes. Per frame; allocation-free.</summary>
    public void UpdateColor()
    {
        var state = Light.Current;
        if (_hasColor && state == _last)
        {
            return;
        }

        var hueChanged = !_hasColor || state.R != _last.R || state.G != _last.G || state.B != _last.B;
        _hasColor = true;
        _last = state;
        var intensity = float.IsNaN(state.Intensity) ? 0f : Math.Clamp(state.Intensity, 0f, 1f);
        var r = state.R;
        var g = state.G;
        var b = state.B;

        _glowCenter.Color = Color.FromArgb(Alpha(intensity * 0.85f), r, g, b);
        _glowMiddle.Color = Color.FromArgb(Alpha(intensity * 0.32f), r, g, b);
        _beamStart.Color = Color.FromArgb(Alpha(intensity * 0.45f), r, g, b);
        if (hueChanged)
        {
            // Transparent ends keep the hue so the fade does not turn grey; only a hue change touches them.
            _glowEdge.Color = Color.FromArgb(0, r, g, b);
            _beamEnd.Color = Color.FromArgb(0, r, g, b);
        }

        // Lens: the dark lens blended towards the emitted colour by the brightness.
        _lensBrush.Color = Color.FromRgb(Mix(LensOff.R, r, intensity), Mix(LensOff.G, g, intensity), Mix(LensOff.B, b, intensity));
    }

    private static byte Alpha(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static byte Mix(byte from, byte to, float amount) =>
        (byte)Math.Clamp((int)MathF.Round(from + ((to - from) * amount)), 0, 255);

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
