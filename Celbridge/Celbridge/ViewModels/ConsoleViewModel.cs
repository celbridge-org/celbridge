using Celbridge.Services;
using CommunityToolkit.Diagnostics;
using Serilog;

namespace Celbridge.ViewModels
{
    public partial class ConsoleViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IConsoleService _consoleService;

        // [ObservableProperty] causes a compile error on Skia.Gtk when using Mode=TwoWay
        private string _outputText = string.Empty;
        public string OutputText
        {
            get { return _outputText; }
            set
            {
                SetProperty(ref _outputText, value);
            }
        }

        private string _inputText = string.Empty;
        public string InputText
        {
            get { return _inputText; }
            set
            {
                SetProperty(ref _inputText, value);
            }
        }

        public ConsoleViewModel(ISettingsService settingsService, IConsoleService consoleService)
        {
            _settingsService = settingsService;
            _consoleService = consoleService;

            _consoleService.OnWriteMessage += ConsoleService_OnWriteMessage;
            _consoleService.OnClearMessages += () => OutputText = string.Empty;

            Log.Information("Celbridge v0.00000001\n");
        }

        public event Action? OnWriteMessage;

        private void ConsoleService_OnWriteMessage(string message)
        {
            OutputText += $"{message}\n";
            OnWriteMessage?.Invoke();
        }

        public ICommand CollapseCommand => new RelayCommand(Collapse_Executed);
        private void Collapse_Executed()
        {
            // Toggle the bottom toolbar expanded state
            Guard.IsNotNull(_settingsService.EditorSettings);
            _settingsService.EditorSettings.BottomPanelExpanded = false;
        }

        public ICommand ClearCommand => new RelayCommand(Clear_Executed);
        private void Clear_Executed()
        {
            _consoleService.ClearMessages();
        }

        public event Action? OnCommandEntered;

        public ICommand SubmitCommand => new RelayCommand(Submit_Executed);
        private void Submit_Executed()
        {
            var commandText = InputText;
            if (commandText.IsNullOrEmpty())
            {
                return;
            }

            _consoleService.ExecuteCommand(commandText);

            InputText = string.Empty;
            OnCommandEntered?.Invoke();
        }

        public event Action? OnHistoryCycled;

        public ICommand CycleHistoryCommand => new RelayCommand<bool>(CycleHistory_Executed);
        private void CycleHistory_Executed(bool forwards)
        {
            var result = _consoleService.CycleHistory(forwards);
            if (result.Success)
            {
                InputText = result.Data!;
                OnHistoryCycled?.Invoke();
            }
        }
    }
}
