using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json;
using System.Linq;

UMPWrapper UMP_WRAPPER = new UMPWrapper
(
    Data,
    ScriptPath,
    (string name, string code) => { ImportGMLString(name, code); return ""; },
    (string name, string code) => { ImportASMString(name, code); return ""; },
    (string name) => GetDisassemblyText(name),
    (string name) => GetDecompiledText(name)
);

abstract class UMPLoader
{
    public abstract string CodePath { get; }
    public abstract bool UseGlobalScripts { get; }
    public virtual string[] Symbols { get; } = null;
    public virtual bool EnableCache { get; } = false;
    public abstract string[] GetCodeNames (string filePath);

    public UMPWrapper Wrapper { get; set; }
    public UMPLoader (UMPWrapper wrapper)
    {
        Wrapper = wrapper;
    }

    public Dictionary<string, Dictionary<string, int>> GetEnums ()
    {
        Dictionary<string, Dictionary<string, int>> enumValues = new();
        Type classType = this.GetType();
        foreach (Type nestedType in classType.GetNestedTypes())
        {
            if (nestedType.IsEnum)
            {
                Dictionary<string, int> values = Enum.GetNames(nestedType).ToDictionary(name => name, name => (int)Enum.Parse(nestedType, name));
                enumValues[nestedType.Name] = values;
            }
        }

        return enumValues;
    }

    public void Load ()
    {
        string[] searchPatterns = new[] { "*.gml", "*.asm" };
        string scriptDir = Path.GetDirectoryName(Wrapper.ScriptPath);

        string absoluteCodePath = "";
        try
        {
            absoluteCodePath = Path.Combine(scriptDir, CodePath);
        }
        catch
        {
            throw new UMPException("Error getting code path");
        }
        string[] files = null;
        try
        {
            files = searchPatterns.SelectMany(pattern => Directory.GetFiles(absoluteCodePath, pattern, SearchOption.AllDirectories)).ToArray();
        }
        catch
        {
            throw new UMPException("Error getting code files");
        }

        Dictionary<string, string> processedFiles = new();

        // preprocessing
        foreach (string file in files)
        {
            string code = File.ReadAllText(file);
            // ignoring files
            if (Regex.IsMatch(code, @"^///.*?\.ignore"))
            {
                string ifPattern = @"(?<=^///.*?\.ignore\s+if\s+)[\d\w_]+";
                string ifndefPattern = ifPattern.Replace("if", "ifndef");
                string positiveCondition = Regex.Match(code, ifPattern).Value;
                string negativeCondition = Regex.Match(code, ifndefPattern).Value;
                if (positiveCondition == negativeCondition && negativeCondition == "")
                {
                    throw new UMPException($"Invalid \"ignore\" statement in file: {file}");
                }
                // ignore if the condition is met (based on the symbol)
                if
                (
                    (positiveCondition != "" && (Symbols?.Contains(positiveCondition) ?? false)) ||
                    (negativeCondition != "" && (!Symbols?.Contains(negativeCondition) ?? true))
                )
                {
                    continue;
                }
            }

            UMPLoader.CodeProcessor processor = new(code, this);
            string processedCode = processor.Preprocess();
            string relativePath = Path.GetRelativePath(absoluteCodePath, file);
            processedFiles[relativePath] = processedCode;
        }

        List<UMPFunctionEntry> functions = new();
        List<UMPCodeEntry> imports = new();
        List<UMPCodeEntry> patches = new();

        // post processing
        foreach (string file in processedFiles.Keys)
        {
            string code = processedFiles[file];
            // "opening" function files
            if (code.StartsWith("/// FUNCTIONS"))
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
                            while (i < code.Length && code[i] != '(')
                            {
                                i++;
                            }
                            if (i >= code.Length)
                            {
                                throw new UMPException("Function keyword must be preceded by name and parenthesis, in file: " + file);
                                break;
                            }
                            string functionName = code.Substring(nameStart, i - nameStart).Trim();
                            if (!Regex.IsMatch(functionName, @"[\d][\d\w_]*"))
                            {
                                throw new UMPException("Function name must be a valid variable name, in file: " + file);
                                break;
                            }
                            List<string> args = new();
                            nameStart = i + 1;
                            while (i < code.Length && true)
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
                            if (i >= code.Length)
                            {
                                throw new UMPException("Function arguments not closed, in file: " + file);
                                break;
                            }
                            while (i < code.Length && code[i] != '{')
                            {
                                i++;
                            }
                            if (i >= code.Length)
                            {
                                throw new UMPException("Function body not found, in file: " + file);
                                break;
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
                            while (i < code.Length && depth > 0);
                            if (i >= code.Length)
                            {
                                throw new UMPException("Function body not closed, in file: " + file);
                                break;
                            }
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
                            string entryName = UseGlobalScripts ? $"gml_GlobalScript_{functionName}" : $"gml_Script_{functionName}";
                            functions.Add(new UMPFunctionEntry(entryName, functionCodeBlock, functionName, false, Wrapper));
                        }
                    }
                    i++;
                }
            }
            else if (Regex.IsMatch(code, @"^/// (IMPORT|PATCH)"))
            {
                string[] codeEntries = GetCodeNames(file);
                bool isASM = file.EndsWith(".asm");

                for (int i = 0; i < codeEntries.Length; i++)
                {
                    if (UseGlobalScripts)
                    {
                        codeEntries[i] = codeEntries[i].Replace("gml_Script", "gml_GlobalScript");
                    }
                    else
                    {
                        codeEntries[i] = codeEntries[i].Replace("gml_GlobalScript", "gml_Script");
                    }

                }

                foreach (string codeName in codeEntries)
                {
                    UMPCodeEntry codeEntry = new UMPCodeEntry(codeName, code, isASM, Wrapper);
                    if (code.StartsWith("/// PATCH"))
                    {
                        patches.Add(codeEntry);
                    }
                    else
                    {
                        if (codeName.Contains("gml_GlobalScript") || codeName.Contains("gml_Script"))
                        {
                            string functionName = Regex.Match(codeName, @"(?<=(gml_Script_|gml_GlobalScript_))[_\d\w]+").Value;

                            functions.Add(new UMPFunctionEntry(codeName, code, functionName, isASM, Wrapper));
                        }
                        else
                        {
                            // loader only supports scripts and objects... if not script, then it's an object
                            string objName = GetObjectName(codeName);
                            if (objName != "" && Wrapper.Data.GameObjects.ByName(objName) == null)
                            {
                                CreateGameObject(objName);
                            }
                            imports.Add(codeEntry);
                        }
                    }
                }
            }
            else
            {
                throw new UMPException($"File \"{file}\" does not have a valid UMP type");
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

        foreach (UMPFunctionEntry entry in functionsInOrder)
        {
            if (UseGlobalScripts)
            {
                entry.Import();
            }
            else
            {
                string functionBody = Regex.Match(entry.Code, @"(?<=^\s*function[\s\d\w_]+\(.*?\)\s*{)[\s\S]+(?=}\s*$)").Value;
                // if the script was defined without "function"
                if (functionBody == "")
                {
                    functionBody = entry.Code;
                }
                string scriptName = entry.FunctionName;
                string codeName = entry.Name.Replace("gml_GlobalScript", "gml_Script");
                UndertaleCode scriptCode = null;
                if (Wrapper.Data.Scripts.ByName(scriptName) == null)
                {
                    Wrapper.ImportGMLString(codeName, functionBody);
                }
                else
                {
                    Wrapper.Data.Code.ByName(codeName).ReplaceGML(functionBody, Wrapper.Data);
                }
            }
        }

        foreach (UMPCodeEntry entry in imports)
        {
            entry.Import();
        }

        foreach (UMPCodeEntry entry in patches)
        {
            UMPPatchFile patch = new UMPPatchFile(entry.Code, entry.Name, entry.IsASM, Wrapper);
            if (patch.RequiresCompilation)
            {
                patch.UMPAddCodeToPatch(entry.Name);
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
                    if (command.OriginalCode == "")
                    {
                        throw new UMPException("Error in patch file: Replace command requires code to be specified (empty string found)");
                    }
                    patch.Code = patch.Code.Replace(command.OriginalCode, command.NewCode);
                }
                else if (command is UMPAppendCommand)
                {
                    if (entry.IsASM)
                    {
                        patch.Code = patch.Code + "\n" + command.NewCode;
                    }
                    else
                    {
                        try
                        {
                            AppendGML(entry.Name, command.NewCode);
                        }
                        catch
                        {
                            throw new UMPException($"Error appending code to entry \"{entry.Name}\"");
                        }
                        if (patch.RequiresCompilation)
                        {
                            patch.UMPAddCodeToPatch(entry.Name);
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

                try
                {
                    if (patch.IsASM)
                    {
                        Wrapper.ImportASMString(entry.Name, patch.Code);
                    }            
                    else if (patch.RequiresCompilation)
                    {
                        Wrapper.Data.Code.ByName(entry.Name).ReplaceGML(patch.Code, Wrapper.Data);
                    }
                }
                catch
                {
                    throw new UMPException($"Error patching code entry \"{entry.Name}\"");
                }
            }
        }
    }

    public class CodeProcessor
    {
        public string Code { get; set; }

        public UMPLoader Loader { get; set; }

        public Dictionary<string, Dictionary<string ,int>> Enums { get; set; }

        public string[] Symbols { get; set; }

        public int Index { get; set; }

        public void Skip (int amount = 1)
        {
            Index += amount;
        }

        public void Advance (int amount = 1)
        {
            int i = 0;
            while (Inbounds && i < amount)
            {
                ProcessedCode += CurrentChar;
                Index++;
                i++;
            }
        }

        public char CurrentChar => Code[Index];

        public void AddString ()
        {
            Advance();
            while (Inbounds && CurrentChar != '"')
            {
                if (CurrentChar == '\\')
                {
                    Advance();
                }
                Advance();
            }
            if (!Inbounds)
            {
                ThrowStringException();
            }
            Advance();
        }

        public void SkipString ()
        {
            Skip();
            while (CurrentChar != '"')
            {
                if (CurrentChar == '\\')
                {
                    Skip();
                }
                Skip();
            }
            if (!Inbounds)
            {
                ThrowStringException();   
            }
            Skip();
        }

        public void ThrowStringException ()
        {
            throw new UMPException("String not closed in code");
        }

        public bool Inbounds => Index < Code.Length;

        public void AddComment ()
        {
            Advance(2);
            while (Inbounds && CurrentChar != '\n')
            {
                Advance();
            }
            Advance();
        }

        public void SkipWhitespace ()
        {
            while (Inbounds && char.IsWhiteSpace(CurrentChar))
            {
                Skip();
            }
        }

        public void SkipToLineEnd ()
        {
            while (Inbounds && CurrentChar != '\n')
            {
                Skip();
            }
        }


        public void SkipLine ()
        {
            SkipToLineEnd();
            Skip();
        }

        

        public string SkipWordAhead ()
        {
            string word = "";
            while (Inbounds && char.IsLetterOrDigit(CurrentChar) || CurrentChar == '_')
            {
                word += CurrentChar;
                Skip();
            }
            return word;
        }

        public string ProcessedCode { get; set; }

        public void TraverseIfBlock (bool condition)
        {
            while (Inbounds && CurrentChar != '#')
            {
                if (condition)
                {
                    Advance();
                }
                else
                {
                    Skip();
                }
            }
            if (!Inbounds)
            {
                throw new UMPException("Preprocessing if block not closed in code");
            }
            if (Code.Substring(Index + 1, 5) == "endif")
            {
                SkipLine();
            }
            // ADD MORE OPTIONS HERE LATER 
            else
            {
                TraverseIfBlock(condition);
            }
        }

        public void ProcessIfBlock ()
        {
            string symbol = SkipWordAhead();
            if (symbol == "")
            {
                throw new UMPException("No symbol found after if keyword");
            }
            SkipLine();
            bool condition = Symbols?.Contains(symbol) ?? false;
            SkipWhitespace();
            TraverseIfBlock(condition);
        }

        public string ReadWordAhead ()
        {
            int i = Index;
            string word = "";
            while (Inbounds && char.IsLetterOrDigit(CurrentChar) || CurrentChar == '_')
            {
                word += CurrentChar;
                Skip();
            }
            Index = i;
            return word;
        }

        public void ProcessEnum (string enumName)
        {
            if (!Enums.ContainsKey(enumName))
            {
                throw new UMPException($"Enum \"{enumName}\" not found");
            }
            string word;
            // accessing enum property
            if (CurrentChar == '#')
            {
                Skip();
                word = ReadWordAhead();
                if (word == "length")
                {
                    ProcessedCode += Enums[enumName].Count.ToString();
                }
                else
                {
                    throw new UMPException("Invalid UMP enum property in code");
                }
            }
            else
            {
                word = ReadWordAhead();
                if (Enums[enumName].ContainsKey(word))
                {
                    ProcessedCode += Enums[enumName][word].ToString();
                }
                else
                {
                    throw new UMPException($"Enum value \"{word}\" not found in enum \"{enumName}\"");
                }
            }
            Skip(word.Length);
        }

        public void ProcessMethod (string method)
        {
            List<object> methodArgs = new();
            // TO-DO: require arguments to be comma separated
            while (Inbounds && CurrentChar != ')')
            {
                if (CurrentChar == '"')
                {
                    int start = Index;
                    SkipString();
                    string str = Code.Substring(start + 1, Index - start - 2);
                    methodArgs.Add(str);
                }
                // add support for more types: mainly INT and float
                else
                {
                   Skip();
                }
            }
            if (!Inbounds)
            {
                throw new UMPException("UMP Method not closed in code");
            }

            // add error if wrong arg count, types and etc
            MethodInfo methodInfo = Loader.GetType().GetMethod(method);
            if (methodInfo == null)
            {
                throw new UMPException($"UMP Method \"{method}\" not found");
            }
            else
            {
                try
                {
                    object result = methodInfo.Invoke(Loader, methodArgs.ToArray());
                    ProcessedCode += (string)result;
                }
                catch
                {
                    throw new UMPException($"UMP Method \"{method}\" failed");
                }
            }
        }

        public string Preprocess ()
        {
            ProcessedCode = "";
            while (Inbounds)
            {
                switch (CurrentChar)
                {
                    case '"':
                    {
                        AddString();
                        break;
                    }
                    case '/':
                    {
                        if (Code[Index + 1] == '/')
                        {
                            AddComment();
                        }
                        else
                        {
                            Advance();
                        }
                        break;
                    }
                    case '#':
                    {
                        Skip(1);
                        string word = ReadWordAhead();
                        if (word == "if")
                        {
                            Skip(2);
                            ProcessIfBlock();
                        }
                        else
                        {
                            char afterWord = Code[Index + word.Length];
                            // enums
                            if (afterWord == '.')
                            {
                                Skip(word.Length + 1);
                                ProcessEnum(word);
                            }
                            // user methods
                            else if (afterWord == '(')
                            {
                                Skip(word.Length + 1);
                                ProcessMethod(word);
                            }
                        }
                        break;
                    }
                    default:
                    {
                        Advance();
                        break;
                    }
                }
            }

            return ProcessedCode;
        }

        public CodeProcessor (string code, UMPLoader loader)
        {
            Code = code;
            Loader = loader;
            Symbols = loader.Symbols;
            Enums = loader.GetEnums();
        }
    }
    
    public string GetObjectName (string entryName)
    {
        return Regex.Match(entryName, @"(?<=gml_Object_).*?((?=(_[a-zA-Z]+_\d+))|(?=_Collision))").Value;
    }

    public UndertaleGameObject CreateGameObject (string objectName)
    {
        var obj = new UndertaleGameObject();
        obj.Name = Wrapper.Data.Strings.MakeString(objectName);
        Wrapper.Data.GameObjects.Add(obj);

        return obj;
    }

    public void AppendGML (string codeName, string code)
    {
        Wrapper.Data.Code.ByName(codeName).AppendGML(code, Wrapper.Data);
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

    public UMPWrapper Wrapper { get; set; }

    public UMPPatchFile (string gmlCode, string entryName, bool isASM, UMPWrapper wrapper)
    {
        IsASM = isASM;
        Wrapper = wrapper;
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
                            throw new UMPException($"Error in patch file \"{entryName}\": Expected CODE command");
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
                        throw new UMPException($"Error in patch file \"{entryName}\": Expected END command");
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
                        Console.WriteLine($"WARNING: Unknown command ({line}) in patch file: {entryName}");
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

    public void UMPAddCodeToPatch (string codeName)
    {
        try
        {
            if (IsASM)
            {
                // necessary due to linebreak whitespace inconsistency
                Code = Wrapper.GetDisassemblyText(codeName).Replace("\r", "");
            }
            else
            {
                Code = Wrapper.GetDecompiledText(codeName);
            }
        }
        catch
        {
            throw new UMPException($"Error decompiling code entry \"{codeName}\"");
        }
    }
}

/// <summary>
/// Append GML to the end of a code entry
/// </summary>
/// <param name="codeName"></param>
/// <param name="code"></param>


/// <summary>
/// Create a game object with the given name
/// </summary>
/// <param name="objectName"></param>
/// <returns></returns>


/// <summary>
/// Represents a code entry that will be added
/// </summary>
public class UMPCodeEntry
{
    public string Name { get; set; }
    public string Code { get; set; }

    public bool IsASM { get; set; }

    public UMPWrapper Wrapper { get; set; }

    public UMPCodeEntry (string name, string code, bool isASM = false, UMPWrapper wrapper = null)
    {
        Name = name;
        Code = code;
        IsASM = isASM;
        Wrapper = wrapper;
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

    public void Import ()
    {
        if (IsASM)
        {
            Wrapper.ImportASMString(Name, Code);
        }
        else
        {
            Wrapper.ImportGMLString(Name, Code);
        }
    }
}

/// <summary>
/// Represents a code entry that is a function
/// </summary>
class UMPFunctionEntry : UMPCodeEntry
{
    public string FunctionName { get; set; }

    public UMPFunctionEntry (string name, string code, string functionName, bool isASM, UMPWrapper wrapper) : base(name, code, isASM, wrapper)
    {
        FunctionName = functionName;
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
        if (!IsPascalCase(name))
        {
            throw new UMPException($"Original case must be pascal case for name \"{name}\"");
        }
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

    public static bool IsPascalCase(string str)
    {
        return Regex.IsMatch(str, @"^[A-Z][a-zA-Z0-9]*$");
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

public class UMPException : Exception
{
    public UMPException(string message) : base(message)
    {
    }
}

public class UMPWrapper
{
    public UndertaleData Data;

    public string ScriptPath;

    public Func<string, string, string> ImportGMLString;


    public Func<string, string, string> ImportASMString;

    public Func<string, string> GetDisassemblyText;

    public Func<string, string> GetDecompiledText;

    public UMPWrapper
    (
        UndertaleData data,
        string scriptPath,
        Func<string, string, string> importGMLString,
        Func<string, string, string> importASMString,
        Func<string, string> getDisassemblyText,
        Func<string, string> getDecompiledText
    )
    {
        Data = data;
        ScriptPath = scriptPath;
        ImportGMLString = importGMLString;
        ImportASMString = importASMString;
        GetDisassemblyText = getDisassemblyText;
        GetDecompiledText = getDecompiledText;
    }
}