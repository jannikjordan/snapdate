using System.Diagnostics;

namespace Snapdate.Domain.Tests.FileNames;

/// <summary>
/// File names of the maximum length (260) built to keep a regex engine busy: long runs of one character, a single odd
/// character in the middle, repeating pairs, digits everywhere, random text. Used to bound the time of one match.
/// </summary>
internal static class AdversarialNames
{
    /// <summary>One match must stay well below this, on any machine; measured worst cases are around 20 ms.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromMilliseconds(200);

    public static IReadOnlyList<string> All { get; } = Create();

    /// <summary>
    /// The longest time <paramref name="match"/> takes for one of <see cref="All"/>, as the best of three runs, so a
    /// moment of load on the machine does not count.
    /// </summary>
    public static TimeSpan Worst(Action<string> match)
    {
        var best = TimeSpan.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var worst = TimeSpan.Zero;
            foreach (var name in All)
            {
                var watch = Stopwatch.StartNew();
                match(name);
                watch.Stop();
                worst = watch.Elapsed > worst ? watch.Elapsed : worst;
            }

            best = worst < best ? worst : best;
        }

        return best;
    }

    private static string[] Create()
    {
        var random = new Random(7);
        var printable = new string([.. Enumerable.Range(0, 252).Select(_ => (char)random.Next(0x20, 0x7f))]);
        string[] names =
        [
            "20240714" + new string('a', 252),
            "20240714" + new string('a', 120) + "b" + new string('a', 131),
            "20240714" + Repeat("ab", 126),
            "20240714" + printable,
            "20240714" + Repeat("a-", 126),
            "20240714" + new string('0', 252),
            new string('a', 126) + "20240714" + new string('a', 126),
            Repeat("2024071", 37) + "a",
            "20240714_" + Repeat("a0", 125) + "b",
            "20240714" + Repeat("a.", 126),
            "20240714" + new string('a', 251) + "!",
            "20240714" + new string([.. Enumerable.Range(0, 252).Select(_ => (char)random.Next(0x20, 0x3000))]),
            "20240714" + Repeat("abcdefgh", 31) + "abcd",
            "20240714" + Repeat("[x-a]", 50) + "aa",
            "20240714x" + new string('a', 119) + "b" + new string('a', 131) + "]",
        ];
        return names;
    }

    private static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));
}
