using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xaml;
using System.Xaml.Schema;

namespace AppxPortable.WinRT.Xaml
{
    // XBF 1.0 is a persisted XAML node stream. Decode it into the same schema and
    // object writer as source XAML. Format/value tags follow Microsoft's
    // microsoft-ui-xaml XamlBinaryFormatReader.cpp and DumpXbf/FileFormat.cs.
    internal sealed class XbfReader
    {
        private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private sealed class Entry { internal int Flags, Owner, Name; }
        private sealed class Frame { internal XamlType Type; internal XamlMember Member; }
        private readonly List<string> strings = new List<string>();
        private readonly List<Entry> namespaces = new List<Entry>(), types = new List<Entry>(), properties = new List<Entry>();
        private readonly List<string> xmlNamespaces = new List<string>();
        private readonly UwpSchemaContext schema = UwpSchemaContext.Instance;
        private PackageBytes data;
        private int position;
        private int Int() { int value = data.Int(position); position += 4; return value; }
        private uint Bits() { uint value = data.U32(position); position += 4; return value; }
        private byte Byte() { byte value = data.Byte(position); position++; return value; }
        private string Text() { int length = Int(); string value = data.Text(position, checked(length * 2)); position += length * 2; return value; }
        private float Float() { uint value = Bits(); return BitConverter.ToSingle(BitConverter.GetBytes(value), 0); }
        private static T Item<T>(List<T> items, int index)
        {
            if (index < 0 || index >= items.Count) throw new InvalidDataException("Invalid XBF table reference");
            return items[index];
        }
        private XamlType Type(int index)
        {
            var type = Item(types, index);
            string name = Item(strings, type.Name);
            if ((type.Flags & ~1) != 0) throw new NotSupportedException("Unknown XBF type flags");
            string ns = type.Flags == 1 ? XamlLanguage.Xaml2006Namespace : Item(strings, Item(namespaces, type.Owner).Name);
            if (ns == "Windows.Foundation" && name == "String") return XamlLanguage.String;
            ns = ns.StartsWith("Windows.UI.Xaml", StringComparison.Ordinal) ? Presentation : ns == XamlLanguage.Xaml2006Namespace ? ns : "using:" + ns;
            return schema.GetXamlType(new XamlTypeName(ns, name)) ?? new XamlType(ns, name, null, schema);
        }
        private XamlMember Member(int index, Frame frame)
        {
            var property = Item(properties, index);
            string name = Item(strings, property.Name);
            if (property.Flags == 2) return schema.GetXamlDirective(XamlLanguage.Xaml2006Namespace, name);
            if (property.Flags == 1) return schema.GetXamlDirective(XamlLanguage.Xml1998Namespace, name);
            if (property.Flags == 4)
            {
                if (name == "__implicit_items") return XamlLanguage.Items;
                if (name == "__implicit_initialization") return XamlLanguage.Initialization;
                throw new NotSupportedException("Unknown XBF implicit property: " + name);
            }
            if (property.Flags != 0) throw new NotSupportedException("Unknown XBF property flags");
            var owner = Type(property.Owner);
            // XBF keeps the storyboard's deferred-storage name; WPF's schema
            // owns template deferral and exposes the visual state's Storyboard.
            if (name == "__DeferredStoryboard" && owner.UnderlyingType == typeof(System.Windows.VisualState)) name = "Storyboard";
            if (frame.Type.UnderlyingType == typeof(System.Windows.Controls.ContentPresenter) && (name == "FontSize" || name == "FontWeight"))
                return schema.GetXamlType(typeof(System.Windows.Documents.TextElement)).GetAttachableMember(name);
            // Declared attached members take priority over an unrelated instance
            // member. Normal inheritance follows UWP even when its mapped peers
            // (PanelPeer and WPF Grid, for example) are separate subclasses.
            return owner.GetAttachableMember(name) ?? frame.Type.GetMember(name) ?? owner.GetMember(name) ?? new XamlMember(name, owner, false);
        }
        private object Value()
        {
            int kind = Byte();
            switch (kind)
            {
                case 1: return false;
                case 2: return true;
                case 3: case 8: return Float().ToString("R", CultureInfo.InvariantCulture);
                case 4: return unchecked((int)Bits());
                case 5: return Text();
                case 6: case 11: return TimeSpan.FromSeconds(Float()).ToString("c", CultureInfo.InvariantCulture);
                case 7:
                    var sides = new string[4];
                    for (int i = 0; i < sides.Length; i++) sides[i] = Float().ToString("R", CultureInfo.InvariantCulture);
                    return string.Join(",", sides);
                case 9:
                    int unit = Int(); float length = Float();
                    if (unit == 0) return "Auto";
                    if (unit != 1 && unit != 2) throw new InvalidDataException("Invalid XBF grid length unit");
                    return length.ToString("R", CultureInfo.InvariantCulture) + (unit == 2 ? "*" : "");
                case 10: return "#" + Bits().ToString("X8", CultureInfo.InvariantCulture);
                default: throw new NotSupportedException("Unsupported XBF value tag " + kind);
            }
        }
        internal static XamlReader Read(string path) { return new XbfReader().Decode(PackageBytes.Read(path)); }
        private XamlReader Decode(PackageBytes file)
        {
            if (file.U32(0) != 0x00464258) throw new InvalidDataException("Invalid XBF signature");
            int metadataSize = file.Int(4), nodeSize = file.Int(8);
            if ((long)metadataSize + nodeSize + 12 != file.Length) throw new InvalidDataException("Invalid XBF section lengths");
            var metadata = file.Slice(12, metadataSize);
            if (metadata.U32(0) != 1 || metadata.U32(4) != 0) throw new NotSupportedException("Only XBF 1.0 is supported by the managed loader");
            var offsets = new int[7];
            offsets[6] = metadataSize;
            for (int i = 0; i < 6; i++)
            {
                if (metadata.U32(12 + i * 8) != 0) throw new InvalidDataException("Invalid XBF metadata offset");
                offsets[i] = metadata.Int(8 + i * 8);
                if (offsets[i] < 120 || i != 0 && offsets[i] < offsets[i - 1]) throw new InvalidDataException("Invalid XBF table order");
            }
            for (int table = 0; table < 6; table++)
            {
                data = metadata.Slice(offsets[table], offsets[table + 1] - offsets[table]); position = 0;
                int count = Int(), width = table == 0 || table == 5 ? 4 : table == 3 || table == 4 ? 12 : 8;
                if (count > (data.Length - 4) / width) throw new InvalidDataException("Invalid XBF table length");
                for (int i = 0; i < count; i++)
                {
                    if (table == 0) strings.Add(Text());
                    else if (table == 5) xmlNamespaces.Add(Item(strings, Int()));
                    else
                    {
                        var entry = new Entry();
                        entry.Flags = Int();
                        if (table == 3 || table == 4) entry.Owner = Int();
                        entry.Name = Int(); Item(strings, entry.Name);
                        if (table == 2) namespaces.Add(entry);
                        else if (table == 3) types.Add(entry);
                        else if (table == 4) properties.Add(entry);
                    }
                }
                if (position != data.Length) throw new InvalidDataException("Trailing XBF table data");
            }
            data = file.Slice(12 + metadataSize, nodeSize); position = 0;
            var nodes = new XamlNodeList(schema);
            var writer = nodes.Writer;
            var frames = new Stack<Frame>();
            var scopes = new Stack<bool>(); // true = object, false = member
            int roots = 0;
            while (position < data.Length)
            {
                int op = Byte();
                switch (op)
                {
                    case 1:
                        int index = Int(), flags = Int();
                        if ((flags & ~1) != 0) throw new NotSupportedException("Unknown XBF object flags");
                        XamlType type;
                        if (flags == 1)
                        {
                            Item(types, index);
                            if (frames.Count == 0 || frames.Peek().Member == null) throw new InvalidDataException("XBF retrieved object outside a member");
                            type = frames.Peek().Member.Type;
                            writer.WriteGetObject();
                        }
                        else
                        {
                            if (frames.Count == 0) roots++;
                            type = Type(index); writer.WriteStartObject(type);
                        }
                        frames.Push(new Frame { Type = type }); scopes.Push(true);
                        break;
                    case 2:
                        if (scopes.Count == 0 || !scopes.Pop()) throw new InvalidDataException("Unbalanced XBF object scope");
                        frames.Pop(); writer.WriteEndObject(); break;
                    case 3:
                        int propertyIndex = Int(), propertyFlags = Int();
                        if (propertyFlags != 0) throw new NotSupportedException("Unknown XBF member node flags");
                        if (frames.Count == 0 || scopes.Count == 0 || !scopes.Peek()) throw new InvalidDataException("XBF member outside an object");
                        var member = Member(propertyIndex, frames.Peek());
                        frames.Peek().Member = member; writer.WriteStartMember(member); scopes.Push(false); break;
                    case 4:
                        if (scopes.Count == 0 || scopes.Pop()) throw new InvalidDataException("Unbalanced XBF member scope");
                        frames.Peek().Member = null; writer.WriteEndMember(); break;
                    case 5:
                        string text = Item(strings, Int()); Int();
                        if (scopes.Count == 0 || scopes.Peek()) throw new InvalidDataException("XBF value outside a member");
                        writer.WriteValue(text); break;
                    case 6:
                        if (scopes.Count == 0 || scopes.Peek()) throw new InvalidDataException("XBF value outside a member");
                        writer.WriteValue(Value()); break;
                    case 7:
                        string ns = Item(xmlNamespaces, Int()); Int(); string prefix = Text();
                        writer.WriteNamespace(new NamespaceDeclaration(ns, prefix)); break;
                    case 8: break; // End of attributes carries no tree data.
                    case 9:
                        if (position != data.Length) throw new InvalidDataException("Trailing XBF nodes");
                        break;
                    case 10: data.U32(position); position += 4; break; // Delta line/column.
                    case 11: data.U32(position); data.U32(position + 4); position += 8; break;
                    default: throw new NotSupportedException("Unsupported XBF node " + op + " at " + (position - 1));
                }
            }
            if (scopes.Count != 0 || frames.Count != 0 || roots != 1) throw new InvalidDataException("Unbalanced XBF tree");
            writer.Close();
            return nodes.GetReader();
        }
    }
}
