namespace LowPolyAbstraction;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/LowPolyAbstraction;component/Resources/Shader/{shaderName}.cso", UriKind.Absolute);
}
