using NML.Core.Java;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the pre-launch Java compatibility check: <see cref="JavaVersionValidator"/> gates a
/// launch on the runtime's major version being greater-than-or-equal to the version's required
/// major, so a too-old runtime (e.g. Java 8 for 1.17+, which would crash instantly) is caught
/// before launch with a clear reason.
/// </summary>
public class JavaVersionValidatorTests
{
    [Theory]
    // Exact match: 17 for 17, 8 for 8 → OK.
    [InlineData(17, 17, true)]
    [InlineData(8, 8, true)]
    // Newer-than-required: 21 for 17, 17 for 16 → OK (Minecraft is forward-compatible).
    [InlineData(17, 21, true)]
    [InlineData(16, 17, true)]
    // Older-than-required: 8 for 17, 16 for 17 → NOT OK.
    [InlineData(17, 8, false)]
    [InlineData(17, 16, false)]
    [InlineData(21, 17, false)]
    public void Validate_Honors_Greater_Or_Equal(int required, int actual, bool expectedOk)
    {
        var r = JavaVersionValidator.Validate(required, actual);
        r.Ok.Should().Be(expectedOk);
        if (!expectedOk) r.Reason.Should().Be(JavaIncompatibilityReason.TooOld);
        else             r.Reason.Should().Be(JavaIncompatibilityReason.None);
    }

    [Fact]
    public void Validate_TooOld_Message_Quotes_Both_Versions()
    {
        // The message must tell the user both the actual and required majors so they know what to fix.
        var r = JavaVersionValidator.Validate(17, 8);
        r.Ok.Should().BeFalse();
        r.Message.Should().Contain("8");
        r.Message.Should().Contain("17");
    }

    [Fact]
    public void Validate_Ok_Has_Empty_Message()
    {
        JavaVersionValidator.Validate(17, 21).Message.Should().BeEmpty();
    }

    [Fact]
    public void Validate_Null_Runtime_Reports_Missing()
    {
        var r = JavaVersionValidator.Validate(17, (JavaRuntime?)null);
        r.Ok.Should().BeFalse();
        r.Reason.Should().Be(JavaIncompatibilityReason.Missing);
        r.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_Runtime_Overload_Delegates_To_MajorVersion()
    {
        // The runtime overload must agree with the int overload for the same major version.
        var runtime = new JavaRuntime { BinDirectory = "/j/bin", ExecutablePath = "/j/bin/java", MajorVersion = 17 };
        var byRuntime = JavaVersionValidator.Validate(17, runtime);
        var byInt = JavaVersionValidator.Validate(17, 17);
        byRuntime.Ok.Should().Be(byInt.Ok);
        byRuntime.Reason.Should().Be(byInt.Reason);
    }

    [Theory]
    // Real Minecraft version → Java-major boundaries the validator must respect.
    [InlineData(1, 17, 17, true)]   // 1.18 needs 17, has 17 → OK
    [InlineData(1, 16, 17, true)]   // 1.17 needs 16, has 17 → OK
    [InlineData(1, 17, 8, false)]   // 1.18 needs 17, has 8 → block (the common crash case)
    [InlineData(1, 8, 8, true)]     // 1.8-era needs 8, has 8 → OK
    public void Real_Minecraft_Java_Boundaries(int _dummy, int required, int actual, bool ok)
    {
        _ = _dummy;
        JavaVersionValidator.Validate(required, actual).Ok.Should().Be(ok);
    }
}
