using System.Buffers.Binary;

namespace POriginsItemEditor.OTB;

/// <summary>
/// Tibia .spr file parser for protocol 1098.
/// Decodes individual 32×32 RGBA sprites on demand.
/// </summary>
public sealed class SprFile : IDisposable
{
    private const int SpriteSize = 32;
    private const int SpritePixels = SpriteSize * SpriteSize;
    private const int SpriteBytesRgba = SpritePixels * 4;

    private readonly FileStream _stream;
    private readonly uint _spriteCount;
    private readonly long _offsetTableStart;
    private readonly bool _useAlpha;

    private SprFile(FileStream stream, uint spriteCount, long offsetTableStart, bool useAlpha)
    {
        _stream = stream;
        _spriteCount = spriteCount;
        _offsetTableStart = offsetTableStart;
        _useAlpha = useAlpha;
    }

    public uint SpriteCount => _spriteCount;

    /// <summary>
    /// Open an .spr file. For protocol 1098, uses U32 sprite count and RGBA pixels.
    /// </summary>
    public static SprFile Load(string path, bool useAlpha = true)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[8];
        stream.ReadExactly(header);

        // U32 signature, U32 sprite count
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        var offsetStart = stream.Position;

        return new SprFile(stream, count, offsetStart, useAlpha);
    }

    /// <summary>
    /// Decode sprite by 1-based ID. Returns 32×32 RGBA pixel data (4096 bytes),
    /// or null if the sprite doesn't exist.
    /// </summary>
    public byte[]? GetSpriteRgba(uint spriteId)
    {
        if (spriteId == 0 || spriteId > _spriteCount)
            return null;

        // Read offset from table
        var offsetPos = _offsetTableStart + (spriteId - 1) * 4;
        _stream.Seek(offsetPos, SeekOrigin.Begin);

        Span<byte> buf4 = stackalloc byte[4];
        _stream.ReadExactly(buf4);
        var spriteOffset = BinaryPrimitives.ReadUInt32LittleEndian(buf4);

        if (spriteOffset == 0)
            return null;

        _stream.Seek(spriteOffset, SeekOrigin.Begin);

        // 3 bytes color key (skip), 2 bytes data size
        Span<byte> sprHeader = stackalloc byte[5];
        _stream.ReadExactly(sprHeader);
        var dataSize = BinaryPrimitives.ReadUInt16LittleEndian(sprHeader[3..]);

        if (dataSize == 0)
            return null;

        // Read compressed data
        var compressed = new byte[dataSize];
        _stream.ReadExactly(compressed);

        // Decode RLE
        var pixels = new byte[SpriteBytesRgba]; // initialized to 0 (transparent)
        int writePos = 0;
        int readPos = 0;
        int channels = _useAlpha ? 4 : 3;

        while (readPos + 4 <= dataSize && writePos < SpriteBytesRgba)
        {
            var transparentPixels = BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(readPos));
            readPos += 2;
            var coloredPixels = BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(readPos));
            readPos += 2;

            // Skip transparent pixels (already 0)
            writePos += transparentPixels * 4;

            // Read colored pixels
            for (int i = 0; i < coloredPixels && writePos + 4 <= SpriteBytesRgba; i++)
            {
                if (readPos + channels > dataSize) break;

                pixels[writePos + 0] = compressed[readPos + 0]; // R
                pixels[writePos + 1] = compressed[readPos + 1]; // G
                pixels[writePos + 2] = compressed[readPos + 2]; // B
                pixels[writePos + 3] = _useAlpha ? compressed[readPos + 3] : (byte)0xFF; // A
                writePos += 4;
                readPos += channels;
            }
        }

        return pixels;
    }

    public void Dispose() => _stream.Dispose();
}
