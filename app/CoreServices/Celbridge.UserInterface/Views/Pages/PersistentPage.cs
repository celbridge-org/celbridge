using System.Runtime.ConstrainedExecution;
using Celbridge.Navigation;

namespace Celbridge.UserInterface.Views;

public abstract class PersistentPage : Page
{
    private readonly INavigationService _navigationService;

    static List<PersistentPage> _activePersistantPages = new List<PersistentPage>();
    bool Initialised = false;

    // NOTE : The logic using this is currently very simple, but may need to be expanded upon if we start
    //  introducing more cases to this.
    public enum PersistenceLevel
    {
        Project,
        Eternal
    };

    public PersistenceLevel Persistence { get; protected set; }

    public PersistentPage() // : base()
    {
        // Register ourselves with navigation handling. - Looks like we may not have to even do this!!
        _navigationService = ServiceLocator.AcquireService<INavigationService>();
        Loaded += WorkspacePage_Loaded;
        base.Unloaded += WorkspacePage_Unloaded;

        NavigationCacheMode = NavigationCacheMode.Required;

        Persistence = PersistenceLevel.Project;
    }

    public abstract void PageUnloadInternal();

    // %%% Would be nice to figure a way to block inherited classes from accessing this.
    //private new RoutedEventHandler Unloaded = new RoutedEventHandler(null, null);

    public void SetWorkspacePagePersistence(bool persistant)
    {
        if (persistant)
        {
            NavigationCacheMode = NavigationCacheMode.Required;
        }
        else
        {
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }
    }

    public static void ClearPersistenceOfAllLoadedPages()
    {
        foreach (PersistentPage page in _activePersistantPages)
        {
            if (page.Persistence == PersistenceLevel.Project)
            {
                page.SetWorkspacePagePersistence(false);
            }
        }
    }

    private void WorkspacePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (Initialised == false)
        {
            _activePersistantPages.Add(this);
            Initialised = true;
        }
    }

    private void WorkspacePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Only execute this functionality if we have Cache Mode set to Disabled.
        //  - This means we are purposefully wanted to rebuild the Workspace (Intentional Project Load, rather than UI context switch).
        if (NavigationCacheMode == NavigationCacheMode.Disabled)
        {
            Loaded -= WorkspacePage_Loaded;
            Unloaded -= WorkspacePage_Unloaded;
            _activePersistantPages.Remove(this);
            PageUnloadInternal();
        }
    }

    public static void UnloadPersistantUnfocusedPages(string focusedPageName)
    {
        // %%% Update comment for generalised case.

        // Check which page we're on, and if we are not on the workspace page, call the manual unloading for it.
        //  - If the Workspace Page is the current page, then switching away from it will cause it to be unloaded (as we have disabled the cache by this point),
        //      if not however, then the page will need explicitly unloading.
        List<PersistentPage> UnloadPages = new();
        foreach (PersistentPage page in _activePersistantPages)
        {
            if (page.Name != focusedPageName
                && page.Persistence == PersistenceLevel.Project)
            {
                UnloadPages.Add(page);
            }
        }

        for (int i=0; i<UnloadPages.Count; ++i)
        {
            PersistentPage page = UnloadPages[i];
            page.Loaded -= page.WorkspacePage_Loaded;
            page.Unloaded -= page.WorkspacePage_Unloaded;
            _activePersistantPages.Remove(page);
            page.PageUnloadInternal();
        }
    }
}
