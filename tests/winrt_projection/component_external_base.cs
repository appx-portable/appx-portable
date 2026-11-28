using System;
using AppxPortable.WinRT.Interop;

#if BASE_PROJECTION
[assembly: ProjectionProvider(typeof(Example.Component.Base))]
namespace Example.Component
{
    public class Base
    {
        public Base(int owner) { throw new Exception("Unsupported derived construction reached the base constructor"); }
    }
}
#else
class ExternalBaseTest
{
    static int Main()
    {
        if (typeof(Example.Component.Derived).BaseType != typeof(Example.Component.Base))
            throw new Exception("Projected component lost its external base");
        try { new Example.Component.Derived(7); }
        catch (NotImplementedException error)
        {
            if (!error.Message.Contains("Example.Component.Derived..ctor")) throw;
            Console.WriteLine("PASS: external native base preserves managed inheritance");
            return 0;
        }
        throw new Exception("Unsupported native construction succeeded");
    }
}
#endif
