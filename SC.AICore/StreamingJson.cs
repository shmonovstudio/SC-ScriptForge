using System.Globalization;
using System.Text;

namespace ShmonovStudio.AI;

public static class StreamingJson
{
    // Extract complete string characters from an unfinished JSON response without displaying JSON syntax.
    public static string StringProperty(string json, string property)
    {
        var key = "\"" + property + "\""; var position = json.IndexOf(key, StringComparison.Ordinal); if (position < 0) return "";
        position += key.Length; while (position < json.Length && char.IsWhiteSpace(json[position])) position++;
        if (position >= json.Length || json[position++] != ':') return "";
        while (position < json.Length && char.IsWhiteSpace(json[position])) position++;
        if (position >= json.Length || json[position++] != '"') return "";
        var result = new StringBuilder();
        for (; position < json.Length; position++)
        {
            var value = json[position]; if (value == '"') break;
            if (value != '\\') { result.Append(value); continue; }
            if (++position >= json.Length) break;
            var escaped = json[position];
            if (escaped == 'u')
            {
                if (position + 4 >= json.Length) break;
                if (!ushort.TryParse(json.AsSpan(position + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) break;
                result.Append((char)code); position += 4;
            }
            else result.Append(escaped switch { 'n' => '\n', 'r' => '\r', 't' => '\t', 'b' => '\b', 'f' => '\f', _ => escaped });
        }
        return result.ToString();
    }
}
