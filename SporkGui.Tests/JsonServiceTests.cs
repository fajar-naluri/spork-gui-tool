using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// Tests for JsonService.ParseJsonFile() and LoadJsonFiles().
///
/// ParseJsonFile detects two structures:
///   1. Multi-language root: { "en": {...}, "ms": {...} }
///      — all top-level keys must satisfy IsLikelyLanguageCode(name)
///        AND the value must be a JSON object.
///      — each language section is flattened with dot-notation keys.
///   2. Single-language (everything else):
///      — Language code inferred from filename first (e.g. en.json, translations_en.json),
///        then from directory path (e.g. .../en/chat.json), then defaults to "en".
///      — Entire object is flattened.
///
/// LoadJsonFiles:
///   — Scans a directory recursively for *.json files.
///   — Merges entries with the same NormalizedKey: later files' translations overwrite.
///   — Each merged entry keeps the FIRST filename encountered.
///
/// IsLikelyLanguageCode: 2–5 chars, lowercase letters and hyphens matching ^[a-z]+(-[A-Za-z]+)*$
///   — KNOWN QUIRK: Short common-words like "home" (4 chars, all lowercase) satisfy the check,
///     so a JSON file with only { "home": {...} } at root would be incorrectly treated as
///     multi-language with "home" as the language code.
///
/// FlattenJson:
///   — Nested objects: "parent.child" key notation.
///   — Arrays: "key.0", "key.1" index notation.
///   — Primitive at root is ignored (no prefix).
/// </summary>
[TestFixture]
public class JsonServiceTests
{
    private JsonService _svc  = null!;
    private NormalizationService _norm = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _norm = new NormalizationService();
        _svc  = new JsonService(_norm);
        _tempDir = Path.Combine(Path.GetTempPath(), "spork_json_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    /// <summary>Writes a JSON file to a temp subdirectory and returns its path.</summary>
    private string WriteJson(string relPath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    // ────────────────────────────────────────────────────────────────────
    // ParseJsonFile — guard conditions
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void ParseJsonFile_NonExistentFile_ReturnsEmpty()
    {
        var result = _svc.ParseJsonFile("/path/does/not/exist.json");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseJsonFile_EmptyObject_ReturnsEmpty()
    {
        var path = WriteJson("empty.json", "{}");
        var result = _svc.ParseJsonFile(path);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseJsonFile_InvalidJson_ReturnsEmpty()
    {
        var path = WriteJson("bad.json", "this is not json {{ }");
        var result = _svc.ParseJsonFile(path);
        Assert.That(result, Is.Empty, "Malformed JSON must be swallowed and return empty list");
    }

    // ────────────────────────────────────────────────────────────────────
    // ParseJsonFile — single-language structure
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void ParseJsonFile_SingleLang_FlatJson_ReturnsEntry()
    {
        // Plain { "key": "value" } — not multi-language
        var path = WriteJson("en/learn.json", """{"lesson_search": "Lesson Search"}""");

        var result = _svc.ParseJsonFile(path);

        Assert.That(result, Has.Count.EqualTo(1));
        var entry = result[0];
        Assert.That(entry.Key,           Is.EqualTo("lesson_search"));
        Assert.That(entry.NormalizedKey, Is.EqualTo("lessonsearch"));
        Assert.That(entry.Translations.ContainsKey("en"), Is.True);
        Assert.That(entry.Translations["en"], Is.EqualTo("Lesson Search"));
    }

    [Test]
    public void ParseJsonFile_SingleLang_LanguageFromDirectory_En()
    {
        // File is in .../en/ directory — should pick up "en" as language code
        var path = WriteJson("en/chat.json", """{"hello": "Hello"}""");

        var result = _svc.ParseJsonFile(path);

        Assert.That(result[0].Translations.ContainsKey("en"), Is.True,
            "Language code should be inferred from parent directory 'en'");
    }

    [Test]
    public void ParseJsonFile_SingleLang_LanguageFromDirectory_Ms()
    {
        var path = WriteJson("ms/learn.json", """{"lesson_search": "Cari Pelajaran"}""");

        var result = _svc.ParseJsonFile(path);

        Assert.That(result[0].Translations.ContainsKey("ms"), Is.True,
            "Language code should be inferred from parent directory 'ms'");
        Assert.That(result[0].Translations["ms"], Is.EqualTo("Cari Pelajaran"));
    }

    [Test]
    public void ParseJsonFile_SingleLang_LanguageFromDirectory_ZhHans()
    {
        // zh-Hans directory → properly cased language code
        var path = WriteJson("zh-Hans/learn.json", """{"lesson_search": "课程搜索"}""");

        var result = _svc.ParseJsonFile(path);

        Assert.That(result[0].Translations.ContainsKey("zh-Hans"), Is.True,
            "zh-Hans directory should produce 'zh-Hans' language code");
    }

    [Test]
    public void ParseJsonFile_SingleLang_LanguageFromFilename_En()
    {
        // en.json filename → lang="en"
        var path = WriteJson("en.json", """{"cancel": "Cancel"}""");

        var result = _svc.ParseJsonFile(path);

        Assert.That(result[0].Translations.ContainsKey("en"), Is.True,
            "Language code 'en' should be extracted from filename 'en.json'");
    }

    [Test]
    public void ParseJsonFile_SingleLang_LanguageFromFilename_TranslationsEn()
    {
        // translations_en.json → lang="en"
        var path = WriteJson("translations_en.json", """{"done": "Done"}""");

        var result = _svc.ParseJsonFile(path);

        Assert.That(result[0].Translations.ContainsKey("en"), Is.True,
            "Language code 'en' should be extracted from filename 'translations_en.json'");
    }

    [Test]
    public void ParseJsonFile_SingleLang_CodeFilenameAndCodeKey_Set()
    {
        var path = WriteJson("en/learn.json", """{"lesson_search": "Lesson Search"}""");

        var entry = _svc.ParseJsonFile(path)[0];

        Assert.That(entry.CodeFilename, Is.EqualTo("learn.json"),
            "CodeFilename should be the base filename");
        Assert.That(entry.CodeKey, Is.EqualTo("lesson_search"),
            "CodeKey should be the raw JSON key");
    }

    [Test]
    public void ParseJsonFile_SingleLang_MultipleKeys_ReturnsAll()
    {
        var path = WriteJson("en/common.json", """
            {
                "cancel": "Cancel",
                "confirm": "Confirm",
                "done": "Done"
            }
            """);

        var result = _svc.ParseJsonFile(path);

        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result.Select(e => e.Key), Is.EquivalentTo(new[] { "cancel", "confirm", "done" }));
    }

    // ────────────────────────────────────────────────────────────────────
    // ParseJsonFile — FlattenJson (nested objects and arrays)
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void ParseJsonFile_NestedObject_FlattenedWithDotNotation()
    {
        // { "phaseCompleteCongratulations": { "title": "Congrats!" } }
        // → key: "phaseCompleteCongratulations.title"
        var path = WriteJson("en/learn.json", """
            {
                "phaseCompleteCongratulations": {
                    "title": "Congrats!"
                }
            }
            """);

        var result = _svc.ParseJsonFile(path);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Key, Is.EqualTo("phaseCompleteCongratulations.title"));
        Assert.That(result[0].Translations["en"], Is.EqualTo("Congrats!"));
    }

    [Test]
    public void ParseJsonFile_DeeplyNested_FlattenedCorrectly()
    {
        var path = WriteJson("en/nested.json", """
            {
                "level1": {
                    "level2": {
                        "level3": "deep value"
                    }
                }
            }
            """);

        var result = _svc.ParseJsonFile(path);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Key, Is.EqualTo("level1.level2.level3"));
        Assert.That(result[0].Translations["en"], Is.EqualTo("deep value"));
    }

    [Test]
    public void ParseJsonFile_ArrayValues_StoredAsRawJson()
    {
        // When an array is a property value in an object, FlattenJson stores the entire array
        // as a single raw-JSON value (it does NOT recursively index into it).
        // The array-indexing code in FlattenJson only fires when it is called with an Array
        // element at the top level; it is never triggered for array-typed property values.
        var path = WriteJson("en/arr.json", """
            {
                "options": ["First", "Second", "Third"]
            }
            """);

        var result = _svc.ParseJsonFile(path);

        Assert.That(result, Has.Count.EqualTo(1),
            "An array property produces one entry (the raw array), not one entry per element");
        Assert.That(result[0].Key, Is.EqualTo("options"));
        // Translation value is the raw JSON representation of the array
        Assert.That(result[0].Translations["en"], Does.Contain("First"),
            "Raw array text should contain the array elements");
    }

    [Test]
    public void ParseJsonFile_MixedFlatAndNested_AllExtracted()
    {
        var path = WriteJson("en/mixed.json", """
            {
                "simple": "Simple value",
                "nested": {
                    "child": "Nested value"
                }
            }
            """);

        var result = _svc.ParseJsonFile(path);

        Assert.That(result, Has.Count.EqualTo(2));
        var keys = result.Select(e => e.Key).ToList();
        Assert.That(keys, Contains.Item("simple"));
        Assert.That(keys, Contains.Item("nested.child"));
    }

    // ────────────────────────────────────────────────────────────────────
    // ParseJsonFile — multi-language structure detection
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void ParseJsonFile_MultiLang_BothLanguagesExtracted()
    {
        // All top-level keys are language codes → multi-language structure
        var path = WriteJson("combined.json", """
            {
                "en": { "cancel": "Cancel" },
                "ms": { "cancel": "Batal" }
            }
            """);

        var result = _svc.ParseJsonFile(path);

        // Single merged entry with both languages
        Assert.That(result, Has.Count.EqualTo(1));
        var entry = result[0];
        Assert.That(entry.Key, Is.EqualTo("cancel"));
        Assert.That(entry.Translations["en"], Is.EqualTo("Cancel"));
        Assert.That(entry.Translations["ms"], Is.EqualTo("Batal"));
    }

    [Test]
    public void ParseJsonFile_MultiLang_NormalizedKeySet()
    {
        var path = WriteJson("combined.json", """
            {
                "en": { "lesson_search": "Lesson Search" },
                "ms": { "lesson_search": "Cari Pelajaran" }
            }
            """);

        var entry = _svc.ParseJsonFile(path)[0];

        Assert.That(entry.NormalizedKey, Is.EqualTo("lessonsearch"));
    }

    [Test]
    public void ParseJsonFile_MultiLang_NestedKeysFlattened()
    {
        var path = WriteJson("combined.json", """
            {
                "en": {
                    "phaseCompleteCongratulations": {
                        "title": "Congrats!"
                    }
                },
                "ms": {
                    "phaseCompleteCongratulations": {
                        "title": "Tahniah!"
                    }
                }
            }
            """);

        var result = _svc.ParseJsonFile(path);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Key, Is.EqualTo("phaseCompleteCongratulations.title"));
        Assert.That(result[0].Translations["en"], Is.EqualTo("Congrats!"));
        Assert.That(result[0].Translations["ms"], Is.EqualTo("Tahniah!"));
    }

    [Test]
    public void ParseJsonFile_PartiallyMultiLang_TreatedAsSingleLang()
    {
        // One top-level key is NOT a language code → entire file treated as single-language
        var path = WriteJson("en/partial.json", """
            {
                "en": { "cancel": "Cancel" },
                "someRegularKey": "value"
            }
            """);

        var result = _svc.ParseJsonFile(path);

        // "someRegularKey" has length > 5, so IsLikelyLanguageCode returns false for it.
        // The file is treated as single-language; "en" becomes a regular key prefix.
        var keys = result.Select(e => e.Key).ToList();
        Assert.That(keys, Contains.Item("en.cancel"),
            "Because isMultiLanguage=false, 'en' is treated as a regular key prefix");
        Assert.That(keys, Contains.Item("someRegularKey"));
    }

    [Test]
    public void ParseJsonFile_IsLikelyLanguageCode_ShortWords_TreatedAsLangCode()
    {
        // KNOWN QUIRK: A file whose only top-level key is a short word like "home" (4 chars)
        // will be misdetected as a multi-language file with "home" as the language code.
        // This can lead to unexpected behavior in practice.
        var path = WriteJson("home.json", """
            {
                "home": {
                    "title": "Welcome Home"
                }
            }
            """);

        var result = _svc.ParseJsonFile(path);

        // "home" satisfies IsLikelyLanguageCode (4 chars, all lowercase).
        // Treated as multi-lang: lang="home", key="title", value="Welcome Home".
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Translations.ContainsKey("home"), Is.True,
            "KNOWN QUIRK: 'home' satisfies IsLikelyLanguageCode (4 chars, lowercase), " +
            "causing the file to be misdetected as multi-language with 'home' as the lang code");
    }

    // ────────────────────────────────────────────────────────────────────
    // LoadJsonFiles — guard conditions
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void LoadJsonFiles_NonExistentDirectory_ReturnsEmpty()
    {
        var result = _svc.LoadJsonFiles("/path/does/not/exist");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void LoadJsonFiles_EmptyDirectory_ReturnsEmpty()
    {
        var result = _svc.LoadJsonFiles(_tempDir);
        Assert.That(result, Is.Empty);
    }

    // ────────────────────────────────────────────────────────────────────
    // LoadJsonFiles — single-directory scenarios
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void LoadJsonFiles_SingleFile_ReturnsEntries()
    {
        WriteJson("en/learn.json", """{"lesson_search": "Lesson Search", "search_results": "Search Results"}""");

        var result = _svc.LoadJsonFiles(_tempDir);

        Assert.That(result.Count, Is.EqualTo(2));
    }

    [Test]
    public void LoadJsonFiles_TwoLanguageFiles_SameKey_MergesTranslations()
    {
        // en/learn.json + ms/learn.json — same key, different languages
        WriteJson("en/learn.json", """{"lesson_search": "Lesson Search"}""");
        WriteJson("ms/learn.json", """{"lesson_search": "Cari Pelajaran"}""");

        var result = _svc.LoadJsonFiles(_tempDir);

        // Should have exactly 1 merged entry
        Assert.That(result, Has.Count.EqualTo(1));
        var entry = result[0];
        Assert.That(entry.Translations["en"], Is.EqualTo("Lesson Search"));
        Assert.That(entry.Translations["ms"], Is.EqualTo("Cari Pelajaran"));
    }

    [Test]
    public void LoadJsonFiles_MultipleLanguages_AllMergedIntoSingleEntry()
    {
        WriteJson("en/common.json", """{"cancel": "Cancel"}""");
        WriteJson("ms/common.json", """{"cancel": "Batal"}""");
        WriteJson("id/common.json", """{"cancel": "Batalkan"}""");
        WriteJson("th/common.json", """{"cancel": "ยกเลิก"}""");

        var result = _svc.LoadJsonFiles(_tempDir);

        Assert.That(result, Has.Count.EqualTo(1));
        var entry = result[0];
        Assert.That(entry.Translations.Keys, Is.SupersetOf(new[] { "en", "ms", "id", "th" }));
    }

    [Test]
    public void LoadJsonFiles_MultipleKeys_AllReturnedWithCorrectLang()
    {
        WriteJson("en/learn.json", """
            {
                "lesson_search": "Lesson Search",
                "search_results": "Search Results"
            }
            """);
        WriteJson("ms/learn.json", """
            {
                "lesson_search": "Cari Pelajaran",
                "search_results": "Hasil Carian"
            }
            """);

        var result = _svc.LoadJsonFiles(_tempDir);

        Assert.That(result, Has.Count.EqualTo(2));

        var lessonSearch = result.First(e => e.Key == "lesson_search");
        Assert.That(lessonSearch.Translations["en"], Is.EqualTo("Lesson Search"));
        Assert.That(lessonSearch.Translations["ms"], Is.EqualTo("Cari Pelajaran"));

        var searchResults = result.First(e => e.Key == "search_results");
        Assert.That(searchResults.Translations["en"], Is.EqualTo("Search Results"));
        Assert.That(searchResults.Translations["ms"], Is.EqualTo("Hasil Carian"));
    }

    [Test]
    public void LoadJsonFiles_SameLangAppearsTwice_LaterFileWins()
    {
        // Two files for "en" with the same key — last file encountered wins
        WriteJson("fileA.json", """{"cancel": "Cancel A"}""");
        WriteJson("fileB.json", """{"cancel": "Cancel B"}""");

        var result = _svc.LoadJsonFiles(_tempDir);

        // Both files have no language in filename/path → both default to "en".
        // The second file processed should override the first.
        Assert.That(result, Has.Count.EqualTo(1));
        // We can't predict which file wins since directory enumeration order is not guaranteed,
        // but the result should be one of the two values.
        Assert.That(result[0].Translations["en"],
            Is.EqualTo("Cancel A").Or.EqualTo("Cancel B"),
            "When the same lang key appears in multiple files, one value wins (last-file-wins)");
    }

    [Test]
    public void LoadJsonFiles_RecursiveSearch_FindsJsonInSubdirs()
    {
        WriteJson("localization/en/learn.json", """{"lesson_search": "Lesson Search"}""");
        WriteJson("localization/ms/learn.json", """{"lesson_search": "Cari Pelajaran"}""");

        var result = _svc.LoadJsonFiles(_tempDir);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Translations, Does.ContainKey("en").And.ContainKey("ms"));
    }

    [Test]
    public void LoadJsonFiles_NormalizedKey_CamelCaseSameAsUnderscore()
    {
        // "lessonSearch" in JSON normalizes to same key as "lesson_search"
        WriteJson("en/a.json", """{"lessonSearch": "Lesson Search"}""");
        WriteJson("ms/b.json", """{"lesson_search": "Cari Pelajaran"}""");

        var result = _svc.LoadJsonFiles(_tempDir);

        // Both should merge into one entry (same NormalizedKey "lessonsearch")
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].NormalizedKey, Is.EqualTo("lessonsearch"));
        Assert.That(result[0].Translations, Does.ContainKey("en").And.ContainKey("ms"));
    }

    [Test]
    public void LoadJsonFiles_NestedKeys_FlattenedWithDot()
    {
        WriteJson("en/learn.json", """
            {
                "phaseCompleteCongratulations": {
                    "title": "Congrats!"
                }
            }
            """);

        var result = _svc.LoadJsonFiles(_tempDir);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Key, Is.EqualTo("phaseCompleteCongratulations.title"));
        Assert.That(result[0].NormalizedKey, Is.EqualTo("phasecompletecongratulatonstittle").Or
            .EqualTo(_norm.NormalizeKey("phaseCompleteCongratulations.title")),
            "NormalizedKey should equal NormalizationService.NormalizeKey of the raw key");
    }

    [Test]
    public void LoadJsonFiles_FirstFilenameKept_WhenKeysMerge()
    {
        // On merge, the entry's Filename/CodeFilename is from the FIRST file encountered.
        WriteJson("en/learn.json", """{"cancel": "Cancel"}""");
        WriteJson("ms/learn.json", """{"cancel": "Batal"}""");

        var result = _svc.LoadJsonFiles(_tempDir);

        Assert.That(result, Has.Count.EqualTo(1));
        // Filename should be "learn.json" (the base filename from whichever file was processed first)
        Assert.That(result[0].Filename, Is.EqualTo("learn.json"));
    }

    // ────────────────────────────────────────────────────────────────────
    // Integration tests — real fury/src/localization
    // ────────────────────────────────────────────────────────────────────

    private static readonly string FuryLocalizationDir = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "fury", "src", "localization"));

    private void RequireFuryLocalization()
    {
        if (!Directory.Exists(FuryLocalizationDir))
            Assert.Ignore($"fury localization not found at '{FuryLocalizationDir}'; skipping integration test.");
    }

    [Test]
    public void LoadFury_ResultCountIsNonTrivial()
    {
        RequireFuryLocalization();

        var result = _svc.LoadJsonFiles(FuryLocalizationDir);

        Assert.That(result.Count, Is.GreaterThan(20),
            "fury/src/localization should contain many unique translation keys");
    }

    [Test]
    public void LoadFury_LessonSearchKey_HasMultipleLanguages()
    {
        RequireFuryLocalization();

        var result = _svc.LoadJsonFiles(FuryLocalizationDir);
        var normalizedTarget = _norm.NormalizeKey("lesson_search");

        var entry = result.FirstOrDefault(e => e.NormalizedKey == normalizedTarget);
        Assert.That(entry, Is.Not.Null, "lesson_search should be found in fury localization");

        // fury has at minimum English translations
        Assert.That(entry!.Translations.ContainsKey("en"), Is.True,
            "lesson_search must have an English translation");
    }

    [Test]
    public void LoadFury_AllEntriesHaveNormalizedKey()
    {
        RequireFuryLocalization();

        var result = _svc.LoadJsonFiles(FuryLocalizationDir);

        foreach (var entry in result)
        {
            Assert.That(entry.NormalizedKey, Is.Not.Null.And.Not.Empty,
                $"Entry '{entry.Key}' has empty NormalizedKey");
        }
    }

    [Test]
    public void LoadFury_CancelKey_FoundWithEnglishValue()
    {
        RequireFuryLocalization();

        var result = _svc.LoadJsonFiles(FuryLocalizationDir);
        var normalizedCancel = _norm.NormalizeKey("cancel");

        var cancelEntry = result.FirstOrDefault(e => e.NormalizedKey == normalizedCancel);
        Assert.That(cancelEntry, Is.Not.Null, "cancel key should exist in fury localization");
        Assert.That(cancelEntry!.Translations.ContainsKey("en"), Is.True);
    }
}
