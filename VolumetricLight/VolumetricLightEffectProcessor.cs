using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Player.Video.Effects;

namespace VolumetricLight;

internal sealed class VolumetricLightEffectProcessor(IGraphicsDevicesAndContext devices, VolumetricLightEffect item) : VideoEffectProcessorBase(devices)
{
    private static readonly Vector4 UnboundedClip = new(float.MinValue, float.MinValue, float.MaxValue, float.MaxValue);
    private static readonly Matrix4x4 PerspectiveMatrix = ViewTransform.CreateSlope(VolumetricLightSettings.BasePerspectiveSlope);

    private enum SourceState
    {
        None,
        Input,
        Baked,
        Empty,
    }

    private enum ShaftState
    {
        None,
        Shaft,
        Empty,
    }

    private readonly IGraphicsDevicesAndContext _devices = devices;
    private readonly VolumetricLightEffect _item = item;
    private EmissionCustomEffect? _emission;
    private ShaftCustomEffect? _shaft;
    private AffineTransform2D? _upscale;
    private Composite? _composite;
    private Crop? _clip;
    private AffineTransform2D? _zoom;
    private Crop? _crop;
    private Transform3D? _render;
    private ID2D1Image? _renderOutput;
    private ID2D1Image? _emptyOutput;
    private ID2D1Image? _upscaleOutput;
    private readonly AffineTransform2D?[] _levels = new AffineTransform2D?[VolumetricLightSettings.MaximumGoboLevel];
    private SourceState _sourceState;
    private ShaftState _shaftState;
    private bool _isFirst = true;
    private float _threshold = float.NaN;
    private float _scale = float.NaN;
    private Vector4 _clipBounds;
    private ShaftConstants _constants;
    private ZoomState _zoomState;
    private bool _hasZoomState;

    public override DrawDescription Update(EffectDescription effectDescription)
    {
        try
        {
            return UpdateCore(effectDescription);
        }
        catch (Exception exception)
        {
            VolumetricLightTelemetry.Report(exception);
            throw;
        }
    }

    private DrawDescription UpdateCore(EffectDescription effectDescription)
    {
        var draw = effectDescription.DrawDescription;
        if (IsPassThroughEffect || _emission is null || _shaft is null || _zoom is null || _crop is null || _render is null || _renderOutput is null || input is null)
            return draw;

        var frame = effectDescription.ItemPosition.Frame;
        var length = effectDescription.ItemDuration.Frame;
        var fps = effectDescription.FPS;
        var threshold = VolumetricLightSettings.GetThreshold(_item.Threshold.GetValue(frame, length, fps));
        if (_isFirst || _threshold != threshold)
        {
            _emission.Threshold = threshold;
            _threshold = threshold;
        }

        var screenSize = effectDescription.ScreenSize;
        var bounds = _devices.DeviceContext.GetImageLocalBounds(input);
        if (!ShaftScene.TryCreate(ReadParameters(frame, length, fps), draw, bounds, screenSize.Width, screenSize.Height, frame, fps, out var scene))
            return Pass(draw);

        SetSource(_item.ShowSource ? SourceState.Baked : SourceState.Empty);
        SetClip(scene.Canvas.ToVector());
        if (scene.Shaft.IsEmpty || scene.Constants.Beam.W <= 0f)
        {
            SetShaft(ShaftState.Empty);
        }
        else
        {
            SetScale(scene.Scale);
            Apply(scene.Constants);
            SetShaft(ShaftState.Shaft);
        }

        Bake(draw);
        _isFirst = false;
        return draw with
        {
            Draw = Vector3.Zero,
            Zoom = Vector2.One,
            Rotation = Vector3.Zero,
            Camera = Matrix4x4.Identity,
            PerspectiveDistance = null,
            Billboard = BillboardMode.None,
            Invert = false,
        };
    }

    private DrawDescription Pass(DrawDescription draw)
    {
        SetSource(SourceState.Input);
        SetShaft(ShaftState.Empty);
        SetClip(UnboundedClip);
        _isFirst = false;
        return draw;
    }

    private VolumetricLightParameters ReadParameters(long frame, long length, int fps) => new(
        _item.Direction.GetValue(frame, length, fps),
        _item.Elevation.GetValue(frame, length, fps),
        _item.Spread.GetValue(frame, length, fps),
        _item.Length.GetValue(frame, length, fps),
        _item.Intensity.GetValue(frame, length, fps),
        _item.Tint,
        _item.Falloff.GetValue(frame, length, fps),
        _item.LampSize.GetValue(frame, length, fps),
        _item.Scattering.GetValue(frame, length, fps),
        _item.DustAmount.GetValue(frame, length, fps),
        _item.DustSize.GetValue(frame, length, fps),
        _item.DustSpeed.GetValue(frame, length, fps),
        _item.DustDirection.GetValue(frame, length, fps),
        _item.Seed,
        _item.Quality);

    private void SetSource(SourceState state)
    {
        if (_sourceState == state && state != SourceState.Input)
            return;

        var image = state switch
        {
            SourceState.Baked => _renderOutput,
            SourceState.Empty => _emptyOutput,
            _ => input,
        };
        _composite!.SetInput(0, image, true);
        _sourceState = state;
    }

    private void SetShaft(ShaftState state)
    {
        if (_shaftState == state)
            return;

        _composite!.SetInput(1, state == ShaftState.Shaft ? _upscaleOutput : _emptyOutput, true);
        _shaftState = state;
    }

    private void SetClip(Vector4 bounds)
    {
        if (_isFirst || _clipBounds != bounds)
        {
            _clip!.Rectangle = bounds;
            _clipBounds = bounds;
        }
    }

    private void SetScale(float scale)
    {
        if (_scale == scale)
            return;

        _upscale!.TransformMatrix = Matrix3x2.CreateScale(1f / scale);
        _scale = scale;
    }

    private void Bake(DrawDescription draw)
    {
        if (_sourceState != SourceState.Baked)
            return;

        var zoom = new ZoomState(draw.Zoom, draw.Invert, draw.CenterPoint, draw.ZoomInterpolationMode);
        if (!_hasZoomState || _zoomState != zoom)
        {
            _zoom!.TransformMatrix = (draw.Invert ? Matrix3x2.CreateScale(-1f, 1f, draw.CenterPoint) : Matrix3x2.Identity) * Matrix3x2.CreateScale(draw.Zoom);
            _zoom.InterPolationMode = ToTransformMode(draw.ZoomInterpolationMode);
            _zoomState = zoom;
            _hasZoomState = true;
        }

        _render!.TransformMatrix = ViewTransform.CreateWorld(draw.Rotation, draw.Draw) * ViewTransform.FinalizeCamera(draw) * PerspectiveMatrix;
        SafeTransform3DHelper.Apply(_devices.DeviceContext, _crop!, _renderOutput!);
    }

    private static AffineTransform2DInterpolationMode ToTransformMode(InterpolationMode mode)
        => Enum.IsDefined((AffineTransform2DInterpolationMode)(int)mode) ? (AffineTransform2DInterpolationMode)(int)mode : AffineTransform2DInterpolationMode.Linear;

    private void Apply(in ShaftConstants next)
    {
        _shaft!.Apply(next, _constants, _isFirst || _shaftState != ShaftState.Shaft);
        _constants = next;
    }

    protected override ID2D1Image? CreateEffect(IGraphicsDevicesAndContext devices)
    {
        var emission = new EmissionCustomEffect(devices);
        var shaft = new ShaftCustomEffect(devices);
        if (!emission.IsEnabled || !shaft.IsEnabled)
        {
            emission.Dispose();
            shaft.Dispose();
            return null;
        }

        _emission = emission;
        _shaft = shaft;
        disposer.Collect(emission);
        disposer.Collect(shaft);

        var context = devices.DeviceContext;
        var previous = emission.Output;
        disposer.Collect(previous);
        shaft.SetInput(0, previous, true);
        for (var level = 1; level < VolumetricLightSettings.GoboLevelCount; level++)
        {
            var scale = new AffineTransform2D(context)
            {
                TransformMatrix = Matrix3x2.CreateScale(VolumetricLightSettings.GoboLevelScale),
                InterPolationMode = AffineTransform2DInterpolationMode.Linear,
                BorderMode = BorderMode.Hard,
            };
            scale.SetValue((int)Property.Precision, BufferPrecision.PerChannel16Float);
            disposer.Collect(scale);
            scale.SetInput(0, previous, true);
            _levels[level - 1] = scale;
            previous = scale.Output;
            disposer.Collect(previous);
            shaft.SetInput(level, previous, true);
        }

        _upscale = new AffineTransform2D(context)
        {
            InterPolationMode = AffineTransform2DInterpolationMode.HighQualityCubic,
            BorderMode = BorderMode.Hard,
        };
        _upscale.SetValue((int)Property.Precision, BufferPrecision.PerChannel16Float);
        disposer.Collect(_upscale);
        using (var shaftOutput = shaft.Output)
            _upscale.SetInput(0, shaftOutput, true);
        _upscaleOutput = _upscale.Output;
        disposer.Collect(_upscaleOutput);

        _zoom = new AffineTransform2D(context);
        _crop = new Crop(context);
        _render = new Transform3D(context);
        disposer.Collect(_zoom);
        disposer.Collect(_crop);
        disposer.Collect(_render);
        using (var zoomOutput = _zoom.Output)
            _crop.SetInput(0, zoomOutput, true);
        using (var cropOutput = _crop.Output)
            _render.SetInput(0, cropOutput, true);
        _renderOutput = _render.Output;
        disposer.Collect(_renderOutput);

        var flood = new Flood(context) { Color = Vector4.Zero };
        var emptyCrop = new Crop(context) { Rectangle = Vector4.Zero };
        disposer.Collect(flood);
        disposer.Collect(emptyCrop);
        using (var floodOutput = flood.Output)
            emptyCrop.SetInput(0, floodOutput, true);
        _emptyOutput = emptyCrop.Output;
        disposer.Collect(_emptyOutput);

        _composite = new Composite(context) { InputCount = 2, Mode = CompositeMode.Plus };
        _composite.SetValue((int)Property.Precision, BufferPrecision.PerChannel16Float);
        disposer.Collect(_composite);
        _composite.SetInput(0, _emptyOutput, true);
        _composite.SetInput(1, _emptyOutput, true);
        _sourceState = SourceState.Empty;
        _shaftState = ShaftState.Empty;

        _clip = new Crop(context) { Rectangle = UnboundedClip };
        _clipBounds = UnboundedClip;
        disposer.Collect(_clip);
        using (var compositeOutput = _composite.Output)
            _clip.SetInput(0, compositeOutput, true);

        var output = _clip.Output;
        disposer.Collect(output);
        return output;
    }

    protected override void setInput(ID2D1Image? input)
    {
        _emission?.SetInput(0, input, true);
        _zoom?.SetInput(0, input, true);
        if (_sourceState == SourceState.Input)
            _composite?.SetInput(0, input, true);
    }

    protected override void ClearEffectChain()
    {
        try
        {
            _emission?.SetInput(0, null, true);
            _zoom?.SetInput(0, null, true);
            _crop?.SetInput(0, null, true);
            _render?.SetInput(0, null, true);
            _upscale?.SetInput(0, null, true);
            foreach (var level in _levels)
                level?.SetInput(0, null, true);
            if (_shaft is { } shaft)
            {
                for (var index = 0; index < VolumetricLightSettings.GoboLevelCount; index++)
                    shaft.SetInput(index, null, true);
            }

            _composite?.SetInput(0, null, true);
            _composite?.SetInput(1, null, true);
            _clip?.SetInput(0, null, true);
            _isFirst = true;
            _hasZoomState = false;
            _sourceState = SourceState.None;
            _shaftState = ShaftState.None;
        }
        catch (Exception exception)
        {
            VolumetricLightTelemetry.Report(exception);
            throw;
        }
    }

    private readonly record struct ZoomState(Vector2 Zoom, bool Invert, Vector2 CenterPoint, InterpolationMode Interpolation);
}
