using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TremorScope.App.ViewModels;
using TremorScope.App.Views;
using TremorScope.Core.Analysis;
using TremorScope.Core.Domain;

namespace TremorScope.App.Services;

/// <summary>レポートに載せる内容</summary>
public sealed record ReportData(
    SessionSummary Session,
    string PatientIdLabel,
    string PatientIdValue,
    string Facility,
    ConditionCard? Rest,
    ConditionCard? Postural,
    byte[] SpectrumPng,
    string AppVersion)
{
    public string MeasuredAt => Labels.LocalTime(Session.MeasuredAtUtc);

    public string Hand => Labels.Hand(Session.Hand);

    public string PatternTitle => Labels.Pattern(Session.Comparison.Pattern);

    public string PatternSummary => Session.Comparison.Summary;

    public string Disclaimer => Labels.Disclaimer;

    public string FacilityLine => string.IsNullOrWhiteSpace(Facility) ? "" : Facility;

    public string Footer => $"TremorScope {AppVersion} · 解析アルゴリズム {TremorAnalyzer.AlgorithmVersion} · 出力 {DateTime.Now:yyyy/MM/dd HH:mm}";

    public ImageSource SpectrumImage
    {
        get
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(SpectrumPng);
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}

/// <summary>
/// レポートを、画面に出さずに決まった大きさの画像にする（画面のキャプチャではないので、
/// ウィンドウの大きさや拡大率によらず、いつも同じレイアウトになる）。
/// </summary>
public static class ReportRenderer
{
    // A4 横。1169 × 827 で並べて、1.5 倍（144dpi）で画像にする → 1754 × 1240 ピクセル（約 150dpi）
    public const double LayoutWidth = 1169;
    public const double LayoutHeight = 827;
    public const int PixelWidth = 1754;
    public const int PixelHeight = 1240;

    private static ReportView Layout(ReportData data, double width, double height)
    {
        var view = new ReportView { DataContext = data, Width = width, Height = height };
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        // 一覧（ItemsControl）の中身や画像の読み込みを待ってから、もう一度並べる
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        view.UpdateLayout();
        return view;
    }

    public static BitmapSource Render(ReportData data)
    {
        var view = Layout(data, LayoutWidth, LayoutHeight);
        var bitmap = new RenderTargetBitmap(PixelWidth, PixelHeight, 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(view);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>DICOM 用に、透明度のない RGB の並びにする</summary>
    public static byte[] ToRgb(BitmapSource bitmap, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        width = converted.PixelWidth;
        height = converted.PixelHeight;
        int stride = width * 3;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    public static bool Print(ReportData data, string jobName)
    {
        var dialog = new PrintDialog();
        dialog.PrintTicket.PageOrientation = PageOrientation.Landscape;
        if (dialog.ShowDialog() != true) return false;

        // 決まった大きさで並べたレポートを、用紙の印刷できる範囲に縮めて印刷する
        double pw = dialog.PrintableAreaWidth, ph = dialog.PrintableAreaHeight;
        var page = new Viewbox
        {
            Width = pw,
            Height = ph,
            Child = new ReportView { DataContext = data, Width = LayoutWidth, Height = LayoutHeight },
        };
        page.Measure(new Size(pw, ph));
        page.Arrange(new Rect(0, 0, pw, ph));
        page.UpdateLayout();
        page.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        page.UpdateLayout();
        dialog.PrintVisual(page, jobName);
        return true;
    }
}
