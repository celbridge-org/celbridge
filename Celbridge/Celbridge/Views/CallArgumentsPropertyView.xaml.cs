using Celbridge.Utils;
using Celbridge.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace Celbridge.Views
{
    public partial class CallArgumentsPropertyView : UserControl, IPropertyView
    {
        public CallArgumentsPropertyViewModel ViewModel { get; }

        public CallArgumentsPropertyView()
        {
            this.InitializeComponent();

            var services = (Application.Current as App)!.Host!.Services;
            ViewModel = services.GetRequiredService<CallArgumentsPropertyViewModel>();
        }

        public int ItemIndex
        {
            get => ViewModel.ItemIndex;
            set => ViewModel.ItemIndex = value;
        }

        public Result CreateChildViews()
        {
            var createResult = ViewModel.CreateChildViews();
            if (createResult is ErrorResult<List<UIElement>> createError)
            {
                return new ErrorResult(createError.Message);
            }

            var views = createResult.Data!;
            var callArgumentsView = views[0] as RecordPropertyView;
            Guard.IsNotNull(callArgumentsView);

            // Reparent the child property views to remove the wrapping "Call Signature" view.
            // This avoids displaying the "Call Signature" header in the details panel.
            var propertyViews = callArgumentsView.GetPropertyViews();

            var viewList = new List<UIElement>();
            foreach (var view in propertyViews)
            {
                var item = view as UIElement;
                Guard.IsNotNull(item);
                viewList.Add(item);
            }

            foreach (var view in viewList)
            {
                propertyViews.Remove(view);
                PropertyViews.Items.Add(view);
            }

            return new SuccessResult();
        }

        public void SetProperty(Property property, string labelText)
        {
            ViewModel.SetProperty(property, labelText);
        }
    }
}
