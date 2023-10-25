# Warning!

This framework only partly works in [this build of of UTMT](https://github.com/krzys-h/UndertaleModTool/pull/1504). If you are using a base `.csx` script (details below), until the pull request is approved, you will need to build the code for that if you want to use this.

# Welcome to UMP - A mini framework for modding with Undertale Mod Tool

UMP (pronounce it by letters or phonetically, both is fine) is a (mini) framework for modding using Undertale Mod Tool. It includes:

* Support for having all the code for the mod as `.gml` files. You can link a folder with all the files, and name them such that they will be linked directly to
existing objects and scripts, which is already an UTMT feature, but also:

1. Automatically create new objects if attempting to link to a non-existing object

2. Scripts are compiled in the proper order as to not cause problems

* Add simple and short syntax for using patch files, as to have files that only change parts of the code, making them clean and more resistant to version changes

* Allow defining multiple functions in one file

* Extra useful API that can be used inside the `.csx` scripts

The framework is meant to be used alongside a base `.csx` script, or if you do not wish to use any custom scripting code, you can use it directly.

# Installation tutorial

If your project already contains a base `.csx` script, then:

1. Download the `ump.csx` file from the releases
2. Drop it anywhere you want inside your project
3. Inside your main `.csx` script, add `#load "RELATIVE PATH TO ump.csx"` at the top
4. In the same folder as your main `.csx` script, add a `ump-config.json` file, and set it to your desired options (check tutorial below)

If your project contains no `.csx` script, then:
1. Put the `ump.csx` file wherever you keep your `gml` code, and in the same folder add a `ump-config.json`.
2. Configurate the `ump-config` to link to the folder (check tutorial for how to)
3. Whenever you wish to run the mod, run the `ump.csx` file

# Guide/Tutorial

See: [Guide on how to use the framework](https://github.com/nhaar/UMP/blob/main/guide/guide.md)
