using System.Drawing;
using System.Numerics;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Tests;

[Collection("Direct2D")]
public sealed class VolumetricLightOccluderEffectProcessorTests
{
    const int Width = 96;
    const int Height = 64;
    const int Length = 30;
    const int GlassMargin = 6;
    const int GlassPeriod = 32;
    const byte GlassLevel = 110;
    const int BarWidth = 8;

    static readonly Size Canvas = new(320, 240);
    static readonly DrawDescription InFront = EffectDescriptions.Flat with { Draw = new Vector3(0f, 56f, 80f) };

    static Bgra Glass(int x, int y)
    {
        var column = x % GlassPeriod;
        if (column < GlassMargin || column >= GlassPeriod - GlassMargin || y < GlassMargin || y >= Height - GlassMargin)
            return Bgra.Opaque(8, 8, 8);

        return (x / GlassPeriod) switch
        {
            0 => Bgra.Opaque(20, 30, GlassLevel),
            1 => Bgra.Opaque(30, GlassLevel, 40),
            _ => Bgra.Opaque(GlassLevel, 60, 20),
        };
    }

    static Bgra Bars(int x, int y) => (x / BarWidth) % 2 == 0 ? Bgra.Opaque(20, 20, 20) : Bgra.Transparent;

    static VolumetricLightEffect Light(int channel)
        => new() { OcclusionChannel = channel };

    static VolumetricLightOccluderEffect Blocker(int channel = 1, double strength = 100d)
    {
        var effect = new VolumetricLightOccluderEffect { Channel = channel };
        effect.Strength.Values[0].Value = strength;
        return effect;
    }

    sealed class Item : IDisposable
    {
        readonly IGraphicsDevicesAndContext context;
        readonly SourceImage source;
        readonly Guid scene;

        public Item(GraphicsDevices devices, YukkuriMovieMaker.Plugin.Effects.VideoEffectBase effect, Func<int, int, Bgra> pixels, Guid scene = default)
        {
            this.scene = scene;
            context = devices.CreateContext();
            source = new SourceImage(context, Width, Height, pixels);
            Processor = effect.CreateVideoEffect(context);
            Processor.SetInput(source.Bitmap);
        }

        public IVideoEffectProcessor Processor { get; }

        public SourceImage Source => source;

        public DrawDescription Update(int frame, DrawDescription draw, Guid? moveTo = null)
            => Processor.Update(EffectDescriptions.At(frame, Length, draw, Canvas, moveTo ?? scene));

        public Rendering Render(int frame, DrawDescription draw, Guid? moveTo = null)
        {
            Update(frame, draw, moveTo);
            return Rendering.Capture(context, Processor.Output);
        }

        public Rendering Capture() => Rendering.Capture(context, Processor.Output);

        public void Dispose()
        {
            Processor.Dispose();
            source.Dispose();
            context.Dispose();
        }
    }

    static bool IsLit(Rendering rendering, int x, int y)
    {
        var pixel = rendering[x, y];
        return pixel.Red != 0 || pixel.Green != 0 || pixel.Blue != 0;
    }

    static long OutsideLight(Rendering rendering)
        => rendering.Coordinates()
            .Where(point => point.X < 0 || point.Y < 0 || point.X >= Width || point.Y >= Height)
            .Sum(point => (long)rendering[point.X, point.Y].Red + rendering[point.X, point.Y].Green + rendering[point.X, point.Y].Blue);

    static Occluder[] Snapshot(IGraphicsDevicesAndContext context, int channel, int frame = 0, Guid scene = default)
    {
        using var lease = OccluderRegistry.Acquire(context, scene, channel);
        var snapshot = new Occluder[VolumetricLightSettings.OccluderCapacity];
        lease.Group.Snapshot(snapshot, frame);
        return snapshot;
    }

    [Fact]
    public void TheOccluderPassesTheImageThroughAndKeepsTheDrawDescription()
    {
        using var devices = new GraphicsDevices();
        using var item = new Item(devices, Blocker(), Bars);

        var draw = item.Update(0, InFront);
        var rendering = item.Capture();

        Assert.Equal(InFront, draw);
        Assert.Equal((0, 0, Width, Height), (rendering.Left, rendering.Top, rendering.Width, rendering.Height));
        Assert.All(rendering.Coordinates(), point => Assert.Equal(item.Source[point.X, point.Y], rendering[point.X, point.Y]));
    }

    [Fact]
    public void TheOccluderPublishesItsStrengthFromTheParameterAndTheOpacityToItsChannel()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var item = new Item(devices, Blocker(2, 40d), Bars);

        item.Update(0, InFront with { Opacity = 0.5d });
        var snapshot = Snapshot(context, 2);

        Assert.Equal(0.2f, snapshot[0].Strength, 5);
        Assert.All(snapshot.Skip(1), occluder => Assert.Equal(0f, occluder.Strength));
        Assert.All(Snapshot(context, 1), occluder => Assert.Equal(0f, occluder.Strength));
    }

    [Fact]
    public void TheOccluderPublishesTheViewOfItsPose()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var item = new Item(devices, Blocker(), Bars);
        var draw = EffectDescriptions.Flat with { Draw = new Vector3(10f, -20f, 30f), Rotation = new Vector3(5f, 15f, 25f), Zoom = new Vector2(1.5f, 0.5f), Invert = true };
        Assert.True(ViewTransform.TryCreate(draw, out var expected));

        item.Update(0, draw);
        var published = Snapshot(context, 1)[0];

        Assert.Equal(expected, published.View);
        Assert.Equal(GoboMapping.Create(draw.Zoom, draw.Invert, draw.CenterPoint), published.Gobo);
    }

    [Fact]
    public void ChangingTheNumberMovesTheOccluderToTheNewChannel()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var effect = Blocker(1);
        using var item = new Item(devices, effect, Bars);
        item.Update(0, InFront);
        Assert.True(Snapshot(context, 1)[0].Strength > 0f);

        effect.Channel = 3;
        item.Update(1, InFront);

        Assert.All(Snapshot(context, 1, 1), occluder => Assert.Equal(0f, occluder.Strength));
        Assert.True(Snapshot(context, 3, 1)[0].Strength > 0f);
    }

    [Fact]
    public void DisposingTheOccluderWithdrawsItFromTheChannel()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var item = new Item(devices, Blocker(), Bars);
        item.Update(0, InFront);
        Assert.True(Snapshot(context, 1)[0].Strength > 0f);

        item.Dispose();

        Assert.All(Snapshot(context, 1), occluder => Assert.Equal(0f, occluder.Strength));
    }

    [Fact]
    public void DisposingTheLightLetsGoOfItsNumber()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        var light = Light(1).CreateVideoEffect(context);
        light.SetInput(source.Bitmap);
        light.Update(EffectDescriptions.At(0, Length, EffectDescriptions.Flat, Canvas));
        var held = OccluderRegistry.Acquire(context, Guid.Empty, 1);
        var group = held.Group;

        light.Dispose();
        held.Dispose();

        using var again = OccluderRegistry.Acquire(context, Guid.Empty, 1);
        Assert.NotSame(group, again.Group);
    }

    [Fact]
    public void OnlyFourOccludersCountOnOneChannelAndTheFifthIsIgnored()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var items = Enumerable.Range(0, VolumetricLightSettings.OccluderCapacity + 1).Select(_ => new Item(devices, Blocker(), Bars)).ToArray();
        try
        {
            foreach (var item in items)
                item.Update(0, InFront);
            var snapshot = Snapshot(context, 1);

            Assert.All(snapshot, occluder => Assert.True(occluder.Strength > 0f));
            items[0].Dispose();
            foreach (var item in items.Skip(1))
                item.Update(1, InFront);
            Assert.All(Snapshot(context, 1, 1), occluder => Assert.True(occluder.Strength > 0f));
        }
        finally
        {
            foreach (var item in items.Skip(1))
                item.Dispose();
        }
    }

    [Theory]
    [InlineData(0f, 0f, 1d)]
    [InlineData(1f, 1f, 0d)]
    public void ADegeneratePoseCastsNoShadow(float zoomX, float zoomY, double opacity)
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var item = new Item(devices, Blocker(), Bars);

        item.Update(0, InFront with { Zoom = new Vector2(zoomX, zoomY), Opacity = opacity });

        Assert.All(Snapshot(context, 1), occluder => Assert.Equal(0f, occluder.Strength));
    }

    [Fact]
    public void WithoutAnInputNothingIsPublishedAndNothingFails()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = Blocker().CreateVideoEffect(context);

        var draw = processor.Update(EffectDescriptions.At(0, Length, InFront, Canvas));

        Assert.Equal(InFront, draw);
        Assert.All(Snapshot(context, 1), occluder => Assert.Equal(0f, occluder.Strength));
    }

    [Fact]
    public void AFailureWhileUpdatingIsNotSwallowed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var processor = Blocker().CreateVideoEffect(context);

        Assert.ThrowsAny<Exception>(() => processor.Update(null!));
    }

    [Fact]
    public void AnOccluderInFrontOfTheShaftsCastsAShadowIntoThem()
    {
        using var devices = new GraphicsDevices();
        using var light = new Item(devices, Light(1), Glass);
        using var occluder = new Item(devices, Blocker(), Bars);
        var clear = light.Render(0, EffectDescriptions.Flat);

        occluder.Update(0, InFront);
        var shadowed = light.Render(0, EffectDescriptions.Flat);

        var before = OutsideLight(clear);
        var after = OutsideLight(shadowed);
        var darker = shadowed.Coordinates().Count(point => shadowed[point.X, point.Y].Red + shadowed[point.X, point.Y].Green + shadowed[point.X, point.Y].Blue < (clear[point.X, point.Y].Red + clear[point.X, point.Y].Green + clear[point.X, point.Y].Blue) * 0.8);
        Assert.True(before > 0);
        Assert.True(after < before * 0.9, $"{after} {before}");
        Assert.True(darker > Width * Height / 4, $"{darker}");
    }

    [Fact]
    public void AnOccluderPublishedForAnotherFrameCastsNoShadowUntilThatFrame()
    {
        using var devices = new GraphicsDevices();
        using var light = new Item(devices, Light(1), Glass);
        using var occluder = new Item(devices, Blocker(), Bars);
        var clearNow = light.Render(0, EffectDescriptions.Flat);
        var clearLater = light.Render(15, EffectDescriptions.Flat);

        occluder.Update(15, InFront);

        Assert.True(clearNow.SamePixelsAs(light.Render(0, EffectDescriptions.Flat)));
        Assert.True(OutsideLight(light.Render(15, EffectDescriptions.Flat)) < OutsideLight(clearLater) * 0.9);
    }

    [Fact]
    public void AnOccluderInAnotherSceneNeverReachesTheLight()
    {
        var home = Guid.NewGuid();
        var away = Guid.NewGuid();
        using var devices = new GraphicsDevices();
        using var light = new Item(devices, Light(1), Glass, home);
        using var foreign = new Item(devices, Blocker(), Bars, away);
        var clear = light.Render(0, EffectDescriptions.Flat);

        foreign.Update(0, InFront);
        Assert.True(clear.SamePixelsAs(light.Render(0, EffectDescriptions.Flat)));

        using var native = new Item(devices, Blocker(), Bars, home);
        native.Update(0, InFront);
        Assert.True(OutsideLight(light.Render(0, EffectDescriptions.Flat)) < OutsideLight(clear) * 0.9);
    }

    [Fact]
    public void ALightThatMovesToAnotherSceneFollowsTheOccludersThereAndLeavesTheOldOnes()
    {
        var home = Guid.NewGuid();
        var away = Guid.NewGuid();
        using var devices = new GraphicsDevices();
        using var light = new Item(devices, Light(1), Glass, home);
        using var occluder = new Item(devices, Blocker(), Bars, away);
        var clear = light.Render(0, EffectDescriptions.Flat);
        occluder.Update(0, InFront);

        Assert.True(OutsideLight(light.Render(0, EffectDescriptions.Flat, away)) < OutsideLight(clear) * 0.9);
        Assert.True(clear.SamePixelsAs(light.Render(0, EffectDescriptions.Flat, home)));
    }

    [Fact]
    public void AnOccluderThatMovesToAnotherSceneLeavesTheLightsOfTheOldOne()
    {
        var home = Guid.NewGuid();
        var away = Guid.NewGuid();
        using var devices = new GraphicsDevices();
        using var light = new Item(devices, Light(1), Glass, home);
        using var occluder = new Item(devices, Blocker(), Bars, home);
        var clear = light.Render(0, EffectDescriptions.Flat);
        occluder.Update(0, InFront);
        Assert.True(OutsideLight(light.Render(0, EffectDescriptions.Flat)) < OutsideLight(clear) * 0.9);

        occluder.Update(0, InFront, away);

        Assert.True(clear.SamePixelsAs(light.Render(0, EffectDescriptions.Flat)));
    }

    [Fact]
    public void EachSceneKeepsItsOwnFourOccluderSlotsOnTheSameNumber()
    {
        var home = Guid.NewGuid();
        var away = Guid.NewGuid();
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var items = Enumerable.Range(0, VolumetricLightSettings.OccluderCapacity + 1)
            .Select(index => new Item(devices, Blocker(), Bars, index < VolumetricLightSettings.OccluderCapacity ? home : away))
            .ToArray();
        try
        {
            foreach (var item in items)
                item.Update(0, InFront);

            Assert.All(Snapshot(context, 1, 0, home), occluder => Assert.True(occluder.Strength > 0f));
            Assert.True(Snapshot(context, 1, 0, away)[0].Strength > 0f);
        }
        finally
        {
            foreach (var item in items)
                item.Dispose();
        }
    }

    [Fact]
    public void ALightIgnoresOccludersOfAnotherNumberAndWhenItsOwnNumberIsZero()
    {
        using var devices = new GraphicsDevices();
        using var other = new Item(devices, Light(2), Glass);
        using var off = new Item(devices, Light(0), Glass);
        var clearOther = other.Render(0, EffectDescriptions.Flat);
        var clearOff = off.Render(0, EffectDescriptions.Flat);
        using var occluder = new Item(devices, Blocker(1), Bars);

        occluder.Update(0, InFront);

        Assert.True(clearOther.SamePixelsAs(other.Render(0, EffectDescriptions.Flat)));
        Assert.True(clearOff.SamePixelsAs(off.Render(0, EffectDescriptions.Flat)));
    }

    [Fact]
    public void AnOccluderAfterTheLightOnTheSameItemIsIgnoredInsteadOfBreakingTheGraph()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        using var alone = new Item(devices, Light(1), Glass);
        var expected = alone.Render(0, EffectDescriptions.Flat);
        using var light = Light(1).CreateVideoEffect(context);
        light.SetInput(source.Bitmap);
        using var blocker = Blocker().CreateVideoEffect(context);
        blocker.SetInput(light.Output);

        var draw = light.Update(EffectDescriptions.At(0, Length, EffectDescriptions.Flat, Canvas));
        blocker.Update(EffectDescriptions.At(0, Length, draw, Canvas));
        light.Update(EffectDescriptions.At(1, Length, EffectDescriptions.Flat, Canvas));
        var chained = Rendering.Capture(context, blocker.Output);

        Assert.True(expected.SamePixelsAs(chained));
    }

    [Fact]
    public void AnOccluderBehindOtherEffectsOfALightOnTheSameItemIsIgnoredToo()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Glass);
        var lightEffect = Light(0);
        using var light = lightEffect.CreateVideoEffect(context);
        light.SetInput(source.Bitmap);
        using var blur = new Vortice.Direct2D1.Effects.GaussianBlur(context.DeviceContext) { StandardDeviation = 2f };
        using var lightOutput = light.Output;
        blur.SetInput(0, lightOutput, true);
        using var blurred = blur.Output;
        using var blocker = Blocker().CreateVideoEffect(context);
        blocker.SetInput(blurred);
        var draw = light.Update(EffectDescriptions.At(0, Length, EffectDescriptions.Flat, Canvas));
        blocker.Update(EffectDescriptions.At(0, Length, draw, Canvas));
        var before = Rendering.Capture(context, blurred);

        lightEffect.OcclusionChannel = 1;
        light.Update(EffectDescriptions.At(0, Length, EffectDescriptions.Flat, Canvas));
        blocker.Update(EffectDescriptions.At(0, Length, draw, Canvas));
        var after = Rendering.Capture(context, blurred);

        Assert.True(before.SamePixelsAs(after));
    }

    [Fact]
    public void AnOccluderOnTheSameNumberReachesEveryLightThatUsesIt()
    {
        using var devices = new GraphicsDevices();
        using var first = new Item(devices, Light(1), Glass);
        using var second = new Item(devices, Light(1), Glass);
        using var occluder = new Item(devices, Blocker(), Bars);
        var clearFirst = first.Render(0, EffectDescriptions.Flat);
        var clearSecond = second.Render(0, EffectDescriptions.Flat);

        occluder.Update(0, InFront);
        var shadowedFirst = first.Render(0, EffectDescriptions.Flat);
        var shadowedSecond = second.Render(0, EffectDescriptions.Flat);

        Assert.True(OutsideLight(shadowedFirst) < OutsideLight(clearFirst) * 0.9);
        Assert.True(OutsideLight(shadowedSecond) < OutsideLight(clearSecond) * 0.9);
        Assert.True(shadowedFirst.SamePixelsAs(shadowedSecond));
    }

    [Fact]
    public void WithdrawingTheOccluderRestoresTheLightExactly()
    {
        using var devices = new GraphicsDevices();
        using var light = new Item(devices, Light(1), Glass);
        var clear = light.Render(0, EffectDescriptions.Flat);
        var occluder = new Item(devices, Blocker(), Bars);
        occluder.Update(0, InFront);
        var shadowed = light.Render(0, EffectDescriptions.Flat);

        occluder.Dispose();
        var restored = light.Render(0, EffectDescriptions.Flat);

        Assert.False(clear.SamePixelsAs(shadowed));
        Assert.True(clear.SamePixelsAs(restored));
    }

    [Fact]
    public void MovingTheOccluderMovesTheShadowTheSameWayAsANewlyBuiltScene()
    {
        using var devices = new GraphicsDevices();
        var moved = InFront with { Draw = new Vector3(24f, 70f, 120f), Rotation = new Vector3(0f, 20f, 10f) };
        using var light = new Item(devices, Light(1), Glass);
        using var occluder = new Item(devices, Blocker(), Bars);
        occluder.Update(0, InFront);
        var first = light.Render(0, EffectDescriptions.Flat);

        occluder.Update(1, moved);
        var second = light.Render(1, EffectDescriptions.Flat);

        using var freshLight = new Item(devices, Light(2), Glass);
        using var freshOccluder = new Item(devices, Blocker(2), Bars);
        freshOccluder.Update(1, moved);
        var fresh = freshLight.Render(1, EffectDescriptions.Flat);
        Assert.False(first.SamePixelsAs(second));
        Assert.True(second.SamePixelsAs(fresh));
    }

    [Fact]
    public void MovingTheLightMovesTheShadowTheSameWayAsANewlyBuiltScene()
    {
        using var devices = new GraphicsDevices();
        var moved = EffectDescriptions.Flat with { Draw = new Vector3(20f, -10f, 0f), Rotation = new Vector3(0f, 15f, 0f) };
        using var light = new Item(devices, Light(1), Glass);
        using var occluder = new Item(devices, Blocker(), Bars);
        occluder.Update(0, InFront);
        var first = light.Render(0, EffectDescriptions.Flat);

        var second = light.Render(0, moved);

        using var freshLight = new Item(devices, Light(2), Glass);
        using var freshOccluder = new Item(devices, Blocker(2), Bars);
        freshOccluder.Update(0, InFront);
        var fresh = freshLight.Render(0, moved);
        Assert.False(first.SamePixelsAs(second));
        Assert.True(second.SamePixelsAs(fresh));
    }

    [Fact]
    public void TheLightFollowsAChangeOfItsOwnNumber()
    {
        using var devices = new GraphicsDevices();
        var effect = Light(0);
        using var light = new Item(devices, effect, Glass);
        using var occluder = new Item(devices, Blocker(), Bars);
        occluder.Update(0, InFront);
        var off = light.Render(0, EffectDescriptions.Flat);

        effect.OcclusionChannel = 1;
        var on = light.Render(0, EffectDescriptions.Flat);
        effect.OcclusionChannel = 0;
        var offAgain = light.Render(0, EffectDescriptions.Flat);

        Assert.True(OutsideLight(on) < OutsideLight(off) * 0.9);
        Assert.True(off.SamePixelsAs(offAgain));
    }

    [Fact]
    public void UpdatingTheItemsAtTheSameTimeGivesTheSameLightAsOneAfterAnother()
    {
        using var devices = new GraphicsDevices();
        using var light = new Item(devices, Light(1), Glass);
        using var occluder = new Item(devices, Blocker(), Bars);
        occluder.Update(0, InFront);
        var sequential = light.Render(0, EffectDescriptions.Flat);

        for (var repeat = 0; repeat < 20; repeat++)
        {
            Parallel.Invoke(() => occluder.Update(repeat % 3, InFront), () => light.Update(repeat % 3, EffectDescriptions.Flat));
            var parallel = light.Capture();
            Assert.True(sequential.SamePixelsAs(parallel), $"{repeat}");
        }
    }
}
