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

UMPEnumImporter UMP_ENUM_IMPORTER = null;

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
        else if (code.StartsWith("/// IMPORT"))
        {
            string importArg = Regex.Match(code, @"(?<=^///\s*IMPORT[^\S\r\n]*)[\d\w_]+").Value.Trim();
            string fileName = "";
            string codeName = "";
            if (importArg != "" && importArg != ".ignore")
            {
                fileName = importArg;
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

            imports.Add(new UMPCodeEntry(codeName, code, isASM));
        }
        else if (code.StartsWith("/// PATCH"))
        {

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
        UMPImportGML(functionEntry.Name, functionEntry.Code);
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
    
    if (UMPHasCommand(code, "USE ENUM"))
    {
        string enumsDeclaration = Regex.Match(code, @"(?<=USE ENUM).*$", RegexOptions.Multiline).Value.Trim();
        string[] enums = enumsDeclaration.Split(',').Select(s => s.Trim()).ToArray();
        foreach (string enumName in enums)
        {
            if (!UMP_ENUM_IMPORTER.Enums.ContainsKey(enumName))
            {
                Console.WriteLine(UMP_ENUM_IMPORTER.Enums.Keys);
                throw new Exception($"Enum \"{enumName}\" not found in enum file");
            }
            foreach (string enumMember in UMP_ENUM_IMPORTER.Enums[enumName].Keys)
            {
                code = Regex.Replace(code, @$"\b{enumName}.{enumMember}\b", UMP_ENUM_IMPORTER.Enums[enumName][enumMember].ToString());
            }
        }
    }

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

/// <summary>
/// Class handles the system that translates enums from a CSX file to variables useable by UMP GML files
/// </summary>
class UMPEnumImporter
{
    /// <summary>
    /// A dictionary that maps the name of the enum to a dictionary that maps the name of the enum member to its value
    /// </summary>
    public Dictionary<string, Dictionary<string, int>> Enums;

    /// <summary>
    /// Instantiate with enums from a file
    /// </summary>
    /// <param name="enumFile">Path to the CSX file to read</param>
    public UMPEnumImporter (string enumFile)
    {
        Enums = new();
        Tokenizer tokenizer = new Tokenizer(enumFile);
        Parser parser = new(tokenizer);
        foreach (Parser.EnumTree tree in parser.EnumTrees)
        {
            Enums.Add(tree.EnumName, tree.Members);
        }
    }

    /// <summary>
    /// Parser that generates enums from the tokens
    /// </summary>
    public class Parser
    {
        /// <summary>
        /// A structure that represents an enum
        /// </summary>
        public class EnumTree
        {
            /// <summary>
            /// The name of the enum
            /// </summary>
            public string EnumName { get; set; }

            /// <summary>
            /// A dictionary that maps the name of the enum member to its value
            /// </summary>
            public Dictionary<string, int> Members { get; set; }

            /// <summary>
            /// Instantiate with the name of the enum and no members
            /// </summary>
            /// <param name="name"></param>
            public EnumTree (string name)
            {
                EnumName = name;
                Members = new();
            }
        }

        /// <summary>
        /// A list of all the enums parsed
        /// </summary>
        public List<EnumTree> EnumTrees { get; set; }

        /// <summary>
        /// Exception thrown when the parser encounters an unexpected token
        /// </summary>
        public class TokenException : Exception
        {
            /// <summary>
            /// Throw exception
            /// </summary>
            /// <param name="found">Found token</param>
            /// <param name="expected">Expected token</param>
            public TokenException (Tokenizer.TokenType found, Tokenizer.TokenType expected) : base
            (
                $"Unexpected token: {found}, expected {expected}"
            )
            {}
        }

        /// <summary>
        /// Parses results from a tokenizer
        /// </summary>
        /// <param name="tokenizer">Tokenizer that read the CSX file</param>
        /// <exception cref="TokenException">If an unexpected token is found</exception>
        public Parser (Tokenizer tokenizer)
        {
            EnumTrees = new();

            EnumTree currentTree = null;
            int currentEnumValue;

            int i = 0;
            while (i < tokenizer.Tokens.Count)
            {
                Tokenizer.Token token = tokenizer.Tokens[i];
                if (token.Type == Tokenizer.TokenType.Enum)
                {
                    i++;
                    if (tokenizer.Tokens[i].Type != Tokenizer.TokenType.EnumName)
                    {
                        throw new TokenException(token.Type, Tokenizer.TokenType.EnumName);
                    }
                    currentTree = new EnumTree(tokenizer.Tokens[i].Value);
                    currentEnumValue = 0;
                    EnumTrees.Add(currentTree);
                    i++;
                    if (tokenizer.Tokens[i].Type != Tokenizer.TokenType.EnumStart)
                    {
                        throw new TokenException(token.Type, Tokenizer.TokenType.EnumStart);
                    }
                    i++;
                    while (i < tokenizer.Tokens.Count)
                    {
                        token = tokenizer.Tokens[i];
                        if (token.Type == Tokenizer.TokenType.EnumEnd)
                        {
                            i++;
                            break;
                        }
                        else if (token.Type == Tokenizer.TokenType.EnumName)
                        {
                            string memberName = token.Value;
                            i++;
                            if (tokenizer.Tokens[i].Type == Tokenizer.TokenType.EnumEquals)
                            {
                                i++;
                                if (tokenizer.Tokens[i].Type != Tokenizer.TokenType.EnumValue)
                                {
                                    throw new TokenException(token.Type, Tokenizer.TokenType.EnumValue);
                                }
                                currentEnumValue = int.Parse(tokenizer.Tokens[i].Value);
                                currentTree.Members.Add(memberName, currentEnumValue);
                                currentEnumValue++;
                                i++;
                                if (tokenizer.Tokens[i].Type == Tokenizer.TokenType.EnumEnd)
                                {
                                    i++;
                                    break;
                                }
                                if (tokenizer.Tokens[i].Type != Tokenizer.TokenType.Comma)
                                {
                                    throw new TokenException(token.Type, Tokenizer.TokenType.Comma);
                                }
                                i++;
                            }
                            else
                            {
                                currentTree.Members.Add(memberName, currentEnumValue);
                                currentEnumValue++;
                                if (tokenizer.Tokens[i].Type == Tokenizer.TokenType.EnumEnd)
                                {
                                    i++;
                                    break;
                                }
                                if (tokenizer.Tokens[i].Type != Tokenizer.TokenType.Comma)
                                {
                                    throw new TokenException(token.Type, Tokenizer.TokenType.Comma);
                                }
                                i++;
                            }
                        }
                        else
                        {
                            throw new TokenException(token.Type, Tokenizer.TokenType.EnumName);
                        }
                    }
                }
                else
                {
                    throw new TokenException(token.Type, Tokenizer.TokenType.Enum);
                }
            }
        }
    }

    /// <summary>
    /// Creates token from the CSX file for enums
    /// </summary>
    public class Tokenizer
    {
        /// <summary>
        /// All tokens in the CSX file
        /// </summary>
        public List<Token> Tokens;

        /// <summary>
        /// A token in the CSX file
        /// </summary>
        public class Token
        {
            /// <summary>
            /// The type of the token
            /// </summary>
            public TokenType Type { get; set; }

            /// <summary>
            /// The value of the token, if applicable
            /// </summary>
            public string Value { get; set; }

            public Token (TokenType type, string value = "")
            {
                Type = type;
                Value = value;
            }
        }

        /// <summary>
        /// Type of a token in the CSX file
        /// </summary>
        public enum TokenType
        {
            /// <summary>
            /// Keyword "enum" that defines a new enum
            /// </summary>
            Enum,
            /// <summary>
            /// Any word, though it is expected to be the name of an enum or enum member
            /// </summary>
            EnumName,
            /// <summary>
            /// Opening brace
            /// </summary>
            EnumStart,
            /// <summary>
            /// Equal sign
            /// </summary>
            EnumEquals,
            /// <summary>
            /// Any number, though it is expected to be the value of an enum member
            /// </summary>
            EnumValue,
            /// <summary>
            /// Comma
            /// </summary>
            Comma,
            /// <summary>
            /// Closing brace
            /// </summary>
            EnumEnd
        }

        /// <summary>
        /// Instantiate and create tokens
        /// </summary>
        /// <param name="code">CSX code with enums</param>
        public Tokenizer (string code)
        {
            Tokens = new();
            int i = 0;
            while (i < code.Length)
            {
                char c = code[i];
                if (c == 'e')
                {
                    if (code.Substring(i, 4) == "enum")
                    {
                        Tokens.Add(new Token(TokenType.Enum));
                        i += 4;
                    }
                }
                else if (c == ' ')
                {
                    i++;
                }
                else if (c == '{')
                {
                    Tokens.Add(new Token(TokenType.EnumStart));
                    i++;
                }
                else if (c == '}')
                {
                    Tokens.Add(new Token(TokenType.EnumEnd));
                    i++;
                }
                else if (c == '=')
                {
                    Tokens.Add(new Token(TokenType.EnumEquals));
                    i++;
                }
                else if (c == ',')
                {
                    Tokens.Add(new Token(TokenType.Comma));
                    i++;
                }
                else if (char.IsLetter(c))
                {
                    int start = i;
                    string value = "";

                    while (char.IsLetterOrDigit(code[i]))
                    {
                        value += code[i];
                        i++;
                    }
                    Tokens.Add(new Token(TokenType.EnumName, value));
                }
                else if (char.IsDigit(c))
                {
                    int start = i;
                    string value = "";
                    while (char.IsDigit(code[i]))
                    {
                        value += code[i];
                        i++;
                    }
                    Tokens.Add(new Token(TokenType.EnumValue, value));
                }
                else
                {
                    i++;
                }
            }
        }
    }
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

