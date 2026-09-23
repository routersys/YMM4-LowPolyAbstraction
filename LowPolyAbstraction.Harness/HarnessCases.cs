namespace LowPolyAbstraction.Harness;

internal static class HarnessCases
{
    public static IEnumerable<(string Name, LowPolyAbstractionEffect Effect, IReadOnlyList<int> Frames)> All()
    {
        yield return ("default", Create(), [0]);
        yield return ("default-frames-0-8", Create(), Enumerable.Range(0, 9).ToArray());
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0), [0]);
        yield return ("amount-50", Create(effect => effect.Amount.Values[0].Value = 50), [0]);
        yield return ("quality-balanced", Create(effect => effect.Quality = LowPolyAbstractionQuality.Balanced), [0]);
        yield return ("quality-ultra", Create(effect => effect.Quality = LowPolyAbstractionQuality.Ultra), [0]);
        yield return ("detail-0", Create(effect => effect.Detail.Values[0].Value = 0), [0]);
        yield return ("detail-100", Create(effect => effect.Detail.Values[0].Value = 100), [0]);
        yield return ("fidelity-0", Create(effect => effect.Fidelity.Values[0].Value = 0), [0]);
        yield return ("fidelity-100", Create(effect => effect.Fidelity.Values[0].Value = 100), [0]);
        yield return ("refine-0", Create(effect => effect.Refine.Values[0].Value = 0), [0]);
        yield return ("refine-100", Create(effect => effect.Refine.Values[0].Value = 100), [0]);
        yield return ("seed-42", Create(effect => effect.Seed = 42), [0]);
        yield return ("gradient-0", Create(effect => effect.Gradient.Values[0].Value = 0), [0]);
        yield return ("gradient-100", Create(effect => effect.Gradient.Values[0].Value = 100), [0]);
        yield return ("wireframe-50", Create(effect => effect.Wireframe.Values[0].Value = 50), [0]);
        yield return ("wireframe-100", Create(effect => effect.Wireframe.Values[0].Value = 100), [0]);
        yield return ("saturation-0", Create(effect => effect.Saturation.Values[0].Value = 0), [0]);
        yield return ("saturation-100", Create(effect => effect.Saturation.Values[0].Value = 100), [0]);
        yield return ("jitter-0", Create(effect => effect.Jitter.Values[0].Value = 0), [0]);
        yield return ("jitter-100", Create(effect => effect.Jitter.Values[0].Value = 100), [0]);
    }

    public static IEnumerable<(string Name, Func<LowPolyAbstractionEffect> Create, Action<LowPolyAbstractionEffect> Change, int Frame)> Transitions()
    {
        yield return ("quality-high-to-ultra", () => Create(), effect => effect.Quality = LowPolyAbstractionQuality.Ultra, 0);
        yield return ("detail-60-to-100", () => Create(), effect => effect.Detail.Values[0].Value = 100, 0);
        yield return ("fidelity-70-to-0", () => Create(), effect => effect.Fidelity.Values[0].Value = 0, 0);
        yield return ("refine-50-to-100", () => Create(), effect => effect.Refine.Values[0].Value = 100, 0);
        yield return ("seed-0-to-42", () => Create(), effect => effect.Seed = 42, 0);
        yield return ("gradient-30-to-100", () => Create(), effect => effect.Gradient.Values[0].Value = 100, 0);
        yield return ("wireframe-0-to-100", () => Create(), effect => effect.Wireframe.Values[0].Value = 100, 0);
        yield return ("saturation-30-to-100", () => Create(), effect => effect.Saturation.Values[0].Value = 100, 0);
        yield return ("jitter-25-to-100", () => Create(), effect => effect.Jitter.Values[0].Value = 100, 0);
        yield return ("amount-100-to-0", () => Create(), effect => effect.Amount.Values[0].Value = 0, 0);
    }

    public static IEnumerable<(string Name, LowPolyAbstractionEffect Effect)> Benchmarks()
    {
        yield return ("quality-balanced", Create(effect => effect.Quality = LowPolyAbstractionQuality.Balanced));
        yield return ("default", Create());
        yield return ("quality-ultra", Create(effect => effect.Quality = LowPolyAbstractionQuality.Ultra));
        yield return ("detail-100", Create(effect => effect.Detail.Values[0].Value = 100));
        yield return ("amount-0", Create(effect => effect.Amount.Values[0].Value = 0));
    }

    public static LowPolyAbstractionEffect Create(Action<LowPolyAbstractionEffect>? configure = null)
    {
        var effect = new LowPolyAbstractionEffect();
        configure?.Invoke(effect);
        return effect;
    }
}
