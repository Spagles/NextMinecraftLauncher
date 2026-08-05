using NML.Core.Modloaders.Forge;

namespace NML.Core.Tests;

/// <summary>
/// Validates the Forge processor type registry — the classification of well-known processor
/// types (SpecialSource/JarSigner/BinaryPatch/Copy/Generic) and their arg-count expectations.
/// These classifications drive type-specific conventions in the executor.
/// </summary>
public class ForgeProcessorTypeRegistryTests
{
    [Fact]
    public void SpecialSource_classified_by_jar_name()
    {
        var p = new ForgeProcessor { Jar = "net.md-5:SpecialSource:1.8.5" };
        ForgeProcessorTypeRegistry.Classify(p).Should().Be(ForgeProcessorKind.SpecialSource);
    }

    [Fact]
    public void BinaryPatch_classified_by_jar_name()
    {
        var p = new ForgeProcessor { Jar = "net.minecraftforge:binarypatcher:0.1.0" };
        ForgeProcessorTypeRegistry.Classify(p).Should().Be(ForgeProcessorKind.BinaryPatch);
    }

    [Fact]
    public void JarSigner_classified_by_jar_name()
    {
        var p = new ForgeProcessor { Jar = "net.minecraftforge:jarsigner:1.0", Args = new() };
        ForgeProcessorTypeRegistry.Classify(p).Should().Be(ForgeProcessorKind.JarSigner);
    }

    [Fact]
    public void JarSigner_classified_by_signing_args()
    {
        var p = new ForgeProcessor
        {
            Jar = "some:signingfix:1.0",
            Args = new() { "--signedJar", "out.jar", "--jar", "in.jar" },
        };
        ForgeProcessorTypeRegistry.Classify(p).Should().Be(ForgeProcessorKind.JarSigner);
    }

    [Fact]
    public void Copy_classified_by_extract_processor_name()
    {
        var p = new ForgeProcessor { Jar = "com.example:extractprocessor:1.0" };
        ForgeProcessorTypeRegistry.Classify(p).Should().Be(ForgeProcessorKind.Copy);
    }

    [Fact]
    public void Unknown_jar_is_generic()
    {
        var p = new ForgeProcessor { Jar = "com.example:randomtool:1.0" };
        ForgeProcessorTypeRegistry.Classify(p).Should().Be(ForgeProcessorKind.Generic);
    }

    [Fact]
    public void Null_jar_is_generic()
    {
        var p = new ForgeProcessor { Jar = null };
        ForgeProcessorTypeRegistry.Classify(p).Should().Be(ForgeProcessorKind.Generic);
    }

    [Theory]
    [InlineData(ForgeProcessorKind.SpecialSource, 4)]
    [InlineData(ForgeProcessorKind.JarSigner, 3)]
    [InlineData(ForgeProcessorKind.BinaryPatch, 3)]
    public void Typed_processors_expect_minimum_args(ForgeProcessorKind kind, int expected)
    {
        ForgeProcessorTypeRegistry.ExpectedMinArgs(kind).Should().Be(expected);
    }

    [Fact]
    public void Generic_and_copy_have_no_arg_expectation()
    {
        ForgeProcessorTypeRegistry.ExpectedMinArgs(ForgeProcessorKind.Generic).Should().BeNull();
        ForgeProcessorTypeRegistry.ExpectedMinArgs(ForgeProcessorKind.Copy).Should().BeNull();
    }

    [Fact]
    public void Describe_is_human_readable()
    {
        ForgeProcessorTypeRegistry.Describe(ForgeProcessorKind.SpecialSource).Should().Contain("deobf");
        ForgeProcessorTypeRegistry.Describe(ForgeProcessorKind.JarSigner).Should().Contain("sign");
        ForgeProcessorTypeRegistry.Describe(ForgeProcessorKind.BinaryPatch).Should().Contain("patch");
    }
}
