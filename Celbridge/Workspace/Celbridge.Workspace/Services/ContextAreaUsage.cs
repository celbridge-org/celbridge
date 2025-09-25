namespace Celbridge.Workspace.Services;

public class ContextAreaUsage
{
    private Dictionary<ContextAreaUse, UIElement> ContextAreaDictionary = new Dictionary<ContextAreaUse, UIElement>();

    public void Add(ContextAreaUse Use, UIElement Element)
    {
        ContextAreaDictionary[Use] = Element;
        Element.Visibility = Visibility.Collapsed;
    }

    public void SetUsage(ContextAreaUse Use)
    {
        foreach (KeyValuePair<ContextAreaUse, UIElement> Pair in ContextAreaDictionary)
        {
            UIElement Element = Pair.Value;
            if ((Pair.Key != Use) && (Element.Visibility != Visibility.Collapsed))
            {
                Element.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (Pair.Key == Use)
                {
                    Element.Visibility = Visibility.Visible;
                }
            }
        }
    }
};
