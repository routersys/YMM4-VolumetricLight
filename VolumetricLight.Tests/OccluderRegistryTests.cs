using System.Numerics;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight.Tests;

[Collection("Direct2D")]
public sealed class OccluderRegistryTests
{
    const int Width = 8;
    const int Height = 4;

    static Bgra Marker(int x, int y) => Bgra.Opaque((byte)(x * 20), (byte)(y * 40), 200);

    static Occluder Sample(float strength = 1f)
    {
        Assert.True(ViewTransform.TryCreate(EffectDescriptions.Flat, out var view));
        return new Occluder(view, GoboMapping.Create(Vector2.One, false, Vector2.Zero), strength);
    }

    static Rendering Render(IGraphicsDevicesAndContext devices, ID2D1Image image) => Rendering.Capture(devices, image);

    [Fact]
    public void TheSameChannelOnTheSameDeviceSharesOneGroup()
    {
        using var devices = new GraphicsDevices();
        using var first = devices.CreateContext();
        using var second = devices.CreateContext();

        using var a = OccluderRegistry.Acquire(first, 1);
        using var b = OccluderRegistry.Acquire(second, 1);
        using var c = OccluderRegistry.Acquire(first, 2);

        Assert.Same(a.Group, b.Group);
        Assert.NotSame(a.Group, c.Group);
    }

    [Fact]
    public void DifferentDevicesNeverShareAGroup()
    {
        using var firstDevices = new GraphicsDevices();
        using var secondDevices = new GraphicsDevices();
        using var first = firstDevices.CreateContext();
        using var second = secondDevices.CreateContext();

        using var a = OccluderRegistry.Acquire(first, 1);
        using var b = OccluderRegistry.Acquire(second, 1);

        Assert.NotSame(a.Group, b.Group);
    }

    [Fact]
    public void TheGroupLivesUntilTheLastLeaseIsReleasedAndThenStartsOver()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        var a = OccluderRegistry.Acquire(context, 3);
        var b = OccluderRegistry.Acquire(context, 3);
        var group = a.Group;
        var slot = group.Claim()!;

        a.Dispose();
        a.Dispose();
        Assert.NotNull(group.Claim());
        b.Dispose();
        using var again = OccluderRegistry.Acquire(context, 3);

        Assert.NotSame(group, again.Group);
        Assert.Null(group.Claim());
        slot.Dispose();
    }

    [Fact]
    public void OnlyFourOccludersFitAndARetiredSlotCanBeClaimedAgain()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var lease = OccluderRegistry.Acquire(context, 1);
        var slots = Enumerable.Range(0, VolumetricLightSettings.OccluderCapacity).Select(_ => lease.Group.Claim()).ToArray();

        Assert.All(slots, Assert.NotNull);
        Assert.Null(lease.Group.Claim());
        slots[1]!.Dispose();
        slots[1]!.Dispose();
        var reclaimed = lease.Group.Claim();
        Assert.NotNull(reclaimed);
        Assert.Null(lease.Group.Claim());
    }

    [Fact]
    public void TheGroupIsOccupiedWhileAnySlotIsClaimed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var lease = OccluderRegistry.Acquire(context, 1);
        Assert.False(lease.Group.IsOccupied);

        var first = lease.Group.Claim()!;
        var second = lease.Group.Claim()!;
        Assert.True(lease.Group.IsOccupied);
        first.Dispose();
        Assert.True(lease.Group.IsOccupied);
        second.Dispose();
        Assert.False(lease.Group.IsOccupied);
    }

    [Fact]
    public void ThePublishedOccluderAppearsInTheSnapshotUntilItsSlotIsDisposed()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Marker);
        using var lease = OccluderRegistry.Acquire(context, 1);
        using var first = lease.Group.Claim()!;
        var second = lease.Group.Claim()!;
        var snapshot = new Occluder[VolumetricLightSettings.OccluderCapacity];

        first.Publish(source.Bitmap, Sample(0.25f));
        second.Publish(source.Bitmap, Sample(0.75f));
        lease.Group.Snapshot(snapshot);
        Assert.Equal([0.25f, 0.75f, 0f, 0f], snapshot.Select(occluder => occluder.Strength));

        second.Dispose();
        lease.Group.Snapshot(snapshot);
        Assert.Equal([0.25f, 0f, 0f, 0f], snapshot.Select(occluder => occluder.Strength));
    }

    [Fact]
    public void AnUnclaimedSlotIgnoresPublishing()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Marker);
        using var lease = OccluderRegistry.Acquire(context, 1);
        var slot = lease.Group.Claim()!;
        slot.Dispose();
        var snapshot = new Occluder[VolumetricLightSettings.OccluderCapacity];

        slot.Publish(source.Bitmap, Sample());
        lease.Group.Snapshot(snapshot);

        Assert.All(snapshot, occluder => Assert.Equal(0f, occluder.Strength));
    }

    [Fact]
    public void TheRelayShowsThePublishedImageAndGoesEmptyAgainAfterwards()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Marker);
        using var lease = OccluderRegistry.Acquire(context, 1);
        var slot = lease.Group.Claim()!;
        var relay = lease.Group.GetOutput(0);

        var empty = context.DeviceContext.GetImageLocalBounds(relay);
        slot.Publish(source.Bitmap, Sample());
        var shown = Render(context, relay);
        slot.Dispose();
        var gone = context.DeviceContext.GetImageLocalBounds(relay);

        Assert.Equal((0f, 0f, 0f, 0f), (empty.Left, empty.Top, empty.Right, empty.Bottom));
        Assert.Equal((0, 0, Width, Height), (shown.Left, shown.Top, shown.Width, shown.Height));
        Assert.All(shown.Coordinates(), point => Assert.Equal(source[point.X, point.Y], shown[point.X, point.Y]));
        Assert.Equal((0f, 0f, 0f, 0f), (gone.Left, gone.Top, gone.Right, gone.Bottom));
    }

    [Fact]
    public void AnOtherContextOfTheSameDeviceCanPublishAndAnotherCanRead()
    {
        using var devices = new GraphicsDevices();
        using var reader = devices.CreateContext();
        using var writer = devices.CreateContext();
        using var source = new SourceImage(writer, Width, Height, Marker);
        using var lease = OccluderRegistry.Acquire(reader, 1);
        using var slot = lease.Group.Claim()!;

        slot.Publish(source.Bitmap, Sample());
        var shown = Render(reader, lease.Group.GetOutput(0));

        Assert.Equal(source[3, 2], shown[3, 2]);
    }

    [Fact]
    public void ARelayStaysValidAfterTheContextThatCreatedItIsDisposed()
    {
        using var devices = new GraphicsDevices();
        using var reader = devices.CreateContext();
        var creator = devices.CreateContext();
        using var source = new SourceImage(reader, Width, Height, Marker);
        using var lease = OccluderRegistry.Acquire(creator, 1);
        using var slot = lease.Group.Claim()!;
        creator.Dispose();

        slot.Publish(source.Bitmap, Sample());
        var shown = Render(reader, lease.Group.GetOutput(0));

        Assert.Equal(source[5, 1], shown[5, 1]);
    }

    [Fact]
    public void ClaimingAndPublishingFromManyThreadsKeepsEverySlotConsistent()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Marker);
        using var lease = OccluderRegistry.Acquire(context, 1);
        var claimed = 0;

        Parallel.For(0, 64, index =>
        {
            var slot = lease.Group.Claim();
            if (slot is null)
                return;

            Interlocked.Increment(ref claimed);
            slot.Publish(source.Bitmap, Sample((index + 1) / 100f));
        });
        var snapshot = new Occluder[VolumetricLightSettings.OccluderCapacity];
        lease.Group.Snapshot(snapshot);

        Assert.Equal(VolumetricLightSettings.OccluderCapacity, claimed);
        Assert.All(snapshot, occluder => Assert.True(occluder.Strength > 0f));
    }

    [Fact]
    public void AnImageThatDependsOnAConsumerIsKeptOutUntilTheConsumerLeaves()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Marker);
        using var lease = OccluderRegistry.Acquire(context, 1);
        using var slot = lease.Group.Claim()!;
        using var blur = new Vortice.Direct2D1.Effects.GaussianBlur(context.DeviceContext);
        blur.SetInput(0, source.Bitmap, true);
        using var downstream = new Vortice.Direct2D1.Effects.Crop(context.DeviceContext) { Rectangle = new Vector4(0f, 0f, Width, Height) };
        using var blurOutput = blur.Output;
        downstream.SetInput(0, blurOutput, true);
        using var image = downstream.Output;
        var blocked = new Occluder[VolumetricLightSettings.OccluderCapacity];
        var freed = new Occluder[VolumetricLightSettings.OccluderCapacity];

        lease.Group.AddConsumer(blur.NativePointer);
        slot.Publish(image, Sample());
        lease.Group.Snapshot(blocked);
        var kept = context.DeviceContext.GetImageLocalBounds(lease.Group.GetOutput(0));
        lease.Group.RemoveConsumer(blur.NativePointer);
        slot.Publish(image, Sample());
        lease.Group.Snapshot(freed);
        var relayed = context.DeviceContext.GetImageLocalBounds(lease.Group.GetOutput(0));

        Assert.Equal(0f, blocked[0].Strength);
        Assert.Equal((0f, 0f, 0f, 0f), (kept.Left, kept.Top, kept.Right, kept.Bottom));
        Assert.Equal(1f, freed[0].Strength);
        Assert.True(relayed.Right > relayed.Left);
    }

    [Fact]
    public void AConsumerThatArrivesAfterThePublishingStillBlocksAnImageItDependsOn()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var source = new SourceImage(context, Width, Height, Marker);
        using var lease = OccluderRegistry.Acquire(context, 1);
        using var slot = lease.Group.Claim()!;
        using var blur = new Vortice.Direct2D1.Effects.GaussianBlur(context.DeviceContext);
        blur.SetInput(0, source.Bitmap, true);
        using var image = blur.Output;
        var snapshot = new Occluder[VolumetricLightSettings.OccluderCapacity];
        slot.Publish(image, Sample());
        lease.Group.Snapshot(snapshot);
        Assert.Equal(1f, snapshot[0].Strength);

        lease.Group.AddConsumer(blur.NativePointer);
        lease.Group.Snapshot(snapshot);

        Assert.Equal(0f, snapshot[0].Strength);
        Assert.Equal((0f, 0f, 0f, 0f), (context.DeviceContext.GetImageLocalBounds(lease.Group.GetOutput(0)) is var bounds ? (bounds.Left, bounds.Top, bounds.Right, bounds.Bottom) : default));
    }

    [Fact]
    public void ALinkHandsBackItsGroupByIdUntilItIsUnregistered()
    {
        using var devices = new GraphicsDevices();
        using var context = devices.CreateContext();
        using var lease = OccluderRegistry.Acquire(context, 1);
        var link = new OccluderLink { Group = lease.Group };

        var id = OccluderLinks.Register(link);

        Assert.True(OccluderLinks.TryGet(id, out var found));
        Assert.Same(link, found);
        Assert.Same(lease.Group, found.Group);
        OccluderLinks.Unregister(id);
        Assert.False(OccluderLinks.TryGet(id, out _));
        Assert.NotEqual(id, OccluderLinks.Register(new OccluderLink()));
    }
}
