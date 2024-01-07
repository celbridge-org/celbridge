using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Celbridge.ViewModels
{
    public abstract partial class ClassPropertyViewModel<T> : ObservableObject where T : class
    {
        private Property? _property;
        public Property Property 
        {
            get
            {
                Guard.IsNotNull(_property);
                return _property;
            }
            private set
            {
                Guard.IsNotNull(value);
                _property = value;
            }
        }

        public void SetProperty(Property property, string labelText)
        {
            Property = property;
            LabelText = labelText;
        }

        public string LabelText { get; private set; } = string.Empty;
        public int ItemIndex { get; set; }
        public bool HasLabelText => !string.IsNullOrEmpty(LabelText);

        public T? Value
        {
            get
            {
                var propertyInfo = Property.PropertyInfo;
                if (Property.CollectionType != null)
                {
                    var list = propertyInfo.GetValue(Property.Object) as List<T>;
                    Guard.IsNotNull(list);
                    Guard.IsTrue(ItemIndex < list.Count);
                    return list[ItemIndex];
                }

                return propertyInfo.GetValue(Property.Object) as T;
            }

            set
            {
                if (value is null ||
                    value.Equals(Value))
                {
                    return;
                }

                var propertyInfo = Property.PropertyInfo;
                if (Property.CollectionType != null)
                {
                    var list = propertyInfo.GetValue(Property.Object) as List<T>;
                    Guard.IsNotNull(list);
                    Guard.IsTrue(ItemIndex < list.Count);
                    list[ItemIndex] = value;
                }
                else 
                { 
                    propertyInfo.SetValue(Property.Object, value);
                }
                OnPropertyChanged(nameof(Value));
            }
        }

        public ClassPropertyViewModel()
        {
            PropertyChanged += PropertyViewModel_PropertyChanged;
        }

        private void PropertyViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Value))
            {
                Property.NotifyPropertyChanged();
            }
        }
    }
}
