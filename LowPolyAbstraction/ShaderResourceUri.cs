namespace LowPolyAbstraction;

internal static class ShaderResourceUri
{
    public static Uri Get(string shaderName) => new($"pack://application:,,,/LowPolyAbstraction;component/Shaders/{shaderName}.cso", UriKind.Absolute);
}
