using NUnit.Framework;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AslHelp.Memory.Remote.Tests;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WriteAccessTests
{
    // Matches PROCESS_VM_OPERATION | PROCESS_VM_WRITE.
    private const int WriteRights = 0x0008 | 0x0020;

    // A process that refuses the rights to write still opens and reads, but a write fails.
    [Test]
    public void Write_ProcessRefusesWriteRights_ReadsAndFailsTheWrite()
    {
        using var child = FixtureChild.Start();
        ProcessSecurity.DenyToEveryone(child.Process.SafeHandle, WriteRights);

        using RemoteProcessMemory memory = new(child.Process);
        byte[] bytes = new byte[8];

        var read = memory.Read(child.Tick, bytes);
        var write = memory.Write(child.Tick, bytes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.IsOk, Is.True);
            Assert.That(write.TryUnwrapErr(out var error), Is.True);
            Assert.That(error?.Message, Does.Contain("refused a handle for writes"));
        }
    }

    // A write to a read-only page lifts the protection for the write and puts it back, because Windows only lifts it
    // for code pages on its own.
    [Test]
    public void Write_ReadOnlyPage_WritesAndKeepsTheProtection()
    {
        using var child = FixtureChild.Start();
        var headers = child.Memory.GetMainModule().Unwrap().Range.Start;
        byte[] bytes = new byte[8];
        child.Memory.Read(headers, bytes).Unwrap();

        var before = ProtectionAt(child, headers);
        var write = child.Memory.Write(headers, bytes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.EqualTo(MemoryProtection.Read));
            Assert.That(write.IsOk, Is.True);
            Assert.That(ProtectionAt(child, headers), Is.EqualTo(before));
        }
    }

    private static MemoryProtection ProtectionAt(FixtureChild child, Address address)
    {
        return child.Memory.GetRegions(new(address, 1)).Unwrap().Single(region => region.Range.Contains(address)).Protection;
    }

    private sealed class ProcessSecurity : NativeObjectSecurity
    {
        private ProcessSecurity(SafeHandle process)
            : base(false, ResourceType.KernelObject, process, AccessControlSections.Access)
        {
        }

        public override Type AccessRightType => typeof(int);

        public override Type AccessRuleType => typeof(ProcessAccessRule);

        public override Type AuditRuleType => typeof(ProcessAuditRule);

        // Adds a deny entry, which Windows checks before every allow entry.
        public static void DenyToEveryone(SafeHandle process, int rights)
        {
            ProcessSecurity security = new(process);
            SecurityIdentifier everyone = new(WellKnownSidType.WorldSid, null);

            security.AddAccessRule(new ProcessAccessRule(everyone, rights, AccessControlType.Deny));
            security.Persist(process, AccessControlSections.Access);
        }

        public override AccessRule AccessRuleFactory(
            IdentityReference identityReference,
            int accessMask,
            bool isInherited,
            InheritanceFlags inheritanceFlags,
            PropagationFlags propagationFlags,
            AccessControlType type)
        {
            return new ProcessAccessRule(identityReference, accessMask, type);
        }

        public override AuditRule AuditRuleFactory(
            IdentityReference identityReference,
            int accessMask,
            bool isInherited,
            InheritanceFlags inheritanceFlags,
            PropagationFlags propagationFlags,
            AuditFlags flags)
        {
            return new ProcessAuditRule(identityReference, accessMask, flags);
        }
    }

    private sealed class ProcessAccessRule(IdentityReference identity, int rights, AccessControlType type)
        : AccessRule(identity, rights, false, InheritanceFlags.None, PropagationFlags.None, type);

    private sealed class ProcessAuditRule(IdentityReference identity, int rights, AuditFlags flags)
        : AuditRule(identity, rights, false, InheritanceFlags.None, PropagationFlags.None, flags);
}
