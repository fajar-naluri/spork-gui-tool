using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using SporkGui.Models;

namespace SporkGui.Services
{
    public class CsvService
    {
        private readonly NormalizationService _normalizationService;
        private readonly string[] _languageCodes = { "en", "ms", "id", "th", "vi", "zh-Hans", "zh-Hant" };

        public CsvService(NormalizationService normalizationService)
        {
            _normalizationService = normalizationService;
        }

        public List<TranslationEntry> LoadCsvFile(string path)
        {
            var entries = new List<TranslationEntry>();

            if (!File.Exists(path))
                return entries;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                TrimOptions = TrimOptions.Trim
            };

            using (var reader = new StreamReader(path))
            using (var csv = new CsvReader(reader, config))
            {
                csv.Read();
                csv.ReadHeader();

                while (csv.Read())
                {
                    var filename = csv.GetField("Filename") ?? string.Empty;
                    var key = csv.GetField("Key") ?? string.Empty;
                    var normalizedKey = _normalizationService.NormalizeKey(key);

                    var translations = new Dictionary<string, string>();
                    foreach (var langCode in _languageCodes)
                    {
                        var translation = csv.GetField(langCode);
                        if (!string.IsNullOrWhiteSpace(translation))
                        {
                            translations[langCode] = translation;
                        }
                    }

                    entries.Add(new TranslationEntry(filename, key, translations)
                    {
                        NormalizedKey = normalizedKey,
                        SheetFilename = filename,
                        SheetKey = key
                    });
                }
            }

            return entries;
        }

        public void SaveCsvFile(List<TranslationEntry> entries, string path)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            };

            using (var writer = new StreamWriter(path))
            using (var csv = new CsvWriter(writer, config))
            {
                // Write header
                csv.WriteField("Sheet Filename");
                csv.WriteField("Sheet Key");
                csv.WriteField("Code Filename");
                csv.WriteField("Code Key");
                csv.WriteField("Flattened Key");
                foreach (var langCode in _languageCodes)
                {
                    csv.WriteField(langCode);
                }
                csv.NextRecord();

                // Write data
                foreach (var entry in entries)
                {
                    csv.WriteField(entry.SheetFilename ?? string.Empty);
                    csv.WriteField(entry.SheetKey ?? string.Empty);
                    csv.WriteField(entry.CodeFilename ?? string.Empty);
                    csv.WriteField(entry.CodeKey ?? string.Empty);
                    
                    // Generate flattened key from Sheet Key (or Code Key if Sheet Key is empty)
                    var keyToFlatten = !string.IsNullOrWhiteSpace(entry.SheetKey) ? entry.SheetKey : entry.CodeKey;
                    var flattenedKey = FlattenKey(keyToFlatten);
                    csv.WriteField(flattenedKey);
                    
                    foreach (var langCode in _languageCodes)
                    {
                        entry.Translations.TryGetValue(langCode, out var translation);
                        csv.WriteField(translation ?? string.Empty);
                    }
                    csv.NextRecord();
                }
            }
        }

        /// <summary>
        /// Flattens a key by converting camelCase to snake_case and dots to underscores
        /// Example: "reassignment.systemChatNotShare" -> "reassignment_system_chat_not_share"
        /// </summary>
        private string FlattenKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            // First, replace dots with underscores
            var result = key.Replace(".", "_");

            // Convert camelCase to snake_case
            // Insert underscore before uppercase letters that follow lowercase letters or numbers
            result = Regex.Replace(result, @"([a-z0-9])([A-Z])", "$1_$2");

            // Convert to lowercase
            result = result.ToLowerInvariant();

            return result;
        }
    }
}
