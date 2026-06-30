using Celbridge.Resources.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Platform;
using Celbridge.Resources.Services;

namespace Celbridge.Resources;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        PlatformServiceConfiguration.ConfigureServices(services);

        services.AddSingleton<IResourceOperationService, ResourceOperationService>();
        services.AddSingleton<IFileTemplateService, FileTemplateService>();

        services.AddTransient<IResourceService, ResourceService>();
        services.AddTransient<IResourceTransferService, ResourceTransferService>();
        services.AddTransient<IResourceNameValidator, ResourceNameValidator>();
        services.AddTransient<IResourceMonitor, ResourceMonitor>();
        services.AddTransient<IResourceFileSystem, LocalResourceFileSystem>();
        services.AddTransient<IResourcePolicy, ResourcePolicy>();
        services.AddTransient<ITrashService, TrashService>();
        services.AddTransient<IResourceScanner, ResourceScanner>();
        services.AddTransient<ISidecarService, SidecarService>();
        services.AddTransient<IResourceClassifier, ResourceClassifier>();
        services.AddTransient<IProjectTreeBuilder, ProjectTreeBuilder>();
        services.AddTransient<CreateResourceHelper>();

        //
        // Register commands
        //

        services.AddTransient<IUpdateResourcesCommand, UpdateResourcesCommand>();
        services.AddTransient<ICreateResourceCommand, CreateResourceCommand>();
        services.AddTransient<IDeleteResourceCommand, DeleteResourceCommand>();
        services.AddTransient<ICopyResourceCommand, CopyResourceCommand>();
        services.AddTransient<ITransferResourcesCommand, TransferResourcesCommand>();

        services.AddTransient<IArchiveResourceCommand, ArchiveResourceCommand>();
        services.AddTransient<IUnarchiveResourceCommand, UnarchiveResourceCommand>();

        services.AddTransient<IUndoResourceCommand, UndoResourceCommand>();
        services.AddTransient<IRedoResourceCommand, RedoResourceCommand>();

        services.AddTransient<IListFolderContentsCommand, ListFolderContentsCommand>();
        services.AddTransient<IGetFileTreeCommand, GetFileTreeCommand>();
        services.AddTransient<IGetFileInfoCommand, GetFileInfoCommand>();
        services.AddTransient<IProjectCheckCommand, ProjectCheckCommand>();

        services.AddTransient<ISetFieldsCommand, SetFieldsCommand>();
        services.AddTransient<IRemoveFieldsCommand, RemoveFieldsCommand>();
        services.AddTransient<IAddTagsCommand, AddTagsCommand>();
        services.AddTransient<IRemoveTagsCommand, RemoveTagsCommand>();

        services.AddTransient<IGetFieldsCommand, GetFieldsCommand>();
        services.AddTransient<IInspectCommand, InspectCommand>();
        services.AddTransient<IFindTagCommand, FindTagCommand>();
        services.AddTransient<IListTagsCommand, ListTagsCommand>();

        services.AddTransient<IApplyRangeEditsCommand, ApplyRangeEditsCommand>();
        services.AddTransient<IEditFileCommand, EditFileCommand>();
        services.AddTransient<IMultiEditFileCommand, MultiEditFileCommand>();
        services.AddTransient<IReplaceFileCommand, ReplaceFileCommand>();
        services.AddTransient<IWriteFileCommand, WriteFileCommand>();
        services.AddTransient<IWriteBinaryFileCommand, WriteBinaryFileCommand>();
        services.AddTransient<ISetWriteableCommand, SetWriteableCommand>();
    }
}
