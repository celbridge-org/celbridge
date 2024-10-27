# Automatic Updates

Ideally, the Celbridge application should update automatically.

This requires hosting builds on a server and providing some infrastructure to handle the update checks and downloads.
https://learn.microsoft.com/en-us/windows/msix/non-store-developer-updates

Another option is something like [Squirrel](https://github.com/Squirrel/Squirrel.Windows/tree/develop) which uses the older
.msi installer format. This is still a popular approach (I think due to limitations with the MSIX format), although Microsoft 
recommend using the newer format.