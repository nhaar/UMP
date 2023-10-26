using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;

// used for decompiling
ThreadLocal<GlobalDecompileContext> UMP_DECOMPILE_CONTEXT = new ThreadLocal<GlobalDecompileContext>(() => new GlobalDecompileContext(Data, false));

// the path to the MAIN script (not this one)
string UMP_SCRIPT_DIR = Path.GetDirectoryName(ScriptPath);

// the config file for UMP
Dictionary<string, object> UMP_CONFIG = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(Path.Combine(UMP_SCRIPT_DIR, "ump-config.json"))); 

// the path to all folders that will have the files that will be automatically read
string UMP_MOD_PATH = (string)UMP_CONFIG["mod-path"];

// prefixes for game object files
List<string> UMP_OBJECT_PREFIXES = new();
try
{
    UMP_OBJECT_PREFIXES = ((Newtonsoft.Json.Linq.JArray)UMP_CONFIG["object-prefixes"]).ToObject<List<string>>();
}
catch (System.Exception)
{        
}

// all files that will be read
string[] UMP_MOD_FILES = Directory.GetFiles(Path.Combine(UMP_SCRIPT_DIR, UMP_MOD_PATH), "*.gml", SearchOption.AllDirectories);

// exceptions need to be logged if the file is being loaded, otherwise UTMT crashes
try
{
    UMPMain();
}
catch (Exception e)
{
    Console.WriteLine("UMP related exception! There may be an issue with your files.");
    Console.WriteLine(e.Message);
    Console.WriteLine(e.StackTrace);
}

/// <summary>
/// The main function of the script
/// </summary>
void UMPMain ()
{
    List<UMPFunctionEntry> functions = new();
    List<UMPCodeEntry> nonFunctions = new();
    Dictionary<string, string> functionNames = new();

    // first check: function separation and object creation
    foreach (string file in UMP_MOD_FILES)
    {
        string code = File.ReadAllText(file);
        // ignoring files
        if (UMPHasCommand(code, "IGNORE"))
            continue;
        // "opening" function files
        else if (UMPHasCommand(code, "FUNCTIONS"))
        {
            string currentFunction = "";
            int i = 0;
            int start = 0;
            int depth = 0;
            while (i < code.Length)
            {
                char c = code[i];
                if (c == 'f')
                {
                    if (code.Substring(i, 8) == "function")
                    {
                        start = i;
                        i += 8;
                        int nameStart = i;
                        while (code[i] != '(')
                        {
                            i++;
                        }
                        string functionName = code.Substring(nameStart, i - nameStart).Trim();
                        List<string> args = new();
                        nameStart = i + 1;
                        while (true)
                        {
                            bool endLoop = code[i] == ')';
                            if (code[i] == ',' || endLoop)
                            {
                                string argName = code.Substring(nameStart, i - nameStart).Trim();
                                if (argName != "")
                                    args.Add(argName);
                                nameStart = i + 1;
                                if (endLoop)
                                    break;
                            }
                            i++;
                        }
                        while (code[i] != '{')
                        {
                            i++;
                        }
                        int codeStart = i + 1;
                        do
                        {
                            if (code[i] == '{')
                            {
                                depth++;
                            }
                            else if (code[i] == '}')
                            {
                                depth--;
                            }
                            i++;
                        }
                        while (depth > 0);
                        // - 1 at the end to remove the last }
                        string functionCodeBlock = code.Substring(codeStart, i - codeStart - 1);

                        
                        List<string> gmlArgs = new();
                        // initializing args, unless they are argumentN in gamemaker because those already work normally
                        for (int j = 0; j < args.Count; j++)
                        {
                            gmlArgs.Add("argument" + j);
                            string arg = args[j];
                            if (arg.StartsWith("argument"))
                            {
                                continue;
                            }
                            else
                            {
                                functionCodeBlock = $"var {arg} = argument{j};" + functionCodeBlock;
                            }
                        }
                        functionCodeBlock = $"function {functionName}({string.Join(", ", gmlArgs)}) {{ {functionCodeBlock} }}";
                        string entryName = $"gml_GlobalScript_{functionName}";
                        functions.Add(new UMPFunctionEntry(entryName, functionCodeBlock, functionName));
                    }
                }
                i++;
            }

            // skip this file
            continue;
        }

        if (file.Contains("gml_GlobalScript") || file.Contains("gml_Script"))
        {
            string entryName = Path.GetFileNameWithoutExtension(file);
            string functionName = Regex.Match(entryName, @"(?<=(gml_Script_|gml_GlobalScript_))[_\d\w]+").Value;

            functions.Add(new UMPFunctionEntry(entryName, code, functionName));
        }
        else
        {
            string entryName = Path.GetFileNameWithoutExtension(file);
            nonFunctions.Add(new UMPCodeEntry(UMPPrefixEntryName(entryName), code));
        }
    }


    // creating new objects
    foreach (UMPCodeEntry entry in nonFunctions)
    {
        // extract name from event ending in number or with collision which can not end in a number
        string objName = UMPGetObjectName(entry.Name);
        if (objName != "")
        {
            if (Data.GameObjects.ByName(objName) == null)
            {
                UMPCreateGMSObject(objName);
            }
        }
    }

    // order functions so that they never call functions not yet defined
    List<UMPFunctionEntry> functionsInOrder = new();

    while (functionsInOrder.Count < functions.Count)
    {   
        // go through each function, check if it's never mentiond in all functions that are already not in functionsInOrder 
        foreach (UMPFunctionEntry testFunction in functions)
        {
            if (functionsInOrder.Contains(testFunction)) continue;
            bool isSafe = true;
            foreach (UMPFunctionEntry otherFunction in functions)
            {
                if (!functionsInOrder.Contains(otherFunction) && !otherFunction.Equals(testFunction))
                {
                    if (Regex.IsMatch(testFunction.Code, @$"\b{otherFunction.FunctionName}\b"))
                    {
                        isSafe = false;
                        break;
                    }
                }
            }
            if (isSafe)
            {
                functionsInOrder.Add(testFunction);
            }
        }
    }

    foreach (UMPFunctionEntry functionEntry in functionsInOrder)
    {
        UMPImportGML(functionEntry.Name, functionEntry.Code);
    }
    foreach (UMPCodeEntry entry in nonFunctions)
    {
        UMPImportGML(entry.Name, entry.Code);
    }
}


/// <summary>
/// Check if a code entry exists by its name
/// </summary>
/// <param name="codeName"></param>
/// <returns></returns>
bool UMPCheckIfCodeExists (string codeName)
{
    return Data.Code.ByName(codeName) != null;
}

/// <summary>
/// Import a GML file into the game with its path, using the UMP format
/// </summary>
/// <param name="path"></param>
void UMPImportFile (string path)
{
    // at the moment, exceptions here crash UTMT
    var fileName = Path.GetFileNameWithoutExtension(path);
    var code = File.ReadAllText(path);
    UMPImportGML(fileName, code);
}

/// <summary>
/// Import a GML string to the code entry with its name, using the UMP format
/// </summary>
/// <param name="codeName"></param>
/// <param name="code"></param>
/// <exception cref="Exception"></exception>
void UMPImportGML (string codeName, string code)
{
    codeName = UMPPrefixEntryName(codeName);
    var isPatchFile = UMPHasCommand(code, "PATCH") && UMPCheckIfCodeExists(codeName);

    if (isPatchFile)
    {
        UMPPatchFile patch = new UMPPatchFile(code, codeName);
        if (patch.RequiresCompilation)
        {
            UMPAddCodeToPatch(patch, codeName);
        }

        foreach (UMPPatchCommand command in patch.Commands)
        {
            if (command is UMPAfterCommand)
            {
                int placeIndex = patch.Code.IndexOf(command.OriginalCode) + command.OriginalCode.Length;
                patch.Code = patch.Code.Insert(placeIndex, "\n" + command.NewCode + "\n");
            }
            else if (command is UMPBeforeCommand)
            {
                int placeIndex = patch.Code.IndexOf(command.OriginalCode);
                patch.Code = patch.Code.Insert(placeIndex, "\n" + command.NewCode + "\n");
            }
            else if (command is UMPReplaceCommand)
            {
                patch.Code = patch.Code.Replace(command.OriginalCode, command.NewCode);
            }
            else if (command is UMPAppendCommand)
            {
                UMPAppendGML(codeName, command.NewCode);
                if (patch.RequiresCompilation)
                {
                    UMPAddCodeToPatch(patch, codeName);
                }
            }
            else if (command is UMPPrependCommand)
            {
                patch.Code = command.NewCode + "\n" + patch.Code;
            }
            else
            {
                throw new Exception("Unknown command type: " + command.GetType().Name);
            }
            
            if (patch.RequiresCompilation)
            {
                Data.Code.ByName(codeName).ReplaceGML(patch.Code, Data);
            }
        }

    }
    else
    {
        ImportGMLString(codeName, code);
    }
}

/// <summary>
/// Add the decompiled code of a code entry to a patch
/// </summary>
/// <param name="patch"></param>
/// <param name="codeName"></param>
void UMPAddCodeToPatch (UMPPatchFile patch, string codeName)
{
    if (Data.KnownSubFunctions is null) Decompiler.BuildSubFunctionCache(Data);
    patch.Code = Decompiler.Decompile(Data.Code.ByName(codeName), UMP_DECOMPILE_CONTEXT.Value);
}

/// <summary>
/// Represents a command in a UMP patch file
/// </summary>
abstract class UMPPatchCommand
{
    /// <summary>
    /// Whether the command requires code from the original entry to be presented
    /// </summary>
    public abstract bool BasedOnText { get; }

    /// <summary>
    /// Whether the command requires the code to be decompiled and then recompiled to create the changes
    /// </summary>
    public abstract bool RequiresCompilation { get; }

    /// <summary>
    /// If is based on text, the original code required
    /// </summary>
    public string OriginalCode { get; set; }

    /// <summary>
    /// The new code to be added
    /// </summary>
    public string NewCode { get; set; }

    public UMPPatchCommand (string newCode, string originalCode = null)
    {
        NewCode = newCode;
        OriginalCode = originalCode;
    }
}

/// <summary>
/// Command that places some code after another
/// </summary>
class UMPAfterCommand : UMPPatchCommand
{
    public UMPAfterCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => true;

    public override bool RequiresCompilation => true;
}

/// <summary>
/// Command that places some code before another
/// </summary>
class UMPBeforeCommand : UMPPatchCommand
{
    public UMPBeforeCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => true;

    public override bool RequiresCompilation => true;
}

/// <summary>
/// Command that replaces some code for another
/// </summary>
class UMPReplaceCommand : UMPPatchCommand
{
    public UMPReplaceCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => true;

    public override bool RequiresCompilation => true;

}

/// <summary>
/// Command that adds code to the end of a code entry
/// </summary>
class UMPAppendCommand : UMPPatchCommand
{
    public UMPAppendCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => false;

    public override bool RequiresCompilation => false;
}

/// <summary>
/// Command that prepends code to the start of a code entry
/// </summary>
class UMPPrependCommand : UMPPatchCommand
{
    public UMPPrependCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => false;

    public override bool RequiresCompilation => true;
}

/// <summary>
/// Represents a .gml file that has the `/// PATCH` syntax in it
/// </summary>
class UMPPatchFile
{
    /// <summary>
    /// All commands in the patch
    /// </summary>
    public List<UMPPatchCommand> Commands = new();

    /// <summary>
    /// Whether any of the patches require the code to be decompiled and then recompiled to create the changes
    /// </summary>
    public bool RequiresCompilation { get; }

    /// <summary>
    /// Code of the code entry that is being updated, expected to always be up to date with patch changes
    /// </summary>
    public string Code { get; set ; }

    public UMPPatchFile (string gmlCode, string entryName)
    {
        gmlCode = gmlCode.Substring(gmlCode.IndexOf('\n') + 1);
        string[] patchLines = gmlCode.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

        Type currentCommand = null;
        List<string> originalCode = new();
        List<string> newCode = new();
        bool inOriginalText = true;

        foreach (string line in patchLines)
        {
            if (currentCommand != null)
            {
                if (line.StartsWith("///"))
                {
                    if (inOriginalText)
                    {
                        if (Regex.IsMatch(line, @"\bCODE\b"))
                        {
                            inOriginalText = false;
                        }
                        else
                        {
                            throw new Exception($"Error in patch file \"{entryName}\": Expected CODE command");
                        }
                    }
                    else if (Regex.IsMatch(line, @"\bEND\b"))
                    {
                        inOriginalText = true;
                        string originalCodeString = string.Join("\n", originalCode);
                        string newCodeString = string.Join("\n", newCode);
                        object command = Activator.CreateInstance(currentCommand, args: new object[] { newCodeString, originalCodeString });                                                        
                        Commands.Add((UMPPatchCommand)command);
                        currentCommand = null;
                        newCode = new List<string>();
                        originalCode = new List<string>();
                    }
                    else
                    {
                        throw new Exception($"Error in patch file \"{entryName}\": Expected END command");
                    }
                }
                else
                {
                    if (inOriginalText)
                    {
                        originalCode.Add(line);
                    }
                    else
                    {
                        newCode.Add(line);
                    }
                }
            }
            else
            {
                if (line.StartsWith("///"))
                {
                    if (Regex.IsMatch(line, @"\bAFTER\b"))
                    {
                        currentCommand = typeof(UMPAfterCommand);
                    }
                    else if (Regex.IsMatch(line, @"\bBEFORE\b"))
                    {
                        currentCommand = typeof(UMPBeforeCommand);
                    }
                    else if (Regex.IsMatch(line, @"\bREPLACE\b"))
                    {
                        currentCommand = typeof(UMPReplaceCommand);
                    }
                    else if (Regex.IsMatch(line, @"\bAPPEND\b"))
                    {
                        inOriginalText = false;
                        currentCommand = typeof(UMPAppendCommand);
                    }
                    else if (Regex.IsMatch(line, @"\bPREPEND\b"))
                    {
                        inOriginalText = false;
                        currentCommand = typeof(UMPPrependCommand);
                    }
                    else
                    {
                        Console.WriteLine("WARNING: Unknown command in patch file: " + line);
                    }
                }
            }
        }

        foreach (UMPPatchCommand command in Commands)
        {
            if (command.RequiresCompilation)
            {
                RequiresCompilation = true;
                break;
            }
        }
    }
}

/// <summary>
/// Append GML to the end of a code entry
/// </summary>
/// <param name="codeName"></param>
/// <param name="code"></param>
void UMPAppendGML (string codeName, string code)
{
    Data.Code.ByName(codeName).AppendGML(code, Data);
}

/// <summary>
/// Create a game object with the given name
/// </summary>
/// <param name="objectName"></param>
/// <returns></returns>
UndertaleGameObject UMPCreateGMSObject (string objectName)
{
    var obj = new UndertaleGameObject();
    obj.Name = Data.Strings.MakeString(objectName);
    Data.GameObjects.Add(obj);

    return obj;
}

/// <summary>
/// Represents a code entry that will be added
/// </summary>
class UMPCodeEntry
{
    public string Name { get; set; }
    public string Code { get; set; }

    public UMPCodeEntry (string name, string code)
    {
        Name = name;
        Code = code;
    }

    public override bool Equals(object obj)
    {        
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }
        
        UMPCodeEntry other = (UMPCodeEntry)obj;
        return Name == other.Name && Code == other.Code;
    }
    
    // override object.GetHashCode
    public override int GetHashCode()
    {
        // TODO: write your implementation of GetHashCode() here
        throw new System.NotImplementedException();
        return base.GetHashCode();
    }
}

/// <summary>
/// Represents a code entry that is a function
/// </summary>
class UMPFunctionEntry : UMPCodeEntry
{
    public string FunctionName { get; set; }

    public UMPFunctionEntry (string name, string code, string functionName) : base(name, code)
    {
        FunctionName = functionName;
    }
}

bool UMPHasCommand (string code, string command)
{
    return Regex.IsMatch(code, @$"^\s*/// {command}", RegexOptions.Multiline);
}

/// <summary>
/// Get the name of the game object from a code entry that belongs to the object (in the UTMT code entry name format)
/// </summary>
/// <param name="entryName"></param>
/// <returns></returns>
string UMPGetObjectName (string entryName)
{
    return Regex.Match(entryName, @"(?<=gml_Object_).*?((?=(_[a-zA-Z]+_\d+))|(?=_Collision))").Value;
}

/// <summary>
/// Prefix a code entry name with the proper game object prefix if it has an object prefix defined in the config
/// </summary>
/// <param name="entryName"></param>
/// <returns></returns>
string UMPPrefixEntryName (string entryName)
{
    foreach (string prefix in UMP_OBJECT_PREFIXES)
    {
        if (entryName.StartsWith(prefix))
        {
            return$"gml_Object_{entryName}";
        }
    }
    return entryName;
}