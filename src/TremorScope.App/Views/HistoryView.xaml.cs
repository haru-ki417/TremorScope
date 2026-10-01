using System.Windows.Controls;
using TremorScope.App.Services;
using TremorScope.App.ViewModels;

namespace TremorScope.App.Views;

public partial class HistoryView : UserControl
{
    private HistoryViewModel? vm;

    public HistoryView()
    {
        InitializeComponent();
        TrendPlot.UserInputProcessor.Disable();
        DataContextChanged += (_, _) =>
        {
            if (vm is not null) vm.TrendReady -= OnReady;
            vm = DataContext as HistoryViewModel;
            if (vm is null) return;
            vm.TrendReady += OnReady;
            OnReady(vm, EventArgs.Empty);
        };
    }

    private void OnReady(object? sender, EventArgs e)
    {
        if (vm is null) return;
        PlotTheme.DrawTrend(TrendPlot.Plot, vm.Sessions.Select(s => SessionSummaryPoint.From(s.Summary)).ToList());
        TrendPlot.Refresh();
    }
}
