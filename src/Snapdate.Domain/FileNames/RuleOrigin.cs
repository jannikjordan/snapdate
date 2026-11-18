namespace Snapdate.Domain.FileNames;

/// <summary>Who maintains a <see cref="DateRule"/>.</summary>
public enum RuleOrigin
{
    /// <summary>Ships with the app; the user can switch it off but not edit it.</summary>
    System,

    /// <summary>Created by the user; never touched by updates.</summary>
    Custom,
}
