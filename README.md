# What is Celbridge?

Celbridge is an open source tool that provides a bridge between spreadsheets and Python scripting. Edit spreadsheets and write Python scripts side-by-side in a user-friendly graphical interface, making it easy to enter, analyze, and automate your data. 

**The goals of Celbridge are to:**

* Make **common tasks with data** easier and more accessible.
* Keep your data **local-first** and **private** by default.
* Act as **a bridge** between Python scripting and widely used filetypes such as Excel, CSV, Markdown, JSON, HTML, CSS, etc.).
* Support easy extensibility via **Python packages**, with a core editor application written in [.NET](https://dotnet.microsoft.com/en-us/) using [Uno Platform](https://platform.uno/).
* Provide quick access to the **web applications** you regularly use (e.g. dashboards, support portals, docs, etc).

> [!NOTE]
> Many thanks to [MESCIUS SpreadJS](https://developer.mescius.com/spreadjs) for sponsoring Celbridge and supporting open source developers!

<br>
<a href="https://github.com/AnTulcha/Celbridge/blob/main/docs/images/celbridge.gif" alt="Celbridge screenshot GIF">
  <img width="400" heigth="400" src="https://github.com/AnTulcha/Celbridge/blob/main/docs/images/celbridge.gif?raw=true">
</a>
<br>

# Key Features

> [!NOTE]
> Celbridge is still in **early development**. Any update may introduce incompatibilities with previous versions or breaking architectural changes, so always back up any data before upgrading.

## Privacy and Security

* Celbridge is designed to keep your data local and private by default.
* All files in Celbridge are managed locally on your machine, not in the cloud — unless you upload them yourself.

## Python Integration

* An integrated Python interpreter, with configurable Python version and packages.
* Fast and easy package installation using [uv](https://docs.astral.sh/uv/): no need to manage virtual environments. 
* An integrated read–eval–print loop (REPL), featuring
    * Syntax highlighting
    * Command history
    * Command completion
    * [IPython](https://ipython.readthedocs.io/en/stable/index.html) magic commands (%run, %alias, %ls, etc.)
    * [pdb](https://docs.python.org/3/library/pdb.html#module-pdb) command line debugger
* Run Python scripts with a single click. 

## Spreadsheets and Data Processing

* Edit `.xlsx` files using a powerful built-in spreadsheet editor, powered by [MESCIUS SpreadJS](https://developer.mescius.com/spreadjs/docs/overview). 
* Supports all common Excel functionality, including formulas, graph and table generation, images, pivot tables, etc.
* Does **not require Excel** to be installed on your machine.

<img width="400" heigth="400" src="https://github.com/AnTulcha/Celbridge/blob/main/docs/images/excel_spreadsheet.png?raw=true">

## File Explorer

* Create, move, drag, drop, copy, and paste files, with full undo/redo support.
* Built-in support for viewing and printing PDFs, images, audio and other media. 

## Text Editor 

* Fully featured text editor based on [Monaco](https://microsoft.github.io/monaco-editor/), the editor used in [Visual Studio Code](https://code.visualstudio.com/). 
* Supports all popular text formats and programming languages.
* Features:
  * Syntax highlighting
  * Search and replace
  * Formatting
  * Copy and paste, full undo/redo
  * Multi-cursor editing
  * Command palette 

## Markdown Documentation

* Markdown documentation editor with syntax highlighting and split-screen preview.
* A preview window that updates as you type, rendered using [Markdig](https://github.com/xoofx/markdig).
* Display images and other media by linking to local project files or using a URL.

## Web App Integration

* Create a `.webapp` file to allow you to open any web page as a tabbed document. 
* Provides quick access to the web applications you regularly use (e.g. dashboards, support portals, docs, etc).
* Quickly navigate back to web pages using bookmark-like functionality. No more searching through open tabs!
* Files are download directly to your project folder.

## Planned Features

* Privacy-first AI: bring your own model (BYOM), run LLMs locally, **keep control of your data**.
* A Python-based extension system to allow the community to add custom functionality.

# Getting Started

Celbridge is distributed as a `.msix` installer for Windows.

> [!NOTE]
> Celbridge currently runs on Windows only. A macOS version is planned, and Linux/WASM versions may be possible depending on demand.

1. Navigate to the [Releases](https://github.com/AnTulcha/Celbridge/releases/) page.
2. Install the latest `.msix` version of Celbridge from the `Assets` section of the Releases page. 
3. Install the .NET Desktop Runtime if prompted.  
4. Run the Celbridge `.msix` installer. 
5. When prompted, click `Install` (if this is your first time installing the program) or `Update`. 
6. Launch Celbridge from the Start menu if it doesn't launch automatically. 

Each release includes an example project that demonstrates the core features of Celbridge. Access this example project as follows: 
1. Launch Celbridge. 
2. From the Home menu, click "New example project".

> [!NOTE] 
> If an existing project opens instead of the Home menu, click the Home icon in the toolbar on the left side of the program. 

3. Name the project and select a location for it. 
4. Celbridge will generate a new example project. Each folder in this project contains an example of a core feature of Celbridge. For example, the `01_markdown` folder demonstrates how to work with Markdown files. 
5. To get started with each feature, see the `readme.md` files contained in the example folders. 

# Building From Source

1. Clone the Celbridge git repo on your machine.
2. Install the latest version of Visual Studio (the free [Community Edition](https://visualstudio.microsoft.com/vs/community/) works fine).
3. Follow the Visual Studio setup instructions for [Uno Platform](https://platform.uno/docs/articles/get-started-vs-2022.html?tabs=ubuntu1804) development.
4. Open `Celbridge.sln` in Visual Studio.
5. In `Solution Explorer`, right click on the `Celbridge` project and select `Set as Startup Project`.
6. Select the `Celbridge (WinAppSDK Packaged)` and `net8.0-windows10.0.22621` targets in the Visual Studio configuration toolbar.
7. Build and run the application.

If you encounter build errors, try restarting Visual Studio. If this fails, do a clean build.

# Support

If you encounter any issues with Celbridge, please open a [ticket](https://github.com/celbridge-org/celbridge/issues). You're also welcome to look for help and support on our [community forums](https://celbridge.discourse.group/). 

# Roadmap

Our goal is to turn Celbridge into a robust data processing tool suitable for use in production environments, backed by a friendly and helpful user community. 

# Who are we?

We're a small team led by [Chris Gregan](https://github.com/chrisgregan). Chris has worked in games development for over 20 years — most recently he was Lead Tools Programmer at [Romero Games](https://romerogames.com/). He has a lot of experience with many programming languages and technologies, his favourites being Python, C#, and .NET.  

Chris is currently doing a Research Masters with [Technological University Dublin](https://www.tudublin.ie/). Chris also created the [Fungus](https://github.com/snozbot/fungus) visual scripting tool for the Unity game engine. That project is now maintained by [the community](https://github.com/Atelier-Mycelia/Amanita).

# Contributions

All feedback and contributions are very welcome! If you'd like to contribute a new feature, please open an issue first so we can discuss the best way to implement it. We have a strong vision for the architecture of Celbridge that we hope will allow the project to scale successfully. 

# Credits

Thank you to everyone who has contributed to Celbridge, especially [Katie Canning](https://katiewrites.games/), [Matt Smith](https://github.com/dr-matt-smith), and [Matt Johnson](https://github.com/amazinggitboy).

This project was made possible by the Sabbatical Policy at [Romero Games](https://romerogames.com/). Huge thanks to Brenda Romero🏅 & John Romero and all of the incredible team at Romero Games for their support. ❤️❤️❤️

Many thanks to [MESCIUS SpreadJS](https://developer.mescius.com/spreadjs) for sponsoring Celbridge and supporting open source developers!

Celbridge relies on code from many fantastic open source projects, including:
* [Uno Platform](https://platform.uno) 
* [Monaco Editor](https://microsoft.github.io/monaco-editor)
* [WebView2](https://github.com/MicrosoftEdge/WebView2Browser)
* [file-icons](https://github.com/file-icons/vscode/blob/master/LICENSE.md)
* [github-markdown-css](https://github.com/sindresorhus/github-markdown-css/blob/main/license)

Celbridge also uses a range of open source NuGet & Python packages, details of which can be found in `THIRD-PARTY-LICENSES.txt`. 