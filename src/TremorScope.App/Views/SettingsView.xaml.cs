using System.Windows;
using System.Windows.Controls;
using TremorScope.App.ViewModels;

namespace TremorScope.App.Views;

/// <summary>パスワード欄は（安全のため）画面とのひも付けができないので、ここで受け渡す</summary>
public partial class SettingsView : UserControl
{
    private SettingsViewModel? vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (vm is not null) vm.SecretsSaved -= OnSaved;
            vm = DataContext as SettingsViewModel;
            // 画面が使い回されても、前に入力した秘密が残らないようにする
            IotBox.Clear();
            CosmosBox.Clear();
            if (vm is not null) vm.SecretsSaved += OnSaved;
        };
    }

    private void OnIotChanged(object sender, RoutedEventArgs e)
    {
        if (vm is not null) vm.NewIotConnectionString = IotBox.Password;
    }

    private void OnCosmosChanged(object sender, RoutedEventArgs e)
    {
        if (vm is not null) vm.NewCosmosKey = CosmosBox.Password;
    }

    private void OnSaved(object? sender, EventArgs e)
    {
        IotBox.Clear();
        CosmosBox.Clear();
    }
}
