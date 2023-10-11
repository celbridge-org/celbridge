using Celbridge.Services;
using Celbridge.Utils;
using Celbridge.ViewModels;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace Celbridge.Views
{
    public partial class PropertyListView : UserControl, IPropertyView
    {
        public PropertyListViewModel ViewModel { get; }

        public PropertyListView()
        {
            this.InitializeComponent();

            var services = (Application.Current as App)!.Host!.Services;
            ViewModel = services.GetRequiredService<PropertyListViewModel>();
            ViewModel.ListView = ItemsListView;

            Loaded += PropertyListView_Loaded;
            Unloaded += PropertyListView_Unloaded;
        }

        private void PropertyListView_Loaded(object? sender, RoutedEventArgs e)
        {
            // Set the ListView again in case a previous Unload event has cleared it
            ViewModel.OnViewLoaded(ItemsListView);
        }

        private void PropertyListView_Unloaded(object? sender, RoutedEventArgs e)
        {
            ViewModel.OnViewUnloaded();
        }

        public void SetProperty(Property property, string labelText)
        {
            ViewModel.Property = property;
            ViewModel.LabelText = labelText;

            // PropertyChanged is raised whenever a structural change is made to the list (add, remove, etc.)
            property.PropertyChanged += ItemsListView_PropertyChanged;

            ViewModel.PopulateListView();
        }

        public int ItemIndex
        {
            get => ViewModel.ItemIndex;
            set => ViewModel.ItemIndex = value;
        }

        public Result CreateChildViews()
        {
            return new SuccessResult();
        }

        private void ItemsListView_DragItemsStarting(object? sender, DragItemsStartingEventArgs e)
        {
            ViewModel.DragItemsStartingCommand.Execute(e);
        }

        private void ItemsListView_DragItemsCompleted(object? sender, DragItemsCompletedEventArgs e)
        {
            ViewModel.DragItemsCompletedCommand.Execute(e);

            var listView = sender as ListView;
            Guard.IsNotNull(listView);
            UpdateSelectedIndex(listView);
        }

        private void ItemsListView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var listView = sender as ListView;
            Guard.IsNotNull(listView);

            UpdateSelectedIndex(listView);
        }

        private void ItemsListView_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateSelectedIndex(ItemsListView);
        }

        private void UpdateSelectedIndex(ListView listView)
        {
            // Note: This method may be called several times during any particular operation,
            // but ViewMode.SetSelectedIndex() will only raise an event when the index actually changes.

            Guard.IsNotNull(listView);

            var selectedItem = listView.SelectedItem as PropertyListItem;
            if (selectedItem == null)
            {
                // This will clear the selection in the detail panel.
                ViewModel.SetSelectedIndex(-1);
            }
            else
            {
                var propertyView = selectedItem.PropertyView;
                Guard.IsNotNull(propertyView);

                ViewModel.SetSelectedIndex(listView.SelectedIndex);
            }
        }
    }
}
