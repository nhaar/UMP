#load "TestModBase.csx"

class TestLoaderWithoutSymbol : TestLoaderBase
{
    public override bool UseGlobalScripts => true;

    public TestLoaderWithoutSymbol (UMPWrapper wrapper) : base(wrapper) {}
}

TestLoaderWithoutSymbol loader = new TestLoaderWithoutSymbol(UMP_WRAPPER);
loader.Load();
