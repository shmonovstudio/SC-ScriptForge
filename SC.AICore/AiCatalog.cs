namespace ShmonovStudio.AI;

public enum AiComputeBackend { Cpu, Vulkan }

public sealed record AiModelDownload(string Name, string FileName, long Bytes, string Sha256, string Url)
{
    public string Description { get; init; } = "";
    public int RecommendedRamGb { get; init; }
    public int RecommendedVramGb { get; init; }
}

public sealed record AiRuntimeDownload(string Id, string Url, long Bytes, string Sha256);

public static class AiCatalog
{
    public const string RuntimeVersion = "b11669";
    public const string RuntimeUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11669/llama-b11669-bin-win-cpu-x64.zip";
    public const string RuntimeSha256 = "37b3efcf962847ca869745fd4ccf7f5e7e8dacfeff9fa3523b4ca8cdc66d632b";
    public static AiRuntimeDownload Cpu { get; } = new("cpu", RuntimeUrl, 19537886, RuntimeSha256);
    public static AiRuntimeDownload Vulkan { get; } = new("vulkan", "https://github.com/ggml-org/llama.cpp/releases/download/b11669/llama-b11669-bin-win-vulkan-x64.zip", 32182350, "2c07574e7bde71321cb785282b20cc821e951132149eeceeea32ddebdbf0598e");
    public static AiRuntimeDownload Runtime(AiComputeBackend backend) => backend switch
    {
        AiComputeBackend.Cpu => Cpu, AiComputeBackend.Vulkan => Vulkan,
        _ => throw new ArgumentOutOfRangeException(nameof(backend))
    };
    public static AiModelDownload Qwen2B { get; } = new("Qwen3.5 2B · Q4_K_M", "Qwen3.5-2B-Q4_K_M.gguf", 1270808032, "0bfe35afc9f05b7fac3fa04925e051ac7939a42a8a17ea11afc99701bea826cc", "https://huggingface.co/lmstudio-community/Qwen3.5-2B-GGUF/resolve/bb84e11355a036e28f080c7793fa6d22b7c4e344/Qwen3.5-2B-Q4_K_M.gguf")
    { Description = "Лёгкая · короткие вопросы, объяснения и небольшие скрипты. Для ноутбуков и офисных ПК; качество сложных правок ниже.", RecommendedRamGb = 8, RecommendedVramGb = 4 };
    public static AiModelDownload Qwen4B { get; } = new("Qwen3.5 4B · Q4_K_M", "Qwen3.5-4B-Q4_K_M.gguf", 2707513696, "25082a7dd3776cc3c741c6347d3bd04523f05796607b3fbc32fa3a25dfa1418c", "https://huggingface.co/lmstudio-community/Qwen3.5-4B-GGUF/resolve/f9f88ac3e234be915e23811a6d28ea287bdb927e/Qwen3.5-4B-Q4_K_M.gguf")
    { Description = "Универсальная · повседневная работа с PowerShell, исправления и генерация кода. Для современных домашних и рабочих ПК.", RecommendedRamGb = 16, RecommendedVramGb = 6 };
    public static AiModelDownload Qwen9B { get; } = new("Qwen3.5 9B · Q4_K_M", "Qwen3.5-9B-Q4_K_M.gguf", 5627044256, "cd76ec205963b3b33350093e6904d9de16c4e666fd104e1f632d25c7f15f2a13", "https://huggingface.co/lmstudio-community/Qwen3.5-9B-GGUF/resolve/1379f25c6b505a3fc737bd7818cb09389cf807c1/Qwen3.5-9B-Q4_K_M.gguf")
    { Description = "Мощная · сложные правки и более подробные ответы. Для игровых ПК и рабочих станций; рекомендуется видеокарта. На CPU будет медленнее.", RecommendedRamGb = 32, RecommendedVramGb = 12 };
    public static IReadOnlyList<AiModelDownload> Models { get; } = [Qwen2B, Qwen4B, Qwen9B];
}
