using System.Windows;
using Telemetry;

namespace VolumetricLight.Tests;

public sealed class HostIntegrationTests
{
    [Fact]
    public void OutsideAWpfApplicationNoTelemetryIsStartedOrSent()
    {
        Assert.Null(Application.Current);

        VolumetricLightTelemetry.EnsureStartedOnce();
        VolumetricLightTelemetry.Report(new InvalidOperationException());

        Assert.Null(ProcessState.Read("DrainClaimed"));
        Assert.Null(ProcessState.Read("SentCount"));
    }

    [Fact]
    public void OutsideAWpfApplicationTheEffectCanStillBeCreated()
    {
        Assert.Null(Application.Current);

        var effect = new VolumetricLightEffect();

        Assert.Equal(Texts.VolumetricLight, effect.Label);
        Assert.Null(ProcessState.Read("DrainClaimed"));
    }
}
