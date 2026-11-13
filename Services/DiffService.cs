namespace SC.ScriptForge.Services;

public enum DiffKind { Unchanged, Removed, Added }
public sealed record DiffLine(DiffKind Kind, string Text);

public static class DiffService
{
    public static List<DiffLine> Compare(string before, string after)
    {
        var left = before.Replace("\r\n", "\n").Split('\n');
        var right = after.Replace("\r\n", "\n").Split('\n');
        int n = left.Length, m = right.Length;
        var result = new List<DiffLine>();
        if ((long)n * m > 800_000)
        {
            // For huge scripts use a safe, bounded fallback instead of allocating a massive diff table.
            var first = 0;
            while (first < Math.Min(n, m) && left[first] == right[first]) result.Add(new(DiffKind.Unchanged, left[first++]));
            var li = n - 1;
            var ri = m - 1;
            while (li >= first && ri >= first && left[li] == right[ri]) { li--; ri--; }
            for (var i = first; i <= li; i++) result.Add(new(DiffKind.Removed, left[i]));
            for (var j = first; j <= ri; j++) result.Add(new(DiffKind.Added, right[j]));
            for (var k = li + 1; k < n; k++) result.Add(new(DiffKind.Unchanged, left[k]));
            return result;
        }

        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = left[i] == right[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        int x = 0, y = 0;
        while (x < n || y < m)
        {
            if (x < n && y < m && left[x] == right[y]) { result.Add(new(DiffKind.Unchanged, left[x++])); y++; }
            else if (x < n && (y == m || dp[x + 1, y] >= dp[x, y + 1])) result.Add(new(DiffKind.Removed, left[x++]));
            else result.Add(new(DiffKind.Added, right[y++]));
        }
        return result;
    }
}
