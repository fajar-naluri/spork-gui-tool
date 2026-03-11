using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SporkGui.Models;

namespace SporkGui.Services
{
    public class KeyMigrationService
    {
        private readonly JsonService _jsonService;
        private readonly CodeScannerService _codeScannerService;
        private readonly NormalizationService _normalizationService;

        public KeyMigrationService(JsonService jsonService, CodeScannerService codeScannerService, NormalizationService normalizationService)
        {
            _jsonService = jsonService;
            _codeScannerService = codeScannerService;
            _normalizationService = normalizationService;
        }

        /// <summary>
        /// Generates a migration plan by matching old and new translation entries by value
        /// </summary>
        public KeyMigrationResult GenerateMigrationPlan(
            string oldJsonDirectory,
            string newJsonDirectory,
            string codeDirectory,
            string[] fileExtensions)
        {
            var result = new KeyMigrationResult();

            // Load old and new JSON entries
            var oldEntries = _jsonService.LoadJsonFiles(oldJsonDirectory);
            var newEntries = _jsonService.LoadJsonFiles(newJsonDirectory);

            // Match entries by normalized translation values
            var matches = MatchTranslationsByValue(oldEntries, newEntries);

            // Build value-to-keys maps to detect duplicates
            var oldValueToKeys = BuildValueToKeysMap(oldEntries);
            var newValueToKeys = BuildValueToKeysMap(newEntries);

            // Find code usages for old keys
            var codeUsages = FindCodeUsages(codeDirectory, fileExtensions, matches.Keys.ToList());

            // Create migration entries
            foreach (var match in matches)
            {
                var oldKey = match.Key;
                var newKeyInfo = match.Value; // Dictionary<string, string> where key is newKey, value is namespace

                var oldEntry = oldEntries.FirstOrDefault(e => e.NormalizedKey == _normalizationService.NormalizeKey(oldKey));
                if (oldEntry == null) continue;

                // Extract namespace from filename
                var oldNamespace = ExtractNamespaceFromFilename(oldEntry.Filename);

                // Handle multiple matches
                if (newKeyInfo.Count == 0)
                {
                    // No match found
                    result.Entries.Add(new KeyMigrationEntry
                    {
                        OldKey = oldEntry.Key,
                        OldNamespace = oldNamespace,
                        OldFilename = oldEntry.Filename,
                        Status = MigrationStatus.NoMatch
                    });
                }
                else if (newKeyInfo.Count == 1)
                {
                    // Single match - auto-assign
                    var newKeyPair = newKeyInfo.First();
                    var newKey = newKeyPair.Key;
                    var newNamespace = newKeyPair.Value;

                    var newEntry = newEntries.FirstOrDefault(e => 
                        e.NormalizedKey == _normalizationService.NormalizeKey(newKey) && 
                        ExtractNamespaceFromFilename(e.Filename) == newNamespace);
                    
                    if (newEntry != null)
                    {
                        // Skip if old and new are identical (no migration needed)
                        var oldKeyNormalized = _normalizationService.NormalizeKey(oldEntry.Key);
                        var newKeyNormalized = _normalizationService.NormalizeKey(newEntry.Key);
                        
                        // Check if keys match (both normalized and exact) and filenames match (same file, same key = no migration needed)
                        if (oldKeyNormalized == newKeyNormalized && 
                            oldEntry.Key == newEntry.Key && // Exact key match
                            oldEntry.Filename == newEntry.Filename &&
                            oldNamespace == newNamespace)
                        {
                            // Keys, filenames, and namespaces are the same - no migration needed
                            continue;
                        }

                        // Find the matching value
                        var matchedValue = FindMatchingValue(oldEntry, newEntry);
                        var matchedLanguage = FindMatchingLanguage(oldEntry, newEntry, matchedValue);

                        // Check for duplicate values
                        var normalizedMatchedValue = NormalizeValue(matchedValue);
                        var hasDuplicate = CheckForDuplicateValue(normalizedMatchedValue, oldEntry.Key, newEntry.Key, oldValueToKeys, newValueToKeys, out var duplicateOldKeys, out var duplicateNewKeys);

                        var migrationEntry = new KeyMigrationEntry
                        {
                            OldKey = oldEntry.Key,
                            OldNamespace = oldNamespace,
                            OldFilename = oldEntry.Filename,
                            NewKey = newEntry.Key,
                            NewNamespace = newNamespace,
                            NewFilename = newEntry.Filename,
                            MatchedByValue = matchedValue,
                            MatchedByLanguage = matchedLanguage,
                            Status = hasDuplicate ? MigrationStatus.NeedsReview : MigrationStatus.Pending,
                            HasDuplicateValue = hasDuplicate,
                            DuplicateOldKeys = duplicateOldKeys,
                            DuplicateNewKeys = duplicateNewKeys
                        };

                        // Add code usages
                        if (codeUsages.TryGetValue(oldKey, out var usages))
                        {
                            migrationEntry.CodeUsages = usages;
                        }

                        result.Entries.Add(migrationEntry);
                    }
                }
                else
                {
                    // Multiple matches - needs review
                    var newKeyPair = newKeyInfo.First(); // Use first as default, but mark for review
                    var newKey = newKeyPair.Key;
                    var newNamespace = newKeyPair.Value;

                    var newEntry = newEntries.FirstOrDefault(e => 
                        e.NormalizedKey == _normalizationService.NormalizeKey(newKey) && 
                        ExtractNamespaceFromFilename(e.Filename) == newNamespace);
                    
                    if (newEntry != null)
                    {
                        // Skip if old and new are identical (no migration needed)
                        var oldKeyNormalized = _normalizationService.NormalizeKey(oldEntry.Key);
                        var newKeyNormalized = _normalizationService.NormalizeKey(newEntry.Key);
                        
                        // Check if keys match (both normalized and exact) and filenames match (same file, same key = no migration needed)
                        if (oldKeyNormalized == newKeyNormalized && 
                            oldEntry.Key == newEntry.Key && // Exact key match
                            oldEntry.Filename == newEntry.Filename &&
                            oldNamespace == newNamespace)
                        {
                            // Keys, filenames, and namespaces are the same - no migration needed
                            continue;
                        }

                        var matchedValue = FindMatchingValue(oldEntry, newEntry);
                        var matchedLanguage = FindMatchingLanguage(oldEntry, newEntry, matchedValue);

                        // Check for duplicate values
                        var normalizedMatchedValue = NormalizeValue(matchedValue);
                        var hasDuplicate = CheckForDuplicateValue(normalizedMatchedValue, oldEntry.Key, newEntry.Key, oldValueToKeys, newValueToKeys, out var duplicateOldKeys, out var duplicateNewKeys);

                        var migrationEntry = new KeyMigrationEntry
                        {
                            OldKey = oldEntry.Key,
                            OldNamespace = oldNamespace,
                            OldFilename = oldEntry.Filename,
                            NewKey = newEntry.Key,
                            NewNamespace = newNamespace,
                            NewFilename = newEntry.Filename,
                            MatchedByValue = matchedValue,
                            MatchedByLanguage = matchedLanguage,
                            Status = MigrationStatus.NeedsReview, // Already needs review due to multiple matches
                            HasDuplicateValue = hasDuplicate,
                            DuplicateOldKeys = duplicateOldKeys,
                            DuplicateNewKeys = duplicateNewKeys
                        };

                        // Add alternative matches
                        foreach (var alt in newKeyInfo.Skip(1))
                        {
                            migrationEntry.AlternativeMatches.Add(new KeyValuePair<string, string>(alt.Key, alt.Value));
                        }

                        // Add code usages
                        if (codeUsages.TryGetValue(oldKey, out var usages))
                        {
                            migrationEntry.CodeUsages = usages;
                        }

                        result.Entries.Add(migrationEntry);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Matches old and new translation entries by normalized values
        /// Returns dictionary: oldKey -> List of (newKey, newNamespace) pairs
        /// </summary>
        private Dictionary<string, Dictionary<string, string>> MatchTranslationsByValue(
            List<TranslationEntry> oldEntries,
            List<TranslationEntry> newEntries)
        {
            var matches = new Dictionary<string, Dictionary<string, string>>();

            foreach (var oldEntry in oldEntries)
            {
                var matchesForOldKey = new Dictionary<string, string>();

                // Try to match by value across all languages
                foreach (var oldLang in oldEntry.Translations.Keys)
                {
                    var oldValue = oldEntry.Translations[oldLang];
                    if (string.IsNullOrWhiteSpace(oldValue))
                        continue;

                    var normalizedOldValue = NormalizeValue(oldValue);

                    // Find new entries with matching value
                    foreach (var newEntry in newEntries)
                    {
                        foreach (var newLang in newEntry.Translations.Keys)
                        {
                            var newValue = newEntry.Translations[newLang];
                            if (string.IsNullOrWhiteSpace(newValue))
                                continue;

                            var normalizedNewValue = NormalizeValue(newValue);

                            if (normalizedOldValue == normalizedNewValue)
                            {
                                var newKey = newEntry.Key;
                                var newNamespace = ExtractNamespaceFromFilename(newEntry.Filename);
                                
                                // Avoid duplicates
                                if (!matchesForOldKey.ContainsKey(newKey))
                                {
                                    matchesForOldKey[newKey] = newNamespace;
                                }
                            }
                        }
                    }
                }

                if (matchesForOldKey.Count > 0)
                {
                    matches[oldEntry.Key] = matchesForOldKey;
                }
            }

            return matches;
        }

        /// <summary>
        /// Normalizes a translation value for comparison (trim, case-insensitive)
        /// </summary>
        private string NormalizeValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Extracts namespace from JSON filename
        /// Examples: "home.json" -> "home", "locales/en/home.json" -> "home"
        /// </summary>
        public string ExtractNamespaceFromFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return string.Empty;

            // Remove path and extension
            var name = Path.GetFileNameWithoutExtension(filename);

            // Remove language code if present (e.g., "home_en" -> "home")
            // Common patterns: filename_lang.json, lang_filename.json
            var patterns = new[]
            {
                @"^([a-z]{2}(?:-[A-Z]{2,})?)_(.+)$", // lang_key.json
                @"^(.+)_([a-z]{2}(?:-[A-Z]{2,})?)$"  // key_lang.json
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(name, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1)
                {
                    // Return the part that's not the language code
                    return match.Groups[1].Value != name ? match.Groups[1].Value : match.Groups[2].Value;
                }
            }

            return name;
        }

        /// <summary>
        /// Finds code usages for old keys
        /// </summary>
        private Dictionary<string, List<CodeUsage>> FindCodeUsages(
            string codeDirectory,
            string[] fileExtensions,
            List<string> oldKeys)
        {
            var usages = new Dictionary<string, List<CodeUsage>>();

            if (!Directory.Exists(codeDirectory) || oldKeys.Count == 0)
                return usages;

            // Get all files matching the extensions
            var files = new List<string>();
            foreach (var extension in fileExtensions)
            {
                var ext = extension.StartsWith(".") ? extension : "." + extension;
                files.AddRange(Directory.GetFiles(codeDirectory, $"*{ext}", SearchOption.AllDirectories));
            }

            files = files.Distinct().ToList();

            // Pattern to find translation key usage: t('key') or t("key")
            var keyPattern = @"t\(['""]([^'""]+)['""][^)]*\)";

            foreach (var filePath in files)
            {
                try
                {
                    var lines = File.ReadAllLines(filePath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        var matches = Regex.Matches(line, keyPattern, RegexOptions.IgnoreCase);

                        foreach (Match match in matches)
                        {
                            if (match.Groups.Count > 1)
                            {
                                var key = match.Groups[1].Value.Trim();
                                
                                // Check if this key matches any old key (normalized comparison)
                                foreach (var oldKey in oldKeys)
                                {
                                    if (_normalizationService.NormalizeKey(key) == _normalizationService.NormalizeKey(oldKey))
                                    {
                                        if (!usages.ContainsKey(oldKey))
                                        {
                                            usages[oldKey] = new List<CodeUsage>();
                                        }

                                        usages[oldKey].Add(new CodeUsage
                                        {
                                            FilePath = filePath,
                                            LineNumber = i + 1, // 1-based line numbers
                                            Context = line.Trim()
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scanning file {filePath}: {ex.Message}");
                }
            }

            return usages;
        }

        /// <summary>
        /// Finds the matching value between old and new entries
        /// </summary>
        private string FindMatchingValue(TranslationEntry oldEntry, TranslationEntry newEntry)
        {
            foreach (var lang in oldEntry.Translations.Keys)
            {
                if (oldEntry.Translations.TryGetValue(lang, out var oldValue) &&
                    newEntry.Translations.TryGetValue(lang, out var newValue))
                {
                    if (NormalizeValue(oldValue) == NormalizeValue(newValue))
                    {
                        return oldValue; // Return original value, not normalized
                    }
                }
            }

            // Fallback: return first available value
            return oldEntry.Translations.Values.FirstOrDefault() ?? string.Empty;
        }

        /// <summary>
        /// Finds the language code used for matching
        /// </summary>
        private string FindMatchingLanguage(TranslationEntry oldEntry, TranslationEntry newEntry, string matchedValue)
        {
            foreach (var lang in oldEntry.Translations.Keys)
            {
                if (oldEntry.Translations.TryGetValue(lang, out var oldValue) &&
                    NormalizeValue(oldValue) == NormalizeValue(matchedValue))
                {
                    return lang;
                }
            }

            return oldEntry.Translations.Keys.FirstOrDefault() ?? "en";
        }

        /// <summary>
        /// Builds a map from normalized values to list of keys that have that value
        /// </summary>
        private Dictionary<string, List<string>> BuildValueToKeysMap(List<TranslationEntry> entries)
        {
            var valueToKeys = new Dictionary<string, List<string>>();

            foreach (var entry in entries)
            {
                foreach (var lang in entry.Translations.Keys)
                {
                    var value = entry.Translations[lang];
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    var normalizedValue = NormalizeValue(value);
                    if (!valueToKeys.ContainsKey(normalizedValue))
                    {
                        valueToKeys[normalizedValue] = new List<string>();
                    }

                    // Add key if not already in list
                    if (!valueToKeys[normalizedValue].Contains(entry.Key))
                    {
                        valueToKeys[normalizedValue].Add(entry.Key);
                    }
                }
            }

            return valueToKeys;
        }

        /// <summary>
        /// Checks if a value appears in multiple keys and returns duplicate key lists
        /// </summary>
        private bool CheckForDuplicateValue(
            string normalizedValue,
            string currentOldKey,
            string currentNewKey,
            Dictionary<string, List<string>> oldValueToKeys,
            Dictionary<string, List<string>> newValueToKeys,
            out List<string> duplicateOldKeys,
            out List<string> duplicateNewKeys)
        {
            duplicateOldKeys = new List<string>();
            duplicateNewKeys = new List<string>();

            bool hasDuplicate = false;

            // Check old keys
            if (oldValueToKeys.TryGetValue(normalizedValue, out var oldKeys))
            {
                var otherOldKeys = oldKeys.Where(k => k != currentOldKey).ToList();
                if (otherOldKeys.Count > 0)
                {
                    duplicateOldKeys = otherOldKeys;
                    hasDuplicate = true;
                }
            }

            // Check new keys
            if (newValueToKeys.TryGetValue(normalizedValue, out var newKeys))
            {
                var otherNewKeys = newKeys.Where(k => k != currentNewKey).ToList();
                if (otherNewKeys.Count > 0)
                {
                    duplicateNewKeys = otherNewKeys;
                    hasDuplicate = true;
                }
            }

            return hasDuplicate;
        }
    }
}
