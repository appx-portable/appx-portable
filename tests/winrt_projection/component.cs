using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Example.Component;
// A managed implementation of a component interface, called back by native code.
class Recorder : IRenderer
{
    private readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
    public void Begin(bool header, int entry, string label) { log.Append(header + ":" + entry + ":" + label + ";"); }
    public void Words(IList<string> words) { log.Append(words == null ? "null" : string.Join(",", words)).Append(";"); }
    public void Unsupported(Changed callback) { throw new Exception("an unsupported thunk reached managed code"); }
    public double Scale(uint index, double size) { if (index == 0) throw new ArgumentException("zero index"); return index * size; }
    public string Result() { return log.ToString(); }
    public uint Sum(byte[] data) { uint sum = 0; foreach (byte value in data) sum += value; return sum; }
}
class ComponentTest
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference Keep(Printer printer)
    {
        var recorder = new Recorder();
        printer.Keep(recorder);
        return new WeakReference(recorder);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference Subscribe(Engine engine, out System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken token)
    {
        var expected = new[] { -42 };
        Changed handler = (count, active, text) => { Require(count == expected[0] && active && text == "đá\0💎", "callback ABI"); return text + count; };
        token = engine.add_Changed(handler); return new WeakReference(handler);
    }
    [MTAThread]
    static int Main()
    {
        // A factory constructor with arguments, as Microsoft.Xbox's ServiceClient(String) has.
        Require(new Gauge("đá 💎").Name == "đá 💎", "factory constructor argument");
        Require(new Gauge(null).Name == "", "null string factory argument");
        var engine = new Engine();
        Require(engine.Echo(-1234567) == -1234567, "signed scalar result");
        engine.SetState(true, 1.25, 0xf0000000U);
        Require(engine.GetState() == 4026531841.25, "mixed-width parameters");
        engine.SetState(false, 2.5, 1);
        Require(engine.GetState() == -2.5, "one-byte false");
        Require(engine.EchoText("đá\0💎") == "đá\0💎", "HSTRING ownership and length");
        Require(engine.EchoText(null) == "", "null HSTRING");
        Require(engine.SumBytes(new byte[] { 0, 1, 128, 255 }) == 384, "array length and unsigned bytes");
        Require(engine.SumBytes(new byte[0]) == 0 && engine.SumBytes(null) == 0, "empty arrays");
        var large = new byte[1024 * 1024]; large[large.Length - 1] = 255;
        Require(engine.SumBytes(large) == 255, "large pinned array");
        System.Runtime.InteropServices.WindowsRuntime.EventRegistrationToken token;
        var weak = Subscribe(engine, out token);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Require(weak.IsAlive, "native retained delegate");
        Require(engine.Raise(-42, true, "đá\0💎") == "đá\0💎-42", "string-returning callback");
        engine.remove_Changed(token);
        Require(engine.Raise(1, false, "unused") == "", "removed callback");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Require(!weak.IsAlive, "removed delegate collectable");
        var failure = new InvalidOperationException("component callback failure");
        token = engine.add_Changed((count, active, text) => { throw failure; });
        try { engine.Raise(1, false, ""); throw new Exception("callback exception lost"); }
        catch (InvalidOperationException ex) { Require(ex.HResult == failure.HResult, "callback HRESULT"); }
        engine.remove_Changed(token);
        try { engine.Fail(); throw new Exception("native error lost"); }
        catch (ArgumentException) { }
        // Non-blittable structs are declared with their metadata fields; members passing them by value are named stubs.
        var form = new Form { Word = "đá 💎", Primary = true, Rank = -3 };
        Require(form.Word == "đá 💎" && form.Primary && form.Rank == -3, "struct string, boolean and scalar fields");
        try { engine.GetForm("word"); throw new Exception("struct-returning stub returned"); }
        catch (NotImplementedException ex) { Require(ex.Message.Contains("Example.Component.Engine.GetForm"), "struct method stub names itself"); }
        // Stubs keep the accessor a game's IL calls, not the C# property.
        try { form = engine.get_LastForm(); throw new Exception("struct-typed property stub returned"); }
        catch (NotImplementedException ex) { Require(ex.Message.Contains("Example.Component.Engine.get_LastForm"), "struct property stub names itself"); }
        // Printer.Render checks the unsupported slot's E_NOTIMPL, Scale's thrown E_INVALIDARG,
        // and Scale and Sum results itself, then returns Result().
        var printer = new Printer();
        Require(printer.Render(new Recorder()) == "True:-42:đá\0💎;alpha,βeta;", "managed interface callbacks");
        var kept = Keep(printer);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Require(kept.IsAlive, "native retains a managed implementation");
        Require(printer.RenderKept() == "False:7:kept;", "retained managed implementation");
        printer.Dispose();
        try { printer.RenderKept(); throw new Exception("closed component still rendered"); }
        catch (Exception error) { Require(error.HResult == unchecked((int)0x80000013), "IClosable.Close through Dispose: " + error); }
        Console.WriteLine("PASS: generic component ABI");
        return 0;
    }
}
