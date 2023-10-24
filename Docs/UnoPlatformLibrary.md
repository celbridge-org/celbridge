# Uno Platform Library

<!-- Q: What is an Uno Platform Library? -->

- A Visual Studio project that generates a cross platform assembly for use in a Uno Application

<!-- Q: How do I add a new Uno Platform Library to the project? -->

- Install the [Visual Studio Solution Templates](https://platform.uno/docs/articles/get-started-vs-2022.html)
- In Visual Studio, right click on the solution and select `Add > New Project`
- Select `New Platform Library` and create a new library with the required name
- Open the .csproj file for the library and remove any unneccessary platforms from <TargetFrameworks>
- Right click on the project and select Manage Nuget Packages
- Update any out of date packages to the latest version (e.g. Uno.WinUI)

