using Celbridge.Utils;

namespace Celbridge.Views
{
    public interface IPropertyView
    {
        void SetProperty(Property property, string labelText);
        int ItemIndex { set; get; }
        Result CreateChildViews();
        void NotifyWillDelete() {}
        void NotifyIndexChanged(int newIndex) {}
    }
}
