> [!NOTE]
> This document assumes you've already successfully installed Celbridge. For detailed installation instructions, see the [Installation section](https://github.com/celbridge-org/celbridge?tab=readme-ov-file#installation) of the project's `README`. 

# The Basics

Celbridge has two main interfaces: the **home menu**, where you create and open projects, and the **Celbridge editor**, where you work directly in a project. 

If you have no existing project, the home menu will open automatically when Celbridge is launched. You can switch between the home menu and Celbridge editor using the buttons in the status bar to the left of the main interface.  

![](https://github.com/AnTulcha/Celbridge/blob/main/docs/images/home_explorer_buttons_switch.gif)*Switching between the home menu and Celbridge editor*

# Creating a Project

You can **create and open Celbridge projects** from either the **home menu**, or the **hamburger menu** at the top of the status bar to the left of the main interface. 

Let's create a project from the home menu.

1. Open the home menu.  
2. Click `New project`. 
3. Name your project.

> [!NOTE]
> Currently, project names cannot contain spaces. 

4. Choose a folder to place your project in, and select whether you want Celbridge to create a subfolder with the same name as the project. 

The steps to create a project from the hamburger menu are identical: just click the menu and select `New project`.

# The Celbridge Editor

Let's explore the Celbridge editor. Open the project you created in the `Creating a Project` step. From left to right, top to bottom, the sections of the Celbridge editor are as follows:

## Status Bar

* The **status bar** lets you easily switch between **different menus and project contexts**.
* The **hamburger menu** at the top of the status bar lets you quickly create, open, or reload a project.
* The **home button** navigates to the home menu.
* The **explorer button** navigates to the Celbridge editor. 
* The **globe button** opens the Celbridge community forums as a web application within Celbridge.
* The **settings button** lets you change application settings.   

## Explorer Panel

* The **explorer panel** lists all files in the project. **Add existing files by dragging and dropping** them from the Windows File Explorer. 
* Run Python files using the `Run` option. Right-click on a Python file in the Explorer panel and select `Run`. The Console panel prints any output from the script. 
* Open a file by right-clicking it and selecting `Open`. 
* Perform various options on files using the `Edit` option. 
* **Add a new file or folder** by right-clicking anywhere in the Explorer panel and selecting an option from the `Add` dropdown. The supported options are a folder, Python script, Excel file, Markdown file, web application file, or plain text file of any format. 
* Open a file in the Windows File Explorer or the associated system application using the `Open in` option. 

## Documents Panel

* View and edit open documents in the documents panel. **Double-click on a file** in the explorer panel to **open it** in the documents panel.  
* You can tab between multiple open documents.  
* Celbridge includes a fully-featured text editor based on [Monaco](https://microsoft.github.io/monaco-editor/), the editor used in [Visual Studio Code](https://code.visualstudio.com/). 
* The text editor supports all popular text formats and programming languages. 
* The documents panel also provides a live preview for Markdown. 

## Inspector Panel

* The inspector panel allows you to perform document-specific actions. For example, you can run a Python script that edits the data of a spreadsheet from the spreadsheet inspector panel. 

## Console

* The console is Celbridge's integrated Python interpreter. Here you can run Python commands and scripts. 

# Installing Python Versions and Packages

You can easily add new Python versions and packages from the Celbridge project settings file. The project settings file is named `yourprojectname.celbridge`, and is visible in the explorer panel. 

Let's add the data analysis library [Pandas](https://pandas.pydata.org/) to our Celbridge project. 

1. Open the `.celbridge` project settings file by double-clicking on it in the explorer panel.
2. Under the `packages` key in the `[python]` section, type `"pandas"`:

![](https://github.com/AnTulcha/Celbridge/blob/main/docs/images/project_settings.png)

3. Reload the project by navigating to the hambuger menu in the top left and clicking `Reload project`. 
4. The console window will show Pandas being installed. 

# The Example Project

Celbridge includes an example project that demonstrates the core features of Celbridge. Let's take a look at it. 

1. From the home menu, click `New example project`.
2. Name the project and select a location for it.
3. Celbridge will generate a new example project. Each folder in the explorer panel of this project contains an example of a core feature of Celbridge. For example, the `01_markdown` folder demonstrates how to work with Markdown files.
4. To get started with each feature, take a look at the `readme.md` files contained in the example folders.

# Help and Support 

The community forums are a great place to ask for help. You can access them at any time from the **globe button** in the status bar, or [here](https://celbridge.discourse.group/).

To report bugs, please open a [ticket](https://github.com/celbridge-org/celbridge/issues). 