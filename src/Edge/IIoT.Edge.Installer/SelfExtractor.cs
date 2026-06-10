using System.Buffers.Binary;
using System.IO.Compression;

namespace IIoT.Edge.Installer;

/// <summary>
/// 自解压安装器的载荷读写：成品 .exe = [安装器外壳][载荷 zip][载荷长度 8 字节小端][magic 8 字节]。
/// 服务端按你勾选的工序，把【launcher + 选中的工序文件夹 + iiot-binding.json(含码)】打成 zip 追加到外壳尾部，
/// 得到一个双击即装的 .exe;安装器运行时从自身尾部读回 zip 解压。整条链不需要在服务端编译。
/// </summary>
internal static class SelfExtractor
{
    private static readonly byte[] Magic = "IIOTEDG1"u8.ToArray();
    private const int TrailerLength = 16; // 8(长度) + 8(magic)

    /// <summary>读取自身 exe 尾部追加的载荷(zip 字节);没有则返回 null。</summary>
    public static byte[]? ReadAppendedPayload(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        if (stream.Length < TrailerLength)
        {
            return null;
        }

        var trailer = new byte[TrailerLength];
        stream.Seek(-TrailerLength, SeekOrigin.End);
        ReadExact(stream, trailer, TrailerLength);

        if (!trailer.AsSpan(8, 8).SequenceEqual(Magic))
        {
            return null;
        }

        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(0, 8));
        if (payloadLength <= 0 || payloadLength > stream.Length - TrailerLength)
        {
            return null;
        }

        var payload = new byte[payloadLength];
        stream.Seek(-(TrailerLength + payloadLength), SeekOrigin.End);
        ReadExact(stream, payload, (int)payloadLength);
        return payload;
    }

    /// <summary>把载荷 zip 解压到目标目录(带 zip 路径穿越防护)。</summary>
    public static void ExtractPayload(byte[] payloadZip, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var fullTarget = Path.GetFullPath(targetDirectory);

        using var zipStream = new MemoryStream(payloadZip, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // 目录项
            }

            var destination = Path.GetFullPath(Path.Combine(fullTarget, entry.FullName));
            if (!destination.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue; // 防 zip 穿越
            }

            var entryDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(entryDirectory))
            {
                Directory.CreateDirectory(entryDirectory);
            }

            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    /// <summary>生成成品 .exe:外壳 + 载荷 + 尾部。供发布脚本/服务端打包与测试使用。</summary>
    public static void AppendPayload(string stubPath, byte[] payloadZip, string outputPath)
    {
        File.Copy(stubPath, outputPath, overwrite: true);

        using var output = new FileStream(outputPath, FileMode.Append, FileAccess.Write);
        output.Write(payloadZip);

        Span<byte> trailer = stackalloc byte[TrailerLength];
        BinaryPrimitives.WriteInt64LittleEndian(trailer[..8], payloadZip.Length);
        Magic.CopyTo(trailer[8..]);
        output.Write(trailer);
    }

    private static void ReadExact(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }
            offset += read;
        }
    }
}
