# Monaco Text Editor

Monaco is the same web-based code editor used in VS Code. It's integrated into Celbridge via a Blazor Webassembly web page.
This notes covers some of the rabbit holes I went down to get it to work.

## Blazor Monaco

Blazor Monaco is a nuget package that allows you to add a Monaco editor as a Blazor Component.
https://github.com/serdarciplak/BlazorMonaco

Blazor Monaco currently only supports .NET 7, which means it can't be used with the neat new .NET 8 web
assembly modes (yet).

Using a WebView2 with a full WebAssembly and Monaco Editor instance per tab is (probably) very resource intensive.

## Uno Platform Javascript Interop

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

## Embedding Webassembly App in Windows Head

The webassembly files are quite deeply nested, which can exceed the maximum path length on Windows.
When this happens you get this completely unhelpful error message:
> "The project needs to be deployed before we can debug. Please enable Deploy in the Configuration Manager."

This can be fixed by reducing the folder depth of the Celbridge project. It's also possible to change a 
registry setting to enable long path support. Windows sux :(


