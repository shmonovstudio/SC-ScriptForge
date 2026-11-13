using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using SC.ScriptForge.Models;

namespace SC.ScriptForge.Services;

public sealed class ScriptRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static IReadOnlyList<string> DefaultCategories { get; } = Array.AsReadOnly(new[]
    {
        "Active Directory", "Exchange", "Windows", "Network", "Files", "Security", "Servers", "Certificates", "Microsoft 365", "Other"
    });

    private readonly object _sync = new();
    public IReadOnlyList<string> Categories { get; private set; } = DefaultCategories;

    public string DataRoot { get; }
    public string ScriptsRoot { get; }
    public string VersionsRoot { get; }
    public string TrashRoot { get; }

    public ScriptRepository(string? dataRoot = null)
    {
        // Portable folder if writable; otherwise use per-user AppData (e.g., installed in Program Files).
        var portable = dataRoot is null ? Path.Combine(AppContext.BaseDirectory, "Data") : Path.GetFullPath(dataRoot);
        try
        {
            Directory.CreateDirectory(portable);
            var probe = Path.Combine(portable, $".write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            DataRoot = portable;
        }
        catch (UnauthorizedAccessException) when (dataRoot is null)
        {
            DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shmonov Studio", "SC ScriptForge", "Data");
        }
        catch (IOException) when (dataRoot is null)
        {
            DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shmonov Studio", "SC ScriptForge", "Data");
        }

        ScriptsRoot = Path.Combine(DataRoot, "Scripts");
        VersionsRoot = Path.Combine(DataRoot, ".versions");
        TrashRoot = Path.Combine(DataRoot, ".trash");
        Directory.CreateDirectory(ScriptsRoot);
        Directory.CreateDirectory(VersionsRoot);
        Directory.CreateDirectory(TrashRoot);
        foreach (var category in Categories)
            Directory.CreateDirectory(Path.Combine(ScriptsRoot, category));
        ReloadCategories();
    }

    private void ReloadCategories() => Categories = Array.AsReadOnly(DefaultCategories
        .Concat(Directory.GetDirectories(ScriptsRoot).Select(Path.GetFileName).OfType<string>())
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

    public void InstallBundledScripts()
    {
        lock (_sync)
        {
            const string prefix = "SC.ScriptForge.Library/";
            var assembly = typeof(ScriptRepository).Assembly;
            var resources = assembly.GetManifestResourceNames();
            var ledgerPath = Path.Combine(DataRoot, ".bundled-scripts.json");
            var installed = File.Exists(ledgerPath)
                ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(ledgerPath)) ?? new() : new HashSet<string>();
            foreach (var resource in resources.Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".ps1", StringComparison.Ordinal)))
            {
                var relative = resource[prefix.Length..].Replace('\\', '/');
                if (installed.Contains(relative)) continue;
                var target = Path.Combine(ScriptsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(target) && !File.Exists(MetaPath(target)))
                {
                    using var script = assembly.GetManifestResourceStream(resource)!;
                    using var meta = assembly.GetManifestResourceStream(resource + ".meta.json")
                        ?? throw new IOException("Bundled script metadata is missing: " + relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var scriptReader = new StreamReader(script);
                    using var metaReader = new StreamReader(meta);
                    AtomicWrite(MetaPath(target), metaReader.ReadToEnd(), new UTF8Encoding(false));
                    AtomicWrite(target, scriptReader.ReadToEnd(), new UTF8Encoding(true));
                }
                installed.Add(relative);
            }
            // A deleted or moved bundled script is not recreated on the next application start.
            AtomicWrite(ledgerPath, JsonSerializer.Serialize(installed, JsonOptions), new UTF8Encoding(false));
            ReloadCategories();
        }
    }

    public void CreateCategory(string name)
    {
        lock (_sync)
        {
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 70 || name.StartsWith('.') || name.EndsWith('.') ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || SafeFileName(name) != name)
                throw new ArgumentException("Укажите корректное название категории (до 70 символов, без специальных символов пути).");
            if (Categories.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Такая категория уже существует.");
            Directory.CreateDirectory(Path.Combine(ScriptsRoot, name));
            ReloadCategories();
        }
    }

    public List<ScriptEntry> LoadAll()
    {
        lock (_sync)
        {
            ReloadCategories();
            var entries = new List<ScriptEntry>();
            foreach (var path in Directory.EnumerateFiles(ScriptsRoot, "*.ps1", SearchOption.AllDirectories))
            {
                try
                {
                    var category = Path.GetRelativePath(ScriptsRoot, path).Split(Path.DirectorySeparatorChar)[0];
                    var metaExists = File.Exists(MetaPath(path));
                    var entry = ReadMeta(path);
                    entry.FilePath = path;
                    entry.Name = string.IsNullOrWhiteSpace(entry.Name) ? Path.GetFileNameWithoutExtension(path) : entry.Name;
                    entry.Category = !metaExists || string.IsNullOrWhiteSpace(entry.Category) ? category : entry.Category;
                    entry.Status = NormalizeStatus(entry.Status);
                    entry.Tags ??= new List<string>();
                    entry.Translations ??= new();
                    entry.FileUpdated = File.GetLastWriteTime(path);
                    if (!metaExists) SaveMeta(entry, touchUpdated: false);
                    if (entry.Id == Guid.Empty) { entry.Id = Guid.NewGuid(); SaveMeta(entry, touchUpdated: false); }
                    MigrateLegacyVersions(entry);
                    entries.Add(entry);
                }
                catch (IOException) { /* Skip inaccessible file. Other scripts are still usable. */ }
                catch (UnauthorizedAccessException) { }
                catch (JsonException) { }
            }
            return entries.OrderByDescending(x => x.Favorite).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public ScriptEntry Create(string name, string category, string description, string notes, IEnumerable<string> tags, bool favorite, string status)
    {
        lock (_sync)
        {
            var path = UniquePath(Path.Combine(ScriptsRoot, SafeCategory(category)), name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Template(Path.GetFileNameWithoutExtension(path), category), new UTF8Encoding(true));
            var now = DateTime.Now;
            var entry = new ScriptEntry
            {
                FilePath = path, Name = name.Trim(), Category = SafeCategory(category), Description = description,
                Notes = notes, Tags = tags.ToList(), Favorite = favorite, Status = NormalizeStatus(status), Created = now, Updated = now,
                FileUpdated = now
            };
            SaveMeta(entry);
            Snapshot(entry, "Создание сценария");
            return entry;
        }
    }

    public ScriptEntry Import(string sourcePath, string category)
    {
        lock (_sync)
        {
            if (!File.Exists(sourcePath) || !string.Equals(Path.GetExtension(sourcePath), ".ps1", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Можно импортировать только существующий файл .ps1.");

            var path = UniquePath(Path.Combine(ScriptsRoot, SafeCategory(category)), Path.GetFileNameWithoutExtension(sourcePath));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(sourcePath, path);
            var now = DateTime.Now;
            var entry = new ScriptEntry
            {
                Name = Path.GetFileNameWithoutExtension(sourcePath), FilePath = path, Category = SafeCategory(category),
                Description = "Импортирован из " + Path.GetFileName(sourcePath), Status = "Draft", Created = now, Updated = now,
                FileUpdated = File.GetLastWriteTime(path)
            };
            SaveMeta(entry);
            Snapshot(entry, "Импорт");
            return entry;
        }
    }

    public void SaveMeta(ScriptEntry entry, bool touchUpdated = true)
    {
        lock (_sync)
        {
            if (touchUpdated) entry.Updated = DateTime.Now;
            var json = JsonSerializer.Serialize(entry, JsonOptions);
            // Metadata writes are atomic so a crash cannot truncate a working card.
            var target = MetaPath(entry.FilePath);
            AtomicWrite(target, json, new UTF8Encoding(false));
        }
    }

    public void EditMetadata(ScriptEntry entry, string name, string category, string description, string notes, IEnumerable<string> tags, bool favorite, string status)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Название не может быть пустым.");
            // Preserve the original filename when the visible title is renamed. This keeps links and version paths stable.
            var newFolder = Path.Combine(ScriptsRoot, SafeCategory(category));
            Directory.CreateDirectory(newFolder);
            var oldFile = entry.FilePath;
            var newFile = Path.Combine(newFolder, Path.GetFileName(oldFile));
            if (!string.Equals(oldFile, newFile, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(newFile)) throw new IOException("Сценарий с таким файлом уже есть в выбранной категории.");
                File.Move(oldFile, newFile);
                if (File.Exists(MetaPath(oldFile))) File.Move(MetaPath(oldFile), MetaPath(newFile));
                entry.FilePath = newFile;
            }
            entry.Name = name.Trim();
            entry.Category = SafeCategory(category);
            entry.Description = description;
            entry.Notes = notes;
            entry.Tags = tags.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            entry.Favorite = favorite;
            entry.Status = NormalizeStatus(status);
            SaveMeta(entry);
        }
    }

    public ScriptEntry Duplicate(ScriptEntry entry)
    {
        lock (_sync)
        {
            var newPath = UniquePath(Path.GetDirectoryName(entry.FilePath)!, entry.Name + " Copy");
            File.Copy(entry.FilePath, newPath);
            var now = DateTime.Now;
            var copy = new ScriptEntry
            {
                FilePath = newPath, FileUpdated = now, Name = Path.GetFileNameWithoutExtension(newPath),
                Description = entry.Description, Notes = entry.Notes, Tags = entry.Tags.ToList(),
                Category = entry.Category, Status = "Draft", Created = now, Updated = now,
                Translations = entry.Translations.ToDictionary(p => p.Key, p => new ScriptTranslation { Name = p.Value.Name, Description = p.Value.Description, Notes = p.Value.Notes })
            };
            SaveMeta(copy);
            Snapshot(copy, "Копия сценария");
            return copy;
        }
    }

    public void WriteCode(ScriptEntry entry, string code, string? reason = null, bool preserveEncoding = false)
    {
        lock (_sync)
        {
            var current = ReadCode(entry.FilePath);
            if (current == code) return;
            Encoding encoding = new UTF8Encoding(true);
            if (preserveEncoding)
            {
                var bytes = File.ReadAllBytes(entry.FilePath);
                encoding = bytes.AsSpan().StartsWith(new byte[] { 0xFF,0xFE,0,0 }) ? new UTF32Encoding(false,true) : bytes.AsSpan().StartsWith(new byte[] { 0,0,0xFE,0xFF }) ? new UTF32Encoding(true,true) : bytes.AsSpan().StartsWith(new byte[] { 0xFF,0xFE }) ? Encoding.Unicode : bytes.AsSpan().StartsWith(new byte[] { 0xFE,0xFF }) ? Encoding.BigEndianUnicode : new UTF8Encoding(bytes.AsSpan().StartsWith(new byte[] { 0xEF,0xBB,0xBF }));
            }
            Snapshot(entry, reason is null ? "До редактирования" : "До применения SC AI");
            AtomicWrite(entry.FilePath, code, encoding);
            entry.FileUpdated = File.GetLastWriteTime(entry.FilePath);
            SaveMeta(entry);
            Snapshot(entry, reason ?? "После редактирования");
        }
    }

    public static string ReadCode(string filePath) => File.ReadAllText(filePath);

    public static string MetadataHash(ScriptMetadata metadata) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata))));
    public ScriptReviewSnapshot ReadForReview(ScriptEntry entry)
    {
        lock (_sync)
        {
            var path = Path.GetFullPath(entry.FilePath);
            if (!path.StartsWith(Path.GetFullPath(ScriptsRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Скрипт находится вне библиотеки.");
            if (new FileInfo(path).Length > 1_048_576) throw new IOException("Для ИИ выберите скрипт размером до 1 МБ.");
            var current = ReadMeta(path); if (current.Id != entry.Id) throw new IOException("Идентификатор скрипта изменился. Обновите библиотеку.");
            var bytes = File.ReadAllBytes(path); using var reader = new StreamReader(new MemoryStream(bytes),new UTF8Encoding(false,true),detectEncodingFromByteOrderMarks: true);
            var metadata = new ScriptMetadata(current.Name, current.Category, current.Description, current.Notes, current.Tags.ToArray(), current.Favorite, current.Status);
            return new(entry.Id, path, reader.ReadToEnd(), Convert.ToHexString(SHA256.HashData(bytes)), metadata, MetadataHash(metadata));
        }
    }
    public ScriptEntry ApplyReviewedChange(ScriptReviewSnapshot snapshot, string? code, ScriptMetadata? metadata)
    {
        lock (_sync)
        {
            if ((code is null) == (metadata is null)) throw new ArgumentException("Подтвердите одно предложение за раз.");
            var current = ReadForReview(new ScriptEntry { Id = snapshot.Id, FilePath = snapshot.Path });
            if (current.ContentHash != snapshot.ContentHash || current.MetadataHash != snapshot.MetadataHash) throw new IOException("Скрипт или его сведения изменились после анализа. Создайте новое предложение; текущие данные сохранены.");
            var entry = ReadMeta(snapshot.Path); entry.FilePath = snapshot.Path; entry.FileUpdated = File.GetLastWriteTime(snapshot.Path);
            if (code is not null) WriteCode(entry, code,"Предложение SC AI",preserveEncoding:true);
            else { var value = metadata!; EditMetadata(entry, value.Name, value.Category, value.Description, value.Notes, value.Tags, value.Favorite, value.Status); }
            return entry;
        }
    }

    public ScriptVersion Snapshot(ScriptEntry entry, string reason)
    {
        lock (_sync)
        {
            var folder = Path.Combine(VersionsRoot, entry.Id.ToString("N"));
            Directory.CreateDirectory(folder);
            var baseName = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
            var versionFile = Path.Combine(folder, baseName + ".ps1");
            File.Copy(entry.FilePath, versionFile);
            File.WriteAllText(Path.Combine(folder, baseName + ".json"), JsonSerializer.Serialize(new VersionRecord
            {
                Created = DateTime.Now, Reason = reason
            }, JsonOptions), new UTF8Encoding(false));
            return new ScriptVersion { Path = versionFile, Timestamp = DateTime.Now, Reason = reason };
        }
    }

    public List<ScriptVersion> GetVersions(ScriptEntry entry)
    {
        lock (_sync)
        {
            var folder = Path.Combine(VersionsRoot, entry.Id.ToString("N"));
            if (!Directory.Exists(folder)) return new List<ScriptVersion>();
            var versions = new List<ScriptVersion>();
            foreach (var versionPath in Directory.EnumerateFiles(folder, "*.ps1"))
            {
                VersionRecord? record = null;
                var metadataPath = Path.ChangeExtension(versionPath, ".json");
                try
                {
                    if (File.Exists(metadataPath))
                        record = JsonSerializer.Deserialize<VersionRecord>(File.ReadAllText(metadataPath), JsonOptions);
                }
                catch (JsonException) { }
                versions.Add(new ScriptVersion
                {
                    Path = versionPath,
                    Timestamp = record?.Created ?? File.GetLastWriteTime(versionPath),
                    Reason = record?.Reason ?? "Снимок версии"
                });
            }
            return versions.OrderByDescending(x => x.Timestamp).ToList();
        }
    }

    public void Restore(ScriptEntry entry, ScriptVersion version)
    {
        lock (_sync)
        {
            if (!File.Exists(version.Path)) throw new FileNotFoundException("Не удалось найти сохранённую версию.");
            Snapshot(entry, "Перед восстановлением");
            var temporary = entry.FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(version.Path, temporary); File.Move(temporary, entry.FilePath, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            entry.FileUpdated = File.GetLastWriteTime(entry.FilePath);
            SaveMeta(entry);
            Snapshot(entry, "Восстановлено из истории");
        }
    }

    public TrashEntry MoveToTrash(ScriptEntry entry)
    {
        lock (_sync)
        {
            var folder = Path.Combine(TrashRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var originalRelative = Path.GetRelativePath(ScriptsRoot, entry.FilePath);
            var manifest = new TrashEntry { Folder = folder, OriginalRelativePath = originalRelative, Name = entry.Name, DeletedAt = DateTime.Now };
            var script = Path.Combine(folder, "script.ps1");
            var metadata = Path.Combine(folder, "script.meta.json");
            try
            {
                // A discoverable recovery manifest exists before the original files move.
                AtomicWrite(Path.Combine(folder, "trash.json"), JsonSerializer.Serialize(manifest, JsonOptions), Encoding.UTF8);
                File.Move(entry.FilePath, script);
                if (File.Exists(MetaPath(entry.FilePath))) File.Move(MetaPath(entry.FilePath), metadata);
                return manifest;
            }
            catch
            {
                try
                {
                    if (File.Exists(metadata)) File.Move(metadata, MetaPath(entry.FilePath));
                    if (File.Exists(script)) File.Move(script, entry.FilePath);
                    File.Delete(Path.Combine(folder, "trash.json"));
                    Directory.Delete(folder);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException) { /* Keep the manifest and any remaining files for recovery. */ }
                throw;
            }
        }
    }

    public List<TrashEntry> GetTrash()
    {
        lock (_sync)
        {
            var trash = new List<TrashEntry>();
            foreach (var folder in Directory.GetDirectories(TrashRoot))
            {
                try
                {
                    var manifestFile = Path.Combine(folder, "trash.json");
                    if (!File.Exists(manifestFile)) continue;
                    var entry = JsonSerializer.Deserialize<TrashEntry>(File.ReadAllText(manifestFile), JsonOptions);
                    if (entry is not null && File.Exists(Path.Combine(folder, "script.ps1")))
                    {
                        entry.Folder = folder; // Never trust path from JSON
                        trash.Add(entry);
                    }
                }
                catch (IOException) { }
                catch (JsonException) { }
            }
            return trash.OrderByDescending(x => x.DeletedAt).ToList();
        }
    }

    public void RestoreFromTrash(TrashEntry trash)
    {
        lock (_sync)
        {
            ValidateTrashFolder(trash.Folder);
            var root = Path.GetFullPath(ScriptsRoot + Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(ScriptsRoot, trash.OriginalRelativePath));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Некорректный путь в корзине.");
            if (File.Exists(target) || File.Exists(MetaPath(target))) target = UniquePath(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + " Restored");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var script = Path.Combine(trash.Folder, "script.ps1");
            var metaFile = Path.Combine(trash.Folder, "script.meta.json");
            File.Move(script, target);
            try { if (File.Exists(metaFile)) File.Move(metaFile, MetaPath(target)); }
            catch { File.Move(target, script); throw; }
            Directory.Delete(trash.Folder, true);
        }
    }

    public void PermanentlyDelete(TrashEntry trash)
    {
        lock (_sync)
        {
            ValidateTrashFolder(trash.Folder);
            Directory.Delete(trash.Folder, recursive: true);
        }
    }

    public void CreateBackup(string zipPath)
    {
        lock (_sync)
        {
            if (Path.GetFullPath(zipPath).StartsWith(Path.GetFullPath(DataRoot + Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Сохраняйте резервную копию вне папки Data.");
            var temporary = zipPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                ZipFile.CreateFromDirectory(DataRoot, temporary, CompressionLevel.Optimal, includeBaseDirectory: true);
                File.Move(temporary, zipPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public string GetRelativePath(ScriptEntry entry) => Path.GetRelativePath(ScriptsRoot, entry.FilePath);

    public string GetInsights(ScriptEntry entry)
    {
        lock (_sync)
        {
            var file = new FileInfo(entry.FilePath);
            if (file.Length > 2_000_000) return $"Размер: {file.Length / 1024d / 1024d:F1} МБ\nПрофиль больших файлов не рассчитывается автоматически.\nИзменён: {entry.FileUpdated:dd.MM.yyyy HH:mm}";
            var code = ReadCode(entry.FilePath);
            var lines = code.Replace("\r\n", "\n").Split('\n');
            var meaningful = lines.Count(l => !string.IsNullOrWhiteSpace(l));
            var functions = Regex.Matches(code, @"(?im)^\s*function\s+[\w-]+").Count;
            var modules = Regex.Matches(code, @"(?im)^\s*Import-Module\b").Count;
            var versionCount = GetVersions(entry).Count;
            var hasDestructive = Regex.IsMatch(code, @"\b(Remove-ADUser|Remove-Item|Remove-Mailbox|Clear-Content)\b", RegexOptions.IgnoreCase);
            return $"{meaningful} непустых строк\n{functions} функций · {modules} импортов\n{versionCount} версий в истории\nИзменён: {entry.FileUpdated:dd.MM.yyyy HH:mm}" +
                   (hasDestructive ? "\n⚠ Есть потенциально удаляющие команды" : "");
        }
    }

    private ScriptEntry ReadMeta(string filePath)
    {
        try
        {
            if (File.Exists(MetaPath(filePath)))
            {
                var json = File.ReadAllText(MetaPath(filePath));
                // Older PowerShell prototypes did not store stable IDs. Upgrade sidecars on first load.
                var entry = JsonSerializer.Deserialize<ScriptEntry>(json, JsonOptions) ?? new ScriptEntry();
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.EnumerateObject().Any(p => string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase)))
                    entry.Id = Guid.Empty;
                return entry;
            }
        }
        catch (JsonException) { }
        return new ScriptEntry
        {
            Name = Path.GetFileNameWithoutExtension(filePath), Created = File.GetCreationTime(filePath),
            Updated = File.GetLastWriteTime(filePath)
        };
    }

    private void MigrateLegacyVersions(ScriptEntry entry)
    {
        var relative = Path.GetRelativePath(ScriptsRoot, entry.FilePath);
        var legacyName = new string(relative.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
        var oldFolder = Path.Combine(VersionsRoot, legacyName);
        var newFolder = Path.Combine(VersionsRoot, entry.Id.ToString("N"));
        if (!Directory.Exists(oldFolder) || string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(newFolder);
        foreach (var path in Directory.EnumerateFiles(oldFolder))
        {
            var newPath = Path.Combine(newFolder, Path.GetFileName(path));
            if (!File.Exists(newPath)) File.Copy(path, newPath);
        }
        // Old directory deliberately retained as a backup during migration.
    }

    private static string MetaPath(string path) => path + ".meta.json";

    private static string NormalizeStatus(string? value) => value switch
    {
        "Ready" => "Ready", "Archived" => "Archived", _ => "Draft"
    };

    private string SafeCategory(string category) => Categories.Contains(category) ? category : "Other";

    private void ValidateTrashFolder(string folder)
    {
        var root = Path.GetFullPath(TrashRoot + Path.DirectorySeparatorChar);
        if (!Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Некорректная папка корзины.");
    }

    private static void AtomicWrite(string path, string content, Encoding encoding)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, encoding);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string SafeFileName(string title)
    {
        var value = title.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        value = value.TrimEnd('.', ' ');
        if (value.Length > 100) value = value[..100].TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(value)) value = "Untitled";
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "LPT1", "LPT2" };
        if (reserved.Contains(value, StringComparer.OrdinalIgnoreCase)) value = "Script_" + value;
        return value;
    }

    private static string UniquePath(string folder, string title)
    {
        Directory.CreateDirectory(folder);
        var clean = SafeFileName(title);
        var path = Path.Combine(folder, clean + ".ps1");
        var suffix = 2;
        while (File.Exists(path) || File.Exists(MetaPath(path)))
            path = Path.Combine(folder, $"{clean} ({suffix++}).ps1");
        return path;
    }

    private static string Template(string name, string category) =>
        $"# {name}\r\n# Категория: {category}\r\n# Назначение: добавьте описание\r\n\r\n" +
        "# Здесь будет ваш PowerShell-сценарий.\r\n" +
        "# Приложение никогда не выполняет код автоматически.\r\n";

    public sealed class VersionRecord
    {
        public DateTime Created { get; set; }
        public string Reason { get; set; } = "";
    }
}
