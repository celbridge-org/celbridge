using Celbridge.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public abstract class DocumentViewModel : ObservableObject
{
    public ResourceKey FileResource { get; set; }

    public string FilePath { get; set; } = string.Empty;
}
