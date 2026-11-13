using System.Data;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using SC.ScriptForge.Presentation;
using SC.ScriptForge.Services;

namespace SC.ScriptForge;

/// <summary>Short results fit a table; wide records are readable, selectable vertical cards.</summary>
public sealed class ResultDataView : Border
{
    private readonly ResultTable _table;
    private readonly string[] _columns;
    private readonly string[] _fieldNames;
    private bool? _cards;
    public bool UsesCards => _cards == true;
    private string L(string ru, string en) => LibraryLocalization.Text(ru, en, _table.Language);

    public ResultDataView(ResultTable table)
    {
        _table = table;
        _columns = table.Rows.Table!.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();
        _fieldNames = table.Signature.Split('\u001f');
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Name = "ResultData";
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Four short columns can fit on a wide surface. On a narrow surface the same data
        // switches to cards instead of shrinking or hiding values beyond the right edge.
        var cards = _columns.Length > 4 || (_columns.Length > 1
            && double.IsFinite(availableSize.Width) && availableSize.Width < _columns.Length * 145 + 32);
        if (_cards != cards)
        {
            _cards = cards;
            var header = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10 };
            header.Children.Add(Feedback.Icon(cards ? "list" : "grid", Tone.Info, 16));
            var title = Ui.Text(_table.Title, "section", 14); title.FontWeight = FontWeight.SemiBold;
            header.Children.Add(title.Place(0, 1));
            header.Children.Add(Feedback.Chip(_table.CountLabel, Tone.Neutral).Place(0, 2));
            var body = Ui.Stack(header, cards ? Cards() : Table()); body.Spacing = 14;
            Child = Feedback.Panel(body);
        }
        return base.MeasureOverride(availableSize);
    }

    private Control Cards()
    {
        var records = Ui.Stack(); records.Name = "ResultRecordCards"; records.Spacing = 10;
        for (var index = 0; index < _table.Count; index++)
        {
            var row = _table.Rows[index];
            if (_table.Count == 1) { records.Children.Add(Fields(row)); continue; }
            var recordNumber = index + 1;
            var identity = RecordIdentity(row);
            var title = Ui.Text(identity, "section", 13);
            var number = Feedback.Chip(recordNumber.ToString("00"), Tone.Info);
            var heading = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
            heading.Children.Add(number); heading.Children.Add(title.Place(0, 1));
            var expander = new Expander
            {
                Name = "ResultRecord", Header = heading, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, IsExpanded = index == 0
            };
            AutomationProperties.SetName(expander, L("Запись ", "Record ") + recordNumber + ": " + identity);
            void Populate() { if (expander.IsExpanded && expander.Content is null) expander.Content = Fields(row).Also(c => c.Margin = new(10, 2, 10, 10)); }
            expander.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) Populate(); };
            Populate();
            records.Children.Add(expander);
        }
        return records;
    }

    private string RecordIdentity(DataRowView row)
    {
        foreach (var key in new[] { "DisplayName", "ComputerName", "Name", "SamAccountName", "FullName", "DeviceId", "Subject", "DnsName", "UserName" })
        {
            var column = Array.FindIndex(_fieldNames, name => name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (column >= 0 && Convert.ToString(row[column]) is { Length: > 0 } value && value != "—") return value;
        }
        return L("Сведения о записи", "Record details");
    }

    private Control Fields(DataRowView row)
    {
        var fields = Ui.Stack(); fields.Spacing = 0;
        for (var column = 0; column < _columns.Length; column++)
        {
            var label = Ui.Text(_columns[column], "muted", 11);
            var value = Value(Convert.ToString(row[column]) ?? "", 13.5);
            value.Name = "ResultFieldValue"; value.FontWeight = FontWeight.Medium;
            AutomationProperties.SetName(value, _columns[column]);
            var copy = Ui.Stack(label, value); copy.Spacing = 5;
            var field = new Border { Child = copy, Padding = new(2, 10, 2, 12), BorderThickness = new(0, 0, 0, column < _columns.Length - 1 ? 1 : 0) };
            Ui.Resource(field, BorderBrushProperty, "ScLine");
            fields.Children.Add(field);
        }
        return fields;
    }

    private Control Table()
    {
        var sample = _table.Rows.Cast<DataRowView>().Take(60).ToArray();
        var definitions = new ColumnDefinitions();
        for (var column = 0; column < _columns.Length; column++)
        {
            var length = Math.Max(_columns[column].Length, sample.Select(r => Convert.ToString(r[column])?.Length ?? 0).DefaultIfEmpty(0).Max());
            definitions.Add(new ColumnDefinition(new GridLength(Math.Clamp(length, 12, 38), GridUnitType.Star)));
        }
        Grid Row(IEnumerable<string> values, bool header)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions() };
            foreach (var column in definitions) row.ColumnDefinitions.Add(new ColumnDefinition(column.Width));
            var index = 0;
            foreach (var value in values)
            {
                var cell = Value(value, header ? 11 : 12.5); cell.Margin = new(10, 9);
                if (header) { cell.FontWeight = FontWeight.SemiBold; Ui.Resource(cell, TextBlock.ForegroundProperty, "ScMuted"); }
                row.Children.Add(cell.Place(0, index++));
            }
            return row;
        }
        var header = new Border { Child = Row(_columns, true), CornerRadius = new(9) };
        Ui.Resource(header, BackgroundProperty, "ScQuiet");
        var rows = new ItemsControl
        {
            ItemsSource = _table.Rows,
            ItemTemplate = new FuncDataTemplate<DataRowView>((r, _) =>
            {
                var line = new Border
                {
                    Child = Row(r?.Row.ItemArray.Select(v => Convert.ToString(v) ?? "") ?? [], false),
                    BorderThickness = new(0, 0, 0, 1)
                };
                Ui.Resource(line, BorderBrushProperty, "ScLine"); return line;
            })
        };
        var list = new ScrollViewer
        {
            Content = rows, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden
        };
        var table = Ui.Stack(header, list); table.Name = "ResultCompactTable"; table.Spacing = 2;
        return table;
    }

    private static SelectableTextBlock Value(string value, double size)
    {
        var text = new SelectableTextBlock
        {
            Text = string.IsNullOrEmpty(value) ? "—" : value, TextWrapping = TextWrapping.Wrap,
            FontSize = size, VerticalAlignment = VerticalAlignment.Top, LineHeight = size * 1.5
        };
        Ui.Resource(text, TextBlock.ForegroundProperty, string.IsNullOrEmpty(value) || value == "—" ? "ScMuted" : "ScText");
        return text;
    }
}
