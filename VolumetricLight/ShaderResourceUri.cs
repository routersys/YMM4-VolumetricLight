namespace VolumetricLight;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/VolumetricLight;component/Resources/Shader/{shaderName}.cso", UriKind.Absolute);
}
