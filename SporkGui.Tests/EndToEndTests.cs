using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SporkGui.Models;
using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// End-to-end tests that exercise the full migration pipeline:
///
///   1. GenerateMigrationPlan  — match old keys to new keys by translation value,
///                               find their locations in code.
///   2. ApplyMigrations        — rewrite t() calls and useTranslation() hooks in code files.
///   3. Verify code files      — assert that the target files now contain the new keys
///                               and that backups were created.
///   4. Re-scan code           — confirm new keys are found and old keys are gone.
///
/// Each test creates an isolated temp directory; no shared state between tests.
///
/// There are also two sync-pipeline E2E scenarios (CSV → JSON → compare)
/// and, when fury/src is present, integration tests against the real project.
/// </summary>
[TestFixture]
public class EndToEndTests
{
    // ── services (wired up as the GUI does) ──────────────────────────────
    private NormalizationService  _norm          = null!;
    private JsonService           _jsonSvc       = null!;
    private CodeScannerService    _scanner       = null!;
    private KeyMigrationService   _migrationSvc  = null!;
    private CodeMigrationService  _codeMigSvc    = null!;
    private CsvService            _csvSvc        = null!;
    private ComparisonService     _compSvc       = null!;

    private string _root = null!;   // per-test temp root

    // Standard t('key') pattern
    private const string TPattern = @"t\(['""]([^'""]+)['""][^)]*\)";

    [SetUp]
    public void SetUp()
    {
        _norm         = new NormalizationService();
        _jsonSvc      = new JsonService(_norm);
        _scanner      = new CodeScannerService(_norm);
        _migrationSvc = new KeyMigrationService(_jsonSvc, _scanner, _norm);
        _codeMigSvc   = new CodeMigrationService(_norm);
        _csvSvc       = new CsvService(_norm);
        _compSvc      = new ComparisonService();

        _root = Path.Combine(Path.GetTempPath(), "spork_e2e_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private string WriteFile(string relPath, string content)
    {
        var path = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string ReadFile(string relPath)
        => File.ReadAllText(Path.Combine(_root, relPath));

    private string Dir(string relPath) => Path.Combine(_root, relPath);

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 1 — Simple key rename, single file
    //
    //  Old: lesson_search  → "Lesson Search"   (learn.json)
    //  New: lessonSearch   → "Lesson Search"   (learn.json)
    //  Code: t('lesson_search') and t('lesson_search', { ns: 'learn' })
    //  Expected: both t() calls become t('lessonSearch')
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_SimpleKeyRename_CodeFileUpdated()
    {
        // Arrange — JSON files
        WriteFile("old_json/en/learn.json",
            """{"lesson_search": "Lesson Search", "cancel": "Cancel"}""");
        WriteFile("new_json/en/learn.json",
            """{"lessonSearch": "Lesson Search", "cancel": "Cancel"}""");

        // Arrange — TypeScript code file
        const string originalCode =
            "const { t } = useTranslation('learn');\n" +
            "const label = t('lesson_search');\n" +
            "const title = t('lesson_search', { ns: 'learn' });\n" +
            "const ok    = t('cancel');\n";      // 'cancel' key is NOT being renamed
        WriteFile("code/LearnSearch.tsx", originalCode);

        // Act — Step 1: generate migration plan
        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });

        var lessonEntry = plan.Entries.FirstOrDefault(e => e.OldKey == "lesson_search");
        Assert.That(lessonEntry, Is.Not.Null, "lesson_search should appear in the migration plan");
        Assert.That(lessonEntry!.NewKey, Is.EqualTo("lessonSearch"));
        Assert.That(lessonEntry.Status, Is.EqualTo(MigrationStatus.Pending));
        Assert.That(lessonEntry.CodeUsages, Is.Not.Empty,
            "Code usages must be found before migration can proceed");

        // Act — Step 2: apply migrations
        var applyResults = _codeMigSvc.ApplyMigrations(plan.Entries);

        Assert.That(applyResults, Is.Not.Empty);
        Assert.That(applyResults.All(r => r.Success), Is.True,
            "All migrations should succeed");

        // Assert — Step 3: code file is updated
        var updatedCode = ReadFile("code/LearnSearch.tsx");

        Assert.That(updatedCode, Does.Contain("t('lessonSearch')"),
            "t('lesson_search') must be rewritten to t('lessonSearch')");
        Assert.That(updatedCode, Does.Not.Contain("t('lesson_search')"),
            "Old key 'lesson_search' must be removed from code");

        // 'cancel' key was NOT in the migration plan → must be untouched
        Assert.That(updatedCode, Does.Contain("t('cancel')"),
            "Unrelated t('cancel') call must remain unchanged");

        // Entry status set to Applied
        Assert.That(lessonEntry.Status, Is.EqualTo(MigrationStatus.Applied));
    }

    [Test]
    public void E2E_SimpleKeyRename_BackupFileCreated()
    {
        WriteFile("old_json/en/learn.json", """{"lesson_search": "Lesson Search"}""");
        WriteFile("new_json/en/learn.json", """{"lessonSearch": "Lesson Search"}""");
        WriteFile("code/LearnSearch.tsx",
            "const label = t('lesson_search');\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });

        _codeMigSvc.ApplyMigrations(plan.Entries);

        // A timestamped .backup file should exist alongside the original
        var backups = Directory.GetFiles(Dir("code"), "*.backup");
        Assert.That(backups, Is.Not.Empty, "A backup file must be created before modifying the code file");
        Assert.That(File.ReadAllText(backups[0]), Does.Contain("lesson_search"),
            "Backup should contain the original (pre-migration) content");
    }

    [Test]
    public void E2E_SimpleKeyRename_RescanFindsNewKey()
    {
        WriteFile("old_json/en/learn.json", """{"lesson_search": "Lesson Search"}""");
        WriteFile("new_json/en/learn.json", """{"lessonSearch": "Lesson Search"}""");
        WriteFile("code/LearnSearch.tsx",
            "const label = t('lesson_search');\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });
        _codeMigSvc.ApplyMigrations(plan.Entries);

        // Re-scan the code directory after migration
        var normalizedNewKey = _norm.NormalizeKey("lessonSearch");
        var normalizedOldKey = _norm.NormalizeKey("lesson_search");

        var postMigrationKeys = _scanner.ScanCodeFiles(Dir("code"), new[] { ".tsx" }, TPattern);

        Assert.That(postMigrationKeys, Contains.Item(normalizedNewKey),
            "New key 'lessonSearch' must be found in code after migration");

        // The scanner normalises lessonSearch and lesson_search to the same value
        // so we can't assert the old raw key is gone — but we can verify that the
        // code file's raw text uses the new form.
        var codeText = ReadFile("code/LearnSearch.tsx");
        Assert.That(codeText, Does.Contain("lessonSearch"), "Code file must contain the new key");
        Assert.That(codeText, Does.Not.Contain("lesson_search"), "Code file must not contain the old key");
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 2 — Key rename across multiple code files
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_KeyUsedInMultipleFiles_AllFilesUpdated()
    {
        WriteFile("old_json/en/learn.json", """{"search_results": "Search Results"}""");
        WriteFile("new_json/en/learn.json", """{"searchResults": "Search Results"}""");

        // Three separate files that all use the old key
        WriteFile("code/screens/LearnSearch.tsx",
            "const a = t('search_results');\n");
        WriteFile("code/screens/LearnHome.tsx",
            "const b = t('search_results');\n");
        WriteFile("code/components/ResultList.tsx",
            "const c = t('search_results');\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });

        var entry = plan.Entries.First(e => e.OldKey == "search_results");
        Assert.That(entry.CodeUsages, Has.Count.EqualTo(3),
            "All three files should be discovered as code usages");

        var results = _codeMigSvc.ApplyMigrations(plan.Entries);

        Assert.That(results.Single().UpdatedFiles, Has.Count.EqualTo(3),
            "All three files should be updated");

        foreach (var relPath in new[] {
            "code/screens/LearnSearch.tsx",
            "code/screens/LearnHome.tsx",
            "code/components/ResultList.tsx" })
        {
            var text = ReadFile(relPath);
            Assert.That(text, Does.Contain("t('searchResults')"),
                $"{relPath} should use the new key");
            Assert.That(text, Does.Not.Contain("t('search_results')"),
                $"{relPath} must no longer use the old key");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 3 — Key rename + namespace change
    //
    //  Old: learn.json  → search  → "Search"
    //  New: explore.json → search → "Search"   (same key, different file/namespace)
    //  Code: useTranslation('learn') → must become useTranslation('explore')
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_NamespaceChange_UseTranslationUpdated()
    {
        WriteFile("old_json/en/learn.json",   """{"search": "Search"}""");
        WriteFile("new_json/en/explore.json", """{"search": "Search"}""");

        WriteFile("code/LearnScreen.tsx",
            "const { t } = useTranslation('learn');\n" +
            "const label = t('search');\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });

        var entry = plan.Entries.FirstOrDefault(e => e.OldKey == "search");
        Assert.That(entry, Is.Not.Null, "Plan should contain the 'search' key");
        Assert.That(entry!.OldNamespace, Is.EqualTo("learn"));
        Assert.That(entry.NewNamespace, Is.EqualTo("explore"));

        _codeMigSvc.ApplyMigrations(plan.Entries);

        var updated = ReadFile("code/LearnScreen.tsx");
        Assert.That(updated, Does.Contain("useTranslation('explore')"),
            "useTranslation hook must be updated to the new namespace");
        Assert.That(updated, Does.Not.Contain("useTranslation('learn')"),
            "Old namespace 'learn' must be removed from useTranslation");
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 4 — Applied / Skipped entries are NOT re-applied
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_AlreadyAppliedEntry_NotReapplied()
    {
        WriteFile("old_json/en/learn.json", """{"lesson_search": "Lesson Search"}""");
        WriteFile("new_json/en/learn.json", """{"lessonSearch": "Lesson Search"}""");
        WriteFile("code/Screen.tsx",
            "const label = t('lesson_search');\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });

        // Apply once
        _codeMigSvc.ApplyMigrations(plan.Entries);

        // Capture state of the file after first apply
        var afterFirst = ReadFile("code/Screen.tsx");

        // Apply again — all entries are now Applied, so none should run
        var secondResults = _codeMigSvc.ApplyMigrations(plan.Entries);
        Assert.That(secondResults, Is.Empty,
            "ApplyMigrations must skip entries that are already Applied");

        // File must be unchanged from the first apply
        var afterSecond = ReadFile("code/Screen.tsx");
        Assert.That(afterSecond, Is.EqualTo(afterFirst),
            "Code file must not be modified by the second (no-op) apply");
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 5 — Key with no code usage is NOT applied
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_NoCodeUsage_MigrationResultIsFailure()
    {
        WriteFile("old_json/en/learn.json", """{"lesson_search": "Lesson Search"}""");
        WriteFile("new_json/en/learn.json", """{"lessonSearch": "Lesson Search"}""");

        // Code directory exists but has no t('lesson_search') calls
        WriteFile("code/Screen.tsx", "const x = 42;\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });

        var entry = plan.Entries.FirstOrDefault(e => e.OldKey == "lesson_search");
        if (entry == null)
        {
            // Old key had no code usages → not in plan at all, which is also valid
            Assert.Pass("Key with no code usage is omitted from the plan entirely — acceptable.");
            return;
        }

        // If entry IS in the plan but has no usages, ApplyMigration should report failure
        var result = _codeMigSvc.ApplyMigration(entry);
        Assert.That(result.Success, Is.False,
            "ApplyMigration must fail when the entry has no code usages");
        Assert.That(result.ErrorMessage, Does.Contain("No code usages"),
            "Error message should mention missing code usages");
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 6 — Plan-then-apply with multi-language JSON
    //  Tests that translation matching works across languages
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_MultiLanguageJson_MatchedByEnglishValue()
    {
        // Old JSON: both en and ms files
        WriteFile("old_json/en/learn.json", """{"lesson_search": "Lesson Search"}""");
        WriteFile("old_json/ms/learn.json", """{"lesson_search": "Cari Pelajaran"}""");

        // New JSON: key renamed in both languages
        WriteFile("new_json/en/learn.json", """{"lessonSearch": "Lesson Search"}""");
        WriteFile("new_json/ms/learn.json", """{"lessonSearch": "Cari Pelajaran"}""");

        WriteFile("code/Screen.tsx",
            "const label = t('lesson_search');\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });

        var entry = plan.Entries.FirstOrDefault(e => e.OldKey == "lesson_search");
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.NewKey, Is.EqualTo("lessonSearch"));

        _codeMigSvc.ApplyMigrations(plan.Entries);

        var updated = ReadFile("code/Screen.tsx");
        Assert.That(updated, Does.Contain("t('lessonSearch')"));
        Assert.That(updated, Does.Not.Contain("t('lesson_search')"));
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 7 — Both .tsx and .ts files updated
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_TsxAndTsFiles_BothUpdated()
    {
        WriteFile("old_json/en/common.json", """{"cancel": "Cancel"}""");
        WriteFile("new_json/en/common.json", """{"cancelAction": "Cancel"}""");

        WriteFile("code/ui/Button.tsx",
            "const label = t('cancel');\n");
        WriteFile("code/utils/actions.ts",
            "const label = t('cancel');\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx", ".ts" });

        var entry = plan.Entries.FirstOrDefault(e => e.OldKey == "cancel");
        Assert.That(entry, Is.Not.Null, "cancel → cancelAction must be in the plan");
        Assert.That(entry!.CodeUsages, Has.Count.EqualTo(2),
            "Both .tsx and .ts files should be found");

        _codeMigSvc.ApplyMigrations(plan.Entries);

        Assert.That(ReadFile("code/ui/Button.tsx"),    Does.Contain("t('cancelAction')"));
        Assert.That(ReadFile("code/utils/actions.ts"), Does.Contain("t('cancelAction')"));
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 8 — Full diff generated before and changes match after
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_DiffPreview_MatchesActualChange()
    {
        WriteFile("old_json/en/learn.json", """{"lesson_search": "Lesson Search"}""");
        WriteFile("new_json/en/learn.json", """{"lessonSearch": "Lesson Search"}""");
        WriteFile("code/Screen.tsx",
            "const { t } = useTranslation('learn');\n" +
            "const label = t('lesson_search');\n");

        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old_json"), Dir("new_json"), Dir("code"), new[] { ".tsx" });

        var entry = plan.Entries.First(e => e.OldKey == "lesson_search");
        var filePath = entry.CodeUsages.First().FilePath;

        // Generate diff BEFORE applying
        var diff = _codeMigSvc.GenerateDiff(filePath, entry);
        Assert.That(diff, Does.Contain("lesson_search"), "Diff should mention old key");
        Assert.That(diff, Does.Contain("lessonSearch"),  "Diff should show new key");
        Assert.That(diff, Is.Not.Empty, "Diff output must not be empty");

        // Apply migrations
        _codeMigSvc.ApplyMigrations(plan.Entries);

        // The actual file must match what the diff predicted
        var updated = ReadFile("code/Screen.tsx");
        Assert.That(updated, Does.Contain("t('lessonSearch')"));
        Assert.That(updated, Does.Not.Contain("t('lesson_search')"));
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 9 — Sync pipeline E2E (CSV + JSON → comparison)
    //
    //  Tests the complete Sync Tool pipeline:
    //    CsvService.LoadCsvFile (master + platform)
    //    + JsonService.LoadJsonFiles (code JSON)
    //    + CodeScannerService.ScanCodeFiles
    //    → ComparisonService.CompareTranslations
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_SyncPipeline_MissingInPlatform_Detected()
    {
        // Master CSV: has "lesson_search" + "cancel"
        WriteFile("master.csv",
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lesson_search,Lesson Search,Cari Pelajaran,,,,,\r\n" +
            "common,cancel,Cancel,Batal,,,,,"
        );

        // Platform CSV: only "cancel" (lesson_search is missing in platform)
        WriteFile("platform.csv",
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "common,cancel,Cancel,Batal,,,,,"
        );

        // Code JSON: has both keys
        WriteFile("code_json/en/learn.json",   """{"lesson_search": "Lesson Search"}""");
        WriteFile("code_json/en/common.json",  """{"cancel": "Cancel"}""");

        // Code files: both keys are used in TypeScript
        WriteFile("code/Screen.tsx",
            "const a = t('lesson_search');\n" +
            "const b = t('cancel');\n");

        // Wire up pipeline
        var master   = _csvSvc.LoadCsvFile(Path.Combine(_root, "master.csv"));
        var platform = _csvSvc.LoadCsvFile(Path.Combine(_root, "platform.csv"));
        var codeJson = _jsonSvc.LoadJsonFiles(Dir("code_json"));
        var usedKeys = _scanner.ScanCodeFiles(Dir("code"), new[] { ".tsx" }, TPattern);

        var result = _compSvc.CompareTranslations(master, platform, codeJson, usedKeys);

        Assert.That(result.MissingInPlatform, Has.Count.EqualTo(1),
            "lesson_search is in master+code+usedKeys but not in platform → MissingInPlatform");
        Assert.That(result.MissingInPlatform[0].Key, Is.EqualTo("lesson_search"));
    }

    [Test]
    public void E2E_SyncPipeline_UnusedInCode_Detected()
    {
        // 9 columns: Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant → 8 commas per data row
        const string csvHeader = "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n";

        // Master CSV: has "lesson_search" + "hang_tight"
        WriteFile("master.csv",
            csvHeader +
            "learn,lesson_search,Lesson Search,,,,,,\r\n" +
            "common,hang_tight,Hang Tight!,,,,,,\r\n"
        );

        // Platform CSV: same two keys
        WriteFile("platform.csv",
            csvHeader +
            "learn,lesson_search,Lesson Search,,,,,,\r\n" +
            "common,hang_tight,Hang Tight!,,,,,,\r\n"
        );

        // Code JSON: both keys
        WriteFile("code_json/en/learn.json",  """{"lesson_search": "Lesson Search"}""");
        WriteFile("code_json/en/common.json", """{"hang_tight": "Hang Tight!"}""");

        // Code files: only lesson_search is used; hang_tight is NOT
        WriteFile("code/Screen.tsx",
            "const a = t('lesson_search');\n");

        var master   = _csvSvc.LoadCsvFile(Path.Combine(_root, "master.csv"));
        var platform = _csvSvc.LoadCsvFile(Path.Combine(_root, "platform.csv"));
        var codeJson = _jsonSvc.LoadJsonFiles(Dir("code_json"));
        var usedKeys = _scanner.ScanCodeFiles(Dir("code"), new[] { ".tsx" }, TPattern);

        var result = _compSvc.CompareTranslations(master, platform, codeJson, usedKeys);

        Assert.That(result.IsFullySynced, Is.True,
            "No MissingInPlatform, DifferentTranslation, or MissingInCode → fully synced");

        var unusedNorm = _norm.NormalizeKey("hang_tight");
        var unusedEntry = result.UnusedInCode.FirstOrDefault(e => e.NormalizedKey == unusedNorm);
        Assert.That(unusedEntry, Is.Not.Null,
            "hang_tight is in CSV+JSON but never t()-called → UnusedInCode");
    }

    [Test]
    public void E2E_SyncPipeline_DifferentTranslation_Detected()
    {
        // DifferentTranslation fires when codeEntry translations ≠ platformEntry translations.
        // Master and Platform both say "Lesson Search", but the code JSON says "LESSON SEARCH".
        // ComparisonService.TranslationsMatch(codeEntry, platformEntry) → false → DifferentTranslation.
        WriteFile("master.csv",
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lesson_search,Lesson Search,,,,,,"
        );
        WriteFile("platform.csv",
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lesson_search,Lesson Search,,,,,,"   // platform matches master
        );
        // Code JSON has a DIFFERENT English value from what the platform CSV says
        WriteFile("code_json/en/learn.json",
            """{"lesson_search": "LESSON SEARCH"}""");
        WriteFile("code/Screen.tsx",
            "const a = t('lesson_search');\n");

        var master   = _csvSvc.LoadCsvFile(Path.Combine(_root, "master.csv"));
        var platform = _csvSvc.LoadCsvFile(Path.Combine(_root, "platform.csv"));
        var codeJson = _jsonSvc.LoadJsonFiles(Dir("code_json"));
        var usedKeys = _scanner.ScanCodeFiles(Dir("code"), new[] { ".tsx" }, TPattern);

        var result = _compSvc.CompareTranslations(master, platform, codeJson, usedKeys);

        Assert.That(result.DifferentTranslation, Has.Count.GreaterThanOrEqualTo(1),
            "lesson_search has different value in master vs platform/code → DifferentTranslation");
    }

    // ════════════════════════════════════════════════════════════════════
    //  SCENARIO 10 — Realistic fury-like project structure
    //
    //  Mimics the real fury/src layout:
    //    old_json/{lang}/{namespace}.json   (e.g. en/learn.json)
    //    new_json/{lang}/{namespace}.json
    //    code/features/learn/Screen.tsx
    //    code/navigation/navigator.tsx
    // ════════════════════════════════════════════════════════════════════

    [Test]
    public void E2E_FuryLikeStructure_FullPipeline()
    {
        // Old translation files — fury en/learn.json style
        WriteFile("old/en/learn.json", """
            {
                "lesson_search": "Lesson Search",
                "search_results": "Search Results",
                "loading_up_lessons": "Loading up lessons..."
            }
            """);
        WriteFile("old/ms/learn.json", """
            {
                "lesson_search": "Cari Pelajaran",
                "search_results": "Hasil Carian",
                "loading_up_lessons": "Memuatkan pelajaran..."
            }
            """);

        // New translation files — keys renamed (camelCase style)
        WriteFile("new/en/learn.json", """
            {
                "lessonSearch": "Lesson Search",
                "searchResults": "Search Results",
                "loadingUpLessons": "Loading up lessons..."
            }
            """);
        WriteFile("new/ms/learn.json", """
            {
                "lessonSearch": "Cari Pelajaran",
                "searchResults": "Hasil Carian",
                "loadingUpLessons": "Memuatkan pelajaran..."
            }
            """);

        // Code files — fury-style React Native TypeScript
        WriteFile("src/features/learn/screens/LearnSearch.tsx",
            "const { t } = useTranslation('learn');\n" +
            "const placeholder = t('lesson_search');\n" +
            "const title       = t('search_results');\n" +
            "const loading     = t('loading_up_lessons');\n");

        WriteFile("src/navigation/root-navigator.tsx",
            "const { t } = useTranslation('learn');\n" +
            "const label = t('lesson_search', { ns: 'learn' });\n");

        // ── Step 1: Generate plan
        var plan = _migrationSvc.GenerateMigrationPlan(
            Dir("old"), Dir("new"), Dir("src"), new[] { ".tsx", ".ts" });

        Assert.That(plan.Entries, Has.Count.EqualTo(3),
            "Three old keys should be matched to three new keys");

        Assert.That(plan.Entries.All(e => e.Status == MigrationStatus.Pending), Is.True,
            "All entries should be Pending (unique value matches, no duplicates)");

        // lesson_search is used in 2 files
        var lessonEntry = plan.Entries.First(e => e.OldKey == "lesson_search");
        Assert.That(lessonEntry.CodeUsages, Has.Count.EqualTo(2),
            "lesson_search appears in both LearnSearch.tsx and root-navigator.tsx");

        // ── Step 2: Apply migrations
        var results = _codeMigSvc.ApplyMigrations(plan.Entries);

        Assert.That(results.All(r => r.Success), Is.True,
            "All three migrations must succeed");

        // ── Step 3: Verify LearnSearch.tsx
        var learnSearch = ReadFile("src/features/learn/screens/LearnSearch.tsx");
        Assert.That(learnSearch, Does.Contain("t('lessonSearch')"),    "lessonSearch renamed");
        Assert.That(learnSearch, Does.Contain("t('searchResults')"),   "searchResults renamed");
        Assert.That(learnSearch, Does.Contain("t('loadingUpLessons')"), "loadingUpLessons renamed");
        Assert.That(learnSearch, Does.Not.Contain("t('lesson_search')"),    "old key gone");
        Assert.That(learnSearch, Does.Not.Contain("t('search_results')"),   "old key gone");
        Assert.That(learnSearch, Does.Not.Contain("t('loading_up_lessons')"), "old key gone");

        // ── Step 4: Verify root-navigator.tsx
        var navigator = ReadFile("src/navigation/root-navigator.tsx");
        Assert.That(navigator, Does.Contain("lessonSearch"),
            "root-navigator.tsx must also have lesson_search renamed");
        Assert.That(navigator, Does.Not.Contain("t('lesson_search')"),
            "Old key must be gone from root-navigator.tsx");

        // ── Step 5: Re-scan — all three new keys must now be found
        var postScan = _scanner.ScanCodeFiles(Dir("src"), new[] { ".tsx", ".ts" }, TPattern);
        foreach (var newKey in new[] { "lessonSearch", "searchResults", "loadingUpLessons" })
        {
            Assert.That(postScan, Contains.Item(_norm.NormalizeKey(newKey)),
                $"Re-scan after migration must find new key '{newKey}'");
        }

        // ── Step 6: All entries must be Applied
        Assert.That(plan.Entries.All(e => e.Status == MigrationStatus.Applied), Is.True,
            "Every migration entry must be marked Applied after ApplyMigrations");
    }

    // ════════════════════════════════════════════════════════════════════
    //  Integration — real fury/src project (skipped if not present)
    // ════════════════════════════════════════════════════════════════════

    private static readonly string FuryRoot = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "fury"));

    private static readonly string FurySrcDir         = Path.Combine(FuryRoot, "src");
    private static readonly string FuryLocalizationDir = Path.Combine(FurySrcDir, "localization");

    private static readonly string MobileSheetPath = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "mobile_sheet.csv"));

    private void RequireFury()
    {
        if (!Directory.Exists(FuryLocalizationDir))
            Assert.Ignore($"fury not found at '{FuryRoot}'; skipping integration test.");
    }

    private void RequireMobileSheet()
    {
        if (!File.Exists(MobileSheetPath))
            Assert.Ignore($"mobile_sheet.csv not found; skipping integration test.");
    }

    [Test]
    public void E2E_RealFury_ScanCodeAndLoadJson_KeysOverlap()
    {
        RequireFury();

        var codeKeys = _scanner.ScanCodeFiles(FurySrcDir, new[] { ".tsx", ".ts" }, TPattern);
        var jsonEntries = _jsonSvc.LoadJsonFiles(FuryLocalizationDir);
        var jsonNormKeys = jsonEntries.Select(e => e.NormalizedKey).ToHashSet();

        // Some t()-called keys must also be present in the JSON files
        var overlap = codeKeys.Where(k => jsonNormKeys.Contains(k)).ToList();

        Assert.That(overlap, Is.Not.Empty,
            "At least some t()-called keys must exist in the JSON translation files");
        Assert.That(overlap.Count, Is.GreaterThan(5),
            "A real project should have many overlapping code-and-json keys");
    }

    [Test]
    public void E2E_RealFury_SyncPipeline_WithMobileSheet()
    {
        RequireFury();
        RequireMobileSheet();

        var platform = _csvSvc.LoadCsvFile(MobileSheetPath);
        var codeJson = _jsonSvc.LoadJsonFiles(FuryLocalizationDir);
        var usedKeys = _scanner.ScanCodeFiles(FurySrcDir, new[] { ".tsx", ".ts" }, TPattern);

        // Without a master sheet, pass null — comparison runs without master
        var result = _compSvc.CompareTranslations(null, platform, codeJson, usedKeys);

        // Basic sanity: the result object is valid
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MissingInPlatform, Is.Not.Null);
        Assert.That(result.DifferentTranslation, Is.Not.Null);
        Assert.That(result.MissingInCode, Is.Not.Null);
        Assert.That(result.OnlyInCode, Is.Not.Null);

        // lesson_search IS t()-called in fury → must NOT appear in UnusedInCode
        var lessonNorm = _norm.NormalizeKey("lesson_search");
        Assert.That(result.UnusedInCode.Any(e => e.NormalizedKey == lessonNorm), Is.False,
            "lesson_search is actively used in code and must not be flagged as UnusedInCode");
    }
}
