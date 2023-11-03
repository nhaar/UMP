using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json;
using System.Linq;

/// <summary>
/// Wrapper for the IScriptInterface methods
/// </summary>
UMPWrapper UMP_WRAPPER = new UMPWrapper
(
    Data,
    ScriptPath,
    (string name, string code) => { ImportGMLString(name, code); return ""; },
    (string name, string code) => { ImportASMString(name, code); return ""; },
    (string name) => GetDisassemblyText(name),
    (string name) => GetDecompiledText(name)
);

/// <summary>
/// Base class for the GameMaker files (and disassembly files) loaders
/// </summary>
abstract class UMPLoader
{
    /// <summary>
    /// The path to the folder containing the code files, relative to the directory the main script lies in
    /// </summary>
    public abstract string CodePath { get; }

    /// <summary>
    /// Whether the scripts should be imported as global scripts (for GMS 2.3 and higher) or not (lower than GMS 2.3)
    /// </summary>
    public abstract bool UseGlobalScripts { get; }

    /// <summary>
    /// A list of all the defined symbols, if any
    /// </summary>
    public virtual string[] Symbols { get; } = null;

    /// <summary>
    /// A function that takes a path (relative to the main script folder) to a code file and returns the names of the code entries that should be imported from it as an array
    /// </summary>
    /// <param name="filePath">Path to the file, relative to the directory where the main script is</param>
    /// <returns>An array with all the code entries</returns>
    public abstract string[] GetCodeNames (string filePath);
    
    // TO-DO: implement cache
    public virtual bool EnableCache { get; } = false;

    /// <summary>
    /// The IScriptInterface wrapper that is being used
    /// </summary>
    public UMPWrapper Wrapper { get; set; }

    /// <summary>
    /// Creates a new UMPLoader
    /// </summary>
    /// <param name="wrapper">Should be UMP_WRAPPER</param>
    public UMPLoader (UMPWrapper wrapper)
    {
        Wrapper = wrapper;
    }

    /// <summary>
    /// Returns a dictionary with all the enums defined in the loader
    /// </summary>
    /// <returns>
    /// A dictionary with the vakues. It takes the form of two dictionaries nested dictionaries: string -> string -> int, where the first string is the name of the enum, the second string is the name of the enum value and the int is the value of the enum
    /// </returns>
    public Dictionary<string, Dictionary<string, int>> GetEnums ()
    {
        Dictionary<string, Dictionary<string, int>> enumValues = new();
        // going until UMPLoader is equivalent to getting all user defined classes
        for (Type classType = this.GetType(); classType != typeof(UMPLoader); classType = classType.BaseType)
        {
            foreach (Type nestedType in classType.GetNestedTypes())
            {
                if (nestedType.IsEnum)
                {
                    Dictionary<string, int> values = Enum.GetNames(nestedType).ToDictionary(name => name, name => (int)Enum.Parse(nestedType, name));
                    enumValues[nestedType.Name] = values;
                }
            }
        }

        return enumValues;
    }

    /// <summary>
    /// Loads all the code files with all the defined settings for the class
    /// </summary>
    /// <exception cref="UMPException">If an error using the UMP features occurs</exception>
    /// <exception cref="Exception"></exception>
    public void Load ()
    {
        string absoluteCodePath = "";
        try
        {
            absoluteCodePath = Path.Combine(Path.GetDirectoryName(Wrapper.ScriptPath), CodePath);
        }
        catch
        {
            throw new UMPException("Error getting code path");
        }

        string[] files = null;
        string[] searchPatterns = new[] { "*.gml", "*.asm" };
        if (Directory.Exists(absoluteCodePath))
        {
            files = searchPatterns.SelectMany(pattern => Directory.GetFiles(absoluteCodePath, pattern, SearchOption.AllDirectories)).ToArray();
        }
        else
        {
            throw new UMPException("Error getting code files");
        }

        Dictionary<string, string> processedFiles = new();

        // preprocessing code (everything using #)
        foreach (string file in files)
        {
            string code = File.ReadAllText(file);
            // ignoring files
            if (ShouldIgnoreFile(code, file))
            {
                continue;
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
                FunctionsFileParser parser = new(code, file, functions, UseGlobalScripts, Wrapper);
                parser.Parse();
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
                string functionBody = Regex.Match(entry.Code, @"(?<=^[\S\s]*?function[\s\d\w_]+\(.*?\)\s*{)[\s\S]+(?=}\s*$)").Value;
                // if the script was defined without "function"
                if (functionBody == "")
                {
                    functionBody = entry.Code;
                }
                string scriptName = entry.FunctionName;
                string codeName = entry.Name.Replace("gml_GlobalScript", "gml_Script");
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
                patch.AddCode(entry.Name);
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
                            patch.AddCode(entry.Name);
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

    /// <summary>
    /// Whether the file should be ignored or not, based on its code
    /// </summary>
    /// <param name="code">Code of the file</param>
    /// <param name="file">File path, for debugging only</param>
    /// <returns>True if it should be ignored</returns>
    /// <exception cref="UMPException">If the ignore statement is malformed</exception>
    public bool ShouldIgnoreFile (string code, string file)
    {
        if (!Regex.IsMatch(code, @"^///.*?\.ignore"))
        {
            return false;
        }

        string ifPattern = @"(?<=^///.*?\.ignore\s+if\s+)[\d\w_]+";
        string ifndefPattern = ifPattern.Replace("if", "ifndef");
        string positiveCondition = Regex.Match(code, ifPattern).Value;
        string negativeCondition = Regex.Match(code, ifndefPattern).Value;

        if (positiveCondition == negativeCondition && negativeCondition == "")
        {
            throw new UMPException($"Invalid \"ignore\" statement in file: {file}");
        }

        // ignore if the condition is met (based on the symbol)
        return 
        (
            (positiveCondition != "" && Symbols?.Contains(positiveCondition) == true) ||
            (negativeCondition != "" && !Symbols?.Contains(negativeCondition) == true)
        );
    }

    /// <summary>
    /// Process GML code with UMP options
    /// </summary>
    public class CodeProcessor
    {
        /// <summary>
        /// Code before processing
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// The UMPLoader that is being used for the options
        /// </summary>
        public UMPLoader Loader { get; set; }

        /// <summary>
        /// All the enums defined in the loader
        /// </summary>
        public Dictionary<string, Dictionary<string ,int>> Enums { get; set; }

        /// <summary>
        /// All the symbols defined in the loader
        /// </summary>
        public string[] Symbols { get; set; }

        /// <summary>
        /// Current index in the code
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Skip an amount of characters
        /// </summary>
        /// <param name="amount"></param>
        public void Skip (int amount = 1)
        {
            Index += amount;
        }

        /// <summary>
        /// Skip an amount of characters, adding them to the processed code
        /// </summary>
        /// <param name="amount"></param>
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

        /// <summary>
        /// Current character in the code
        /// </summary>
        public char CurrentChar => Code[Index];

        /// <summary>
        /// Skip current string and add it to the processed code
        /// </summary>
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

        /// <summary>
        /// Skip current string
        /// </summary>
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

        /// <summary>
        /// Throw an exception for an unclosed string
        /// </summary>
        /// <exception cref="UMPException"></exception>
        public void ThrowStringException ()
        {
            throw new UMPException("String not closed in code");
        }

        /// <summary>
        /// Whether the index is inbounds or not
        /// </summary>
        public bool Inbounds => Index < Code.Length;

        /// <summary>
        /// Skip a comment and add it to the processed code
        /// </summary>
        public void AddComment ()
        {
            Advance(2);
            while (Inbounds && CurrentChar != '\n')
            {
                Advance();
            }
            Advance();
        }

        /// <summary>
        /// Skip all of current whitespace
        /// </summary>
        public void SkipWhitespace ()
        {
            while (Inbounds && char.IsWhiteSpace(CurrentChar))
            {
                Skip();
            }
        }

        /// <summary>
        /// Skip the current line
        /// </summary>
        public void SkipLine ()
        {
            while (Inbounds && CurrentChar != '\n')
            {
                Skip();
            }
            Skip();
        }

        /// <summary>
        /// Skip the upcoming word and return it
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// The output code from processing
        /// </summary>
        public string ProcessedCode { get; set; }

        /// <summary>
        /// Go through a block of an "if" until its end
        /// </summary>
        /// <param name="condition"></param>
        /// <exception cref="UMPException"></exception>
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

        /// <summary>
        /// Process the start of an #if block
        /// </summary>
        /// <exception cref="UMPException"></exception>
        public void ProcessIfBlock ()
        {
            SkipWhitespace();
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

        /// <summary>
        /// Skip the upcoming word while adding it to the processed code and return it
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Process code for a UMP enum
        /// </summary>
        /// <param name="enumName"></param>
        /// <exception cref="UMPException"></exception>
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

        /// <summary>
        /// Process code for a UMP method
        /// </summary>
        /// <param name="method"></param>
        /// <exception cref="UMPException"></exception>
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
            // closing parenthesis
            Skip();

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

        /// <summary>
        /// Process the code
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Create processor for a code string with given options
        /// </summary>
        /// <param name="code"></param>
        /// <param name="loader"></param>
        public CodeProcessor (string code, UMPLoader loader)
        {
            Code = code;
            Loader = loader;
            Symbols = loader.Symbols;
            Enums = loader.GetEnums();
        }
    }

    /// <summary>
    /// Parses the code for the /// FUNCTIONS files
    /// </summary>
    public class FunctionsFileParser
    {
        /// <summary>
        /// Index in the code
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// Code of the file
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// File path, for debugging only
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// List of all the functions that will be added
        /// </summary>
        public List<UMPFunctionEntry> Functions { get; set; }

        /// <summary>
        /// Should be UMP_WRAPPER
        /// </summary>
        public UMPWrapper Wrapper { get; set; }

        /// <summary>
        /// Whether the functions should be imported as global scripts (for GMS 2.3 and higher) or not (lower than GMS 2.3)
        /// </summary>
        public bool UseGlobalScripts { get; set; }

        /// <summary>
        /// Create parser for given code and options
        /// </summary>
        /// <param name="code"></param>
        /// <param name="file"></param>
        /// <param name="functions"></param>
        /// <param name="useGlobalScripts"></param>
        /// <param name="wrapper"></param>
        public FunctionsFileParser (string code, string file, List<UMPFunctionEntry> functions, bool 
        useGlobalScripts, UMPWrapper wrapper)
        {
            Code = code;
            File = file;
            Functions = functions;
            Index = 0;
            UseGlobalScripts = useGlobalScripts;
            Wrapper = wrapper;
        }

        /// <summary>
        /// Current character in the code
        /// </summary>
        public char CurrentChar => Code[Index];

        /// <summary>
        /// Skip an amount of characters
        /// </summary>
        /// <param name="amount"></param>
        public void Advance (int amount = 1)
        {
            Index += amount;
        }

        /// <summary>
        /// Whether the index is inbounds or not
        /// </summary>
        public bool Inbounds => Index < Code.Length;

        /// <summary>
        /// Parse the code and add all the functions to the list
        /// </summary>
        /// <exception cref="UMPException"></exception>
        public void Parse ()
        {
            while (Index < Code.Length)
            {
                if (Code.Substring(Index).StartsWith("function"))
                {
                    Advance("function".Length);
                
                    int nameStart = Index;
                    while (Inbounds && CurrentChar != '(')
                    {
                        Advance();
                    }
                    if (!Inbounds)
                    {
                        throw new UMPException("Function keyword must be preceded by name and parenthesis, in file: " + File);
                    }
                    string functionName = Code.Substring(nameStart, Index - nameStart).Trim();
                    if (!Regex.IsMatch(functionName, @"[\d][\d\w_]*"))
                    {
                        throw new UMPException("Function name must be a valid variable name, in file: " + File);
                    }

                    List<string> args = ExtractArguments();

                    string functionBody = ExtractFunctionBody();

                    string functionCode = UMPLoader.FunctionsFileParser.CreateFunctionCode(functionName, args, functionBody);

                    string entryName = UseGlobalScripts ? $"gml_GlobalScript_{functionName}" : $"gml_Script_{functionName}";
                    Functions.Add(new UMPFunctionEntry(entryName, functionCode, functionName, false, Wrapper));
                }
                Advance();
            }
        }
    
        /// <summary>
        /// Extract the arguments of the current function
        /// </summary>
        /// <returns></returns>
        /// <exception cref="UMPException"></exception>
        public List<string> ExtractArguments ()
        {
            List<string> args = new();
            int argStart = Index + 1;
            while (Inbounds)
            {
                bool endLoop = CurrentChar == ')';
                if (CurrentChar == ',' || endLoop)
                {
                    string argName = Code.Substring(argStart, Index - argStart).Trim();
                    if (argName != "")
                    {
                        args.Add(argName);
                    }
                    argStart = Index + 1;
                    if (endLoop)
                    {
                        break;
                    }
                }
                Advance();
            }
            if (!Inbounds)
            {
                throw new UMPException("Function arguments not closed, in file: " + File);
            }

            return args;
        }

        /// <summary>
        /// Extract the body of the current function
        /// </summary>
        /// <returns></returns>
        /// <exception cref="UMPException"></exception>
        public string ExtractFunctionBody ()
        {
            while (Inbounds && CurrentChar != '{')
            {
                Advance();
            }
            if (!Inbounds)
            {
                throw new UMPException("Function body not found, in file: " + File);
            }
            // we do want to skip the { (its added in something other function)
            int codeStart = Index + 1;
            int depth = 0;
            do
            {
                if (CurrentChar == '{')
                {
                    depth++;
                }
                else if (CurrentChar == '}')
                {
                    depth--;
                }
                Advance();
            }
            while (Inbounds && depth > 0);
            if (depth > 0)
            {
                throw new UMPException("Function body not closed, in file: " + File);
            }
            // - 1 at the end to remove the last }
            return Code.Substring(codeStart, Index - codeStart - 1);
        }

        /// <summary>
        /// Create the code for a function's script
        /// </summary>
        /// <param name="functionName"></param>
        /// <param name="args"></param>
        /// <param name="functionBody"></param>
        /// <returns></returns>
        public static string CreateFunctionCode (string functionName, List<string> args, string functionBody)
        {
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
                    functionBody = $"var {arg} = argument{j};" + functionBody;
                }
            }
            return $"function {functionName}({string.Join(", ", gmlArgs)}) {{ {functionBody} }}";
        }
    }
    
    /// <summary>
    /// Get the name of the object from its entry name
    /// </summary>
    /// <param name="entryName"></param>
    /// <returns></returns>
    public string GetObjectName (string entryName)
    {
        return Regex.Match(entryName, @"(?<=gml_Object_).*?((?=(_[a-zA-Z]+_\d+))|(?=_Collision))").Value;
    }

    /// <summary>
    /// Create a new game object with the given name
    /// </summary>
    /// <param name="objectName"></param>
    /// <returns></returns>
    public UndertaleGameObject CreateGameObject (string objectName)
    {
        var obj = new UndertaleGameObject();
        obj.Name = Wrapper.Data.Strings.MakeString(objectName);
        Wrapper.Data.GameObjects.Add(obj);

        return obj;
    }

    /// <summary>
    /// Add GML code to the end of a code entry
    /// </summary>
    /// <param name="codeName"></param>
    /// <param name="code"></param>
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

    /// <summary>
    /// Whether the patch is for a disassembly text file or not
    /// </summary>
    public bool IsASM { get; set; }

    /// <summary>
    /// Should be UMP_WRAPPER
    /// </summary>
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

    public void AddCode (string codeName)
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