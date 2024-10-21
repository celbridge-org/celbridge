

namespace Celbridge.Inspector.Views;

public sealed partial class ComponentListView : UserControl
{
    public ComponentListView()
    {
        this.InitializeComponent();
    }

    public void AddItem(ListViewItem listViewItem)
    {
        ComponentList.Items.Add(listViewItem);
    }
}
