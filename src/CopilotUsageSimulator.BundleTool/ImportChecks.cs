namespace CopilotUsageSimulator.BundleTool;

internal static class ImportChecks
{
    public static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new ImportException(code, message);
    }

    public static string Identifier(string? value, string path)
    {
        Require(!string.IsNullOrWhiteSpace(value) && value == value.Trim(),
            "invalid-identifier", $"{path} must be a nonblank identifier without surrounding whitespace.");
        return value!;
    }

    public static decimal Amount(decimal? value, string path)
    {
        Require(value is >= 0m, "missing-financial-value",
            $"{path} requires an explicit non-negative value; missing data is not zero.");
        return value!.Value;
    }

    public static void Items<T>(IReadOnlyList<T>? items, string path)
    {
        Require(items is not null && items.All(item => item is not null),
            "invalid-collection", $"{path} must be an array without null items.");
    }

    public static void Unique(IEnumerable<string> values, string path)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            Identifier(value, path);
            Require(seen.Add(value), "duplicate-identifier", $"Duplicate identifier '{value}' in {path}.");
        }
    }

    public static void KnownOverrides<T>(IReadOnlyDictionary<string, T> overrides, IEnumerable<string> known, string path)
    {
        Require(overrides is not null, "invalid-overrides", $"{path} must not be null.");
        Unique(overrides!.Keys, path);
        var identifiers = known.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in overrides)
        {
            Require(pair.Value is not null && identifiers.Contains(pair.Key),
                "unknown-override-target", $"{path}.{pair.Key} does not identify an observed resource.");
        }
    }

    public static T? Find<T>(IReadOnlyDictionary<string, T> values, string id) where T : class =>
        values.FirstOrDefault(pair => Same(pair.Key, id)).Value;

    public static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}