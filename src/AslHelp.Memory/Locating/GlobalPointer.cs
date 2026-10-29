using AslHelp.Logging;
using System;
using System.Diagnostics;

namespace AslHelp.Memory.Locating;

/// <summary>
///     Represents a pointer the game stores in a global, located once and read fresh on each access.
/// </summary>
/// <remarks>
///     A failed locate is tried again once <paramref name="retryAfter"/> has passed, so a game that sets the code up
///     late is still found. The wait is measured in time, so reading the global many times in 1 tick doesn't search
///     again. Once the global is located, <see cref="Read"/> doesn't allocate anything.
/// </remarks>
/// <param name="reader">The memory to read from.</param>
/// <param name="module">The module the routes start at.</param>
/// <param name="locator">The routes to the global.</param>
/// <param name="retryAfter">The time to wait after a failed locate before the next one.</param>
/// <param name="logger">The logger to write the outcome of each locate to, if any.</param>
public sealed class GlobalPointer(
    IMemoryReader reader,
    MemoryModule module,
    MemoryLocator locator,
    TimeSpan retryAfter,
    Logger? logger = null)
{
    private readonly IMemoryReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly MemoryModule _module = module ?? throw new ArgumentNullException(nameof(module));
    private readonly MemoryLocator _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    private readonly long _retryTicks = (long)(retryAfter.TotalSeconds * Stopwatch.Frequency);

    private Address _global;
    private bool _isLocated;
    private ResultError? _error;
    private long _retryAt;

    /// <summary>
    ///     Gets the address of the global, locating it when it isn't located yet and the wait has passed.
    /// </summary>
    /// <returns>
    ///     The address of the global on success, otherwise the error of the last locate.
    /// </returns>
    public Result<Address> Locate()
    {
        if (_isLocated)
        {
            return _global;
        }

        if (_error is not null && Stopwatch.GetTimestamp() < _retryAt)
        {
            return _error;
        }

        if (!_locator.Locate(_reader, _module, logger).TryUnwrap(out _global, out _error))
        {
            _retryAt = Stopwatch.GetTimestamp() + _retryTicks;
            return _error;
        }

        _isLocated = true;
        return _global;
    }

    /// <summary>
    ///     Reads the pointer in the global now.
    /// </summary>
    /// <returns>
    ///     The pointer on success, otherwise a failed result.
    /// </returns>
    public Result<Address> Read()
    {
        var located = Locate();
        if (!located.TryUnwrap(out var address, out var error))
        {
            return error;
        }

        return _reader.ReadPointer(address);
    }
}
