# Celbridge.WorkspaceDependencies

This project defines the core set of dependencies that other Workspace extension projects use. It also provides a reference to 
the Celbridge.BaseLibrary project. This project does not provide any other functionality or source code, it's only intended to
simplify the management of package dependencies and project references for workspace extension projects.

To use it in a workspace extension project, add it as a project reference in your .csproj file:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Celbridge.WorkspaceDependencies\Celbridge.WorkspaceDependencies.csproj" />
  </ItemGroup>
```