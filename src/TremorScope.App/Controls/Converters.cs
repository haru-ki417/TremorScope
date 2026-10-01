using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TremorScope.Core.Analysis;

namespace TremorScope.App.Controls;

/// <summary>true → 表示（ConverterParameter=Invert で反転）</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value switch
        {
            bool x => x,
            string s => !string.IsNullOrWhiteSpace(s),
            null => false,
            _ => true,
        };
        if (parameter as string == "Invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>品質の段階 → 色（ConverterParameter=Soft で薄い色）</summary>
public sealed class QualityToBrush : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool soft = parameter as string == "Soft";
        string key = value is QualityLevel q ? q switch
        {
            QualityLevel.Good => "Good",
            QualityLevel.Caution => "Caution",
            _ => "Danger",
        } : "Ink3";
        return Application.Current.TryFindResource(key + (soft ? "SoftBrush" : "Brush")) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>安静時かどうか → 条件の色（ConverterParameter=Soft で薄い色）</summary>
public sealed class ConditionToBrush : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool rest = value is true;
        bool soft = parameter as string == "Soft";
        return Application.Current.TryFindResource((rest ? "Rest" : "Postural") + (soft ? "SoftBrush" : "Brush")) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0〜1 の値 × ConverterParameter（ピクセル）</summary>
public sealed class Scale : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value is double d ? d : 0;
        double k = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 100;
        return Math.Max(2, Math.Clamp(v, 0, 1) * k);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>0〜1 の割合 → Grid の列幅（* の比率）。ConverterParameter=Rest で残りの割合</summary>
public sealed class ShareToStar : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value is double d ? Math.Clamp(d, 0.02, 0.98) : 0.5;
        if (parameter as string == "Rest") v = 1 - v;
        return new GridLength(v, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>今の段階（数値）と ConverterParameter の段階を比べる: Done / Current / Todo</summary>
public sealed class StepState : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int current = value is int i ? i : 0;
        int step = int.TryParse(parameter as string, out var s) ? s : 0;
        return current > step ? "Done" : current == step ? "Current" : "Todo";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
