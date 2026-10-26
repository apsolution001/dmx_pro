namespace DmxPro.App.ViewModels.Editors;

/// <summary>
/// One corner of an effect editor's on-screen preview curve: <paramref name="X"/> 0..1 across the preview window,
/// <paramref name="Level"/> 0..1 brightness.
/// </summary>
/// <param name="X">Position across the preview window, 0 (left) .. 1 (right).</param>
/// <param name="Level">Brightness, 0 (off) .. 1 (full).</param>
public readonly record struct EffectPreviewPoint(double X, double Level);
