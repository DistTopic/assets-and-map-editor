namespace AssetsAndMapEditor.OTB;

/// <summary>Top-level data read from an items.otb file.</summary>
public sealed class OtbData
{
    /// <summary>First 4 bytes of the file (version/format header).</summary>
    public byte[] FileHeader { get; set; } = new byte[4];

    /// <summary>Root node type byte.</summary>
    public byte RootType { get; set; }

    /// <summary>
    /// Root node data (raw escaped bytes between root start and first child or end).
    /// Contains OTB version info.  Preserved for lossless round-trip.
    /// </summary>
    public byte[] RootData { get; set; } = [];

    /// <summary>All item entries.</summary>
    public List<OtbItem> Items { get; set; } = [];
}
