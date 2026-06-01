#load "../src/ump.csx"

class TestLoader : UMPLoader
{
    public override string CodePath => "";
    public override bool UseGlobalScripts => true;
    public override string[] Symbols => new string[] { "U", "M", "Y" };
    public override string[] GetCodeNames (string filePath)
    {
        return new string[] { };
    }
    public TestLoader (UMPWrapper wrapper) : base(wrapper)
    {
    }
}
TestLoader loader = new TestLoader(UMP_WRAPPER);

var code = @"var a = 1;
#if U
var b = 2;
#if M
var c = 3;
#if P
var d = 4;
#else
var e = 5;
#endif
var f = 6;
#elsif X
var g = 7;
#else
var h = 8;
#endif
var i = 9;
#else
var j = 10;
#if P
var k = 11;
#endif
#endif
#if !U
var l = 12;
#else
var m = 13;
#endif
#if P
var n = 14;
#elsif M
var o = 15;
#else
var p = 16;
#endif
#if U
#if M
#if !P
var q = 17;
#else
var r = 18;
#endif
#else
var s = 19;
#endif
#endif
#if X
var t = 20;
#if Y
var u = 21;
#else
var v = 22;
#endif
#else
var w = 23;
#endif
#if U
#if M
#if P
var x = 24;
#if Z
var y = 25;
#else
var z = 26;
#endif
#else
var aa = 27;
#endif
#endif
#endif
var ab = 28;";

var expected = @"var a = 1;
var b = 2;
var c = 3;
var e = 5;
var f = 6;
var i = 9;
var m = 13;
var o = 15;
var q = 17;
var w = 23;
var aa = 27;
var ab = 28;";

var processor = new UMPLoader.CodeProcessor(code, loader);
var processed = processor.Preprocess();

Console.WriteLine(processed);
Console.WriteLine(processed == expected ? "Passed" : "Failed");