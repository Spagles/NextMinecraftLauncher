using NML.Core.Models;

namespace NML.Core.Tests;

public class MavenCoordinateTests
{
    [Fact]
    public void Parses_group_artifact_version()
    {
        var c = MavenCoordinate.Parse("com.mojang:authlib:4.0.43");

        c.Group.Should().Be("com.mojang");
        c.Artifact.Should().Be("authlib");
        c.Version.Should().Be("4.0.43");
        c.Classifier.Should().BeNull();
    }

    [Fact]
    public void Parses_classifier()
    {
        var c = MavenCoordinate.Parse("org.lwjgl:lwjgl:3.3.1:natives-windows");

        c.Classifier.Should().Be("natives-windows");
    }

    [Fact]
    public void RelativePath_matches_maven_layout()
    {
        var c = MavenCoordinate.Parse("com.mojang:authlib:4.0.43");

        c.RelativePath.Should().Be("com/mojang/authlib/4.0.43/authlib-4.0.43.jar");
    }

    [Fact]
    public void RelativePath_includes_classifier()
    {
        var c = MavenCoordinate.Parse("org.lwjgl:lwjgl:3.3.1:natives-linux");

        c.RelativePath.Should().Be("org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar");
    }

    [Fact]
    public void Throws_on_malformed()
    {
        Action act = () => MavenCoordinate.Parse("not-valid");
        act.Should().Throw<FormatException>();
    }
}
