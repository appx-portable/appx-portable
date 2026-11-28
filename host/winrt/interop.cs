// Shared cross-assembly projection contract. No SDK or Cecil dependency.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
[assembly: AssemblyVersion("1.0.0.0")]
namespace AppxPortable.WinRT.Interop
{
    public interface INativeObject
    {
        // The caller owns the returned interface lease and must dispose it.
        SafeHandle AcquireInterface(Guid iid);
    }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Interface, Inherited = false)]
    public sealed class ProjectionTypeAttribute : Attribute
    {
        public string InterfaceId { get; private set; }
        public ProjectionTypeAttribute(string interfaceId) { InterfaceId = interfaceId; }
    }
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class ProjectionProviderAttribute : Attribute
    {
        public Type Provider { get; private set; }
        public ProjectionProviderAttribute(Type provider) { Provider = provider; }
    }
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ProjectionContractAttribute : Attribute
    {
        public string Name { get; private set; }
        public string InterfaceId { get; private set; }
        public ProjectionContractAttribute(string name, string interfaceId) { Name = name; InterfaceId = interfaceId; }
    }
    public interface IProjectionProvider
    {
        // The lease is borrowed for this call only. The provider retains its own reference.
        object Wrap(Type type, SafeHandle borrowed);
    }
    public interface IProjectionArgumentProvider
    {
        // The caller owns the lease. The declaring projection owns the callback vtable.
        SafeHandle AcquireArgument(Type type, object target, Guid iid);
    }

    public static class ProjectionInterop
    {
        private static readonly object gate = new object();
        private static readonly Dictionary<Assembly, IProjectionProvider> providers = new Dictionary<Assembly, IProjectionProvider>();

        private static IProjectionProvider Provider(Type contract)
        {
            var assembly = contract.Assembly;
            lock (gate)
            {
                IProjectionProvider provider;
                if (!providers.TryGetValue(assembly, out provider))
                {
                    var attributes = assembly.GetCustomAttributes(typeof(ProjectionProviderAttribute), false);
                    if (attributes.Length != 1) throw new NotSupportedException("No projection provider: " + contract.FullName);
                    var type = ((ProjectionProviderAttribute)attributes[0]).Provider;
                    if (type.Assembly != assembly || !typeof(IProjectionProvider).IsAssignableFrom(type))
                        throw new NotSupportedException("Invalid projection provider: " + type.FullName);
                    provider = (IProjectionProvider)Activator.CreateInstance(type);
                    providers.Add(assembly, provider);
                }
                return provider;
            }
        }

        // Consumes the owned lease on success, null, and every failure path.
        public static T Wrap<T>(SafeHandle owned) where T : class
        {
            if (owned == null) return null;
            using (owned) return (T)Provider(typeof(T)).Wrap(typeof(T), owned);
        }

        public static SafeHandle AcquireArgument<T>(T target, Guid iid) where T : class
        {
            if (target == null) return null;
            var provider = Provider(typeof(T)) as IProjectionArgumentProvider;
            if (provider == null) throw new NotSupportedException("No projection argument provider: " + typeof(T).FullName);
            return provider.AcquireArgument(typeof(T), target, iid);
        }
    }
}
