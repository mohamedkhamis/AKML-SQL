using System;
using System.IO;
using System.Reflection;

namespace AkmlSql.Shell.Shared
{
    /// <summary>
    /// Resolves assemblies from the extension directory when the default resolver fails.
    /// This avoids using BindingPaths in pkgdef (which pollutes the global probing path
    /// and can break other SSMS/VS packages like Azure.Identity).
    /// Must be registered BEFORE any code that loads extension dependencies (System.Text.Json, etc.).
    /// </summary>
    public static class ExtensionAssemblyResolver
    {
        private static string _extensionDir;
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            _extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (_extensionDir == null) return null;
            var name = new AssemblyName(args.Name);
            var candidate = Path.Combine(_extensionDir, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                try { return Assembly.LoadFrom(candidate); }
                catch { /* fall through to default resolution */ }
            }
            return null;
        }
    }
}
