using System.Collections.Generic;
using SporkGui.Models;
using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// Unit tests for ComparisonService.CompareTranslations() — Sync Tool without code usage.
/// usedKeysInCode = null throughout this file, so code-usage filtering is disabled.
///
/// Data mirrored from real CSV files in the working directory:
///   master_sheet.csv  — the authoritative translation source
///   mobile_sheet.csv  — the platform sheet (mobile)
///   fury/src/localization/**/*.json — the code JSON files
/// </summary>
[TestFixture]
public class ComparisonServiceTests
{
    private ComparisonService _svc = null!;
    private NormalizationService _norm = null!;

    [SetUp]
    public void SetUp()
    {
        _norm = new NormalizationService();
        _svc = new ComparisonService();
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private TranslationEntry MakeEntry(string filename, string key, params (string lang, string value)[] translations)
    {
        var dict = new Dictionary<string, string>();
        foreach (var (lang, value) in translations)
            dict[lang] = value;

        return new TranslationEntry(filename, key, dict)
        {
            NormalizedKey = _norm.NormalizeKey(key),
            SheetFilename = filename,
            SheetKey = key
        };
    }

    private TranslationComparison Compare(
        List<TranslationEntry>? master,
        List<TranslationEntry> platform,
        List<TranslationEntry> code)
        => _svc.CompareTranslations(master, platform, code, usedKeysInCode: null);

    // ─────────────────────────────────────────────────────────────────
    // 1. Fully synced — all categories empty, IsFullySynced = true
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void AllSynced_AllListsEmpty_IsFullySyncedTrue()
    {
        // Key exists in master, platform AND code with identical translations.
        // Based on master_sheet.csv: error | email_is_required
        var entry = MakeEntry("error", "email_is_required",
            ("en", "Email is required"),
            ("ms", "E-mel diperlukan"),
            ("id", "Email diperlukan"));

        var result = Compare(
            master:   [entry],
            platform: [entry],
            code:     [entry]);

        Assert.That(result.MissingInPlatform,    Is.Empty, "MissingInPlatform should be empty");
        Assert.That(result.DifferentTranslation, Is.Empty, "DifferentTranslation should be empty");
        Assert.That(result.MissingInCode,        Is.Empty, "MissingInCode should be empty");
        Assert.That(result.OnlyInCode,           Is.Empty, "OnlyInCode should be empty");
        Assert.That(result.UnusedInCode,         Is.Empty, "UnusedInCode should be empty (no code-usage filter)");
        Assert.That(result.IsFullySynced,        Is.True);
    }

    [Test]
    public void AllEmpty_NoEntries_IsFullySyncedTrue()
    {
        var result = Compare(master: [], platform: [], code: []);

        Assert.That(result.IsFullySynced, Is.True);
        Assert.That(result.MissingInPlatform,    Is.Empty);
        Assert.That(result.DifferentTranslation, Is.Empty);
        Assert.That(result.MissingInCode,        Is.Empty);
        Assert.That(result.OnlyInCode,           Is.Empty);
    }

    // ─────────────────────────────────────────────────────────────────
    // 2. MissingInPlatform — in master + code but NOT in platform
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void MissingInPlatform_KeyInMasterAndCode_NotInPlatform()
    {
        // master_sheet.csv: error | email_is_required is defined.
        // Simulate: platform (mobile_sheet.csv) does not yet have this key.
        var masterEntry = MakeEntry("error", "email_is_required",
            ("en", "Email is required"), ("ms", "E-mel diperlukan"));
        var codeEntry   = MakeEntry("error.json", "email_is_required",
            ("en", "Email is required"), ("ms", "E-mel diperlukan"));

        var result = Compare(
            master:   [masterEntry],
            platform: [],           // not in platform
            code:     [codeEntry]);

        Assert.That(result.MissingInPlatform, Has.Count.EqualTo(1));
        Assert.That(result.MissingInPlatform[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("email_is_required")));
        Assert.That(result.IsFullySynced, Is.False);
    }

    [Test]
    public void MissingInPlatform_MultipleKeys_AllReported()
    {
        // Two keys that are in master+code but absent from platform.
        var e1m = MakeEntry("error", "email_is_required",    ("en", "Email is required"));
        var e1c = MakeEntry("error.json", "email_is_required", ("en", "Email is required"));
        var e2m = MakeEntry("error", "please_provide_a_valid_email_address", ("en", "Please provide a valid email address"));
        var e2c = MakeEntry("error.json", "please_provide_a_valid_email_address", ("en", "Please provide a valid email address"));

        var result = Compare(
            master:   [e1m, e2m],
            platform: [],
            code:     [e1c, e2c]);

        Assert.That(result.MissingInPlatform, Has.Count.EqualTo(2));
    }

    [Test]
    public void MissingInPlatform_KeyInMasterButNotInCode_NotReported()
    {
        // A key must be in BOTH master AND code JSON to appear in MissingInPlatform.
        // If it's only in master (not code), it's excluded.
        var masterOnly = MakeEntry("error", "email_is_required", ("en", "Email is required"));

        var result = Compare(
            master:   [masterOnly],
            platform: [],
            code:     []);              // NOT in code

        Assert.That(result.MissingInPlatform, Is.Empty,
            "Key only in master (not in code) must NOT appear in MissingInPlatform");
    }

    [Test]
    public void MissingInPlatform_KeyInMasterCodeAndPlatform_NotReported()
    {
        // If the key exists in all three it should not appear in MissingInPlatform.
        var entry = MakeEntry("common", "next", ("en", "Next"), ("ms", "Seterusnya"));

        var result = Compare(
            master:   [entry],
            platform: [entry],
            code:     [entry]);

        Assert.That(result.MissingInPlatform, Is.Empty);
    }

    // ─────────────────────────────────────────────────────────────────
    // 3. DifferentTranslation — in all three but translations differ
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void DifferentTranslation_CodeAndPlatformTranslationsDiffer()
    {
        // master_sheet.csv / mobile_sheet.csv: common | next
        // Simulate: platform was updated but code JSON was not.
        var masterEntry   = MakeEntry("common", "next", ("en", "Next"), ("ms", "Seterusnya"));
        var platformEntry = MakeEntry("common", "next", ("en", "Next"), ("ms", "Seterusnya"));
        var codeEntry     = MakeEntry("common.json", "next",
            ("en", "Next"), ("ms", "OUTDATED TRANSLATION")); // code differs from platform

        var result = Compare(
            master:   [masterEntry],
            platform: [platformEntry],
            code:     [codeEntry]);

        Assert.That(result.DifferentTranslation, Has.Count.EqualTo(1));
        Assert.That(result.DifferentTranslation[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("next")));
        // The update entry should carry platform translations (the "correct" value)
        Assert.That(result.DifferentTranslation[0].Translations["ms"],
            Is.EqualTo("Seterusnya"), "Update entry should have platform's translations");
    }

    [Test]
    public void DifferentTranslation_ExtraLanguageInCodeNotInPlatform_Reported()
    {
        // Code has "vi" translation, platform does not → TranslationsMatch returns false
        var masterEntry   = MakeEntry("common", "back", ("en", "Back"), ("ms", "Kembali"));
        var platformEntry = MakeEntry("common", "back", ("en", "Back"), ("ms", "Kembali"));
        var codeEntry     = MakeEntry("common.json", "back",
            ("en", "Back"), ("ms", "Kembali"), ("vi", "Trở lại")); // extra language

        var result = Compare(
            master:   [masterEntry],
            platform: [platformEntry],
            code:     [codeEntry]);

        Assert.That(result.DifferentTranslation, Has.Count.EqualTo(1),
            "Extra language in code but not in platform should cause DifferentTranslation");
    }

    [Test]
    public void DifferentTranslation_TranslationsIdentical_NotReported()
    {
        // When code and platform translations are identical, no difference is reported.
        var entry = MakeEntry("common", "confirm",
            ("en", "Confirm"), ("ms", "Sahkan"), ("id", "Konfirmasi"));

        var result = Compare(
            master:   [entry],
            platform: [entry],
            code:     [entry]);

        Assert.That(result.DifferentTranslation, Is.Empty);
    }

    [Test]
    public void DifferentTranslation_KeyMissingInMaster_NotReported()
    {
        // DifferentTranslation requires the key to exist in master too.
        // If key is missing from master, it should NOT appear here.
        var platformEntry = MakeEntry("common", "submit", ("en", "Submit"));
        var codeEntry     = MakeEntry("common.json", "submit", ("en", "Submit CHANGED"));

        var result = Compare(
            master:   [],               // NOT in master
            platform: [platformEntry],
            code:     [codeEntry]);

        Assert.That(result.DifferentTranslation, Is.Empty,
            "DifferentTranslation requires key to exist in master");
    }

    // ─────────────────────────────────────────────────────────────────
    // 4. MissingInCode — in master + platform but NOT in code JSON
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void MissingInCode_KeyInMasterAndPlatform_NotInCode()
    {
        // mobile_sheet.csv has "lesson_search" but it may not be in the JSON yet.
        var masterEntry   = MakeEntry("learn", "lesson_search", ("en", "Lesson Search"), ("ms", "Carian Pelajaran"));
        var platformEntry = MakeEntry("learn", "lesson_search", ("en", "Lesson Search"), ("ms", "Carian Pelajaran"));

        var result = Compare(
            master:   [masterEntry],
            platform: [platformEntry],
            code:     []);              // NOT in code

        Assert.That(result.MissingInCode, Has.Count.EqualTo(1));
        Assert.That(result.MissingInCode[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("lesson_search")));
    }

    [Test]
    public void MissingInCode_KeyInMasterNotInPlatform_NotReported()
    {
        // Key must be in BOTH master AND platform to appear in MissingInCode.
        var masterOnly = MakeEntry("learn", "lesson_search", ("en", "Lesson Search"));

        var result = Compare(
            master:   [masterOnly],
            platform: [],               // NOT in platform
            code:     []);

        Assert.That(result.MissingInCode, Is.Empty,
            "Key only in master (not in platform) must NOT appear in MissingInCode");
    }

    [Test]
    public void MissingInCode_KeyInAllThree_NotReported()
    {
        var entry = MakeEntry("learn", "lesson_search",
            ("en", "Lesson Search"), ("id", "Pencarian Pelajaran"));

        var result = Compare(
            master:   [entry],
            platform: [entry],
            code:     [entry]);

        Assert.That(result.MissingInCode, Is.Empty);
    }

    // ─────────────────────────────────────────────────────────────────
    // 5. OnlyInCode — in code JSON but NOT in master
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void OnlyInCode_KeyExistsInCodeOnly_Reported()
    {
        // Code JSON contains a key that was never added to the master sheet.
        // E.g., an old/orphaned key still in the JSON.
        var codeEntry = MakeEntry("common.json", "message_not_sent",
            ("en", "Message not sent"), ("ms", "Mesej tidak dihantar"));

        var result = Compare(
            master:   [],               // NOT in master
            platform: [],
            code:     [codeEntry]);

        Assert.That(result.OnlyInCode, Has.Count.EqualTo(1));
        Assert.That(result.OnlyInCode[0].NormalizedKey,
            Is.EqualTo(_norm.NormalizeKey("message_not_sent")));
    }

    [Test]
    public void OnlyInCode_KeyMatchesMasterByTranslationValue_NotReported()
    {
        // If the code entry's translation VALUE matches any master entry's translation,
        // the key is considered found in master (fallback matching) → NOT in OnlyInCode.
        var masterEntry = MakeEntry("learn", "lesson",
            ("en", "Lesson Search"));   // same English value as codeEntry below
        var codeEntry   = MakeEntry("common.json", "lesson_search_alias",
            ("en", "Lesson Search"));   // different key, same value

        var result = Compare(
            master:   [masterEntry],
            platform: [],
            code:     [codeEntry]);

        Assert.That(result.OnlyInCode, Is.Empty,
            "Code entry whose translation matches a master translation should NOT appear in OnlyInCode");
    }

    [Test]
    public void OnlyInCode_KeyInCodeAndPlatformButNotMaster_Reported()
    {
        // A key that slipped into the platform sheet but was never in master.
        // OnlyInCode is based purely on whether the key/value is in master.
        var platformEntry = MakeEntry("learn", "orphan_key", ("en", "Orphan"));
        var codeEntry     = MakeEntry("learn.json", "orphan_key", ("en", "Orphan"));

        var result = Compare(
            master:   [],               // NOT in master
            platform: [platformEntry],
            code:     [codeEntry]);

        Assert.That(result.OnlyInCode, Has.Count.EqualTo(1));
    }

    // ─────────────────────────────────────────────────────────────────
    // 6. UnusedInCode — always empty when usedKeysInCode = null
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void UnusedInCode_WithoutCodeUsageFilter_AlwaysEmpty()
    {
        // Even with entries in all sources, UnusedInCode must be empty
        // when usedKeysInCode is null (code-usage feature disabled).
        var entry = MakeEntry("common", "skip", ("en", "Skip"), ("ms", "Langkau"));

        var result = Compare(
            master:   [entry],
            platform: [entry],
            code:     [entry]);

        Assert.That(result.UnusedInCode, Is.Empty,
            "UnusedInCode must always be empty when usedKeysInCode = null");
    }

    // ─────────────────────────────────────────────────────────────────
    // 7. Key normalization — dots, underscores, camelCase all collapse
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void KeyNormalization_DotSeparatedVsUnderscore_MatchedCorrectly()
    {
        // master uses "email_is_required" (underscore)
        // code uses the same key format — NormalizationService strips underscores
        var masterEntry = MakeEntry("error", "email_is_required",
            ("en", "Email is required"));
        // code key with dots instead (simulated — real JSON keys might use dots)
        var codeEntry   = new TranslationEntry("error.json", "email.is.required",
            new Dictionary<string, string> { ["en"] = "Email is required" })
        {
            NormalizedKey = _norm.NormalizeKey("email.is.required"),  // → "emailisrequired"
            SheetFilename = "error.json",
            SheetKey      = "email.is.required"
        };
        // Both should normalize identically: "emailisrequired"
        Assert.That(_norm.NormalizeKey("email_is_required"),
            Is.EqualTo(_norm.NormalizeKey("email.is.required")),
            "Dot-separated and underscore-separated keys must normalize to the same value");

        var platformEntry = new TranslationEntry("error", "email_is_required",
            new Dictionary<string, string> { ["en"] = "Email is required" })
        {
            NormalizedKey = _norm.NormalizeKey("email_is_required")
        };

        var result = Compare(
            master:   [masterEntry],
            platform: [],               // not in platform → MissingInPlatform
            code:     [codeEntry]);

        Assert.That(result.MissingInPlatform, Has.Count.EqualTo(1),
            "Normalization should match dot-key in code with underscore-key in master");
    }

    [Test]
    public void KeyNormalization_CamelCaseVsUnderscore_NormalizeToSame()
    {
        // master_sheet.csv key: "please_provide_a_valid_email_address"
        // potential code key: "pleaseProvideAValidEmailAddress" (camelCase)
        var normalized1 = _norm.NormalizeKey("please_provide_a_valid_email_address");
        var normalized2 = _norm.NormalizeKey("pleaseProvideAValidEmailAddress");

        Assert.That(normalized1, Is.EqualTo(normalized2),
            "camelCase and underscore forms of the same key must normalize identically");
    }

    // ─────────────────────────────────────────────────────────────────
    // 8. Mixed scenario — multiple categories at once
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void MixedScenario_AllCategoriesPopulated()
    {
        // Keys drawn from real master_sheet.csv / mobile_sheet.csv data:
        //   synced:            common.next             → in all three, same value
        //   missingInPlatform: error.email_is_required → in master + code, not in platform
        //   differentTranslation: common.back          → in all three, code value differs
        //   missingInCode:     learn.lesson_search     → in master + platform, not in code
        //   onlyInCode:        common.message_not_sent → in code only

        var syncedM = MakeEntry("common", "next",
            ("en", "Next"), ("ms", "Seterusnya"));
        var syncedP = MakeEntry("common", "next",
            ("en", "Next"), ("ms", "Seterusnya"));
        var syncedC = MakeEntry("common.json", "next",
            ("en", "Next"), ("ms", "Seterusnya"));

        var mipMaster = MakeEntry("error", "email_is_required", ("en", "Email is required"));
        var mipCode   = MakeEntry("error.json", "email_is_required", ("en", "Email is required"));

        var diffM = MakeEntry("common", "back", ("en", "Back"), ("ms", "Kembali"));
        var diffP = MakeEntry("common", "back", ("en", "Back"), ("ms", "Kembali"));
        var diffC = MakeEntry("common.json", "back", ("en", "Back"), ("ms", "OLD VALUE"));

        var micMaster   = MakeEntry("learn", "lesson_search",
            ("en", "Lesson Search"), ("ms", "Carian Pelajaran"));
        var micPlatform = MakeEntry("learn", "lesson_search",
            ("en", "Lesson Search"), ("ms", "Carian Pelajaran"));

        var oicCode = MakeEntry("common.json", "message_not_sent",
            ("en", "Message not sent"), ("ms", "Mesej tidak dihantar"));

        var result = Compare(
            master:   [syncedM, mipMaster, diffM, micMaster],
            platform: [syncedP, diffP, micPlatform],
            code:     [syncedC, mipCode, diffC, oicCode]);

        Assert.Multiple(() =>
        {
            Assert.That(result.MissingInPlatform,    Has.Count.EqualTo(1), "MissingInPlatform");
            Assert.That(result.DifferentTranslation, Has.Count.EqualTo(1), "DifferentTranslation");
            Assert.That(result.MissingInCode,        Has.Count.EqualTo(1), "MissingInCode");
            Assert.That(result.OnlyInCode,           Has.Count.EqualTo(1), "OnlyInCode");
            Assert.That(result.UnusedInCode,         Is.Empty,             "UnusedInCode");
            Assert.That(result.IsFullySynced,        Is.False);
        });
    }

    // ─────────────────────────────────────────────────────────────────
    // 9. Null master sheet — treated as empty
    // ─────────────────────────────────────────────────────────────────

    [Test]
    public void NullMasterSheet_TreatedAsEmpty_NoException()
    {
        // CompareTranslations accepts null for masterSheet; must not throw.
        var codeEntry = MakeEntry("common.json", "orphan",
            ("en", "Orphan"), ("ms", "Yatim"));

        TranslationComparison result = null!;
        Assert.DoesNotThrow(() =>
        {
            result = _svc.CompareTranslations(
                masterSheet:    null,
                platformSheet:  [],
                codeEntries:    [codeEntry],
                usedKeysInCode: null);
        });

        // With null master, code entries have no master counterpart → OnlyInCode
        Assert.That(result.OnlyInCode, Has.Count.EqualTo(1));
        Assert.That(result.MissingInPlatform, Is.Empty,
            "MissingInPlatform requires master; null master means nothing can be MissingInPlatform");
    }
}
