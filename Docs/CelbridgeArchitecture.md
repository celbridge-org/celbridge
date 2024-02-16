# Extension Architecture

Celbridge use a `Vertical Slice Architecture`, with a strong emphasis on extensibility.  Celbridge is based on Uno Platform and targets the Windows and Skia+Gtk project heads.

All major features in Celbridge are implemented as self contained units, called `Extensions`. `Extensions` register with the core application at startup via `Dependency Injection`.

Extensions are implemented via a Uno Platform Class Library targeting .NET 7.  Extension assemblies are loaded at runtime via an AssemblyLoadContext so that they can be dynamically loaded & unloaded while the Celbridge is running.

Core features of the application are referred to as `Core Extensions`, user added features are called `User Extensions`. Both types are implemented in the exact same way, the only difference is that User Extensions are added externally 
while `Core Extensions` come built-in with the application.

The `Celbridge.Tests` project provides a comprehensive set of unit tests for all `Core Extensions`.

# Main Components

This architecture promotes a clean and modular approach, with Celbridge.BaseLibrary forming the core foundation. Each project has a distinct role, facilitating maintainability and scalability. The use of dependency injection through Celbridge.DIContainer enhances the flexibility and decoupling of the components, making the overall system robust and adaptable to changes.

Celbridge.MainApplication
- Purpose: This is the entry point and orchestrator of the entire application. It's responsible for initiating the startup sequence and managing the application's lifecycle.
- Direct Dependencies: Relies on Celbridge.DIContainer for dependency injection setup.
- Indirect Dependencies: Utilizes Celbridge.Extensions and Celbridge.UIComponents, which are loaded dynamically (often through reflection).

Celbridge.BaseLibrary
- Purpose: Serves as the shared kernel across the application. Contains all common interfaces, utilities, and possibly base classes.
- Direct Dependencies: None, ensuring its independence and reusability across the solution.
- Indirect Dependencies: None; it's a foundational layer used by other projects.

Celbridge.UIComponents
- Purpose: Dedicated to the UI layer of the application, housing all UI components and their implementations.
- Direct Dependencies: May reference Celbridge.BaseLibrary for shared interfaces and utilities.
- Indirect Dependencies: None; it’s designed to be consumed by Celbridge.MainApplication.

Celbridge.Extensions
- Purpose: Contains modular features or extensions of the application, implemented as separate units.
- Direct Dependencies: References Celbridge.BaseLibrary for necessary interfaces and shared resources.
- Indirect Dependencies: Loaded and managed by Celbridge.MainApplication and Celbridge.DIContainer, but without direct references to these projects.

Celbridge.CommonServices
- Purpose: Houses shared services like logging, data access, etc., that are used by various parts of the application.
- Direct Dependencies: Likely to depend on Celbridge.BaseLibrary for common interfaces and helpers.
- Indirect Dependencies: Services are consumed across the application, but the project itself doesn’t directly depend on other specific projects.

Celbridge.DIContainer
- Purpose: Manages and configures the dependency injection throughout the application, acting as the composition root.
- Direct Dependencies: References Celbridge.BaseLibrary for interfaces and needs knowledge of service implementations in projects like Celbridge.CommonServices, Celbridge.Extensions, and possibly Celbridge.UIComponents.
- Indirect Dependencies: None; it's central to dependency management but doesn’t depend on the functionality of other projects.

# Things that aren't Extensions

Some parts of the application should work even when no extensions (Core or User) are loaded.  The only way to modify 
the behaviour of these components is by changing the main Celbridge source code. This approach also facilitates a fast application startup time.

## Workspace

## Start View