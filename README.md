# Intro

UMP is a framework for modding games using Undertale Mod Tool - It is quick to learn (contains only a small guide) and has useful resources for maintaining mods with a decently large code base.

# Warning!

This framework doesn't work in the main verison of Undertale Mod Tool yet. You will need to use [this build of UTMT](https://github.com/krzys-h/UndertaleModTool/pull/1504) until the pull request is approved.

# About

UMP (pronounce it by letters or phonetically, both is fine) is a (mini) framework for modding using Undertale Mod Tool.
It focuses around being able to better organize your gamemaker (and assembly) code for your mod. Its features include:

* You can organize all your code as external `.gml` files in a single folder, with any subdirectory organization, and it will link all subdirectories.

* You can configurate what code entries each file will affect depending on the file name. The standard use case for this is
for example making a file named `obj_OBJECT_NAME_Step_0.gml` map to a code entry `gml_Object_OBJECT_NAME_Step_0.gml`; you are free to customize it to your project

* Automatically create new objects, if code for such an object is detected, and organizes new functions so that they are always defined in a proper order

* Allows to easily create custom functions, allowing you to define multiple functions in one file

* Allows for using PATCH files which will only change parts of a code entry, reducing unused code and making the patches more version resistent

* Allows using enums in UTMT (syntax isn't the same as in normal GML, check guide)

* Allows using inline C# methods to generate GML, which work by defining them in the C# part of the script and calling them in GML

* Allows for code preprocessing using symbols

# Installation tutorial

If your project already contains a base `.csx` script, then:

1. Download the `ump.csx` file from the releases
2. Drop it anywhere you want inside your project
3. Inside your main `.csx` script, add `#load "RELATIVE PATH TO ump.csx"` at the top
4. The mod is installed! Check the guide to learn how to use it

# Guide/Tutorial

See: [Guide on how to use the framework](https://github.com/nhaar/UMP/blob/main/guide/guide.md)

# Mods that use UMP

Unsure if UMP is good for you? Check examples of mods that use it and see if you like it.

* [Keucher Mod - A speedrun practice mod for Deltarune](https://github.com/nhaar/keucher-mod)
