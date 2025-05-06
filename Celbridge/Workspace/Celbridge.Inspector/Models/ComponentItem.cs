using Celbridge.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Inspector.Models;

/// <summary>
/// An item to display in the component list view.
/// </summary>
public partial class ComponentItem : ObservableObject
{
    [ObservableProperty]
    private string _componentType = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _tooltip = string.Empty;

    [ObservableProperty]
    private Visibility _showErrorIcon = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility _showWarningIcon = Visibility.Collapsed;

    [ObservableProperty]
    private ComponentAnnotation? _annotation;

    [ObservableProperty]
    private ComponentSummary? _summary;

    [ObservableProperty]
    private GridLength _indentWidth;

    public ComponentItem()
    {
        PropertyChanged += ComponentItem_PropertyChanged;
    }

    private void ComponentItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Annotation) ||
            e.PropertyName == nameof(Summary))
        {
            UpdateContent();
        }
    }

    private void UpdateContent()
    {
        string description = string.Empty;
        string tooltip = string.Empty;
        var showErrorIcon = Visibility.Collapsed;
        var showWarningIcon = Visibility.Collapsed;

        if (Summary is not null)
        {
            description = Summary.SummaryText;
            tooltip = Summary.Tooltip;
            showErrorIcon = Visibility.Collapsed;
            showWarningIcon = Visibility.Collapsed;
        }

        if (Annotation is not null)
        {
            if (Annotation.ComponentErrors.Count > 0)
            {
                var error = Annotation.ComponentErrors[0];
                tooltip = $"Error: {error.Message}\n{error.Description}";

                if (error.Severity == AnnotationErrorSeverity.Error ||
                    error.Severity == AnnotationErrorSeverity.Error)
                {
                    showErrorIcon = Visibility.Visible;
                    showWarningIcon = Visibility.Collapsed;
                }
                else if (error.Severity == AnnotationErrorSeverity.Warning)
                {
                    showErrorIcon = Visibility.Collapsed;
                    showWarningIcon = Visibility.Visible;
                }
            }

            IndentWidth = new GridLength(Annotation.IndentLevel * 20);
        }

        Description = description;
        Tooltip = tooltip;
        ShowErrorIcon = showErrorIcon;
        ShowWarningIcon = showWarningIcon;
    }        
}
