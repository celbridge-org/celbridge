# DispatcherSetup

It sometimes happens that you need to run code on a main thread when running in a different thread, e.g. in an event handler running on a different thread.

Uno Platform provides the Uno.Extensions.IDispatcher interface to help with this, but unfortunately didn't provide any documentation on how to use it.

When configuring the DI environment, simply create a new Uno.Extensions.Dispatcher instance passing the Main Window into the constructor, like so:

```
services.AddSingleton<IDispatcher>(new Dispatcher(MainWindow!));
```

The Dispatcher class manages the platform specific dispatching mechanism for you. To use it, just get an `IDispatcher` reference from DI, and call `ExecuteAsync` to run your code.