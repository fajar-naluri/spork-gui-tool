using System.Collections.Generic;
using SporkGui.Models;
using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// Unit tests for ComparisonService.CompareTranslations() — Sync Tool WITH code usage.
/// usedKeysInCode is a non-null HashSet throughout this file, enabling code-usage filtering.
///
/// Key behaviour changes vs. without code usage (usedKeysInCode = null):
///   MissingInPlatform   — only reported if key IS in usedKeysInCode
///   DifferentTranslation — only reported if key IS in usedKeysInCode
///   MissingInCode        — only reported if key IS in usedKeysInCode (need to add to JSON)
///   OnlyInCode           — only reported if key IS in usedKeysInCode
///   UnusedInCode         — NEW: key in code JSON + master/platform but NOT in usedKeysInCode
///
/// Keys are grounded in real fury/src data:
///   mobile_sheet.csv  → platform entries
///   fury/src/localization/en/learn.json → code entries
///   LearnSearch.tsx / Learn.tsx etc.    → which keys are actually used
/// </summary>
[TestFixture]
public class ComparisonServiceWithCodeUsageTests
{
    private ComparisonService _svc = null!;
    private NormalizationService _norm = null!;

    [SetUp]
    public void SetUp()
    {
        _norm = new NormalizationService();
        _svc  = new ComparisonService();
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private TranslationEntry MakeEntry(string filename, string key,
        params (string lang, string value)[] translations)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (lang, value) in translations)
            dict[lang] = value;

        return new TranslationEntry(filename, key, dict)
        {
            NormalizedKey = _norm.NormalizeKey(key),
            SheetFilename = filename,
            SheetKey      = key
        };
    }

    /// <summary>Returns a HashSet of normalised keys (simulates CodeScannerService output).</summary>
    private HashSet<string> UsedKeys(params string[] keys)
    {
        var set = new HashSet<string>();
        foreach (var k in keys)
            set.Add(_norm.NormalizeKey(k));
        return set;
    }

    private TranslationComparison Compare(
        List<TranslationEntry>? master,
        List<TranslationEntry> platform,
        List<TranslationEntry> code,
        HashSet<string> usedKeys)
        => _svc.CompareTranslations(master, platform, code, usedKeys);

    // ─────────────────────────────────────────────────────────────────
    // 1. MissingInPlatform — filtered by usedKeysInCode
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void MissingInPlatform_KeyUsedInCode_IsReported()
    {
        // lesson_search: in master + code JSON, NOT in platform,
        //   and IS used in code (t('lesson_search', { ns: 'learn' }) in root-navigator.tsx)
        var masterEntry = MakeEntry("learn", "lesson_search",
            ("en", "Lesson Search"), ("ms", "Carian Pelajaran"));
        var codeEntry   = MakeEntry("learn.json", "lesson_search",
            ("en", "Lesson Search"), ("ms", "Carian Pelajaran"));

        var result = Compare(
            master:   [masterEntry],
            platform: [],
            code:     [codeEntry],
            usedKeys: UsedKeys("lesson_search"));

        Assert.That(result.MissingInPlatform, Has.Count.EqualTo(1));
        Assert.That(result.MissingInPlatform[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("lesson_search")));
    }

    [Test]
    public void MissingInPlatform_KeyNotUsedInCode_IsFiltered()
    {
        // loading_up_lessons is in master + code but NOT in platform.
        // When the code-usage set does NOT include it, it must be suppressed.
        var masterEntry = MakeEntry("learn", "loading_up_lessons",
            ("en", "Loading up lessons..."), ("ms", "Memuatkan pelajaran..."));
        var codeEntry   = MakeEntry("learn.json", "loading_up_lessons",
            ("en", "Loading up lessons..."), ("ms", "Memuatkan pelajaran..."));

        var result = Compare(
            master:   [masterEntry],
            platform: [],
            code:     [codeEntry],
            usedKeys: UsedKeys("search_results")); // loading_up_lessons is NOT in this set

        Assert.That(result.MissingInPlatform, Is.Empty,
            "Key absent from usedKeysInCode must be filtered out of MissingInPlatform");
    }

    [Test]
    public void MissingInPlatform_MixedUsage_OnlyUsedKeysReported()
    {
        // Two keys both in master+code, neither in platform.
        // Only the one that IS in usedKeysInCode should appear.
        var m1 = MakeEntry("learn", "lesson_search", ("en", "Lesson Search"));
        var c1 = MakeEntry("learn.json", "lesson_search", ("en", "Lesson Search"));
        var m2 = MakeEntry("learn", "loading_up_lessons", ("en", "Loading up lessons..."));
        var c2 = MakeEntry("learn.json", "loading_up_lessons", ("en", "Loading up lessons..."));

        var result = Compare(
            master:   [m1, m2],
            platform: [],
            code:     [c1, c2],
            usedKeys: UsedKeys("lesson_search")); // only lesson_search is used

        Assert.That(result.MissingInPlatform, Has.Count.EqualTo(1));
        Assert.That(result.MissingInPlatform[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("lesson_search")));
    }

    // ─────────────────────────────────────────────────────────────────
    // 2. DifferentTranslation — filtered by usedKeysInCode
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void DifferentTranslation_KeyUsedInCode_IsReported()
    {
        // search_results: in master + platform + code, code translation outdated.
        // IS used in code (LearnSearch.tsx: t('search_results')).
        var masterEntry   = MakeEntry("learn", "search_results",
            ("en", "Search results"), ("ms", "Hasil carian"));
        var platformEntry = MakeEntry("learn", "search_results",
            ("en", "Search results"), ("ms", "Hasil carian"));
        var codeEntry     = MakeEntry("learn.json", "search_results",
            ("en", "Search results"), ("ms", "OUTDATED")); // code differs

        var result = Compare(
            master:   [masterEntry],
            platform: [platformEntry],
            code:     [codeEntry],
            usedKeys: UsedKeys("search_results"));

        Assert.That(result.DifferentTranslation, Has.Count.EqualTo(1));
        Assert.That(result.DifferentTranslation[0].Translations["ms"],
            Is.EqualTo("Hasil carian"),
            "Update entry should carry the platform's (correct) translation");
    }

    [Test]
    public void DifferentTranslation_KeyNotUsedInCode_IsFiltered()
    {
        // categories: in all three, code differs — but NOT in usedKeysInCode.
        var masterEntry   = MakeEntry("learn", "categories", ("en", "Categories"));
        var platformEntry = MakeEntry("learn", "categories", ("en", "Categories"));
        var codeEntry     = MakeEntry("learn.json", "categories", ("en", "OLD CATEGORIES"));

        var result = Compare(
            master:   [masterEntry],
            platform: [platformEntry],
            code:     [codeEntry],
            usedKeys: UsedKeys("lesson_search")); // categories NOT in used set

        Assert.That(result.DifferentTranslation, Is.Empty,
            "DifferentTranslation must be filtered when key is not in usedKeysInCode");
    }

    // ─────────────────────────────────────────────────────────────────
    // 3. MissingInCode — filtered by usedKeysInCode
    //    Semantics shift: "used in code but missing from JSON → should be added"
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void MissingInCode_KeyUsedInCode_IsReported()
    {
        // explore_our_library_of_lessons is in mobile_sheet and master,
        // but NOT in any code JSON yet. AND it IS used in code.
        // → Should be reported so developer knows to add it to JSON.
        var masterEntry   = MakeEntry("learn", "explore_our_library_of_lessons",
            ("en", "Explore our library of lessons"),
            ("ms", "Terokai sumber pelajaran kami"));
        var platformEntry = MakeEntry("learn", "explore_our_library_of_lessons",
            ("en", "Explore our library of lessons"),
            ("ms", "Terokai sumber pelajaran kami"));

        var result = Compare(
            master:   [masterEntry],
            platform: [platformEntry],
            code:     [],                 // NOT in code JSON
            usedKeys: UsedKeys("explore_our_library_of_lessons"));

        Assert.That(result.MissingInCode, Has.Count.EqualTo(1));
    }

    [Test]
    public void MissingInCode_KeyNotUsedInCode_IsFiltered()
    {
        // hang_tight: in mobile_sheet (platform) + master, NOT in code JSON,
        //   and NOT used in code either → no need to add it to JSON, so suppress.
        var masterEntry   = MakeEntry("learn", "hang_tight",
            ("en", "Hang tight!"), ("ms", "Tunggu sebentar!"));
        var platformEntry = MakeEntry("learn", "hang_tight",
            ("en", "Hang tight!"), ("ms", "Tunggu sebentar!"));

        var result = Compare(
            master:   [masterEntry],
            platform: [platformEntry],
            code:     [],
            usedKeys: UsedKeys("search_results")); // hang_tight NOT in used set

        Assert.That(result.MissingInCode, Is.Empty,
            "Key not referenced in code and not in JSON should be silently skipped");
    }

    // ─────────────────────────────────────────────────────────────────
    // 4. OnlyInCode — filtered by usedKeysInCode
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void OnlyInCode_KeyUsedInCode_IsReported()
    {
        // A key exists in the code JSON and IS referenced in code,
        // but was never added to the master sheet.
        var codeEntry = MakeEntry("learn.json", "orphan_used_key",
            ("en", "Orphan but used"), ("ms", "Yatim tapi digunakan"));

        var result = Compare(
            master:   [],
            platform: [],
            code:     [codeEntry],
            usedKeys: UsedKeys("orphan_used_key"));

        Assert.That(result.OnlyInCode, Has.Count.EqualTo(1));
    }

    [Test]
    public void OnlyInCode_KeyNotUsedInCode_IsFiltered()
    {
        // numLessons_plural: in learn.json but never directly referenced via t() call
        // (i18next uses it internally for pluralisation). With code-usage filter enabled
        // and the scanner not finding a direct t('numLessons_plural') call, this is filtered.
        var codeEntry = MakeEntry("learn.json", "numLessons_plural",
            ("en", "{{count}} lessons"), ("ms", "{{count}} pelajaran"));

        var result = Compare(
            master:   [],               // not in master sheet
            platform: [],
            code:     [codeEntry],
            usedKeys: UsedKeys("numLessons")); // only numLessons (singular) in used set

        Assert.That(result.OnlyInCode, Is.Empty,
            "numLessons_plural not found by scanner must not appear in OnlyInCode");
    }

    // ─────────────────────────────────────────────────────────────────
    // 5. UnusedInCode — only populated when usedKeysInCode != null
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void UnusedInCode_KeyInCodeAndMaster_NotInUsedSet_IsReported()
    {
        // numLessons_plural: in learn.json + master sheet, but NOT in usedKeysInCode
        // (scanner only finds t('numLessons'), not the plural variant).
        var masterEntry = MakeEntry("learn", "numLessons_plural",
            ("en", "{{count}} lessons"), ("ms", "{{count}} pelajaran"));
        var codeEntry   = MakeEntry("learn.json", "numLessons_plural",
            ("en", "{{count}} lessons"), ("ms", "{{count}} pelajaran"));

        var result = Compare(
            master:   [masterEntry],
            platform: [],
            code:     [codeEntry],
            usedKeys: UsedKeys("numLessons")); // plural NOT in used set

        Assert.That(result.UnusedInCode, Has.Count.EqualTo(1));
        Assert.That(result.UnusedInCode[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("numLessons_plural")));
    }

    [Test]
    public void UnusedInCode_KeyInCodeAndPlatform_NotInUsedSet_IsReported()
    {
        // Key is in code JSON + platform sheet, but not in master or usedKeysInCode.
        var platformEntry = MakeEntry("learn", "hang_tight",
            ("en", "Hang tight!"), ("ms", "Tunggu sebentar!"));
        var codeEntry     = MakeEntry("learn.json", "hang_tight",
            ("en", "Hang tight!"), ("ms", "Tunggu sebentar!"));

        var result = Compare(
            master:   [],
            platform: [platformEntry],
            code:     [codeEntry],
            usedKeys: UsedKeys("lesson_search")); // hang_tight NOT used

        Assert.That(result.UnusedInCode, Has.Count.EqualTo(1));
        Assert.That(result.UnusedInCode[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("hang_tight")));
    }

    [Test]
    public void UnusedInCode_KeyInCodeButNotInMasterOrPlatform_NotReported()
    {
        // UnusedInCode requires the key to also exist in master OR platform.
        // If the key is only in code (OnlyInCode territory) and not used → not in UnusedInCode.
        var codeEntry = MakeEntry("learn.json", "purely_orphan_key",
            ("en", "Orphan only in code"));

        var result = Compare(
            master:   [],
            platform: [],
            code:     [codeEntry],
            usedKeys: UsedKeys("lesson_search")); // orphan not used

        Assert.That(result.UnusedInCode, Is.Empty,
            "Key only in code with no master/platform counterpart should not appear in UnusedInCode");
    }

    [Test]
    public void UnusedInCode_KeyInUsedSet_NotReported()
    {
        // A key that is in code JSON + master AND IS in usedKeysInCode should NOT be in UnusedInCode.
        var masterEntry = MakeEntry("learn", "lesson_search", ("en", "Lesson Search"));
        var codeEntry   = MakeEntry("learn.json", "lesson_search", ("en", "Lesson Search"));

        var result = Compare(
            master:   [masterEntry],
            platform: [],
            code:     [codeEntry],
            usedKeys: UsedKeys("lesson_search")); // IS used

        Assert.That(result.UnusedInCode, Is.Empty);
    }

    [Test]
    public void UnusedInCode_EmptyUsedSet_NotPopulated()
    {
        // When usedKeysInCode is an empty (non-null) set,
        // the condition `usedKeysInCode.Count > 0` is false → UnusedInCode stays empty.
        var masterEntry = MakeEntry("learn", "lesson_search", ("en", "Lesson Search"));
        var codeEntry   = MakeEntry("learn.json", "lesson_search", ("en", "Lesson Search"));

        var result = Compare(
            master:   [masterEntry],
            platform: [],
            code:     [codeEntry],
            usedKeys: new HashSet<string>()); // empty, not null

        Assert.That(result.UnusedInCode, Is.Empty,
            "Empty usedKeysInCode set (Count = 0) must not populate UnusedInCode");
    }

    [Test]
    public void UnusedInCode_PrioritisesMasterOverPlatformForSheetInfo()
    {
        // When a code entry exists in BOTH master and platform,
        // the UnusedInCode entry should use the master's SheetFilename/SheetKey.
        var masterEntry   = MakeEntry("learn", "numLessons_plural",
            ("en", "{{count}} lessons"));
        var platformEntry = MakeEntry("learn_platform", "numLessons_plural",
            ("en", "{{count}} lessons"));
        var codeEntry     = MakeEntry("learn.json", "numLessons_plural",
            ("en", "{{count}} lessons"));

        var result = Compare(
            master:   [masterEntry],
            platform: [platformEntry],
            code:     [codeEntry],
            usedKeys: UsedKeys("numLessons")); // plural NOT used

        Assert.That(result.UnusedInCode, Has.Count.EqualTo(1));
        Assert.That(result.UnusedInCode[0].SheetFilename,
            Is.EqualTo("learn"),
            "SheetFilename should come from master when key exists in both master and platform");
    }

    // ─────────────────────────────────────────────────────────────────
    // 6. IsFullySynced — respects code-usage filter
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void IsFullySynced_AllUsedKeysSynced_ReturnsTrue()
    {
        // lesson_search is in all three with identical translations, and IS used in code.
        var entry = MakeEntry("learn", "lesson_search",
            ("en", "Lesson Search"), ("ms", "Carian Pelajaran"));

        // numLessons_plural is in code + master but NOT used → would be UnusedInCode,
        // but IsFullySynced only checks MissingInPlatform + DifferentTranslation + MissingInCode.
        var pluralMaster = MakeEntry("learn", "numLessons_plural", ("en", "{{count}} lessons"));
        var pluralCode   = MakeEntry("learn.json", "numLessons_plural", ("en", "{{count}} lessons"));

        var result = Compare(
            master:   [entry, pluralMaster],
            platform: [entry],
            code:     [entry, pluralCode],
            usedKeys: UsedKeys("lesson_search")); // numLessons_plural NOT used

        Assert.That(result.MissingInPlatform,    Is.Empty);
        Assert.That(result.DifferentTranslation, Is.Empty);
        Assert.That(result.MissingInCode,        Is.Empty);
        Assert.That(result.IsFullySynced,        Is.True,
            "IsFullySynced should be true even when UnusedInCode has entries");
    }

    // ─────────────────────────────────────────────────────────────────
    // 7. Mixed scenario — all categories at once with code-usage filter
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void MixedScenario_WithCodeUsage_AllCategoriesCorrectlyFiltered()
    {
        // Used keys (simulates CodeScannerService output for fury/src/features/learn):
        //   lesson_search, search_results, categories
        // NOT used: hang_tight, numLessons_plural, message_not_sent

        // ── lesson_search: master+code, not in platform, IS used → MissingInPlatform ──
        var mipM = MakeEntry("learn", "lesson_search",       ("en", "Lesson Search"));
        var mipC = MakeEntry("learn.json", "lesson_search",  ("en", "Lesson Search"));

        // ── search_results: all three, code differs, IS used → DifferentTranslation ──
        var diffM = MakeEntry("learn", "search_results",      ("en", "Search results"), ("ms", "Hasil carian"));
        var diffP = MakeEntry("learn", "search_results",      ("en", "Search results"), ("ms", "Hasil carian"));
        var diffC = MakeEntry("learn.json", "search_results", ("en", "Search results"), ("ms", "OLD"));

        // ── categories: master+platform, not in code, IS used → MissingInCode ──
        var micM = MakeEntry("learn", "categories",   ("en", "Categories"), ("ms", "Kategori"));
        var micP = MakeEntry("learn", "categories",   ("en", "Categories"), ("ms", "Kategori"));

        // ── hang_tight: master+platform+code, NOT used → UnusedInCode (not MissingInPlatform) ──
        var unusedM = MakeEntry("learn", "hang_tight",      ("en", "Hang tight!"));
        var unusedP = MakeEntry("learn", "hang_tight",      ("en", "Hang tight!"));
        var unusedC = MakeEntry("learn.json", "hang_tight", ("en", "Hang tight!"));

        // ── numLessons_plural: master+code, NOT used → UnusedInCode (not MissingInPlatform) ──
        var pluralM = MakeEntry("learn", "numLessons_plural",      ("en", "{{count}} lessons"));
        var pluralC = MakeEntry("learn.json", "numLessons_plural", ("en", "{{count}} lessons"));

        // ── orphan: code only, IS used → OnlyInCode ──
        var oicC = MakeEntry("learn.json", "orphan_used",  ("en", "Orphan"));

        var usedKeys = UsedKeys("lesson_search", "search_results", "categories", "orphan_used");

        var result = Compare(
            master:   [mipM, diffM, micM, unusedM, pluralM],
            platform: [diffP, micP, unusedP],
            code:     [mipC, diffC, unusedC, pluralC, oicC],
            usedKeys: usedKeys);

        Assert.Multiple(() =>
        {
            Assert.That(result.MissingInPlatform,    Has.Count.EqualTo(1), "MissingInPlatform");
            Assert.That(result.DifferentTranslation, Has.Count.EqualTo(1), "DifferentTranslation");
            Assert.That(result.MissingInCode,        Has.Count.EqualTo(1), "MissingInCode");
            Assert.That(result.OnlyInCode,           Has.Count.EqualTo(1), "OnlyInCode");
            Assert.That(result.UnusedInCode,         Has.Count.EqualTo(2), "UnusedInCode");
        });

        // Verify the right keys land in the right buckets
        Assert.That(result.MissingInPlatform[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("lesson_search")));
        Assert.That(result.DifferentTranslation[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("search_results")));
        Assert.That(result.MissingInCode[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("categories")));
        Assert.That(result.OnlyInCode[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("orphan_used")));
    }
}
