using NML.AICore.Providers;

namespace NML.AICore.Tests;

/// <summary>
/// Smoke test proving the AICore test harness runs and core types resolve.
/// Real feature tests live alongside.
/// </summary>
public class AiCoreSmokeTests
{
    [Fact]
    public void Provider_config_and_fake_client_are_accessible()
    {
        var fake = new FakeChatClient("hello");
        fake.Provider.Model.Should().Be("fake-model");
        fake.CallCount.Should().Be(0);
    }
}
