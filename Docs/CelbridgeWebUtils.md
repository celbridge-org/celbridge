# Monaco Editor

This notes covers some of the rabbit holes I had to go down to get the Monaco code editor to work in Celbridge. I 
initially got this to work via an embedded Blazor Webassembly App using Blazor Monaco, then I simplified it to 
just use the Monaca javascript library directly.

# Blazor Monaco

`Monaco` is the same web-based code editor used in VS Code. It's integrated into Celbridge via a Blazor Webassembly 
web app.

`Blazor Monaco` is a nuget package that allows you to add a Monaco text editor as a Blazor Component in Blazor 
application. https://github.com/serdarciplak/BlazorMonaco

Blazor Monaco currently only supports .NET 7, which means it can't be used with the neat new .NET 8 web
assembly modes (yet).

Using a WebView2 with a full WebAssembly and Monaco Editor instance per tab is (probably) very resource intensive.

# Monaco Editor in a Web View

Once I had everything working via Blazor Webassembly, I came across this post which describes how to integrate Monaco 
using just the Javascript library and an index.html.
https://stackoverflow.com/questions/71174736/how-to-use-the-monaco-editor-inside-a-windows-forms-application

I've learned that Blazor is great if you want to implement an entire Single Page Application SPA, but for integrating 
individual web pages it's a lot easier to just work with HTML and javascript directly.

# Monaco Localization warnings

Building the Windows project head generated these warnings about some of the files in the Monaco folder. I think 
MsBuild thinks they're related to localization and is performing some sort of validation on them incorrectly. 
My workaround was to set the following file's Build Action to "None".

```
>WINAPPSDKGENERATEPROJECTPRIFILE : warning : PRI249: 0xdef00520 - Invalid qualifier: ZH-CN
>WINAPPSDKGENERATEPROJECTPRIFILE : warning : PRI249: 0xdef00520 - Invalid qualifier: ZH-TW
>WINAPPSDKGENERATEPROJECTPRIFILE : warning : PRI249: 0xdef00520 - Invalid qualifier: ZH-CN
>WINAPPSDKGENERATEPROJECTPRIFILE : warning : PRI249: 0xdef00520 - Invalid qualifier: ZH-TW
>WINAPPSDKGENERATEPROJECTPRIFILE : warning : PRI263: 0xdef01051 - No default or neutral resource given for 'Files/monaco/min/vs/basic-languages/st.js'. The application may throw an exception for certain user configurations when retrieving the resources.
```

# Uno Platform Javascript Interop

This is the best resource for figuring out how to call JavaScript functions on a page via WebView2, and then send 
messages back to the Uno Platform app.
https://platform.uno/docs/articles/controls/WebView.html

You can use `await webView.ExecuteScriptAsync()` to execute some Javascript. You can call a sync or async Javascript
function, but you can only return a value from a sync Javascript function. Async functions will always return "{}".
This is a well known WebView2 issue that wasn't mentioned in the Uno docs so I wasted a few hours figuring it out.
https://github.com/MicrosoftEdge/WebView2Feedback/issues/247

The way around it is to use the `postWebViewMessage()` solution shown in the Uno Platform doc above. This only works
when the page is hosted in a WebView2 control. Calling `postWebViewMessage()` on a page loaded in a browser has no 
effect. Again, not documented, so spent hours trying to figure out why it wasn't working.

There is a hacky way to call an async Javascript function and await the result via the Dev Tools API. This is an
ugly hack, so I'm going with the Web View Message approach instead.

# Embedding Webassembly App in Windows Head

The webassembly files are quite deeply nested, which can exceed the maximum path length on Windows.
If this happens you get this completely unhelpful error message:
> "The project needs to be deployed before we can debug. Please enable Deploy in the Configuration Manager."

This can be fixed by reducing the folder depth of the Celbridge project. It's also possible to change a registry 
setting to enable long path support. Windows suxxxx :(

# Optimizing Webassembly Warning

This warning appeared when I was publishing the Blazor Webassembly project.

```
"Publishing without optimizations. Although it's optional for Blazor, we strongly recommend using `wasm-tools` 
workload! You can install it by running `dotnet workload install wasm-tools` from the command line."
```

To fix this, you need to install the `wasm-tools` workload, _for the version of .NET SDK you are using in Visual 
Studio_. Turns out there's two ways to install .NET SDK, one for building applications with Visual Studio and one for 
building outside of Visual Studio. Sigh.

So first, step is to install the ".NET SDK for Visual Studio" for the version of .NET you're using in your project 
(.NET 7 for me at the minute). https://dotnet.microsoft.com/en-us/download/visual-studio-sdks

Next, you have to install the workload via the command suggested in the log warning. But of course that doesn't work, 
because you first have to tell the dotnet command which version of .NET to install the workload into. 

You do this by creating a `global.json` file in the working directory where you are running the command. This file 
specifies the version of .NET that the command will apply to. The version number here has to _exactly_ match the 
version of the .NET SDK you just installed, for example:

```
{
  "sdk": {
  "version": "7.0.405",
  "rollForward": "latestFeature"
}
```

Some useful commands:

- `dotnet workload install wasm-tools`
- `dotnet --list-sdks`
- `dotnet workload list`

# Navigating to a page in the embedded web app

I used `WebView2.CoreWebView2.SetVirtualHostNameToFolderMapping()` to allow the WebView to access the embedded 
Blazor Assembly application. This maps the "domain" CelbridgeBlazor to the `wwwroot` folder in the application.

This is how I ended up getting the navigation to the `editor` page to work:
HTMLView.CoreWebView2.Navigate("http://CelbridgeBlazor/index.html?redirect=editor");

The problem is, navigating to "http://CelbridgeBlazor" fails to find the Blazor application. Presumably this is due to 
differences between a real web server and SetVirtualHostNameToFolderMapping().

The workaround was to load index.html, which then goes to the `NotFound` route in the Blazor Application. 
In `App.razor`, I check for a redirect property in the query string and then use the `NavigationManager` to navigate 
to the indicated page. 

# Flashing WebView when switching tabs

There's an annoying flash when switching between tabs that contain a webview.
https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412

I implemented the `DefaultBackgroundColor` property suggested in the thread. There is still a slight flicker for one 
frame when switching between tab items, but it's much less noticeable now.
