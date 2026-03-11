using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SporkGui.Models;

namespace SporkGui.Services
{
    public class CodeMigrationService
    {
        private readonly NormalizationService _normalizationService;

        public CodeMigrationService(NormalizationService normalizationService)
        {
            _normalizationService = normalizationService;
        }

        /// <summary>
        /// Applies a single migration entry to code files
        /// </summary>
        public MigrationApplyResult ApplyMigration(KeyMigrationEntry entry)
        {
            var result = new MigrationApplyResult
            {
                Entry = entry,
                Success = true
            };

            if (entry.Status == MigrationStatus.Applied || entry.Status == MigrationStatus.Skipped)
            {
                result.Success = false;
                result.ErrorMessage = $"Entry is already {entry.Status}";
                return result;
            }

            if (entry.CodeUsages.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No code usages found for this entry";
                return result;
            }

            // Group by file to update each file once
            var filesToUpdate = entry.CodeUsages.GroupBy(u => u.FilePath).ToList();

            foreach (var fileGroup in filesToUpdate)
            {
                var filePath = fileGroup.Key;
                
                try
                {
                    // Create backup
                    CreateBackup(filePath);

                    // Read file content
                    var content = File.ReadAllText(filePath);
                    var lines = File.ReadAllLines(filePath).ToList();

                    // Update namespace if it changed
                    if (entry.OldNamespace != entry.NewNamespace)
                    {
                        content = UpdateNamespace(content, entry.OldNamespace, entry.NewNamespace);
                        lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
                    }

                    // Update translation keys
                    content = UpdateTranslationKeys(content, entry.OldKey, entry.NewKey);

                    // Write updated content
                    File.WriteAllText(filePath, content, Encoding.UTF8);

                    result.UpdatedFiles.Add(filePath);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Error updating file {filePath}: {ex.Message}";
                    result.FailedFiles.Add(filePath);
                }
            }

            if (result.Success)
            {
                entry.Status = MigrationStatus.Applied;
            }

            return result;
        }

        /// <summary>
        /// Applies multiple migrations
        /// </summary>
        public List<MigrationApplyResult> ApplyMigrations(List<KeyMigrationEntry> entries)
        {
            var results = new List<MigrationApplyResult>();

            foreach (var entry in entries.Where(e => e.Status == MigrationStatus.Pending || e.Status == MigrationStatus.NeedsReview))
            {
                var result = ApplyMigration(entry);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Updates useTranslation namespace declarations
        /// </summary>
        private string UpdateNamespace(string content, string oldNamespace, string newNamespace)
        {
            if (string.IsNullOrWhiteSpace(oldNamespace) || string.IsNullOrWhiteSpace(newNamespace))
                return content;

            // Pattern: useTranslation('namespace') or useTranslation("namespace")
            // Also handle: const { t } = useTranslation('namespace')
            var pattern = $@"useTranslation\(['""]({Regex.Escape(oldNamespace)})['""]\)";
            var replacement = $"useTranslation('{newNamespace}')";

            return Regex.Replace(content, pattern, replacement, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Updates translation key calls
        /// </summary>
        private string UpdateTranslationKeys(string content, string oldKey, string newKey)
        {
            if (string.IsNullOrWhiteSpace(oldKey) || string.IsNullOrWhiteSpace(newKey))
                return content;

            // Pattern: t('oldKey') or t("oldKey")
            // Need to match the exact key, not just any key
            var escapedOldKey = Regex.Escape(oldKey);
            var pattern = $@"t\(['""]({escapedOldKey})['""]([^)]*)\)";
            var replacement = $"t('{newKey}'$2)";

            return Regex.Replace(content, pattern, replacement, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Creates a backup of a file before modification
        /// </summary>
        public void CreateBackup(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            var backupPath = filePath + ".backup";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            backupPath = $"{filePath}.{timestamp}.backup";

            // If backup already exists, don't overwrite
            if (File.Exists(backupPath))
                return;

            File.Copy(filePath, backupPath);
        }

        /// <summary>
        /// Generates a diff preview for a migration entry
        /// </summary>
        public string GenerateDiff(string filePath, KeyMigrationEntry entry)
        {
            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                var lines = File.ReadAllLines(filePath).ToList();
                var diff = new StringBuilder();
                diff.AppendLine($"File: {filePath}");
                diff.AppendLine($"Old Key: {entry.OldKey} -> New Key: {entry.NewKey}");
                
                if (entry.OldNamespace != entry.NewNamespace)
                {
                    diff.AppendLine($"Old Namespace: {entry.OldNamespace} -> New Namespace: {entry.NewNamespace}");
                }
                
                diff.AppendLine();
                diff.AppendLine("Changes:");

                // Find lines that need to be updated
                var keyPattern = $@"t\(['""]({Regex.Escape(entry.OldKey)})['""]";
                var namespacePattern = $@"useTranslation\(['""]({Regex.Escape(entry.OldNamespace)})['""]\)";

                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    var lineNumber = i + 1;

                    // Check if this line contains the old key or namespace
                    if (Regex.IsMatch(line, keyPattern, RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(line, namespacePattern, RegexOptions.IgnoreCase))
                    {
                        diff.AppendLine($"Line {lineNumber}:");
                        diff.AppendLine($"  - {line.Trim()}");

                        // Generate what it would look like after update
                        var updatedLine = line;
                        if (entry.OldNamespace != entry.NewNamespace)
                        {
                            updatedLine = UpdateNamespace(updatedLine, entry.OldNamespace, entry.NewNamespace);
                        }
                        updatedLine = UpdateTranslationKeys(updatedLine, entry.OldKey, entry.NewKey);

                        diff.AppendLine($"  + {updatedLine.Trim()}");
                        diff.AppendLine();
                    }
                }

                return diff.ToString();
            }
            catch (Exception ex)
            {
                return $"Error generating diff: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Result of applying a migration
    /// </summary>
    public class MigrationApplyResult
    {
        public KeyMigrationEntry Entry { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> UpdatedFiles { get; set; } = new List<string>();
        public List<string> FailedFiles { get; set; } = new List<string>();
    }
}
