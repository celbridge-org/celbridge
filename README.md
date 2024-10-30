# What is Celbridge?

`Celbridge` is a user friendly tool that helps creative people make cool stuff. 

The goals of Celbridge are to:

- Make common development tasks easier and **more accessible** for makers.
- Act as **a bridge** between systems (e.g. apps, programming languages, libraries, environments).
- Support a wide range of functionality via **extensions**, with a simple core application.

<br>
<a href="https://github.com/AnTulcha/Celbridge/blob/main/Docs/Images/CelbridgeScreenshot.png" alt="Celbridge screenshot">
  <img width="400" heigth="400" src="https://github.com/AnTulcha/Celbridge/blob/main/Docs/Images/CelbridgeScreenshot.png?raw=true">
</a>
<br>

# Key Features

- Manage files via the `Explorer Panel`; add, move and delete files, with copy & paste and undo/redo.
- Edit text documents with syntax highlighting, code completion and split-screen preview.
- View common file types (images, audio, video, pdf, ...)
- Bookmark web pages as documents by creating simple `.web` files.
- Run scripts in the `Console Panel` via a built-in scripting engine, with command history.
- Add custom behaviours to files and folders via the `Inspector Window` and component system (similar to [Unity](https://docs.unity3d.com/6000.0/Documentation/Manual/UsingTheInspector.html) or [Unreal](https://dev.epicgames.com/documentation/en-us/unreal-engine/level-editor-details-panel-in-unreal-engine)).

Planned features include:

- An integrated `Python` scripting and sandboxed execution environment.
- A simple visual scripting language that transpiles to `Python` script.
- An extension system and package manager to allow the user community to add custom functionality.

# Installation

`Celbridge` is distributed as a `.msix` installer for `Windows PCs`.

- Download the latest `.msix` installer from the [releases section](https://github.com/AnTulcha/Celbridge/releases).
- Run the `.msix` installer

You can now run `Celbridge` from the start menu, or by opening a `.celbridge` project file via `Windows Explorer`.

> [!NOTE]
> `Celbridge` currently runs on `Windows`. A `MacOS` version is planned, and a `Linux` version may be possible in future.

# Building From Source

1. Clone the Celbridge git repo on your machine.
2. Install the latest version of `Visual Studio` (the free [Community Edition]() works fine).
3. Follow the `Visual Studio` setup instructions for [Uno Platform](https://platform.uno/docs/articles/get-started-vs-2022.html?tabs=ubuntu1804) development.
4. Open `Celbridge.sln` in `Visual Studio`.

Make sure you have `Celbridge (WinAppSDK Packaged)` and `net8.0-windows10.0.22621` selected in the `Visual Studio` configuration toolbar, then build and run the aplication.

> [!NOTE]
> `Uno Platform` performs some initial setup work behind the scenes when you first open the solution. Allow `Visual Studio` to idle for a minute or so after the solution opens, then close and reopen `Visual Studio`. You may need to repeat this process a couple times before the project will compile and run successfully.

# Roadmap

My goal is to develop `Celbridge` from a personal research project into a robust tool suitable for use in production environments, with a friendly and helpful user community. My own background is in game development, so the initial focus is on typical game development use cases like writing documentation and game narrative.

Please be aware that `Celbridge` is in **early development**! Every release may contain breaking changes for existing project created using the tool. Once the project reaches `v1.0` there will be a strong emphasis on maintaining compatibility for existing user projects.

There's very little user documentation available just yet, but I am working on it.

- [x] Proof-of-concept prototype
- [x] Application framework (`DI`, `MVVM`, extension architecture, public API, etc.)
- [x] File management via `Explorer Panel`
- [x] Scripting support (currently `.NET Interactive`)
- [x] `Console Panel`
- [x] Web page documents and file viewer using `WebView2`
- [x] Advanced text editing via [Monaco Editor]( https://microsoft.github.io/monaco-editor/)
- [ ] Initial pass on documentation
- [ ] Use [Json](https://www.json.org/json-en.html) & [Json Schema](https://json-schema.org/) for all project data files.
- [ ] Change scripting to use `Python` exclusively
- [ ] Investigate using a sandboxed execution environment, e.g. [Pyodide](https://pyodide.org)
- [ ] Global find and replace
- [ ] `Cel Script` visual scripting language
- [ ] `Cel Script` debugger
- [ ] Extension system and package manager

# Who am I?

I'm [Chris Gregan](https://github.com/chrisgregan) and I work at [Romero Games](https://romerogames.com/), a games studio based in Galway, Ireland. I've worked in games development for 20+ years and have a lot of experience with many programming languages and technologies, my favourites being C#, .NET and Python.

I am currently doing a Research Masters with [Technical University Dublin](https://www.tudublin.ie/), and I am using this project to help conduct my research.

I also created the [Fungus](https://github.com/snozbot/fungus) visual scripting tool for Unity Engine. That project is now maintained by the community.

# Contributions

All feedback and contributions are very welcome! I have a strong vision for the architecture of `Celbridge` that I hope will allow the project to scale successfully. If you would like to contribute a new feature, please open an issue first so we can discuss the best way to implement whatever you have in mind.

# Credits

This project was made possible by the Sabbatical Policy at [Romero Games](https://romerogames.com/). Huge thanks to Brenda, John and all of the incredible team at Romero Games for their support.❤️❤️❤️

Celbridge relies on code from these fantastic open source projects:

- [Uno Platform](https://platform.uno) 
- [Monaco Editor](https://microsoft.github.io/monaco-editor)
- [WebView2](https://github.com/MicrosoftEdge/WebView2Browser)
- [file-icons](https://github.com/file-icons/vscode/blob/master/LICENSE.md)
- [github-markdown-css](https://github.com/sindresorhus/github-markdown-css/blob/main/license)

Celbridge also uses a range of open source nuget packages, details of which can be found in the solution files.
