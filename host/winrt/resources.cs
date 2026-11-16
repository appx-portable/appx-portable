using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Windows.ApplicationModel.Resources
{
    public sealed partial class ResourceLoader
    {
        private readonly string resourceMap;
        private AppxPortable.WinRT.PriResources resources;
        public ResourceLoader() : this("Resources") { }
        public ResourceLoader(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A resource map name is required", "name");
            resourceMap = name.Trim('/');
        }
        public static ResourceLoader GetForCurrentView() { return new ResourceLoader(); }
        public static ResourceLoader GetForCurrentView(string name) { return new ResourceLoader(name); }
        public static ResourceLoader GetForViewIndependentUse() { return new ResourceLoader(); }
        public static ResourceLoader GetForViewIndependentUse(string name) { return new ResourceLoader(name); }
        public string GetString(string resource)
        {
            if (resource == null) throw new ArgumentNullException("resource");
            if (resources == null) resources = new AppxPortable.WinRT.PriResources(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources.pri"));
            return resources.GetString(resourceMap + "/" + resource.TrimStart('/'));
        }
    }
}

namespace AppxPortable.WinRT
{
    // Bounded little-endian views shared by the package's declarative formats.
    internal sealed class PackageBytes
    {
        private readonly byte[] bytes;
        private readonly int start;
        internal readonly int Length;
        internal PackageBytes(byte[] bytes) : this(bytes, 0, bytes.Length) { }
        private PackageBytes(byte[] bytes, int start, int length) { this.bytes = bytes; this.start = start; Length = length; }
        private void Check(int at, int count) { if (at < 0 || count < 0 || at > Length - count) throw new InvalidDataException("Package data is truncated or contains an invalid offset"); }
        internal PackageBytes Slice(int at, int length) { Check(at, length); return new PackageBytes(bytes, start + at, length); }
        internal byte Byte(int at) { Check(at, 1); return bytes[start + at]; }
        internal ushort U16(int at) { Check(at, 2); return (ushort)(Byte(at) | Byte(at + 1) << 8); }
        internal uint U32(int at) { Check(at, 4); return (uint)(U16(at) | U16(at + 2) << 16); }
        internal int Int(int at) { uint value = U32(at); if (value > int.MaxValue) throw new InvalidDataException("Package data exceeds supported size"); return (int)value; }
        internal string Ascii(int at, int count) { Check(at, count); return Encoding.ASCII.GetString(bytes, start + at, count).TrimEnd('\0', ' '); }
        internal string Text(int at, int byteCount)
        {
            Check(at, byteCount);
            if ((byteCount & 1) != 0) throw new InvalidDataException("Invalid UTF-16 resource length");
            try { return new UnicodeEncoding(false, false, true).GetString(bytes, start + at, byteCount); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid UTF-16 package string", error); }
        }
        internal static PackageBytes Read(string path)
        {
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new NotSupportedException("Package metadata exceeds 64 MiB");
            return new PackageBytes(File.ReadAllBytes(path));
        }
    }

    // Windows 8/8.1 PRI structures follow Microsoft's MRTCore format definitions:
    // WindowsAppSDK/dev/MRTCore/mrt/mrm/include/mrm/common/file/{FileBase,MrmFiles,HNamesSection,FileBaseSections}.h.
    // This reader resolves unambiguous local UTF-16 string candidates. Qualified
    // alternatives and unfamiliar layouts fail explicitly rather than choose one.
    internal sealed class PriResources
    {
        private sealed class Section { internal string Kind; internal PackageBytes Data; }
        private readonly List<Section> sections = new List<Section>();
        internal PriResources(string path)
        {
            var file = PackageBytes.Read(path);
            string magic = file.Ascii(0, 8);
            if ((magic != "mrm_pri0" && magic != "mrm_pri1") || file.Int(12) != file.Length || file.U16(8) != 0 || file.U16(10) != 1)
                throw new InvalidDataException("Unsupported or invalid PRI header");
            int count = file.U16(24), toc = file.Int(16), data = file.Int(20);
            file.Slice(toc, checked(count * 32));
            if (file.U32(file.Length - 16) != 0xdefffade || file.Int(file.Length - 12) != file.Length || file.Ascii(file.Length - 8, 8) != magic)
                throw new InvalidDataException("Invalid PRI trailer");
            for (int i = 0; i < count; i++)
            {
                int entry = toc + i * 32, size = file.Int(entry + 28);
                var section = file.Slice(checked(data + file.Int(entry + 24)), size);
                string kind = file.Ascii(entry, 16);
                if (section.Ascii(0, 16) != kind || section.Int(24) != size || section.U32(size - 8) != 0xdef5fade || section.Int(size - 4) != size)
                    throw new InvalidDataException("Invalid PRI section");
                sections.Add(new Section { Kind = kind, Data = section.Slice(32, size - 40) });
            }
        }
        private Section At(int index, string kind)
        {
            if (index < 0 || index >= sections.Count || sections[index].Kind != kind) throw new InvalidDataException("Invalid PRI section reference");
            return sections[index];
        }
        private Dictionary<string, int> Names(int index)
        {
            var schema = At(index, "[mrm_hschema]").Data;
            int at = checked(8 + schema.U16(0) * 20 + (schema.U16(2) + schema.U16(4)) * 2);
            at = (at + 3) & ~3;
            var h = schema.Slice(at, schema.Length - at);
            if (h.U16(2) != 0) throw new NotSupportedException("Large PRI name tables are unsupported");
            int nodes = h.Int(4), scopes = h.Int(8), items = h.Int(12);
            int itemTable = checked(24 + nodes * 12 + scopes * 8), names = checked(itemTable + items * 2);
            var namePool = h.Slice(names, checked(h.Int(16) * 2));
            var paths = new string[nodes];
            long totalPathLength = 0;
            for (int i = 0; i < nodes; i++)
            {
                int node = 24 + i * 12, parent = h.U16(node), flags = h.Byte(node + 7);
                if ((flags & 0xc0) != 0 || (flags & 0x20) != 0) throw new NotSupportedException("Extended PRI name encoding is unsupported");
                if (i != 0 && parent >= i) throw new InvalidDataException("PRI name hierarchy has an invalid parent");
                int offset = h.U16(node + 8) | (flags & 15) << 16;
                string name = namePool.Text(checked(offset * 2), h.Byte(node + 6) * 2);
                paths[i] = (i == 0 || paths[parent].Length == 0 ? "" : paths[parent] + "/") + name;
                if (paths[i].Length > h.U16(0)) throw new InvalidDataException("PRI name exceeds its declared longest path");
                totalPathLength += paths[i].Length;
                if (totalPathLength > 64 * 1024 * 1024) throw new NotSupportedException("Expanded PRI name hierarchy exceeds 64 MiB characters");
            }
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < items; i++)
            {
                int node = h.U16(itemTable + i * 2);
                if (node >= nodes) throw new InvalidDataException("Invalid PRI item name");
                result.Add(paths[node], i);
            }
            return result;
        }
        internal string GetString(string key)
        {
            foreach (var section in sections)
            {
                if (section.Kind != "[mrm_res_map__]" && section.Kind != "[mrm_res_map2_]") continue;
                var map = section.Data;
                int schemaIndex;
                if (!Names(map.U16(4)).TryGetValue(key, out schemaIndex)) continue;
                if (map.U16(6) != 0 || map.U16(18) != 0 || map.Int(28) != 0) throw new NotSupportedException("External or large PRI resource maps are unsupported");
                int types = map.U16(10), dirs = map.U16(12), ranges = map.U16(14), items = map.U16(16);
                int typeTable = checked(32 + map.U16(0)), dirTable = checked(typeTable + types * 8);
                int rangeTable = checked(dirTable + dirs * 4), itemTable = checked(rangeTable + ranges * 4), valueTable = checked(itemTable + items * 4);
                for (int i = 0; i < dirs; i++)
                {
                    int dir = dirTable + i * 4, first = map.U16(dir), offset = map.U16(dir + 2), item;
                    if (offset < ranges)
                    {
                        int range = rangeTable + offset * 4;
                        if (schemaIndex < first || schemaIndex - first >= map.U16(range)) continue;
                        item = map.U16(range + 2) + schemaIndex - first;
                    }
                    else { if (schemaIndex != first) continue; item = offset - ranges; }
                    if (item >= items) throw new InvalidDataException("Invalid PRI resource index");
                    int row = itemTable + item * 4, decision = map.U16(row), value = map.U16(row + 2);
                    var decisions = At(map.U16(8), "[mrm_decn_info]").Data;
                    if (decision >= decisions.U16(6)) throw new InvalidDataException("Invalid PRI decision index");
                    int candidates = decisions.U16(12 + decision * 4 + 2);
                    if (candidates == 0) return "";
                    if (candidates != 1) throw new NotSupportedException("PRI qualifier selection is required for " + key);
                    if (value >= map.Int(20)) throw new InvalidDataException("Invalid PRI candidate index");
                    int candidate = valueTable + value * 8, type = map.Byte(candidate + 1);
                    if (type >= types) throw new InvalidDataException("Invalid PRI value type");
                    if (map.U32(typeTable + type * 8) != 4 || map.U32(typeTable + type * 8 + 4) != 0)
                        throw new NotSupportedException("PRI candidate is not a UTF-16 string: " + key);
                    if (map.Byte(candidate) != 1 || map.U16(candidate + 2) != 0) throw new NotSupportedException("Non-local PRI string storage is unsupported");
                    var data = At(map.U16(candidate + 6), "[mrm_dataitem]").Data;
                    if (data.U32(0) != 0) throw new NotSupportedException("Extended PRI data item table is unsupported");
                    int small = data.U16(4), large = data.U16(6), index = map.U16(candidate + 4);
                    if (index >= small + large) throw new InvalidDataException("Invalid PRI data item");
                    int dataStart = checked(12 + small * 4 + large * 8), start, size;
                    if (index < small) { start = data.U16(12 + index * 4); size = data.U16(14 + index * 4); }
                    else { int entry = 12 + small * 4 + (index - small) * 8; start = data.Int(entry); size = data.Int(entry + 4); }
                    var pool = data.Slice(dataStart, data.Int(8));
                    if (size < 2 || pool.U16(checked(start + size - 2)) != 0) throw new InvalidDataException("PRI string lacks a terminator");
                    return pool.Text(start, size - 2);
                }
            }
            return "";
        }
    }
}
