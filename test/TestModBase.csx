#load "..\src\ump.csx"



abstract class TestLoaderBase : UMPLoader
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

    public enum TestEnum
    {
        Test1,
        Test2,
        Test3 = 80
    }

    public enum TestEnum2
    {
        Test1,
        Test2,
        Test3
    }

    public string TestMethod (string arg1, string arg2)
    {
        return "Test" + arg1 + arg2;
    }

    public string TestGMLMethod (string code)
    {
        Console.WriteLine(code);
        return code.Replace("obj_mainchara", "obj_mainchara_ch1");
    }

    public string TestNumberMethod (int integer, double floatNumber)
    {
        return (integer + floatNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public TestLoaderBase (UMPWrapper wrapper) : base(wrapper) {}
}