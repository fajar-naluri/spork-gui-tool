using System;
using System.IO;
using System.Collections.Generic;
using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// Tests for CodeScannerService — the component that scans source files
/// and extracts normalised translation keys.
///
/// Two layers:
///   Unit tests   — exercise ExtractKeysFromPattern with in-memory strings.
///   Integration  — exercise ScanCodeFiles against the real fury/src directory
///                  using the t('key') pattern that the GUI exposes as "t('key')".
///
/// fury/src lives at:  {solution-root}/../../fury/src
/// All key assertions are grounded in actual usage found in:
///   fury/src/features/learn/screens/LearnSearch.tsx
///   fury/src/features/learn/screens/Learn.tsx
///   fury/src/features/learn/screens/LearnHistory.tsx
///   fury/src/navigation/root-navigator.tsx
///   etc.
/// </summary>
[TestFixture]
public class CodeScannerServiceTests
{
    private CodeScannerService _scanner = null!;
    private NormalizationService _norm  = null!;

    // Resolve fury/src relative to the test binary so tests work on any machine
    // that has the repository checked out with the same structure.
    // Binary path: …/SporkGui.Tests/bin/Debug/net10.0/
    // Repository root (6 dirs up): …/spork-migration/
    private static readonly string FurySrcDir = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "fury", "src"));

    // Standard t('key') pattern used by the GUI "t('key')" preset
    private const string TPattern = @"t\(['""]([^'""]+)['""][^)]*\)";

    [SetUp]
    public void SetUp()
    {
        _norm    = new NormalizationService();
        _scanner = new CodeScannerService(_norm);
    }

    // ─────────────────────────────────────────────────────────────────
    // Unit tests — ExtractKeysFromPattern (in-memory strings)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void ExtractKeys_SimpleCall_ReturnsKey()
    {
        // t('search_a_keyword') — as used in LearnSearch.tsx
        var keys = _scanner.ExtractKeysFromPattern(
            "placeholder: t('search_a_keyword')",
            TPattern);

        Assert.That(keys, Contains.Item("search_a_keyword"));
    }

    [Test]
    public void ExtractKeys_CallWithExtraParams_ReturnsKey()
    {
        // t('lesson_search', { ns: 'learn' }) — root-navigator.tsx
        var keys = _scanner.ExtractKeysFromPattern(
            "headerTitle: t('lesson_search', { ns: 'learn' })",
            TPattern);

        Assert.That(keys, Contains.Item("lesson_search"));
    }

    [Test]
    public void ExtractKeys_CallWithCount_ReturnsKey()
    {
        // t('numLessons', { count: phase.lessons?.length }) — PhaseCard.tsx
        var keys = _scanner.ExtractKeysFromPattern(
            "t('numLessons', { count: phase.lessons?.length })",
            TPattern);

        Assert.That(keys, Contains.Item("numLessons"));
    }

    [Test]
    public void ExtractKeys_CallWithNamespacePrefix_ReturnsFullKey()
    {
        // t('common:cancel') — LearnSearch.tsx cancelButtonText
        var keys = _scanner.ExtractKeysFromPattern(
            "cancelButtonText: t('common:cancel')",
            TPattern);

        Assert.That(keys, Contains.Item("common:cancel"));
    }

    [Test]
    public void ExtractKeys_NestedObjectKey_ReturnsKey()
    {
        // t('phaseCompleteCongratulations.title', { phaseTitle: ... }) — PhaseCompletionModal.tsx
        var keys = _scanner.ExtractKeysFromPattern(
            "t('phaseCompleteCongratulations.title', { phaseTitle: phase })",
            TPattern);

        Assert.That(keys, Contains.Item("phaseCompleteCongratulations.title"));
    }

    [Test]
    public void ExtractKeys_MultipleCallsOnOneLine_ReturnsAll()
    {
        // Multiple translations on one JSX line
        var keys = _scanner.ExtractKeysFromPattern(
            "label={t('lessons')} other={t('categories')}",
            TPattern);

        Assert.That(keys, Contains.Item("lessons"));
        Assert.That(keys, Contains.Item("categories"));
    }

    [Test]
    public void ExtractKeys_MultiLineContent_ReturnsAllKeys()
    {
        // Simulate excerpt from LearnSearch.tsx
        var content = """
            {t('search_results')}
            <Text>{t('loading_up_lessons')}</Text>
            {t('explore_our_library_of_lessons')}
            {t('search_for_quick_wellness_tips_short_lessons_and_guided_exercise')}
            """;

        var keys = _scanner.ExtractKeysFromPattern(content, TPattern);

        Assert.That(keys, Is.SupersetOf(new[]
        {
            "search_results",
            "loading_up_lessons",
            "explore_our_library_of_lessons",
            "search_for_quick_wellness_tips_short_lessons_and_guided_exercise"
        }));
    }

    [Test]
    public void ExtractKeys_DuplicateCalls_ReturnsDistinct()
    {
        // The same key used twice should appear only once in the output.
        var keys = _scanner.ExtractKeysFromPattern(
            "t('search_results') ... t('search_results')",
            TPattern);

        Assert.That(keys.FindAll(k => k == "search_results"), Has.Count.EqualTo(1),
            "Duplicate keys must be de-duplicated");
    }

    [Test]
    public void ExtractKeys_EmptyContent_ReturnsEmpty()
    {
        var keys = _scanner.ExtractKeysFromPattern("", TPattern);
        Assert.That(keys, Is.Empty);
    }

    [Test]
    public void ExtractKeys_NoMatches_ReturnsEmpty()
    {
        var keys = _scanner.ExtractKeysFromPattern(
            "const x = 42; // no translation calls here",
            TPattern);
        Assert.That(keys, Is.Empty);
    }

    [Test]
    public void ExtractKeys_EmptyPattern_ReturnsEmpty()
    {
        var keys = _scanner.ExtractKeysFromPattern("t('key')", "");
        Assert.That(keys, Is.Empty);
    }

    [Test]
    public void ExtractKeys_I18nTPattern_ReturnsKey()
    {
        // i18n.t('key') pattern — supported as a second preset in the GUI
        const string i18nPattern = @"i18n\.t\(['""]([^'""]+)['""][^)]*\)";
        var keys = _scanner.ExtractKeysFromPattern(
            "const label = i18n.t('done');",
            i18nPattern);

        Assert.That(keys, Contains.Item("done"));
    }

    // ─────────────────────────────────────────────────────────────────
    // Unit tests — ScanCodeFiles (filesystem, controlled fixtures)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void ScanCodeFiles_EmptyDirectory_ReturnsEmptySet()
    {
        var dir = Path.Combine(Path.GetTempPath(), "spork_test_empty_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var found = _scanner.ScanCodeFiles(dir, [".tsx", ".ts"], TPattern);
            Assert.That(found, Is.Empty);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void ScanCodeFiles_NonExistentDirectory_ReturnsEmptySet()
    {
        var found = _scanner.ScanCodeFiles(
            "/path/that/does/not/exist", [".tsx"], TPattern);
        Assert.That(found, Is.Empty);
    }

    [Test]
    public void ScanCodeFiles_EmptyPattern_ReturnsEmptySet()
    {
        var found = _scanner.ScanCodeFiles(FurySrcDir, [".tsx", ".ts"], "");
        Assert.That(found, Is.Empty);
    }

    [Test]
    public void ScanCodeFiles_NormalizesKeys()
    {
        // Write a temp file with a mixed-case / dotted key and verify it is normalised.
        var dir  = Path.Combine(Path.GetTempPath(), "spork_test_norm_" + Guid.NewGuid());
        var file = Path.Combine(dir, "test.tsx");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(file, "const x = t('phaseCompleteCongratulations.title');");
            var found = _scanner.ScanCodeFiles(dir, [".tsx"], TPattern);

            // NormalizationService strips dots → "phasecompletecongratulatons title" without dot
            var expected = _norm.NormalizeKey("phaseCompleteCongratulations.title");
            Assert.That(found, Contains.Item(expected));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Integration tests — ScanCodeFiles against real fury/src
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Skips the test gracefully if fury/src is not present.</summary>
    private void RequireFurySrc()
    {
        if (!Directory.Exists(FurySrcDir))
            Assert.Ignore($"fury/src not found at '{FurySrcDir}'; skipping integration test.");
    }

    [Test]
    public void ScanFury_FindsLearnSearchKeys()
    {
        RequireFurySrc();

        var found = _scanner.ScanCodeFiles(FurySrcDir, [".tsx", ".ts"], TPattern);

        // All of these are confirmed t() calls in LearnSearch.tsx
        var expected = new[]
        {
            "search_a_keyword",
            "search_results",
            "loading_up_lessons",
            "explore_our_library_of_lessons",
            "search_for_quick_wellness_tips_short_lessons_and_guided_exercise",
            "lessons",
            "categories"
        };

        foreach (var key in expected)
        {
            var normalised = _norm.NormalizeKey(key);
            Assert.That(found, Contains.Item(normalised),
                $"Expected normalised key '{normalised}' (from '{key}') to be found in fury/src");
        }
    }

    [Test]
    public void ScanFury_FindsLearnNavigationKeys()
    {
        RequireFurySrc();

        var found = _scanner.ScanCodeFiles(FurySrcDir, [".tsx", ".ts"], TPattern);

        // lesson_search is used as: t('lesson_search', { ns: 'learn' }) in root-navigator.tsx
        // allPhases is used as:     t('allPhases', { ns: 'learn' }) in root-navigator.tsx
        // history is used as:       t('history', { ns: 'learn' }) in root-navigator.tsx
        Assert.That(found, Contains.Item(_norm.NormalizeKey("lesson_search")),
            "lesson_search (t call with ns param) must be found");
        Assert.That(found, Contains.Item(_norm.NormalizeKey("allPhases")),
            "allPhases (t call with ns param) must be found");
        Assert.That(found, Contains.Item(_norm.NormalizeKey("history")),
            "history (t call with ns param) must be found");
    }

    [Test]
    public void ScanFury_FindsLearnHistoryScreenKeys()
    {
        RequireFurySrc();

        var found = _scanner.ScanCodeFiles(FurySrcDir, [".tsx", ".ts"], TPattern);

        // LearnHistory.tsx uses these keys
        var expected = new[]
        {
            "no_lessons_completed_yet",
            "ready_to_dive_in",
            "dont_know_where_to_start",
            "recently_completed",
            "get_started_with_your_first_lesson_to_begin_tracking_your_progress"
        };

        foreach (var key in expected)
        {
            Assert.That(found, Contains.Item(_norm.NormalizeKey(key)),
                $"Key '{key}' from LearnHistory.tsx must be found");
        }
    }

    [Test]
    public void ScanFury_DoesNotFindPluralVariant()
    {
        RequireFurySrc();

        // numLessons_plural is an i18next pluralisation variant, never directly t()-called.
        // The scanner should NOT find it.
        var found = _scanner.ScanCodeFiles(FurySrcDir, [".tsx", ".ts"], TPattern);

        Assert.That(found, Does.Not.Contain(_norm.NormalizeKey("numLessons_plural")),
            "numLessons_plural is never directly t()-called and must not appear in scan results");
    }

    [Test]
    public void ScanFury_DoesNotFindUnreferencedMobileSheetKeys()
    {
        RequireFurySrc();

        // These keys appear in mobile_sheet.csv but have NO t() call in fury/src:
        var neverUsed = new[] { "message_not_sent", "hang_tight" };

        var found = _scanner.ScanCodeFiles(FurySrcDir, [".tsx", ".ts"], TPattern);

        foreach (var key in neverUsed)
        {
            Assert.That(found, Does.Not.Contain(_norm.NormalizeKey(key)),
                $"'{key}' is in mobile_sheet.csv but never referenced in code; must not be found");
        }
    }

    [Test]
    public void ScanFury_ResultCountIsNonTrivial()
    {
        RequireFurySrc();

        // Sanity: a real codebase should yield dozens of unique translation keys.
        var found = _scanner.ScanCodeFiles(FurySrcDir, [".tsx", ".ts"], TPattern);

        Assert.That(found.Count, Is.GreaterThan(50),
            "A full React Native project should have many translation key usages");
    }

    [Test]
    public void ScanFury_TsxOnlyVsTsAndTsx_TsxSubsetOfCombined()
    {
        RequireFurySrc();

        var tsxOnly   = _scanner.ScanCodeFiles(FurySrcDir, [".tsx"], TPattern);
        var combined  = _scanner.ScanCodeFiles(FurySrcDir, [".tsx", ".ts"], TPattern);

        Assert.That(tsxOnly.IsSubsetOf(combined),
            "Scanning .tsx alone must yield a subset of scanning .tsx + .ts");
    }
}
