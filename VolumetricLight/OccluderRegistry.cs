using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using SharpGen.Runtime;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Plugin;

namespace VolumetricLight;

internal sealed class OccluderGroup : IDisposable
{
    private readonly object _gate = new();
    private readonly DisposeCollector _disposer = new();
    private readonly AffineTransform2D[] _relays = new AffineTransform2D[VolumetricLightSettings.OccluderCapacity];
    private readonly ID2D1Image[] _outputs = new ID2D1Image[VolumetricLightSettings.OccluderCapacity];
    private readonly ID2D1Image?[] _published = new ID2D1Image?[VolumetricLightSettings.OccluderCapacity];
    private readonly Occluder[] _requested = new Occluder[VolumetricLightSettings.OccluderCapacity];
    private readonly bool[] _blocked = new bool[VolumetricLightSettings.OccluderCapacity];
    private readonly int[] _checked = new int[VolumetricLightSettings.OccluderCapacity];
    private readonly HashSet<nint> _consumers = [];
    private int _version;
    private readonly Occluder[] _occluders = new Occluder[VolumetricLightSettings.OccluderCapacity];
    private readonly bool[] _claimed = new bool[VolumetricLightSettings.OccluderCapacity];
    private readonly ID2D1Image _empty;
    private bool _disposed;

    public OccluderGroup(ID2D1DeviceContext context)
    {
        var flood = new Flood(context) { Color = Vector4.Zero };
        var crop = new Crop(context) { Rectangle = Vector4.Zero };
        _disposer.Collect(flood);
        _disposer.Collect(crop);
        using (var floodOutput = flood.Output)
            crop.SetInput(0, floodOutput, true);
        _empty = crop.Output;
        _disposer.Collect(_empty);
        for (var index = 0; index < _relays.Length; index++)
        {
            var relay = new AffineTransform2D(context) { InterPolationMode = AffineTransform2DInterpolationMode.NearestNeighbor };
            _disposer.Collect(relay);
            relay.SetInput(0, _empty, true);
            _relays[index] = relay;
            _outputs[index] = relay.Output;
            _disposer.Collect(_outputs[index]);
        }
    }

    public ID2D1Image GetOutput(int slot) => _outputs[slot];

    public bool IsOccupied
    {
        get
        {
            lock (_gate)
                return Array.IndexOf(_claimed, true) >= 0;
        }
    }

    public OccluderSlot? Claim()
    {
        lock (_gate)
        {
            if (_disposed)
                return null;

            for (var index = 0; index < _claimed.Length; index++)
            {
                if (_claimed[index])
                    continue;

                _claimed[index] = true;
                return new OccluderSlot(this, index);
            }

            return null;
        }
    }

    public void Snapshot(Span<Occluder> destination)
    {
        lock (_gate)
        {
            for (var index = 0; index < destination.Length; index++)
                destination[index] = !_disposed && index < _occluders.Length ? _occluders[index] : default;
        }
    }

    public void AddConsumer(nint effect)
    {
        lock (_gate)
        {
            if (_disposed || !_consumers.Add(effect))
                return;

            _version++;
            for (var index = 0; index < _claimed.Length; index++)
            {
                if (_claimed[index] && _published[index] is { } image)
                    Apply(index, image);
            }
        }
    }

    public void RemoveConsumer(nint effect)
    {
        lock (_gate)
        {
            if (_consumers.Remove(effect))
                _version++;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _disposer.Dispose();
        }
    }

    internal void Publish(int index, ID2D1Image image, in Occluder occluder)
    {
        lock (_gate)
        {
            if (_disposed || !_claimed[index])
                return;

            _requested[index] = occluder;
            if (!ReferenceEquals(_published[index], image) || _checked[index] != _version)
            {
                _published[index] = image;
                Apply(index, image);
            }
            else
            {
                _occluders[index] = _blocked[index] ? default : occluder;
            }
        }
    }

    internal void Retire(int index)
    {
        lock (_gate)
        {
            if (!_claimed[index])
                return;

            _claimed[index] = false;
            _occluders[index] = default;
            _requested[index] = default;
            _published[index] = null;
            _blocked[index] = false;
            if (!_disposed)
                _relays[index].SetInput(0, _empty, true);
        }
    }

    private void Apply(int index, ID2D1Image image)
    {
        _checked[index] = _version;
        _blocked[index] = DependsOnConsumer(image);
        _relays[index].SetInput(0, _blocked[index] ? _empty : image, true);
        _occluders[index] = _blocked[index] ? default : _requested[index];
    }

    private bool DependsOnConsumer(ID2D1Image image)
    {
        if (_consumers.Count == 0)
            return false;

        var visited = new HashSet<nint>();
        var pending = new Stack<ID2D1Image>();
        var owned = new List<IDisposable>();
        pending.Push(image);
        try
        {
            while (pending.TryPop(out var current))
            {
                var effect = current.QueryInterfaceOrNull<ID2D1Effect>();
                if (effect is null)
                    continue;

                owned.Add(effect);
                if (_consumers.Contains(effect.NativePointer))
                    return true;
                if (!visited.Add(effect.NativePointer))
                    continue;

                for (var input = 0; input < effect.InputCount; input++)
                {
                    var source = effect.GetInput(input);
                    if (source is null)
                        continue;

                    owned.Add(source);
                    pending.Push(source);
                }
            }

            return false;
        }
        finally
        {
            foreach (var disposable in owned)
                disposable.Dispose();
        }
    }
}

internal sealed class OccluderSlot(OccluderGroup group, int index) : IDisposable
{
    private int _retired;

    public void Publish(ID2D1Image image, in Occluder occluder) => group.Publish(index, image, occluder);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _retired, 1) == 0)
            group.Retire(index);
    }
}

internal sealed class OccluderLease : IDisposable
{
    private readonly OccluderBoard _board;
    private readonly int _channel;
    private int _released;

    internal OccluderLease(OccluderBoard board, int channel, OccluderGroup group)
    {
        _board = board;
        _channel = channel;
        Group = group;
    }

    public OccluderGroup Group { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _board.Release(_channel);
    }
}

internal sealed class OccluderBoard
{
    private sealed class Entry(OccluderGroup group)
    {
        public OccluderGroup Group { get; } = group;

        public int References { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<int, Entry> _entries = [];

    public OccluderLease Acquire(IGraphicsDevicesAndContext devices, int channel)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(channel, out var entry))
            {
                entry = new Entry(new OccluderGroup(devices.DeviceContext));
                _entries.Add(channel, entry);
            }

            entry.References++;
            return new OccluderLease(this, channel, entry.Group);
        }
    }

    internal void Release(int channel)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(channel, out var entry) || --entry.References > 0)
                return;

            _entries.Remove(channel);
            entry.Group.Dispose();
        }
    }
}

internal static class OccluderRegistry
{
    private static readonly ConditionalWeakTable<D2DDevices, OccluderBoard> Boards = [];

    public static OccluderLease Acquire(IGraphicsDevicesAndContext devices, int channel)
        => Boards.GetValue(devices.D2D, _ => new OccluderBoard()).Acquire(devices, channel);
}

internal sealed class OccluderLink
{
    private OccluderGroup? _group;

    public OccluderGroup? Group
    {
        get => Volatile.Read(ref _group);
        set => Volatile.Write(ref _group, value);
    }
}

internal static class OccluderLinks
{
    private static readonly ConcurrentDictionary<int, OccluderLink> Links = [];
    private static int _next;

    public static int Register(OccluderLink link)
    {
        var id = Interlocked.Increment(ref _next);
        Links[id] = link;
        return id;
    }

    public static void Unregister(int id) => Links.TryRemove(id, out _);

    public static bool TryGet(int id, out OccluderLink link) => Links.TryGetValue(id, out link!);
}
