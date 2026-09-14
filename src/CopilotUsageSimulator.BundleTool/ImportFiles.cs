using System.Text;

namespace CopilotUsageSimulator.BundleTool;

public static class ImportFiles
{
    public const int MaximumSnapshotBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static async Task<string> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maximumBytes) throw new ImportException("input-too-large", $"Input exceeds {maximumBytes} bytes.", 2);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maximumBytes) throw new ImportException("input-too-large", $"Input exceeds {maximumBytes} bytes.", 2);
                buffer.Write(chunk, 0, read);
            }
            var bytes = buffer.ToArray().AsSpan();
            if (bytes.StartsWith(Encoding.UTF8.Preamble)) bytes = bytes[Encoding.UTF8.Preamble.Length..];
            return Utf8.GetString(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new ImportException("input-unreadable", $"Cannot read UTF-8 input '{path}': {exception.Message}", 2);
        }
    }

    public static void CheckTargets(IEnumerable<string?> inputPaths, IEnumerable<string?> outputPaths, bool overwrite)
    {
        var inputs = inputPaths.Where(path => !string.IsNullOrEmpty(path)).Select(path => CanonicalPath(path!)).ToHashSet(PathComparer);
        var outputs = new HashSet<string>(PathComparer);
        foreach (var path in outputPaths.Where(path => path is not null && path != "-"))
        {
            var fullPath = CanonicalPath(path!);
            if (inputs.Contains(fullPath) || !outputs.Add(fullPath))
                throw new ImportException("output-path-collision", "Bundle, snapshot, workload, overrides, catalog and report paths must not overwrite each other.", 2);
            if (Directory.Exists(fullPath)) throw new ImportException("output-is-directory", $"Output '{path}' is a directory.", 5);
            if (File.Exists(fullPath) && !overwrite) throw new ImportException("output-exists", $"Output '{path}' already exists. Use --overwrite to replace it.", 5);
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ImportException("output-is-link", $"Refusing to overwrite linked output '{path}'.", 5);
        }
    }

    private static string CanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)!;
        var current = root;
        foreach (var segment in Path.GetRelativePath(root, fullPath).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? throw new ImportException("path-link-unresolved", "Cannot resolve a filesystem link safely.", 2);
        }
        return current;
    }

    public static async Task WriteAtomicAsync(string path, string content, bool overwrite, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(Utf8.GetBytes(content), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite);
            temporary = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ImportException("output-failed", $"Could not write '{path}': {exception.Message}", 5);
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }
}