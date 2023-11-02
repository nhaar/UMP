#load "TestModBase.csx"

class TestLoaderNoGlobalScripts : TestLoaderBase
{
    public override bool UseGlobalScripts => false;

    public TestLoaderNoGlobalScripts (UMPWrapper wrapper) : base(wrapper) {}
}

TestLoaderNoGlobalScripts loader = new TestLoaderNoGlobalScripts(UMP_WRAPPER);
loader.Load();
