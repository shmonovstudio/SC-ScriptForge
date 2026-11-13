namespace ShmonovStudio.AI;

public enum AiPerformanceLevel { Economy, Balanced, Maximum }

/// <summary>Оценка возможностей ПК для локальной модели. Значения — ориентиры для ползунков, а не измерение.</summary>
public sealed record AiHardwareProfile(long MemoryBytes, int LogicalCores)
{
    public static AiHardwareProfile Current { get; } = new(Math.Max(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, 0), Math.Max(1, Environment.ProcessorCount));
    public static IReadOnlyList<int> ContextSteps { get; } = [2048, 4096, 8192, 12288, 16384, 24576, 32768];
    public static IReadOnlyList<int> OutputSteps { get; } = [512, 1024, 1536, 2048, 3072, 4096, 6144, 8192];
    public const long DefaultModelBytes = 2_707_513_696;
    private const long RuntimeOverheadBytes = 400_000_000;

    public double MemoryGigabytes => MemoryBytes / 1024d / 1024d / 1024d;
    public int MaxThreads => Math.Clamp(LogicalCores, 1, 64);
    public int RecommendedThreads => Math.Clamp(LogicalCores / 2, 1, 16);
    /// <summary>Приблизительный размер KV-кэша на токен. Для 4B Q4 ≈ 135 КБ.</summary>
    public static long KvBytesPerToken(long modelBytes) => Math.Max(48_000, (modelBytes <= 0 ? DefaultModelBytes : modelBytes) / 20_000);
    public static long EstimateMemory(long modelBytes, int context) => (modelBytes <= 0 ? DefaultModelBytes : modelBytes) + KvBytesPerToken(modelBytes) * context + RuntimeOverheadBytes;
    /// <summary>Самый большой контекст, при котором модель занимает не больше 70% ОЗУ.</summary>
    public int MaxContext(long modelBytes)
    {
        var budget = (long)(MemoryBytes * 0.7);
        var best = ContextSteps[0];
        foreach (var step in ContextSteps) if (MemoryBytes == 0 || EstimateMemory(modelBytes, step) <= budget) best = step;
        return best;
    }
    public bool CanRun(long modelBytes) => MemoryBytes == 0 || EstimateMemory(modelBytes, ContextSteps[0]) <= MemoryBytes * 0.85;
    public static int MaxOutput(int context) => OutputSteps.Where(s => s <= context / 2).DefaultIfEmpty(OutputSteps[0]).Max();
    public AiPerformanceLevel Recommended(long modelBytes) => MaxContext(modelBytes) >= 8192 && LogicalCores >= 8 ? AiPerformanceLevel.Balanced : AiPerformanceLevel.Economy;
    public AiRuntimeOptions Apply(AiRuntimeOptions options, AiPerformanceLevel level, long modelBytes)
    {
        var maximum = MaxContext(modelBytes);
        var context = level switch { AiPerformanceLevel.Economy => Math.Min(4096, maximum), AiPerformanceLevel.Balanced => Math.Min(8192, maximum), _ => Math.Min(16384, maximum) };
        var threads = level switch { AiPerformanceLevel.Economy => Math.Max(1, RecommendedThreads / 2), AiPerformanceLevel.Balanced => RecommendedThreads, _ => Math.Clamp(LogicalCores - 2, RecommendedThreads, MaxThreads) };
        var output = Math.Min(level switch { AiPerformanceLevel.Economy => 1024, AiPerformanceLevel.Balanced => 2048, _ => 4096 }, MaxOutput(context));
        return options with { ContextTokens = context, Threads = threads, MaximumOutputTokens = output };
    }
    public static string FormatBytes(long bytes) => bytes >= 1_000_000_000 ? $"{bytes / 1024d / 1024d / 1024d:0.#} ГБ" : $"{bytes / 1024d / 1024d:0} МБ";
}
