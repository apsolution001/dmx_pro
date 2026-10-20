using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DmxPro.App.Controls;

/// <summary>
/// One DMX channel as a vertical fader (scene editor, QLC+ style): include check box, channel icon, editable value
/// box, tall slider filled in the channel colour, channel number and name. Template: <c>Themes/ChannelFader.xaml</c>.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is always a whole number between <see cref="Minimum"/> and <see cref="Maximum"/>.
/// When <see cref="IsIncluded"/> is false the fader is dimmed but stays usable: any user change of the value
/// (drag, track click, keys, wheel, typing) sets <see cref="IsIncluded"/> back to true. Values pushed from the
/// binding source never change <see cref="IsIncluded"/>.
/// Keys (slider or value box focused): Up/Down ±1, PageUp/PageDown ±10; on the slider also Right/Left ±1 and
/// Home/End = 255/0. Mouse wheel anywhere on the fader ±1 (Shift ±10).
/// </remarks>
/// <example>
/// <code>
/// &lt;c:ChannelFader Value="{Binding Value}" IsIncluded="{Binding IsIncluded}" IconKind="{Binding IconKind}"
///     TrackColor="{Binding ColorHex}" Number="{Binding Number}" Label="{Binding Name}" Width="52" /&gt;
/// </code>
/// </example>
[TemplatePart(Name = SliderPartName, Type = typeof(RangeBase))]
[TemplatePart(Name = ValueBoxPartName, Type = typeof(TextBox))]
public sealed class ChannelFader : Control
{
    private const string SliderPartName = "PART_Slider";
    private const string ValueBoxPartName = "PART_ValueBox";
    private const double SmallStep = 1;
    private const double LargeStep = 10;

    /// <summary>Cached "0".."255" so updating the value box while dragging allocates nothing.</summary>
    private static readonly string[] ValueTexts = CreateValueTexts();

    /// <summary>Identifies the <see cref="Value"/> dependency property.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(double),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(
            0.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,
            CoerceValue));

    /// <summary>Identifies the <see cref="Minimum"/> dependency property.</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(double),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(0.0, OnRangeChanged));

    /// <summary>Identifies the <see cref="Maximum"/> dependency property.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(255.0, OnRangeChanged));

    /// <summary>Identifies the <see cref="IsIncluded"/> dependency property.</summary>
    public static readonly DependencyProperty IsIncludedProperty = DependencyProperty.Register(
        nameof(IsIncluded),
        typeof(bool),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>Identifies the <see cref="IconKind"/> dependency property.</summary>
    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind),
        typeof(string),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(null));

    /// <summary>Identifies the <see cref="TrackColor"/> dependency property.</summary>
    public static readonly DependencyProperty TrackColorProperty = DependencyProperty.Register(
        nameof(TrackColor),
        typeof(string),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(null));

    /// <summary>Identifies the <see cref="Number"/> dependency property.</summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number),
        typeof(int),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(0, OnNameSourceChanged));

    /// <summary>Identifies the <see cref="Label"/> dependency property.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(null, OnNameSourceChanged));

    /// <summary>Identifies the <see cref="IsWheelEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsWheelEnabledProperty = DependencyProperty.Register(
        nameof(IsWheelEnabled),
        typeof(bool),
        typeof(ChannelFader),
        new FrameworkPropertyMetadata(true));

    private RangeBase? _slider;
    private TextBox? _valueBox;

    /// <summary>
    /// True once the user has typed, pasted or deleted in the value box since its text was last set from
    /// <see cref="Value"/>. Only then does Enter / focus loss commit the text: merely focusing or tabbing through
    /// the box must never turn a (stale) displayed value into a user change, e.g. a Test page override.
    /// </summary>
    private bool _userEdited;

    /// <summary>Initializes a new instance of the <see cref="ChannelFader"/> class.</summary>
    public ChannelFader()
    {
        UpdateAutomationName(this);
    }

    static ChannelFader()
    {
        // The fader itself is not a tab stop; its check box, value box and slider are.
        FocusableProperty.OverrideMetadata(typeof(ChannelFader), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(ChannelFader), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.TabNavigationProperty.OverrideMetadata(
            typeof(ChannelFader),
            new FrameworkPropertyMetadata(KeyboardNavigationMode.Local));
    }

    /// <summary>Gets or sets the channel value (whole number, clamped to <see cref="Minimum"/>..<see cref="Maximum"/>).</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Gets or sets the lowest value (default 0).</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Gets or sets the highest value (default 255).</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets whether the channel is part of the scene. False = dimmed, still editable.</summary>
    public bool IsIncluded
    {
        get => (bool)GetValue(IsIncludedProperty);
        set => SetValue(IsIncludedProperty, value);
    }

    /// <summary>Gets or sets the <see cref="DmxIcon.Kind"/> shown above the value box.</summary>
    public string? IconKind
    {
        get => (string?)GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    /// <summary>Gets or sets the fill colour as <c>#RRGGBB</c>; null or invalid = accent colour.</summary>
    public string? TrackColor
    {
        get => (string?)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    /// <summary>Gets or sets the channel number shown under the slider.</summary>
    public int Number
    {
        get => (int)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    /// <summary>Gets or sets the channel name shown at the bottom (trimmed).</summary>
    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the mouse wheel changes the value (default true). False lets the wheel scroll the
    /// surrounding list instead, e.g. on the Test page where a stray wheel tick must not change live output.
    /// </summary>
    public bool IsWheelEnabled
    {
        get => (bool)GetValue(IsWheelEnabledProperty);
        set => SetValue(IsWheelEnabledProperty, value);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        if (_slider is not null)
        {
            _slider.ValueChanged -= OnSliderValueChanged;
            _slider.PreviewKeyDown -= OnSliderPreviewKeyDown;
            _slider.RemoveHandler(PreviewMouseLeftButtonDownEvent, (MouseButtonEventHandler)OnSliderPreviewMouseDown);
        }

        if (_valueBox is not null)
        {
            _valueBox.PreviewKeyDown -= OnValueBoxPreviewKeyDown;
            _valueBox.PreviewTextInput -= OnValueBoxPreviewTextInput;
            _valueBox.LostKeyboardFocus -= OnValueBoxLostKeyboardFocus;
            DataObject.RemovePastingHandler(_valueBox, OnValueBoxPasting);
        }

        base.OnApplyTemplate();

        _slider = GetTemplateChild(SliderPartName) as RangeBase;
        _valueBox = GetTemplateChild(ValueBoxPartName) as TextBox;

        if (_slider is not null)
        {
            _slider.ValueChanged += OnSliderValueChanged;
            _slider.PreviewKeyDown += OnSliderPreviewKeyDown;

            // handledEventsToo: Slider marks track clicks handled (move-to-point) before instance handlers run.
            _slider.AddHandler(PreviewMouseLeftButtonDownEvent, (MouseButtonEventHandler)OnSliderPreviewMouseDown, true);
        }

        if (_valueBox is not null)
        {
            _valueBox.PreviewKeyDown += OnValueBoxPreviewKeyDown;
            _valueBox.PreviewTextInput += OnValueBoxPreviewTextInput;
            _valueBox.LostKeyboardFocus += OnValueBoxLostKeyboardFocus;
            DataObject.AddPastingHandler(_valueBox, OnValueBoxPasting);
        }

        UpdateValueText(force: true);
    }

    /// <inheritdoc />
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (e.Delta == 0 || !IsWheelEnabled)
        {
            return;
        }

        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? LargeStep : SmallStep;
        SetUserValue(Value + (e.Delta > 0 ? step : -step));
        e.Handled = true;
    }

    private static string[] CreateValueTexts()
    {
        var texts = new string[256];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = i.ToString(CultureInfo.InvariantCulture);
        }

        return texts;
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var fader = (ChannelFader)d;
        var value = (double)baseValue;
        var min = fader.Minimum;
        var max = Math.Max(min, fader.Maximum);
        if (double.IsNaN(value))
        {
            return min;
        }

        return Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), min, max);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ChannelFader)d).UpdateValueText(force: false);

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.CoerceValue(ValueProperty);

    private static void OnNameSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        UpdateAutomationName((ChannelFader)d);

    private static void UpdateAutomationName(ChannelFader fader)
    {
        var label = fader.Label;
        var name = string.IsNullOrWhiteSpace(label)
            ? string.Format(CultureInfo.CurrentCulture, "Channel {0}", fader.Number)
            : string.Format(CultureInfo.CurrentCulture, "{0} {1}", label, fader.Number);
        fader.SetCurrentValue(AutomationProperties.NameProperty, name);
    }

    /// <summary>A value change made by the user: include the channel, then apply the (coerced) value.</summary>
    private void SetUserValue(double value)
    {
        SetCurrentValue(IsIncludedProperty, true);
        SetCurrentValue(ValueProperty, value);
    }

    private void OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Drag and track click change the slider while the left button is down on it; changes pushed from the
        // binding source arrive without that, so they never touch IsIncluded.
        if (_slider is not null
            && (_slider.IsMouseOver || _slider.IsMouseCaptureWithin)
            && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            SetCurrentValue(IsIncludedProperty, true);
        }
    }

    private void OnSliderPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking the fader gives it keyboard focus, so arrow keys work right after (and the value box commits).
        if (_slider is not null && !_slider.IsKeyboardFocused)
        {
            _slider.Focus();
        }
    }

    private void OnSliderPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handled here (not by Slider) so Home/End map to 255/0 and every key change includes the channel.
        double? target = e.Key switch
        {
            Key.Up or Key.Right => Value + SmallStep,
            Key.Down or Key.Left => Value - SmallStep,
            Key.PageUp => Value + LargeStep,
            Key.PageDown => Value - LargeStep,
            Key.Home => Maximum,
            Key.End => Minimum,
            _ => null,
        };

        if (target is { } value)
        {
            SetUserValue(value);
            e.Handled = true;
        }
    }

    private void OnValueBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Back or Key.Delete or Key.Space:
                _userEdited = true;
                break;
            case Key.X when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                _userEdited = true;
                break;
            case Key.Enter:
                CommitValueText();
                _valueBox?.SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                UpdateValueText(force: true);
                _valueBox?.SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
                StepFromValueBox(SmallStep);
                e.Handled = true;
                break;
            case Key.Down:
                StepFromValueBox(-SmallStep);
                e.Handled = true;
                break;
            case Key.PageUp:
                StepFromValueBox(LargeStep);
                e.Handled = true;
                break;
            case Key.PageDown:
                StepFromValueBox(-LargeStep);
                e.Handled = true;
                break;
        }
    }

    private void OnValueBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitValueText();

    private void OnValueBoxPreviewTextInput(object sender, TextCompositionEventArgs e) => _userEdited = true;

    private void OnValueBoxPasting(object sender, DataObjectPastingEventArgs e) => _userEdited = true;

    private void StepFromValueBox(double delta)
    {
        CommitValueText();
        SetUserValue(Value + delta);
        UpdateValueText(force: true);
        _valueBox?.SelectAll();
    }

    /// <summary>Parses the value box; a valid number becomes a user change, anything else reverts the text.</summary>
    private void CommitValueText()
    {
        if (_valueBox is null)
        {
            return;
        }

        if (!_userEdited)
        {
            // Nothing typed: just show the live value again, never commit what was displayed.
            UpdateValueText(force: true);
            return;
        }

        var text = _valueBox.Text.Trim();
        if (text != CurrentValueText()
            && double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed)
            && !double.IsNaN(parsed))
        {
            SetUserValue(parsed);
        }

        UpdateValueText(force: true);
    }

    /// <summary>
    /// Shows the current value in the box. While the user is editing it (focused and <see cref="_userEdited"/>),
    /// only a forced update replaces the text; a forced update also ends the edit.
    /// </summary>
    private void UpdateValueText(bool force)
    {
        if (_valueBox is null || (!force && _userEdited && _valueBox.IsKeyboardFocusWithin))
        {
            return;
        }

        if (force)
        {
            _userEdited = false;
        }

        var text = CurrentValueText();
        if (!string.Equals(_valueBox.Text, text, StringComparison.Ordinal))
        {
            _valueBox.Text = text;
        }
    }

    private string CurrentValueText()
    {
        var whole = (int)Value;
        return whole is >= 0 and < 256
            ? ValueTexts[whole]
            : whole.ToString(CultureInfo.InvariantCulture);
    }
}
