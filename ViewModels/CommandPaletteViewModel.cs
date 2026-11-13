using SC.ScriptForge.Infrastructure;

namespace SC.ScriptForge.ViewModels;

public sealed record PaletteAction(string Title, string Description, string Icon, Action Execute, string Shortcut = "");

public sealed class CommandPaletteViewModel(IReadOnlyList<PaletteAction> actions, Action<string> searchLibrary) : ObservableObject
{
    private string _query = "";
    public string Query { get => _query; set { if (Set(ref _query, value ?? "")) Raise(nameof(Results)); } }
    public IReadOnlyList<PaletteAction> Results
    {
        get
        {
            var tokens = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = actions.Where(action => tokens.All(token => (action.Title + " " + action.Description).Contains(token, StringComparison.CurrentCultureIgnoreCase))).Take(10).ToList();
            var query = Query.Trim();
            if (query.Length > 0) result.Add(new PaletteAction($"Искать «{query}» в библиотеке", "По названию, тегам и содержимому скриптов", "Search", () => searchLibrary(query), "Enter"));
            return result;
        }
    }
}
