using ScottPlot;
using TremorScope.Core.Analysis;

namespace TremorScope.App.Services;

/// <summary>グラフの見た目を、画面の色と書体にそろえる</summary>
public static class PlotTheme
{
    public static readonly Color Ink = Color.FromHex("#17202E");
    public static readonly Color Ink3 = Color.FromHex("#8A909A");
    public static readonly Color Line = Color.FromHex("#E6E3DC");
    public static readonly Color Rest = Color.FromHex("#0E8A8A");
    public static readonly Color Postural = Color.FromHex("#C0661A");
    public static readonly Color Surface = Color.FromHex("#FFFFFF");

    public static void Apply(Plot plot)
    {
        ArgumentNullException.ThrowIfNull(plot);
        plot.FigureBackground.Color = Surface;
        plot.DataBackground.Color = Surface;
        plot.Axes.Color(Ink3);
        plot.Axes.FrameColor(Line);
        plot.Grid.MajorLineColor = Line;
        plot.Grid.MinorLineColor = Colors.Transparent;
        plot.Legend.BackgroundColor = Surface.WithAlpha(0.9);
        plot.Legend.OutlineColor = Line;
        plot.Legend.FontColor = Ink;
    }

    /// <summary>安静時・姿勢時のスペクトルを重ねて描く</summary>
    public static void DrawSpectrum(Plot plot, AnalysisResult? rest, AnalysisResult? postural, float lineWidth = 2.5f)
    {
        ArgumentNullException.ThrowIfNull(plot);
        plot.Clear();
        Apply(plot);
        var band = plot.Add.HorizontalSpan(4, 6, Rest.WithAlpha(0.08));
        band.LineWidth = 0;
        band.LegendText = "4–6 Hz";

        double max = 0;
        foreach (var (a, color, label) in new[] { (rest, Rest, "安静時"), (postural, Postural, "姿勢時") })
        {
            if (a is null) continue;
            var f = a.Spectrum.Frequencies;
            var idx = Enumerable.Range(0, f.Length).Where(i => f[i] >= 0.5 && f[i] <= 15).ToArray();
            var xs = idx.Select(i => f[i]).ToArray();
            // 振幅密度（mg/√Hz）: パワーの平方根なので、小さなふるえも見えやすい
            var ys = idx.Select(i => Math.Sqrt(Math.Max(0, a.Spectrum.Density[i])) * 1000).ToArray();
            if (ys.Length == 0) continue;
            max = Math.Max(max, ys.Max());
            var line = plot.Add.ScatterLine(xs, ys, color);
            line.LineWidth = lineWidth;
            line.MarkerSize = 0;
            line.LegendText = label;
        }

        plot.Axes.SetLimits(0, 15, 0, max > 0 ? max * 1.15 : 1);
        plot.XLabel("周波数（Hz）", 13);
        plot.YLabel("振幅密度（mg/√Hz）", 13);
        plot.ShowLegend(Alignment.UpperRight);
        plot.Font.Automatic();
    }

    public static byte[] SpectrumImage(AnalysisResult? rest, AnalysisResult? postural, int width, int height)
    {
        var plot = new Plot();
        DrawSpectrum(plot, rest, postural, 3f);
        plot.Axes.Bottom.Label.FontSize = 20;
        plot.Axes.Left.Label.FontSize = 20;
        plot.Axes.Bottom.TickLabelStyle.FontSize = 17;
        plot.Axes.Left.TickLabelStyle.FontSize = 17;
        plot.Legend.FontSize = 18;
        plot.Font.Automatic();
        return plot.GetImageBytes(width, height, ImageFormat.Png);
    }

    /// <summary>経過のグラフ（日付ごとの、ふるえの大きさ）</summary>
    public static void DrawTrend(Plot plot, IReadOnlyList<SessionSummaryPoint> points)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(points);
        plot.Clear();
        Apply(plot);
        plot.Axes.DateTimeTicksBottom();
        foreach (var (pick, color, label) in new (Func<SessionSummaryPoint, double?>, Color, string)[]
                 { (p => p.RestMg, Rest, "安静時"), (p => p.PosturalMg, Postural, "姿勢時") })
        {
            var pts = points.Where(p => pick(p) is not null).OrderBy(p => p.When).ToArray();
            if (pts.Length == 0) continue;
            var s = plot.Add.Scatter(pts.Select(p => p.When.ToOADate()).ToArray(), pts.Select(p => pick(p)!.Value).ToArray(), color);
            s.LineWidth = 2.5f;
            s.MarkerSize = 8;
            s.LegendText = label;
        }
        if (points.Count > 0)
        {
            double lo = points.Min(p => p.When).AddDays(-1).ToOADate();
            double hi = points.Max(p => p.When).AddDays(1).ToOADate();
            double ymax = points.SelectMany(p => new[] { p.RestMg ?? 0, p.PosturalMg ?? 0 }).DefaultIfEmpty(1).Max();
            plot.Axes.SetLimits(lo, hi, 0, Math.Max(ymax * 1.2, 5));
        }
        plot.YLabel("ふるえの大きさ（mg）", 13);
        plot.ShowLegend(Alignment.UpperLeft);
        plot.Font.Automatic();
    }

    /// <summary>測定中の波形（流れるグラフ）の見た目</summary>
    public static ScottPlot.Plottables.DataStreamer CreateLive(Plot plot, int points, double sampleRate)
    {
        ArgumentNullException.ThrowIfNull(plot);
        plot.Clear();
        Apply(plot);
        var streamer = plot.Add.DataStreamer(points, 1 / sampleRate);
        streamer.Color = Rest;
        streamer.LineWidth = 2f;
        streamer.ViewScrollLeft();
        streamer.ManageAxisLimits = false;
        plot.Axes.SetLimits(0, points / sampleRate, -60, 60);
        plot.YLabel("mg", 12);
        plot.Axes.Bottom.TickLabelStyle.IsVisible = false;
        plot.Font.Automatic();
        return streamer;
    }

    public static Color For(TremorScope.Core.Domain.Condition c) => c == TremorScope.Core.Domain.Condition.Rest ? Rest : Postural;
}

public sealed record SessionSummaryPoint(DateTime When, double? RestMg, double? PosturalMg)
{
    public static SessionSummaryPoint From(TremorScope.Core.Domain.SessionSummary s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.MeasuredAtUtc.ToLocalTime(), s.Rest?.RmsAccelerationMg, s.Postural?.RmsAccelerationMg);
    }
}
