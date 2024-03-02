# Workspaces and Services

The Celbridge application only edits a single project at a time. It does not support loading multiple
projects in separate workspaces and switching between them. 

This is unfeasible for several reasons:
- Different projects may use different extension assemblies, or even different versions of the same assemblies.
  - This adds a lot of complexity and scope for bugs when loading and unloading extension assemblies
- Each separate workspace would require its own dependency injection setup
  - Most third-party packages are not designed or tested to support multiple instances in the same application.
  - Services that use shared resources (e.g. file i/o, web sockets, background processes/threads) would likely conflict.
  - Any static variables used in any assembly would need to be reset when switching between projects

However, we can provide the _appearance_ of seamlessly switching between projects by making the UI appear as 
if there are multiple projects loaded (the same trick is used on iOS to simulate OS multitasking).

To perform a project switch, the current project and all extension assemblies are unloaded and the new project is 
loaded in. All user interaction is blocked while this is in progress. To make this appear seamless, the workspace
must be restored to the same state as it was last in, e.g. open the same documents, select the same entity in the 
inspector.

The main downside of this approach is that the user has to wait while this unloading/loading occurs. This is an
acceptable trade-off for a robust and relatively easy to maintain system.  