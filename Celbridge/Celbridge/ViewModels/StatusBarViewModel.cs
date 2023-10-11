using Celbridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.ViewModels
{
    public partial class StatusBarViewModel : ObservableObject
    {
        private readonly IMessenger _messengerService;

        [ObservableProperty]
        private bool _isSaving;

        public StatusBarViewModel(IMessenger messengerService) 
        {
            _messengerService = messengerService;

            _messengerService.Register<SaveQueueUpdatedMessage>(this, OnSaveQueueUpdated);
        }

        private void OnSaveQueueUpdated(object recipient, SaveQueueUpdatedMessage message)
        {
            var pendingSaveCount = message.PendingSaveCount;
            if (pendingSaveCount == 0)
            {
                IsSaving = false;
            }
            else
            {
                IsSaving = true;
            }
        }
    }
}
