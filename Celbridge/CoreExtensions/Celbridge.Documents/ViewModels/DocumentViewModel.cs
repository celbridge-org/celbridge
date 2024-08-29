using Celbridge.Messaging;
using Celbridge.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public abstract partial class DocumentViewModel : ObservableObject
{
    [ObservableProperty]
    private ResourceKey _fileResource = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _isDirty = false;
}
