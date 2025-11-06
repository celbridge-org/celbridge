# Python Examples

These script examples demonstrate some of the Python functionality available in Celbridge.

Make sure the **Console panel** is expanded so that you can see the Python input and output text. The console panel provides a full [IPython shell](https://ipython.readthedocs.io/en/stable/overview.html#main-features-of-the-interactive-shell), with support for syntax highlighting, completion, input history, copy and paste and IPython magic commands. 

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

This runs the Python script with no arguments, displaying the default "Hello world!" text in the console.

# Run via IPython Magic command

In the **Console panel**, enter this command:

```python
%run "03_python/hello_world.py"
```

As before, this displays the default output: "Hello world!".

Now enter this command:

```python
%run "03_python/hello_world.py" "Earth"
```

The "Earth" string is passed as a parameter to the `hello_world.py` script, which then outputs "Hello Earth!".

You can see the list of support IPython magic commands by entering this command.

```
%lsmagic
```

The [IPython Book](https://ipythonbook.com/magic-commands.html) by Eric Hamiter has an excellent description of the available IPython commands.

The `%` character is optional, so you can enter `run` instead of `%run`for example, just be careful not to create variables with the same name as an IPyton magic command.

# Run via Shortcut Button

Celbridge supports custom script shortcut buttons in the left-hand **Navigation Bar**. To see this in action:

1. Click on the **Play** button in the **Navigation Bar**. The available shortcut options are displayed in a flyout menu.
2. Select **03 Hello World** to run the **hello_world.py** script. The text "Hello Universe!" is output to the console.

Shortcut buttons can be easily added via the **.celbridge** project file.

1. Open the **examples.celbridge** project file.
2. Locate this section in the config:

```
[navigation_bar.run_examples.03_python]
tooltip="Run example 03_hello_world"
script='''
run "03_hello_world/hello_world.py" "Universe"
'''
```

3. Change "Universe" to "Galaxy"
4. Select **Main Menu > Reload Project** to reload the project configuration.
5. Select **Play > 03 Hello World** again. The text "Hello Galaxy!" is output in the console.
