using Celbridge.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Shell.Views;

public sealed partial class ShellView : UserControl
{
    private ShellViewModel _viewModel;

    public ShellView(ShellViewModel viewModel)
    {
        this.InitializeComponent();

        _viewModel = viewModel;
    }
}
