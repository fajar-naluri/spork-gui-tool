using System.Collections.Generic;

namespace SporkGui.Models
{
    public class TranslationComparison
    {
        /// <summary>
        /// Keys found in master sheet OR code JSON, but NOT in platform sheet.
        /// When code usage checking is enabled, only includes keys that are used in code.
        /// </summary>
        public List<TranslationEntry> MissingInPlatform { get; set; } = new List<TranslationEntry>();

        /// <summary>
        /// Keys found in master, platform, and JSON, but with different translation values.
        /// When code usage checking is enabled, only includes keys that are used in code.
        /// </summary>
        public List<TranslationEntry> DifferentTranslation { get; set; } = new List<TranslationEntry>();

        /// <summary>
        /// Keys found in master + platform but NOT in code JSON.
        /// When code usage checking is enabled, only includes keys that ARE used in code (should be added to JSON).
        /// </summary>
        public List<TranslationEntry> MissingInCode { get; set; } = new List<TranslationEntry>();

        /// <summary>
        /// Keys found in code JSON but NOT in master sheet.
        /// When code usage checking is enabled, only includes keys that are used in code.
        /// </summary>
        public List<TranslationEntry> OnlyInCode { get; set; } = new List<TranslationEntry>();

        /// <summary>
        /// Keys that exist in sheets/JSON but are NOT used in code.
        /// Only populated when code usage checking is enabled.
        /// Prioritizes JSON entries over sheet entries.
        /// </summary>
        public List<TranslationEntry> UnusedInCode { get; set; } = new List<TranslationEntry>();

        // Backward compatibility properties
        [System.Obsolete("Use MissingInPlatform instead")]
        public List<TranslationEntry> MissingInSheet => MissingInPlatform;

        [System.Obsolete("Use DifferentTranslation instead")]
        public List<TranslationEntry> NeedsUpdate => DifferentTranslation;

        public List<TranslationEntry> MasterSheetEntries { get; set; } = new List<TranslationEntry>();
        public List<TranslationEntry> PlatformSheetEntries { get; set; } = new List<TranslationEntry>();
        public List<TranslationEntry> CodeEntries { get; set; } = new List<TranslationEntry>();
        public HashSet<string> UsedKeys { get; set; } = new HashSet<string>();

        public bool IsFullySynced => MissingInPlatform.Count == 0 && DifferentTranslation.Count == 0 && MissingInCode.Count == 0;
    }
}
