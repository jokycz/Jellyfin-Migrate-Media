// ReSharper disable UnusedMember.Global
namespace JellyfinMigrateMedia.Infrastructure.Db;

/// <summary>
/// Jellyfin DB stores GUIDs as BLOB (Guid.ToByteArray()) in many columns (guid/ParentId/etc),
/// but some columns/config sources may represent the same id as text (usually "N" format).
/// This value-object keeps all representations consistent.
/// </summary>
public readonly struct JellyfinGuid : IEquatable<JellyfinGuid>
{
    public Guid Value { get; }

    /// <summary>Guid as string with dashes ("D").</summary>
    public string Dashed => Value.ToString("D");

    /// <summary>Guid as string without dashes ("N"). This is what PhysicalFolderIds commonly uses.</summary>
    public string NoDashes => Value.ToString("N");

    /// <summary>Guid bytes as stored by Jellyfin in SQLite BLOB columns.</summary>
    public byte[] Bytes => Value.ToByteArray();

    /// <summary>
    /// Hex representation of <see cref="Bytes"/> (little-endian parts, matches SQLite hex(blob)).
    /// Useful for debugging/logging only.
    /// </summary>
    public string Hex => Convert.ToHexString(Bytes).ToLowerInvariant();

    public JellyfinGuid(Guid value) => Value = value;

    public static JellyfinGuid Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Missing GUID text.", nameof(text));
        return new JellyfinGuid(Guid.Parse(text.Trim()));
    }

    public static bool TryParse(string? text, out JellyfinGuid guid)
    {
        guid = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!Guid.TryParse(text.Trim(), out var g)) return false;
        guid = new JellyfinGuid(g);
        return true;
    }

    public static JellyfinGuid FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length != 16) throw new ArgumentException("GUID byte array must be 16 bytes.", nameof(bytes));
        return new JellyfinGuid(new Guid(bytes));
    }

    public override string ToString() => NoDashes;

    public bool Equals(JellyfinGuid other) => Value.Equals(other.Value);
    public override bool Equals(object? obj) => obj is JellyfinGuid other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(JellyfinGuid left, JellyfinGuid right) => left.Equals(right);
    public static bool operator !=(JellyfinGuid left, JellyfinGuid right) => !(left == right);
}

