using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ScottPlot.Plottables;
using TremorScope.App.Services;
using TremorScope.App.ViewModels;

namespace TremorScope.App.Views;

/// <summary>測定画面。流れる波形のグラフだけ、ここで直接描く</summary>
public partial class MeasureView : UserControl
{
    private const int WindowSeconds = 10;
    private const double SampleRate = 50;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Queue<double> recent = new();
    private MeasureViewModel? vm;
    private DataStreamer? streamer;

    public MeasureView()
    {
        InitializeComponent();
        LivePlot.UserInputProcessor.Disable();
        refresh.Tick += (_, _) => Redraw();
        DataContextChanged += (_, _) => Attach(DataContext as MeasureViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) =>
        {
            if (vm is null) Attach(DataContext as MeasureViewModel);
        };
    }

    private void Attach(MeasureViewModel? next)
    {
        if (vm is not null)
        {
            vm.DisplaySamples -= OnSamples;
            vm.DisplayReset -= OnReset;
            vm.PropertyChanged -= OnVmChanged;
            refresh.Stop();
        }
        vm = next;
        if (vm is null) return;
        vm.DisplaySamples += OnSamples;
        vm.DisplayReset += OnReset;
        vm.PropertyChanged += OnVmChanged;
        streamer = PlotTheme.CreateLive(LivePlot.Plot, (int)(WindowSeconds * SampleRate), SampleRate);
        streamer.Color = PlotTheme.For(vm.CurrentCondition);
        recent.Clear();
        refresh.Start();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MeasureViewModel.CurrentCondition) && streamer is not null && vm is not null)
            streamer.Color = PlotTheme.For(vm.CurrentCondition);
    }

    private void OnSamples(object? sender, double[] values)
    {
        if (streamer is null) return;
        streamer.AddRange(values);
        foreach (var v in values)
        {
            recent.Enqueue(Math.Abs(v));
            if (recent.Count > WindowSeconds * SampleRate) recent.Dequeue();
        }
    }

    private void OnReset(object? sender, EventArgs e)
    {
        streamer?.Clear(0);
        recent.Clear();
    }

    private void Redraw()
    {
        if (streamer is null || !streamer.HasNewData) return;
        // 縦軸は、直近の振れ幅に合わせて段階的に変える（細かく変えると見にくいため）
        double peak = recent.Count == 0 ? 0 : recent.Max();
        double limit = peak switch
        {
            < 25 => 30,
            < 50 => 60,
            < 100 => 120,
            < 250 => 300,
            _ => 600,
        };
        LivePlot.Plot.Axes.SetLimitsY(-limit, limit);
        LivePlot.Refresh();
    }
}
