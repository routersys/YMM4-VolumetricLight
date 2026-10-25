using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace VolumetricLight;

internal sealed class VolumetricLightOccluderEffectProcessor(IGraphicsDevicesAndContext devices, VolumetricLightOccluderEffect item) : VideoEffectProcessorBase(devices)
{
    private readonly IGraphicsDevicesAndContext _devices = devices;
    private readonly VolumetricLightOccluderEffect _item = item;
    private OccluderLease? _lease;
    private OccluderSlot? _slot;
    private int _channel;

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        try
        {
            UpdateCore(effectDescription);
            return effectDescription.DrawDescription;
        }
        catch (Exception exception)
        {
            VolumetricLightTelemetry.Report(exception);
            throw;
        }
    }

    private void UpdateCore(EffectDescription effectDescription)
    {
        if (input is null)
            return;

        var draw = effectDescription.DrawDescription;
        var channel = Math.Clamp(_item.Channel, 1, VolumetricLightSettings.MaximumOcclusionChannel);
        if (_lease is null || _channel != channel)
        {
            Release();
            _lease = OccluderRegistry.Acquire(_devices, channel);
            _channel = channel;
        }

        _slot ??= _lease.Group.Claim();
        if (_slot is null)
            return;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var strength = Occluder.GetStrength(_item.Strength.GetValue(frame, length, effectDescription.FPS), draw.Opacity);
        var occluder = draw.Zoom.X != 0f && draw.Zoom.Y != 0f && ViewTransform.TryCreate(draw, out var view)
            ? new Occluder(view, GoboMapping.Create(draw.Zoom, draw.Invert, draw.CenterPoint), strength)
            : default;
        _slot.Publish(input, occluder);
    }

    private void Release()
    {
        _slot?.Dispose();
        _slot = null;
        _lease?.Dispose();
        _lease = null;
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices) => null;

    protected override void setInput(ID2D1Image? input)
    {
    }

    protected override void ClearEffectChain()
    {
        try
        {
            Release();
        }
        catch (Exception exception)
        {
            VolumetricLightTelemetry.Report(exception);
            throw;
        }
    }
}
