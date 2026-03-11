using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SporkGui.Models;
using SporkGui.Services;

namespace SporkGui.Tests;

/// <summary>
/// Tests for CsvService.LoadCsvFile() and SaveCsvFile().
///
/// LoadCsvFile reads a CSV with columns: Filename, Key, en, ms, id, th, vi, zh-Hans, zh-Hant
///   - Extra columns (master-sheet style: Jira/Figma, Initiatives, etc.) are silently ignored.
///   - Missing/whitespace-only translation values are NOT added to the Translations dict.
///   - NormalizedKey, SheetFilename, SheetKey are all populated.
///
/// SaveCsvFile writes a CSV with columns:
///   Sheet Filename, Sheet Key, Code Filename, Code Key, Flattened Key, en, ms, id, th, vi, zh-Hans, zh-Hant
///   - Flattened Key: SheetKey (or CodeKey if SheetKey empty) → camelCase→snake_case, dots→underscores, lowercase.
///   - Missing translation values are written as empty string.
///
/// FlattenKey (private, tested through SaveCsvFile):
///   - Dots replaced by underscores
///   - camelCase split at lowercase→uppercase boundary: ([a-z0-9])([A-Z]) → $1_$2
///   - Then lowercased
///   - Known limitation: consecutive uppercase (e.g. "XMLParser") produces "xmlparser", NOT "xml_parser",
///     because the regex only fires between a lowercase and an uppercase letter.
/// </summary>
[TestFixture]
public class CsvServiceTests
{
    private CsvService _svc = null!;
    private NormalizationService _norm = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _norm = new NormalizationService();
        _svc  = new CsvService(_norm);
        _tempDir = Path.Combine(Path.GetTempPath(), "spork_csv_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private string WriteCsv(string content)
    {
        var path = Path.Combine(_tempDir, "test.csv");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Reads all text lines from a saved CSV file (strips trailing empty).</summary>
    private string[] ReadSavedLines(string path)
        => File.ReadAllLines(path);

    private TranslationEntry MakeEntry(
        string sheetFilename = "learn",
        string sheetKey      = "lesson_search",
        string codeFilename  = "",
        string codeKey       = "",
        params (string lang, string value)[] translations)
    {
        var dict = translations.ToDictionary(t => t.lang, t => t.value);
        return new TranslationEntry(sheetFilename, sheetKey, dict)
        {
            SheetFilename = sheetFilename,
            SheetKey      = sheetKey,
            CodeFilename  = codeFilename,
            CodeKey       = codeKey,
            NormalizedKey = _norm.NormalizeKey(sheetKey)
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // LoadCsvFile
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void LoadCsv_NonExistentFile_ReturnsEmpty()
    {
        var result = _svc.LoadCsvFile("/path/does/not/exist.csv");
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void LoadCsv_HeaderOnly_ReturnsEmpty()
    {
        var path = WriteCsv("Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n");
        var result = _svc.LoadCsvFile(path);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void LoadCsv_SingleRow_SetsFilenameAndKey()
    {
        var path = WriteCsv(
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lesson_search,Lesson Search,Cari Pelajaran,,,,,"
        );

        var result = _svc.LoadCsvFile(path);

        Assert.That(result, Has.Count.EqualTo(1));
        var entry = result[0];
        Assert.That(entry.Filename,      Is.EqualTo("learn"));
        Assert.That(entry.Key,           Is.EqualTo("lesson_search"));
        Assert.That(entry.SheetFilename, Is.EqualTo("learn"));
        Assert.That(entry.SheetKey,      Is.EqualTo("lesson_search"));
    }

    [Test]
    public void LoadCsv_SingleRow_PopulatesTranslations()
    {
        var path = WriteCsv(
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lesson_search,Lesson Search,Cari Pelajaran,,,,,"
        );

        var entry = _svc.LoadCsvFile(path)[0];

        Assert.That(entry.Translations["en"], Is.EqualTo("Lesson Search"));
        Assert.That(entry.Translations["ms"], Is.EqualTo("Cari Pelajaran"));
    }

    [Test]
    public void LoadCsv_EmptyTranslationValue_NotIncludedInDict()
    {
        // id, th, vi, zh-Hans, zh-Hant are empty → must not be in Translations
        var path = WriteCsv(
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lesson_search,Lesson Search,Cari Pelajaran,,,,,"
        );

        var entry = _svc.LoadCsvFile(path)[0];

        Assert.That(entry.Translations.ContainsKey("id"),      Is.False);
        Assert.That(entry.Translations.ContainsKey("th"),      Is.False);
        Assert.That(entry.Translations.ContainsKey("vi"),      Is.False);
        Assert.That(entry.Translations.ContainsKey("zh-Hans"), Is.False);
        Assert.That(entry.Translations.ContainsKey("zh-Hant"), Is.False);
    }

    [Test]
    public void LoadCsv_WhitespaceOnlyTranslation_NotIncludedInDict()
    {
        // A value that is only spaces is treated as missing.
        var path = WriteCsv(
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lesson_search,Lesson Search,   ,,,,,"
        );

        var entry = _svc.LoadCsvFile(path)[0];

        Assert.That(entry.Translations.ContainsKey("ms"), Is.False,
            "Whitespace-only translation must be excluded from the dictionary");
    }

    [Test]
    public void LoadCsv_AllSevenLanguages_AllPresent()
    {
        var path = WriteCsv(
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "common,cancel,Cancel,Batal,Batalkan,ยกเลิก,Hủy,取消,取消"
        );

        var entry = _svc.LoadCsvFile(path)[0];

        Assert.That(entry.Translations.Keys, Is.EquivalentTo(new[] { "en", "ms", "id", "th", "vi", "zh-Hans", "zh-Hant" }));
    }

    [Test]
    public void LoadCsv_MultipleRows_ReturnsAll()
    {
        var path = WriteCsv(
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lesson_search,Lesson Search,Cari Pelajaran,,,,,\r\n" +
            "common,cancel,Cancel,Batal,,,,,\r\n" +
            "learn,search_results,Search Results,Hasil Carian,,,,,"
        );

        var result = _svc.LoadCsvFile(path);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result.Select(e => e.Key), Is.EquivalentTo(new[] { "lesson_search", "cancel", "search_results" }));
    }

    [Test]
    public void LoadCsv_KeyIsNormalized()
    {
        // lessonSearch (camelCase) and lesson_search (underscore) both normalize to "lessonsearch"
        var path = WriteCsv(
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn,lessonSearch,Lesson Search,,,,,,"
        );

        var entry = _svc.LoadCsvFile(path)[0];

        Assert.That(entry.NormalizedKey, Is.EqualTo("lessonsearch"));
        Assert.That(entry.Key,           Is.EqualTo("lessonSearch"),
            "Original Key must be preserved even after normalization");
    }

    [Test]
    public void LoadCsv_TrimsLeadingAndTrailingWhitespace()
    {
        // CsvHelper is configured with TrimOptions.Trim
        var path = WriteCsv(
            "Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "learn , lesson_search ,  Lesson Search  ,  Cari Pelajaran  ,,,,,"
        );

        var entry = _svc.LoadCsvFile(path)[0];

        Assert.That(entry.Filename,             Is.EqualTo("learn"));
        Assert.That(entry.Key,                  Is.EqualTo("lesson_search"));
        Assert.That(entry.Translations["en"],   Is.EqualTo("Lesson Search"));
        Assert.That(entry.Translations["ms"],   Is.EqualTo("Cari Pelajaran"));
    }

    [Test]
    public void LoadCsv_MasterSheetExtraColumns_AreIgnored()
    {
        // Master CSV has additional columns before Filename/Key — CsvHelper reads by header name so they're ignored.
        var path = WriteCsv(
            "Jira/Figma,Initiatives,Feature Sets,Filename,Key,en,ms,id,th,vi,zh-Hans,zh-Hant\r\n" +
            "JIRA-123,Initiative A,Feature X,learn,lesson_search,Lesson Search,Cari Pelajaran,,,,,"
        );

        var result = _svc.LoadCsvFile(path);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Key,                Is.EqualTo("lesson_search"));
        Assert.That(result[0].Translations["en"],  Is.EqualTo("Lesson Search"));
    }

    // ────────────────────────────────────────────────────────────────────
    // SaveCsvFile
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void SaveCsv_EmptyList_WritesHeaderOnly()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry>(), path);

        var lines = ReadSavedLines(path);
        Assert.That(lines, Has.Length.EqualTo(1), "Only the header row should be written");
        Assert.That(lines[0], Does.Contain("Sheet Filename"));
    }

    [Test]
    public void SaveCsv_Header_ContainsAllExpectedColumns()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry>(), path);

        var header = ReadSavedLines(path)[0];
        foreach (var col in new[] { "Sheet Filename", "Sheet Key", "Code Filename", "Code Key", "Flattened Key",
                                     "en", "ms", "id", "th", "vi", "zh-Hans", "zh-Hant" })
        {
            Assert.That(header, Does.Contain(col), $"Header must contain column '{col}'");
        }
    }

    [Test]
    public void SaveCsv_SingleEntry_LanguagesWrittenInOrder()
    {
        var entry = MakeEntry("common", "cancel", "", "",
            ("en", "Cancel"), ("ms", "Batal"), ("zh-Hans", "取消"), ("zh-Hant", "取消"));

        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var lines = ReadSavedLines(path);
        Assert.That(lines, Has.Length.EqualTo(2));  // header + 1 data row
        var dataRow = lines[1];
        Assert.That(dataRow, Does.Contain("Cancel"),  "en translation missing");
        Assert.That(dataRow, Does.Contain("Batal"),   "ms translation missing");
        Assert.That(dataRow, Does.Contain("取消"),     "zh-Hans/zh-Hant translation missing");
    }

    [Test]
    public void SaveCsv_MissingTranslation_WritesEmptyField()
    {
        // Only 'en' provided; remaining 6 languages should be written as empty
        var entry = MakeEntry(translations: ("en", "Cancel"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var lines = ReadSavedLines(path);
        var dataRow = lines[1];
        // Row has 12 commas (13 columns) — count them to verify empty fields
        // "Sheet Filename","Sheet Key","Code Filename","Code Key","Flattened Key",en,ms,id,th,vi,zh-Hans,zh-Hant
        var cols = ParseCsvRow(dataRow);
        // Columns 5-11 are language columns (0-indexed: 5=en, 6=ms, 7=id, 8=th, 9=vi, 10=zh-Hans, 11=zh-Hant)
        Assert.That(cols[5], Is.EqualTo("Cancel"));
        Assert.That(cols[6], Is.Empty, "ms should be empty");
        Assert.That(cols[7], Is.Empty, "id should be empty");
        Assert.That(cols[11], Is.Empty, "zh-Hant should be empty");
    }

    [Test]
    public void SaveCsv_SheetFilenameAndKey_Written()
    {
        var entry = MakeEntry(sheetFilename: "learn", sheetKey: "lesson_search",
                              translations: ("en", "Lesson Search"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[0], Is.EqualTo("learn"),         "Sheet Filename column");
        Assert.That(cols[1], Is.EqualTo("lesson_search"), "Sheet Key column");
    }

    [Test]
    public void SaveCsv_CodeFilenameAndKey_Written()
    {
        var entry = MakeEntry(codeFilename: "learn.json", codeKey: "lessonSearch",
                              translations: ("en", "Lesson Search"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[2], Is.EqualTo("learn.json"),  "Code Filename column");
        Assert.That(cols[3], Is.EqualTo("lessonSearch"), "Code Key column");
    }

    // ────────────────────────────────────────────────────────────────────
    // FlattenKey (via SaveCsvFile, column index 4 = "Flattened Key")
    // ────────────────────────────────────────────────────────────────────

    [Test]
    public void FlattenKey_CamelCase_SplitAndLowercased()
    {
        // "lessonSearch" → "lesson_search"
        var entry = MakeEntry(sheetKey: "lessonSearch", translations: ("en", "Lesson Search"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[4], Is.EqualTo("lesson_search"));
    }

    [Test]
    public void FlattenKey_DotNotation_ConvertedToUnderscores()
    {
        // "reassignment.systemChatNotShare" → "reassignment_system_chat_not_share"
        var entry = MakeEntry(sheetKey: "reassignment.systemChatNotShare", translations: ("en", "val"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[4], Is.EqualTo("reassignment_system_chat_not_share"));
    }

    [Test]
    public void FlattenKey_AlreadySnakeCase_Unchanged()
    {
        // "lesson_search" is already snake_case, stays the same
        var entry = MakeEntry(sheetKey: "lesson_search", translations: ("en", "val"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[4], Is.EqualTo("lesson_search"));
    }

    [Test]
    public void FlattenKey_Uppercase_Lowercased()
    {
        // "HELLO" → "hello"
        var entry = MakeEntry(sheetKey: "HELLO", translations: ("en", "val"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[4], Is.EqualTo("hello"));
    }

    [Test]
    public void FlattenKey_EmptySheetKey_FallsBackToCodeKey()
    {
        // SheetKey empty → uses CodeKey for flattening
        var entry = new TranslationEntry("learn", "", new Dictionary<string, string> { ["en"] = "val" })
        {
            SheetKey     = "",
            CodeKey      = "lessonSearch",
            SheetFilename = "learn"
        };
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[4], Is.EqualTo("lesson_search"),
            "FlattenedKey should fall back to CodeKey when SheetKey is empty");
    }

    [Test]
    public void FlattenKey_NullKey_ReturnsEmpty()
    {
        // Both SheetKey and CodeKey null/empty → FlattenedKey column is empty
        var entry = new TranslationEntry("learn", "", new Dictionary<string, string> { ["en"] = "val" })
        {
            SheetKey      = "",
            CodeKey       = "",
            SheetFilename = "learn"
        };
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[4], Is.Empty);
    }

    [Test]
    public void FlattenKey_ConsecutiveUppercase_DocumentedBehavior()
    {
        // "XMLParser" — the regex ([a-z0-9])([A-Z]) fires at "r"→"P" boundary only.
        // So result is "xml_parser"? Let's check:
        // "XMLParser" → no lowercase→uppercase boundary before "XM" or "ML",
        //   only at "L"→"P": Regex fires at index 2 (L) and 7 (r→P is not there... wait)
        // "XMLParser":
        //   X-M-L-P-a-r-s-e-r
        //   No: "L" is uppercase, "P" is uppercase → ([a-z0-9])([A-Z]) does NOT match.
        //   "P" is uppercase, "a" is lowercase → does not match (wrong order).
        //   So result is just "xmlparser" (no underscore inserted at all).
        // This is a known limitation.
        var entry = MakeEntry(sheetKey: "XMLParser", translations: ("en", "val"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[4], Is.EqualTo("xmlparser"),
            "KNOWN BUG: Consecutive uppercase (XMLParser) produces 'xmlparser', not 'xml_parser'. " +
            "The regex ([a-z0-9])([A-Z]) only fires at a lowercase→uppercase boundary.");
    }

    [Test]
    public void FlattenKey_MixedDotAndCamel_ConvertedCorrectly()
    {
        // "phaseCompleteCongratulations.title" → "phase_complete_congratulations_title"
        var entry = MakeEntry(sheetKey: "phaseCompleteCongratulations.title", translations: ("en", "val"));
        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(new List<TranslationEntry> { entry }, path);

        var cols = ParseCsvRow(ReadSavedLines(path)[1]);
        Assert.That(cols[4], Is.EqualTo("phase_complete_congratulations_title"));
    }

    [Test]
    public void SaveCsv_MultipleEntries_WritesAllRows()
    {
        var entries = new List<TranslationEntry>
        {
            MakeEntry("learn",  "lesson_search",  translations: ("en", "Lesson Search")),
            MakeEntry("common", "cancel",          translations: ("en", "Cancel")),
            MakeEntry("learn",  "search_results",  translations: ("en", "Search Results")),
        };

        var path = Path.Combine(_tempDir, "out.csv");
        _svc.SaveCsvFile(entries, path);

        var lines = ReadSavedLines(path);
        Assert.That(lines, Has.Length.EqualTo(4)); // header + 3 data rows
    }

    // ────────────────────────────────────────────────────────────────────
    // Integration — LoadCsvFile against real mobile_sheet.csv
    // ────────────────────────────────────────────────────────────────────

    private static readonly string MobileSheetPath = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..", "..", "mobile_sheet.csv"));

    private void RequireMobileSheet()
    {
        if (!File.Exists(MobileSheetPath))
            Assert.Ignore($"mobile_sheet.csv not found at '{MobileSheetPath}'; skipping integration test.");
    }

    [Test]
    public void LoadMobileSheet_ResultCountIsNonTrivial()
    {
        RequireMobileSheet();
        var result = _svc.LoadCsvFile(MobileSheetPath);
        Assert.That(result.Count, Is.GreaterThan(10),
            "mobile_sheet.csv should contain many translation entries");
    }

    [Test]
    public void LoadMobileSheet_AllEntriesHaveNormalizedKey()
    {
        RequireMobileSheet();
        var result = _svc.LoadCsvFile(MobileSheetPath);

        foreach (var entry in result)
        {
            Assert.That(entry.NormalizedKey, Is.Not.Null.And.Not.Empty,
                $"Entry '{entry.Key}' has missing NormalizedKey");
        }
    }

    [Test]
    public void LoadMobileSheet_AllEntriesHaveEnglishTranslation()
    {
        RequireMobileSheet();
        var result = _svc.LoadCsvFile(MobileSheetPath);

        // Every row in mobile_sheet.csv should have at least an English value
        foreach (var entry in result)
        {
            Assert.That(entry.Translations.ContainsKey("en"), Is.True,
                $"Entry '{entry.Key}' is missing an English translation");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Minimal CSV parser (handles simple quoted fields)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a single CSV row into columns.
    /// Handles double-quote escaping (RFC 4180) so quoted fields with commas work correctly.
    /// </summary>
    private static List<string> ParseCsvRow(string line)
    {
        var cols = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    // Peek ahead: is next char also a quote? → escaped quote
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // skip the second quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    cols.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        cols.Add(sb.ToString()); // last field
        return cols;
    }
}
