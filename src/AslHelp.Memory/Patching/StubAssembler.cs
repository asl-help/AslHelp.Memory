namespace AslHelp.Memory.Patching;

/// <summary>
///     Provides the assemblers the library writes stubs with, one per target it can patch.
/// </summary>
internal static class StubAssembler
{
    /// <summary>
    ///     Gets the assembler for a 64-bit Intel target.
    /// </summary>
    public static IStubAssembler X64 { get; } = new X64StubAssembler();

    /// <summary>
    ///     Gets the assembler for a 32-bit Intel target.
    /// </summary>
    public static IStubAssembler X86 { get; } = new X86StubAssembler();

    /// <summary>
    ///     Picks the assembler for a target by its width.
    /// </summary>
    /// <param name="is64Bit">Whether the target is a 64-bit process.</param>
    /// <returns>
    ///     The assembler.
    /// </returns>
    public static IStubAssembler Create(bool is64Bit)
    {
        return is64Bit ? X64 : X86;
    }
}
