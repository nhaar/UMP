Here you will see everything you need to know to use this framework!

# Setting up the mod folder

The mod works by linking a folder where all `.gml` code is kept. In the `ump-config.json` file that you set up during
installation, you can set up the path to the mod folder, **relative to the main `.csx` script** of your project. For example, a folder named `mod` in the same directory as the script:

```json
{
    "mod-path": "mod/"
}
```

The files inside the folder can be in any subdirectory, as long as they end in `.gml`.

# Properly naming the files

In vanilla Undertale Mod Tool, you can create new object code directly by just using proper name for the files. For example, if you have an object called `obj_object`, and you want to add a create event listener, you can simply leave a file named `gml_Object_obj_object_Create_0.gml` inside the mod folder. The number at the end is required and can be used to have different events. So, for objects, you just need to name the file in the format `gml_Object_OBJECT_NAME_EVENT_NAME_EVENTNUMBER`.

In the framework, if you use an object that doesn't exist, the object will be automatically created.

For functions, you can name the file in the pattern `gml_GlobalScript_FUNCTION_NAME`, and for GMS 2.3 and above, you may use the `function ()` syntax to define them.

## Shorthand for object prefixes

If you're not very happy with having to write `gml_Object` in every file name, then you can define object prefixes in your configuration file. Inside `ump-config`, you can add the key `object-prefixes`, which should have as the value a JSON array containing all the allowed prefixes for an object. If a file begins with any of the given prefixes, it will be considered an object file. Here's how it should look like:

```json
{
    "object-prefixes": ["obj_", "o_"]
}
```

Now, all files inside the mods directory beginning with either `obj_` or `o_` will automatically be treated as objects.

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

# Ignoring Files

If you want to include `.gml` files in the mod folder and not manually add them, you can add `/// IGNORE` to their first line.

# Function files

UMP supports a custom syntax to define multiple functions in one file. First, write the first line of the file as being `/// FUNCTIONS`.

```gml
/// FUNCTIONS
```

Then, the functions can be defined as normally

```gml
/// FUNCTIONS

function hello()
{
    return "Hello";
}

function world(argument0)
{
    return is_undefined(argument0) ? "World!" : argument0;
}
```

Normally, UTMT only accepts arguments named like `argument0, ... argumentN`. This is not the case with functions defined in a UMP function file

```gml
function named_arg(i_have_name)
{
    return i_have_name;
}
```

Naturally, `argumentN` still works as normal.

Note that hoisting is done as it normally is with other functions: You can use functions before defining them in the file, you just can't have circular dependence.

# Enums

Enums are part of the current verison of GML, but not supported in UTMT. An implementation of enums can be used with UMP files, but not using the same syntax of enums as in standard GML, but instead, a mix of CSX files and UMP commands.

## Setup

1. First, you must create your enums. Create a C# code file including ONLY ENUMS, and no comments or any other code, such as

```cs
enum TestEnum
{
    A,
    B,
    C = 4,
}
```

2. Then, in your `ump-config` file, add the path to the enum file using `enum-file`:

```json
{
    "enum-file": "enum.csx" // relative to the main script
}
```

3. Finally, you can use the enums inside your GML code by using the command `/// USE ENUM` anywhere in your file. An example of a `.gml` file using the enums from UMP:

```gml
/// USE ENUM
show_debug_message(Test.A)
show_debug_message(Test.B)
show_debug_message(Test.C)
```

This will be compiled as:
```gml
show_debug_message(0)
show_debug_message(1)
show_debug_message(4)
```

## Converting Case

If you bother to keep a consistent and different case between your `.csx` files and `.gml` files, you can use the case converting option. The cases for the enum name and members are assumed to be pascal case (PascalCase). You can convert it to either camel case (camelCase), snake case (snake_case) or screaming snake case (SCREAMING_SNAKE_CASE). To do this, include in your `ump-config` file first the key `case-converter` set to `true`, and then the keys `enum-name-case` and `enum-member-case` can be added if you want to change their case. The values of the cases must be one of these:
* `snake-case`
* `screaming-snake-case`
* `camel-case`

Example"

```json
{
    "case-converter": true,
    "enum-name-case": "screaming-snake-case",
    "enum-member-case": "snake-case"
}
```

# Mod API

Aditionally, if you are loading the mod inside another `.csx` script, you can use the API given by the mod, which consists of some functions and variables, some more useful than others, listed below

## UMPAppendGML Method

### Definition

Append GML code to the end of a code entry

### Parameters

`codeName` String

Name of the code entry

`code` String

Code to append

### Returns

`void`

## UMPCreateGMSObject Method

### Definition

Create a new game object and add it to the game data

### Parameters

`objectName` String

Name of the object

### Returns

`UndertaleModLib.Models.UndertaleGameObject`

The object created

## UMPImportFile Method

### Definition

The same as `ImportGMLFile`, but it also accepts the UMP patch files.

If anything wrong happens with the process, the mod will log the error to the console.

### Parameters

`path` String

The path to the file to be imported

### Returns

`void`

## UMPImportGML Method

### Definition

The same as `ImportGMLString`, but it also accepts the UMP patch files.

### Parameters

`codeName` String

The name of the code entry to import the string to

`code` String

The GML code to add to the code entry

### Returns

`void`

## UMPCheckIfCodeExists Method

### Definition

Checks if a code entry exists in the game data.

### Parameters

`codeName` String

The name of the code entry

### Returns

`bool`

`true` if the code entry exists, `false` if it doesn't

## UMPGetObjectName Method

### Definition

Get the name of the game object from a code entry that belongs to the object (in the UTMT code entry name format). For example, the name of the game object from the code entry `gml_Object_obj_time_Create_0` is `obj_time`.

### Parameters

`entryName` String

The name of the code entry

### Returns
`string`

The name of the game object

## UMPPrefixEntryName Method

### Definition

Takes a name, and if necessary adds the UTMT object prefix to it, if the name given starts with any of the object prefixes defined in the config file. If not, it returns the inputted string. For example, it can transform `obj_time` -> `gml_Object_obj_time`, of `obj_` is in the object prefixes in the config file.

### Returns
`string`

The entry name, prefixed if needed.

## UMP_DECOMPILE_CONTEXT Variable

### Type
`ThreadLocal<GlobalDecompileContext>`

### Definition

An instance of a `ThreadLocal<GlobalDecompileContext>` that you can use to decompile code.

## UMP_SCRIPT_DIR Variable

### Type
`string`

### Definition

The path to the directory that contains the main script that is being ran by UndertaleModTool.

## UMP_MOD_PATH Variable

### Type
`string`

### Definition

The path to the mod folder.

## UMP_MOD_FILES Variable

### Type
`string[]`

### Definition
An array with all the `gml` files inside the mod folder.