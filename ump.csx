using System.Threading;
using System.Threading.Tasks;

ThreadLocal<GlobalDecompileContext> DECOMPILE_CONTEXT = new ThreadLocal<GlobalDecompileContext>(() => new GlobalDecompileContext(Data, false));

bool CheckIfCodeExists (string codeName)
{
    return Data.Code.ByName(codeName) != null;
}

void UMPImportFile (string path)
{
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

void UMPImportGML (string codeName, string code)
{
    var isPatchFile = code.StartsWith("/// PATCH") && CheckIfCodeExists(codeName);

    if (isPatchFile)
    {
        UmpPatchFile patch = new UmpPatchFile(code);
        if (patch.RequiresCompilation)
        {
            AddCodeToPatch(patch, codeName);
        }

        foreach (PatchCommand command in patch.Commands)
        {
            if (command is AfterCommand)
            {
                int placeIndex = patch.Code.IndexOf(command.OriginalCode) + command.OriginalCode.Length;
                patch.Code = patch.Code.Insert(placeIndex, "\n" + command.NewCode);
            }
            else if (command is ReplaceCommand)
            {
                patch.Code = patch.Code.Replace(command.OriginalCode, command.NewCode);
            }
            else if (command is AppendCommand)
            {
                AppendGML(codeName, command.NewCode);
                if (patch.RequiresCompilation)
                {
                    AddCodeToPatch(patch, codeName);
                }
            }
            else if (command is PrependCommand)
            {
                patch.Code = command.NewCode + patch.Code;
            }
            else
            {
                throw new Exception("Unknown command type: " + command.GetType().Name);
            }
        }

        if (patch.RequiresCompilation)
        {
            Data.Code.ByName(codeName).ReplaceGML(patch.Code, Data);
        }
    }
    else
    {
        ImportGMLString(codeName, code);
    }
}

void AddCodeToPatch (UmpPatchFile patch, string codeName)
{
    if (Data.KnownSubFunctions is null) Decompiler.BuildSubFunctionCache(Data);
    patch.Code = Decompiler.Decompile(Data.Code.ByName(codeName), DECOMPILE_CONTEXT.Value);
}

abstract class PatchCommand
{
    public abstract bool BasedOnText { get; }

    public abstract bool RequiresCompilation { get; }

    public string OriginalCode { get; set; }

    public string NewCode { get; set; }

    public PatchCommand (string newCode, string originalCode = null)
    {
        NewCode = newCode;
        OriginalCode = originalCode;
    }
}

class AfterCommand : PatchCommand
{
    public AfterCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => true;

    public override bool RequiresCompilation => true;
}

class ReplaceCommand : PatchCommand
{
    public ReplaceCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => true;

    public override bool RequiresCompilation => true;

}

class AppendCommand : PatchCommand
{
    public AppendCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => false;

    public override bool RequiresCompilation => false;
}

class PrependCommand : PatchCommand
{
    public PrependCommand (string newCode, string originalCode = null) : base(newCode, originalCode) { }

    public override bool BasedOnText => false;

    public override bool RequiresCompilation => true;
}


class UmpPatchFile
{
    public List<PatchCommand> Commands = new();

    public bool RequiresCompilation { get; }

    public string Code { get; set ; }

    public class ModifiedCommandException : Exception
    {
        public ModifiedCommandException(string line) : base("Unknown command in modified code: " + line) { }
    }

    public UmpPatchFile (string gmlCode)
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
                        Commands.Add((PatchCommand)command);
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
                        currentCommand = typeof(AfterCommand);
                    }
                    else if (Regex.IsMatch(line, @"\bREPLACE\b"))
                    {
                        currentCommand = typeof(ReplaceCommand);
                    }
                    else if (Regex.IsMatch(line, @"\bAPPEND\b"))
                    {
                        inOriginalText = false;
                        currentCommand = typeof(AppendCommand);
                    }
                    else if (Regex.IsMatch(line, @"\bPREPEND\b"))
                    {
                        inOriginalText = false;
                        currentCommand = typeof(PrependCommand);
                    }
                    else
                    {
                        throw new ModifiedCommandException(line);
                    }
                }
            }
        }

        foreach (PatchCommand command in Commands)
        {
            if (command.RequiresCompilation)
            {
                RequiresCompilation = true;
                break;
            }
        }
    }
}

void AppendGML (string codeName, string code)
{
    Data.Code.ByName(codeName).AppendGML(code, Data);
}