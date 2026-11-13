using SC.ScriptForge.Models;

namespace SC.ScriptForge.Services;

public enum LibrarySort { Recent, Name, Category, Favorites }
public enum LibrarySection { Home, Library, Categories, Favorites }
public sealed record LibraryFilter(string Query, string? Category, string? Status, bool FavoritesOnly, LibrarySort Sort);

/// <summary>Cancelable AND search, relevance ranking, and a bounded, timestamp-aware content cache.</summary>
public sealed class LibrarySearch
{
    private sealed record CachedText(long Ticks, long Length, string Text);
    private readonly Dictionary<string, CachedText> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheGate = new();
    private int _cachedCharacters;
    private const int MaximumFileBytes = 2_000_000;
    private const int MaximumCachedCharacters = 8_000_000;

    public Task<IReadOnlyList<ScriptEntry>> FindAsync(IReadOnlyList<ScriptEntry> entries, LibraryFilter filter, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<ScriptEntry>>(() => Find(entries, filter, cancellationToken), cancellationToken);

    private IReadOnlyList<ScriptEntry> Find(IReadOnlyList<ScriptEntry> entries, LibraryFilter filter, CancellationToken cancellationToken)
    {
        var tokens = filter.Query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.TrimStart('#')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matches = new List<(ScriptEntry Entry, int Score)>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (filter.FavoritesOnly && !entry.Favorite ||
                filter.Category is not null && entry.Category != filter.Category ||
                filter.Status is not null && entry.Status != filter.Status) continue;
            var score = 0;
            var matched = true;
            string? code = null;
            foreach (var token in tokens)
            {
                var rank = entry.DisplayName.Equals(token, StringComparison.OrdinalIgnoreCase) ? 180
                    : entry.DisplayName.StartsWith(token, StringComparison.OrdinalIgnoreCase) ? 120
                    : Has(entry.DisplayName, token) ? 90
                    : entry.Tags.Any(tag => Has(tag, token)) ? 65
                    : Has(entry.DisplayDescription, token) ? 35
                    : Has(entry.Category, token) ? 25
                    : Has(entry.DisplayNotes, token) ? 15
                    : entry.Translations.Values.Any(t => Has(t.Name, token) || Has(t.Description, token)) ? 12 : 0;
                if (rank == 0)
                {
                    code ??= ReadSearchableText(entry.FilePath);
                    if (Has(code, token)) rank = 5;
                }
                if (rank == 0) { matched = false; break; }
                score += rank;
            }
            if (matched) matches.Add((entry, score));
        }
        var ranked = matches.OrderByDescending(x => x.Score);
        var ordered = filter.Sort switch
        {
            LibrarySort.Name => ranked.ThenBy(x => x.Entry.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.Category => ranked.ThenBy(x => x.Entry.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Entry.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.Favorites => ranked.ThenByDescending(x => x.Entry.Favorite).ThenByDescending(x => x.Entry.LastModified),
            _ => ranked.ThenByDescending(x => x.Entry.LastModified)
        };
        return ordered.ThenBy(x => x.Entry.Id).Select(x => x.Entry).ToArray();
    }

    private static bool Has(string text, string token) => text.Contains(token, StringComparison.OrdinalIgnoreCase);

    private string ReadSearchableText(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumFileBytes) return "";
            var ticks = info.LastWriteTimeUtc.Ticks;
            var length = info.Length;
            lock (_cacheGate)
                if (_cache.TryGetValue(path, out var cached) && cached.Ticks == ticks && cached.Length == length) return cached.Text;
            var text = File.ReadAllText(path);
            lock (_cacheGate)
            {
                if (_cache.Remove(path, out var previous)) _cachedCharacters -= previous.Text.Length;
                while (_cachedCharacters + text.Length > MaximumCachedCharacters && _cache.Count > 0)
                {
                    var oldest = _cache.First();
                    _cachedCharacters -= oldest.Value.Text.Length;
                    _cache.Remove(oldest.Key);
                }
                _cache[path] = new CachedText(ticks, length, text);
                _cachedCharacters += text.Length;
            }
            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    public void Prune(IEnumerable<ScriptEntry> entries)
    {
        var paths = entries.Select(x => x.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_cacheGate)
            foreach (var path in _cache.Keys.Where(x => !paths.Contains(x)).ToArray())
            {
                _cachedCharacters -= _cache[path].Text.Length;
                _cache.Remove(path);
            }
    }
}
