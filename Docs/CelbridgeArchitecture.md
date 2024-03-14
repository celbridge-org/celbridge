# Extension Architecture

Celbridge use a `Vertical Slice Architecture`, with a strong emphasis on extensibility.  Celbridge is based on Uno Platform and targets the Windows and Skia + Gtk project heads for Linux and Mac support.

All major features in Celbridge are implemented as self contained units, called `Extensions`. `Extensions` register with the core application at startup via `Dependency Injection`.

Extensions are implemented via a Uno Platform Class Library targeting .NET 8.  Extension assemblies are loaded at runtime via an `AssemblyLoadContext` so that they can be dynamically loaded & unloaded while Celbridge is running.

Core features of the application are provided via `Core Extensions`. Optional user-added features are called `User Extensions`. Both extension types are implemented in the exact same way. User extensions are installed and registered externally while `Core Extensions` come built-in with the application. `Core Extensions` have access to a managed set of third party packages, in
particular for creating UI via Uno Platform. `User Extensions` should not use the Uno Platform UI packages as it would be very
difficult to maintain compatibility as we release updates to Celbridge. 

# Main Components

This architecture promotes a clean and modular approach, with Celbridge.BaseLibrary forming the core foundation. Each project has a distinct role, facilitating maintainability and scalability. The use of dependency injection enhances the flexibility and decoupling of the components, making the overall system robust and adaptable to changes.

# Solution Items
These configuration files control how Visual Studio builds the solution. In particular, we use Centralized Package Management (Directory.Packages.props) to define package versions for the entire solution. 

# Platforms Projects

## Celbridge.Shared
Common code and assets used by all supported platforms.

## Celbridge.Windows
The Windows project head. Set this as the startup project to build the Windows configuration.

## Celbridge.Skia.Gtk
The Windows project head. Set this as the startup project to build the Skia + Gtk configuration for Mac / Linux.

# Celbridge.Tests
The `Celbridge.Tests` project provides a comprehensive set of unit tests for all Services and `Core Extensions`. We focus our unit
tests on practical functional tests of specific subsystems rather than aiming for time consuming detailed unit tests and full code coverage.

# Celbridge.BaseLibrary
Serves as the standard shared API across all projects the application. Contains all common interfaces, utilities, and abstract base classes. It has no direct dependencies ensuring independence and reusability across the solution.
All interact with other elements of the application use this shared API. As the application evolves, this design ensures that extensions have access to a rich and powerful set of standardized functionality.

# Celbridge.Services
Houses shared services like logging, data access, etc., that are used by various parts of the application.
Services are consumed across the application, but this project should not directly depend on any other projects other than Celbridge.BaseLibrary.

# Celbridge.MainApplication
This is the entry point and orchestrator of the entire application. It's responsible for initiating the startup sequence, managing extensions and managing the application's lifecycle and UI setup.

# Celbridge.Views
Dedicated to the common UI layer of the application, housing the main UI components and their implementations.
The UI provided here is available even when no extensions are loaded. This design allows us to quickly load the core application, as well as giving us a lot of flexibility to modify the UI by loading different sets of extensions. 
This project provides several standardized controls that commonly used by extensions (e.g. confirmation dialog). This approach helps avoid extensions referencing each other or duplicating UI functionality.

# Celbridge.ViewModels
Provides the ViewModels used to interact with the views in Celbridge.Views, following the MVVM pattern. 

# Core Extensions

## Celbridge.CoreExtension

This project defines the core set of dependencies that other Core Extension projects use. It also provides a reference to
the Celbridge.BaseLibrary project. This project does not provide any other functionality or source code, it's only intended to
simplify the management of package dependencies and project references.

To use it in a CoreExtension project, just add it as a project reference in the .csproj file like so:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Celbridge.CoreExtension\Celbridge.CoreExtension.csproj" />
  </ItemGroup>
```

## Celbridge.Workspace, etc.

The other projects in the CoreExtensions folder implement a single tightly focussed feature for the application, following the `Vertical Slice Architecture` approach.

For example, the `Celbridge.Console` project contains all the Views, ViewModels and utility classes required to support the console panel UI. All externally accessible functionality for the console is abstracted via interfaces and abstract base classes in `Celbridge.BaseLibrary` and can be accessed via the `Dependency Injection` framework.
