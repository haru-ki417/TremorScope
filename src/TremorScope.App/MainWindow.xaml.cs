using System.Windows;

namespace TremorScope.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// ふつうに起動したとき：最大化して開く。最大化を戻したときも、画面（作業領域）からはみ出さない大きさにする。
    /// （画面の拡大率が大きい PC では、決めておいた大きさより画面のほうが小さいことがある）
    /// </summary>
    public void StartMaximized()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width * 0.92));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height * 0.92));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;
    }
}
