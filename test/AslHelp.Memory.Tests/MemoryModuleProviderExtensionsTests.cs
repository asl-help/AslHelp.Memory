using NUnit.Framework;

namespace AslHelp.Memory.Tests;

[TestFixture]
public class MemoryModuleProviderExtensionsTests
{
    // GetModule matches the name in any case.
    [Test]
    public void GetModule_DifferentCase_FindsTheModule()
    {
        BufferMemory memory = new(0, [], MemoryLayout.X64);
        MemoryModule game = new("Game.exe", @"C:\Game\Game.exe", new(new Address(0x400000), 0x1000));
        memory.Modules.Add(new("kernel32.dll", null, new(new Address(0x7FF00000), 0x1000)));
        memory.Modules.Add(game);

        Assert.That(memory.GetModule("GAME.EXE").Unwrap(), Is.SameAs(game));
    }

    // GetModule returns an error for a module that isn't loaded.
    [Test]
    public void GetModule_Missing_ReturnsAnError()
    {
        BufferMemory memory = new(0, [], MemoryLayout.X64);
        memory.Modules.Add(new("Game.exe", null, new(new Address(0x400000), 0x1000)));

        Assert.That(memory.GetModule("other.dll").IsErr, Is.True);
    }
}
