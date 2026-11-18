using MhModManager.Core;

namespace MhModManager.Host;

internal static class AppearanceBackground
{
    public const int MaxBytes = 8 * 1024 * 1024;
    private static string ImagePath => Path.Combine(AppPaths.Root, "appearance-background");

    public static string? Revision => new FileInfo(ImagePath) is { Exists: true } file
        ? $"{file.LastWriteTimeUtc.Ticks:x}-{file.Length:x}" : null;

    public static void Replace(byte[] image)
    {
        if (image.Length > MaxBytes) throw new InvalidDataException("背景图片不能超过 8 MB。");
        if (ContentType(image) is null) throw new InvalidDataException("请选择 PNG、JPEG 或 WebP 图片。");

        var temporary = Path.Combine(AppPaths.TempDir, $"background-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(temporary, image);
            File.Move(temporary, ImagePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static void Remove() => File.Delete(ImagePath);

    public static IResult Read()
    {
        if (!File.Exists(ImagePath)) return Results.NotFound();
        var stream = new FileStream(ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        Span<byte> header = stackalloc byte[12];
        string? contentType;
        try
        {
            contentType = ContentType(header[..stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false)]);
            stream.Position = 0;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
        if (contentType is not null) return Results.File(stream, contentType);
        stream.Dispose();
        return Results.NotFound();
    }

    private static string? ContentType(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "image/png";
        if (header.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
