using Celbridge.Tasks;
using Celbridge.Utils;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using System.Numerics;
using System.Reflection;

namespace Celbridge.Services
{
    public interface ICelScriptService
    {
        public Dictionary<Guid, ICelScript> CelScripts { get; }
        public SyntaxColors SyntaxColors { get; }
        public Task<Result<ICelScript>> LoadCelScriptAsync(IProject project, FileResource fileResource);
        public Task<Result> LoadAllCelScripts(IProject project);
        public Result UnloadAllCelScripts();
        public Task<Result> SaveCelScriptAsync(IProject project, FileResource fileResource, ICelScript celScript);
        public Result<ICelScript> GetCelScriptByName(IProject project, string celScriptName);
        Result CreateCel(ICelScript celScript, ICelType celType, string celName, Vector2 spawnPosition);
        Result DeleteCel(ICelScriptNode cel);
        Task<Result<string>> GenerateCelSignatures(string projectFolder, string libraryPath);
        Result<ICelSignature> CreateCelSignature(string celScriptName, string celName);
        Task<Result<string>> BuildApplication(string libraryPath);
        Task<Result> StartApplication(string celScriptName, string celName, string projectFolder, string libraryFolder, string chatAPIKey, string sheetsAPIKey);
    }

    public record CelScriptAddedMessage(Guid ResourceId);
    public record CelScriptDeletedMessage(Guid ResourceId);
    public record CelScriptChangedMessage(Guid ResourceId);

    public class CelScriptService : ICelScriptService
    {
        private IMessenger _messengerService;
        private ICelTypeService _celTypeService;
        private IResourceService _resourceService;
        private IProjectService _projectService;
        private IDialogService _dialogService;

        public SyntaxColors SyntaxColors { get; } = new SyntaxColors();

        public Dictionary<Guid, ICelScript> CelScripts { get; private set; } = new ();

        private UpdateSyntaxFormatTask? _updateSyntaxFormatTask;

        public CelScriptService(IMessenger messengerService,
            ICelTypeService celTypeService,
            IResourceService resourceService,
            IProjectService projectService,
            IDialogService dialogService)
        {
            _messengerService = messengerService;
            _celTypeService = celTypeService;
            _resourceService = resourceService;
            _projectService = projectService;
            _dialogService = dialogService;

            _messengerService.Register<ActiveProjectChangedMessage>(this, OnActiveProjectChanged);
            _messengerService.Register<ApplicationClosingMessage>(this, OnApplicationClosing);
        }

        private void OnActiveProjectChanged(object recipient, ActiveProjectChangedMessage message)
        {
            var activeProject = message.Project;
            if (activeProject == null)
            {
                _messengerService.Unregister<ResourcesChangedMessage>(this);
                _messengerService.Unregister<SelectedEntityChangedMessage>(this);
                _messengerService.Unregister<EntityPropertyChangedMessage>(this);

                // Unload all currently loaded CelScripts
                UnloadAllCelScripts();

                // Unload the custom assemblies
                var services = (Application.Current as App)!.Host!.Services;
                var loadCustomAssembliesTask = services.GetRequiredService<LoadCustomAssembliesTask>();
                var unloadResult = loadCustomAssembliesTask.Unload();
                if (unloadResult is ErrorResult unloadError)
                {
                    Log.Error($"Failed to unload custom assemblies. {unloadError.Message}");
                }
            }
            else
            {
                _messengerService.Register<ResourcesChangedMessage>(this, OnResourcesChanged);
                _messengerService.Register<SelectedEntityChangedMessage>(this, OnSelectedEntityChanged);
                _messengerService.Register<EntityPropertyChangedMessage>(this, OnEntityPropertyChanged);
            }
        }

        private void OnApplicationClosing(object recipient, ApplicationClosingMessage message)
        {
            // Speculative fix for an exception crash on exit.
            // The exception happens in InstructionLinePropertyViewModel when handling the
            // CelSyntaxFormatUpdatedMessage and attempting to access the KeywordColor.Color property.
            // It's a weird COM/Native code error that suggests that the data structure isn't valid any more,
            // presumably because the app is in the process of shutting down.
            // The fix here is to stop updating syntax format during shutdown.

            if (_updateSyntaxFormatTask != null)
            {
                _updateSyntaxFormatTask.Dispose();
                _updateSyntaxFormatTask = null;
            }
        }

        private void OnSelectedEntityChanged(object recipient, SelectedEntityChangedMessage message)
        {
            var selectedEntity = message.Entity;

            if (_updateSyntaxFormatTask != null)
            {
                // Destroy any existing syntax format task
                _updateSyntaxFormatTask.Dispose();
                _updateSyntaxFormatTask = null;
            }

            if (selectedEntity is ICel cel)
            {
                // Start a new syntax format task
                var services = (Application.Current as App)!.Host!.Services;
                _updateSyntaxFormatTask = services.GetRequiredService<UpdateSyntaxFormatTask>();

                _updateSyntaxFormatTask.CelSyntaxFormatUpdated += (celSyntaxFormat) =>
                {
                    // I suspect a race condition with the project being nulled while the syntax update is happening
                    Guard.IsNotNull(_projectService.ActiveProject);

                    // Notify listeners that the syntax format has been updated
                    var message = new CelSyntaxFormatUpdatedMessage(celSyntaxFormat);
                    _messengerService.Send(message);
                };

                _ = _updateSyntaxFormatTask.Start(cel);
            }
        }

        private void OnEntityPropertyChanged(object recipient, EntityPropertyChangedMessage message)
        {
            var entity = message.Entity;
            if (entity is ICel cel)
            {
                // Todo: Update the Cel instructions on a timer instead of every time a property changes
                // Ideally this would be done asynchronously, but we need to be careful about race conditions like
                // setting a property at the same time as the instuction is being deleted. Some kind of buffered update
                // might be needed.
                UpdateCelInstructions(cel);
            }
        }

        private Result UpdateCelInstructions(ICel cel)
        {
            var services = (Application.Current as App)!.Host!.Services;
            var updateCelInstructions = services.GetRequiredService<UpdateCelInstructionsTask>();
            return updateCelInstructions.Update(cel);
        }

        public async Task<Result> LoadAllCelScripts(IProject project)
        {
            var celScriptResources = _resourceService.FindResourcesOfType<CelScriptResource>(project);

            bool failed = false;
            foreach (var celScriptResource in celScriptResources)
            {
                var result = await LoadCelScriptAsync(project, celScriptResource);
                if (result.Failure)
                {
                    // Todo: Put this resource into an error state so user can attempt to fix it
                    var error = result as ErrorResult<ICelScript>;
                    Log.Error($"Failed to load CelScript '{celScriptResource.Name}'. {error!.Message}");
                    failed = true;
                }
            }

            if (failed)
            {
                return new ErrorResult("Failed to load all CelScripts");
            }

            return new SuccessResult();
        }

        public Result UnloadAllCelScripts()
        {
            CelScripts.Clear();
            return new SuccessResult();
        }

        public async Task<Result<string>> GenerateCelSignatures(string projectFolder, string libraryPath)
        {
            var services = (Application.Current as App)!.Host!.Services;
            var generateTask = services.GetRequiredService<GenerateCelSignaturesTask>();

            var generateResult = await generateTask.Generate(projectFolder, libraryPath);
            if (generateResult is ErrorResult<string> generateError)
            {
                // Todo: How should we handle a failure here? Log an error and load the previously working DLL? Refuse to load?
                // Think we just need to make this process very robust so it always generates a loadable DLL.
                return new ErrorResult<string>($"Failed to generate Cel Signatures. {generateError.Message}");
            }

            string assemblyFile = generateResult.Data!;

            return new SuccessResult<string>(assemblyFile);
        }

        public Result<ICelSignature> CreateCelSignature(string celScriptName, string celName)
        {
            // Get the currently loaded CelSignatures assembly
            var services = (Application.Current as App)!.Host!.Services;
            var loadCustomAssembliesTask = services.GetRequiredService<LoadCustomAssembliesTask>();
            Guard.IsNotNull(loadCustomAssembliesTask);

            var celSignatureAssembly = loadCustomAssembliesTask.CelSignatureAssembly;
            Guard.IsNotNull(celSignatureAssembly);

            var celSignaturesAssembly = celSignatureAssembly.Target as Assembly;

            // Look for a matching type in the CelSignaturesAssembly
            var findResult = ReflectionUtils.FindTypeInAssembly(celSignaturesAssembly, celScriptName, "Celbridge.Models.CelSignatures");
            if (findResult is ErrorResult<Type> findError)
            {
                return new ErrorResult<ICelSignature>($"Failed to find CelSignature container class. {findError.Message}");
            }

            var containerType = findResult.Data!;
            var celSignatureType = containerType.GetNestedType(celName);
            if (celSignatureType == null)
            {
                return new ErrorResult<ICelSignature>("Failed to find CelSignature in container type.");
            }

            var celSignature = Activator.CreateInstance(celSignatureType) as ICelSignature;
            Guard.IsNotNull(celSignature);

            IRecord record = celSignature;
            Guard.IsNotNull(record);

            return new SuccessResult<ICelSignature>(celSignature);
        }

        public async Task<Result<string>> BuildApplication(string libraryPath)
        {
            var buildApplicationTask = (Application.Current as App)!.Host!.Services.GetRequiredService<BuildApplicationTask>();
            var celScripts = CelScripts.Values.ToList();
            var buildResult = await buildApplicationTask.BuildApplication(celScripts, libraryPath);
            if (buildResult is ErrorResult<string> buildError)
            {
                Log.Error(buildError.Message);
                return buildResult;
            }

            return buildResult;
        }

        public async Task<Result> StartApplication(string celScriptName, string celName, string projectFolder, string libraryPath, string chatAPIKey, string sheetsAPIKey)
        {
            var activeProject = _projectService.ActiveProject;
            if (activeProject == null)
            {
                return new ErrorResult("Failed to start application");
            }

            var libraryFolder = activeProject.LibraryFolder;
            var buildResult = await BuildApplication(libraryFolder);
            if (buildResult is ErrorResult<string> buildError)
            {
                return new ErrorResult($"Failed to start application. {buildError.Message}");
            }

            var assemblyLocation = buildResult.Data!;

            // Load the application assembly
            var celApplicationTask = new LoadAndRunCelApplicationTask();
            var loadResult = celApplicationTask.Load(assemblyLocation);
            if (loadResult is ErrorResult loadError)
            {
                return new ErrorResult($"Failed to start application. {loadError.Message}");
            }

            void print(string message)
            {
                Log.Information(message);
            };

            var runResult = await celApplicationTask.Run(celScriptName, celName, projectFolder, print, chatAPIKey, sheetsAPIKey);
            if (runResult is ErrorResult runError)
            {
                celApplicationTask.Unload();
                Log.Error(runError.Message);
                return new ErrorResult($"Failed to run application. {runError.Message}");
            }

            celApplicationTask.Unload();

            return new SuccessResult();
        }

        private void OnResourcesChanged(object recipient, ResourcesChangedMessage message)
        {
            List<Guid> added = new ();
            foreach (var resourceId in message.Added)
            {
                if (!CelScripts.ContainsKey(resourceId))
                {
                    added.Add(resourceId);
                }
            }

            List<Guid> deleted = new ();
            foreach (var resourceId in message.Deleted)
            {
                if (CelScripts.ContainsKey(resourceId))
                {
                    deleted.Add(resourceId);
                }
            }

            /*
            List<Guid> changed = new ();
            foreach (var resourceId in message.Changed)
            {
                if (CelScripts.ContainsKey(resourceId))
                {
                    changed.Add(resourceId);
                }
                else
                {
                    // Shouldn't happen, but try to get back to a consistent state if it does
                    added.Add(resourceId);
                }
            }
            */

            if (added.Count > 0 || deleted.Count > 0)
            {
                _ = ProcessResourceChanges(added, deleted);
            }
        }

        private async Task ProcessResourceChanges(List<Guid> added, List<Guid> deleted)
        {
            try
            {
                var activeProject = _projectService.ActiveProject;
                Guard.IsNotNull(activeProject);

                if (_dialogService.IsProgressDialogActive)
                {
                    // For now, just assume that an active Progress Dialog indicates that the project is loading.
                    // In this case it's best to allow the project loading process to load the CelScripts.
                    return;
                }

                _dialogService.ShowProgressDialog("Load Cel Scripts", null);

                List<object> messages = new();

                // Process deleted CelScript resources
                foreach (var resourceId in deleted)
                {
                    if (CelScripts.Remove(resourceId))
                    {
                        var deletedMessage = new CelScriptDeletedMessage(resourceId);
                        messages.Add(deletedMessage);
                    }
                }

                // Process added CelScript resources
                foreach (var resourceId in added)
                {
                    // Remove any previously cached version of this CelScript
                    CelScripts.Remove(resourceId);

                    var findResourceResult = _resourceService.FindResourceEntity(activeProject, resourceId);
                    if (findResourceResult is ErrorResult<IEntity> findError)
                    {
                        Log.Error($"Failed to find CelScript resource '{resourceId}'. {findError.Message}");
                        continue;
                    }

                    var celScriptResource = findResourceResult.Data as CelScriptResource;
                    if (celScriptResource == null)
                    {
                        // Ignore added resources which are not CelScripts
                        continue;
                    }

                    var loadResult = await LoadCelScriptAsync(activeProject, celScriptResource);
                    if (loadResult is ErrorResult<ICelScript> loadError)
                    {
                        Log.Error($"Failed to load added CelScript resource '{resourceId}'. {loadError.Message}");
                        continue;
                    }

                    var addedMessage = new CelScriptAddedMessage(resourceId);
                    messages.Add(addedMessage);
                }

                // Process changed CelScript resources
                // Todo: There's no need to reload the CelScript when it changes?
                // We do need to update the signature DLL when the signature changes, but that's a separate process!
                /*
                foreach (var resourceId in changed)
                {
                    // Remove any previously cached version of this CelScript
                    CelScripts.Remove(resourceId);

                    var findResourceResult = _resourceService.FindResourceEntity(activeProject, resourceId);
                    if (findResourceResult is ErrorResult<IEntity> findError)
                    {
                        Log.Error($"Failed to find CelScript resource '{resourceId}'. {findError.Message}");
                        continue;
                    }

                    var celScriptResource = findResourceResult.Data as CelScriptResource;
                    Guard.IsNotNull(celScriptResource);

                    var loadResult = await LoadCelScriptAsync(activeProject, celScriptResource);
                    if (loadResult is ErrorResult<CelScript> loadError)
                    {
                        Log.Error($"Failed to load changed CelScript resource '{resourceId}'. {loadError.Message}");
                        continue;
                    }

                    var changedMessage = new CelScriptChangedMessage(resourceId);
                    messages.Add(changedMessage);
                }
                */

                // CelScripts are now up to date, so it's safe to notify listeners about the adds and deletes

                foreach (var message in messages)
                {
                    _messengerService.Send(message);

                    var m = message.ToString();
                    if (m is not null)
                    {
                        Log.Information(m);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to process CelScript resource changes. {ex}");
            }

            if (_dialogService.IsProgressDialogActive)
            {
                _dialogService.HideProgressDialog();
            }
        }


        public async Task<Result<ICelScript>> LoadCelScriptAsync(IProject project, FileResource fileResource)
        {
            Guard.IsNotNull(fileResource);

            if (CelScripts.TryGetValue(fileResource.Id, out var existingCelScript))
            {
                // CelScript is already loaded, so return cached version
                return new SuccessResult<ICelScript>(existingCelScript);
            }

            var pathResult = _resourceService.GetResourcePath(project, fileResource);
            if (pathResult.Failure)
            {
                var error = pathResult as ErrorResult<string>;
                return new ErrorResult<ICelScript>(error!.Message);
            }
            var path = pathResult.Data!;

            try
            {
                var json = await File.ReadAllTextAsync(path);
                var result = DeserializeCelScript(json);
                if (result.Failure)
                {
                    var error = result as ErrorResult<ICelScript>;
                    return new ErrorResult<ICelScript>($"Failed to load CelScript. {error!.Message}");
                }

                var celScript = result.Data!;

                celScript.Entity = fileResource;

                // Set the FileResource entity as the parent of the CelScript
                var treeNode = celScript as ITreeNode;
                Guard.IsNotNull(treeNode);

                ParentNodeRef.SetParent(treeNode, fileResource);

                CelScripts.Add(fileResource.Id, celScript);

                return new SuccessResult<ICelScript>(celScript);
            }
            catch (Exception ex)
            {
                return new ErrorResult<ICelScript>($"Failed to load CelScript. {ex.Message}");
            }
        }

        public async Task<Result> SaveCelScriptAsync(IProject project, FileResource fileResource, ICelScript celScript)
        {
            Guard.IsNotNull(fileResource);

            var pathResult = _resourceService.GetResourcePath(project, fileResource);
            if (pathResult.Failure)
            {
                var error = pathResult as ErrorResult<string>;
                return new ErrorResult(error!.Message);
            }
            var path = pathResult.Data!;

            var serializeResult = SerializeCelScript(celScript);
            if (serializeResult.Failure)
            {
                var error = serializeResult as ErrorResult<string>;
                return new ErrorResult(error!.Message);
            }
            var serializedData = serializeResult.Data!;

            return await FileUtils.SaveTextAsync(path, serializedData);
        }

        public Result<ICelScript> GetCelScriptByName(IProject project, string celScriptName)
        {
            if (string.IsNullOrEmpty(celScriptName))
            {
                return new ErrorResult<ICelScript>($"Failed to find CelScript because name is empty.'");
            }

            // Ensure the name ends with .cel to match the entity name
            celScriptName = Path.ChangeExtension(celScriptName, ".cel");

            foreach (var kv in CelScripts)
            {
                var celScript = kv.Value;

                var entity = celScript.Entity;
                Guard.IsNotNull(entity);

                if (entity.Name.Equals(celScriptName, StringComparison.OrdinalIgnoreCase))
                {
                    return new SuccessResult<ICelScript>(celScript);
                }
            }

            return new ErrorResult<ICelScript>($"Failed to find CelScript '{celScriptName}'");
        }

        private Result<string> SerializeCelScript(ICelScript celScript)
        {
            try
            {
                // Special json settings that handles CelSignature types correctly
                var jsonSettings = CelScriptJsonSettings.Create();

                string json = JsonConvert.SerializeObject(celScript, jsonSettings);
                return new SuccessResult<string>(json);
            }
            catch (Exception ex)
            {
                return new ErrorResult<string>(ex.Message);
            }
        }

        private Result<ICelScript> DeserializeCelScript(string json)
        {
            try
            {
                // Special json settings that handles CelSignature types correctly
                var jsonSettings = CelScriptJsonSettings.Create();
                var celScript = JsonConvert.DeserializeObject<CelScript>(json, jsonSettings);
                Guard.IsNotNull(celScript);

                // Populate the CelType property for all cels, based on the serialized CelTypeName property.
                foreach (var celScriptNode in celScript.Cels)
                {
                    var cel = celScriptNode as ICel;
                    Guard.IsNotNull(cel);

                    var celTypeResult = _celTypeService.GetCelType(cel.CelTypeName);
                    if (celTypeResult is ErrorResult<ICelType> error)
                    {
                        // Todo: Should we delete the Cel if the CelType can't be found? Maybe default it to another CelType?
                        // We should also check for deserialized Instructions that are no longer supported by the CelType.
                        // A reasonable way to handle this might be to have an InvalidInstruction instruction that contains the
                        // serialized Json of the broken instruction. Probably better than just deleting the instruction.
                        // For now, just log the error and continue.

                        Log.Error($"Failed to deserialize Cel. {error.Message}");
                        continue;
                    }

                    var celType = celTypeResult.Data!;
                    var c = (cel as Cel);
                    Guard.IsNotNull(c);
                    c.CelType = celType;

                    // Ensure the instruction metadata is up to date
                    UpdateCelInstructions(cel);
                }

                return new SuccessResult<ICelScript>(celScript);
            }
            catch (Exception ex)
            {
                return new ErrorResult<ICelScript>(ex.Message);
            }
        }

        public Result CreateCel(ICelScript celScript, ICelType celType, string celName, Vector2 spawnPosition)
        {
            var cel = new Cel();

            cel.Name = celName;
            cel.Id = Guid.NewGuid();
            cel.CelType = celType;
            cel.CelTypeName = celType.Name;
            cel.CelScript = celScript;
            cel.X = (int)spawnPosition.X;
            cel.Y = (int)spawnPosition.Y;

            celScript.AddCel(cel);

            return new SuccessResult();
        }

        public Result DeleteCel(ICelScriptNode cel)
        {
            var celScript = cel.CelScript;
            Guard.IsNotNull(celScript);

            return celScript.DeleteCel(cel);
        }
    }
}