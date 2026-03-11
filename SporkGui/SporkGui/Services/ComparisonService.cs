using System;
using System.Collections.Generic;
using System.Linq;
using SporkGui.Models;

namespace SporkGui.Services
{
    public class ComparisonService
    {
        public TranslationComparison CompareTranslations(
            List<TranslationEntry> masterSheet,
            List<TranslationEntry> platformSheet, 
            List<TranslationEntry> codeEntries,
            HashSet<string> usedKeysInCode = null)
        {
            var comparison = new TranslationComparison
            {
                MasterSheetEntries = masterSheet ?? new List<TranslationEntry>(),
                PlatformSheetEntries = platformSheet,
                CodeEntries = codeEntries,
                UsedKeys = usedKeysInCode ?? new HashSet<string>()
            };

            // Create lookup dictionaries
            var platformLookup = platformSheet
                .GroupBy(e => e.NormalizedKey)
                .ToDictionary(g => g.Key, g => g.ToList());

            var masterLookup = (masterSheet ?? new List<TranslationEntry>())
                .GroupBy(e => e.NormalizedKey)
                .ToDictionary(g => g.Key, g => g.ToList());

            var codeLookup = codeEntries
                .GroupBy(e => e.NormalizedKey)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Missing in Platform: Keys found in master sheet AND code JSON, but NOT in platform sheet
            // Must exist in BOTH master sheet AND code JSON (if not in master, it's "Only in Code" instead)
            // When code usage checking is enabled, filter out entries that are NOT used in code
            foreach (var masterEntry in (masterSheet ?? new List<TranslationEntry>()))
            {
                // Must exist in code JSON as well
                if (!codeLookup.ContainsKey(masterEntry.NormalizedKey))
                    continue; // Skip if not in code JSON

                // Must NOT exist in platform
                if (!platformLookup.ContainsKey(masterEntry.NormalizedKey))
                {
                    // Check if code usage filtering is enabled
                    bool isUsedInCode = usedKeysInCode == null || usedKeysInCode.Contains(masterEntry.NormalizedKey);
                    if (!isUsedInCode)
                        continue; // Skip if not used in code when code check is enabled

                    // Get the code entry for this key
                    var codeEntry = codeLookup[masterEntry.NormalizedKey].First();
                    
                    // Use master sheet entry as the source, but include both sheet and code info
                    comparison.MissingInPlatform.Add(new TranslationEntry(
                        masterEntry.Filename,
                        masterEntry.Key,
                        new Dictionary<string, string>(masterEntry.Translations))
                    {
                        NormalizedKey = masterEntry.NormalizedKey,
                        SheetFilename = masterEntry.Filename,
                        SheetKey = masterEntry.Key,
                        CodeFilename = codeEntry.Filename,
                        CodeKey = codeEntry.Key
                    });
                }
            }

            // Different Translation: Keys found in master, platform, and JSON, but with different translation values
            // When code usage checking is enabled, filter out entries that are NOT used in code
            foreach (var codeEntry in codeEntries)
            {
                // Must exist in platform AND master
                if (platformLookup.TryGetValue(codeEntry.NormalizedKey, out var platformEntries) &&
                    masterLookup.ContainsKey(codeEntry.NormalizedKey))
                {
                    // Check if any platform entry matches this code entry exactly
                    bool isSynced = false;

                    foreach (var platformEntry in platformEntries)
                    {
                        // Compare translations - if they match exactly, it's synced
                        if (TranslationsMatch(codeEntry, platformEntry))
                        {
                            isSynced = true;
                            break;
                        }
                    }

                    // Only add to DifferentTranslation if NOT synced (values differ)
                    if (!isSynced)
                    {
                        // Check if code usage filtering is enabled
                        bool isUsedInCode = usedKeysInCode == null || usedKeysInCode.Contains(codeEntry.NormalizedKey);
                        if (!isUsedInCode)
                            continue; // Skip if not used in code when code check is enabled

                        // Use the first matching platform entry as the reference
                        var referenceEntry = platformEntries.First();
                        // Get master entry for sheet info
                        var masterEntry = masterLookup[codeEntry.NormalizedKey].First();
                        // Create an entry with code's filename but platform's translations for update
                        var updateEntry = new TranslationEntry(codeEntry.Filename, codeEntry.Key, new Dictionary<string, string>(referenceEntry.Translations))
                        {
                            NormalizedKey = codeEntry.NormalizedKey,
                            SheetFilename = masterEntry.Filename,
                            SheetKey = masterEntry.Key,
                            CodeFilename = codeEntry.Filename,
                            CodeKey = codeEntry.Key
                        };
                        comparison.DifferentTranslation.Add(updateEntry);
                    }
                }
            }

            // Missing in Code: Keys found in master + platform but NOT in code JSON
            // When code usage checking is enabled, only show entries that ARE used in code (should be added to JSON)
            foreach (var masterEntry in (masterSheet ?? new List<TranslationEntry>()))
            {
                // Check if exists in platform
                if (platformLookup.ContainsKey(masterEntry.NormalizedKey))
                {
                    // Check if NOT in code JSON
                    if (!codeLookup.ContainsKey(masterEntry.NormalizedKey))
                    {
                        // Check if code usage filtering is enabled
                        // When enabled, only show if key IS used in code (meaning it should be added to JSON)
                        bool shouldInclude = usedKeysInCode == null || usedKeysInCode.Contains(masterEntry.NormalizedKey);
                        if (!shouldInclude)
                            continue; // Skip if not used in code when code check is enabled

                        // Missing in Code: exists in master + platform but not in code
                        // Use platform entry's filename if available, otherwise use master's
                        var platformEntry = platformLookup[masterEntry.NormalizedKey].First();
                        var missingInCodeEntry = new TranslationEntry(
                            platformEntry.Filename, 
                            masterEntry.Key, 
                            new Dictionary<string, string>(masterEntry.Translations))
                        {
                            NormalizedKey = masterEntry.NormalizedKey,
                            SheetFilename = masterEntry.Filename,
                            SheetKey = masterEntry.Key,
                            CodeFilename = string.Empty, // Not in code
                            CodeKey = string.Empty // Not in code
                        };
                        comparison.MissingInCode.Add(missingInCodeEntry);
                    }
                }
            }

            // Find items that exist in code but NOT in master (Only in Code)
            // First, create a lookup by translation value for fallback matching
            var masterByTranslation = new Dictionary<string, List<TranslationEntry>>();
            foreach (var masterEntry in (masterSheet ?? new List<TranslationEntry>()))
            {
                foreach (var lang in masterEntry.Translations.Keys)
                {
                    var translation = masterEntry.Translations[lang];
                    if (!string.IsNullOrWhiteSpace(translation))
                    {
                        // Normalize translation for comparison (case-insensitive, trim)
                        var normalizedTranslation = translation.Trim().ToLowerInvariant();
                        if (!masterByTranslation.ContainsKey(normalizedTranslation))
                        {
                            masterByTranslation[normalizedTranslation] = new List<TranslationEntry>();
                        }
                        masterByTranslation[normalizedTranslation].Add(masterEntry);
                    }
                }
            }

            foreach (var codeEntry in codeEntries)
            {
                // Check by normalized key first
                bool existsInMaster = masterLookup.ContainsKey(codeEntry.NormalizedKey);
                
                // If not found by key, check by translation value (fallback matching)
                if (!existsInMaster)
                {
                    foreach (var codeTranslation in codeEntry.Translations.Values)
                    {
                        if (!string.IsNullOrWhiteSpace(codeTranslation))
                        {
                            var normalizedTranslation = codeTranslation.Trim().ToLowerInvariant();
                            if (masterByTranslation.ContainsKey(normalizedTranslation))
                            {
                                existsInMaster = true;
                                break;
                            }
                        }
                    }
                }

                if (!existsInMaster)
                {
                    // Only in Code: exists in code but not in master
                    // If code scanning is enabled, only include if key is actually used in code
                    bool isUsedInCode = usedKeysInCode == null || usedKeysInCode.Contains(codeEntry.NormalizedKey);
                    
                    if (isUsedInCode)
                    {
                        var onlyInCodeEntry = new TranslationEntry(
                            codeEntry.Filename, 
                            codeEntry.Key, 
                            new Dictionary<string, string>(codeEntry.Translations))
                        {
                            NormalizedKey = codeEntry.NormalizedKey,
                            SheetFilename = string.Empty, // Not in master
                            SheetKey = string.Empty, // Not in master
                            CodeFilename = codeEntry.Filename,
                            CodeKey = codeEntry.Key
                        };
                        comparison.OnlyInCode.Add(onlyInCodeEntry);
                    }
                }
            }

            // Unused in Code: Keys that exist in (master OR platform) AND in code JSON, but are NOT used in code
            // Only populated when code usage checking is enabled
            // Must exist in code JSON AND (master sheet OR platform sheet), but NOT referenced in code
            if (usedKeysInCode != null && usedKeysInCode.Count > 0)
            {
                // Only check entries that exist in code JSON
                foreach (var codeEntry in codeEntries)
                {
                    // Must NOT be used in code
                    if (usedKeysInCode.Contains(codeEntry.NormalizedKey))
                        continue; // Skip if used in code

                    // Must exist in master sheet OR platform sheet
                    bool existsInMaster = masterLookup.ContainsKey(codeEntry.NormalizedKey);
                    bool existsInPlatform = platformLookup.ContainsKey(codeEntry.NormalizedKey);

                    if (existsInMaster || existsInPlatform)
                    {
                        // This entry is in code JSON and in (master OR platform), but not used in code
                        var unusedEntry = new TranslationEntry(
                            codeEntry.Filename,
                            codeEntry.Key,
                            new Dictionary<string, string>(codeEntry.Translations))
                        {
                            NormalizedKey = codeEntry.NormalizedKey,
                            CodeFilename = codeEntry.Filename,
                            CodeKey = codeEntry.Key
                        };

                        // Set sheet info - prioritize master, then platform
                        if (existsInMaster && masterLookup.TryGetValue(codeEntry.NormalizedKey, out var masterEntries) && masterEntries.Any())
                        {
                            var masterEntry = masterEntries.First();
                            unusedEntry.SheetFilename = masterEntry.Filename;
                            unusedEntry.SheetKey = masterEntry.Key;
                        }
                        else if (existsInPlatform && platformLookup.TryGetValue(codeEntry.NormalizedKey, out var platformEntriesForUnused) && platformEntriesForUnused.Any())
                        {
                            var platformEntry = platformEntriesForUnused.First();
                            unusedEntry.SheetFilename = platformEntry.Filename;
                            unusedEntry.SheetKey = platformEntry.Key;
                        }

                        comparison.UnusedInCode.Add(unusedEntry);
                    }
                }
            }

            return comparison;
        }

        private bool TranslationsMatch(TranslationEntry entry1, TranslationEntry entry2)
        {
            // Get all unique language codes from both entries
            var allLanguages = entry1.Translations.Keys.Union(entry2.Translations.Keys).ToList();

            // If no languages at all, consider them matching (both empty)
            if (!allLanguages.Any())
                return true;

            // Check if all translations match (case-sensitive for values)
            // For a match, all languages that exist in either entry must match
            foreach (var lang in allLanguages)
            {
                entry1.Translations.TryGetValue(lang, out var trans1);
                entry2.Translations.TryGetValue(lang, out var trans2);

                // Normalize empty/null to empty string for comparison
                trans1 = trans1 ?? string.Empty;
                trans2 = trans2 ?? string.Empty;

                // They must match exactly (including both being empty)
                if (trans1 != trans2)
                    return false;
            }

            return true;
        }
    }
}
