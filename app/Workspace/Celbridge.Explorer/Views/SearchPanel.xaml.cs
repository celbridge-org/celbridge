using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Explorer.ViewModels;
using Celbridge.Workspace;
using System.Collections.ObjectModel;


namespace Celbridge.Explorer.Views;


public sealed partial class SearchPanel : UserControl, ISearchPanel
{
    public SearchPanelViewModel ViewModel { get; }
    private IDocumentsService _documentsService;
    private IWorkspaceWrapper _workspaceWrapper;
    private ICommandService _commandService;

    public SearchPanel()
    {
        this.InitializeComponent();

        ViewModel = ServiceLocator.AcquireService<SearchPanelViewModel>();

        _documentsService = ServiceLocator.AcquireService<IDocumentsService>();
        _workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
    }

    private void SearchStringTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var results = SearchProjectFiles();

        ViewModel.SetSearchResults(results);
    }

    public ObservableCollection<SearchResults> SearchProjectFiles()
    {
        if ((SearchStringTextBox.Text == null) || (SearchStringTextBox.Text.Length == 0))
        {
            return new ObservableCollection<SearchResults>();
        }

        IResourceRegistry resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        IFolderResource rootFolder = resourceRegistry.RootFolder;

        List<IResource> resources = new List<IResource>();
        SearchFolders(rootFolder, ref resources);

        return SearchFiles(resources);
    }

    public ObservableCollection<SearchResults> SearchFiles(List<IResource> resources)
    {
        string searchString = SearchStringTextBox.Text;
        var results = new ObservableCollection<ViewModels.SearchResults>();

        foreach (IResource resource in resources)
        {
            // Check if our file is a text file.
            if (_documentsService.GetDocumentViewType(new ResourceKey(resource.ParentFolder + "/" + resource.Name)) == DocumentViewType.TextDocument)
            {
                // It's a text file, so we can include it in our search results.
                SearchFile(resource, searchString, ref results);
            }
            else
            {
                // Not a text file, skip it.
                continue;
            }
        }

        return results;
    }

    public void SearchFolders(IFolderResource folder, ref List<IResource> resources)
    {
        foreach (IResource resource in folder.Children)
        {
            if (resource is IFolderResource childFolder)
            {
                SearchFolders(childFolder, ref resources);
            }
            else
            {
                resources.Add(resource);
            }
        }
    }

    public void SearchFile(IResource resource, string searchString, ref ObservableCollection<SearchResults> results)
    {
        Dictionary<int, string> lineHits = new();
        var lineResults = new ObservableCollection<SearchLineResults>();

        IResourceRegistry resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        string filePath = resourceRegistry.GetResourcePath(resource);
        if (File.Exists(filePath))
        {
            try 
            { 
                int lineNumber = 0;
                foreach (var line in File.ReadLines(filePath))
                {
                    lineNumber++;
                    if (line.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                    {
                        lineHits.Add(lineNumber, line);
                        lineResults.Add(new SearchLineResults
                        {
                            LineNumber = lineNumber,
                            Excerpt = line.Trim(),
                            Resource = resource
                        });
                    }
                }
            }
            catch (Exception ex)
            {
               // %%% Quick and dirty exception catcher to stop locked files etc causing crashes.
               //   - Revisit this to come up with something nicer.
            }
        }

        if (lineHits.Count() > 0)
        {
            string parentFolderString = filePath.Substring(0, filePath.Length - resource.Name.Length);
            ViewModels.SearchResults searchResult = new()
            {
                Icon = _workspaceWrapper.WorkspaceService.ExplorerService.GetIconForResource(new ResourceKey(resource.ParentFolder + " / " + resource.Name)),
                FileName = resource.Name,
                FilePath = parentFolderString,
                HitCount = lineResults.Count,
                LineResults = lineResults
            };

            results.Add(searchResult);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listbox)
        {
            if (listbox.SelectedItem is SearchLineResults selectedResult)
            {
                if (selectedResult.Resource == null)
                {
                    return;
                }

                var resourceKey = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry.GetResourceKey(selectedResult.Resource);
                _workspaceWrapper.WorkspaceService.ExplorerService.SelectResource(resourceKey, true);

                // Execute a command to open the HTML document.
                _commandService.Execute<IOpenDocumentCommand>(command =>
                {
                    command.FileResource = resourceKey;
                    command.ForceReload = false;
                });
            }
        }
    }
}
