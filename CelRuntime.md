# Cel Runtime

<!-- Q: What is the Cel Runtime -->

- Assembly shared between the `Celbridge Editor` and generated `Cel Application`
- Provides the built-in functionality that generated `Cel Script` code calls (e.g. `Print`)
- Organized into multiple functionality providers

<!-- Q: What is the relationship between the Celbridge Editor and the Cel Runtime? -->

The `Cel Runtime` is designed to be isolated from the `Celbridge Editor` in future

- Currently, `Celbridge Editor` injects dependencies such as a `Print` callback function to display text in the `Console Panel`.
- `Cel Applications` should be able to run independently of the editor, so this system will need to be replaced
- For example, the `Print` instrcuction should use `IPC` to send the print text back to the editor, if connected.
