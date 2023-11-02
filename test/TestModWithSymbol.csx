#load "TestModBase.csx"

class TestLoaderWithSymbol : TestLoaderBase
{
    public override bool UseGlobalScripts => true;

    public override string[] Symbols => new[] { "TEST" };

    public TestLoaderWithSymbol (UMPWrapper wrapper) : base(wrapper) {}
}

TestLoaderWithSymbol loader = new TestLoaderWithSymbol(UMP_WRAPPER);
loader.Load();
