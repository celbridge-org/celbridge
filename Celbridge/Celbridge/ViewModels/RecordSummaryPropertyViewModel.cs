using Celbridge.Models;
using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;
using System.Collections.Generic;

namespace Celbridge.ViewModels
{
    public partial class RecordSummaryPropertyViewModel : ObservableObject
    {
        protected IMessenger _messengerService;

        public Property Property { get; private set; }

        public void SetProperty(Property property)
        {
            Property = property;
        }

        public int ItemIndex { get; set; }

        public string Description
        {
            get
            {
                var propertyInfo = Property.PropertyInfo;
                IRecord instructionLine;
                if (Property.CollectionType != null)
                {
                    // Todo: This should probably be an IEnumerable of IRecords?
                    // https://stackoverflow.com/questions/31525118/polymorphism-with-lists
                    var list = propertyInfo.GetValue(Property.Object) as List<InstructionLine>;
                    Guard.IsNotNull(list);
                    Guard.IsTrue(ItemIndex < list.Count);
                    instructionLine = list[ItemIndex];
                }
                else
                {
                    instructionLine = propertyInfo.GetValue(Property.Object) as InstructionLine;
                }
                Guard.IsNotNull(instructionLine);

                return instructionLine.Description;
            }
        }
        public RecordSummaryPropertyViewModel(IMessenger messengerService)
        {
            _messengerService = messengerService;
            _messengerService.Register<DetailPropertyChangedMessage>(this, OnDetailPropertyChangedMessage);

            PropertyChanged += PropertyViewModel_PropertyChanged;
        }

        public virtual void OnGotFocus()
        {
            var message = new SelectedCollectionItemGotFocusMessage(Property, ItemIndex);
            _messengerService.Send(message);
        }

        private void OnDetailPropertyChangedMessage(object recipient, DetailPropertyChangedMessage message)
        {
            // Check if the changed detail property relates to this instructionLine line
            if (message.CollectionProperty == Property &
                message.CollectionIndex == ItemIndex)
            {
                OnPropertyChanged(message.ChangedPropertyName);

                // Assume the description has also changed if any other property has changed.
                // Slight overkill, but it's the easiest way to do it without requiring the
                // client to indicate which properties are used in the description.
                if (message.ChangedPropertyName != nameof(Description))
                {
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        protected virtual void PropertyViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Property.NotifyPropertyChanged();
        }
    }
}
