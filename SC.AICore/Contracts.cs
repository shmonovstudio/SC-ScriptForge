using System.Text.Json.Serialization;

namespace ShmonovStudio.AI;

public sealed record AiMessage(string Role, string Content);
public sealed record AiRequest(IReadOnlyList<AiMessage> Messages, int MaximumTokens = 1024, string? JsonSchema = null);
public sealed record AiDelta(string Text, bool Finished = false, bool Truncated = false);
public sealed record AiRuntimeState(string Message, bool Loaded, bool Loading = false);

public sealed record AiRuntimeOptions
{
    public const string BuiltinProvider = "Builtin";
    public const string ExternalProvider = "External";
    public const int MaximumContextTokens = 32768;
    /// <summary>Builtin — собственный llama.cpp с GGUF; External — OpenAI-совместимый сервер (Ollama, LM Studio, llama.cpp server, Jan, vLLM).</summary>
    public string Provider { get; init; } = BuiltinProvider;
    public string RuntimePath { get; init; } = "";
    public string ModelPath { get; init; } = "";
    public string InstallationDirectory { get; init; } = "";
    public AiComputeBackend ComputeBackend { get; init; } = AiComputeBackend.Cpu;
    [JsonIgnore] public string ComputeLabel => ComputeBackend == AiComputeBackend.Vulkan ? "GPU · Vulkan" : "CPU";
    public string Endpoint { get; init; } = "http://127.0.0.1:11434/v1";
    public string ExternalModel { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public int ContextTokens { get; init; } = 4096;
    public int Threads { get; init; } = AiHardwareProfile.Current.RecommendedThreads;
    public int MaximumOutputTokens { get; init; } = 1024;
    [JsonIgnore] public bool IsExternal => Provider == ExternalProvider;
    public void Validate()
    {
        if (IsExternal)
        {
            if (!Uri.TryCreate(Endpoint?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Укажите адрес сервера ИИ, например http://127.0.0.1:11434/v1 для Ollama.");
            if (string.IsNullOrWhiteSpace(ExternalModel)) throw new InvalidOperationException("Выберите модель на сервере ИИ в настройках.");
            if (MaximumOutputTokens is < 128 or > 32768) throw new InvalidOperationException("Лимит ответа должен быть от 128 до 32768 токенов.");
            return;
        }
        if (!File.Exists(RuntimePath) || !Path.GetFileName(RuntimePath).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Не установлен llama.cpp. Откройте настройки ИИ и установите движок.");
        if (!File.Exists(ModelPath)) throw new InvalidOperationException("Модель не установлена. Выберите GGUF или установите Qwen3.5 в настройках ИИ.");
        if (!Enum.IsDefined(ComputeBackend)) throw new InvalidOperationException("Выберите CPU или видеокарту в настройках ИИ.");
        if (ComputeBackend == AiComputeBackend.Vulkan && !File.Exists(Path.Combine(Path.GetDirectoryName(RuntimePath)!, "ggml-vulkan.dll")))
            throw new InvalidOperationException("Для видеокарты нужен движок Vulkan. Выберите GPU и скачайте модель через приложение ещё раз: готовая модель будет использована повторно.");
        if (ContextTokens is < 1024 or > MaximumContextTokens || Threads < 1 || Threads > Math.Min(64, Environment.ProcessorCount) || MaximumOutputTokens < 128 || MaximumOutputTokens > ContextTokens / 2)
            throw new InvalidOperationException("Проверьте настройки производительности: длина ответа не может превышать половину памяти диалога.");
    }
}

public interface IAiRuntime : IDisposable
{
    AiRuntimeState State { get; }
    event Action<AiRuntimeState>? StateChanged;
    void Configure(AiRuntimeOptions options);
    Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    IAsyncEnumerable<AiDelta> GenerateAsync(AiRequest request, CancellationToken cancellationToken = default);
    Task UnloadAsync(CancellationToken cancellationToken = default);
}
