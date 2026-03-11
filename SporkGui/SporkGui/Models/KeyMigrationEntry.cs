using System.Collections.Generic;

namespace SporkGui.Models
{
    public enum MigrationStatus
    {
        Pending,
        Applied,
        Skipped,
        NeedsReview,
        NoMatch
    }

    public class CodeUsage
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Context { get; set; } = string.Empty; // Line of code for context
    }

    public class KeyMigrationEntry
    {
        // Old translation info
        public string OldKey { get; set; } = string.Empty;
        public string OldNamespace { get; set; } = string.Empty;
        public string OldFilename { get; set; } = string.Empty;

        // New translation info
        public string NewKey { get; set; } = string.Empty;
        public string NewNamespace { get; set; } = string.Empty;
        public string NewFilename { get; set; } = string.Empty;

        // Matching info
        public string MatchedByValue { get; set; } = string.Empty; // The translation value that matched
        public string MatchedByLanguage { get; set; } = string.Empty; // Language code used for matching

        // Code usage info
        public List<CodeUsage> CodeUsages { get; set; } = new List<CodeUsage>();

        // Status
        public MigrationStatus Status { get; set; } = MigrationStatus.Pending;

        // Additional info for multiple matches
        public List<KeyValuePair<string, string>> AlternativeMatches { get; set; } = new List<KeyValuePair<string, string>>(); // Key -> Namespace pairs
        
        // Duplicate value detection
        public bool HasDuplicateValue { get; set; } = false; // True if the same value exists in multiple keys
        public List<string> DuplicateOldKeys { get; set; } = new List<string>(); // Other old keys with the same value
        public List<string> DuplicateNewKeys { get; set; } = new List<string>(); // Other new keys with the same value
    }
}
