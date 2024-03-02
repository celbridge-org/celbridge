# Extending the title bar

On Windows, we customize the title bar area to give the application a more modern look.

It is possible to get interactive controls working in the title bar area, but it's not trivial. Here's what I've figured out so far.

This documentation explains the process of customizing the toolbar for a WinUI application.
https://learn.microsoft.com/en-us/windows/apps/develop/title-bar

I wasn't able to get some of the functionality described in that page working, possibly due to how Uno Platform applications are setup. Might be worth getting it working in a pure WinUI project first, and then reimplement it in Uno.

# Custom Window

The general approach seems to be to use a subclassed instance of the Window class instead of the default Window class.
Window has a member variable called AppWindow, which you can use to customize the behaviour of the title bar area.

On Windows, you can do this by subclassing the Window class.
```
#if WINDOWS
    MainWindow = new CelbridgeWindow();
#else
    // On non-Windows platforms, this call just returns Window.Current        
    // https://github.com/unoplatform/uno.extensions/blob/f37bf8537e89473039ef0ee93592828f73d29553/src/Uno.Extensions.Hosting.UI/ApplicationBuilder.cs
    MainWindow = builder.Window;
#endif
```

## ExtendsContentIntoTitleBar

This gotcha is called out in the documentation, but it still tripped me up. Note that Window also has a ExtendsContentIntoTitleBar property, but it's not connected to the property on AppWindow.TitleBar. This is for the older method of extending content into the title bar area. 

```
// ExtendsContentIntoTitleBar has to be set to true before settings the PreferredHeightOption
AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
```

Once you do this however, you are no longer using the standard title bar, so you lose things like the window control styling,
hover effects, etc. There's gotta be some way to configure these to work again. It's also possible that the AppWindow functionality is only available on Windows 11, so we might need a fallback on Windows 10.
