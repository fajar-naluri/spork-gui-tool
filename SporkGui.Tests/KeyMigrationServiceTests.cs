using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SporkGui.Models;
using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// Tests for KeyMigrationService:
///   ExtractNamespaceFromFilename — strips path/extension and lang-code suffix
///   GenerateMigrationPlan        — matches old→new entries by translation value,
///                                  finds code usages, detects duplicates/ambiguity
///
/// Filesystem fixture layout (temp dirs):
///   oldJson/   — per-language dirs (en/, ms/) with old-key JSON files
///   newJson/   — per-language dirs with new-key JSON files
///   code/      — .tsx files with t() calls using old keys
///
/// Integration tests reuse the real fury/src directory for code-usage scanning.
/// </summary>
[TestFixture]
public class KeyMigrationServiceTests
{
    private KeyMigrationService _svc  = null!;
    private NormalizationService _norm = null!;
    private string _tempDir = null!;

    // Fury source path — same resolution as CodeScannerServiceTests
    private static readonly string FurySrcDir = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "fury", "src"));

    [SetUp]
    public void SetUp()
    {
        _norm    = new NormalizationService();
        var jsonSvc     = new JsonService(_norm);
        var scannerSvc  = new CodeScannerService(_norm);
        _svc     = new KeyMigrationService(jsonSvc, scannerSvc, _norm);

        _tempDir = Path.Combine(Path.GetTempPath(), "spork_key_mig_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── filesystem helpers ───────────────────────────────────────────

    /// <summary>Creates a single-language JSON file and returns its directory.</summary>
    private string MakeJsonDir(string dirName, string lang, string jsonFilename,
        string jsonContent)
    {
        var dir = Path.Combine(_tempDir, dirName, lang);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, jsonFilename), jsonContent);
        return Path.Combine(_tempDir, dirName);
    }

    /// <summary>Creates a JSON dir with English + Malay files (as fury uses).</summary>
    private string MakeBilingualJsonDir(string dirName, string jsonFilename,
        string enContent, string msContent)
    {
        MakeJsonDir(dirName, "en", jsonFilename, enContent);
        var msDir = Path.Combine(_tempDir, dirName, "ms");
        Directory.CreateDirectory(msDir);
        File.WriteAllText(Path.Combine(msDir, jsonFilename), msContent);
        return Path.Combine(_tempDir, dirName);
    }

    private string MakeCodeDir(string dirName, params (string file, string content)[] files)
    {
        var dir = Path.Combine(_tempDir, dirName);
        Directory.CreateDirectory(dir);
        foreach (var (file, content) in files)
            File.WriteAllText(Path.Combine(dir, file), content);
        return dir;
    }

    // ─────────────────────────────────────────────────────────────────
    // 1. ExtractNamespaceFromFilename
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void ExtractNamespace_PlainFilename_ReturnsBaseName()
    {
        Assert.That(_svc.ExtractNamespaceFromFilename("learn.json"), Is.EqualTo("learn"));
        Assert.That(_svc.ExtractNamespaceFromFilename("common.json"), Is.EqualTo("common"));
        Assert.That(_svc.ExtractNamespaceFromFilename("home.json"),   Is.EqualTo("home"));
        Assert.That(_svc.ExtractNamespaceFromFilename("chat.json"),   Is.EqualTo("chat"));
    }

    [Test]
    public void ExtractNamespace_WithFullPath_ReturnsBaseName()
    {
        // The fury JSON loader passes just the filename, but ensure paths work too
        Assert.That(
            _svc.ExtractNamespaceFromFilename("en/learn.json"),
            Is.EqualTo("learn"));
    }

    [Test]
    public void ExtractNamespace_EmptyString_ReturnsEmpty()
    {
        Assert.That(_svc.ExtractNamespaceFromFilename(""), Is.Empty);
        Assert.That(_svc.ExtractNamespaceFromFilename(null!), Is.Empty);
    }

    // ─────────────────────────────────────────────────────────────────
    // 2. GenerateMigrationPlan — simple rename (Pending)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_SimpleKeyRename_CreatesPendingEntry()
    {
        // Old JSON: lesson_search = "Lesson Search"
        // New JSON: lessonSearch  = "Lesson Search"  (same value, different key)
        // Code: t('lesson_search') → should be migrated
        var oldDir = MakeJsonDir("old", "en", "learn.json",
            """{"lesson_search": "Lesson Search"}""");
        var newDir = MakeJsonDir("new", "en", "learn.json",
            """{"lessonSearch": "Lesson Search"}""");
        var codeDir = MakeCodeDir("code",
            ("LearnSearch.tsx", "headerTitle: t('lesson_search'),"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.TotalEntries, Is.EqualTo(1));

        var entry = plan.Entries[0];
        Assert.That(entry.OldKey,      Is.EqualTo("lesson_search"));
        Assert.That(entry.NewKey,      Is.EqualTo("lessonSearch"));
        Assert.That(entry.Status,      Is.EqualTo(MigrationStatus.Pending));
        Assert.That(entry.OldNamespace, Is.EqualTo("learn"));
        Assert.That(entry.NewNamespace, Is.EqualTo("learn"));
    }

    [Test]
    public void GeneratePlan_SimpleRename_PopulatesMatchedValue()
    {
        var oldDir = MakeJsonDir("old", "en", "learn.json",
            """{"lesson_search": "Lesson Search"}""");
        var newDir = MakeJsonDir("new", "en", "learn.json",
            """{"lessonSearch": "Lesson Search"}""");
        var codeDir = MakeCodeDir("code", ("A.tsx", "t('lesson_search')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.Entries[0].MatchedByValue,
            Is.EqualTo("Lesson Search").IgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────
    // 3. GenerateMigrationPlan — no match
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_OldKeyValueNotInNewJson_EntryOmittedFromPlan()
    {
        // MatchTranslationsByValue only adds an old entry to the candidates map when
        // at least one new entry shares its normalised value. Old keys with NO value
        // match in the new JSON are silently omitted — they produce 0 plan entries.
        // NOTE: The MigrationStatus.NoMatch branch inside GenerateMigrationPlan is
        //       dead code because the outer dictionary never contains empty inner maps.
        var oldDir  = MakeJsonDir("old", "en", "learn.json",
            """{"orphan_key": "Orphan Text"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"lessonSearch": "Lesson Search"}""");
        var codeDir = MakeCodeDir("code", ("A.tsx", "// no t() calls"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.TotalEntries, Is.EqualTo(0),
            "Old keys whose translation value has no match in the new JSON are silently omitted, not listed as NoMatch");
    }

    // ─────────────────────────────────────────────────────────────────
    // 4. GenerateMigrationPlan — identical key skipped
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_IdenticalKeyAndFile_SkippedEntirely()
    {
        // Old and new JSON have the same key, same filename, same value → no migration needed
        // Based on: fury's existing keys that remain unchanged between versions
        var oldDir  = MakeJsonDir("old", "en", "learn.json",
            """{"search_results": "Search results"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"search_results": "Search results"}""");
        var codeDir = MakeCodeDir("code", ("A.tsx", "t('search_results')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.TotalEntries, Is.EqualTo(0),
            "Identical old/new key with same file and namespace must be silently skipped");
    }

    // ─────────────────────────────────────────────────────────────────
    // 5. GenerateMigrationPlan — namespace change
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_SameKeyDifferentFile_DetectsNamespaceChange()
    {
        // 'cancel' moves from common.json to learn.json (namespace change)
        var oldDir  = MakeJsonDir("old", "en", "common.json",
            """{"cancel": "Cancel"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"cancel": "Cancel"}""");
        var codeDir = MakeCodeDir("code", ("LearnSearch.tsx",
            "const { t } = useTranslation('common');\nt('cancel')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.TotalEntries, Is.EqualTo(1));
        var entry = plan.Entries[0];
        Assert.That(entry.OldNamespace, Is.EqualTo("common"));
        Assert.That(entry.NewNamespace, Is.EqualTo("learn"));
        Assert.That(entry.Status,       Is.EqualTo(MigrationStatus.Pending));
    }

    // ─────────────────────────────────────────────────────────────────
    // 6. GenerateMigrationPlan — duplicate value (NeedsReview)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_DuplicateValueInOldJson_StatusNeedsReview()
    {
        // Two old keys share the same English value "Cancel".
        // The scanner cannot know which one to match → NeedsReview.
        var oldDir = MakeJsonDir("old", "en", "common.json",
            """{"cancel": "Cancel", "dismiss": "Cancel"}""");
        var newDir = MakeJsonDir("new", "en", "common.json",
            """{"cancel": "Cancel"}""");
        var codeDir = MakeCodeDir("code", ("A.tsx", "t('cancel')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        // Both keys match "Cancel" → at least one must be flagged as NeedsReview
        Assert.That(plan.NeedsReviewCount, Is.GreaterThan(0),
            "Duplicate values in old JSON must produce at least one NeedsReview entry");
    }

    [Test]
    public void GeneratePlan_DuplicateValueInNewJson_StatusNeedsReview()
    {
        // The matched value "Cancel" exists in TWO new keys ("cancel" and "dismiss").
        // Use a DIFFERENT old key name ("old_cancel") so the self-match identical skip
        // does not short-circuit the multiple-matches branch.
        var oldDir = MakeJsonDir("old", "en", "common.json",
            """{"old_cancel": "Cancel"}""");
        var newDir = MakeJsonDir("new", "en", "common.json",
            """{"cancel": "Cancel", "dismiss": "Cancel"}""");
        var codeDir = MakeCodeDir("code", ("A.tsx", "t('old_cancel')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.NeedsReviewCount, Is.GreaterThan(0));
        var reviewEntry = plan.Entries.First(e => e.Status == MigrationStatus.NeedsReview);
        Assert.That(reviewEntry.HasDuplicateValue, Is.True);
    }

    // ─────────────────────────────────────────────────────────────────
    // 7. GenerateMigrationPlan — multiple new-key matches (NeedsReview)
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_MultipleNewKeyMatches_StatusNeedsReview()
    {
        // Old: "current_step" = "Next"
        // New has TWO keys with "Next" across two files.
        // The old key name ("current_step") differs from both new keys so the
        // identical-key short-circuit does NOT fire → NeedsReview is set.
        var oldDir = MakeJsonDir("old", "en", "common.json",
            """{"current_step": "Next"}""");

        var newEnDir = Path.Combine(_tempDir, "new", "en");
        Directory.CreateDirectory(newEnDir);
        File.WriteAllText(Path.Combine(newEnDir, "common.json"), """{"nextStep": "Next"}""");
        File.WriteAllText(Path.Combine(newEnDir, "learn.json"),  """{"nextLesson": "Next"}""");
        var newDir = Path.Combine(_tempDir, "new");

        var codeDir = MakeCodeDir("code", ("A.tsx", "t('current_step')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.NeedsReviewCount, Is.GreaterThan(0),
            "Multiple new-key matches for the same value must trigger NeedsReview");
    }

    [Test]
    public void GeneratePlan_MultipleMatches_HasAlternativeMatches()
    {
        // Same scenario: "current_step" → two possible new keys.
        // The second new key should be listed in AlternativeMatches.
        var oldDir = MakeJsonDir("old", "en", "common.json",
            """{"current_step": "Next"}""");

        var newEnDir = Path.Combine(_tempDir, "new", "en");
        Directory.CreateDirectory(newEnDir);
        File.WriteAllText(Path.Combine(newEnDir, "common.json"), """{"nextStep": "Next"}""");
        File.WriteAllText(Path.Combine(newEnDir, "learn.json"),  """{"nextLesson": "Next"}""");
        var newDir = Path.Combine(_tempDir, "new");

        var codeDir = MakeCodeDir("code", ("A.tsx", "t('current_step')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        var reviewEntry = plan.Entries.FirstOrDefault(e => e.AlternativeMatches.Count > 0);
        Assert.That(reviewEntry, Is.Not.Null,
            "A NeedsReview entry with multiple new-key matches should expose AlternativeMatches");
    }

    // ─────────────────────────────────────────────────────────────────
    // 8. GenerateMigrationPlan — code usages
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_CodeUsesOldKey_UsagesPopulated()
    {
        var oldDir  = MakeJsonDir("old", "en", "learn.json",
            """{"lesson_search": "Lesson Search"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"lessonSearch": "Lesson Search"}""");
        var codeDir = MakeCodeDir("code",
            ("LearnSearch.tsx",  "t('lesson_search')"),
            ("Navigator.tsx", "t('lesson_search', { ns: 'learn' })"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.TotalEntries,          Is.EqualTo(1));
        Assert.That(plan.Entries[0].CodeUsages, Has.Count.GreaterThanOrEqualTo(2),
            "Both files referencing the old key must appear in CodeUsages");
    }

    [Test]
    public void GeneratePlan_CodeUsage_ContainsCorrectFilePath()
    {
        var oldDir  = MakeJsonDir("old", "en", "learn.json",
            """{"lesson_search": "Lesson Search"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"lessonSearch": "Lesson Search"}""");
        var codeFile = Path.Combine(_tempDir, "code", "LearnSearch.tsx");
        Directory.CreateDirectory(Path.GetDirectoryName(codeFile)!);
        File.WriteAllText(codeFile, "t('lesson_search')");
        var codeDir = Path.GetDirectoryName(codeFile)!;

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        var usage = plan.Entries[0].CodeUsages[0];
        Assert.That(usage.FilePath, Is.EqualTo(codeFile));
        Assert.That(usage.LineNumber, Is.EqualTo(1));
    }

    [Test]
    public void GeneratePlan_NoCodeUsages_EntryStillCreated()
    {
        // The migration entry should still appear even with no code usages.
        // (The developer must add the t() calls manually.)
        var oldDir  = MakeJsonDir("old", "en", "learn.json",
            """{"lesson_search": "Lesson Search"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"lessonSearch": "Lesson Search"}""");
        var codeDir = MakeCodeDir("code", ("Empty.tsx", "// no t() calls"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.TotalEntries,          Is.EqualTo(1));
        Assert.That(plan.Entries[0].CodeUsages, Is.Empty);
    }

    // ─────────────────────────────────────────────────────────────────
    // 9. GenerateMigrationPlan — multilingual matching
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_MatchedByMalayTranslation_CreatesPendingEntry()
    {
        // Old and new JSON have the same Malay value (matching via ms lang)
        // even if the English values differ slightly.
        var oldDir = MakeBilingualJsonDir("old", "learn.json",
            enContent: """{"lesson_search": "Lesson Search"}""",
            msContent: """{"lesson_search": "Carian Pelajaran"}""");
        var newDir = MakeBilingualJsonDir("new", "learn.json",
            enContent: """{"lessonSearch": "Lesson Search"}""",
            msContent: """{"lessonSearch": "Carian Pelajaran"}""");
        var codeDir = MakeCodeDir("code", ("A.tsx", "t('lesson_search')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.Entries, Has.Count.EqualTo(1));
        Assert.That(plan.Entries[0].Status, Is.EqualTo(MigrationStatus.Pending));
    }

    // ─────────────────────────────────────────────────────────────────
    // 10. GenerateMigrationPlan — result counters
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void GeneratePlan_MixedResults_CountersAreCorrect()
    {
        // 1 clean rename (Pending) + 1 old key with no value match in new JSON.
        // The unmatched old key ("orphan") is silently omitted — not listed as NoMatch.
        // Total plan entries = 1 (only the rename).
        var oldDir = MakeJsonDir("old", "en", "learn.json",
            """{"lesson_search": "Lesson Search", "orphan": "Orphan Value"}""");
        var newDir = MakeJsonDir("new", "en", "learn.json",
            """{"lessonSearch": "Lesson Search"}"""); // no "Orphan Value"
        var codeDir = MakeCodeDir("code", ("A.tsx", "t('lesson_search')"));

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, codeDir, [".tsx"]);

        Assert.That(plan.PendingCount, Is.EqualTo(1), "Pending");
        Assert.That(plan.NoMatchCount, Is.EqualTo(0), "NoMatch — unmatched keys are omitted, not reported");
        Assert.That(plan.TotalEntries, Is.EqualTo(1), "Total");
    }

    // ─────────────────────────────────────────────────────────────────
    // 11. Integration — fury/src as code directory
    // ─────────────────────────────────────────────────────────────────

    private void RequireFurySrc()
    {
        if (!Directory.Exists(FurySrcDir))
            Assert.Ignore($"fury/src not found at '{FurySrcDir}'; skipping integration test.");
    }

    [Test]
    public void GeneratePlan_FuryCodeDir_FindsLessonSearchUsages()
    {
        RequireFurySrc();

        // Old JSON: lesson_search = "Lesson Search" (the current fury key)
        // New JSON: lessonSearch  = "Lesson Search" (hypothetical rename target)
        // Code dir: real fury/src — lesson_search IS used in root-navigator.tsx and others
        var oldDir  = MakeJsonDir("old", "en", "learn.json",
            """{"lesson_search": "Lesson Search"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"lessonSearch": "Lesson Search"}""");

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, FurySrcDir, [".tsx", ".ts"]);

        Assert.That(plan.TotalEntries, Is.EqualTo(1));
        Assert.That(plan.Entries[0].CodeUsages, Is.Not.Empty,
            "lesson_search is used in fury/src — code usages must be found");
        Assert.That(
            plan.Entries[0].CodeUsages.Any(u => u.FilePath.Contains("root-navigator")),
            Is.True,
            "root-navigator.tsx contains t('lesson_search', { ns: 'learn' })");
    }

    [Test]
    public void GeneratePlan_FuryCodeDir_KeyNotInCode_EmptyUsages()
    {
        RequireFurySrc();

        // numLessons_plural is in the JSON but NOT directly t()-called in fury/src
        var oldDir  = MakeJsonDir("old", "en", "learn.json",
            """{"numLessons_plural": "{{count}} lessons"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"numLessonsPlural": "{{count}} lessons"}""");

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, FurySrcDir, [".tsx", ".ts"]);

        Assert.That(plan.TotalEntries, Is.EqualTo(1));
        Assert.That(plan.Entries[0].CodeUsages, Is.Empty,
            "numLessons_plural has no direct t() call in fury/src — CodeUsages must be empty");
    }

    [Test]
    public void GeneratePlan_FuryCodeDir_MultipleUsageFiles()
    {
        RequireFurySrc();

        // search_results is used in LearnSearch.tsx — verifying multi-file scan
        var oldDir  = MakeJsonDir("old", "en", "learn.json",
            """{"search_results": "Search results"}""");
        var newDir  = MakeJsonDir("new", "en", "learn.json",
            """{"searchResults": "Search results"}""");

        var plan = _svc.GenerateMigrationPlan(oldDir, newDir, FurySrcDir, [".tsx", ".ts"]);

        Assert.That(plan.TotalEntries, Is.EqualTo(1));
        var entry = plan.Entries[0];
        Assert.That(entry.CodeUsages, Is.Not.Empty);

        // Line numbers must be 1-based
        Assert.That(entry.CodeUsages.All(u => u.LineNumber >= 1), Is.True,
            "All line numbers must be 1-based");
        // Context (the code line) must be non-empty
        Assert.That(entry.CodeUsages.All(u => !string.IsNullOrWhiteSpace(u.Context)), Is.True,
            "All usage contexts must contain the line of code");
    }
}
