using AslHelp.Memory.Errors;
using System;

namespace AslHelp.Memory;

/// <summary>
///     Provides module lookups over an <see cref="IMemoryModuleProvider"/>.
/// </summary>
public static class MemoryModuleProviderExtensions
{
    extension(IMemoryModuleProvider provider)
    {
        /// <summary>
        ///     Finds the loaded module whose name matches <paramref name="name"/>, ignoring case.
        /// </summary>
        /// <param name="name">The file name of the module, such as <c>kernel32.dll</c>.</param>
        /// <returns>
        ///     The first matching module on success, otherwise a failed result.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     The provider or <paramref name="name"/> is <see langword="null"/>.
        /// </exception>
        public Result<MemoryModule> GetModule(string name)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(name);

            var modules = provider.GetModules();
            if (!modules.TryUnwrap(out var list, out var error))
            {
                return error;
            }

            foreach (var module in list)
            {
                if (string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return module;
                }
            }

            return MemoryError.ModuleNotFound(name);
        }
    }
}
