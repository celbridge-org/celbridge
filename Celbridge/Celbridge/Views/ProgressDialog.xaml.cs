using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Celbridge.Views
{
    public sealed partial class ProgressDialog : ContentDialog
    {
        public ProgressDialogViewModel ViewModel { get; private set; }

        public ProgressDialog(Action onCancel)
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App).Host.Services.GetRequiredService<ProgressDialogViewModel>();
            ViewModel.ContentDialog = this;
            
            if (onCancel != null)
            {
                ViewModel.OnCancel = onCancel;
                PrimaryButtonText = "Cancel";
            }
        }
    }
}
