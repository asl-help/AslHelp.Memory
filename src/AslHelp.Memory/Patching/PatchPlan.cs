namespace AslHelp.Memory.Patching;

/// <summary>
///     Represents what an install settles about one entry before it knows where the stub will go.
/// </summary>
/// <remarks>
///     Everything here is read out of the entry and the layout alone, so a plan is what an install can be refused on
///     before any of the target's memory is taken for it.
/// </remarks>
/// <param name="Entry">The method entry to patch.</param>
/// <param name="Original">The bytes the jump displaces, which the stub runs before jumping back.</param>
/// <param name="Layout">What the stub copies out of the call, which may be nothing.</param>
/// <param name="Assembler">What encodes the stub for the target.</param>
internal sealed record PatchPlan(
    Address Entry,
    byte[] Original,
    CaptureLayout Layout,
    IStubAssembler Assembler);
