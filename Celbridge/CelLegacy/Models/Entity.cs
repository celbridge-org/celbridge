using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;

namespace Celbridge.Legacy.Models;

public interface IEntity
{
    public Guid Id { get; set; }
    string Name { get; set; }
    bool IsNameEditable { get; }
    string Description { get; set; }
    string GetKey();
}

public abstract class Entity : ObservableObject, IEntity, ITreeNode
{
    public ITreeNodeRef ParentNode { get; } = new ParentNodeRef();
    public virtual void OnSetParent(ITreeNode parent)
    {
        // Override this to be notified when the entity's parent is set
    }

    public Entity()
		{
        PropertyChanged += Entity_PropertyChanged;

        _children = new ObservableCollection<Entity>();
        _children.CollectionChanged += Children_CollectionChanged;

        UpdateIconAndTooltip();
    }

    protected virtual void UpdateIconAndTooltip()
    {
        var itemTypeService = LegacyServiceProvider.Services!.GetService<IResourceTypeService>();
        Guard.IsNotNull(itemTypeService);

        var result = itemTypeService.GetResourceTypeInfo(GetType());
        if (result.Success)
        {
            Icon = result.Data!.Icon;
            Tooltip = result.Data.Description;
        }
    }

    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            SetProperty(ref _name, value);
        }
    }

    [JsonIgnore]
    [HideProperty]
    public bool IsNameEditable => true;

    private string _description = string.Empty;

    [TextAreaProperty]
    public string Description 
    {
        get => _description;
        set
        {
            SetProperty(ref _description, value);
        }
    }

    [HideProperty]
    public Guid Id { get; set; }

    [JsonIgnore]
    [HideProperty]
    public string Icon { get; private set; } = string.Empty;
    
    private string _tooltip = string.Empty;
    [JsonIgnore]
    [HideProperty]
    public string Tooltip
    {
        get => _tooltip;
        set
        {
            SetProperty(ref _tooltip, value);
        }
    }

    [JsonIgnore]
    public Entity? Parent { get; set; }

		private ObservableCollection<Entity> _children;
		public ObservableCollection<Entity> Children
		{
			get
			{
				return _children;
			}
			set
			{
            SetProperty(ref _children, value);
        }
    }

    public string GetKey()
    {
        List<string> segments = new()
        {
            Name
        };

        var parent = Parent;
        while (parent != null) 
        {
            if (parent.Name == "Root")
            {
                // Don't include the Root folder in the Key
                break;
            }

            segments.Add(parent.Name);   
            parent = parent.Parent;
        }

        var sb = new StringBuilder();
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (sb.Length > 0)
            {
                sb.Append('/');
            }
            sb.Append(segments[i]);
        }

        return sb.ToString();
    }

    private void Entity_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Description):
                {
                    var itemTypeService = LegacyServiceProvider.Services!.GetService<IResourceTypeService>();
                    Guard.IsNotNull(itemTypeService);

                    var result = itemTypeService.GetResourceTypeInfo(GetType());
                    if (result.Success)
                    {
                        Tooltip = !string.IsNullOrEmpty(Description) ? Description : result.Data!.Description;
                    }

                    break;
                }
        }

        Parent?.OnPropertyChanged(nameof(Children));
    }

    private void Children_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var child in e.OldItems)
            {
                var newEntity = child as Entity;
                Guard.IsNotNull(newEntity);

                newEntity.Parent = null;
            }
        }

        if (e.NewItems != null)
        {
            foreach (var child in e.NewItems)
            {
                var newEntity = child as Entity;
                Guard.IsNotNull(newEntity);

                newEntity.Parent = this;
            }
        }

        OnPropertyChanged(nameof(Children));
    }
}
