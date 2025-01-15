using Celbridge.Forms;

namespace Celbridge.UserInterface.Models.Forms;

public class Form : IForm
{
    public Form(IStackPanelElement stackPanel)
    {
        Panel = stackPanel;
    }

    public IFormPanel Panel { get; set; }
}
