#load "..\src\ump.csx"

class TestLoader : UMPLoader
{
    public override string CodePath => "mod/";

    public override string[] GetCodeNames (string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.StartsWith("obj"))
        {
            fileName = $"gml_Object_{fileName}";
        }

        return new[] { fileName };
    }

    public TestLoader (UMPWrapper wrapper, string[] symbols, bool useGlobalScripts) : base(wrapper, symbols, useGlobalScripts) {}
}

TestLoader testLoader = new TestLoader(UMP_WRAPPER, new string[] {}, false);
testLoader.Load();