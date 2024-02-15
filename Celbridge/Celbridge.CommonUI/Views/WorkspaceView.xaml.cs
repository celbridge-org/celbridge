using Celbridge.Shell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.CommonUI.Views;

public sealed partial class WorkspaceView : Page
{
    public WorkspaceViewModel ViewModel { get; set; }

    public WorkspaceView()
    {
        InitializeComponent();

        var serviceProvider = BaseLibrary.Core.Services.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<WorkspaceViewModel>();
    }
}
