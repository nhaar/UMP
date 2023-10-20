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

## UMP_DECOMPILE_CONTEXT Variable

### Definition

An instance of a `ThreadLocal<GlobalDecompileContext>` that you can use to decompile code.
