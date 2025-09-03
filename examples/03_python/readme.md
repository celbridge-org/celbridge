# Python Examples

These examples demonstrate some of the Python functionality available in `Celbridge`.

Make sure the `Console` window is expanded so that you can see the `Python` input and output text.

# Hello World

Let's start with some traditional Hello World examples.

## Python Script in Console

In the console window at the `>>>` prompt, type this command and press `Enter`.

```python
print("Hello world!")
```

The text "Hello world!" is displayed on the following line.

## Python Script in File

Double click the `hello_world.py` file that is in the same folder as this `readme.md` file. It contains a simple `Python` module that prints "Hello <name>!" using a supplied name argument, or "Hello world!" if no name is provided.

### Run via Context Menu

In the `Explorer Window`, right click on `hello_world.py` and select `Run`.

This runs the Python script with no arguments, displaying the default "Hello world!" text.

# Run via IPython Magic command

In the `Console Window`, enter this command:

```python
run "03_python/hello_world.py"
```

As before, this displays the default output: "Hello world!".

Now enter this command:

```python
run "03_python/hello_world.py" "Earth"
```

The "Earth" string is passed as a parameter to the `hello_world.py` script, which then outputs "Hello Earth!".

You can see the list of support IPython magic commands by entering this command.

```
%lsmagic
```

The [IPython Book](https://ipythonbook.com/magic-commands.html) by Eric Hamiter has an excellent description of the available commands.
 