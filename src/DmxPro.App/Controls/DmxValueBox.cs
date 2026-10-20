using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DmxPro.Core.Functions;

namespace DmxPro.App.Controls;

/// <summary>
/// Small text box for one DMX level 0–255 (next to a horizontal slider). Typing commits on Enter or focus loss
/// (rounded and clamped, e.g. 300 → 255); text that is not a number reverts; Esc reverts. Up/Down change the value
/// by 1, PageUp/PageDown by 10.
/// </summary>
/// <example>
/// <code>&lt;c:DmxValueBox Value="{Binding Value}" AutomationProperties.Name="Red level" Width="56" /&gt;</code>
/// </example>
public sealed class DmxValueBox : TextBox
{
    /// <summary>Identifies the <see cref="Value"/> dependency property.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(int),
        typeof(DmxValueBox),
        new FrameworkPropertyMetadata(
            0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,
            CoerceValue));

    private const int SmallStep = 1;
    private const int LargeStep = 10;

    /// <summary>Initializes a new instance of the <see cref="DmxValueBox"/> class with the app's TextBox style.</summary>
    public DmxValueBox()
    {
        // Implicit styles are looked up by exact type; take the themed TextBox style (dark + light).
        SetResourceReference(StyleProperty, typeof(TextBox));
        MaxLength = 5;
        HorizontalContentAlignment = HorizontalAlignment.Right;
        InputMethod.SetIsInputMethodEnabled(this, false);
        Text = "0";
    }

    /// <summary>Gets or sets the level, always a whole number 0–255.</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                SelectAll();
                e.Handled = true;
                break;
            case Key.Escape when Text != Format(Value):
                ShowValue();
                SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
                Step(SmallStep);
                e.Handled = true;
                break;
            case Key.Down:
                Step(-SmallStep);
                e.Handled = true;
                break;
            case Key.PageUp:
                Step(LargeStep);
                e.Handled = true;
                break;
            case Key.PageDown:
                Step(-LargeStep);
                e.Handled = true;
                break;
        }
    }

    /// <inheritdoc />
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Commit();
    }

    /// <inheritdoc />
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SelectAll();
    }

    private static object CoerceValue(DependencyObject d, object baseValue) => Math.Clamp((int)baseValue, 0, 255);

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (DmxValueBox)d;
        if (!box.IsKeyboardFocusWithin)
        {
            box.ShowValue();
        }
    }

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);

    private void Step(int delta)
    {
        Commit();
        SetCurrentValue(ValueProperty, Math.Clamp(Value + delta, 0, 255));
        ShowValue();
        SelectAll();
    }

    /// <summary>A valid number becomes the value (rounded, clamped); anything else reverts the text.</summary>
    private void Commit()
    {
        if (Text != Format(Value) && SceneEditing.TryParseDmxValue(Text, out var parsed))
        {
            SetCurrentValue(ValueProperty, (int)parsed);
        }

        ShowValue();
    }

    private void ShowValue()
    {
        var text = Format(Value);
        if (!string.Equals(Text, text, StringComparison.Ordinal))
        {
            Text = text;
        }
    }
}
