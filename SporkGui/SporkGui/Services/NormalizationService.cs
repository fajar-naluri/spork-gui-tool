using System;
using System.Text.RegularExpressions;

namespace SporkGui.Services
{
    public class NormalizationService
    {
        /// <summary>
        /// Normalizes a key for comparison by converting to lowercase and removing all whitespace, underscores, hyphens, and other separators.
        /// This makes keys like "getYourEmotion" and "process_your_feelings_manage_your_emotions_and_identify_triggers" match.
        /// </summary>
        public string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            // Convert to lowercase and remove all whitespace, underscores, hyphens, and other separators
            // This makes "getYourEmotion" and "process_your_feelings..." both become "getyouremotion" / "processyourfeelings..."
            var normalized = Regex.Replace(key.ToLowerInvariant(), @"[\s_\-\.]+", string.Empty);
            
            return normalized;
        }
    }
}
