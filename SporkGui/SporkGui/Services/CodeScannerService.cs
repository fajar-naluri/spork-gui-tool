using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SporkGui.Services
{
    public class CodeScannerService
    {
        private readonly NormalizationService _normalizationService;

        public CodeScannerService(NormalizationService normalizationService)
        {
            _normalizationService = normalizationService;
        }

        /// <summary>
        /// Common translation key patterns
        /// Patterns handle cases with additional parameters like t('key', { ns: 'health' })
        /// </summary>
        public static readonly Dictionary<string, string> CommonPatterns = new Dictionary<string, string>
        {
            { "t('key')", @"t\(['""]([^'""]+)['""][^)]*\)" },
            { "i18n.t('key')", @"i18n\.t\(['""]([^'""]+)['""][^)]*\)" },
            { "translate('key')", @"translate\(['""]([^'""]+)['""][^)]*\)" },
            { "t.key", @"t\.([a-zA-Z_][a-zA-Z0-9_]*)" }
        };

        /// <summary>
        /// Scans code files in the specified directory and extracts translation keys using the given pattern
        /// </summary>
        /// <param name="directory">Directory to scan</param>
        /// <param name="fileExtensions">File extensions to scan (e.g., [".tsx", ".ts"])</param>
        /// <param name="pattern">Regex pattern to extract translation keys</param>
        /// <returns>Set of normalized translation keys found in code</returns>
        public HashSet<string> ScanCodeFiles(string directory, string[] fileExtensions, string pattern)
        {
            var foundKeys = new HashSet<string>();

            if (!Directory.Exists(directory))
                return foundKeys;

            if (string.IsNullOrWhiteSpace(pattern))
                return foundKeys;

            // Get all files matching the extensions
            var files = new List<string>();
            foreach (var extension in fileExtensions)
            {
                var ext = extension.StartsWith(".") ? extension : "." + extension;
                files.AddRange(Directory.GetFiles(directory, $"*{ext}", SearchOption.AllDirectories));
            }

            // Remove duplicates (in case of overlapping patterns)
            files = files.Distinct().ToList();

            foreach (var filePath in files)
            {
                try
                {
                    var content = File.ReadAllText(filePath);
                    var keys = ExtractKeysFromPattern(content, pattern);
                    
                    foreach (var key in keys)
                    {
                        var normalizedKey = _normalizationService.NormalizeKey(key);
                        if (!string.IsNullOrEmpty(normalizedKey))
                        {
                            foundKeys.Add(normalizedKey);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue scanning other files
                    Console.WriteLine($"Error scanning file {filePath}: {ex.Message}");
                }
            }

            return foundKeys;
        }

        /// <summary>
        /// Extracts translation keys from code content using the specified regex pattern
        /// </summary>
        /// <param name="content">Code content to scan</param>
        /// <param name="pattern">Regex pattern with a capture group for the key</param>
        /// <returns>List of extracted translation keys</returns>
        public List<string> ExtractKeysFromPattern(string content, string pattern)
        {
            var keys = new List<string>();

            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(pattern))
                return keys;

            try
            {
                var regex = new Regex(pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
                var matches = regex.Matches(content);

                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
                    {
                        var key = match.Groups[1].Value.Trim();
                        // Handle nested keys (e.g., t.key.subkey) - extract full path
                        if (match.Groups.Count > 2)
                        {
                            // If pattern captures multiple groups, combine them
                            var fullKey = string.Join(".", match.Groups.Cast<Group>().Skip(1).Select(g => g.Value).Where(v => !string.IsNullOrWhiteSpace(v)));
                            if (!string.IsNullOrWhiteSpace(fullKey))
                                keys.Add(fullKey);
                        }
                        else
                        {
                            keys.Add(key);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting keys with pattern {pattern}: {ex.Message}");
            }

            return keys.Distinct().ToList();
        }
    }
}
