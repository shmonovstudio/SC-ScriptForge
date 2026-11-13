using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace ShmonovStudio.AI;

public sealed record AiInstallProgress(string Stage, double Percent, long DownloadedBytes = 0, long TotalBytes = 0);

public sealed class AiInstaller
{
    public Task<AiRuntimeOptions> InstallAsync(string folder, AiModelDownload model, IProgress<AiInstallProgress>? progress = null, CancellationToken cancellationToken = default)
        => InstallAsync(folder, model, AiComputeBackend.Cpu, progress, cancellationToken);

    public async Task<AiRuntimeOptions> InstallAsync(string folder, AiModelDownload model, AiComputeBackend backend, IProgress<AiInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw new PlatformNotSupportedException("Готовые движки рассчитаны на Windows x64. Для другой системы выберите свой сервер ИИ.");
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Выберите папку для движка и моделей.");
        if (Path.GetFileName(model.FileName) != model.FileName) throw new ArgumentException("Некорректное имя файла модели.");
        folder = Path.GetFullPath(folder); Directory.CreateDirectory(folder);
        var package = AiCatalog.Runtime(backend);
        // Different builds never mix their DLLs, including when switching CPU ↔ Vulkan.
        var runtime = Path.Combine(folder, "Runtime", AiCatalog.RuntimeVersion + "-" + package.Id);
        var models = Path.Combine(folder, "Models"); Directory.CreateDirectory(runtime); Directory.CreateDirectory(models);
        var zip = Path.Combine(folder, "llama-" + AiCatalog.RuntimeVersion + "-" + package.Id + ".zip");
        var modelPath = Path.Combine(models, model.FileName);
        long Remaining(string path, long bytes, string hash) => File.Exists(path) ? 0 : Math.Max(0, bytes - (File.Exists(PartialPath(path, hash)) ? new FileInfo(PartialPath(path, hash)).Length : 0));
        var needed = Remaining(zip, package.Bytes, package.Sha256) + Remaining(modelPath, model.Bytes, model.Sha256) + 512L * 1024 * 1024;
        var drive = new DriveInfo(Path.GetPathRoot(folder)!);
        if (drive.IsReady && drive.AvailableFreeSpace < needed) throw new IOException($"Недостаточно места. Освободите минимум {AiHardwareProfile.FormatBytes(needed)} на диске установки.");
        await DownloadAsync(package.Url, zip, package.Bytes, package.Sha256, "Движок · " + package.Id, progress, cancellationToken);
        using (var archive = ZipFile.OpenRead(zip))
        {
            if (archive.Entries.Sum(e => e.Length) > 512_000_000) throw new InvalidDataException("Архив движка превышает допустимый размер.");
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested(); var name = Path.GetFileName(entry.FullName);
                if (name == "llama-server.exe" || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase))
                    entry.ExtractToFile(Path.Combine(runtime, name), true);
            }
        }
        if (!File.Exists(Path.Combine(runtime, "llama-server.exe"))) throw new InvalidDataException("В архиве нет llama-server.exe.");
        await DownloadAsync(model.Url, modelPath, model.Bytes, model.Sha256, model.Name, progress, cancellationToken);
        _ = await Task.Run(() => GgufMetadata.Read(modelPath), cancellationToken);
        void License(string name, string destination)
        {
            if (File.Exists(destination)) return;
            using var source = typeof(AiInstaller).Assembly.GetManifestResourceStream("ShmonovStudio.AI.Licenses." + name) ?? throw new IOException("Лицензия отсутствует в сборке.");
            using var target = new FileStream(destination, FileMode.CreateNew); source.CopyTo(target);
        }
        License("llama.cpp-MIT.txt", Path.Combine(runtime, "LICENSE-llama.cpp.txt"));
        License("Qwen-Apache-2.0.txt", Path.Combine(models, "LICENSE-Qwen.txt"));
        progress?.Report(new("Файлы скачаны и проверены", 100));
        return new() { RuntimePath = Path.Combine(runtime, "llama-server.exe"), ModelPath = modelPath, ComputeBackend = backend, InstallationDirectory = folder };
    }

    public static string PartialPath(string path, string sha256) => path + "." + sha256.ToLowerInvariant() + ".download";

    public static async Task DownloadAsync(string url, string path, long length, string sha256, string stage, IProgress<AiInstallProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (length <= 0 || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw new ArgumentException("Не заданы размер и SHA256 загрузки.");
        var temporary = PartialPath(path, sha256);
        // Keep a stable partial file for resume, but serialize writers (also across app instances).
        await using var downloadLock = new FileStream(temporary + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        async Task<bool> Matches(string file)
        {
            progress?.Report(new("Проверяем SHA256 · " + stage, 100));
            await using var input = File.OpenRead(file);
            return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
        }
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length == length && await Matches(path)) return;
            throw new IOException("В папке уже есть другой файл с таким именем. Выберите другую папку; существующий файл не будет перезаписан.");
        }
        try
        {
            var offset = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
            if (offset > length) { File.Delete(temporary); offset = 0; }
            if (offset < length)
            {
                using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (range is null || range.Unit != "bytes" || range.From != offset || range.To != length - 1 || range.Length != length)
                        throw new InvalidDataException("Сервер вернул неверный диапазон докачивания.");
                }
                else if (response.StatusCode == HttpStatusCode.OK) offset = 0;
                else throw new InvalidDataException("Неподдерживаемый ответ сервера загрузки.");
                if (response.Content.Headers.ContentLength is { } advertised && advertised != length - offset)
                    throw new InvalidDataException("Размер загрузки не соответствует закреплённой версии.");
                var label = (offset > 0 ? "Докачиваем · " : "Скачиваем · ") + stage;
                progress?.Report(new(label, offset * 100d / length, offset, length));
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = new FileStream(temporary, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
                {
                    var buffer = new byte[131072]; long total = offset; var report = Environment.TickCount64;
                    while (await input.ReadAsync(buffer, cancellationToken) is var read && read > 0)
                    {
                        total += read; if (total > length) throw new InvalidDataException("Загрузка превышает ожидаемый размер.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        if (Environment.TickCount64 - report > 150) { progress?.Report(new(label, total * 100d / length, total, length)); report = Environment.TickCount64; }
                    }
                    if (total != length) throw new EndOfStreamException("Соединение прервано. Повторите скачивание: загрузка продолжится с сохранённого места.");
                }
            }
            if (!await Matches(temporary)) throw new InvalidDataException("SHA256 не совпадает. Повреждённая загрузка удалена; повторите скачивание.");
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, path);
        }
        catch (InvalidDataException) { if (File.Exists(temporary)) File.Delete(temporary); throw; }
        // Cancellation, network errors and app exits retain only a non-executable partial file.
    }
}
