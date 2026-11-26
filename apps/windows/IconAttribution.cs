using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fignimble.Windows;

// These tags match the Mac client's SVG and PNG conventions. They travel with the image.
internal static class IconAttribution
{
    private const string SvgPrefix = "<!-- biocartoon64:";
    private const string PngPrefix = "Comment\0biocartoon:";
    private const int MaxBytes = 32 * 1024 * 1024;
    private const int MaxTag = 64 * 1024;

    internal static void Write(string path, string record)
    {
        if (record.Length > MaxTag) throw new IOException("The BioCartoon attribution record is too large.");
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            File.AppendAllText(path, "\n" + SvgPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(record)) + " -->\n", new UTF8Encoding(false));
            return;
        }
        if (new FileInfo(path).Length > MaxBytes) throw new IOException("The fetched PNG is too large to tag.");
        byte[] png = File.ReadAllBytes(path);
        int end = Chunks(png).FirstOrDefault(c => c.Type == "IEND").Start;
        if (end <= 0) throw new IOException("The downloaded icon is not a complete PNG.");
        // System.Text.Json emits ASCII by default, including Unicode escape sequences for credits.
        byte[] data = Encoding.ASCII.GetBytes(PngPrefix + record);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        "tEXt"u8.CopyTo(chunk.AsSpan(4));
        data.CopyTo(chunk.AsSpan(8));
        uint crc = 0xffffffff;
        foreach (byte b in chunk.AsSpan(4, 4 + data.Length))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), ~crc);
        using var output = File.Create(path);
        output.Write(png.AsSpan(0, end));
        output.Write(chunk);
        output.Write(png.AsSpan(end));
    }

    internal static string? Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxBytes) return null;
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                string text = File.ReadAllText(path);
                int start = text.LastIndexOf(SvgPrefix, StringComparison.Ordinal);
                if (start < 0) return null;
                start += SvgPrefix.Length;
                int end = text.IndexOf(" -->", start, StringComparison.Ordinal);
                return end >= start && end - start <= MaxTag * 2 ? Encoding.UTF8.GetString(Convert.FromBase64String(text[start..end])) : null;
            }
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
            byte[] png = File.ReadAllBytes(path);
            string? found = null;
            foreach (var chunk in Chunks(png))
            {
                if (chunk.Type is not ("tEXt" or "iTXt") || chunk.Length > MaxTag * 2) continue;
                string data = Encoding.UTF8.GetString(png, chunk.Start + 8, chunk.Length);
                if (chunk.Type == "iTXt")
                {
                    const string key = "Comment\0\0\0"; // uncompressed iTXt comment
                    if (!data.StartsWith(key, StringComparison.Ordinal)) continue;
                    int language = data.IndexOf('\0', key.Length);
                    int translated = language < 0 ? -1 : data.IndexOf('\0', language + 1);
                    if (translated < 0) continue;
                    data = "Comment\0" + data[(translated + 1)..];
                }
                if (data.StartsWith(PngPrefix, StringComparison.Ordinal)) found = data[PngPrefix.Length..];
            }
            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { return null; }
    }

    private static IEnumerable<(int Start, int Length, string Type)> Chunks(byte[] png)
    {
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < signature.Length || !png.AsSpan(0, 8).SequenceEqual(signature)) yield break;
        for (int start = 8; start <= png.Length - 12;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(start, 4));
            if (length < 0 || length > png.Length - start - 12) yield break;
            string type = Encoding.ASCII.GetString(png, start + 4, 4);
            yield return (start, length, type);
            if (type == "IEND") yield break;
            start += 12 + length;
        }
    }
}
