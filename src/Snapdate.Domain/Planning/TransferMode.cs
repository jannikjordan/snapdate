namespace Snapdate.Domain.Planning;

/// <summary>What happens to the originals.</summary>
public enum TransferMode
{
    /// <summary>Originals stay untouched (default, safe).</summary>
    Copy,

    /// <summary>Originals are removed after a verified copy.</summary>
    Move,
}
