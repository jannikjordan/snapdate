using System.Globalization;
using Snapdate.App.Localization;
using Snapdate.App.Services;
using Snapdate.App.ViewModels;
using Snapdate.App.Views;
using Snapdate.Application.FileNames;
using Snapdate.Application.Ports;
using Snapdate.Application.Settings;
using Snapdate.Domain.FileNames;
using Snapdate.Infrastructure.FileNames;
using Snapdate.Infrastructure.FileSystem;
using Snapdate.Infrastructure.Metadata;

namespace Snapdate.App;

/// <summary>
/// <c>Snapdate.exe --self-test</c>: starts the platform (graphics, fonts, native libraries), loads
/// every view and both languages, then exits without showing a window. Exit code 0 means the
/// published executable can start. The build runs this on every release candidate.
/// </summary>
internal static class SelfTest
{
    public const string Argument = "--self-test";

    private sealed class NoSettings : ISettingsStore
    {
        public AppSettings Load() => new();

        public void Save(AppSettings settings)
        {
        }
    }

    private sealed class NoDesktop : IDesktopServices
    {
        public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult<string?>(null);

        public Task OpenUrlAsync(Uri url) => Task.CompletedTask;

        public Task OpenFolderAsync(string path) => Task.CompletedTask;

        public Task OpenFileAsync(string path) => Task.CompletedTask;
    }

    /// <summary>The embedded built-in rules and no user rules; never reads or writes the user's rule file.</summary>
    private sealed class EmbeddedRulesOnly(IReadOnlyList<DateRule> systemRules) : IDateRuleStore
    {
        public IReadOnlyList<DateRule> LoadSystemRules() => systemRules;

        public UserDateRulesLoad LoadUserRules() => UserDateRulesLoad.Missing;

        public UserDateRulesSave SaveUserRules(UserDateRules rules, UserRulesFingerprint expected) =>
            new(DateRuleSaveResult.ReadOnly, expected);
    }

    public static int Run()
    {
        try
        {
            // Initialises Skia/HarfBuzz/ANGLE: fails if a native library is missing or incompatible.
            Program.BuildAvaloniaApp().SetupWithoutStarting();

            var en = new LocalizedStrings(CultureInfo.GetCultureInfo("en-US"));
            var de = new LocalizedStrings(CultureInfo.GetCultureInfo("de-DE"));
            if (en.Step1Title == de.Step1Title || de.Step1Title == nameof(LocalizedStrings.Step1Title))
            {
                return 2; // German satellite resources missing
            }

            // The built-in date rules are an embedded resource: trimming must not drop it, and the generated
            // regular expressions must work in the published build. Only the embedded rules are read here.
            var systemRules = new JsonDateRuleStore(JsonDateRuleStore.DefaultPath).LoadSystemRules();
            if (systemRules.Count == 0
                || new DateRuleSet(systemRules).Match("IMG-20240714-WA0001", new DateTime(2026, 1, 1)) is null)
            {
                return 3; // built-in file-name date rules missing or not working
            }

            var services = new AppServices(
                new AvaloniaThemeService(Avalonia.Application.Current!),
                new NoDesktop(),
                new NoSettings(),
                new LocalFileSystem(),
                new MetadataExtractorReader(TimeProvider.System),
                TimeProvider.System,
                null,
                DateRules: new EmbeddedRulesOnly(systemRules));

            // Loads the compiled XAML, styles and bindings of the main window and of the "Dates from file names"
            // dialog (measuring it builds the rule rows from their template).
            foreach (var culture in new[] { "en-US", "de-DE" })
            {
                var vm = new MainWindowViewModel(services, CultureInfo.GetCultureInfo(culture));
                _ = new MainWindow { DataContext = vm };
                var rules = vm.RulesDialog!;
                rules.TestName = "IMG-20240714-WA0001.jpg";
                if (rules.Items.Count == 0 || rules.TestKind != RuleTestKind.Match)
                {
                    return 4; // rules dialog cannot list or test the built-in rules
                }

                var dialog = new DateRulesDialog { DataContext = rules };
                dialog.Measure(new Avalonia.Size(980, 720));

                // The rule editor: its template, blocks, validation and tester.
                rules.AddRuleCommand.Execute(null);
                if (rules.Editor is not { } editor)
                {
                    return 5; // rule editor cannot be opened
                }

                editor.PatternsText = "IMG-{YYYY}{MM}{DD}-WA*";
                editor.Sample = "IMG-20240714-WA0001.jpg";
                if (!editor.IsValid || editor.SampleKind != RuleSampleKind.Match)
                {
                    return 5; // rule editor cannot validate or test a pattern
                }

                dialog.Measure(new Avalonia.Size(980, 720));
            }

            // Exercises the metadata parser (any file works; a non-image yields "no metadata").
            if (Environment.ProcessPath is { } self)
            {
                _ = services.MetadataReader.Read(self);
            }

            return 0;
        }
#pragma warning disable CA1031 // Any failure means "this build cannot start".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
