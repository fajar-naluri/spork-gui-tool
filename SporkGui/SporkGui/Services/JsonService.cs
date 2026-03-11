using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SporkGui.Models;

namespace SporkGui.Services
{
    public class JsonService
    {
        private readonly NormalizationService _normalizationService;

        public JsonService(NormalizationService normalizationService)
        {
            _normalizationService = normalizationService;
        }

        public List<TranslationEntry> LoadJsonFiles(string directory)
        {
            var allEntries = new List<TranslationEntry>();

            if (!Directory.Exists(directory))
                return allEntries;

            var jsonFiles = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);

            // First, collect all entries from all files
            foreach (var filePath in jsonFiles)
            {
                var fileEntries = ParseJsonFile(filePath);
                allEntries.AddRange(fileEntries);
            }

            // Merge entries with the same normalized key across different files
            // This ensures each TranslationEntry has all languages from all JSON files
            var mergedEntries = new Dictionary<string, TranslationEntry>();

            foreach (var entry in allEntries)
            {
                if (mergedEntries.TryGetValue(entry.NormalizedKey, out var existing))
                {
                    // Merge translations from this entry into the existing one
                    // This combines all languages from different JSON files for the same key
                    foreach (var translation in entry.Translations)
                    {
                        // Always merge - if language already exists, prefer the new value (latest file wins)
                        // This ensures all languages are preserved
                        existing.Translations[translation.Key] = translation.Value;
                    }
                    // Keep the first filename encountered
                }
                else
                {
                    // Create a new entry with a copy of translations
                    var newEntry = new TranslationEntry(entry.Filename, entry.Key, new Dictionary<string, string>(entry.Translations))
                    {
                        NormalizedKey = entry.NormalizedKey,
                        CodeFilename = entry.CodeFilename ?? entry.Filename,
                        CodeKey = entry.CodeKey ?? entry.Key
                    };
                    mergedEntries[entry.NormalizedKey] = newEntry;
                }
            }

            return mergedEntries.Values.ToList();
        }

        public List<TranslationEntry> ParseJsonFile(string filePath)
        {
            var entries = new List<TranslationEntry>();
            var fileName = Path.GetFileName(filePath);
            var directoryName = Path.GetDirectoryName(filePath);

            try
            {
                var jsonContent = File.ReadAllText(filePath);
                using (var doc = JsonDocument.Parse(jsonContent))
                {
                    // Check if root is an object with language codes as keys
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        var rootObj = doc.RootElement;
                        
                        // Check if this is a multi-language structure (all top-level keys are language codes)
                        bool isMultiLanguage = true;
                        var props = rootObj.EnumerateObject().ToList();
                        
                        if (props.Count > 0)
                        {
                            foreach (var prop in props)
                            {
                                if (!IsLikelyLanguageCode(prop.Name) || prop.Value.ValueKind != JsonValueKind.Object)
                                {
                                    isMultiLanguage = false;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            isMultiLanguage = false;
                        }

                        if (isMultiLanguage)
                        {
                            // Multi-language structure: { "en": {...}, "ms": {...} }
                            foreach (var langProp in props)
                            {
                                var langCode = langProp.Name;
                                var langElement = langProp.Value;
                                
                                if (langElement.ValueKind == JsonValueKind.Object)
                                {
                                    var flattened = FlattenJson(langElement);
                                    
                                    foreach (var kvp in flattened)
                                    {
                                        var key = kvp.Key;
                                        var normalizedKey = _normalizationService.NormalizeKey(key);
                                        
                                        var translations = new Dictionary<string, string>();
                                        
                                        if (kvp.Value.ValueKind == JsonValueKind.String)
                                        {
                                            translations[langCode] = kvp.Value.GetString() ?? string.Empty;
                                        }
                                        else
                                        {
                                            translations[langCode] = kvp.Value.GetRawText();
                                        }

                                        // Check if we already have this key and merge translations
                                        var existing = entries.FirstOrDefault(e => e.NormalizedKey == normalizedKey && e.Filename == fileName);
                                        if (existing != null)
                                        {
                                            foreach (var trans in translations)
                                            {
                                                existing.Translations[trans.Key] = trans.Value;
                                            }
                                        }
                                        else
                                        {
                                            entries.Add(new TranslationEntry(fileName, key, translations)
                                            {
                                                NormalizedKey = normalizedKey,
                                                CodeFilename = fileName,
                                                CodeKey = key
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Single language structure - extract language from filename or directory path
                            var langCode = ExtractLanguageFromFilename(fileName) ?? ExtractLanguageFromPath(directoryName) ?? "en";
                            
                            var flattened = FlattenJson(doc.RootElement);
                            
                            foreach (var kvp in flattened)
                            {
                                var key = kvp.Key;
                                var normalizedKey = _normalizationService.NormalizeKey(key);
                                
                                var translations = new Dictionary<string, string>();
                                
                                if (kvp.Value.ValueKind == JsonValueKind.String)
                                {
                                    translations[langCode] = kvp.Value.GetString() ?? string.Empty;
                                }
                                else
                                {
                                    translations[langCode] = kvp.Value.GetRawText();
                                }

                                entries.Add(new TranslationEntry(fileName, key, translations)
                                {
                                    NormalizedKey = normalizedKey,
                                    CodeFilename = fileName,
                                    CodeKey = key
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error or handle it appropriately
                Console.WriteLine($"Error parsing JSON file {filePath}: {ex.Message}");
            }

            return entries;
        }

        private bool IsLikelyLanguageCode(string name)
        {
            // Check if name looks like a language code (2-5 chars, lowercase letters and dashes)
            return name.Length >= 2 && name.Length <= 5 &&
                   System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z]+(-[A-Za-z]+)*$");
        }

        private Dictionary<string, JsonElement> FlattenJson(JsonElement element, string prefix = "")
        {
            var result = new Dictionary<string, JsonElement>();

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) 
                        ? property.Name 
                        : $"{prefix}.{property.Name}";

                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        var nested = FlattenJson(property.Value, key);
                        foreach (var nestedKvp in nested)
                        {
                            result[nestedKvp.Key] = nestedKvp.Value;
                        }
                    }
                    else
                    {
                        result[key] = property.Value;
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                // Handle arrays by indexing them
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var key = string.IsNullOrEmpty(prefix) 
                        ? index.ToString() 
                        : $"{prefix}.{index}";

                    if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                    {
                        var nested = FlattenJson(item, key);
                        foreach (var nestedKvp in nested)
                        {
                            result[nestedKvp.Key] = nestedKvp.Value;
                        }
                    }
                    else
                    {
                        result[key] = item;
                    }
                    index++;
                }
            }
            else
            {
                // Primitive value - shouldn't happen at root, but handle it
                if (!string.IsNullOrEmpty(prefix))
                {
                    result[prefix] = element;
                }
            }

            return result;
        }

        private string ExtractLanguageFromFilename(string filename)
        {
            // Try to extract language code from filename
            // Common patterns: en.json, en-US.json, translations_en.json, etc.
            var patterns = new[]
            {
                @"_([a-z]{2}(?:-[A-Z]{2,})?)\.json$",
                @"^([a-z]{2}(?:-[A-Z]{2,})?)\.json$",
                @"\.([a-z]{2}(?:-[A-Z]{2,})?)\.json$"
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(filename, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        private string ExtractLanguageFromPath(string directoryPath)
        {
            // Try to extract language code from directory path
            // Common patterns: /en/chat.json, /th/chat.json, /locales/en/chat.json, etc.
            if (string.IsNullOrEmpty(directoryPath))
                return null;

            // Check if any directory in the path matches a language code pattern
            var parts = directoryPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var languageCodes = new[] { "en", "ms", "id", "th", "vi", "zh-Hans", "zh-Hant" };
            
            // Check each directory part (from end to start for most specific match)
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var part = parts[i];
                var partLower = part.ToLowerInvariant();
                
                // Check if it matches a language code pattern first (handles zh-Hans, zh-Hant, etc.)
                var langPattern = @"^([a-z]{2}(?:-[a-z]{2,})?)$";
                var match = System.Text.RegularExpressions.Regex.Match(partLower, langPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    var detected = match.Groups[1].Value;
                    // Normalize to match our language codes (e.g., "zh-hans" -> "zh-Hans")
                    if (detected == "zh-hans" || detected == "zhhans")
                        return "zh-Hans";
                    if (detected == "zh-hant" || detected == "zhhant")
                        return "zh-Hant";
                    // Check if it matches any language code (case-insensitive)
                    if (languageCodes.Any(lc => lc.Equals(detected, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Return the properly cased version from languageCodes
                        return languageCodes.First(lc => lc.Equals(detected, StringComparison.OrdinalIgnoreCase));
                    }
                }
            }

            return null;
        }
    }
}
