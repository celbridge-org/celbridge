# Celbridge.CoreExtension

This project defines the core set of dependencies that other Core Extension projects use. It also provides a reference to 
the Celbridge.BaseLibrary project. This project does not provide any other functionality or source code, it's only intended to
simplify the management of package dependencies and project references.

To use it in a CoreExtension project, just add it as a project reference in the .csproj file:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Celbridge.CoreExtension\Celbridge.CoreExtension.csproj" />
  </ItemGroup>
```