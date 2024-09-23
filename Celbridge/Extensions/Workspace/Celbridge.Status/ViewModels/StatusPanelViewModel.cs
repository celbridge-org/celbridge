using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Documents;
using Celbridge.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Status.ViewModels;

public partial class StatusPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private string _selectedDocument = string.Empty;

    [ObservableProperty]
    private float _saveIconOpacity;

    [ObservableProperty]
    private Visibility _selectedDocumentVisibility = Visibility.Collapsed;

    public StatusPanelViewModel(
        IMessengerService messengerService,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _commandService = commandService;
    }

    public void OnLoaded()
    {
        _messengerService.Register<PendingDocumentSaveMessage>(this, OnPendingDocumentSaveMessage);
        _messengerService.Register<SelectedDocumentChangedMessage>(this, OnSelectedDocumentChangedMessage);
    }

    public void OnUnloaded()
    {
        _messengerService.Unregister<PendingDocumentSaveMessage>(this);
        _messengerService.Unregister<SelectedDocumentChangedMessage>(this);
    }

    private void OnPendingDocumentSaveMessage(object recipient, PendingDocumentSaveMessage message)
    {
        if (message.PendingSaveCount > 0)
        {
            SaveIconOpacity = 1;
        }
        else
        {
            SaveIconOpacity = 0;
        }
    }

    private void OnSelectedDocumentChangedMessage(object recipient, SelectedDocumentChangedMessage message)
    {
        var resource = message.DocumentResource;

        SelectedDocument = resource.ToString();

        SelectedDocumentVisibility = string.IsNullOrEmpty(SelectedDocument) ? Visibility.Collapsed : Visibility.Visible;
    }

    public IRelayCommand CopyDocumentResourceCommand => new RelayCommand(CopyDocumentResource_Executed);
    private void CopyDocumentResource_Executed()
    {
        if (string.IsNullOrEmpty(SelectedDocument))
        {
            return;
        }

        // Copy the selected document resource to the clipboard
        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = SelectedDocument;
            command.TransferMode = DataTransferMode.Copy;
        });
    }
}
