using Celbridge.Models;
using Celbridge.Services;
using Celbridge.Tasks;
using Celbridge.ViewModels;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge
{
    public class App : Application
    {
        public Window? MainWindow { get; private set; }
        public IHost? Host { get; private set; }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var builder = this.CreateBuilder(args)
                .Configure(host => host
#if DEBUG
				// Switch to Development environment when running in DEBUG
				.UseEnvironment(Environments.Development)
#endif
                    .UseConfiguration(configure: configBuilder =>
                        configBuilder
                            .EmbeddedSource<App>()
                            .Section<AppConfig>()
                    )
                    // Enable localization (see appsettings.json for supported languages)
                    .UseLocalization()
                    .ConfigureServices((context, services) =>
                    {
                        RegisterServices(services);
                    })
                );
            MainWindow = builder.Window;

            Host = builder.Build();

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (MainWindow.Content is not Frame rootFrame)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                // Place the frame in the current Window
                MainWindow.Content = rootFrame;
            }

            rootFrame.Loaded += (s, e) =>
            {
                // XamlRoot is required for displaying content dialogs
                var dialogService = Host.Services.GetRequiredService<IDialogService>();
                Guard.IsNotNull(rootFrame.XamlRoot);

                dialogService.XamlRoot = rootFrame.XamlRoot;

                // Start monitoring for save requests
                var saveDataService = Host.Services.GetRequiredService<ISaveDataService>();
                _ = saveDataService.StartMonitoringAsync(0.25);
            };

            if (rootFrame.Content == null)
            {
                // When the navigation stack isn't restored navigate to the first page,
                // configuring the new page by passing required information as a navigation
                // parameter
                rootFrame.Navigate(typeof(Shell), args.Arguments);
            }
            // Ensure the current window is active
            MainWindow.Activate();
        }

        private void RegisterServices(IServiceCollection services)
        {
            IMessenger messengerService = WeakReferenceMessenger.Default;
            ISettingsService settingsService = new SettingsService(messengerService);
            IResourceTypeService resourceTypeService = new ResourceTypeService();
            IResourceService resourceService = new ResourceService(messengerService, resourceTypeService);
            IInspectorService inspectorService = new InspectorService(messengerService);
            ISaveDataService saveDataService = new SaveDataService(messengerService);
            IDocumentService documentService = new DocumentService(messengerService);
            IDialogService dialogService = new DialogService(messengerService);
            IProjectService projectService = new ProjectService(messengerService, settingsService, saveDataService, resourceService, documentService, dialogService, inspectorService);
            IAppThemeService appThemeService = new AppThemeService(settingsService);
            IAIService aiService = new AIService();
            IConsoleService consoleService = new ConsoleService(aiService);
            ICelTypeService celTypeService = new CelTypeService();
            ICelScriptService celScriptService = new CelScriptService(messengerService, celTypeService, resourceService, projectService, dialogService);

            services.AddSingleton(messengerService);
            services.AddSingleton(settingsService);
            services.AddSingleton(resourceTypeService);
            services.AddSingleton(celTypeService);
            services.AddSingleton(resourceService);
            services.AddSingleton(saveDataService);
            services.AddSingleton(dialogService);
            services.AddSingleton(projectService);
            services.AddSingleton(appThemeService);
            services.AddSingleton(consoleService);
            services.AddSingleton(inspectorService);
            services.AddSingleton(documentService);
            services.AddSingleton(aiService);
            services.AddSingleton(celScriptService);
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<ConsoleViewModel>();
            services.AddSingleton<ProjectViewModel>();
            services.AddSingleton<LeftNavigationBarViewModel>();
            services.AddSingleton<RightNavigationBarViewModel>();
            services.AddSingleton<StatusBarViewModel>();
            services.AddSingleton<DocumentsViewModel>();
            services.AddSingleton<InspectorViewModel>();
            services.AddSingleton<DetailViewModel>();
            services.AddSingleton<MainMenuViewModel>();
            services.AddTransient<NewProjectViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<AddResourceViewModel>();
            services.AddTransient<AddCelViewModel>();
            services.AddTransient<ProgressDialogViewModel>();
            services.AddTransient<CelCanvasViewModel>();
            services.AddTransient<TextFileDocumentViewModel>();
            services.AddTransient<CelScriptDocumentViewModel>();
            services.AddTransient<PropertyListViewModel>();
            services.AddTransient<NumberPropertyViewModel>();
            services.AddTransient<TextPropertyViewModel>();
            services.AddTransient<BooleanPropertyViewModel>();
            services.AddTransient<ExpressionPropertyViewModel>();
            services.AddTransient<PathPropertyViewModel>();
            services.AddTransient<RecordPropertyViewModel>();
            services.AddTransient<RecordSummaryPropertyViewModel>();
            services.AddTransient<InstructionLinePropertyViewModel>();
            services.AddTransient<CallArgumentsPropertyViewModel>();
            services.AddTransient<CelConnectionLineViewModel>();
            services.AddTransient<LoadProjectTask>();
            services.AddTransient<UpdateSyntaxFormatTask>();
            services.AddTransient<GenerateCelSignaturesTask>();
            services.AddTransient<UpdateCelInstructionsTask>();
            services.AddSingleton<LoadCustomAssembliesTask>();
            services.AddTransient<BuildApplicationTask>();
        }
    }
}