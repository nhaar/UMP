using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;

/// <summary>
/// The main function of the script
/// </summary>
void UMPLoad
(
    string modPath = "",
    Type[] enums = null,
    bool convertCase = false,
    UMPCaseConverter.NameCase enumNameCase = UMPCaseConverter.NameCase.PascalCase,
    UMPCaseConverter.NameCase enumMemberCase = UMPCaseConverter.NameCase.PascalCase,
    string[] objectPrefixes = null,
    bool useIgnore = true
)
{
    string[] gmlFiles = null;
    string[] asmFiles = null;
    string searchPath = Path.Combine(Path.GetDirectoryName(ScriptPath), modPath);
    if (File.Exists(searchPath))
    {
        string[] files = new string[] { searchPath };
        string[] empty = new string[] { };
        if (modPath.EndsWith(".gml"))
        {
            gmlFiles = files;
            asmFiles = empty;
        }
        else if (modPath.EndsWith(".asm"))
        {
            gmlFiles = empty;
            asmFiles = files;
        }
        else
        {
            throw new Exception($"Mod path \"{searchPath}\" is not a .gml or .asm file");
        }
    }
    else if (Directory.Exists(searchPath))
    {
        gmlFiles = Directory.GetFiles(searchPath, "*.gml", SearchOption.AllDirectories);
        asmFiles = Directory.GetFiles(searchPath, "*.asm", SearchOption.AllDirectories);
    }
    else
    {
        throw new Exception($"Mod path \"{searchPath}\" does not exist");
    }

    Dictionary<string, Dictionary<string, int>> enumValues = new();
    if (enums != null)
    {
    foreach (Type enumType in enums)
    {
        Dictionary<string, int> values = new();
        foreach (string name in Enum.GetNames(enumType))
        {
            values.Add(name, (int)Enum.Parse(enumType, name));
        }
        enumValues.Add(enumType.Name, values);
        }
    }

    if (convertCase)
            {
            if (enumNameCase != UMPCaseConverter.NameCase.PascalCase)
                {
                string[] keys = enumValues.Keys.ToArray();
                    foreach (string enumName in keys)
                    {
                        string newName = UMPCaseConverter.Convert(enumNameCase, enumName);
                    enumValues.Add(newName, enumValues[enumName]);
                    enumValues.Remove(enumName);
                    }
                }
            if (enumMemberCase != UMPCaseConverter.NameCase.PascalCase)
            {
                foreach (string enumName in enumValues.Keys)
                    {
                        Dictionary<string, int> newMembers = new();
                    foreach (string enumMember in enumValues[enumName].Keys)
                        {
                            string newMemberName = UMPCaseConverter.Convert(enumMemberCase, enumMember);
                        newMembers.Add(newMemberName, enumValues[enumName][enumMember]);
                        }
                    enumValues[enumName] = newMembers;
        }
    }
    }

    List<UMPFunctionEntry> functions = new();
    List<UMPCodeEntry> imports = new();
    List<UMPCodeEntry> patches = new();
    Dictionary<string, string> functionNames = new();

    Dictionary<string, string> processedCode = new();

    foreach (string file in asmFiles)
    {
        processedCode[file] = GetDisassemblyText(file);
    }

    foreach (string file in gmlFiles)
    {
        string code = File.ReadAllText(file);

        // for enums
        Regex enumPattern = new Regex(@"#[\w\d_]+\.[\w\d_]+");
        code = enumPattern.Replace(code, match =>
        {
            string value = match.Value;
            string[] names = value.Split('.');
            string enumName = names[0].Substring(1);
            string enumMember = names[1];
            if (!enumValues.ContainsKey(enumName))
            {
                throw new Exception($"Enum \"{enumName}\" not found in enum file");
            }
            if (!enumValues[enumName].ContainsKey(enumMember))
            {
                throw new Exception($"Enum member \"{enumMember}\" not found in enum \"{enumName}\"");
            }
            return enumValues[enumName][enumMember].ToString();
        });
    
        processedCode[file] = code;
    }

    // first check: function separation and object creation
    foreach (string file in processedCode.Keys)
    {
        string code = processedCode[file];
    
        // ignoring files
        if (useIgnore && Regex.IsMatch(code, @"^///.*?\.ignore"))
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
        else if (Regex.IsMatch(code, @"^/// (IMPORT|PATCH)"))
        {
            string commandArg = Regex.Match(code, @"(?<=^///\s*(IMPORT|PATCH)[^\S\r\n]*)[\d\w_]+").Value.Trim();
            string fileName = "";
            string codeName = "";
            if (commandArg != "" && commandArg != ".ignore")
            {
                fileName = commandArg;
            }
            else
            {
                fileName = file;
            }
            codeName = Path.GetFileNameWithoutExtension(fileName);

            string objName = UMPGetObjectName(codeName);
            if (objName != "")
            {
                if (Data.GameObjects.ByName(objName) == null)
                {
                    UMPCreateGMSObject(objName);
                }
            }

            bool isASM = fileName.EndsWith(".asm");
            if (objectPrefixes != null)
            {
                foreach (string prefix in objectPrefixes)
                {
                    if (codeName.StartsWith(prefix))
                    {
                        codeName = $"gml_Object_{codeName}";
                    }
                }
            }

            UMPCodeEntry codeEntry = new UMPCodeEntry(codeName, code, isASM);
            if (code.StartsWith("/// PATCH"))
            {
                patches.Add(codeEntry);
        }
            else
        {
                imports.Add(codeEntry);
            }
        }
        
        if (file.Contains("gml_GlobalScript") || file.Contains("gml_Script"))
        {
            string entryName = Path.GetFileNameWithoutExtension(file);
            string functionName = Regex.Match(entryName, @"(?<=(gml_Script_|gml_GlobalScript_))[_\d\w]+").Value;

            functions.Add(new UMPFunctionEntry(entryName, code, functionName));
        }
        else
        {
            // nonFunctions.Add(new UMPCodeEntry(UMPPrefixEntryName(entryName), code));
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
    }
    // foreach (UMPCodeEntry entry in nonFunctions)
    // {
        // UMPImportGML(entry.Name, entry.Code);
    // }

    foreach (UMPCodeEntry entry in imports)
    {
        if (entry.isASM)
        {
            ImportASMString(entry.Name, entry.Code);
        }
        else
        {
            ImportGMLString(entry.Name, entry.Code);
        }
    }

    foreach (UMPCodeEntry entry in patches)
    {
        UMPPatchFile patch = new UMPPatchFile(entry.Code, entry.Name, entry.isASM);
        if (patch.RequiresCompilation)
        {
            UMPAddCodeToPatch(patch, entry.Name);
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
                if (entry.isASM)
                {
                    patch.Code = patch.Code + "\n" + command.NewCode;
                }
                else
                {
                    UMPAppendGML(entry.Name, command.NewCode);
                if (patch.RequiresCompilation)
                {
                        UMPAddCodeToPatch(patch, entry.Name);
                    }
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
            
            if (patch.IsASM)
            {
                ImportASMString(entry.Name, patch.Code);
            }            
            else if (patch.RequiresCompilation)
            {
                Data.Code.ByName(entry.Name).ReplaceGML(patch.Code, Data);
            }
        }
    }
}

/// <summary>
/// Add the decompiled code of a code entry to a patch
/// </summary>
/// <param name="patch"></param>
/// <param name="codeName"></param>
void UMPAddCodeToPatch (UMPPatchFile patch, string codeName)
{
    if (patch.IsASM)
    {
        patch.Code = GetDisassemblyText(codeName);
    }
    else
    {
        patch.Code = GetDecompiledText(codeName);
    }
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

    public bool IsASM { get; set; }

    public UMPPatchFile (string gmlCode, string entryName, bool isASM)
    {
        IsASM = isASM;
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
            if (isASM || command.RequiresCompilation)
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

    public bool isASM { get; set; }

    public UMPCodeEntry (string name, string code, bool isASM = false)
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
/// Handles converting from PASCAL CASE to other cases
/// </summary>
static class UMPCaseConverter
{
    /// <summary>
    /// Convert into a generic case
    /// </summary>
    /// <param name="nameCase">Case to convert to</param>
    /// <param name="name">Name to change the case</param>
    /// <returns>Converted name</returns>
    /// <exception cref="Exception">If giving an unsupported case</exception>
    public static string Convert (NameCase nameCase, string name)
    {
        switch (nameCase)
        {
            case NameCase.CamelCase:
                return ToCamel(name);
            case NameCase.SnakeCase:
                return ToSnake(name);
            case NameCase.ScreamingSnakeCase:
                return ToScreamingSnake(name);
            default:
                throw new Exception($"Unsupported case: {nameCase}");
        }
    }

    /// <summary>
    /// Convert from pascal case to camel case
    /// </summary>
    /// <param name="pascalCase">String in pascal case</param>
    /// <returns>String in camel case</returns>
    public static string ToCamel (string pascalCase)
    {
        return pascalCase.Substring(0, 1).ToLower() + pascalCase.Substring(1);
    }

    /// <summary>
    /// Convert from pascal case to snake case
    /// </summary>
    /// <param name="pascalCase">String in pascal case</param>
    /// <returns>String in snake case</returns>
    public static string ToSnake (string pascalCase)
    {
        string snakeCase = "";
        for (int i = 0; i < pascalCase.Length; i++)
        {
            char c = pascalCase[i];
            if (char.IsUpper(c))
            {
                snakeCase += "_" + char.ToLower(c);
            }
            else
            {
                snakeCase += c;
            }
        }
        if (snakeCase.StartsWith("_"))
        {
            snakeCase = snakeCase.Substring(1);
        }
        return snakeCase;
    }

    /// <summary>
    /// Convert from pascal case to screaming snake case
    /// </summary>
    /// <param name="pascalCase">String in pascal case</param>
    /// <returns>String in screaming snake case</returns>
    public static string ToScreamingSnake (string pascalCase)
    {
        return UMPCaseConverter.ToSnake(pascalCase).ToUpper();
    }

    /// <summary>
    /// Get the name case from the name of the case as supported in the UMP config file
    /// </summary>
    /// <param name="caseName">Name of the case</param>
    /// <returns>Case type</returns>
    /// <exception cref="Exception">If an unknown case name is given</exception>
    public static NameCase CaseFromString (string caseName)
    {
        switch (caseName)
        {
            case "camel-case":
                return NameCase.CamelCase;
            case "snake-case":
                return NameCase.SnakeCase;
            case "screaming-snake-case":
                return NameCase.ScreamingSnakeCase;
            default:
                throw new Exception("Unknown case name: " + caseName);
        }
    }

    /// <summary>
    /// Represents a case for a name
    /// </summary>
    public enum NameCase
    {
        /// <summary>
        /// Case "LikeThis"
        /// </summary>
        PascalCase,
        /// <summary>
        /// Case "likeThis"
        /// </summary>
        CamelCase,
        /// <summary>
        /// Case "like_this"
        /// </summary>
        SnakeCase,
        /// <summary>
        /// Case "LIKE_THIS"
        /// </summary>
        ScreamingSnakeCase
    }
}

