# Storing Secrets

This [article](https://auth0.com/blog/secret-management-in-dotnet-applications/) provides a good overview of the 
main options for storing secret keys in .NET Applications.

For Celbridge, the main use case at the moment is allowing the user to provide their own secret keys for services 
such as OpenAI. The user can enter these keys via the Settings dialog and they are persisted in the local settings
for the application.


Uno Platform does support the [Windows.Security.Credentials.PasswordVault API](https://platform.uno/docs/articles/features/PasswordVault.html?tabs=android) 
but only on a limited number of platforms. We could use it in future if they extend support to Mac & Linux.