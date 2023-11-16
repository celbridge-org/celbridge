using Celbridge.Services;
using Celbridge.Tasks;
using Celbridge.Utils;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI;
using System.ComponentModel;
using System.Text;

namespace Celbridge.ViewModels
{
    public partial class CelNodeViewModel : ObservableObject
    {
        private readonly IMessenger _messengerService;
        private readonly IInspectorService _entityService;
        private readonly ICelScriptService _celScriptService;

        public CelNodeViewModel(IMessenger messengerService,
                                IInspectorService entityService,
                                ICelScriptService celScriptService)
        {
            _messengerService = messengerService;
            _entityService = entityService;
            _celScriptService = celScriptService;

            PropertyChanged += ViewModel_PropertyChanged;
            _messengerService.Register<SelectedEntityChangedMessage>(this, OnSelectedEntityChanged);
            _messengerService.Register<PreviouslySelectedEntityMessage>(this, OnPreviouslySelectedEntity);
        }

        public ICelScriptNode? Cel { get; private set; }

        public CelScriptDocumentViewModel? CelScriptDocumentViewModel { get; set; }

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private SolidColorBrush? _fillColor;

        [ObservableProperty]
        private SolidColorBrush? _strokeColor;

        [ObservableProperty]
        private float _strokeThickness;

        [ObservableProperty]
        private string _nameText = string.Empty;

        [ObservableProperty]
        private string _descriptionText = string.Empty;

        [ObservableProperty]
        private string _icon = string.Empty;

        [ObservableProperty]
        private string _tooltipText = string.Empty;

        private Windows.UI.Color? _cachedFillColor;

        public event Action<int,int>? CelPositionChanged;

        public void SetCel(ICelScriptNode _cel)
        {
            Cel = _cel;

            var cel = _cel as Cel;
            Guard.IsNotNull(cel);

            cel.PropertyChanged += Cel_PropertyChanged;

            UpdateAppearance(cel);
            UpdateLabelText();
            UpdateTooltipText();
        }

        private void UpdateAppearance(ICel cel)
        {
            var celType = cel.CelType;
            Guard.IsNotNull(celType);

            try 
            {
                _cachedFillColor = CommunityToolkit.WinUI.Helpers.ColorHelper.ToColor(celType.Color);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to parse color string '{celType.Color}'. {ex.Message}");
                _cachedFillColor = Colors.White;
            }

            UpdateFillColor();

            var icon = celType.Icon;
            if (!string.IsNullOrEmpty(icon))
            {
                Icon = icon;
            }
        }

        public void SelectCell()
        {
            _entityService.SelectedEntity = Cel as IEntity;
        }

        public void PlayCel()
        {
            Guard.IsNotNull(Cel);

            async Task PlayCelAsync()
            {
                try
                {
                    var celScriptName = Cel!.CelScript!.Entity!.Name;
                    celScriptName = Path.GetFileNameWithoutExtension(celScriptName);

                    var celName = Cel.Name;

                    var playCelTask = (Application.Current as App)!.Host!.Services.GetRequiredService<PlayCelTask>();
                    var playResult = await playCelTask.PlayCel(celScriptName, celName);
                    if (playResult is ErrorResult playError)
                    {
                        Log.Error(playError.Message);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to play Cel. {ex.Message}");
                }
            }

            // Todo: Prevent playing if already in progress
            _ = PlayCelAsync();
        }

        public void DeleteCel()
        {
            Guard.IsNotNull(Cel);
            _celScriptService.DeleteCel(Cel);
        }

        public void SetCelPosition(int x, int y)
        {
            Guard.IsNotNull(CelScriptDocumentViewModel);
            Guard.IsNotNull(Cel);

            CelScriptDocumentViewModel.SetCelPosition(Cel.Id, x, y);
            CelPositionChanged?.Invoke(x, y);
        }

        private void OnSelectedEntityChanged(object recipient, SelectedEntityChangedMessage message)
        {
            IsSelected = Cel == message.Entity;
        }

        private void OnPreviouslySelectedEntity(object recipient, PreviouslySelectedEntityMessage message)
        {
            Guard.IsNotNull(Cel);

            if (Cel.Id == message.EntityId)
            {
                SelectCell();
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IsSelected):
                    UpdateFillColor();
                    break;
            }
        }

        private void Cel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CelScriptNode.Name):
                    UpdateLabelText();
                    break;
                case nameof(CelScriptNode.Description):
                    UpdateLabelText();
                    UpdateTooltipText();
                    break;
            }
        }

        private void UpdateFillColor()
        {
            Guard.IsNotNull(_cachedFillColor);

            FillColor = new SolidColorBrush((Windows.UI.Color)_cachedFillColor);
            StrokeColor = new SolidColorBrush(Colors.White);
            StrokeThickness = IsSelected ? 4 : 0;
        }

        private void UpdateLabelText()
        {
            Guard.IsNotNull(Cel);
            NameText = StringUtils.ToHumanFromPascal(Cel.Name);
            DescriptionText = Cel.Description;
        }

        public void UpdateTooltipText()
        {
            // The tooltip may have been removed by a previous move operation.
            // This forces the tooltip to be reapplied.
            TooltipText = string.Empty;

            var sb = new StringBuilder();

            var cel = Cel as ICel;
            Guard.IsNotNull(cel);

            sb.Append("Cel Type: ");
            sb.Append(cel.CelType!.Name);

            Guard.IsNotNull(Cel);
            if (!string.IsNullOrEmpty(Cel.Description))
            {
                sb.Append("\n\n");
                sb.AppendLine(Cel.Description);
            }

            TooltipText = sb.ToString().Trim();
        }
    }
}
