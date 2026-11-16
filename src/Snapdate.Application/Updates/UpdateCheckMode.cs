namespace Snapdate.Application.Updates;

public enum UpdateCheckMode
{
    /// <summary>Check on start: honours the opt-out, the daily limit and "skip this version".</summary>
    Automatic,

    /// <summary>User pressed "check now": ignores the opt-out, the daily limit and "skip this version".</summary>
    Manual,
}
