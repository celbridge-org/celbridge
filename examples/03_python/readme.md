# Python Examples

These examples demonstrate some of the Python functionality available in Celbridge.

Make sure the **Console panel** is expanded so that you can see the Python input and output text.

# Hello World

Let's start with some traditional Hello World examples.

## Python Script in Console

In the **Console panel**, enter this command at the `>>>` prompt.

```python
print("Hello world!")
```

The text "Hello world!" is displayed on the following line.

## Python Script File

Open the **hello_world.py** file in the same folder as this readme. This Python script prints "Hello <name>!" using the supplied name argument, or "Hello world!" if no name is provided.

### Run via Context Menu

In the **Explorer panel**, right click on **hello_world.py** and select **Run**.

This runs the Python script with no arguments, displaying the default "Hello world!" text.

# Run via IPython Magic command

In the **Console panel**, enter this command:

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

The [IPython Book](https://ipythonbook.com/magic-commands.html) by Eric Hamiter has an excellent description of the available IPython commands.
 