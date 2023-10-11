using Celbridge.Models;
using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Serilog;
using System;
using System.ComponentModel;
using System.Text;

namespace Celbridge.ViewModels
{
    public partial class CelCanvasViewModel : ObservableObject
    {
        private readonly IMessenger _messengerService;
        private readonly IInspectorService _entityService;
        private readonly ICelTypeService _celTypeService;

        public CelCanvasViewModel(IMessenger messengerService,
            IInspectorService entityService,
            ICelTypeService celTypeService)
        {
            _messengerService = messengerService;
            _entityService = entityService;
            _celTypeService = celTypeService;

            PropertyChanged += ViewModel_PropertyChanged;
            _messengerService.Register<SelectedEntityChangedMessage>(this, OnSelectedEntityChanged);
            _messengerService.Register<PreviouslySelectedEntityMessage>(this, OnPreviouslySelectedEntity);
        }

        public ICelScriptNode Cel { get; private set; }

        public CelScriptDocumentViewModel CelScriptDocumentViewModel { get; set; }

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private SolidColorBrush _fillColor;

        [ObservableProperty]
        private SolidColorBrush _strokeColor;

        [ObservableProperty]
        private float _strokeThickness;

        [ObservableProperty]
        private string _labelText;

        [ObservableProperty]
        private string _icon;

        [ObservableProperty]
        private string _tooltipText;

        private Windows.UI.Color _cachedFillColor;

        public event Action<int,int> CelPositionChanged;

        public void SetCel(ICelScriptNode _cel)
        {
            Cel = _cel;

            var cel = _cel as Cel;
            cel.PropertyChanged += Cel_PropertyChanged;

            UpdateAppearance(cel);
            UpdateLabelText();
            UpdateTooltipText();
        }

        private void UpdateAppearance(ICel cel)
        {
            var celType = cel.CelType;
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

        public void SetCelPosition(int x, int y)
        {
            Guard.IsNotNull(Cel);

            CelScriptDocumentViewModel.SetCelPosition(Cel.Id, x, y);
            CelPositionChanged?.Invoke(x, y);
        }

        private void OnSelectedEntityChanged(object recipient, SelectedEntityChangedMessage message)
        {
            IsSelected = Cel == message.Value;
        }

        private void OnPreviouslySelectedEntity(object recipient, PreviouslySelectedEntityMessage message)
        {
            if (Cel.Id == message.entityId)
            {
                SelectCell();
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IsSelected):
                    UpdateFillColor();
                    break;
            }
        }

        private void Cel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(CelScriptNode.Name):
                    UpdateLabelText();
                    break;
                case nameof(CelScriptNode.Description):
                    UpdateTooltipText();
                    break;
            }
        }

        private void UpdateFillColor()
        {
            FillColor = new SolidColorBrush(_cachedFillColor);
            StrokeColor = new SolidColorBrush(Colors.White);
            StrokeThickness = IsSelected ? 2 : 0;
        }

        private void UpdateLabelText()
        {
            LabelText = Cel.Name;
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
            sb.Append(cel.CelType.Name);

            if (!string.IsNullOrEmpty(Cel.Description))
            {
                sb.Append("\n\n");
                sb.AppendLine(Cel.Description);
            }

            TooltipText = sb.ToString().Trim();
        }
    }
}
