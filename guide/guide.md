Here you will see everything you need to know to use this framework!

# The LOADER

Everything revolves around creating your own loader with the options, enums and methods you wish to use.

Once you have loaded the ump file, you will have access to the abstract class `UMPLoader`. To create your own loader, you will need to create a new class inheriting it, and overriding all the properties and methods as you wish, which will be shown below.

## Loader properties and methods to override

This is a list of every property and method and what they do. Abstract ones are mandatory, while virtual ones aren't and have a default value. Scroll down for examples if you aren't very familiar to C# and this looks a bit confusing!

* `abstract string CodePath`: The path to your folder containing all your files, relative to your MAIN `.csx` script, which is the one that you tell UTMT to run,

* `abstract bool UseGlobalScripts`: A boolean that depends on the GameMaker Studio version for your game. For versions smaller than 2.3, you will set this to `false`, and for versions greater than that, you will set this to `true`

* `virtual string[] Symbols`: (optional) An array containing all symbols you want to define for preprocessing.

* `virtual bool UseDecompileCache`: (default false) A boolean that should be true if caching the decompiled vanilla code for improved script execution is desired. WARNING: You may want to delete the cache if you ever change your version of UMT to make sure that your code will work with everyone else and not just the specific version you are using.

* `abstract string[] GetCodeNames (string filePath)`: This is a method you must override. This function will be used to tell the loader what code entries you will replace with each file. The argument is the (relative) path of the file, and it should return an array containing all code entries it will replace

## Constructor

You will need to give a constructor for your class. Because of how the scripting environment works, you will need to
have it contain a `UMPWrapper` argument, and pass it to the base constructor. When you instantiate the class, you need to pass the global variable UMP_WRAPPER. Look at the example below for more information

## Loading

Once you have created and instantiated your derived class, you can load all the code files using the `.Load` method, which takes no arguments.

## Basic example

Here's a very simple loader example you can copy if you aren't sure of how to start

```cs
class ExampleLoader : UMPLoader
{
    public override string CodePath => "mod/"; // loading all files inside a folder "mod"

    public override bool UseGlobalScripts => true; // For GameMaker Studio > 2.3

    public bool IsDebug { get; set; } // THIS IS NOT NCESSARY! It's just an example of how you can use the symbols

    // Here's an example of how you can set up your symbols. Symbols are optional, so it will depend on if you want to use them
    // If you don't want to use them, you can skip overriding it
    public override string[] Symbols => IsDebug ? new string[] { "DEBUG" } : new string[] { "PRODUCTION" };

    // A very simple implementation of the names method which returns a single code entry which should be
    // equal to the file name you gave
    public override string[] GetCodeNames (string filePath)
    {
        return new string[] { Path.GetFileNameWithoutExtension(filePath) };
    }

    // You must give a constructor and pass a UMPWrapper to the base constructor
    public ExampleLoader (UMPWrapper wrapper, bool isDebug) : base(wrapper)
    {
        IsDebug = isDebug;
    }
}

// When you instantiate, you must pass the global variable UMP_WRAPPER
ExampleLoader loader = new ExampleLoader(UMP_WRAPPER, true);

// Loading all the files
loader.Load();
```

# The files

All files need to end with either `.gml` or `.asm` to be recognized. `.gml` files represent gamemaker language files, and `.asm` represent files with the UTMT disassembly text. Both will work just fine with the rest in here, but beware of the following:

**WARNING**

If you are using `.asm` files, chances are you're doing that because the UTMT compiler can't properly compile some code. The way this framework works is that unless you are appending code to an entry, the entire entry will have to be recompiled, and as such, it may lead to errors that aren't even yours (this is an UTMT limitation). You can use `.asm` files to bypass this, but keep in mind that:

* If you have both `.asm` files and `.gml` files for the same code entry, if you are doing anything but appending (see importing and patching code below), then your code will probably conflict and only one of those will work. So make sure there is no such interception

# How to import code with UMP

If you just drop normal GML files in your mod folder, you will notice you will get an error. This is because you need to specify in the first line of your code what the UMP type is. There are three UMP types:

1. IMPORT -> This represents that you want the entire code inside your file to replace a code entry (or create a new one)
2. PATCH -> This represents a patch file, the syntax will be given below
3. FUNCTIONS -> This represents a file where you can define new functions, and they will be added to your project

To specify the type, you must use three slashes:

```/// IMPORT```

# How to use GetCodeNames: Automatic object creation

To make use of the automatic code creation for objects, you will need to make sure you are giving proper object names. In Undertale Mod Tool, you can linke code to objects if the code name has the following format:

* Prefixed with `gml_Object_`
* Followed by the object name
* An underscore followed by the event name
* An underscore followed by the event type

For example: 

`gml_Object_obj_time_Create_0`

`obj_time` is the name, `Create` is the event. `0` is the subtype ID. Create events have no subtypes, but for example for "Draw" events, subtype ID 64 equates to DrawGUI. You can look up the events and subtypes inside Undertale Mod Tool itself.

With this information, you can setup your own `GetCodeNames` function how you wish to. An example implementation:

```cs
public override string[] GetCodeNames (string filePath)
{
    string fileName = Path.GetFileNameWithoutExtension(filePath);

    // assume all code files starting with obj_ are for objects
    if (fileName.StartsWith("obj_"))
    {
        fileName = "gml_Object_" + fileName;
    }

    return new string[] { fileName };
}
```

An example file for this implementation: `obj_time_Create_0` will create the code entry `gml_Object_obj_time_Create_0`

# Creating functions with UMP: Function files

Function files are the ones that start with `/// FUNCTIONS`. In a function file, the following is valid:

1. You can define new functions using the `function` keyword. This is valid even for older GameMaker Studio versions.
2. The arguments of the functions need not be `argument0`, `argument1`...

**WARNING**: `GetCodeNames` does NOT get used in function files. It only applies to IMPORT and PATCHES files.

Here is an example of a "functions" file:

```gml
/// FUNCTIONS

function hello_world(message)
{
    return "Hello, World! " + message
}
```

# Patch files

The framework introduces a shorthand syntax for patching code files. First, you declare a `.gml` file as being a patch by including in the first line (capitalization is needed for all these):

```gml
/// PATCH
```

using three forward slashes.

Then, you can use a few different commands. It should be best explained through an example file:

```gml
/// PATCH

// in this code file, we will replace a line and set `global.debug` to be true

/// REPLACE
global.debug = false
/// CODE
global.debug = true
/// END
```

If a file like this is included, then it will patch the code entry it is named after. Note that this syntax can't work with newly created files, only with code entries that already exist in the game.

Note that when you are using a PATCH that searches for specific code, you are looking for the code that a specific version of Undertale Mod Tool is decompiling. In general, it should not change much as new versions come out, but it is possible that it will change. Beware of eventual exceptions that will show up due to a UMT version difference.

UMP will raise an error if it cannot find the code you have given, in an attempt to warn you of a possible decompilation issue. It will also raise an error if the code find more than one code that matches the given pattern, since it is most likely not unintended to match more than once. If you want to match multiple similar things, you will need to make sure you explicit where exactly that is by supplying "neighbors".

Note that all whitespace between the `///` of each line is considered. The first text requires everything included to be matched, so do not include unecessary whitespace, or, in multilines cases, you will need to provide the exact whitespace, for example, if the part you want to replace has indentation:

```gml
/// PATCH

/// REPLACE
    if this_code_is_normally_indented
    {
        then = "you should include whitespaces in multiline strings to properly match it"
    }
/// CODE
/// END
```

(Since no code was provided, it will just remove it)

A list of all commands follow below.

## After and before commands

Place code AFTER or BEFORE another code entry. Note that this will always include line breaks between the code. For example

```gml
/// AFTER
this_is_the_original_code = 0
/// CODE
code_after = "this is code placed after"
/// END
```

If the code looks like

```gml
foo = bar
this_is_the_original_code = 0
blahblah = 0
```

then it will be patched to look like:

```gml
foo = bar
this_is_the_original_code = 0
code_after = "this is code placed after"
blahblah = 0
```

It can also work with before:

```gml
/// BEFORE
this_is_the_original_code = 0
/// CODE
code_before = "this is code placed before"
/// END
```

in the same code from above gives

```gml
foo = bar
code_before = "this is code placed before"
this_is_the_original_code = 0
blahblah = 0
```

## Append and prepend commands

Append can be used to place code at the end of a file, and prepend can be used to add to the start of the file, like this:


Original code:

```gml
foo = bar
```

Patch:

```gml
/// PATCH

/// APPEND
this_is_an_append = 0
/// END

/// PREPEND
this_is_a_prepend = 0
/// END
```

The final code:

```gml
this_is_a_prepend = 0
foo = bar
this_is_an_append = 0
```

## Replace command

As it was already stated above, this command can be used to replace code for another.

Original code:

```gml
line1 = 1
line2 = 2
line3 = 3
```

The patch:

```gml
/// PATCH

/// REPLACE
line2
/// CODE
middle_line = "this is changed now"
/// END
```

The final code:

```gml
line1 = 1
middle_line = "this is changed now"
line3 = 3
```

# Preprocessing

In preprocessing, you can choose what part of your code will get used or not depending on your symbols. A symbol is just a word that can be defined or not. You can define them in your symbols array in `UMPLoader`.

## Symbol Boolean Expressions

You can write boolean expressions using symbols, which are used for IGNORING and for preprocessing (see below for more information). The syntax is basically the same as the one used in GameMakerLanguage. For example,

```
(!DEBUG && SYMBOL1) || SYMBOL2 
```

It will be evaluated to either TRUE or FALSE depending on the symbols. The symbols are treated as TRUE if they are defined and FALSE otherwise

## Selectively including files based on name

You can add custom code which will be able to filter which code you would like to include base on its file name. All you need to do is create an implementation of the `AcceptFile` function in your loader. Here is an example:

```cs
// CUSTOM variable to track the state of the "chapter"
public bool IsChapter1 { get; set; }

// custom implementation of the function
public virtual bool AcceptFile(string filePath)
{
    if (IsChapter1)
    {
        return filePath.StartsWith('chapter1');
    }
}
```

As seen above, the function takes the file path (relative to the mod directory) as the argument, and you must return `true` if the file should be accepted, and `false` otherwise. By default, it always returns `true`

## Ignoring Files

You can ignore files with your symbols. In the first line, after the file type, you can include `.ignore if EXPRESSION` to ignore the file if the symbol is defined. The expression should follow the format defined above. For example:

```gml
/// FUNCTIONS .ignore if !DEBUG

function some_debug_function()
{
    return "Debug"
}
```

## Selectively including code

You can remove or include parts of code depending on your symbols using if-else statements with the symbols. You can do so by using "#" followed by the keywords "if", "elsif", "else" and "endif". To start such block, you begin by writing either "#if", followed by a symbol boolean expression. Then, everything after that (including everything in the same line) will be ERASED if the condition is not met, up until the "#endif" statement. Likewise, you can chain it using else statements, with other expressions with "#elsif" or with no boolean condition using "#else". Here's an example

```gml
/// FUNCTIONS

function is_debug()
{
#if DEBUG
    return true
#elsif PRODUCTION
    return false
#else
    return "something else..."
#endif
}
```

If the DEBUG symbol is defined, it will return true, else if the PRODUCTION symbol is defined, it will return false. If neither are defined, it will return something else.

# Enums

Enums are part of the current verison of GML, but not supported in UTMT. An implementation of enums can be used with UMP files, but not using the same syntax of enums as in standard GML. To create custom enums, you will want to include inside your `UMPLoader` implementation declarations of enums. For example

```cs
public class ExampleLoader : UMPLoader
{
    /// ... rest of the code

    public enum ExampleEnum
    {
        Value,
        AnotherValue = 10
    }
}
```

After defining this, you can then call the enum inside GML using "#":

```gml
show_debug_message(#ExampleEnum.Value) // will be compiled as show_debug_message(0)
```

You can also access specific enum properties, using ".#" and the name, for example:

```gml
#ExampleEnum.#length
```

Supported properties include:

* "length": The total number of values in the enum

# Methods

You can create custom methods that return strings in C# and access them inside GML. To do this, you must add new methods
to your `UMPLoader` derived class. For example:

```cs
public class ExampleLoader : UMPLoader
{
    /// ... rest of the code

    public string ExampleMethod()
    {
        return "Hello, World!";
    }
}
```

Now, if you call the method inside the files:

```gml
show_debug_message(#ExampleMethod()) // this will get compiled as show_debug_message("Hello, World!")
```

Remember that the methods are executed at compile time only, and they must return strings, because the methods are used to generate GML.

Arguments are supported, and they can be of the following type:

* Strings, which is anything enclosed by double quotes.
* Integers and doubles
* UMP Literals: Arbitrary text that starts with @@ and ends with $$. Anything between them will be added as is, and this will be passed as a string.

Here's an example of all arguments
```cs
public class ExampleLoader : UMPLoader
{
    /// ... rest of the code

    public string ExampleMethod(string exampleString, int exampleInt, double exampleDouble, string exampleUMPLiteral)
    {
        return $"{exampleString} ({exampleInt + exampleDouble}): {exampleUMPLiteral}";
    }
}
```

In GML:

```gml
#ExampleMethod("case", 1, 2.3, @@
    show_debug_message("hello world!")
$$)
```

will be compiled as

```gml
case (2.4):
    show_debug_message("hello world!")
```
