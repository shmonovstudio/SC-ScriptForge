using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ShmonovStudio.AI;

public sealed class LlamaCppRuntime(AiRuntimeOptions options) : IAiRuntime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AiRuntimeOptions _options = options;
    private RuntimeProcess? _process;
    private HttpClient? _client;
    private bool _disposed;
    public AiRuntimeState State { get; private set; } = new("Модель выгружена", false);
    public event Action<AiRuntimeState>? StateChanged;
    public int? OwnedProcessId => _process?.Process.Id;
    private void Status(string message, bool loaded, bool loading = false) { State = new(message, loaded, loading); StateChanged?.Invoke(State); }
    public void Configure(AiRuntimeOptions value)
    {
        if (!_gate.Wait(0)) throw new InvalidOperationException("Остановите генерацию перед изменением настроек.");
        try { Stop(); _options = value; } finally { _gate.Release(); }
    }
    private void Stop() { _client?.Dispose(); _client = null; _process?.Dispose(); _process = null; Status("Модель выгружена", false); }
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is not null && !_process.Process.HasExited) return;
        Stop(); _options.Validate();
        _ = await Task.Run(() => GgufMetadata.Read(_options.ModelPath), cancellationToken);
        Status("Загружаем модель в память…", false, true);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var info = new ProcessStartInfo { FileName = Path.GetFullPath(_options.RuntimePath), WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_options.RuntimePath)), UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("LLAMA_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("GGML_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        var secret = RandomNumberGenerator.GetHexString(64); info.Environment["LLAMA_API_KEY"] = secret;
        foreach (var argument in new[] { "--model", Path.GetFullPath(_options.ModelPath), "--host", "127.0.0.1", "--port", port.ToString(), "--alias", "sc-local", "--ctx-size", _options.ContextTokens.ToString(), "--threads", _options.Threads.ToString(), "--threads-batch", _options.Threads.ToString(), "--device", _options.ComputeBackend == AiComputeBackend.Vulkan ? "Vulkan0" : "none", "--n-gpu-layers", _options.ComputeBackend == AiComputeBackend.Vulkan ? "all" : "0", "--parallel", "1", "--batch-size", "256", "--ubatch-size", "128", "--poll", "0", "--jinja", "--reasoning-budget", "0", "--no-webui", "--no-agent", "--offline", "--no-warmup", "--no-context-shift", "--log-disable" }) info.ArgumentList.Add(argument);
        _process = new RuntimeProcess(info);
        _client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(2) }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (_process.Process.HasExited) throw new InvalidOperationException($"llama.cpp завершился с кодом {_process.Process.ExitCode}. " + (_options.ComputeBackend == AiComputeBackend.Vulkan ? "Для GPU нужна видеокарта с Vulkan и актуальный драйвер, а также достаточно свободной видеопамяти. Попробуйте CPU или модель поменьше." : "Проверьте совместимость модели и свободную ОЗУ. Попробуйте режим «Экономно» или другую модель."));
                try { using var response = await _client.GetAsync("v1/models", deadline.Token); if (response.IsSuccessStatusCode) { using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(deadline.Token), cancellationToken: deadline.Token); if (document.RootElement.GetProperty("data").EnumerateArray().Any(x => x.GetProperty("id").GetString() == "sc-local")) break; } }
                catch (HttpRequestException) { }
                // ConnectTimeout can cancel an individual probe while the model is still
                // starting. It is not the three-minute model-load deadline.
                catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { }
                await Task.Delay(150, deadline.Token);
            }
            Status("Модель готова · локально · " + _options.ComputeLabel, true);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { throw new TimeoutException("Модель не загрузилась за 3 минуты. Попробуйте модель поменьше или режим «Экономно»."); }
    }
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { await LoadAsync(cancellationToken); }
        catch { Stop(); throw; }
        finally { _gate.Release(); }
    }
    private static object[] Messages(AiRequest request) => request.Messages.Select(m => (object)new { role = m.Role, content = m.Content }).ToArray();
    public async IAsyncEnumerable<AiDelta> GenerateAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.Messages.Count is 0 or > 16 || request.Messages.Any(m => m.Role is not ("system" or "user" or "assistant") || m.Content.Length > 100_000) || request.MaximumTokens < 1 || request.MaximumTokens > _options.ContextTokens / 2) throw new ArgumentException("Запрос слишком велик. Выберите нужную функцию или диапазон строк.");
        await _gate.WaitAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(10));
        var completed = false;
        try
        {
            await LoadAsync(deadline.Token);
            var client = _client!;
            var messages = Messages(request); var kwargs = new { enable_thinking = false };
            using var formatted = await _client!.PostAsJsonAsync("apply-template", new { messages, chat_template_kwargs = kwargs }, deadline.Token);
            if (!formatted.IsSuccessStatusCode) throw new InvalidOperationException("Runtime не поддерживает шаблон чата. Установите закреплённую сборку llama.cpp.");
            using var formatDocument = await JsonDocument.ParseAsync(await formatted.Content.ReadAsStreamAsync(deadline.Token), cancellationToken: deadline.Token);
            var prompt = formatDocument.RootElement.GetProperty("prompt").GetString()!;
            using var tokenized = await client.PostAsJsonAsync("tokenize", new { content = prompt, add_special = true, parse_special = true }, deadline.Token); tokenized.EnsureSuccessStatusCode();
            using var tokens = await JsonDocument.ParseAsync(await tokenized.Content.ReadAsStreamAsync(deadline.Token), cancellationToken: deadline.Token);
            // Длинный запрос уменьшает место для ответа; отказываем только если ответу почти не остаётся места.
            var available = _options.ContextTokens - tokens.RootElement.GetProperty("tokens").GetArrayLength() - 32;
            if (available < Math.Min(512, request.MaximumTokens)) throw new InvalidOperationException("Запрос не помещается в память диалога. Прикрепите функцию или диапазон строк, сократите запрос или увеличьте «Память диалога» в настройках модели.");
            var body = new Dictionary<string, object?> { ["model"] = "sc-local", ["messages"] = messages, ["stream"] = true, ["max_tokens"] = Math.Min(request.MaximumTokens, available), ["temperature"] = .25, ["chat_template_kwargs"] = kwargs, ["reasoning_effort"] = "none", ["cache_prompt"] = false };
            if (request.JsonSchema is { } schema) body["response_format"] = new { type = "json_object", schema = JsonSerializer.Deserialize<JsonElement>(schema) };
            using var message = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = JsonContent.Create(body) };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Локальная модель отклонила запрос (HTTP {(int)response.StatusCode}). Проверьте контекст и совместимость GGUF/runtime.");
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token); using var reader = new StreamReader(stream);
            var total = 0; var finish = false;
            while (await reader.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length > 262144) throw new InvalidDataException("Слишком большое событие ИИ.");
                if (!line.StartsWith("data: ")) continue;
                var data = line[6..]; if (data == "[DONE]") break;
                using var document = JsonDocument.Parse(data);
                if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                var choice = choices[0];
                var text = choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content) ? content.GetString() ?? "" : "";
                total += text.Length; if (total > 262144) throw new InvalidDataException("Ответ превышает ограничение размера.");
                if (text.Length > 0) yield return new(text);
                if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String) { finish = true; yield return new("", true, reason.GetString() != "stop"); }
            }
            if (!finish) throw new IOException("Поток ИИ прервался. Незавершённое предложение нельзя применить.");
            completed = true;
        }
        finally { if (!completed) Stop(); _gate.Release(); }
    }
    public async Task UnloadAsync(CancellationToken cancellationToken = default) { await _gate.WaitAsync(cancellationToken); try { Stop(); } finally { _gate.Release(); } }
    public void Dispose() { _disposed = true; Stop(); }
}
