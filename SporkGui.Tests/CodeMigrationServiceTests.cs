using System;
using System.IO;
using System.Collections.Generic;
using SporkGui.Models;
using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// Tests for CodeMigrationService:
///   ApplyMigration  — rewrites t() calls and useTranslation() hooks in code files
///   ApplyMigrations — batch apply, skips non-pending entries
///   CreateBackup    — timestamped .backup copy
///   GenerateDiff    — preview without writing
///
/// Each test that touches the filesystem uses a fresh temp directory
/// cleaned up in TearDown.
/// </summary>
[TestFixture]
public class CodeMigrationServiceTests
{
    private CodeMigrationService _svc = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _svc     = new CodeMigrationService(new NormalizationService());
        _tempDir = Path.Combine(Path.GetTempPath(), "spork_code_mig_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private KeyMigrationEntry MakeEntry(
        string oldKey, string newKey,
        string oldNs = "learn", string newNs = "learn",
        MigrationStatus status = MigrationStatus.Pending,
        params string[] filePaths)
    {
        var entry = new KeyMigrationEntry
        {
            OldKey       = oldKey,
            OldNamespace = oldNs,
            NewKey       = newKey,
            NewNamespace = newNs,
            Status       = status
        };
        foreach (var fp in filePaths)
            entry.CodeUsages.Add(new CodeUsage { FilePath = fp, LineNumber = 1, Context = "" });
        return entry;
    }

    // ─────────────────────────────────────────────────────────────────
    // 1. ApplyMigration — guard conditions
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void ApplyMigration_AlreadyApplied_ReturnsFailed()
    {
        var entry = MakeEntry("lesson_search", "lessonSearch",
            status: MigrationStatus.Applied);

        var result = _svc.ApplyMigration(entry);

        Assert.That(result.Success,       Is.False);
        Assert.That(result.ErrorMessage,  Does.Contain("Applied"));
    }

    [Test]
    public void ApplyMigration_AlreadySkipped_ReturnsFailed()
    {
        var entry = MakeEntry("lesson_search", "lessonSearch",
            status: MigrationStatus.Skipped);

        var result = _svc.ApplyMigration(entry);

        Assert.That(result.Success,       Is.False);
        Assert.That(result.ErrorMessage,  Does.Contain("Skipped"));
    }

    [Test]
    public void ApplyMigration_NoCodeUsages_ReturnsFailed()
    {
        var entry = MakeEntry("lesson_search", "lessonSearch"); // no file paths added

        var result = _svc.ApplyMigration(entry);

        Assert.That(result.Success,      Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("No code usages"));
    }

    [Test]
    public void ApplyMigration_NeedsReviewStatus_IsProcessed()
    {
        // NeedsReview entries are still applicable — it just means the user
        // has been warned about ambiguity but chose to proceed.
        var file = WriteFile("LearnSearch.tsx",
            "const { t } = useTranslation('learn');\nt('lesson_search')");

        var entry = MakeEntry("lesson_search", "lessonSearch",
            status: MigrationStatus.NeedsReview, filePaths: file);

        var result = _svc.ApplyMigration(entry);

        Assert.That(result.Success, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    // 2. ApplyMigration — key rewriting
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void ApplyMigration_SimpleKey_RewritesCall()
    {
        // LearnSearch.tsx: t('lesson_search') → t('lessonSearch')
        var file = WriteFile("LearnSearch.tsx",
            "headerTitle: t('lesson_search'),");

        var entry = MakeEntry("lesson_search", "lessonSearch", filePaths: file);
        var result = _svc.ApplyMigration(entry);

        Assert.That(result.Success,       Is.True);
        Assert.That(result.UpdatedFiles,  Contains.Item(file));

        var updated = File.ReadAllText(file);
        Assert.That(updated, Does.Contain("t('lessonSearch')"));
        Assert.That(updated, Does.Not.Contain("t('lesson_search')"));
    }

    [Test]
    public void ApplyMigration_KeyWithExtraParams_PreservesParams()
    {
        // t('lesson_search', { ns: 'learn' }) → t('lessonSearch', { ns: 'learn' })
        var file = WriteFile("Navigator.tsx",
            "headerTitle: t('lesson_search', { ns: 'learn' }),");

        var entry = MakeEntry("lesson_search", "lessonSearch", filePaths: file);
        _svc.ApplyMigration(entry);

        var updated = File.ReadAllText(file);
        Assert.That(updated, Does.Contain("t('lessonSearch', { ns: 'learn' })"));
    }

    [Test]
    public void ApplyMigration_MultipleCallsSameFile_AllReplaced()
    {
        // lesson_search used twice (header + screen title)
        var file = WriteFile("Navigator.tsx",
            "headerTitle: t('lesson_search'),\ntitle: t('lesson_search'),");

        var entry = MakeEntry("lesson_search", "lessonSearch", filePaths: file);
        _svc.ApplyMigration(entry);

        var updated = File.ReadAllText(file);
        Assert.That(updated, Does.Not.Contain("t('lesson_search')"),
            "Every occurrence must be replaced");
        var count = System.Text.RegularExpressions.Regex
            .Matches(updated, @"t\('lessonSearch'\)").Count;
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void ApplyMigration_MultipleFiles_AllUpdated()
    {
        // Same old key used in two different screens (LearnSearch + Navigator)
        var file1 = WriteFile("LearnSearch.tsx",  "t('lesson_search')");
        var file2 = WriteFile("Navigator.tsx", "t('lesson_search', { ns: 'learn' })");

        var entry = MakeEntry("lesson_search", "lessonSearch",
            "learn", "learn", MigrationStatus.Pending, file1, file2);
        var result = _svc.ApplyMigration(entry);

        Assert.That(result.UpdatedFiles, Has.Count.EqualTo(2));
        Assert.That(File.ReadAllText(file1), Does.Contain("t('lessonSearch')"));
        Assert.That(File.ReadAllText(file2), Does.Contain("t('lessonSearch'"));
    }

    [Test]
    public void ApplyMigration_OtherKeysUnchanged()
    {
        // Only the targeted key should be modified; nearby keys must be left alone.
        var file = WriteFile("LearnSearch.tsx",
            "t('search_results') + t('lesson_search') + t('categories')");

        var entry = MakeEntry("lesson_search", "lessonSearch", filePaths: file);
        _svc.ApplyMigration(entry);

        var updated = File.ReadAllText(file);
        Assert.That(updated, Does.Contain("t('search_results')"),    "search_results must be unchanged");
        Assert.That(updated, Does.Contain("t('categories')"),         "categories must be unchanged");
        Assert.That(updated, Does.Contain("t('lessonSearch')"),       "lesson_search must be renamed");
    }

    [Test]
    public void ApplyMigration_StatusSetToAppliedOnSuccess()
    {
        var file  = WriteFile("Learn.tsx", "t('lesson_search')");
        var entry = MakeEntry("lesson_search", "lessonSearch", filePaths: file);

        _svc.ApplyMigration(entry);

        Assert.That(entry.Status, Is.EqualTo(MigrationStatus.Applied));
    }

    // ─────────────────────────────────────────────────────────────────
    // 3. ApplyMigration — namespace rewriting
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void ApplyMigration_NamespaceChanged_UpdatesUseTranslationHook()
    {
        // Moving 'cancel' from 'common' namespace to 'learn' namespace.
        // The useTranslation hook declaration must be updated.
        var file = WriteFile("LearnSearch.tsx",
            "const { t } = useTranslation('common');");

        var entry = MakeEntry("cancel", "cancel",
            oldNs: "common", newNs: "learn", filePaths: file);
        _svc.ApplyMigration(entry);

        var updated = File.ReadAllText(file);
        Assert.That(updated, Does.Contain("useTranslation('learn')"));
        Assert.That(updated, Does.Not.Contain("useTranslation('common')"));
    }

    [Test]
    public void ApplyMigration_NamespaceUnchanged_DoesNotTouchUseTranslation()
    {
        // When namespace stays the same, useTranslation must not be modified.
        var file = WriteFile("Learn.tsx",
            "const { t } = useTranslation('learn');\nt('lesson_search')");

        var entry = MakeEntry("lesson_search", "lessonSearch",
            oldNs: "learn", newNs: "learn", filePaths: file);
        _svc.ApplyMigration(entry);

        var updated = File.ReadAllText(file);
        Assert.That(updated, Does.Contain("useTranslation('learn')"),
            "Unchanged namespace declaration must be preserved exactly");
    }

    [Test]
    public void ApplyMigration_BothKeyAndNamespaceChanged_BothUpdated()
    {
        // lesson_search in common → lessonSearch in learn
        var file = WriteFile("Home.tsx",
            "const { t } = useTranslation('common');\n" +
            "return t('lesson_search');");

        var entry = MakeEntry("lesson_search", "lessonSearch",
            oldNs: "common", newNs: "learn", filePaths: file);
        _svc.ApplyMigration(entry);

        var updated = File.ReadAllText(file);
        Assert.That(updated, Does.Contain("useTranslation('learn')"));
        Assert.That(updated, Does.Contain("t('lessonSearch')"));
    }

    // ─────────────────────────────────────────────────────────────────
    // 4. CreateBackup
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void CreateBackup_CreatesTimestampedFile()
    {
        var file = WriteFile("Learn.tsx", "t('lesson_search')");

        _svc.CreateBackup(file);

        var backups = Directory.GetFiles(_tempDir, "*.backup");
        Assert.That(backups, Has.Length.EqualTo(1));
        Assert.That(backups[0], Does.Contain("Learn.tsx"));
    }

    [Test]
    public void CreateBackup_PreservesOriginalContent()
    {
        const string original = "const x = t('lesson_search');";
        var file = WriteFile("Learn.tsx", original);

        _svc.CreateBackup(file);

        var backups = Directory.GetFiles(_tempDir, "*.backup");
        Assert.That(File.ReadAllText(backups[0]), Is.EqualTo(original));
    }

    [Test]
    public void CreateBackup_NonExistentFile_DoesNotThrow()
    {
        Assert.DoesNotThrow(() =>
            _svc.CreateBackup(Path.Combine(_tempDir, "ghost.tsx")));
    }

    [Test]
    public void ApplyMigration_CreatesBackupBeforeWriting()
    {
        var file  = WriteFile("Learn.tsx", "t('lesson_search')");
        var entry = MakeEntry("lesson_search", "lessonSearch", filePaths: file);

        _svc.ApplyMigration(entry);

        var backups = Directory.GetFiles(_tempDir, "*.backup");
        Assert.That(backups, Has.Length.EqualTo(1),
            "A backup must be created before the file is modified");
    }

    // ─────────────────────────────────────────────────────────────────
    // 5. GenerateDiff
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GenerateDiff_ShowsOldAndNewKey()
    {
        var file = WriteFile("Learn.tsx", "t('lesson_search')");
        var entry = MakeEntry("lesson_search", "lessonSearch");

        var diff = _svc.GenerateDiff(file, entry);

        Assert.That(diff, Does.Contain("lesson_search"));
        Assert.That(diff, Does.Contain("lessonSearch"));
    }

    [Test]
    public void GenerateDiff_ShowsLineNumber()
    {
        // Put the key on line 3
        var file  = WriteFile("Learn.tsx", "// header\n// body\nt('lesson_search')");
        var entry = MakeEntry("lesson_search", "lessonSearch");

        var diff = _svc.GenerateDiff(file, entry);

        Assert.That(diff, Does.Contain("Line 3"),
            "Diff must show the correct 1-based line number");
    }

    [Test]
    public void GenerateDiff_ShowsNamespaceChange_WhenDifferent()
    {
        var file  = WriteFile("Learn.tsx",
            "const { t } = useTranslation('common');\nt('cancel')");
        var entry = MakeEntry("cancel", "cancel", oldNs: "common", newNs: "learn");

        var diff = _svc.GenerateDiff(file, entry);

        Assert.That(diff, Does.Contain("common"));
        Assert.That(diff, Does.Contain("learn"));
    }

    [Test]
    public void GenerateDiff_NoNamespaceSection_WhenSameNamespace()
    {
        var file  = WriteFile("Learn.tsx", "t('lesson_search')");
        var entry = MakeEntry("lesson_search", "lessonSearch",
            oldNs: "learn", newNs: "learn");

        var diff = _svc.GenerateDiff(file, entry);

        // The diff header "Old Namespace: X -> New Namespace: Y" must NOT appear
        Assert.That(diff, Does.Not.Contain("Old Namespace"),
            "Namespace section should be omitted when namespace is unchanged");
    }

    [Test]
    public void GenerateDiff_NonExistentFile_ReturnsEmpty()
    {
        var entry = MakeEntry("lesson_search", "lessonSearch");
        var diff  = _svc.GenerateDiff(Path.Combine(_tempDir, "ghost.tsx"), entry);

        Assert.That(diff, Is.Empty);
    }

    [Test]
    public void GenerateDiff_DoesNotModifyFile()
    {
        const string original = "t('lesson_search')";
        var file  = WriteFile("Learn.tsx", original);
        var entry = MakeEntry("lesson_search", "lessonSearch");

        _svc.GenerateDiff(file, entry);

        Assert.That(File.ReadAllText(file), Is.EqualTo(original),
            "GenerateDiff must be read-only — it must not write any changes");
    }

    // ─────────────────────────────────────────────────────────────────
    // 6. ApplyMigrations (batch)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void ApplyMigrations_SkipsAlreadyAppliedEntries()
    {
        var file = WriteFile("Learn.tsx", "t('lesson_search')");

        var applied = MakeEntry("lesson_search", "lessonSearch",
            status: MigrationStatus.Applied, filePaths: file);
        var pending = MakeEntry("search_results", "searchResults",
            filePaths: WriteFile("LearnSearch.tsx", "t('search_results')"));

        var results = _svc.ApplyMigrations([applied, pending]);

        // Only the Pending entry is processed
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Entry.OldKey, Is.EqualTo("search_results"));
    }

    [Test]
    public void ApplyMigrations_ProcessesPendingAndNeedsReview()
    {
        var f1 = WriteFile("A.tsx", "t('lesson_search')");
        var f2 = WriteFile("B.tsx", "t('categories')");

        var pending    = MakeEntry("lesson_search", "lessonSearch",
            status: MigrationStatus.Pending,    filePaths: f1);
        var needsReview = MakeEntry("categories", "categoryList",
            status: MigrationStatus.NeedsReview, filePaths: f2);

        var results = _svc.ApplyMigrations([pending, needsReview]);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results.TrueForAll(r => r.Success), Is.True);
    }

    [Test]
    public void ApplyMigrations_AllSkipped_ReturnsEmptyResults()
    {
        var skipped = MakeEntry("lesson_search", "lessonSearch",
            status: MigrationStatus.Skipped);
        var noMatch = MakeEntry("orphan", "", status: MigrationStatus.NoMatch);

        var results = _svc.ApplyMigrations([skipped, noMatch]);

        Assert.That(results, Is.Empty,
            "Skipped and NoMatch entries must not be processed by ApplyMigrations");
    }

    // ─────────────────────────────────────────────────────────────────
    // 7. Integration — real fury source file patterns
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void ApplyMigration_RealPatternFromLearnSearch_WorksCorrectly()
    {
        // Simulates the actual LearnSearch.tsx content pattern:
        //   useTranslation(['learn', 'common'])
        //   t('search_a_keyword') — plain call
        //   t('common:cancel')    — namespace-prefixed, unrelated key
        //   t('lesson_search', { ns: 'learn' }) — key being migrated with ns param
        var content =
            "const { t } = useTranslation('learn');\n" +
            "placeholder: t('search_a_keyword'),\n" +
            "cancelButtonText: t('common:cancel'),\n" +
            "headerTitle: t('lesson_search', { ns: 'learn' }),\n";

        var file  = WriteFile("LearnSearch.tsx", content);
        var entry = MakeEntry("lesson_search", "lessonSearch", filePaths: file);

        var result = _svc.ApplyMigration(entry);

        var updated = File.ReadAllText(file);
        Assert.That(result.Success,                            Is.True);
        Assert.That(updated, Does.Contain("t('lessonSearch', { ns: 'learn' })"));
        Assert.That(updated, Does.Contain("t('search_a_keyword')"),  "unrelated key unchanged");
        Assert.That(updated, Does.Contain("t('common:cancel')"),      "namespace-prefixed key unchanged");
        Assert.That(updated, Does.Contain("useTranslation('learn')"), "namespace unchanged when same");
    }

    [Test]
    public void ApplyMigration_RealPatternNamespaceMove_UpdatesBothHookAndCall()
    {
        // Simulates moving 'cancel' from 'common' JSON to 'learn' JSON.
        // The hook and the call must both be updated in the same pass.
        var content =
            "const { t } = useTranslation('common');\n" +
            "return (\n" +
            "  <Button title={t('cancel')} />\n" +
            ");";

        var file  = WriteFile("LearnSearch.tsx", content);
        var entry = MakeEntry("cancel", "cancel",
            oldNs: "common", newNs: "learn", filePaths: file);

        _svc.ApplyMigration(entry);

        var updated = File.ReadAllText(file);
        Assert.That(updated, Does.Contain("useTranslation('learn')"));
        Assert.That(updated, Does.Contain("t('cancel')"), "key itself stays 'cancel'");
    }
}
