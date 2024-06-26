using Celbridge.BaseLibrary.Dialog;

namespace Celbridge.UserInterface.Services;

public record ProgressDialogToken : IProgressDialogToken
{
    public Guid Token { get; private set; } = Guid.NewGuid();
    public string DialogTitle { get; init; }

    public ProgressDialogToken(string DialogTitle)
    {
        this.DialogTitle = DialogTitle;
    }
}
