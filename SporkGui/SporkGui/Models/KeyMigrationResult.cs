using System.Collections.Generic;
using System.Linq;

namespace SporkGui.Models
{
    public class KeyMigrationResult
    {
        public List<KeyMigrationEntry> Entries { get; set; } = new List<KeyMigrationEntry>();
        
        public int TotalEntries => Entries.Count;
        public int PendingCount => Entries.Count(e => e.Status == MigrationStatus.Pending);
        public int NeedsReviewCount => Entries.Count(e => e.Status == MigrationStatus.NeedsReview);
        public int NoMatchCount => Entries.Count(e => e.Status == MigrationStatus.NoMatch);
        public int AppliedCount => Entries.Count(e => e.Status == MigrationStatus.Applied);
    }
}
