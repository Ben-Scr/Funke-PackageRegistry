
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Funke.Serialization;
public static class Json
{
    public static readonly JsonSerializerOptions DefaultJson = new JsonSerializerOptions
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    public static readonly JsonSerializerOptions FormattedJson = new JsonSerializerOptions
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal static void PrepareWritePath(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new System.ArgumentException("path can't be null or empty", nameof(path));

        // Full path so a bare file name doesn't yield "" (CreateDirectory("") throws).
        string? dirPath = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dirPath))
            Directory.CreateDirectory(dirPath);
    }

    public static void Save<T>(string path, T obj, JsonSerializerOptions? options = null)
    {
        PrepareWritePath(path);

        using FileStream fs = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 1 << 20, FileOptions.SequentialScan);

        options ??= DefaultJson;

        using (fs)
        {
            JsonSerializer.Serialize(fs, obj, options);
        }
    }
    public static void SaveCompressed<T>(string path, T obj, CompressionLevel compressionLevel = CompressionLevel.Fastest, JsonSerializerOptions? options = null)
    {
        PrepareWritePath(path);

        using var fs = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 20,
            options: FileOptions.SequentialScan);

        options ??= DefaultJson;

        using var gzip = new GZipStream(fs, compressionLevel, leaveOpen: false);
        JsonSerializer.Serialize(gzip, obj, options);
    }

    public static T Load<T>(string path, T defaultValue = default!, JsonSerializerOptions? options = null)
    {
        if (!File.Exists(path)) return defaultValue;

        using FileStream fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, FileOptions.SequentialScan);

        options ??= DefaultJson;

        using (fs)
        {
            return JsonSerializer.Deserialize<T>(fs, options) ?? defaultValue;
        }
    }

    public static T LoadCompressed<T>(string path, T defaultValue = default!, JsonSerializerOptions? options = null)
    {
        if (!File.Exists(path)) return defaultValue;

        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1 << 20,
            options: FileOptions.SequentialScan);

        options ??= DefaultJson;

        using var gzip = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: false);
        return JsonSerializer.Deserialize<T>(gzip, options) ?? defaultValue;
    }

    public static string Serialize<T>(T obj, JsonSerializerOptions? options = null)
    {
        options ??= DefaultJson;
        return JsonSerializer.Serialize(obj, options);
    }

    public static byte[] SerializeCompressed<T>(
        T obj,
        CompressionLevel compressionLevel = CompressionLevel.Fastest,
        JsonSerializerOptions? options = null)
    {
        options ??= DefaultJson;

        using var memoryStream = new MemoryStream();

        using (var gzip = new GZipStream(memoryStream, compressionLevel, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, obj, options);
        }

        return memoryStream.ToArray();
    }
}
