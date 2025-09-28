using Celbridge.Explorer.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Explorer.Views;

public sealed partial class SearchPanel : UserControl, ISearchPanel
{
    public SearchPanel()
    {
        this.InitializeComponent();
    }
}
