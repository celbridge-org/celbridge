using Celbridge.Forms;

namespace Celbridge.UserInterface.Models.Forms;

public class StackPanelElement : IStackPanelElement
{
    public FormOrientation Orientation { get; set; }
    
    public IList<IFormElement> Children { get; } = new List<IFormElement>();

    public IStackPanelElement AddChildren(params IFormElement[] childElements)
    {
        foreach (var childElement in childElements)
        {
            Children.Add(childElement);
        }
        return this;
    }
}
