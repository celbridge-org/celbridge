# Windows packaged build

These are the steps required to build a packaged build of Celbridge for Windows.
The output is a .msix installer that can be installed on a Windows 10 or 11 PC.

## Set the active configuration

Firstly, set:
* Celbridge.Windows as the startup project
* the build configuration to Release > x64 > Celbridge.Windows (Package)
Note: This process only works with the (Package) configuration.

## Package and Publish

* In Solution Explorer, right click on Celbridge.Windows 
* Select "Package and Publish > Create App Packages"

There appears to be a bug in Visual Studio that prevent this option from being displayed, which may 
be related to this issue: https://github.com/microsoft/WindowsAppSDK/issues/1664

As a workaround, select any other project in Solution Explorer and then select Celbridge.Windows again.
The "Package and Publish" option should now be displayed. I had lots of fun figuring that one out :(

Just to add to the confusion, there are also context menu options for "Pack" and "Publish" which you *shouldn't* use.
* Pack: Used to build Nuget packages for distribution
* Publish: Uses the .NET 5 publish pipepline. This does not produce a MSIX installer for Windows.

## Code signing the application

The packaged application must be codesigned with a valid certificate in order to install and run it on another machine.
There are 3 main ways you can do this, depending on how you want to distribute the application.

Sideloading here refers to installing the application without going via the Windows Store.

### Sideloading: Sign with a personal certificate

* Make a new code signing certificate and set it as the signing certificate in the "Package and Publish" settings.
** The easiest way is via the "Create certificate" option in the "Package and Publish" workflow.
* Generate the .msix installer. 
** A .cer file will be generated with the same name as the .msix
** This certificate must be installed on any machine where you want to deploy the application.

* Right click on the .cer file and select "Install"
** In the Certificate Import Wizard, set "Store Location" to "Local Machine" and click Next.
** Select the "Place all certificates in the following store" option and click "Browse".
** Select the "Trusted People" certificate store and click Ok.
** Click Next and then Finish to import the certificate.

You should now be able to install the .msix and run the application.
It's important to follow the certificate install steps above exactly, placing it in other locations doesn't work.

### Sideloading: Sign with a commercial code signing certificate

You buy a code signing certificate from a company like Verisign. They perform some checks to validate your identity and you
have to pay several hundred euros a year to renew the certificate.

This is annoying but it's how Microsoft check that the applications people are installing are safe so I guess it's fair enough.

### Windows Store: Sign with the certificate

You don't need to pay for a certificate if you're distributing your application via the Windows Store, you just use Microsoft's 
certificate.



