using System.Globalization;
using System.Text.RegularExpressions;
using SC.ScriptForge.Infrastructure;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;

namespace SC.ScriptForge.ViewModels;

public sealed class ScriptParameterViewModel : ObservableObject
{
    public ScriptParameterDefinition Definition { get; }
    private readonly string _language;
    private string L(string ru, string en) => LibraryLocalization.Text(ru, en, _language);
    private string _value;
    private bool _boolean;
    private bool _useDefault;
    private bool _required;
    private string _error = "";
    private ParameterRole _role;
    public ScriptParameterViewModel(ScriptParameterDefinition definition, string? language = null)
    {
        Definition = definition;
        _language = LibraryLocalization.Normalize(language ?? LibraryLocalization.Language);
        _value = definition.LiteralDefault ?? "";
        _boolean = string.Equals(_value, "true", StringComparison.OrdinalIgnoreCase);
        _role = GuessRole(definition);
        _useDefault = definition.HasDefault;
    }
    public string Name => Definition.Name;
    public string Label => Name.ToLowerInvariant() switch
    {
        "inputfile" or "inputpath" => L("Входной файл", "Input file"), "excelpath" or "excelfile" => L("Excel-файл", "Excel file"),
        "csvpath" or "csvfile" => L("CSV-файл", "CSV file"), "outputfolder" or "outputdirectory" => L("Папка результата", "Output folder"),
        "outputfile" or "outputpath" => L("Файл результата", "Output file"), "sheetname" or "worksheetname" => L("Лист Excel", "Excel worksheet"),
        "format" => L("Формат результата", "Output format"), "delimiter" => L("Разделитель", "Delimiter"), "onlynonemptyrows" => L("Только непустые строки", "Non-empty rows only"), "maxrows" => L("Максимум строк", "Maximum rows"),
        "computername" => L("Компьютер или сервер", "Computer or server"),
        "searchbase" => L("Область поиска (OU)", "Search base (OU)"),
        "server" => L("Контроллер домена", "Domain controller"),
        "days" => L("Число дней", "Number of days"),
        "requirepatronymic" => L("Требовать три части ФИО", "Require three name parts"),
        "attributes" => L("Проверяемые поля AD", "AD attributes to check"),
        "minimumfreepercent" => L("Минимум свободного места (%)", "Minimum free space (%)"),
        "ports" => L("TCP-порты", "TCP ports"),
        "timeoutms" => L("Время ожидания (мс)", "Timeout (ms)"),
        "dnsname" => L("DNS-имя", "DNS name"),
        "recordtype" => L("Тип DNS-записи", "DNS record type"),
        "dnsserver" => L("DNS-сервер", "DNS server"),
        "folderpath" => L("Папка для проверки", "Folder to inspect"),
        "top" => L("Максимум файлов", "Maximum files"),
        "minimumsizemb" => L("Минимальный размер (МиБ)", "Minimum size (MiB)"),
        "hours" => L("Период проверки (часы)", "Lookback period (hours)"),
        "maxevents" => L("Максимум событий", "Maximum events"),
        "storepath" => L("Хранилище сертификатов", "Certificate store"),
        "target" => L("Сервер, сайт, домен или лес", "Server, site, domain or forest"),
        "scope" => L("Область репликации", "Replication scope"),
        "logname" => L("Журнал Windows", "Windows log"),
        "language" => L("Язык отчёта (ru / en)", "Report language (ru / en)"),
        _ => Regex.Replace(Name, "(?<=[a-zа-я])(?=[A-ZА-Я])", " ")
    };
    public string Caption => "-" + Name + " · " + Definition.TypeName.Replace("System.", "").Replace("Management.Automation.", "");
    public string Description
    {
        get
        {
            var match = Regex.Match(Definition.Description, _language == "en" ? @"(?ms)^EN: (.*?)(?=^RU:|\z)" : @"(?ms)^RU: (.*?)(?=^EN:|\z)");
            return match.Success ? match.Groups[1].Value.Trim() : Definition.Description;
        }
    }
    public string DefaultCaption => IsSecret || Definition.LiteralDefault is null ? L("Использовать готовую настройку", "Use script default") : L("По умолчанию: ", "Default: ") + (IsBooleanEditor ? (string.Equals(Definition.LiteralDefault, "true", StringComparison.OrdinalIgnoreCase) ? L("включено", "enabled") : L("выключено", "disabled")) : Definition.LiteralDefault);
    public string DefaultActionText => UseDefault ? L("Изменить", "Customize") : L("Сбросить", "Reset");
    public string DefaultSummary
    {
        get
        {
            if (IsSecret) return L("Значение из скрипта", "Value from script");
            if (Name.Equals("ComputerName", StringComparison.OrdinalIgnoreCase)
                && Definition.DefaultDisplay.Trim().Equals("$env:COMPUTERNAME", StringComparison.OrdinalIgnoreCase))
                return Environment.MachineName + L(" · этот компьютер", " · this computer");
            var value = Definition.LiteralDefault;
            if (Name.Equals("Language", StringComparison.OrdinalIgnoreCase)
                && Choices.Contains("ru") && Choices.Contains("en")
                && (value is "ru" or "en" || Definition.DefaultDisplay.Contains("SC_SCRIPTFORGE_LANGUAGE", StringComparison.OrdinalIgnoreCase)))
                return (value ?? _language) == "en" ? "English" : "Русский";
            if (value is null) return L("Значение из скрипта", "Value from script");
            if (IsBooleanEditor) return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? L("Включено", "Enabled") : L("Выключено", "Disabled");
            return value.Length == 0 ? L("Не задано", "Not specified") : value;
        }
    }
    public void ToggleDefault()
    {
        // Only seed defaults whose values are already known; never evaluate script expressions.
        if (UseDefault && Value.Length == 0 && !IsSecret)
        {
            if (Definition.LiteralDefault is { Length: > 0 } literal) Value = literal;
            else if (Name.Equals("ComputerName", StringComparison.OrdinalIgnoreCase)
                && Definition.DefaultDisplay.Trim().Equals("$env:COMPUTERNAME", StringComparison.OrdinalIgnoreCase)) Value = Environment.MachineName;
            else if (Name.Equals("Language", StringComparison.OrdinalIgnoreCase) && Choices.Contains("ru") && Choices.Contains("en")
                && Definition.DefaultDisplay.Contains("SC_SCRIPTFORGE_LANGUAGE", StringComparison.OrdinalIgnoreCase)) Value = _language;
        }
        UseDefault = !UseDefault;
    }
    public bool IsPrimary => IsRequired || Name.Equals("Format", StringComparison.OrdinalIgnoreCase) || Name.Equals("OutputFormat", StringComparison.OrdinalIgnoreCase);
    public string DefaultTooltip => IsSecret ? L("Значение берётся из исходного скрипта", "The value comes from the script") : Definition.DefaultDisplay;
    public IReadOnlyList<string> Choices => Definition.Choices;
    public bool IsSecret => Regex.IsMatch(Name, "password|secret|token|credential", RegexOptions.IgnoreCase) || Role == ParameterRole.Secret
        || Definition.TypeName.Contains("SecureString", StringComparison.OrdinalIgnoreCase) || Definition.TypeName.Contains("PSCredential", StringComparison.OrdinalIgnoreCase);
    public bool CanChooseRole => !Name.Equals("StorePath", StringComparison.OrdinalIgnoreCase) && !IsSecret && Definition.Choices.Length == 0 && Definition.TypeName is "System.String" or "System.Object" or "System.IO.FileInfo" or "System.IO.DirectoryInfo";
    public bool IsRequired { get => _required; private set => Set(ref _required, value); }
    public bool HasOptionalDefault => Definition.HasDefault && !IsRequired;
    public bool IsEditable => !UseDefault;
    public bool IsTextEditor => Role is not (ParameterRole.Boolean or ParameterRole.Choice or ParameterRole.Secret);
    public bool IsBooleanEditor => Role == ParameterRole.Boolean;
    public bool IsChoiceEditor => Role == ParameterRole.Choice;
    public bool IsSecretEditor => Role == ParameterRole.Secret;
    public bool IsMultiline => Role == ParameterRole.Lines;
    public bool CanBrowse => Role is ParameterRole.InputFile or ParameterRole.OutputFile or ParameterRole.InputDirectory or ParameterRole.OutputDirectory;
    public string BrowseIcon => Role is ParameterRole.OutputFile ? "Save" : "Folder";
    public string Value { get => _value; set { if (Set(ref _value, value ?? "")) ValidationError = ""; } }
    public bool BooleanValue { get => _boolean; set => Set(ref _boolean, value); }
    public bool UseDefault { get => _useDefault; set { if (Set(ref _useDefault, value)) { Raise(nameof(IsEditable)); Raise(nameof(DefaultActionText)); ValidationError = ""; } } }
    public string ValidationError { get => _error; private set { if (Set(ref _error, value)) Raise(nameof(HasError)); } }
    public bool HasError => ValidationError.Length > 0;
    public ParameterRole Role
    {
        get => _role;
        set
        {
            if (!Set(ref _role, value)) return;
            foreach (var property in new[] { nameof(IsTextEditor), nameof(IsBooleanEditor), nameof(IsChoiceEditor), nameof(IsSecretEditor), nameof(IsMultiline), nameof(CanBrowse), nameof(BrowseIcon), nameof(IsSecret), nameof(CanChooseRole), nameof(DefaultCaption), nameof(DefaultTooltip), nameof(DefaultSummary) }) Raise(property);
        }
    }
    public IReadOnlyList<FilterOption<ParameterRole>> PathRoles => new[]
    {
        new FilterOption<ParameterRole>(ParameterRole.Text, L("Текст", "Text")), new(ParameterRole.InputFile, L("Входной файл", "Input file")),
        new(ParameterRole.InputDirectory, L("Входная папка", "Input folder")), new(ParameterRole.OutputFile, L("Файл результата", "Output file")), new(ParameterRole.OutputDirectory, L("Папка результата", "Output folder"))
    };

    public void SetParameterSet(string parameterSet)
    {
        IsRequired = Definition.RequiredIn.Contains("*") || Definition.RequiredIn.Contains(parameterSet);
        if (IsRequired) UseDefault = false;
        Raise(nameof(HasOptionalDefault));
    }
    public static ParameterRole GuessRole(ScriptParameterDefinition definition)
    {
        var name = definition.Name.ToLowerInvariant();
        var type = definition.TypeName;
        if (name == "storepath") return ParameterRole.Text;
        if (type.Contains("SecureString", StringComparison.OrdinalIgnoreCase) || type.Contains("PSCredential", StringComparison.OrdinalIgnoreCase)) return ParameterRole.Secret;
        if (Regex.IsMatch(name, "password|secret|token|credential", RegexOptions.IgnoreCase)) return ParameterRole.Secret;
        if (type is "System.Boolean" or "System.Management.Automation.SwitchParameter") return ParameterRole.Boolean;
        if (definition.Choices.Length > 0) return ParameterRole.Choice;
        if (type.EndsWith("[]", StringComparison.Ordinal)) return ParameterRole.Lines;
        if (type is "System.Int16" or "System.Int32" or "System.Int64" or "System.UInt16" or "System.UInt32" or "System.UInt64" or "System.Double" or "System.Single" or "System.Decimal" or "System.Byte") return ParameterRole.Number;
        var output = Regex.IsMatch(name, "^(out|output|export|destination|report|result|target)");
        var directory = type == "System.IO.DirectoryInfo" || Regex.IsMatch(name, "folder|directory|dir$");
        var file = type == "System.IO.FileInfo" || Regex.IsMatch(name, "file|path|excel|csv");
        if (output) return directory ? ParameterRole.OutputDirectory : ParameterRole.OutputFile;
        if (directory) return ParameterRole.InputDirectory;
        if (file) return ParameterRole.InputFile;
        return ParameterRole.Text;
    }

    public bool TryBind(string workingDirectory, out BoundScriptParameter? parameter, out string? outputDirectory)
    {
        parameter = null; outputDirectory = null; ValidationError = "";
        if (UseDefault && !IsRequired) return true;
        if (Definition.TypeName.Contains("SecureString", StringComparison.OrdinalIgnoreCase) || Definition.TypeName.Contains("PSCredential", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsRequired && Value.Length == 0) return true;
            ValidationError = L("Этот тип требует отдельного интерактивного сценария авторизации.", "This type requires an interactive authentication workflow."); return false;
        }
        object? value = Value;
        if (Role == ParameterRole.Boolean) value = BooleanValue;
        else
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                if (!IsRequired && !Definition.HasDefault) return true;
                if (IsRequired) { ValidationError = L("Заполните обязательный параметр.", "Fill in this required parameter."); return false; }
            }
            if (Role == ParameterRole.Choice && !Choices.Contains(Value)) { ValidationError = L("Выберите значение из списка.", "Select a value from the list."); return false; }
            if (Role == ParameterRole.Number)
            {
                try
                {
                    var type = Type.GetType(Definition.TypeName, throwOnError: false) ?? typeof(double);
                    value = Convert.ChangeType(Value.Replace(',', '.'), type, CultureInfo.InvariantCulture);
                    if (value is double d && !double.IsFinite(d) || value is float f && !float.IsFinite(f)) throw new FormatException();
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
                { ValidationError = L("Введите число подходящего типа и диапазона.", "Enter a number of the required type and range."); return false; }
            }
            else if (Role == ParameterRole.Lines)
            {
                if (Definition.TypeName != "System.String[]") { ValidationError = L("Редактор списков поддерживает параметры string[].", "The list editor supports string[] parameters."); return false; }
                value = Value.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            else if (CanBrowse)
            {
                try
                {
                    var path = Path.GetFullPath(Value.Trim(), workingDirectory);
                    if (Role == ParameterRole.InputFile && !File.Exists(path)) { ValidationError = L("Входной файл не найден.", "Input file not found."); return false; }
                    if (Role == ParameterRole.InputDirectory && !Directory.Exists(path)) { ValidationError = L("Входная папка не найдена.", "Input folder not found."); return false; }
                    if (Role == ParameterRole.OutputFile && Directory.Exists(path)) { ValidationError = L("Укажите файл, а не папку.", "Specify a file path."); return false; }
                    outputDirectory = Role == ParameterRole.OutputDirectory ? path : Role == ParameterRole.OutputFile ? Path.GetDirectoryName(path) : null;
                    value = path;
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                { ValidationError = L("Укажите корректный путь.", "Specify a valid path."); return false; }
            }
        }
        parameter = new BoundScriptParameter(Name, Definition.TypeName, value);
        return true;
    }

    public ParameterPreset Capture() => new() { TypeName = Definition.TypeName, Value = Value, BooleanValue = BooleanValue, UseDefault = UseDefault, Role = Role };
    public void Restore(ParameterPreset preset)
    {
        if (preset.TypeName != Definition.TypeName || IsSecret) return;
        Value = preset.Value; BooleanValue = preset.BooleanValue;
        UseDefault = HasOptionalDefault && preset.UseDefault;
        if (CanChooseRole && PathRoles.Any(x => x.Value == preset.Role)) Role = preset.Role;
    }
}
