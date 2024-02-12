# Dependency Injection

We're using Microsoft.Extensions.DependencyInjection to manage dependency injection in Celbridge.
https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection

This greatly helps with separating concerns and maintaining a clean, scalable and testable architecture. It also works very well with the MVVM pattern we use for UI.

Some usage guidelines:
- Favour using constructor injection to acquire dependencies when possible.
- The Service Locator may be used to acquire a dependency in cases where constructor injection isn't feasible.
- Use interfaces / abstract base classes whenever possible rather than depending on concrete types.


