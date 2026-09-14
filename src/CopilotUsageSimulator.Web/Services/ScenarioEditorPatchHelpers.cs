namespace CopilotUsageSimulator.Web.Services;

internal static class ScenarioEditorPatchHelpers
{
    public static IReadOnlyList<T> PatchById<T>(
        IReadOnlyList<T> values,
        string? selectedId,
        bool enabled,
        string defaultId,
        Func<T, string> idSelector,
        Func<T, T> update,
        Func<string, T> create)
    {
        var patched = values.ToList();
        var index = string.IsNullOrWhiteSpace(selectedId)
            ? -1
            : patched.FindIndex(value =>
                string.Equals(idSelector(value), selectedId, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            if (enabled)
            {
                patched[index] = update(patched[index]);
            }
            else
            {
                patched.RemoveAt(index);
            }
        }
        else if (enabled)
        {
            patched.Add(create(defaultId));
        }

        return patched;
    }

    public static IReadOnlyList<T> PatchFirst<T>(
        IReadOnlyList<T> values,
        Func<T, T> update,
        Func<T> create)
    {
        var patched = values.ToList();
        if (patched.Count > 0)
        {
            patched[0] = update(patched[0]);
        }
        else
        {
            patched.Add(create());
        }

        return patched;
    }

    public static IReadOnlyList<T> PatchAt<T>(
        IReadOnlyList<T> values,
        int? selectedIndex,
        bool enabled,
        Func<T, T> update,
        Func<T> create)
    {
        var patched = values.ToList();
        if (selectedIndex is >= 0 && selectedIndex < patched.Count)
        {
            if (enabled)
            {
                patched[selectedIndex.Value] = update(patched[selectedIndex.Value]);
            }
            else
            {
                patched.RemoveAt(selectedIndex.Value);
            }
        }
        else if (enabled)
        {
            patched.Add(create());
        }

        return patched;
    }

    public static string? ResolvePatchedId<T>(
        IReadOnlyList<T> values,
        string? selectedId,
        bool enabled,
        string defaultId,
        Func<T, string> idSelector)
    {
        if (!enabled)
        {
            return null;
        }

        return values
            .Select(idSelector)
            .FirstOrDefault(id => string.Equals(id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? values
                .Select(idSelector)
                .FirstOrDefault(id => string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase));
    }

    public static int? ResolvePatchedIndex(int count, int? selectedIndex, bool enabled)
    {
        if (!enabled || count == 0)
        {
            return null;
        }

        return selectedIndex is >= 0 && selectedIndex < count
            ? selectedIndex
            : count - 1;
    }

    public static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
