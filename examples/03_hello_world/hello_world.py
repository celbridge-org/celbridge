import sys

def main():

    # Check if script was called with a name argument
    if len(sys.argv) > 1:

        # Say hello to the named person
        name = sys.argv[1]
        print(f"Hello {name}!")
    else:

        # Default to "Hello world!"
        print("Hello world!")


if __name__ == "__main__":
    main()
