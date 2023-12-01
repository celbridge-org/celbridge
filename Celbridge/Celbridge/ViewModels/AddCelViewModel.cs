using Serilog;
using Celbridge.Services;
using Celbridge.Utils;
using CommunityToolkit.Diagnostics;
using System.Numerics;

namespace Celbridge.ViewModels
{
    public partial class AddCelViewModel : ObservableObject
    {
        private readonly ICelTypeService _celTypeService;
        private readonly ICelScriptService _celScriptService;

        public AddCelViewModel(ICelTypeService celTypeService, ICelScriptService celScriptService)
        {
            _celTypeService = celTypeService;
            _celScriptService = celScriptService;

            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CelName))
                {
                    CanAddCel= !string.IsNullOrEmpty(CelName);
                }
            };

            _celTypes = new List<ICelType>();
            foreach (var celTypeName in _celTypeService.CelTypeNames)
            {
                var result = _celTypeService.GetCelType(celTypeName);
                if (result is ErrorResult<ICelType> error)
                {
                    Log.Error(error.Message);
                    continue;
                }

                var celType = result.Data!;
                Guard.IsNotNull(celType);

                CelTypeNames.Add(celTypeName);
                _celTypes.Add(celType);
            }
            SelectedCelTypeIndex = 0;
        }

        public ICelScript? CelScript { get; set; }
        public Vector2 SpawnPosition { get; internal set; }

        private string _celName = string.Empty;
        public string CelName
        {
            get { return _celName; }
            set
            {
                SetProperty(ref _celName, value);
            }
        }

        [ObservableProperty]
        private bool _canAddCel;


        private List<ICelType> _celTypes;
        public List<string> CelTypeNames { get; } = new ();

        private int _selectedCelTypeIndex;
        public int SelectedCelTypeIndex
        {
            get => _selectedCelTypeIndex;
            set
            {
                SetProperty(ref _selectedCelTypeIndex, value);
            }
        }

        public ICommand AddCelCommand => new RelayCommand(AddCel_Executed);

        private void AddCel_Executed()
        {
            var celType = _celTypes[SelectedCelTypeIndex];
            Guard.IsNotNull(celType);

            Guard.IsNotNull(CelScript);

            // No whitespace allowed
            var trimmed = CelName.Trim().Replace(" ", "");
            if (string.IsNullOrEmpty(trimmed))
            {
                Log.Error("Failed to add CelName. Name is empty.");
                return;
            }

            _celScriptService.CreateCel(CelScript, celType, trimmed, SpawnPosition);
        }
    }
}
