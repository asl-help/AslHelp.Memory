using AslHelp.Memory.Watching;

namespace AslHelp.Memory.Tests.Watching;

internal sealed class ManualTicks : ITickCounter
{
    public long Value { get; set; }
}
