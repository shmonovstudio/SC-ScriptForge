using SC.ScriptForge.Infrastructure;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;

namespace SC.ScriptForge.ViewModels;

public sealed record FilterOption<T>(T Value, string Name);

/// <summary>Owns library state. Windows only handle presentation, dialogs, and OS integration.</summary>
public sealed class LibraryViewModel : ObservableObject, IDisposable
{
    private readonly ScriptRepository _repository;
    private readonly LibrarySearch _search;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _refreshCancellation;
    private bool _disposed;
    private long _searchRevision;
    private long _refreshRevision;
    private IReadOnlyList<ScriptEntry> _entries = Array.Empty<ScriptEntry>();
    private IReadOnlyList<ScriptEntry> _filtered = Array.Empty<ScriptEntry>();
    private IReadOnlyList<string> _categories = new[] { "Все категории" };
    private ScriptEntry? _selected;
    private Guid? _selectedId;
    private string _query = "";
    private string _category;
    private string _status;
    private LibrarySort _sort;
    private LibrarySection _section;
    private bool _loading;
    private bool _searching;

    public LibraryViewModel(ScriptRepository repository, LibrarySearch search, AppPreferences preferences)
    {
        _repository = repository;
        _search = search;
        _sort = Enum.IsDefined(preferences.Sort) ? preferences.Sort : LibrarySort.Recent;
        _status = StatusOptions.Any(x => x.Value == preferences.Status) ? preferences.Status : "All";
        _category = preferences.Category ?? "Все категории";
        _categories = new[] { "Все категории" }.Concat(repository.Categories).ToArray();
        if (!_categories.Contains(_category)) _category = "Все категории";
    }

    public event Action<string, Exception>? OperationFailed;
    public IReadOnlyList<ScriptEntry> Entries => _entries;
    public IReadOnlyList<ScriptEntry> FilteredEntries => _filtered;
    public IReadOnlyList<string> Categories => _categories;
    public Task PendingSearch { get; private set; } = Task.CompletedTask;
    public bool IsLoading { get => _loading; private set { if (Set(ref _loading, value)) Raise(nameof(ActivityText)); } }
    public bool IsSearching { get => _searching; private set { if (Set(ref _searching, value)) Raise(nameof(ActivityText)); } }
    public bool IsEmpty => !IsLoading && _filtered.Count == 0;
    public bool HasActiveFilters => !string.IsNullOrWhiteSpace(Query) || Category != "Все категории" || Status != "All";
    public bool HasQuery => Query.Length > 0;
    public bool CanStartLibrary => _entries.Count == 0 && !HasActiveFilters && Section != LibrarySection.Favorites;
    public bool ShowFavoriteGuidance => !HasActiveFilters && Section == LibrarySection.Favorites;
    public string ResultSummary => $"{_filtered.Count} из {_entries.Count} скриптов";
    public string ActivityText => IsLoading ? "Обновляем библиотеку…" : IsSearching ? "Ищем в библиотеке…" : ResultSummary;
    public string EmptyTitle => HasActiveFilters ? "Ничего не найдено" : Section == LibrarySection.Favorites ? "Здесь будет избранное" : "Ваша библиотека начинается здесь";
    public string EmptyDescription => HasActiveFilters ? "Попробуйте другой запрос или сбросьте фильтры."
        : Section == LibrarySection.Favorites ? "Отметьте скрипты звёздочкой — они появятся здесь." : "Создайте скрипт или импортируйте файл .ps1.";

    public ScriptEntry? SelectedEntry
    {
        get => _selected;
        set { if (Set(ref _selected, value) && value is not null) _selectedId = value.Id; }
    }
    public LibrarySection Section
    {
        get => _section;
        set { if (Set(ref _section, value)) QueueSearch(); }
    }
    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value ?? "")) return;
            if (_query.Trim().Length > 0 && Section is LibrarySection.Home or LibrarySection.Categories) Section = LibrarySection.Library;
            QueueSearch(debounce: true);
        }
    }
    public string Category { get => _category; set { if (Set(ref _category, value ?? "Все категории")) QueueSearch(); } }
    public string Status { get => _status; set { if (Set(ref _status, value ?? "All")) QueueSearch(); } }
    public LibrarySort Sort { get => _sort; set { if (Set(ref _sort, value)) QueueSearch(); } }

    public IReadOnlyList<FilterOption<string>> StatusOptions { get; } = new[]
    {
        new FilterOption<string>("All", "Все статусы"), new("Draft", "Черновики"), new("Ready", "Готовые"), new("Archived", "Архив")
    };
    public IReadOnlyList<FilterOption<LibrarySort>> SortOptions { get; } = new[]
    {
        new FilterOption<LibrarySort>(LibrarySort.Recent, "Сначала новые"), new(LibrarySort.Name, "По названию"),
        new(LibrarySort.Category, "По категории"), new(LibrarySort.Favorites, "Избранное сверху")
    };

    public async Task RefreshAsync(Guid? preferredId = null)
    {
        if (_disposed) return;
        _refreshCancellation?.Cancel();
        _refreshCancellation?.Dispose();
        var cancellation = _refreshCancellation = new CancellationTokenSource();
        var revision = ++_refreshRevision;
        IsLoading = true;
        Raise(nameof(IsEmpty));
        try
        {
            var entries = await Task.Run(() => _repository.LoadAll(), cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || revision != _refreshRevision) return;
            _entries = entries;
            _search.Prune(entries);
            _categories = new[] { "Все категории" }.Concat(_repository.Categories).ToArray();
            if (!_categories.Contains(Category)) _category = "Все категории";
            Raise(nameof(Entries)); Raise(nameof(Categories)); Raise(nameof(Category));
            PendingSearch = RunSearchAsync(preferredId, debounce: false);
            await PendingSearch;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { if (!_disposed && revision == _refreshRevision) OperationFailed?.Invoke("Не удалось обновить библиотеку", ex); }
        finally
        {
            if (!_disposed && revision == _refreshRevision) { IsLoading = false; Raise(nameof(IsEmpty)); }
        }
    }

    public void ResetFilters()
    {
        _query = ""; _category = "Все категории"; _status = "All";
        Raise(nameof(Query)); Raise(nameof(Category)); Raise(nameof(Status)); QueueSearch();
    }

    public void Select(Guid id)
    {
        _selectedId = id;
        SelectedEntry = _filtered.FirstOrDefault(x => x.Id == id) ?? _filtered.FirstOrDefault();
    }

    private void QueueSearch(bool debounce = false)
    {
        if (_disposed) return;
        Raise(nameof(HasActiveFilters)); Raise(nameof(HasQuery)); Raise(nameof(EmptyTitle)); Raise(nameof(EmptyDescription));
        Raise(nameof(CanStartLibrary)); Raise(nameof(ShowFavoriteGuidance));
        PendingSearch = RunSearchAsync(debounce: debounce);
    }

    private async Task RunSearchAsync(Guid? preferredId = null, bool debounce = false)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = _searchCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var revision = ++_searchRevision;
        var filter = new LibraryFilter(Query.Trim(), Category == "Все категории" ? null : Category, Status == "All" ? null : Status, Section == LibrarySection.Favorites, Sort);
        IsSearching = true;
        try
        {
            if (debounce) await Task.Delay(180, token);
            var result = await _search.FindAsync(_entries, filter, token);
            if (_disposed || token.IsCancellationRequested || revision != _searchRevision) return;
            _filtered = result;
            Raise(nameof(FilteredEntries)); Raise(nameof(ResultSummary)); Raise(nameof(ActivityText)); Raise(nameof(IsEmpty));
            var id = preferredId ?? _selectedId;
            SelectedEntry = result.FirstOrDefault(x => x.Id == id) ?? result.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { if (!_disposed && revision == _searchRevision) OperationFailed?.Invoke("Не удалось выполнить поиск", ex); }
        finally { if (!_disposed && revision == _searchRevision) IsSearching = false; }
    }

    public void Dispose()
    {
        _disposed = true;
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose();
        _refreshCancellation?.Cancel(); _refreshCancellation?.Dispose();
    }
}
