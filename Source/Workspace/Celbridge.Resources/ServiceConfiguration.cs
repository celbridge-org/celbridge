using Celbridge.Resources.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services;

namespace Celbridge.Resources;

public static class ServiceConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        //
        // Register services
        //

        services.AddSingleton<IResourceOperationService, ResourceOperationService>();
        services.AddSingleton<IFileTemplateService, FileTemplateService>();

        services.AddTransient<IResourceService, ResourceService>();
        services.AddTransient<IResourceTransferService, ResourceTransferService>();
        services.AddTransient<IResourceNameValidator, ResourceNameValidator>();
        services.AddTransient<IResourceMonitor, ResourceMonitor>();
        services.AddTransient<IResourceFileSystem, LocalResourceFileSystem>();
        services.AddTransient<ITrashService, TrashService>();
        services.AddTransient<IResourceScanner, ResourceScanner>();
        services.AddTransient<ISidecarService, SidecarService>();
        services.AddTransient<IResourceClassifier, ResourceClassifier>();
        services.AddTransient<IProjectTreeBuilder, ProjectTreeBuilder>();
        services.AddTransient<AddResourceHelper>();

        //
        // Register commands
        //

        services.AddTransient<IUpdateResourcesCommand, UpdateResourcesCommand>();
        services.AddTransient<IAddResourceCommand, AddResourceCommand>();
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

        services.AddTransient<ISetFieldCommand, SetFieldCommand>();
        services.AddTransient<IRemoveFieldCommand, RemoveFieldCommand>();
        services.AddTransient<IAddTagCommand, AddTagCommand>();
        services.AddTransient<IRemoveTagCommand, RemoveTagCommand>();
        services.AddTransient<IWriteBlockCommand, WriteBlockCommand>();
        services.AddTransient<IRemoveBlockCommand, RemoveBlockCommand>();

        services.AddTransient<IGetFieldCommand, GetFieldCommand>();
        services.AddTransient<IReadBlockCommand, ReadBlockCommand>();
        services.AddTransient<IGetInfoCommand, GetInfoCommand>();
        services.AddTransient<IFindTagCommand, FindTagCommand>();

        services.AddTransient<IApplyRangeEditsCommand, ApplyRangeEditsCommand>();
        services.AddTransient<IEditFileCommand, EditFileCommand>();
        services.AddTransient<IMultiEditFileCommand, MultiEditFileCommand>();
        services.AddTransient<IReplaceFileCommand, ReplaceFileCommand>();
        services.AddTransient<IWriteFileCommand, WriteFileCommand>();
        services.AddTransient<IWriteBinaryFileCommand, WriteBinaryFileCommand>();
    }
}
