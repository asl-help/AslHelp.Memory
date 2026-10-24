using AslHelp.Logging;
using AslHelp.Memory.Locating.Errors;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Represents named routes to 1 address, tried until 1 of them locates it.
/// </summary>
/// <remarks>
///     A game that ships in more than 1 build often needs a different pattern per build, so each build gets a route of
///     its own. In JSON a locator is an object whose keys are the route names and whose values are arrays of steps.
/// </remarks>
[JsonConverter(typeof(MemoryLocatorConverter))]
public sealed class MemoryLocator
{
    private readonly Dictionary<string, LocatorRoute> _routes;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemoryLocator"/> class.
    /// </summary>
    /// <param name="routes">The routes to try.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="routes"/> or 1 of the routes is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="routes"/> is empty. 2 routes have the same name.
    /// </exception>
    public MemoryLocator(params LocatorRoute[] routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        if (routes.Length == 0)
        {
            throw new ArgumentException("A locator needs at least 1 route.", nameof(routes));
        }

        _routes = new(routes.Length, StringComparer.Ordinal);

        // Routes are looked up by name and an error includes the name of the route that failed, so names must be unique.
        foreach (var route in routes)
        {
            ArgumentNullException.ThrowIfNull(route, nameof(routes));

            if (!_routes.TryAdd(route.Name, route))
            {
                throw new ArgumentException($"2 routes are named '{route.Name}'. Give each route its own name.", nameof(routes));
            }
        }
    }

    /// <summary>
    ///     Gets the routes by name.
    /// </summary>
    /// <remarks>
    ///     <c>Routes["name"].Locate(reader, module)</c> runs only that route.
    /// </remarks>
    public IReadOnlyDictionary<string, LocatorRoute> Routes => _routes;

    /// <summary>
    ///     Tries the routes until 1 of them succeeds and returns its address.
    /// </summary>
    /// <param name="reader">The memory to read from.</param>
    /// <param name="module">The module the routes start at.</param>
    /// <param name="logger">The logger to write which route located the address to, if any.</param>
    /// <returns>
    ///     The address on success, otherwise a failed result that lists each route with its error.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader"/> or <paramref name="module"/> is <see langword="null"/>.
    /// </exception>
    public Result<Address> Locate(IMemoryReader reader, MemoryModule module, Logger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(module);

        StringBuilder failures = new();

        foreach (var route in _routes.Values)
        {
            if (route.Locate(reader, module).TryUnwrap(out var address, out var error))
            {
                logger?.LogDebug($"Route '{route.Name}' located {address}.");
                return address;
            }

            logger?.LogTrace($"Route '{route.Name}' failed. {error.Message}");

            if (failures.Length > 0)
            {
                _ = failures.Append(' ');
            }

            _ = failures.Append('\'').Append(route.Name).Append("': ").Append(error.Message);
        }

        return LocatorError.NoRouteLocated(failures.ToString());
    }

    /// <summary>
    ///     Formats the locator as its route names joined by <c>or</c>.
    /// </summary>
    /// <returns>
    ///     The formatted locator.
    /// </returns>
    public override string ToString()
    {
        return string.Join(" or ", _routes.Keys);
    }
}
