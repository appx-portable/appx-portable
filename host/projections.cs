// Generated component projections live apart from their same-named native DLLs.
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml;
internal static class ComponentProjections
{
    internal static void Install(string directory)
    {
        string root = Path.Combine(directory, "appx-projections");
        InstallRuntimeOverrides(directory, root);
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var requested = new AssemblyName(args.Name);
            string name = requested.Name;
            if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name == "." || name == "..") return null;
            string path = Path.Combine(root, name + ".dll");
            if (!File.Exists(path)) return null;
            var actual = AssemblyName.GetAssemblyName(path);
            if (!string.Equals(actual.Name, name, StringComparison.OrdinalIgnoreCase) || actual.Version != requested.Version ||
                !string.Equals(actual.CultureName ?? "", requested.CultureName ?? "", StringComparison.OrdinalIgnoreCase) ||
                Convert.ToBase64String(actual.GetPublicKeyToken() ?? new byte[0]) != Convert.ToBase64String(requested.GetPublicKeyToken() ?? new byte[0]))
            {
                string message = "Generated component projection identity does not match " + args.Name;
                // Mono replaces resolver exceptions with FileNotFoundException;
                // retain the reason in the host log before returning to the loader.
                ManagedLog.AppendLog(Path.Combine(directory, "managed.log"), "[projection] " + message + "\n");
                throw new FileLoadException(message, path);
            }
            return Assembly.LoadFrom(path);
        };
    }
    private static string Digest(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    private static void InstallRuntimeOverrides(string directory, string root)
    {
        if (Type.GetType("Mono.Runtime") == null) return;
        string manifest = Path.Combine(root, "runtime-overrides.xml");
        if (!File.Exists(manifest)) return;
        var document = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
        if (document.DocumentElement == null || document.DocumentElement.Name != "RuntimeOverrides")
            throw new InvalidDataException("Invalid runtime override manifest");
        foreach (XmlNode node in document.DocumentElement.ChildNodes)
        {
            var entry = node as XmlElement;
            if (entry == null) continue;
            string name = entry.GetAttribute("Name");
            if (entry.Name != "Assembly" || name.Length == 0 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid runtime override filename");
            string original = Path.Combine(directory, name), compatible = Path.Combine(Path.Combine(root, "runtime-overrides"), name);
            if (Digest(original) != entry.GetAttribute("SourceSha256") || Digest(compatible) != entry.GetAttribute("Sha256"))
                throw new InvalidDataException("Runtime override hash mismatch: " + name);
            string identity = entry.GetAttribute("Identity");
            if (AssemblyName.GetAssemblyName(original).FullName != identity || AssemblyName.GetAssemblyName(compatible).FullName != identity)
                throw new FileLoadException("Runtime override identity mismatch: " + name);
            Assembly.LoadFrom(compatible);
            ManagedLog.AppendLog(Path.Combine(directory, "managed.log"), "[host] Mono runtime override " + name + "\n");
        }
    }
}
