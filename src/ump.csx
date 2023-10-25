using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;

// used for decompiling
ThreadLocal<GlobalDecompileContext> UMP_DECOMPILE_CONTEXT = new ThreadLocal<GlobalDecompileContext>(() => new GlobalDecompileContext(Data, false));

UMPMain();

/// <summary>
/// The main function of the script
/// </summary>
void UMPMain ()
{

    var scriptDir = Path.GetDirectoryName(ScriptPath);
    string config = File.ReadAllText(Path.Combine(scriptDir, "ump-config.json"));
    Dictionary<string, string> umpConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(config);

    // the path to all folders that will have the files that will be automatically read
    string modPath = umpConfig["mod-path"];

    string[] files = Directory.GetFiles(Path.Combine(scriptDir, modPath), "*.gml", SearchOption.AllDirectories);

    List<string> functionFiles = new();

    List<string> notFunctionFiles = new();

    Dictionary<string, string> functionCode = new();
    Dictionary<string, string> functionNames = new();

    // first check: function separation and object creation
    foreach (string file in files)
    {
        // ignoring files
        // TO-DO: code being read twice? possible optimization if needed
        string code = File.ReadAllText(file);
        if (code.StartsWith("/// IGNORE"))
            continue;
        // "opening" function files
        else if (code.StartsWith("/// FUNCTIONS"))
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
                                args.Add(code.Substring(nameStart, i - nameStart).Trim());
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

                        // initializing args, unless they are argumentN in gamemaker because those already work normally
                        foreach (string arg in args)
                        {
                            if (arg.StartsWith("argument"))
                            {
                                continue;
                            }
                            else
                            {
                                functionCodeBlock = $"var {arg} = argument{args.IndexOf(arg) + 1};" + functionCodeBlock;
                            }
                        }
                        functionCodeBlock = $"function {functionName}() {{ {functionCodeBlock} }}";
                        string entryName = $"gml_GlobalScript_{functionName}";
                        Console.WriteLine(functionName);
                        functionCode[entryName] = functionCodeBlock;
                        functionNames[entryName] = functionName;
                    }
                }
                i++;
            }

            // skip this file
            continue;
        }

        // extract name from event ending in number or with collision which can not end in a number
        string objName = Regex.Match(file, @"(?<=gml_Object_).*?((?=(_[a-zA-Z]+_\d+))|(?=_Collision))").Value;
        if (objName != "")
        {
            if (Data.GameObjects.ByName(objName) == null)
            {
                UMPCreateGMSObject(objName);
            }
        }
        if (file.Contains("gml_GlobalScript") || file.Contains("gml_Script"))
        {
            functionFiles.Add(file);
        }
        else
        {
            notFunctionFiles.Add(file);
        }
    }

    foreach (string file in functionFiles)
    {
        // this is for the automatically added from /// FUNCTIONS type files
        if (functionCode.ContainsKey(file)) continue;

        string code = File.ReadAllText(file);
        string functionName = Regex.Match(file, @"(?<=(gml_Script_|gml_GlobalScript_)).*?(?=\.gml)").Value;
        functionCode[file] = code;
        functionNames[file] = functionName;
    }

    // order functions so that they never call functions not yet defined
    List<string> functionsInOrder = new();

    while (functionsInOrder.Count < functionCode.Count)
    {   
        // go through each function, check if it's never mentiond in all functions that are already not in functionsInOrder 
        foreach (string testFunction in functionCode.Keys)
        {
            if (functionsInOrder.Contains(testFunction)) continue;
            bool isSafe = true;
            foreach (string otherFunction in functionCode.Keys)
            {
                if (!functionsInOrder.Contains(otherFunction) && otherFunction != testFunction)
                {
                    if (Regex.IsMatch(functionCode[testFunction], @$"\b{functionNames[otherFunction]}\b"))
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

    foreach (string file in functionsInOrder)
    {
        UMPImportGML(file.Replace(".gml", ""), functionCode[file]);
    }
    foreach (string file in notFunctionFiles)
    {
        UMPImportFile(file);
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
    try
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var code = File.ReadAllText(path);
        UMPImportGML(fileName, code);
    }
    catch (System.Exception e)
    {
        Console.WriteLine(e.Message);
        Console.WriteLine(e.StackTrace);
    }
}

/// <summary>
/// Import a GML string to the code entry with its name, using the UMP format
/// </summary>
/// <param name="codeName"></param>
/// <param name="code"></param>
/// <exception cref="Exception"></exception>
void UMPImportGML (string codeName, string code)
{
    var isPatchFile = code.StartsWith("/// PATCH") && UMPCheckIfCodeExists(codeName);

    if (isPatchFile)
    {
        UMPPatchFile patch = new UMPPatchFile(code);
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

    /// <summary>
    /// Exception thrown when a command is not recognized
    /// </summary>
    public class ModifiedCommandException : Exception
    {
        public ModifiedCommandException(string line) : base("Unknown command in modified code: " + line) { }
    }

    public UMPPatchFile (string gmlCode)
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
                    if (Regex.IsMatch(line, @"\bCODE\b"))
                    {
                        inOriginalText = false;
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
                        throw new ModifiedCommandException(line);
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
                        throw new ModifiedCommandException(line);
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