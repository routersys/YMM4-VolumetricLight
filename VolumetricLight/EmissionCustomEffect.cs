using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace VolumetricLight;

internal sealed class EmissionCustomEffect : D2D1CustomShaderEffectBase
{
    public EmissionCustomEffect(IGraphicsDevicesAndContext devices) : base(Create<EffectImpl>(devices))
    {
        if (IsEnabled)
            SetValue((int)Property.Precision, BufferPrecision.PerChannel16Float);
    }

    internal enum PropertyIndex
    {
        Threshold = 0,
    }

    public float Threshold { set => SetValue((int)PropertyIndex.Threshold, value); }

    [CustomEffect(1)]
    internal sealed class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
    {
        private ConstantBuffer _cb;

        [CustomEffectProperty(PropertyType.Float, (int)PropertyIndex.Threshold)]
        public float Threshold { get => _cb.Threshold; set { _cb.Threshold = float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f); UpdateConstants(); } }

        public EffectImpl() : base(ShaderResourceUri.Get("Emission"))
        {
        }

        protected override void UpdateConstants()
        {
            if (drawInformation is null)
                return;

            try
            {
                drawInformation.SetPixelShaderConstantBuffer(_cb);
            }
            catch (Exception exception)
            {
                VolumetricLightTelemetry.Report(exception);
                throw;
            }
        }

        public override void MapInputRectsToOutputRect(
            RawRect[] inputRects,
            RawRect[] inputOpaqueSubRects,
            out RawRect outputRect,
            out RawRect outputOpaqueSubRect)
        {
            inputRect = ClampInputRect(inputRects[0]);
            outputRect = inputRect;
            outputOpaqueSubRect = default;
        }

        public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
            => inputRects[0] = outputRect;

        [StructLayout(LayoutKind.Sequential)]
        private struct ConstantBuffer
        {
            public float Threshold;
            public float Padding0;
            public float Padding1;
            public float Padding2;
        }
    }
}
