using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight;

internal sealed class ShaftCustomEffect : D2D1CustomShaderEffectBase
{
    public ShaftCustomEffect(IGraphicsDevicesAndContext devices) : base(Create<EffectImpl>(devices))
    {
        if (IsEnabled)
            SetValue((int)Property.Precision, BufferPrecision.PerChannel16Float);
    }

    internal enum PropertyIndex
    {
        LocalFromCamera = 0,
        Plane0,
        Plane1,
        Plane2,
        Plane3,
        Plane4,
        Plane5,
        Focus,
        Axis,
        View,
        Gobo,
        Beam,
        Tint,
        March,
        Dust,
        Wind,
        Mode,
        Fade,
        OutputBounds,
        CameraFromLocal,
        Link,
        Revision,
    }

    public Matrix4x4 LocalFromCamera { set => SetValue((int)PropertyIndex.LocalFromCamera, value); }
    public Vector4 Plane0 { set => SetValue((int)PropertyIndex.Plane0, value); }
    public Vector4 Plane1 { set => SetValue((int)PropertyIndex.Plane1, value); }
    public Vector4 Plane2 { set => SetValue((int)PropertyIndex.Plane2, value); }
    public Vector4 Plane3 { set => SetValue((int)PropertyIndex.Plane3, value); }
    public Vector4 Plane4 { set => SetValue((int)PropertyIndex.Plane4, value); }
    public Vector4 Plane5 { set => SetValue((int)PropertyIndex.Plane5, value); }
    public Vector4 Focus { set => SetValue((int)PropertyIndex.Focus, value); }
    public Vector4 Axis { set => SetValue((int)PropertyIndex.Axis, value); }
    public Vector4 View { set => SetValue((int)PropertyIndex.View, value); }
    public Vector4 Gobo { set => SetValue((int)PropertyIndex.Gobo, value); }
    public Vector4 Beam { set => SetValue((int)PropertyIndex.Beam, value); }
    public Vector4 Tint { set => SetValue((int)PropertyIndex.Tint, value); }
    public Vector4 March { set => SetValue((int)PropertyIndex.March, value); }
    public Vector4 Dust { set => SetValue((int)PropertyIndex.Dust, value); }
    public Vector4 Wind { set => SetValue((int)PropertyIndex.Wind, value); }
    public Vector4 Mode { set => SetValue((int)PropertyIndex.Mode, value); }
    public Vector4 Fade { set => SetValue((int)PropertyIndex.Fade, value); }
    public Vector4 OutputBounds { set => SetValue((int)PropertyIndex.OutputBounds, value); }
    public Matrix4x4 CameraFromLocal { set => SetValue((int)PropertyIndex.CameraFromLocal, value); }
    public int Link { set => SetValue((int)PropertyIndex.Link, value); }
    public int Revision { set => SetValue((int)PropertyIndex.Revision, value); }

    public void Apply(in ShaftConstants next, in ShaftConstants last, bool force)
    {
        if (force || last.LocalFromCamera != next.LocalFromCamera)
            LocalFromCamera = next.LocalFromCamera;
        if (force || last.Plane0 != next.Plane0)
            Plane0 = next.Plane0;
        if (force || last.Plane1 != next.Plane1)
            Plane1 = next.Plane1;
        if (force || last.Plane2 != next.Plane2)
            Plane2 = next.Plane2;
        if (force || last.Plane3 != next.Plane3)
            Plane3 = next.Plane3;
        if (force || last.Plane4 != next.Plane4)
            Plane4 = next.Plane4;
        if (force || last.Plane5 != next.Plane5)
            Plane5 = next.Plane5;
        if (force || last.Focus != next.Focus)
            Focus = next.Focus;
        if (force || last.Axis != next.Axis)
            Axis = next.Axis;
        if (force || last.View != next.View)
            View = next.View;
        if (force || last.Gobo != next.Gobo)
            Gobo = next.Gobo;
        if (force || last.Beam != next.Beam)
            Beam = next.Beam;
        if (force || last.Tint != next.Tint)
            Tint = next.Tint;
        if (force || last.March != next.March)
            March = next.March;
        if (force || last.Dust != next.Dust)
            Dust = next.Dust;
        if (force || last.Wind != next.Wind)
            Wind = next.Wind;
        if (force || last.Mode != next.Mode)
            Mode = next.Mode;
        if (force || last.Fade != next.Fade)
            Fade = next.Fade;
        if (force || last.OutputBounds != next.OutputBounds)
            OutputBounds = next.OutputBounds;
        if (force || last.CameraFromLocal != next.CameraFromLocal)
            CameraFromLocal = next.CameraFromLocal;
    }

    [CustomEffect(VolumetricLightSettings.GoboLevelCount + VolumetricLightSettings.OccluderCapacity)]
    internal sealed class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
    {
        private ConstantBuffer _cb;
        private Vector4 _outputBounds;
        private RawRect _outputRect;
        private readonly RawRect[] _inputRects = new RawRect[VolumetricLightSettings.GoboLevelCount + VolumetricLightSettings.OccluderCapacity];
        private Matrix4x4 _cameraFromLocal = Matrix4x4.Identity;
        private int _link;
        private int _revision;

        [CustomEffectProperty(PropertyType.Matrix4x4, (int)PropertyIndex.LocalFromCamera)]
        public Matrix4x4 LocalFromCamera { get => _cb.LocalFromCamera; set { _cb.LocalFromCamera = ViewTransform.IsFinite(value) ? value : default; UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Plane0)]
        public Vector4 Plane0 { get => _cb.Planes[0]; set { _cb.Planes[0] = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Plane1)]
        public Vector4 Plane1 { get => _cb.Planes[1]; set { _cb.Planes[1] = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Plane2)]
        public Vector4 Plane2 { get => _cb.Planes[2]; set { _cb.Planes[2] = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Plane3)]
        public Vector4 Plane3 { get => _cb.Planes[3]; set { _cb.Planes[3] = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Plane4)]
        public Vector4 Plane4 { get => _cb.Planes[4]; set { _cb.Planes[4] = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Plane5)]
        public Vector4 Plane5 { get => _cb.Planes[5]; set { _cb.Planes[5] = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Focus)]
        public Vector4 Focus { get => _cb.Focus; set { _cb.Focus = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Axis)]
        public Vector4 Axis { get => _cb.Axis; set { _cb.Axis = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.View)]
        public Vector4 View { get => _cb.View; set { _cb.View = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Gobo)]
        public Vector4 Gobo { get => _cb.Gobo; set { _cb.Gobo = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Beam)]
        public Vector4 Beam { get => _cb.Beam; set { _cb.Beam = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Tint)]
        public Vector4 Tint { get => _cb.Tint; set { _cb.Tint = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.March)]
        public Vector4 March { get => _cb.March; set { _cb.March = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Dust)]
        public Vector4 Dust { get => _cb.Dust; set { _cb.Dust = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Wind)]
        public Vector4 Wind { get => _cb.Wind; set { _cb.Wind = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Mode)]
        public Vector4 Mode { get => _cb.Mode; set { _cb.Mode = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.Fade)]
        public Vector4 Fade { get => _cb.Fade; set { _cb.Fade = Finite(value); UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Vector4, (int)PropertyIndex.OutputBounds)]
        public Vector4 OutputBounds
        {
            get => _outputBounds;
            set
            {
                _outputBounds = Finite(value);
                _outputRect = new RawRect(
                    (int)Math.Floor(_outputBounds.X),
                    (int)Math.Floor(_outputBounds.Y),
                    (int)Math.Ceiling(_outputBounds.Z),
                    (int)Math.Ceiling(_outputBounds.W));
            }
        }

        [CustomEffectProperty(PropertyType.Matrix4x4, (int)PropertyIndex.CameraFromLocal)]
        public Matrix4x4 CameraFromLocal { get => _cameraFromLocal; set { _cameraFromLocal = ViewTransform.IsFinite(value) ? value : Matrix4x4.Identity; UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Int32, (int)PropertyIndex.Link)]
        public int Link { get => _link; set { _link = value; UpdateConstants(); } }

        [CustomEffectProperty(PropertyType.Int32, (int)PropertyIndex.Revision)]
        public int Revision { get => _revision; set { _revision = value; UpdateConstants(); } }

        public EffectImpl() : base(ShaderResourceUri.Get("Shaft"))
        {
        }

        private static Vector4 Finite(Vector4 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W) ? value : default;

        protected override void UpdateConstants()
        {
            if (drawInformation is null)
                return;

            try
            {
                ResolveOccluders();
                drawInformation.SetPixelShaderConstantBuffer(_cb);
            }
            catch (Exception exception)
            {
                VolumetricLightTelemetry.Report(exception);
                throw;
            }
        }

        private void ResolveOccluders()
        {
            _cb.OccluderStrength = default;
            if (!OccluderLinks.TryGet(_link, out var link) || link.Group is not { } group)
                return;

            Span<Occluder> occluders = stackalloc Occluder[VolumetricLightSettings.OccluderCapacity];
            Span<float> strengths = stackalloc float[VolumetricLightSettings.OccluderCapacity];
            group.Snapshot(occluders, link.Frame);
            for (var index = 0; index < occluders.Length; index++)
            {
                var plane = OccluderPlane.Create(_cameraFromLocal, occluders[index]);
                _cb.OccluderPlanes[index] = plane.Plane;
                _cb.OccluderImageX[index] = plane.ImageX;
                _cb.OccluderImageY[index] = plane.ImageY;
                strengths[index] = plane.Strength;
            }

            _cb.OccluderStrength = new Vector4(strengths[0], strengths[1], strengths[2], strengths[3]);
        }

        public override void MapInputRectsToOutputRect(
            RawRect[] inputRects,
            RawRect[] inputOpaqueSubRects,
            out RawRect outputRect,
            out RawRect outputOpaqueSubRect)
        {
            for (var index = 0; index < _inputRects.Length; index++)
                _inputRects[index] = inputRects[index];
            outputRect = _outputRect;
            outputOpaqueSubRect = default;
        }

        public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
        {
            for (var index = 0; index < _inputRects.Length; index++)
            {
                var rect = _inputRects[index];
                inputRects[index] = rect.Right > rect.Left && rect.Bottom > rect.Top
                    ? new RawRect(rect.Left - 1, rect.Top - 1, rect.Right + 1, rect.Bottom + 1)
                    : rect;
            }
        }

        public override RawRect MapInvalidRect(int inputIndex, RawRect invalidInputRect)
            => _outputRect;

        [StructLayout(LayoutKind.Sequential)]
        private struct ConstantBuffer
        {
            public Matrix4x4 LocalFromCamera;
            public PlaneSet Planes;
            public Vector4 Focus;
            public Vector4 Axis;
            public Vector4 View;
            public Vector4 Gobo;
            public Vector4 Beam;
            public Vector4 Tint;
            public Vector4 March;
            public Vector4 Dust;
            public Vector4 Wind;
            public Vector4 Mode;
            public Vector4 Fade;
            public OccluderPlaneSet OccluderPlanes;
            public OccluderPlaneSet OccluderImageX;
            public OccluderPlaneSet OccluderImageY;
            public Vector4 OccluderStrength;
        }
    }
}
