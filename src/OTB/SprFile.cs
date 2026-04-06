using System.Buffers.Binary;

namespace AssetsAndMapEditor.OTB;

/// <summary>
/// Tibia .spr file parser supporting multiple protocol versions.
/// Extended (protocol &gt;= 960): 8-byte header (U32 sig + U32 count). Legacy: 6-byte (U32 sig + U16 count).
/// Transparency: controls pixel format — RGBA (4 bytes) or RGB (3 bytes). Independent from extended.
/// </summary>
public sealed class SprFile : IDisposable
{
    private const int SpriteSize = 32;
    private const int SpritePixels = SpriteSize * SpriteSize;
    private const int SpriteBytesRgba = SpritePixels * 4;

    private readonly FileStream _stream;
    private uint _spriteCount;
    private readonly long _offsetTableStart;
    private readonly bool _useAlpha;
    private readonly uint _signature;
    private readonly bool _extended; // true = U32 count (protocol >= 960)

    // In-memory overrides for modified sprites (null value = blank/transparent)
    private Dictionary<uint, byte[]?>? _overrides;
    private Dictionary<uint, byte[]?> Overrides => _overrides ??= new();

    public bool HasChanges => _overrides is { Count: > 0 } || _spriteCount != _originalCount;
    private readonly uint _originalCount;

    private SprFile(FileStream stream, uint signature, uint spriteCount, long offsetTableStart, bool useAlpha, bool extended)
    {
        _stream = stream;
        _signature = signature;
        _spriteCount = spriteCount;
        _originalCount = spriteCount;
        _offsetTableStart = offsetTableStart;
        _useAlpha = useAlpha;
        _extended = extended;
    }

    public uint SpriteCount => _spriteCount;

    /// <summary>
    /// Open an .spr file with protocol-aware header parsing.
    /// </summary>
    /// <param name="path">Path to Tibia.spr</param>
    /// <param name="extended">True for U32 sprite count header (protocol &gt;= 960). False for U16 count.</param>
    /// <param name="transparency">True for RGBA pixel data (4 bytes per pixel). False for RGB (3 bytes). When null, defaults to same as extended.</param>
    public static SprFile Load(string path, bool extended = true, bool? transparency = null)
    {
        bool useAlpha = transparency ?? extended;
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (extended)
        {
            // Extended: 4 sig + 4 count = 8 bytes header
            var header = new byte[8];
            stream.ReadExactly(header);
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0));
            var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            var offsetStart = stream.Position;
            return new SprFile(stream, signature, count, offsetStart, useAlpha: useAlpha, extended: true);
        }
        else
        {
            // Legacy: 4 sig + 2 count = 6 bytes header
            var header = new byte[6];
            stream.ReadExactly(header);
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0));
            var count = (uint)BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
            var offsetStart = stream.Position;
            return new SprFile(stream, signature, count, offsetStart, useAlpha: useAlpha, extended: false);
        }
    }

    /// <summary>
    /// Decode sprite by 1-based ID. Returns 32×32 RGBA pixel data (4096 bytes),
    /// or null if the sprite doesn't exist.
    /// </summary>
    public byte[]? GetSpriteRgba(uint spriteId)
    {
        if (spriteId == 0 || spriteId > _spriteCount)
            return null;

        // Check in-memory overrides first
        if (_overrides != null && _overrides.TryGetValue(spriteId, out var overridden))
            return overridden is { Length: SpriteBytesRgba } ? (byte[])overridden.Clone() : null;

        return ReadOriginalSpriteRgba(spriteId);
    }

    /// <summary>Replace the pixels of an existing sprite (1-based ID).</summary>
    public void SetSpriteRgba(uint spriteId, byte[]? rgba)
    {
        if (spriteId == 0 || spriteId > _spriteCount)
            throw new ArgumentOutOfRangeException(nameof(spriteId));
        Overrides[spriteId] = rgba is { Length: SpriteBytesRgba } ? (byte[])rgba.Clone() : null;
    }

    /// <summary>Append a new sprite. Returns the 1-based ID of the new sprite.</summary>
    public uint AddSprite(byte[]? rgba)
    {
        _spriteCount++;
        Overrides[_spriteCount] = rgba is { Length: SpriteBytesRgba } ? (byte[])rgba.Clone() : null;
        return _spriteCount;
    }

    /// <summary>
    /// Remove a sprite. If it's the last one, decrements count.
    /// Otherwise blanks it (transparent).
    /// </summary>
    public void RemoveSprite(uint spriteId)
    {
        if (spriteId == 0 || spriteId > _spriteCount)
            throw new ArgumentOutOfRangeException(nameof(spriteId));

        if (spriteId == _spriteCount)
        {
            // Last sprite — decrement count and remove any override
            _overrides?.Remove(spriteId);
            _spriteCount--;
            // Trim consecutive blank trailing sprites
            while (_spriteCount > 0)
            {
                if (_overrides != null && _overrides.TryGetValue(_spriteCount, out var ov) && ov == null)
                {
                    _overrides.Remove(_spriteCount);
                    _spriteCount--;
                    continue;
                }
                break;
            }
        }
        else
        {
            // Not last — blank it
            Overrides[spriteId] = null;
        }
    }

    /// <summary>Save all sprites (original + overrides) to a new .spr file.</summary>
    public void Save(string path)
    {
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        // Header: signature + sprite count (size depends on extended flag)
        if (_extended)
        {
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(header, _signature);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], _spriteCount);
            output.Write(header);
        }
        else
        {
            Span<byte> header = stackalloc byte[6];
            BinaryPrimitives.WriteUInt32LittleEndian(header, _signature);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)_spriteCount);
            output.Write(header);
        }

        // We'll write offset table (4 bytes × spriteCount) as a placeholder, then sprite data.
        // After writing all sprites, we'll seek back and fill in offsets.
        var offsetTablePos = output.Position;
        var offsets = new uint[_spriteCount];

        // Reserve space for offset table
        output.Write(new byte[_spriteCount * 4]);

        // Pre-allocate buffers outside the loop
        Span<byte> sprHeader = stackalloc byte[5];
        sprHeader[0] = 0xFF;
        sprHeader[1] = 0x00;
        sprHeader[2] = 0xFF;

        // Write each sprite's data
        for (uint id = 1; id <= _spriteCount; id++)
        {
            byte[]? rgba = null;

            if (_overrides != null && _overrides.TryGetValue(id, out var ov))
                rgba = ov;
            else
                rgba = ReadOriginalSpriteRgba(id);

            if (rgba is not { Length: SpriteBytesRgba })
            {
                // Blank sprite — offset stays 0
                continue;
            }

            offsets[id - 1] = (uint)output.Position;

            // Color key (3 bytes: 0xFF 0x00 0xFF) + compressed data size (U16) + RLE data
            var rle = RleEncode(rgba, _useAlpha);
            BinaryPrimitives.WriteUInt16LittleEndian(sprHeader[3..], (ushort)rle.Length);
            output.Write(sprHeader);
            output.Write(rle);
        }

        // Seek back and write offset table
        output.Seek(offsetTablePos, SeekOrigin.Begin);
        Span<byte> offsetBuf = stackalloc byte[4];
        for (uint i = 0; i < _spriteCount; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(offsetBuf, offsets[i]);
            output.Write(offsetBuf);
        }
    }

    /// <summary>Read a sprite from the original file (bypasses overrides).</summary>
    private byte[]? ReadOriginalSpriteRgba(uint spriteId)
    {
        if (spriteId == 0 || spriteId > _originalCount)
            return null;

        var offsetPos = _offsetTableStart + (spriteId - 1) * 4;
        _stream.Seek(offsetPos, SeekOrigin.Begin);

        Span<byte> buf4 = stackalloc byte[4];
        _stream.ReadExactly(buf4);
        var spriteOffset = BinaryPrimitives.ReadUInt32LittleEndian(buf4);
        if (spriteOffset == 0) return null;

        _stream.Seek(spriteOffset, SeekOrigin.Begin);
        Span<byte> sprH = stackalloc byte[5];
        _stream.ReadExactly(sprH);
        var dataSize = BinaryPrimitives.ReadUInt16LittleEndian(sprH[3..]);
        if (dataSize == 0) return null;

        var compressed = new byte[dataSize];
        _stream.ReadExactly(compressed);
        return DecodeRle(compressed, dataSize);
    }

    private byte[] DecodeRle(byte[] compressed, int dataSize)
    {
        var pixels = new byte[SpriteBytesRgba];
        int writePos = 0, readPos = 0;
        int channels = _useAlpha ? 4 : 3;

        while (readPos + 4 <= dataSize && writePos < SpriteBytesRgba)
        {
            var transparentPixels = BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(readPos));
            readPos += 2;
            var coloredPixels = BinaryPrimitives.ReadUInt16LittleEndian(compressed.AsSpan(readPos));
            readPos += 2;
            writePos += transparentPixels * 4;
            for (int i = 0; i < coloredPixels && writePos + 4 <= SpriteBytesRgba; i++)
            {
                if (readPos + channels > dataSize) break;
                pixels[writePos + 0] = compressed[readPos + 0];
                pixels[writePos + 1] = compressed[readPos + 1];
                pixels[writePos + 2] = compressed[readPos + 2];
                pixels[writePos + 3] = _useAlpha ? compressed[readPos + 3] : (byte)0xFF;
                writePos += 4;
                readPos += channels;
            }
        }
        return pixels;
    }

    /// <summary>RLE-encode 32×32 RGBA pixels into Tibia SPR format.</summary>
    private static byte[] RleEncode(byte[] rgba, bool useAlpha)
    {
        using var ms = new MemoryStream();
        int channels = useAlpha ? 4 : 3;
        int totalPixels = SpritePixels;
        int pos = 0;

        Span<byte> runHeader = stackalloc byte[4];

        while (pos < totalPixels)
        {
            // Count transparent pixels (alpha == 0)
            ushort transparent = 0;
            while (pos + transparent < totalPixels && rgba[(pos + transparent) * 4 + 3] == 0)
                transparent++;
            pos += transparent;

            // Count colored pixels (alpha != 0)
            ushort colored = 0;
            while (pos + colored < totalPixels && rgba[(pos + colored) * 4 + 3] != 0)
                colored++;

            // Write run header
            BinaryPrimitives.WriteUInt16LittleEndian(runHeader, transparent);
            BinaryPrimitives.WriteUInt16LittleEndian(runHeader[2..], colored);
            ms.Write(runHeader);

            // Write colored pixel data
            for (int i = 0; i < colored; i++)
            {
                int idx = (pos + i) * 4;
                ms.WriteByte(rgba[idx + 0]); // R
                ms.WriteByte(rgba[idx + 1]); // G
                ms.WriteByte(rgba[idx + 2]); // B
                if (useAlpha) ms.WriteByte(rgba[idx + 3]); // A
            }
            pos += colored;
        }
        return ms.ToArray();
    }

    public void Dispose() => _stream.Dispose();
}
