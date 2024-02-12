# Error Handling

Error handling is a critical feature of any software application. The .NET Framework Guidelines encourage the 
use of exceptions for pretty much all error handling. I find this very odd, as they advocate throwing 
exceptions in cases which are entirely predictable, e.g. trying to read from a non-existent file. 

I feel that exceptions should be reserved for situations where there is no clear way to recover from the
state that the program has got into. In most cases it's better to let the caller know about the error that 
has occurred via a Result object and let them decide how to handle it. 

The readme for Fluent Results goes into a lot of detail about the pitfalls of exception handling and why
returning Result objects is often a better approach in many cases.
https://github.com/altmann/FluentResults?tab=readme-ov-file#why-results-instead-of-exceptions

There is a performance overhead to returning Result objects, which in most cases is negligible. Where it does
impact performance, just return bool or use the TryGet pattern instead.

# Railroad Oriented Development

The functional programming concept of Railroad Oriented Development is a more modern, more declarative way to 
handle error conditions. In languages like F#, it is common to use monads to "hide" the error handling code, essentially wrapping the called function inside another function that handles any errors.

This is good because it makes the "happy path" clean, with no error handling code cluttering up the place. However
you really need to understand functional programming and monads to understand how the wrapper functions are working. 
It's clever, elegant and quite difficult to get your head around initially.

Our Result type doesn't attempt the monad approach, but it does abstract out the concept of success/failure which
is where I think most of the value lies.

# Fluent Results

The Celbridge result type is modelled after the Fluent Results library. 
https://github.com/altmann/FluentResults

The Celbridge Base Library is not allowed to have package dependencies, so I wrote my own implementation. 
Fluent Results also provides a lot of extra functionality which we didn't require. 




