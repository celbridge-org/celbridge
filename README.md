Celbridge is a user friendly tool that helps creative people make cool stuff. 

Celbridge aims to:

- make common development tasks easier and more accessible for makers.
- act as a bridge between systems (e.g. apps, programming languages, libraries, runtime environments).
- support a wide range of functionality via extensions, with a simple core application.

![Celbridge screenshot](/Docs/Images/CelbridgeScreenshot.png)

## Installation

Celbridge is distributed as an .msix installer for Windows PCs.

- Download the latest .msix installer from the [releases section](https://github.com/AnTulcha/Celbridge/releases).
- Run the .msix installer

You can now run Celbridge from the start menu, or by opening a `.celbridge` project file via Windows Explorer.

> [!NOTE]
> Celbridge currently supports Windows 11. A Mac version is planned, and a Linux version could be possible in future.


# Project Overview

My goal is to develop Celbridge from a personal research project into a robust tool suitable for use in production environments, with a friendly and helpful user community. My own background is in game development, so the initial focus is on typical game development use cases like writing documentation and game narrative.

Celbridge is in **early development** which means that there may be breaking changes with every release. Once the project reaches v1.0 there will be a strong emphasis on maintaining compatibility for existing user projects. Until then, please be aware that updating to a newer version of Celbridge could break your project!

There's very little user documentation available just yet, but I am working on it.

All feedback and contributions are very welcome!

> [!NOTE]
> This project was made possible by the Sabbatical Policy at Romero Games. Huge thanks to Brenda, John and all of the of the amazing team at Romero Games for their support.❤️❤️❤️

# Who am I?

I'm [Chris Gregan](https://github.com/chrisgregan) and I work at [Romero Games](https://romerogames.com/), a games studio based in Galway, Ireland. I've worked in games development for 20+ years and have a lot of experience with many programming languages
and technologies, my favourites being C#, .NET and Python.

I am currently doing a Research Masters with [Technical University Dublin](https://www.tudublin.ie/), and I am using Celbridge to help conduct this research.

I also created the [Fungus](https://github.com/snozbot/fungus) visual scripting tool for the Unity game engine (now maintained by the community).

# Roadmap

- [x] Proof-of-concept prototype
- [x] Application framework (DI, MVVM, extension architecture, public API, etc.)
- [x] File management via Explorer window
- [x] Scripting support (C# Interactive)
- [x] Console window
- [x] Web page documents and file viewer using WebView2
- [x] Advanced text editing via [Monaco Editor]( https://microsoft.github.io/monaco-editor/)
- [ ] Initial pass on documentation
- [ ] Change scripting to use Python
- [ ] Global find and replace
- [ ] Cel Script programming language
- [ ] Cel Script debugger
- [ ] No idea yet honestly

# Credits

This project uses code from these asweome open source projects:

- [Uno Platform](https://platform.uno) 
- [Monaco Editor](https://microsoft.github.io/monaco-editor)
- [WebView2](https://github.com/MicrosoftEdge/WebView2Browser)
- [file-icons](https://github.com/file-icons/vscode/blob/master/LICENSE.md)
- [github-markdown-css](https://github.com/sindresorhus/github-markdown-css/blob/main/license)

We also use a range of open source nuget packages, details of which can be found in the solution files.
