using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ShmonovStudio.AI;

/// <summary>
/// Подключение к уже запущенному OpenAI-совместимому серверу: Ollama, LM Studio, llama.cpp server, Jan, vLLM.
/// Приложение не запускает и не устанавливает такой сервер; модель и её память принадлежат ему.
/// </summary>
public sealed class OpenAiCompatibleRuntime(AiRuntimeOptions options) : IAiRuntime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AiRuntimeOptions _options = options;
    private bool _disposed;
    public AiRuntimeState State { get; private set; } = new("Внешний сервер · " + Host(options.Endpoint), false);
    public event Action<AiRuntimeState>? StateChanged;
    private void Status(string message, bool loaded, bool loading = false) { State = new(message, loaded, loading); StateChanged?.Invoke(State); }

    public static Uri BaseUri(string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new InvalidOperationException("Некорректный адрес сервера ИИ.");
        var path = uri.AbsolutePath.TrimEnd('/');
        // Пользователи часто вводят адрес Ollama или LM Studio без /v1.
        if (path.Length == 0) path = "/v1";
        return new UriBuilder(uri) { Path = path + "/", Query = "" }.Uri;
    }
    public static string Host(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Authority : endpoint;
    public static bool IsLocal(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(uri.Host, out var address)) return uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase);
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && (bytes[0] == 10 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31);
    }
    private static HttpClient Client(string endpoint, string apiKey, TimeSpan connect)
    {
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = connect }) { BaseAddress = BaseUri(endpoint), Timeout = Timeout.InfiniteTimeSpan };
        if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        return client;
    }

    /// <summary>Список моделей сервера. Используется кнопкой «Найти модели» в настройках.</summary>
    public static async Task<IReadOnlyList<string>> ListModelsAsync(string endpoint, string apiKey, CancellationToken cancellationToken = default)
    {
        using var client = Client(endpoint, apiKey, TimeSpan.FromSeconds(3));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        HttpResponseMessage response;
        try { response = await client.GetAsync("models", deadline.Token); }
        catch (HttpRequestException) { throw new InvalidOperationException($"Сервер {Host(endpoint)} не отвечает. Запустите Ollama / LM Studio и проверьте адрес."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException($"Сервер {Host(endpoint)} не ответил за 10 секунд."); }
        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new InvalidOperationException("Сервер требует ключ доступа. Укажите API-ключ.");
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Сервер ответил HTTP {(int)response.StatusCode}. Проверьте, что адрес заканчивается на /v1.");
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(deadline.Token), cancellationToken: deadline.Token);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Сервер не вернул список моделей в формате OpenAI.");
            return data.EnumerateArray().Select(m => m.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "").Where(id => id.Length > 0).Distinct().OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public void Configure(AiRuntimeOptions value)
    {
        if (!_gate.Wait(0)) throw new InvalidOperationException("Остановите генерацию перед изменением настроек.");
        try { _options = value; Status("Внешний сервер · " + Host(value.Endpoint), false); } finally { _gate.Release(); }
    }

    public async IAsyncEnumerable<AiDelta> GenerateAsync(AiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.Messages.Count is 0 or > 16 || request.Messages.Any(m => m.Role is not ("system" or "user" or "assistant") || m.Content.Length > 200_000) || request.MaximumTokens is < 1 or > 32768)
            throw new ArgumentException("Запрос слишком велик. Выберите нужную функцию или диапазон строк.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var options = _options; options.Validate();
            using var client = Client(options.Endpoint, options.ApiKey, TimeSpan.FromSeconds(5));
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(15));
            if (!State.Loaded) Status("Подключаемся к " + Host(options.Endpoint) + "…", false, true);
            var messages = request.Messages.Select(m => (object)new { role = m.Role, content = m.Content }).ToArray();
            // Не все серверы понимают json_schema: пробуем строгую схему, затем json_object, затем обычный ответ с инструкцией в промпте.
            object?[] formats = request.JsonSchema is { } schema
                ? [new { type = "json_schema", json_schema = new { name = "sc_result", strict = true, schema = JsonSerializer.Deserialize<JsonElement>(schema) } }, new { type = "json_object" }, null]
                : [null];
            HttpResponseMessage? response = null;
            foreach (var format in formats)
            {
                var body = new Dictionary<string, object?> { ["model"] = options.ExternalModel, ["messages"] = messages, ["stream"] = true, ["max_tokens"] = request.MaximumTokens, ["temperature"] = .25 };
                if (format is not null) body["response_format"] = format;
                var message = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = JsonContent.Create(body) };
                try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token); }
                catch (HttpRequestException) { message.Dispose(); throw new InvalidOperationException($"Сервер ИИ {Host(options.Endpoint)} недоступен. Запустите его или проверьте адрес в настройках."); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { message.Dispose(); throw new TimeoutException("Сервер ИИ не ответил вовремя."); }
                message.Dispose();
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity && format is not null) { response.Dispose(); response = null; continue; }
                break;
            }
            if (response is null) throw new InvalidOperationException("Сервер ИИ отклонил запрос.");
            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new InvalidOperationException("Сервер ИИ отклонил ключ доступа.");
                if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException($"Модель «{options.ExternalModel}» не найдена на сервере. Выберите модель в настройках.");
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Сервер ИИ ответил HTTP {(int)response.StatusCode}. Проверьте модель и размер контекста на сервере.");
                Status("Подключено · " + options.ExternalModel, true);
                using var stream = await response.Content.ReadAsStreamAsync(deadline.Token); using var reader = new StreamReader(stream);
                var total = 0; var finish = false;
                while (await reader.ReadLineAsync(deadline.Token) is { } line)
                {
                    if (line.Length > 1_048_576) throw new InvalidDataException("Слишком большое событие ИИ.");
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var data = line[5..].Trim(); if (data == "[DONE]") break;
                    if (data.Length == 0) continue;
                    using var document = JsonDocument.Parse(data);
                    if (document.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException("Сервер ИИ: " + (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var text) ? text.GetString() : error.ToString()));
                    if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
                    var choice = choices[0];
                    var delta = choice.TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : "";
                    total += delta.Length; if (total > 1_048_576) throw new InvalidDataException("Ответ превышает ограничение размера.");
                    if (delta.Length > 0) yield return new(delta);
                    if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String) { finish = true; yield return new("", true, reason.GetString() == "length"); }
                }
                if (!finish) yield return new("", true);
            }
        }
        finally { _gate.Release(); }
    }
    public Task UnloadAsync(CancellationToken cancellationToken = default) { Status("Внешний сервер · " + Host(_options.Endpoint), false); return Task.CompletedTask; }
    public void Dispose() => _disposed = true;
}

/// <summary>Переключает встроенный llama.cpp и внешний сервер без перезапуска приложения.</summary>
public sealed class AiRuntimeHost : IAiRuntime
{
    private IAiRuntime _current;
    public AiRuntimeHost(AiRuntimeOptions options) { _current = Create(options); _current.StateChanged += Forward; }
    private static IAiRuntime Create(AiRuntimeOptions options) => options.IsExternal ? new OpenAiCompatibleRuntime(options) : new LlamaCppRuntime(options);
    private void Forward(AiRuntimeState state) => StateChanged?.Invoke(state);
    public AiRuntimeState State => _current.State;
    public event Action<AiRuntimeState>? StateChanged;
    public void Configure(AiRuntimeOptions options)
    {
        if (options.IsExternal == _current is OpenAiCompatibleRuntime) { _current.Configure(options); return; }
        _current.Configure(options); // бросает исключение, если идёт генерация
        _current.StateChanged -= Forward; _current.Dispose();
        _current = Create(options); _current.StateChanged += Forward; Forward(_current.State);
    }
    public IAsyncEnumerable<AiDelta> GenerateAsync(AiRequest request, CancellationToken cancellationToken = default) => _current.GenerateAsync(request, cancellationToken);
    public Task PrepareAsync(CancellationToken cancellationToken = default) => _current.PrepareAsync(cancellationToken);
    public Task UnloadAsync(CancellationToken cancellationToken = default) => _current.UnloadAsync(cancellationToken);
    public void Dispose() { _current.StateChanged -= Forward; _current.Dispose(); }
}
