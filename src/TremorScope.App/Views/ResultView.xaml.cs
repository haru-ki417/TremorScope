using System.Windows.Controls;
using TremorScope.App.Services;
using TremorScope.App.ViewModels;

namespace TremorScope.App.Views;

public partial class ResultView : UserControl
{
    private ResultViewModel? vm;

    public ResultView()
    {
        InitializeComponent();
        SpectrumPlot.UserInputProcessor.Disable();
        DataContextChanged += (_, _) =>
        {
            if (vm is not null) vm.SpectraReady -= OnReady;
            vm = DataContext as ResultViewModel;
            if (vm is null) return;
            vm.SpectraReady += OnReady;
            if (vm.IsLoaded) OnReady(vm, EventArgs.Empty);
        };
    }

    private void OnReady(object? sender, EventArgs e)
    {
        if (vm is null) return;
        PlotTheme.DrawSpectrum(SpectrumPlot.Plot, vm.RestAnalysis, vm.PosturalAnalysis);
        SpectrumPlot.Refresh();
    }
}
