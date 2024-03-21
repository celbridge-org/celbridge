# Deploying to WSL

It's possible to build the project on Windows using the Skia + Gtk project head and deploy directly to Linux via WSL.

Follow the setup instructions here: https://platform.uno/docs/articles/get-started-with-linux.html?tabs=ubuntu1804

I got it working by installing Ubuntu via the Microsoft Store and then installing the prerequisites listed on that page.

To build and test it, select Celbridge.Skia.Gtk as the startup project, select WSL in the play button menu and hit play.

If Visual Studio detects that .NET 8 is not installed in .NET 8, you can choose to install it automatically.

# Resetting the WSL user password

I forgot my WSL user password (twice now), so this note is just to mention that it's easy to change your user password quite easily on WSL.
https://itsfoss.com/reset-linux-password-wsl/

This can easily happen if you haven't run WSL in a while.