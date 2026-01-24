namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Community Page for accessing forums and community content.
/// </summary>
public sealed partial class CommunityPage : Page
{
    public CommunityPage()
    {
        this.InitializeComponent();

        // Enable caching so the page persists during navigation
        NavigationCacheMode = NavigationCacheMode.Required;
    }
}
