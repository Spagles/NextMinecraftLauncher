using NML.Core.Java;

namespace NML.Core.Tests;

public class JavaVersionParsingTests
{
    [Theory]
    [InlineData("openjdk version \"17.0.9\" 2023-10-17", 17)]
    [InlineData("java version \"1.8.0_362\"", 8)]
    [InlineData("openjdk version \"21.0.1\" 2023-10-17", 21)]
    [InlineData("openjdk version \"25\" 2025-09-16", 25)]
    [InlineData("openjdk version \"1.8.0_271\"", 8)]
    public void Parses_major_version_from_java_dash_version(string output, int expected)
    {
        JavaRuntimeDetector.ParseMajorVersion(output).Should().Be(expected);
    }

    [Fact]
    public void Returns_zero_for_unrecognized_output()
    {
        JavaRuntimeDetector.ParseMajorVersion("garbage with no version").Should().Be(0);
    }
}
