# What is Celbridge?

Celbridge is a user-friendly data processing tool. It provides a bridge between Python and your project, making complex tasks with data easy. 

**The goals of Celbridge are:**

* Make <em>common tasks with data</em> easier and more accessible.
* Act as <em>a bridge</em> between systems (e.g. applications, programming languages like Python, libraries, environments).
* Support a wide range of functionality via <em>extensions</em>, with a simple core application. 

<br>
<a href="https://github.com/AnTulcha/Celbridge/blob/main/Docs/Images/celbridge.gif" alt="Celbridge screenshot GIF">
  <img width="400" heigth="400" src="https://github.com/AnTulcha/Celbridge/blob/main/Docs/Images/celbridge.gif?raw=true">
</a>
<br>

Celbridge is still in **early development**. Any update may introduce incompatibilities with previous versions, so always back up any data created with Celbridge.

# Key Features

## Python Integration

* An integrated Python kernel running Python 3.13.
* Easy package installation process: no need for virtual environments. 
* An integrated read–eval–print loop (REPL) shell, featuring
    * Syntax highlighting
    * Command history
    * Command completion
    * [pdb](https://docs.python.org/3/library/pdb.html#module-pdb) command line debugger
    * [IPython](https://ipython.readthedocs.io/en/stable/index.html) magic commands (%run, %alias, %ls, etc. )
* Run Python scripts with a single click. 

## Spreadsheets and Data Processing

* View and edit `xlsx` files using the built-in spreadsheet editor, powered by [SpreadJS](https://developer.mescius.com/spreadjs/docs/overview). 
* Supports most common Excel functionality, including formulas, graph and table generation, images, etc.
* Does not require Excel to be installed on your machine.

## Security

* All files in Celbridge are managed locally on your machine, not in the cloud.

## Editor Functionality
* Create, move, drag, drop, copy, and paste files. 
* A code editor based on [Monaco](https://microsoft.github.io/monaco-editor/), the text editor used in VS Code. 
* Supports all popular programming languages, with features such a syntax highlighting and search and replace. 
* A Markdown document editor with a split-screen preview window.
* Support for images and other media in Markdown preview windows. 

## Web Integration

* `.web` files allow you to open any web page as a tabbed document. 
* Quickly tab back to web pages using bookmark-like functionality. 

**Planned features include:**

* A simple visual scripting language that transpiles to Python script.
* An extension system and package manager to allow the user community to add custom functionality.

# Getting Started

Celbridge is distributed as a `.msix` installer for Windows.

1. Install the latest `.msix` version of Celbridge from the [releases](https://github.com/AnTulcha/Celbridge/releases) section of the Github page. 
2. Install the .NET Desktop Runtime if prompted.  
3. Run the Celbrifge `.msix` installer. 

> [!NOTE]
> Celbridge currently runs on Windows only. A macOS version is planned, and a Linux version may be possible in future.

# Installing From Source

1. Clone the Celbridge git repo on your machine.
2. Install the latest version of Visual Studio (the free [Community Edition](https://visualstudio.microsoft.com/vs/community/) works fine).
3. Follow the Visual Studio setup instructions for [Uno Platform](https://platform.uno/docs/articles/get-started-vs-2022.html?tabs=ubuntu1804) development.
4. Open `Celbridge.sln` in Visual Studio.

Make sure you have `Celbridge (WinAppSDK Packaged)` and `net8.0-windows10.0.22621` selected in the Visual Studio configuration toolbar, then build and run the aplication.

> [!NOTE]
> Uno Platform performs some initial setup work behind the scenes when you first open the solution. Allow Visual Studio to idle for a minute or so after the solution opens, then close and reopen Visual Studio. You may need to repeat this process a couple times before the project will compile and run successfully.

# Roadmap

Our goal is to turn Celbridge into a robust tool suitable for use in production environments, backed by a friendly and helpful user community. We're hoping to get version 1.0 out by the end of 2025. 

# Who are we?

We're a small team led by [Chris Gregan](https://github.com/chrisgregan). Chris has worked in games development for over 20 years — most recently he was Lead Tools Programmer at [Romero Games](https://romerogames.com/). He has a lot of experience with many programming languages and technologies, his favourites being C#, .NET and Python.  

Chris is currently doing a Research Masters with [Technical University Dublin](https://www.tudublin.ie/), where the idea for Celbridge was born.

Chris also created the [Fungus](https://github.com/snozbot/fungus) visual scripting tool for the Unity game engine. That project is now maintained by the community.

# Contributions

All feedback and contributions are very welcome! If you'd like to contribute a new feature, please open an issue first so we can discuss the best way to implement it. We have a strong vision for the architecture of Celbridge that we hope will allow the project to scale successfully. 

# Credits

This project was made possible by the Sabbatical Policy at [Romero Games](https://romerogames.com/). Huge thanks to Brenda, John and all of the incredible team at Romero Games for their support.❤️❤️❤️

Many thanks to [SpreadJS](https://developer.mescius.com/spreadjs) for sponsoring Celbridge and supporting open source developers!

Celbridge relies on code from these fantastic open source projects:
* [Uno Platform](https://platform.uno) 
* [Monaco Editor](https://microsoft.github.io/monaco-editor)
* [WebView2](https://github.com/MicrosoftEdge/WebView2Browser)
* [file-icons](https://github.com/file-icons/vscode/blob/master/LICENSE.md)
* [github-markdown-css](https://github.com/sindresorhus/github-markdown-css/blob/main/license)

Celbridge also uses a range of open source NuGet packages, details of which can be found in the solution files.
