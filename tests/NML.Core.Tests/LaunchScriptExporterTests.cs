using NML.Core.Launch;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="LaunchScriptExporter"/> — HMCL's "export launch script" feature. The
/// exported .bat/.sh must reproduce the exact java command line and be runnable directly.
/// </summary>
public class LaunchScriptExporterTests
{
    private static readonly string[] SampleArgs =
    {
        "-Xmx2048M",
        "-Djava.library.path=bin\\natives",
        "-cp", "libraries/*;versions/1.20.1/1.20.1.jar",
        "net.minecraft.client.main.Main",
        "--username", "Player With Spaces",
        "--gameDir", ".",
    };

    [Fact]
    public void Bat_Script_Has_Essential_Structure()
    {
        string script = LaunchScriptExporter.BuildScript(
            @"C:\Program Files\Java\bin\javaw.exe", SampleArgs,
            @"C:\games\.minecraft", "bat");

        script.Should().Contain("@echo off");
        script.Should().Contain("cd /d \"C:\\games\\.minecraft\"");
        // The java path (has spaces) must be quoted.
        script.Should().Contain("\"C:\\Program Files\\Java\\bin\\javaw.exe\"");
        // All args survive.
        script.Should().Contain("-Xmx2048M");
        script.Should().Contain("net.minecraft.client.main.Main");
        // An arg containing spaces must be quoted.
        script.Should().Contain("\"Player With Spaces\"");
        script.Should().Contain("pause");
        // No stray newlines inside the command (single-line invocation).
        var commandLine = script.Lines().First(l => l.Contains("javaw.exe"));
        commandLine.Should().NotContain("\n");
    }

    [Fact]
    public void Sh_Script_Has_Essential_Structure()
    {
        string script = LaunchScriptExporter.BuildScript(
            "/usr/bin/java", SampleArgs, "/home/user/.minecraft", "sh");

        script.Should().StartWith("#!/bin/sh");
        script.Should().Contain("cd \"/home/user/.minecraft\"");
        script.Should().Contain("'/usr/bin/java'");
        // POSIX single-quoting: spaces inside an arg are preserved within quotes.
        script.Should().Contain("'Player With Spaces'");
        script.Should().Contain("net.minecraft.client.main.Main");
        // No .bat artifacts.
        script.Should().NotContain("@echo off");
        script.Should().NotContain("pause");
    }

    [Fact]
    public void Sh_Quotes_Single_Quotes_In_Args()
    {
        // An arg containing a single quote must be escaped as '\'' per POSIX.
        string script = LaunchScriptExporter.BuildScript(
            "/usr/bin/java", new[] { "-Dname=it's" }, "/g", "sh");
        script.Should().Contain("-Dname=it'\\''s");
    }

    [Fact]
    public void Bat_Does_Not_Quote_Simple_Args()
    {
        string script = LaunchScriptExporter.BuildScript(
            "javaw.exe", new[] { "-Xmx1G", "main" }, @"C:\g", "bat");
        // No-space args should remain unquoted for readability.
        script.Should().Contain("javaw.exe -Xmx1G main");
    }

    [Fact]
    public void Export_Writes_File_With_Correct_Extension_Content()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-script-" + Guid.NewGuid().ToString("N")[..8]);
        string path = Path.Combine(dir, "nested", "launch.bat");
        try
        {
            LaunchScriptExporter.Export("javaw.exe", new[] { "-Xmx1G", "Main" }, @"C:\g", path);
            File.Exists(path).Should().BeTrue("export must create the file (and parent dirs)");
            string content = File.ReadAllText(path);
            content.Should().Contain("@echo off");
            content.Should().Contain("javaw.exe -Xmx1G Main");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Empty_Java_Or_Args_Throws()
    {
        var act1 = () => LaunchScriptExporter.BuildScript("", new[] { "a" }, "/g", "bat");
        act1.Should().Throw<ArgumentException>();
        var act2 = () => LaunchScriptExporter.BuildScript("java", Array.Empty<string>(), "/g", "bat");
        act2.Should().Throw<ArgumentException>();
    }
}

file static class StringExt
{
    public static IEnumerable<string> Lines(this string s)
    {
        using var sr = new StringReader(s);
        while (sr.ReadLine() is { } line) yield return line;
    }
}
