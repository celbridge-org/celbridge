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

        if (Annotation is not null &&
            Annotation.Errors.Count > 0)
        {
            var error = Annotation.Errors[0];
            description = error.Message;
            tooltip = error.Description;

            if (error.Severity == ComponentErrorSeverity.Critical ||
                error.Severity == ComponentErrorSeverity.Error)
            {
                showErrorIcon = Visibility.Visible;
                showWarningIcon = Visibility.Collapsed;
            }
            else if (error.Severity == ComponentErrorSeverity.Warning)
            {
                showErrorIcon = Visibility.Collapsed;
                showWarningIcon = Visibility.Visible;
            }
        }
        else if (Summary is not null)
        {
            description = Summary.SummaryText;
            tooltip = Summary.Tooltip;
            showErrorIcon = Visibility.Collapsed;
            showWarningIcon = Visibility.Collapsed;
        }

        Description = description;
        Tooltip = tooltip;
        ShowErrorIcon = showErrorIcon;
        ShowWarningIcon = showWarningIcon;
    }        
}
