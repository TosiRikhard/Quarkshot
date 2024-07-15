using Avalonia.Controls;
using Quarkshot.ViewModels;

namespace Quarkshot.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}