using System.Collections.Generic;

namespace SporkGui.Models
{
    public class TranslationEntry
    {
        public string Filename { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string NormalizedKey { get; set; } = string.Empty;
        public Dictionary<string, string> Translations { get; set; } = new Dictionary<string, string>();

        // Sheet (CSV) information
        public string SheetFilename { get; set; } = string.Empty;
        public string SheetKey { get; set; } = string.Empty;

        // Code/JSON information
        public string CodeFilename { get; set; } = string.Empty;
        public string CodeKey { get; set; } = string.Empty;

        public TranslationEntry()
        {
        }

        public TranslationEntry(string filename, string key, Dictionary<string, string> translations)
        {
            Filename = filename;
            Key = key;
            Translations = translations ?? new Dictionary<string, string>();
        }
    }
}
