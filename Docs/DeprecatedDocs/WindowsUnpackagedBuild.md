# Windows Unpackaged Build

The solution supports an unpackaged Windows build configuration. This works, but there are a number of issues compared to the packaged configuration.

# Hot Reload

The Hot Reload feature in Visual Studio only works for _unpackaged_ builds. This is mentioned in the Uno Platform docs, but you have to click on the `Supported Features > WinAppSDK` to find this information. 
https://platform.uno/docs/articles/features/working-with-xaml-hot-reload.html?tabs=vswin%2Cwinappsdk

If you try to use `Hot Reload` in a packaged build there's no warning or prompt, the `Hot Reload` just doesn't do anything.

Hot Reload only seems to work partially in some areas of the application, e.g. the `ConsolePanel`.
At first, changes made directly to the console panel are not reflected in the running application. If you then make a change to the `WorkspacePage` though, the previous changes made to the `ConsolePanel` appear. Any further changes made to `ConsolePanel` are reflected immediately. This provides a workaround that at least allows us to use Hot Reload with a bit of extra fiddling - super weird though.

> This issue could be related to the indirect way that we populate the Workspace panels to support extensions. 

# Startup Exceptions

When the application starts up, a few exceptions are thrown from `WinRT.Runtime.dll` because the application attempts to access functionality which presumably isn't supported in unpackaged builds. These exceptions appear to be benign and the application
starts up ok despite them.

To catch this exceptions in the debugger, go to `Debug > Windows > Exception Settings` and enable all CLR exceptions. When the debugger catches one of these exceptions, Visual Studio allows you to see the callstack and step through the decompiled source code which is actually very helpful.

# ApplicationData

The biggest drawback I've encountered with unpackaged builds is that `ApplicationData` is not supported. It used to be supported in earlier versions of `WinAppSDK` / `Uno Platform`, but it doesn't work now. This means that you need to implement your own solution for persisting stuff like user settings when working with unpackaged builds.