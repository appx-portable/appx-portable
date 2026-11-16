using System;
using System.IO;
using Windows.ApplicationModel.Resources;
class ResourcesTest
{
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    static int Main()
    {
        var loader = ResourceLoader.GetForCurrentView();
        Check(loader.GetString("Hello") == "đá 💎", "PRI Unicode string");
        Check(loader.GetString("Blank") == "" && loader.GetString("absent") == "", "empty and absent strings");
        Check(ResourceLoader.GetForCurrentView("Resources").GetString("Label/Text") == "A label", "property resource path");
        Check(new ResourceLoader().GetString("Hello") == "đá 💎", "default constructor");
        var bytes = File.ReadAllBytes("resources.pri");
        try
        {
            File.WriteAllBytes("resources.pri", new byte[32]);
            try { ResourceLoader.GetForCurrentView().GetString("Hello"); throw new Exception("malformed PRI accepted"); }
            catch (InvalidDataException) { }
        }
        finally { File.WriteAllBytes("resources.pri", bytes); }
        // Keep the section intact but declare an empty name pool. Offsets into
        // trailing section bytes must not count as valid names.
        var broken = (byte[])bytes.Clone();
        int toc = BitConverter.ToInt32(bytes, 16), sectionData = BitConverter.ToInt32(bytes, 20);
        int schema = sectionData + BitConverter.ToInt32(bytes, toc + 24) + 32;
        int names = (schema + 8 + BitConverter.ToUInt16(bytes, schema) * 20 +
            (BitConverter.ToUInt16(bytes, schema + 2) + BitConverter.ToUInt16(bytes, schema + 4)) * 2 + 3) & ~3;
        Array.Clear(broken, names + 16, 4);
        try
        {
            File.WriteAllBytes("resources.pri", broken);
            try { ResourceLoader.GetForCurrentView().GetString("Hello"); throw new Exception("PRI name outside declared pool accepted"); }
            catch (InvalidDataException) { }
        }
        finally { File.WriteAllBytes("resources.pri", bytes); }
        Console.WriteLine("PASS: PRI resource strings, paths and malformed input");
        return 0;
    }
}
