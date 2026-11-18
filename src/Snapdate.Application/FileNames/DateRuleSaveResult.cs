namespace Snapdate.Application.FileNames;

/// <summary>Outcome of saving the user's file-name date rules.</summary>
public enum DateRuleSaveResult
{
    /// <summary>The file was written.</summary>
    Saved,

    /// <summary>
    /// Not written: the file was written by a newer version, or (from <see cref="DateRuleCatalog.Save"/>) the catalog
    /// is read-only because the file could not be read when it was loaded.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Not written: the file changed since it was loaded (another app instance, or it was edited or repaired meanwhile).
    /// Reload before changing the rules again.
    /// </summary>
    Conflict,

    /// <summary>Not written: the rules would exceed the size limit of the file.</summary>
    TooLarge,

    /// <summary>Not written: the file could not be read, moved aside or written (e.g. locked or no access).</summary>
    Failed,
}
