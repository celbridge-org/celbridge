# Building From Source Code

## Build Instructions

1. Install the latest version of Visual Studio (the free [Community Edition]() works fine).
2. Follow the Visual Studio setup instructions for [Uno Platform](https://platform.uno/docs/articles/get-started-vs-2022.html?tabs=ubuntu1804) development.
3. Open `Celbridge.sln` in Visual Studio

Make sure you have `Celbridge (WinAppSDK Packaged)` and `net8.0-windows10.0.22621` selected in the configuration toolbar.

## Building Gotchas

### Initial Setup

Uno Platform does some initial setup work behind the scenes when you first open the solution. It's a bit quirky at the start. Just let Visual Studio idle for a minute after the solution opens, then close and reopen the application. You may need to repeat this a couple of time before the project will compile and run successfully.




