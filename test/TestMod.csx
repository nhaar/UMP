#load "..\src\ump.csx"

class TestLoader : UMPLoader
{
    public override string CodePath => "mod/";

    public override bool UseGlobalScripts => true;

    public override string[] GetCodeNames (string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        if (fileName.StartsWith("obj"))
        {
            fileName = $"gml_Object_{fileName}";
        }

        return new[] { fileName };
    }

    public TestLoader (UMPWrapper wrapper) : base(wrapper) {}
}

TestLoader testLoader = new TestLoader(UMP_WRAPPER);
testLoader.Load();