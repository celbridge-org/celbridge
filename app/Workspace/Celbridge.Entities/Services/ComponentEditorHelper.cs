using Celbridge.Logging;
using Celbridge.Utilities;

namespace Celbridge.Entities.Services;

public class ComponentEditorHelper : IComponentEditorHelper
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ComponentEditorHelper> _logger;
    private readonly IUtilityService _utilityService;

    public event Action<string>? ComponentPropertyChanged;

    public ComponentEditorHelper(
        IServiceProvider serviceProvider,
        ILogger<ComponentEditorHelper> logger,
        IUtilityService utilityService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _utilityService = utilityService;
    }

    protected IComponentProxy? _component;
    public IComponentProxy Component => _component!;

    public virtual Result Initialize(IComponentProxy component)
    {
        _component = component;
        _component.ComponentPropertyChanged += OnComponentPropertyChanged;

        return Result.Ok();
    }

    public virtual void Uninitialize()
    {
        if (_component is not null)
        {
            _component.ComponentPropertyChanged -= OnComponentPropertyChanged;
            _component = null;
        }
    }

    protected virtual void OnComponentPropertyChanged(string propertyPath)
    {
        // Forward the property changed event so the editor view can update itself
        ComponentPropertyChanged?.Invoke(propertyPath);
    }
}
