Celbridge is a tool for creative people who want to make cool stuff. The current version runs on Windows 11, other platforms are planned.

# Project Overview

Celbridge aims to:

- Make common tasks easier and more accessible for makers.
- Act as a bridge between systems (e.g. apps, programming languages, libraries, runtime environments).
- Support a wide range of functionality via extensions, with a simple core application.

> [!NOTE]
> This research was made possible by the Sabbatical Policy at Romero Games. Huge thanks to Brenda, John and all of the of the amazing 
team at Romero Games for their support.❤️❤️❤️

My goal is to develop Celbridge from a personal research project into a robust tool suitable for use in production environments, with a nice friendly community. My own background is in game development, so the initial focus is on typical game development use cases like writing documentation and narrative tools.

Celbridge is in **early development** which means there will be project breaking changes with (almost) every release. Do please do try it out though, all feedback and contributions are very welcome!

There's also little (i.e. no) user documentation available yet, this will be coming soon!

> [!NOTE]
> I would advise against using Celbridge in a production environment until we reach v1.0. From then on there will be a strong emphasis on maintaining compatibility with existing user projects. Until then, be aware that updating Celbridge could break your project!

# Build Instructions

1. Install the latest version of Visual Studio (Community Edition works fine)
1. Install the latest version of Visual Studio (the free [Community Edition]() works fine)
2. Follow the Visual Studio setup instructions for [Uno Platform](https://platform.uno/docs/articles/get-started-vs-2022.html?tabs=ubuntu1804) development
3. Open `Celbridge.sln` in Visual Studio

Make sure you have `Celbridge (WinAppSDK Packaged)` and `net8.0-windows10.0.22621` selected in the configuration toolbar.

> [!NOTE]
> Uno Platform does some initial setup work behind the scenes when you first open the solution. It's a bit quirky at the start. Just let it idle for a minute after the solution opens, then close and reopen Visual Studio. You may need to repeat this a couple of time before it will compile and run.

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
- [ ] No idea really

# Credits

This project uses code from these asweome open source projects:

- [file-icons](https://github.com/file-icons/vscode/blob/master/LICENSE.md)
- [github-markdown-css](https://github.com/sindresorhus/github-markdown-css/blob/main/license)
- [Monaco Editor](https://microsoft.github.io/monaco-editor)
- [WebView2](https://github.com/MicrosoftEdge/WebView2Browser)

We also use a range of open source nuget packages, details of which can be found in the solution files.
