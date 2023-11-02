using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Newtonsoft.Json;
using System.Linq;

UMPWrapper UMP_WRAPPER = new UMPWrapper
(
    Data,
    ScriptPath,
    (string name) => UMPGetObjectName(name),
    (string name) => { UMPCreateGMSObject(name); return ""; },
    (UMPCodeEntry name) => { UMPImportCodeEntry(name); return ""; },
    (string name, string code) => { ImportGMLString(name, code); return ""; },
    (string name, string code) => { UMPAppendGML(name, code); return ""; },
    (string name, string code) => { ImportASMString(name, code); return ""; },
    (string name) => { GetDisassemblyText(name); return ""; },
    (string name) => { GetDecompiledText(name); return ""; }
);

    // string modPath = null, -> CodePath
    // string codeNameWithExtension = null, -> not supported
    // string codeString = null, -> not supported
    // Type[] enums = null, -> all enums defined within the class will be automatically retrieved
    // bool convertCase = false, -> dropped
    // UMPCaseConverter.NameCase enumNameCase = UMPCaseConverter.NameCase.PascalCase,
    // UMPCaseConverter.NameCase enumMemberCase = UMPCaseConverter.NameCase.PascalCase,
    // string[] objectPrefixes = null, -> dropped! (use should implement this themselves)
    // bool useIgnore = true, -> DROPPED! ignore is always on now.
    // string[] symbols = null, ->
    // bool useFunctions = true

abstract class UMPLoader
{
    public UMPWrapper Wrapper { get; set; }

    public string CodePath { get; set; }

    public UMPCaseConverter.NameCase EnumNameCase { get; set; }

    public UMPCaseConverter.NameCase EnumMemberCase { get; set; }

    public string[] Symbols { get; set; }

    public bool UseGlobalScripts { get; set; }

    public bool EnableCache { get; set; }

    public UMPLoader (UMPWrapper wrapper)
    {
        Wrapper = wrapper;
    }

    public abstract string[] GetCodeNames (string filePath);

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
        string[] files = searchPatterns.SelectMany(pattern => Directory.GetFiles(CodePath, pattern, SearchOption.AllDirectories)).ToArray();

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
                    // ADD ERROR LATER: needs to specify condition
                }
                // ignore if the condition is met (based on the symbol)
                if
                (
                    (positiveCondition != "" && Symbols.Contains(positiveCondition)) ||
                    (negativeCondition != "" && !Symbols.Contains(negativeCondition))
                )
                {
                    continue;
                }
            }

            UMPLoader.CodeProcessor processor = new(code, this);
            string processedCode = processor.Preprocess();
            string relativePath = Path.GetRelativePath(CodePath, file);
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
                try
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
                                string entryName = UseGlobalScripts ? $"gml_GlobalScript_{functionName}" : $"gml_Script_{functionName}";
                                functions.Add(new UMPFunctionEntry(entryName, functionCodeBlock, functionName, false));
                            }
                        }
                        i++;
                    }
                }
                catch (System.Exception e)
                {                
                    Console.WriteLine(new UMPException(16, $"Error processing functions in file \"{file}\""));
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
                    UMPCodeEntry codeEntry = new UMPCodeEntry(codeName, code, isASM);
                    if (code.StartsWith("/// PATCH"))
                    {
                        patches.Add(codeEntry);
                    }
                    else
                    {
                        if (codeName.Contains("gml_GlobalScript") || codeName.Contains("gml_Script"))
                        {
                            string functionName = Regex.Match(codeName, @"(?<=(gml_Script_|gml_GlobalScript_))[_\d\w]+").Value;

                            functions.Add(new UMPFunctionEntry(codeName, code, functionName, isASM));
                        }
                        else
                        {
                            // loader only supports scripts and objects... if not script, then it's an object
                            string objName = Wrapper.UMPGetObjectName(codeName);
                            if (objName != "" && Wrapper.Data.GameObjects.ByName(objName) == null)
                            {
                                Wrapper.UMPCreateGMSObject(objName);
                            }
                            imports.Add(codeEntry);
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine(new UMPException(9, $"File \"{file}\" does not have a valid UMP type"));
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
                Wrapper.UMPImportCodeEntry(entry);
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
            Wrapper.UMPImportCodeEntry(entry);
        }

        foreach (UMPCodeEntry entry in patches)
        {
            UMPPatchFile patch = new UMPPatchFile(entry.Code, entry.Name, entry.IsASM, Wrapper);
            if (patch.RequiresCompilation)
            {
                patch.UMPAddCodeToPatch(patch, entry.Name);
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
                    if (entry.IsASM)
                    {
                        patch.Code = patch.Code + "\n" + command.NewCode;
                    }
                    else
                    {
                        try
                        {
                            Wrapper.UMPAppendGML(entry.Name, command.NewCode);
                            
                        }
                        catch (System.Exception)
                        {
                            Console.WriteLine(new UMPException(15, $"Error appending code to entry \"{entry.Name}\""));
                        }
                        if (patch.RequiresCompilation)
                        {
                            patch.UMPAddCodeToPatch(patch, entry.Name);
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
                catch (Exception)
                {
                    Console.WriteLine(new UMPException(14, "Error importing code entry \"" + entry.Name + "\""));
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
            while (CurrentChar != '"')
            {
                if (CurrentChar == '\\')
                {
                    Advance();
                }
                Advance();
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
            Skip();
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
            // ADD error for no symbol
            SkipLine();
            bool condition = Symbols.Contains(symbol);
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
                    // ADD ERROR HERE LATER and more
                }
            }
            // add error for having no name here
            else
            {
                word = ReadWordAhead();
                if (Enums[enumName].ContainsKey(word))
                {
                    ProcessedCode += Enums[enumName][word].ToString();
                }
                else
                {
                    // ADD ERROR HERE LATER
                }
            }
            Skip(word.Length);
        }

        public void ProcessMethod (string method)
        {
            List<object> methodArgs = new();
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

            // add error if wrong arg count, types and etc
            MethodInfo methodInfo = Loader.GetType().GetMethod(method);
            if (methodInfo == null)
            {
                // ADD ERROR HERE LATER
            }
            else
            {
                object result = methodInfo.Invoke(Loader, methodArgs.ToArray());
                ProcessedCode += (string)result;
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
}

class TestLoader : UMPLoader
{
    public TestLoader (UMPWrapper wrapper) : base(wrapper)
    {
    }

    public override string[] GetCodeNames (string filePath)
    {
        throw new NotImplementedException();
    }

    public string TestMethod (string arg1, string arg2)
    {
        return arg1 + arg2;
    }

    public enum TestEnum
    {
        Test1,
        Test2,
        Test3
    }
}

var testLoader = new TestLoader(UMP_WRAPPER);
var test = new UMPLoader.CodeProcessor(@"/// PATCH .ignore

// removing rectangle near the chasing enemies
/// REPLACE
#if DEMO
if scr_debug_ch1()
#endif
#if SURVEY_PROGRAM
if scr_debug()
#endif
{
    show_debug_message(#TestMethod(""a"", ""b"")))
    show_debug_message(#TestEnum.#length)
    draw_set_color(c_red)
    draw_rectangle((targetx - 2), (targety - 2), (targetx + 2), (targety + 2), false)
    draw_set_color(c_aqua)
    draw_rectangle(((targetx - 2) + (sprite_width / 2)), ((targety - 2) + (sprite_height / 2)), ((targetx + 2) + (sprite_width / 2)), ((targety + 2) + (sprite_height / 2)), false)
}
/// CODE
/// END",  testLoader);
Console.WriteLine("YEY;");
Console.WriteLine(test.Preprocess());

/// <summary>
/// Loads GML or ASM code, through a file or a string, and imports it into the game using the UMP processing options
/// </summary>
/// <param name="modPath">If given, will look at all the files inside the directory</param>
/// <param name="codeNameWithExtension">If given, will import the code with the given name. Extensions .gml and .asm are required</param>
/// <param name="codeString">If given together with codeNameWithExtension, will be the code content imported</param>
/// <param name="enums">An array of enum types that will be imported to use in GML</param>
/// <param name="convertCase">Whether the enum cases should be converted or not</param>
/// <param name="enumNameCase">If given, will convert the enum names from pascal case to the given case</param>
/// <param name="enumMemberCase">If given, will convert the enum members from pascal case to the given case</param>
/// <param name="objectPrefixes">An array of all the object prefixes</param>
/// <param name="useIgnore">Whether or not .ignore should make a file be ignored or not</param>
/// <param name="symbols">An array with all pre-defined symbols to give the code</param>
/// <returns>A dictionary mapping code names to their code</returns>
/// <exception cref="Exception"></exception>
// Dictionary<string, string> UMPLoad
// (
//     string modPath = null,
//     string codeNameWithExtension = null,
//     string codeString = null,
//     Type[] enums = null,
//     bool convertCase = false,
//     UMPCaseConverter.NameCase enumNameCase = UMPCaseConverter.NameCase.PascalCase,
//     UMPCaseConverter.NameCase enumMemberCase = UMPCaseConverter.NameCase.PascalCase,
//     string[] objectPrefixes = null,
//     bool useIgnore = true,
//     string[] symbols = null,
//     bool useFunctions = true
// )
// {
//     Dictionary<string, string> originalCode = new();
//     if (modPath != null && codeNameWithExtension != null)
//     {
//         Console.WriteLine(new UMPException(1, "Cannot specify both code via file and via string"));
//     }
//     else if (codeNameWithExtension == null)
//     {
//         string[] allFiles = null;
//         string searchPath = Path.Combine(Path.GetDirectoryName(ScriptPath), modPath);
//         if (File.Exists(searchPath))
//         {
//             if (!Regex.IsMatch(searchPath, @"\.(gml)|(asm)$"))
//             {
//                 Console.WriteLine(new UMPException(2, $"File \"{searchPath}\" is not a .gml or .asm file"));
//             }
//             allFiles = new string[] { searchPath };
//         }
//         else if (Directory.Exists(searchPath))
//         {
//             string[] searchPatterns = new[] { "*.gml", "*.asm" };
//             allFiles = searchPatterns.SelectMany(pattern => Directory.GetFiles(searchPath, pattern, SearchOption.AllDirectories)).ToArray();
//         }
//         else
//         {
//             Console.WriteLine(new UMPException(3, $"Mod path \"{searchPath}\" does not exist"));
//         }

//         foreach (string file in allFiles)
//         {
//             string code = File.ReadAllText(file);
//             originalCode[file] = code;
//         }
//     }
//     else
//     {
//         if (codeString == null)
//         {
//             Console.WriteLine(new UMPException(4, "Code string must be given if code name is given"));
//         }
//         if (!Regex.IsMatch(codeNameWithExtension, @"\.(gml)|(asm)$"))
//         {
//             Console.WriteLine(new UMPException(5, $"Code name given without extension or with invalid extension: \"{codeNameWithExtension}\""));
//         }
//         originalCode[codeNameWithExtension] = codeString;
//     }

//     // computing enum related arguments
//     Dictionary<string, Dictionary<string, int>> enumValues = new();

//     foreach (Type enumType in enums ?? new Type[0])
//     {
//         var values = Enum.GetNames(enumType).ToDictionary(name => name, name => (int)Enum.Parse(enumType, name));
//         enumValues[enumType.Name] = values;
//     }

//     if (convertCase)
//     {
//         if (enumNameCase != UMPCaseConverter.NameCase.PascalCase)
//         {
//             string[] keys = enumValues.Keys.ToArray();
//             foreach (string enumName in keys)
//             {
//                 string newName = UMPCaseConverter.Convert(enumNameCase, enumName);
//                 enumValues.Add(newName, enumValues[enumName]);
//                 enumValues.Remove(enumName);
//             }
//         }
//         if (enumMemberCase != UMPCaseConverter.NameCase.PascalCase)
//         {
//             foreach (string enumName in enumValues.Keys.ToList())
//             {
//                 enumValues[enumName] = enumValues[enumName]
//                     .ToDictionary(pair => UMPCaseConverter.Convert(enumMemberCase, pair.Key), pair => pair.Value);
//             }
//         }
//     }

//     // storing to be added entries
//     List<UMPFunctionEntry> functions = new();
//     List<UMPCodeEntry> imports = new();
//     List<UMPCodeEntry> patches = new();
    
//     Dictionary<string, string> unprocessedCode = new();
//     Dictionary<string, string> processedCode = new();

//     List<string> symbolList = symbols?.ToList() ?? new List<string>();

//     // code preprocessing
//     foreach (string file in originalCode.Keys)
//     {
//         // check if need to create objects
//         // needs to be done here otherwise
//         // calling the obj names in scripts will not work
//         string codeName = Path.GetFileNameWithoutExtension(file);
//         bool isObject = codeName.StartsWith("gml_Object_");
//         if (!isObject)
//         {
//             foreach (string prefix in objectPrefixes ?? new string[0])
//             {
//                 if (codeName.StartsWith(prefix))
//                 {
//                     codeName = $"gml_Object_{codeName}";
//                     isObject = true;
//                     break;
//                 }
//             }
//         }

//         string objName = UMPGetObjectName(codeName);
//         if (objName != "" && Data.GameObjects.ByName(objName) == null)
//         {
//             UMPCreateGMSObject(objName);
//         }

//         // getting symbols and saving code without defines
//         string code = originalCode[file];
//         MatchCollection foundSymbols = Regex.Matches(code, @"(?<=^#define\s+)[\w\d_]+", RegexOptions.Multiline);
//         symbolList.AddRange(foundSymbols.Cast<Match>().Select(m => m.Value).ToList());
//         Regex definePattern = new Regex(@"#define\s+[\w\d_]+\s*?\n");
//         unprocessedCode[file] = definePattern.Replace(code, "");        
//     }

//     // code that will be given back to the user
//     Dictionary<string, string> exportedCode = new();

//     // code processing
//     foreach (string file in unprocessedCode.Keys)
//     {
//         string code = unprocessedCode[file];
//         MatchCollection ifSymbol = Regex.Matches(code, @"(?<=^#if\s+)[\w\d_]+", RegexOptions.Multiline);
//         foreach (Match match in ifSymbol)
//         {
//             // if the symbol is not defined, remove the code
//             if (!symbolList.Contains(match.Value))
//             {
//                 code = Regex.Replace(code, @$"#if\s+{match.Value}[\s\S]*?#endif\s*?\n", "");
//             }
//             // remove only #if
//             else
//             {
//                 code = Regex.Replace(code, @$"#if\s+{match.Value}\s*?\n", "");
//             }
//         }

//         // all endifs from defined symbols
//         code = Regex.Replace(code, @"#endif\s*?\n", "");

//         if (file.EndsWith(".gml"))
//         {
//             // exporting code blocks with names
//             Regex codeBlockPattern = new Regex(@"#code\s+[\w\d_]+\s*\n[\s\S]*?#endcode");
//             MatchCollection codeBlockMatches = codeBlockPattern.Matches(code);
//             foreach (Match match in codeBlockMatches)
//             {
//                 string codeName = Regex.Match(match.Value, @"(?<=#code\s+)[\w\d_]+\s*(\n|\r\n)").Value;
//                 string codeBlock = Regex.Match(match.Value, @"(?<=#code\s+[\w\d_]+\s*(\n|\r\n))[\s\S]*?(?=#endcode)").Value;
//                 exportedCode[codeName] = codeBlock;
//             }

//             // remove code name processing
//             code = Regex.Replace(code, @"#code\s+[\w\d_]+\s*?\n", "");
//             code = code.Replace(@"#endcode", "");

//             // inserting enums
//             Regex enumPattern = new Regex(@"#[\w\d_]+\.[#\w\d_][\w\d_]*");
//             code = enumPattern.Replace(code, match =>
//             {
//                 string value = match.Value;
//                 string[] names = value.Split('.');
//                 string enumName = names[0].Substring(1);
//                 if (!enumValues.ContainsKey(enumName))
//                 {
//                     Console.WriteLine(new UMPException(7, $"Enum \"{enumName}\" not found in given enums"));
//                 }
//                 string enumMember = names[1];
//                 if (enumMember.StartsWith("#"))
//                 {
//                     enumMember = enumMember.Substring(1);
//                     switch (enumMember)
//                     {
//                         case "length":
//                             return enumValues[enumName].Count.ToString();
//                         default:
//                             Console.WriteLine(new UMPException(13, $"Unknown enum property \"{enumMember}\""));
//                             return "";
//                     }
//                 }
//                 if (!enumValues[enumName].ContainsKey(enumMember))
//                 {
//                     Console.WriteLine(new UMPException(8, $"Enum member \"{enumMember}\" not found in enum \"{enumName}\""));
//                 }
//                 return enumValues[enumName][enumMember].ToString();
//             });
//         }

//         processedCode[file] = code;
//     }

//     // post processing (organizing which files to import and how)
//     foreach (string file in processedCode.Keys)
//     {
//         string code = processedCode[file];
    
//         // ignoring files
//         if (useIgnore && Regex.IsMatch(code, @"^///.*?\.ignore"))
//         {
//             string positiveCondition = Regex.Match(code, @"(?<=^///.*?\.ignore\s+if\s+)[\d\w_]+").Value;
//             string negativeCondition = Regex.Match(code, @"(?<=^///.*?\.ignore\s+ifndef\s+)[\d\w_]+").Value;
//             if
//             (
//                 (positiveCondition == negativeCondition && positiveCondition == "") ||
//                 (
//                     (positiveCondition != "" && symbolList.Contains(positiveCondition)) ||
//                     (negativeCondition != "" && !symbolList.Contains(negativeCondition))
//                 )
//             )
//             {
//                 continue;
//             }
//         }
//         // "opening" function files
//         if (code.StartsWith("/// FUNCTIONS"))
//         {
//             try
//             {
                
//                 string currentFunction = "";
//                 int i = 0;
//                 int start = 0;
//                 int depth = 0;
//                 while (i < code.Length)
//                 {
//                     char c = code[i];
//                     if (c == 'f')
//                     {
//                         if (code.Substring(i, 8) == "function")
//                         {
//                             start = i;
//                             i += 8;
//                             int nameStart = i;
//                             while (code[i] != '(')
//                             {
//                                 i++;
//                             }
//                             string functionName = code.Substring(nameStart, i - nameStart).Trim();
//                             List<string> args = new();
//                             nameStart = i + 1;
//                             while (true)
//                             {
//                                 bool endLoop = code[i] == ')';
//                                 if (code[i] == ',' || endLoop)
//                                 {
//                                     string argName = code.Substring(nameStart, i - nameStart).Trim();
//                                     if (argName != "")
//                                         args.Add(argName);
//                                     nameStart = i + 1;
//                                     if (endLoop)
//                                         break;
//                                 }
//                                 i++;
//                             }
//                             while (code[i] != '{')
//                             {
//                                 i++;
//                             }
//                             int codeStart = i + 1;
//                             do
//                             {
//                                 if (code[i] == '{')
//                                 {
//                                     depth++;
//                                 }
//                                 else if (code[i] == '}')
//                                 {
//                                     depth--;
//                                 }
//                                 i++;
//                             }
//                             while (depth > 0);
//                             // - 1 at the end to remove the last }
//                             string functionCodeBlock = code.Substring(codeStart, i - codeStart - 1);

                            
//                             List<string> gmlArgs = new();
//                             // initializing args, unless they are argumentN in gamemaker because those already work normally
//                             for (int j = 0; j < args.Count; j++)
//                             {
//                                 gmlArgs.Add("argument" + j);
//                                 string arg = args[j];
//                                 if (arg.StartsWith("argument"))
//                                 {
//                                     continue;
//                                 }
//                                 else
//                                 {
//                                     functionCodeBlock = $"var {arg} = argument{j};" + functionCodeBlock;
//                                 }
//                             }
//                             functionCodeBlock = $"function {functionName}({string.Join(", ", gmlArgs)}) {{ {functionCodeBlock} }}";
//                             string entryName = $"gml_GlobalScript_{functionName}";
//                             if (!useFunctions) entryName = entryName.Replace("gml_GlobalScript", "gml_Script");
//                             functions.Add(new UMPFunctionEntry(entryName, functionCodeBlock, functionName, false));
//                         }
//                     }
//                     i++;
//                 }
//             }
//             catch (System.Exception e)
//             {                
//                 Console.WriteLine(new UMPException(16, $"Error processing functions in file \"{file}\""));
//             }
//         }
//         else if (Regex.IsMatch(code, @"^/// (IMPORT|PATCH)"))
//         {
//             string fileName = file;
//             string codeName = "";
//             codeName = Path.GetFileNameWithoutExtension(fileName);
//             bool isASM = fileName.EndsWith(".asm");

//             foreach (string prefix in objectPrefixes ?? new string[0])
//             {
//                 if (codeName.StartsWith(prefix))
//                 {
//                     codeName = $"gml_Object_{codeName}";
//                 }
//             }

//             if (!useFunctions)
//             {
//                 codeName = codeName.Replace("gml_GlobalScript", "gml_Script");
//             }

//             UMPCodeEntry codeEntry = new UMPCodeEntry(codeName, code, isASM);
//             if (code.StartsWith("/// PATCH"))
//             {
//                 patches.Add(codeEntry);
//             }
//             else
//             {
//                 if (codeName.Contains("gml_GlobalScript") || codeName.Contains("gml_Script"))
//                 {
//                     string entryName = Path.GetFileNameWithoutExtension(file);
//                     string functionName = Regex.Match(entryName, @"(?<=(gml_Script_|gml_GlobalScript_))[_\d\w]+").Value;

//                     functions.Add(new UMPFunctionEntry(entryName, code, functionName, isASM));
//                 }
//                 else
//                 {
//                     imports.Add(codeEntry);
//                 }
//             }
//         }
//         else
//         {
//             Console.WriteLine(new UMPException(9, $"File \"{file}\" does not have a valid UMP type"));
//         }
//     }

//     // order functions so that they never call functions not yet defined
//     List<UMPFunctionEntry> functionsInOrder = new();

//     while (functionsInOrder.Count < functions.Count)
//     {   
//         // go through each function, check if it's never mentiond in all functions that are already not in functionsInOrder 
//         foreach (UMPFunctionEntry testFunction in functions)
//         {
//             if (functionsInOrder.Contains(testFunction)) continue;
//             bool isSafe = true;
//             foreach (UMPFunctionEntry otherFunction in functions)
//             {
//                 if (!functionsInOrder.Contains(otherFunction) && !otherFunction.Equals(testFunction))
//                 {
//                     if (Regex.IsMatch(testFunction.Code, @$"\b{otherFunction.FunctionName}\b"))
//                     {
//                         isSafe = false;
//                         break;
//                     }
//                 }
//             }
//             if (isSafe)
//             {
//                 functionsInOrder.Add(testFunction);
//             }
//         }
//     }

//     foreach (UMPFunctionEntry entry in functionsInOrder)
//     {
//         if (useFunctions)
//         {
//             UMPImportCodeEntry(entry);
//         }
//         else
//         {
//             string functionBody = Regex.Match(entry.Code, @"(?<=^\s*function[\s\d\w_]+\(.*?\)\s*{)[\s\S]+(?=}\s*$)").Value;
//             // if the script was defined without "function"
//             if (functionBody == "")
//             {
//                 functionBody = entry.Code;
//             }
//             string scriptName = entry.FunctionName;
//             string codeName = entry.Name.Replace("gml_GlobalScript", "gml_Script");
//             UndertaleCode scriptCode = null;
//             if (Data.Scripts.ByName(scriptName) == null)
//             {
//                 ImportGMLString(codeName, functionBody);
//             }
//             else
//             {
//                 Data.Code.ByName(codeName).ReplaceGML(functionBody, Data);
//             }
//         }
//     }

//     foreach (UMPCodeEntry entry in imports)
//     {
//         UMPImportCodeEntry(entry);
//     }

//     foreach (UMPCodeEntry entry in patches)
//     {
//         UMPPatchFile patch = new UMPPatchFile(entry.Code, entry.Name, entry.IsASM);
//         if (patch.RequiresCompilation)
//         {
//             UMPAddCodeToPatch(patch, entry.Name);
//         }

//         foreach (UMPPatchCommand command in patch.Commands)
//         {
//             if (command is UMPAfterCommand)
//             {
//                 int placeIndex = patch.Code.IndexOf(command.OriginalCode) + command.OriginalCode.Length;
//                 patch.Code = patch.Code.Insert(placeIndex, "\n" + command.NewCode + "\n");
//             }
//             else if (command is UMPBeforeCommand)
//             {
//                 int placeIndex = patch.Code.IndexOf(command.OriginalCode);
//                 patch.Code = patch.Code.Insert(placeIndex, "\n" + command.NewCode + "\n");
//             }
//             else if (command is UMPReplaceCommand)
//             {
//                 patch.Code = patch.Code.Replace(command.OriginalCode, command.NewCode);
//             }
//             else if (command is UMPAppendCommand)
//             {
//                 if (entry.IsASM)
//                 {
//                     patch.Code = patch.Code + "\n" + command.NewCode;
//                 }
//                 else
//                 {
//                     try
//                     {
//                         UMPAppendGML(entry.Name, command.NewCode);
                        
//                     }
//                     catch (System.Exception)
//                     {
//                         Console.WriteLine(new UMPException(15, $"Error appending code to entry \"{entry.Name}\""));
//                     }
//                     if (patch.RequiresCompilation)
//                     {
//                         UMPAddCodeToPatch(patch, entry.Name);
//                     }
//                 }
//             }
//             else if (command is UMPPrependCommand)
//             {
//                 patch.Code = command.NewCode + "\n" + patch.Code;
//             }
//             else
//             {
//                 throw new Exception("Unknown command type: " + command.GetType().Name);
//             }

//             try
//             {
//                 if (patch.IsASM)
//                 {
//                     ImportASMString(entry.Name, patch.Code);
//                 }            
//                 else if (patch.RequiresCompilation)
//                 {
//                     Data.Code.ByName(entry.Name).ReplaceGML(patch.Code, Data);
//                 }
//             }
//             catch (Exception)
//             {
//                 Console.WriteLine(new UMPException(14, "Error importing code entry \"" + entry.Name + "\""));
//             }
//         }
//     }

//     return exportedCode;
// }

/// <summary>
/// Add the decompiled code of a code entry to a patch
/// </summary>
/// <param name="patch"></param>
/// <param name="codeName"></param>
void UMPAddCodeToPatch (UMPPatchFile patch, string codeName)
{
    try
    {
        if (patch.IsASM)
        {
            // necessary due to linebreak whitespace inconsistency
            patch.Code = GetDisassemblyText(codeName).Replace("\r", "");
        }
        else
        {
            patch.Code = GetDecompiledText(codeName);
        }
    }
    catch (System.Exception)
    {
        Console.WriteLine(new UMPException(12, $"Error decompiling code entry \"{codeName}\""));
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
                            Console.WriteLine(new UMPException(10, $"Error in patch file \"{entryName}\": Expected CODE command"));
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
                        Console.WriteLine(new UMPException(11, $"Error in patch file \"{entryName}\": Expected END command"));
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

    public void UMPAddCodeToPatch (UMPPatchFile patch, string codeName)
    {
        try
        {
            if (patch.IsASM)
            {
                // necessary due to linebreak whitespace inconsistency
                patch.Code = Wrapper.GetDisassemblyText(codeName).Replace("\r", "");
            }
            else
            {
                patch.Code = Wrapper.GetDecompiledText(codeName);
            }
        }
        catch (System.Exception)
        {
            Console.WriteLine(new UMPException(12, $"Error decompiling code entry \"{codeName}\""));
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
public class UMPCodeEntry
{
    public string Name { get; set; }
    public string Code { get; set; }

    public bool IsASM { get; set; }

    public UMPCodeEntry (string name, string code, bool isASM = false)
    {
        Name = name;
        Code = code;
        IsASM = isASM;
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

    }
}

void UMPImportCodeEntry (UMPCodeEntry codeEntry)
{
    if (codeEntry.IsASM)
    {
        ImportASMString(codeEntry.Name, codeEntry.Code);
    }
    else
    {
        ImportGMLString(codeEntry.Name, codeEntry.Code);
    }
}


/// <summary>
/// Represents a code entry that is a function
/// </summary>
class UMPFunctionEntry : UMPCodeEntry
{
    public string FunctionName { get; set; }

    public UMPFunctionEntry (string name, string code, string functionName, bool isASM) : base(name, code, isASM)
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
        if (!IsPascalCase(name))
        {
            Console.WriteLine(new UMPException(6, $"Original case must be pascal case for name \"{name}\""));
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

/// <summary>
/// A documented UMP error
/// </summary>
public class UMPException : Exception
{
    /// <summary>
    /// Error code of the error
    /// </summary>
    public int ErrorCode { get; }

    /// <summary>
    /// Create a new UMP error
    /// </summary>
    /// <param name="errorCode">Error code of the error</param>
    /// <param name="message">Message to log</param>
    public UMPException(int errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public override string ToString()
    {
        return $"UMP ERROR #{ErrorCode.ToString("D4")}\n{Message}";
    }
}

public class UMPWrapper
{
    public UndertaleData Data;

    public string ScriptPath;

    public Func<string, string> UMPGetObjectName;

    public Func<string, string> UMPCreateGMSObject;

    public Func<UMPCodeEntry, string> UMPImportCodeEntry;

    public Func<string, string, string> ImportGMLString;

    public Func<string, string, string> UMPAppendGML;

    public Func<string, string, string> ImportASMString;

    public Func<string, string> GetDisassemblyText;

    public Func<string, string> GetDecompiledText;

    public UMPWrapper
    (
        UndertaleData data,
        string scriptPath,
        Func<string, string> umpGetObjectName,
        Func<string, string> umpCreateGMSObject,
        Func<UMPCodeEntry, string> umpImportCodeEntry,
        Func<string, string, string> importGMLString,
        Func<string, string, string> umpAppendGML,
        Func<string, string, string> importASMString,
        Func<string, string> getDisassemblyText,
        Func<string, string> getDecompiledText
    )
    {
        Data = data;
        ScriptPath = scriptPath;
        UMPGetObjectName = umpGetObjectName;
        UMPCreateGMSObject = umpCreateGMSObject;
        UMPImportCodeEntry = umpImportCodeEntry;
        ImportGMLString = importGMLString;
        UMPAppendGML = umpAppendGML;
        ImportASMString = importASMString;
        GetDisassemblyText = getDisassemblyText;
        GetDecompiledText = getDecompiledText;
    }
}