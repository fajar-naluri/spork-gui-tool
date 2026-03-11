using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Eto.Forms;
using Eto.Drawing;
using SporkGui.Models;
using SporkGui.Services;

namespace SporkGui
{
    public partial class MainForm : Form
    {
        private TextBox _masterCsvPath;
        private TextBox _platformCsvPath;
        private TextBox _jsonDirectoryPath;
        private Button _compareButton;
        private GridView _resultsGrid;
        private Label _statusLabel;
        private Button _generateMissingCsvButton;
        private Button _generateUpdateCsvButton;
        private Button _generateMissingInCodeCsvButton;
        private Button _generateOnlyInCodeCsvButton;
        private Button _generateUnusedInCodeCsvButton;
        
        private CheckBox _checkCodeUsageCheckBox;
        private TextBox _codeDirectoryPath;
        private Button _codeDirectoryButton;
        private TextBox _fileExtensionsInput;
        private DropDown _patternSelector;
        private TextBox _customRegexInput;
        private Label _customRegexLabel;
        
        private TranslationComparison _currentComparison;
        private CsvService _csvService;
        private JsonService _jsonService;
        private ComparisonService _comparisonService;
        private NormalizationService _normalizationService;
        private CodeScannerService _codeScannerService;
        private KeyMigrationService _keyMigrationService;
        private CodeMigrationService _codeMigrationService;

        // Migration tab UI components
        private TextBox _oldJsonDirectoryPath;
        private TextBox _newJsonDirectoryPath;
        private TextBox _migrationCodeDirectoryPath;
        private TextBox _migrationFileExtensionsInput;
        private Button _scanMigrationButton;
        private GridView _migrationResultsGrid;
        private Label _migrationStatusLabel;
        private Button _applySelectedMigrationsButton;
        private Button _applyAllMigrationsButton;
        private TextArea _diffViewer;
        private KeyMigrationResult _currentMigrationResult;

        public MainForm()
        {
            Title = "SPORK Sync Tool (Kinda Work)";
            MinimumSize = new Size(800, 600);
            Size = new Size(1000, 700);

            // Initialize services
            _normalizationService = new NormalizationService();
            _csvService = new CsvService(_normalizationService);
            _jsonService = new JsonService(_normalizationService);
            _comparisonService = new ComparisonService();
            _codeScannerService = new CodeScannerService(_normalizationService);
            _keyMigrationService = new KeyMigrationService(_jsonService, _codeScannerService, _normalizationService);
            _codeMigrationService = new CodeMigrationService(_normalizationService);

            InitializeUI();
        }

        private void InitializeUI()
        {
            // Master CSV file picker
            var masterCsvLabel = new Label { Text = "Master CSV File:" };
            _masterCsvPath = new TextBox { ReadOnly = true, Width = 400 };
            var masterCsvButton = new Button { Text = "Browse..." };
            masterCsvButton.Click += (s, e) =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Master CSV File",
                    Filters = { new FileFilter("CSV Files", "*.csv") }
                };
                if (dialog.ShowDialog(this) == DialogResult.Ok)
                {
                    _masterCsvPath.Text = dialog.FileName;
                }
            };

            var masterCsvLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { masterCsvLabel, _masterCsvPath, masterCsvButton }
            };

            // Platform CSV file picker
            var platformCsvLabel = new Label { Text = "Platform CSV File:" };
            _platformCsvPath = new TextBox { ReadOnly = true, Width = 400 };
            var platformCsvButton = new Button { Text = "Browse..." };
            platformCsvButton.Click += (s, e) =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Platform CSV File",
                    Filters = { new FileFilter("CSV Files", "*.csv") }
                };
                if (dialog.ShowDialog(this) == DialogResult.Ok)
                {
                    _platformCsvPath.Text = dialog.FileName;
                }
            };

            var platformCsvLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { platformCsvLabel, _platformCsvPath, platformCsvButton }
            };

            // JSON directory picker
            var jsonDirLabel = new Label { Text = "JSON Directory:" };
            _jsonDirectoryPath = new TextBox { ReadOnly = true, Width = 400 };
            var jsonDirButton = new Button { Text = "Browse..." };
            jsonDirButton.Click += (s, e) =>
            {
                var dialog = new SelectFolderDialog
                {
                    Title = "Select Directory Containing JSON Files"
                };
                if (dialog.ShowDialog(this) == DialogResult.Ok)
                {
                    _jsonDirectoryPath.Text = dialog.Directory;
                }
            };

            var jsonDirLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { jsonDirLabel, _jsonDirectoryPath, jsonDirButton }
            };

            // Code usage checking controls
            _checkCodeUsageCheckBox = new CheckBox { Text = "Check code usage", Checked = false };
            _checkCodeUsageCheckBox.CheckedChanged += (s, e) => UpdateCodeUsageControlsVisibility();

            // Code directory picker
            var codeDirLabel = new Label { Text = "Code Directory:" };
            _codeDirectoryPath = new TextBox { ReadOnly = true, Width = 400, Enabled = false };
            _codeDirectoryButton = new Button { Text = "Browse...", Enabled = false };
            _codeDirectoryButton.Click += (s, e) =>
            {
                var dialog = new SelectFolderDialog
                {
                    Title = "Select Directory Containing Source Code Files"
                };
                if (dialog.ShowDialog(this) == DialogResult.Ok)
                {
                    _codeDirectoryPath.Text = dialog.Directory;
                }
            };

            var codeDirLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { codeDirLabel, _codeDirectoryPath, _codeDirectoryButton }
            };

            // File extensions input
            var fileExtLabel = new Label { Text = "File Extensions (comma-separated):" };
            _fileExtensionsInput = new TextBox { Text = ".tsx", Width = 200, Enabled = false };
            var fileExtLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { fileExtLabel, _fileExtensionsInput }
            };

            // Pattern selector
            var patternLabel = new Label { Text = "Usage Pattern:" };
            _patternSelector = new DropDown { Width = 200, Enabled = false };
            _patternSelector.Items.Add("t('key')");
            _patternSelector.Items.Add("i18n.t('key')");
            _patternSelector.Items.Add("translate('key')");
            _patternSelector.Items.Add("t.key");
            _patternSelector.Items.Add("Custom regex");
            _patternSelector.SelectedIndex = 0;
            _patternSelector.SelectedIndexChanged += (s, e) => UpdateCustomRegexVisibility();

            // Custom regex input
            _customRegexLabel = new Label { Text = "Custom Regex Pattern:", Visible = false };
            _customRegexInput = new TextBox { Width = 400, Visible = false, Enabled = false };
            var customRegexLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { _customRegexLabel, _customRegexInput }
            };

            var patternLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { patternLabel, _patternSelector }
            };

            var codeUsageGroup = new GroupBox
            {
                Text = "Code Usage Checking",
                Content = new TableLayout
                {
                    Padding = 5,
                    Spacing = new Size(5, 5),
                    Rows =
                    {
                        new TableRow { ScaleHeight = false, Cells = { _checkCodeUsageCheckBox } },
                        new TableRow { ScaleHeight = false, Cells = { codeDirLayout } },
                        new TableRow { ScaleHeight = false, Cells = { fileExtLayout } },
                        new TableRow { ScaleHeight = false, Cells = { patternLayout } },
                        new TableRow { ScaleHeight = false, Cells = { customRegexLayout } }
                    }
                }
            };

            // Compare button
            _compareButton = new Button { Text = "Compare", Width = 150 };
            _compareButton.Click += OnCompareClicked;

            // Status label
            _statusLabel = new Label { Text = "Ready" };

            // Results grid
            _resultsGrid = new GridView
            {
                Height = 300
            };
            _resultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Type",
                DataCell = new TextBoxCell("Type"),
                Width = 120
            });
            _resultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Sheet Filename",
                DataCell = new TextBoxCell("SheetFilename"),
                Width = 150
            });
            _resultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Sheet Key",
                DataCell = new TextBoxCell("SheetKey"),
                Width = 200
            });
            _resultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Code Filename",
                DataCell = new TextBoxCell("CodeFilename"),
                Width = 150
            });
            _resultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Code Key",
                DataCell = new TextBoxCell("CodeKey"),
                Width = 200
            });

            // Generate CSV buttons
            _generateMissingCsvButton = new Button 
            { 
                Text = "Generate Missing in Platform CSV",
                Enabled = false
            };
            _generateMissingCsvButton.Click += OnGenerateMissingCsv;

            _generateUpdateCsvButton = new Button 
            { 
                Text = "Generate Different Translation CSV",
                Enabled = false
            };
            _generateUpdateCsvButton.Click += OnGenerateUpdateCsv;

            _generateMissingInCodeCsvButton = new Button 
            { 
                Text = "Generate Missing in Code CSV",
                Enabled = false
            };
            _generateMissingInCodeCsvButton.Click += OnGenerateMissingInCodeCsv;

            _generateOnlyInCodeCsvButton = new Button 
            { 
                Text = "Generate Only in Code CSV",
                Enabled = false
            };
            _generateOnlyInCodeCsvButton.Click += OnGenerateOnlyInCodeCsv;

            _generateUnusedInCodeCsvButton = new Button 
            { 
                Text = "Generate Unused in Code CSV",
                Enabled = false
            };
            _generateUnusedInCodeCsvButton.Click += OnGenerateUnusedInCodeCsv;

            var buttonLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Items = { _generateMissingCsvButton, _generateUpdateCsvButton, _generateMissingInCodeCsvButton, _generateOnlyInCodeCsvButton, _generateUnusedInCodeCsvButton }
            };

            // Translation Sync tab content
            var translationSyncTabContent = new TableLayout
            {
                Padding = 10,
                Spacing = new Size(5, 5),
                Rows =
                {
                    new TableRow { ScaleHeight = false, Cells = { masterCsvLayout } },
                    new TableRow { ScaleHeight = false, Cells = { platformCsvLayout } },
                    new TableRow { ScaleHeight = false, Cells = { jsonDirLayout } },
                    new TableRow { ScaleHeight = false, Cells = { codeUsageGroup } },
                    new TableRow { ScaleHeight = false, Cells = { _compareButton } },
                    new TableRow { ScaleHeight = false, Cells = { _statusLabel } },
                    new TableRow { ScaleHeight = true, Cells = { _resultsGrid } },
                    new TableRow { ScaleHeight = false, Cells = { buttonLayout } }
                }
            };

            // Create tabs
            var tabControl = new TabControl();
            
            // Translation Sync tab
            var translationSyncTab = new TabPage
            {
                Text = "Translation Sync",
                Content = translationSyncTabContent
            };
            tabControl.Pages.Add(translationSyncTab);

            // Key Migration tab
            var migrationTab = CreateMigrationTab();
            tabControl.Pages.Add(migrationTab);

            // Main layout
            Content = tabControl;

            // Menu bar
            var quitCommand = new Command
                { MenuText = "Quit", Shortcut = Application.Instance.CommonModifier | Keys.Q };
            quitCommand.Executed += (sender, e) => Application.Instance.Quit();

            Menu = new MenuBar
            {
                QuitItem = quitCommand
            };
        }

        private TabPage CreateMigrationTab()
        {
            // Old JSON directory picker
            var oldJsonLabel = new Label { Text = "Old JSON Directory:" };
            _oldJsonDirectoryPath = new TextBox { ReadOnly = true, Width = 400 };
            var oldJsonButton = new Button { Text = "Browse..." };
            oldJsonButton.Click += (s, e) =>
            {
                var dialog = new SelectFolderDialog
                {
                    Title = "Select Old JSON Directory"
                };
                if (dialog.ShowDialog(this) == DialogResult.Ok)
                {
                    _oldJsonDirectoryPath.Text = dialog.Directory;
                }
            };

            var oldJsonLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { oldJsonLabel, _oldJsonDirectoryPath, oldJsonButton }
            };

            // New JSON directory picker
            var newJsonLabel = new Label { Text = "New JSON Directory:" };
            _newJsonDirectoryPath = new TextBox { ReadOnly = true, Width = 400 };
            var newJsonButton = new Button { Text = "Browse..." };
            newJsonButton.Click += (s, e) =>
            {
                var dialog = new SelectFolderDialog
                {
                    Title = "Select New JSON Directory"
                };
                if (dialog.ShowDialog(this) == DialogResult.Ok)
                {
                    _newJsonDirectoryPath.Text = dialog.Directory;
                }
            };

            var newJsonLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { newJsonLabel, _newJsonDirectoryPath, newJsonButton }
            };

            // Code directory picker
            var migrationCodeDirLabel = new Label { Text = "Code Directory:" };
            _migrationCodeDirectoryPath = new TextBox { ReadOnly = true, Width = 400 };
            var migrationCodeDirButton = new Button { Text = "Browse..." };
            migrationCodeDirButton.Click += (s, e) =>
            {
                var dialog = new SelectFolderDialog
                {
                    Title = "Select Code Directory"
                };
                if (dialog.ShowDialog(this) == DialogResult.Ok)
                {
                    _migrationCodeDirectoryPath.Text = dialog.Directory;
                }
            };

            var migrationCodeDirLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { migrationCodeDirLabel, _migrationCodeDirectoryPath, migrationCodeDirButton }
            };

            // File extensions input
            var fileExtLabel = new Label { Text = "File Extensions (comma-separated):" };
            _migrationFileExtensionsInput = new TextBox { Text = ".tsx,.ts", Width = 200 };
            var fileExtLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Items = { fileExtLabel, _migrationFileExtensionsInput }
            };

            // Scan button
            _scanMigrationButton = new Button { Text = "Scan for Migrations", Width = 150 };
            _scanMigrationButton.Click += OnScanMigrationClicked;

            // Status label
            _migrationStatusLabel = new Label { Text = "Ready" };

            // Migration results grid
            _migrationResultsGrid = new GridView
            {
                Height = 250
            };
            _migrationResultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Status",
                DataCell = new TextBoxCell("Status"),
                Width = 100
            });
            _migrationResultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Old Key",
                DataCell = new TextBoxCell("OldKey"),
                Width = 150
            });
            _migrationResultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "New Key",
                DataCell = new TextBoxCell("NewKey"),
                Width = 150
            });
            _migrationResultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Old Namespace",
                DataCell = new TextBoxCell("OldNamespace"),
                Width = 120
            });
            _migrationResultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "New Namespace",
                DataCell = new TextBoxCell("NewNamespace"),
                Width = 120
            });
            _migrationResultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "File",
                DataCell = new TextBoxCell("FilePath"),
                Width = 200
            });
            _migrationResultsGrid.Columns.Add(new GridColumn
            {
                HeaderText = "Line",
                DataCell = new TextBoxCell("LineNumber"),
                Width = 60
            });
            _migrationResultsGrid.SelectionChanged += OnMigrationSelectionChanged;

            // Action buttons
            _applySelectedMigrationsButton = new Button
            {
                Text = "Apply Selected Migrations",
                Enabled = false
            };
            _applySelectedMigrationsButton.Click += OnApplySelectedMigrationsClicked;

            _applyAllMigrationsButton = new Button
            {
                Text = "Apply All Migrations",
                Enabled = false
            };
            _applyAllMigrationsButton.Click += OnApplyAllMigrationsClicked;

            var migrationButtonLayout = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Items = { _applySelectedMigrationsButton, _applyAllMigrationsButton }
            };

            // Diff viewer
            var diffLabel = new Label { Text = "Diff Preview:" };
            _diffViewer = new TextArea
            {
                ReadOnly = true,
                Height = 150
            };

            var diffLayout = new TableLayout
            {
                Padding = 5,
                Spacing = new Size(5, 5),
                Rows =
                {
                    new TableRow { ScaleHeight = false, Cells = { diffLabel } },
                    new TableRow { ScaleHeight = true, Cells = { _diffViewer } }
                }
            };

            var diffGroup = new GroupBox
            {
                Text = "Diff Viewer",
                Content = diffLayout
            };

            // Migration tab layout
            var migrationTabContent = new TableLayout
            {
                Padding = 10,
                Spacing = new Size(5, 5),
                Rows =
                {
                    new TableRow { ScaleHeight = false, Cells = { oldJsonLayout } },
                    new TableRow { ScaleHeight = false, Cells = { newJsonLayout } },
                    new TableRow { ScaleHeight = false, Cells = { migrationCodeDirLayout } },
                    new TableRow { ScaleHeight = false, Cells = { fileExtLayout } },
                    new TableRow { ScaleHeight = false, Cells = { _scanMigrationButton } },
                    new TableRow { ScaleHeight = false, Cells = { _migrationStatusLabel } },
                    new TableRow { ScaleHeight = true, Cells = { _migrationResultsGrid } },
                    new TableRow { ScaleHeight = false, Cells = { migrationButtonLayout } },
                    new TableRow { ScaleHeight = false, Cells = { diffGroup } }
                }
            };

            return new TabPage
            {
                Text = "Key Migration",
                Content = migrationTabContent
            };
        }

        private void UpdateCodeUsageControlsVisibility()
        {
            var enabled = _checkCodeUsageCheckBox.Checked ?? false;
            _codeDirectoryPath.Enabled = enabled;
            _codeDirectoryButton.Enabled = enabled;
            _fileExtensionsInput.Enabled = enabled;
            _patternSelector.Enabled = enabled;
            _customRegexInput.Enabled = enabled && _patternSelector.SelectedIndex == 4; // Custom regex index
            UpdateCustomRegexVisibility();
        }

        private void UpdateCustomRegexVisibility()
        {
            var isCustomRegex = _patternSelector.SelectedIndex == 4; // Custom regex is last item
            _customRegexLabel.Visible = isCustomRegex;
            _customRegexInput.Visible = isCustomRegex;
            _customRegexInput.Enabled = isCustomRegex && (_checkCodeUsageCheckBox.Checked ?? false);
        }

        private void OnCompareClicked(object sender, EventArgs ee)
        {
            try
            {
                _statusLabel.Text = "Loading files...";
                Application.Instance.AsyncInvoke(() =>
                {
                    try
                    {
                        // Load master CSV (optional, for reference)
                        var masterCsvPath = _masterCsvPath.Text;
                        var platformCsvPath = _platformCsvPath.Text;
                        var jsonDirectory = _jsonDirectoryPath.Text;

                        if (string.IsNullOrWhiteSpace(platformCsvPath))
                        {
                            MessageBox.Show(this, "Please select a platform CSV file.", MessageBoxType.Error);
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(jsonDirectory))
                        {
                            MessageBox.Show(this, "Please select a JSON directory.", MessageBoxType.Error);
                            return;
                        }

                        // Load master CSV (optional)
                        List<TranslationEntry> masterEntries = null;
                        if (!string.IsNullOrWhiteSpace(masterCsvPath))
                        {
                            masterEntries = _csvService.LoadCsvFile(masterCsvPath);
                            _statusLabel.Text = $"Loaded {masterEntries.Count} entries from master CSV";
                        }

                        // Load platform CSV
                        var platformEntries = _csvService.LoadCsvFile(platformCsvPath);
                        _statusLabel.Text = $"Loaded {platformEntries.Count} entries from platform CSV";

                        // Load JSON files
                        var codeEntries = _jsonService.LoadJsonFiles(jsonDirectory);
                        _statusLabel.Text = $"Loaded {codeEntries.Count} entries from {codeEntries.Select(e => e.Filename).Distinct().Count()} JSON files";

                        // Code usage scanning (if enabled)
                        HashSet<string> usedKeysInCode = null;
                        if (_checkCodeUsageCheckBox.Checked ?? false)
                        {
                            var codeDirectory = _codeDirectoryPath.Text;
                            if (string.IsNullOrWhiteSpace(codeDirectory))
                            {
                                MessageBox.Show(this, "Please select a code directory when code usage checking is enabled.", MessageBoxType.Error);
                                return;
                            }

                            // Parse file extensions
                            var extensions = _fileExtensionsInput.Text
                                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(e => e.Trim())
                                .Where(e => !string.IsNullOrWhiteSpace(e))
                                .ToArray();

                            if (extensions.Length == 0)
                            {
                                extensions = new[] { ".tsx" }; // Default
                            }

                            // Get pattern
                            string pattern;
                            if (_patternSelector.SelectedIndex == 4) // Custom regex
                            {
                                pattern = _customRegexInput.Text;
                                if (string.IsNullOrWhiteSpace(pattern))
                                {
                                    MessageBox.Show(this, "Please enter a custom regex pattern.", MessageBoxType.Error);
                                    return;
                                }
                            }
                            else
                            {
                                var selectedIndex = _patternSelector.SelectedIndex;
                                string patternKey;
                                switch (selectedIndex)
                                {
                                    case 0: patternKey = "t('key')"; break;
                                    case 1: patternKey = "i18n.t('key')"; break;
                                    case 2: patternKey = "translate('key')"; break;
                                    case 3: patternKey = "t.key"; break;
                                    default: patternKey = "t('key')"; break;
                                }
                                pattern = CodeScannerService.CommonPatterns.ContainsKey(patternKey) 
                                    ? CodeScannerService.CommonPatterns[patternKey] 
                                    : CodeScannerService.CommonPatterns["t('key')"];
                            }

                            _statusLabel.Text = "Scanning code files...";
                            usedKeysInCode = _codeScannerService.ScanCodeFiles(codeDirectory, extensions, pattern);
                            _statusLabel.Text = $"Found {usedKeysInCode.Count} translation keys in code";
                        }

                        // Compare (pass master sheet entries, can be null)
                        _currentComparison = _comparisonService.CompareTranslations(
                            masterEntries ?? new List<TranslationEntry>(), 
                            platformEntries, 
                            codeEntries,
                            usedKeysInCode);

                        // Update UI
                        UpdateResultsDisplay();

                        _statusLabel.Text = $"Comparison complete. Missing in Platform: {_currentComparison.MissingInPlatform.Count}, Different Translation: {_currentComparison.DifferentTranslation.Count}, Missing in Code: {_currentComparison.MissingInCode.Count}, Only in Code: {_currentComparison.OnlyInCode.Count}, Unused in Code: {_currentComparison.UnusedInCode.Count}";
                        _generateMissingCsvButton.Enabled = _currentComparison.MissingInPlatform.Count > 0;
                        _generateUpdateCsvButton.Enabled = _currentComparison.DifferentTranslation.Count > 0;
                        _generateMissingInCodeCsvButton.Enabled = _currentComparison.MissingInCode.Count > 0;
                        _generateOnlyInCodeCsvButton.Enabled = _currentComparison.OnlyInCode.Count > 0;
                        _generateUnusedInCodeCsvButton.Enabled = _currentComparison.UnusedInCode.Count > 0;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Error during comparison: {ex.Message}", MessageBoxType.Error);
                        _statusLabel.Text = "Error occurred";
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error: {ex.Message}", MessageBoxType.Error);
            }
        }

        private void UpdateResultsDisplay()
        {
            var displayItems = new List<ResultDisplayItem>();

            foreach (var entry in _currentComparison.MissingInPlatform)
            {
                displayItems.Add(new ResultDisplayItem
                {
                    Type = "Missing in Platform",
                    SheetFilename = entry.SheetFilename ?? string.Empty,
                    SheetKey = entry.SheetKey ?? string.Empty,
                    CodeFilename = entry.CodeFilename ?? string.Empty,
                    CodeKey = entry.CodeKey ?? string.Empty
                });
            }

            foreach (var entry in _currentComparison.DifferentTranslation)
            {
                displayItems.Add(new ResultDisplayItem
                {
                    Type = "Different Translation",
                    SheetFilename = entry.SheetFilename ?? string.Empty,
                    SheetKey = entry.SheetKey ?? string.Empty,
                    CodeFilename = entry.CodeFilename ?? string.Empty,
                    CodeKey = entry.CodeKey ?? string.Empty
                });
            }

            foreach (var entry in _currentComparison.MissingInCode)
            {
                displayItems.Add(new ResultDisplayItem
                {
                    Type = "Missing in Code",
                    SheetFilename = entry.SheetFilename ?? string.Empty,
                    SheetKey = entry.SheetKey ?? string.Empty,
                    CodeFilename = entry.CodeFilename ?? string.Empty,
                    CodeKey = entry.CodeKey ?? string.Empty
                });
            }

            foreach (var entry in _currentComparison.OnlyInCode)
            {
                displayItems.Add(new ResultDisplayItem
                {
                    Type = "Only in Code",
                    SheetFilename = entry.SheetFilename ?? string.Empty,
                    SheetKey = entry.SheetKey ?? string.Empty,
                    CodeFilename = entry.CodeFilename ?? string.Empty,
                    CodeKey = entry.CodeKey ?? string.Empty
                });
            }

            foreach (var entry in _currentComparison.UnusedInCode)
            {
                displayItems.Add(new ResultDisplayItem
                {
                    Type = "Unused in Code",
                    SheetFilename = entry.SheetFilename ?? string.Empty,
                    SheetKey = entry.SheetKey ?? string.Empty,
                    CodeFilename = entry.CodeFilename ?? string.Empty,
                    CodeKey = entry.CodeKey ?? string.Empty
                });
            }

            _resultsGrid.DataStore = displayItems;
        }

        private void OnGenerateMissingCsv(object sender, EventArgs e)
        {
            if (_currentComparison == null || _currentComparison.MissingInPlatform.Count == 0)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Save Missing in Platform CSV",
                Filters = { new FileFilter("CSV Files", "*.csv") },
                FileName = "missing_in_platform.csv"
            };

            if (dialog.ShowDialog(this) == DialogResult.Ok)
            {
                try
                {
                    _csvService.SaveCsvFile(_currentComparison.MissingInPlatform, dialog.FileName);
                    MessageBox.Show(this, $"Successfully saved {_currentComparison.MissingInPlatform.Count} missing in platform items to {dialog.FileName}", MessageBoxType.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error saving CSV: {ex.Message}", MessageBoxType.Error);
                }
            }
        }

        private void OnGenerateUpdateCsv(object sender, EventArgs e)
        {
            if (_currentComparison == null || _currentComparison.DifferentTranslation.Count == 0)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Save Different Translation CSV",
                Filters = { new FileFilter("CSV Files", "*.csv") },
                FileName = "different_translation.csv"
            };

            if (dialog.ShowDialog(this) == DialogResult.Ok)
            {
                try
                {
                    _csvService.SaveCsvFile(_currentComparison.DifferentTranslation, dialog.FileName);
                    MessageBox.Show(this, $"Successfully saved {_currentComparison.DifferentTranslation.Count} different translation items to {dialog.FileName}", MessageBoxType.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error saving CSV: {ex.Message}", MessageBoxType.Error);
                }
            }
        }

        private void OnGenerateMissingInCodeCsv(object sender, EventArgs e)
        {
            if (_currentComparison == null || _currentComparison.MissingInCode.Count == 0)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Save Missing in Code CSV",
                Filters = { new FileFilter("CSV Files", "*.csv") },
                FileName = "missing_in_code.csv"
            };

            if (dialog.ShowDialog(this) == DialogResult.Ok)
            {
                try
                {
                    _csvService.SaveCsvFile(_currentComparison.MissingInCode, dialog.FileName);
                    MessageBox.Show(this, $"Successfully saved {_currentComparison.MissingInCode.Count} missing in code items to {dialog.FileName}", MessageBoxType.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error saving CSV: {ex.Message}", MessageBoxType.Error);
                }
            }
        }

        private void OnGenerateOnlyInCodeCsv(object sender, EventArgs e)
        {
            if (_currentComparison == null || _currentComparison.OnlyInCode.Count == 0)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Save Only in Code CSV",
                Filters = { new FileFilter("CSV Files", "*.csv") },
                FileName = "only_in_code.csv"
            };

            if (dialog.ShowDialog(this) == DialogResult.Ok)
            {
                try
                {
                    _csvService.SaveCsvFile(_currentComparison.OnlyInCode, dialog.FileName);
                    MessageBox.Show(this, $"Successfully saved {_currentComparison.OnlyInCode.Count} only in code items to {dialog.FileName}", MessageBoxType.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error saving CSV: {ex.Message}", MessageBoxType.Error);
                }
            }
        }

        private void OnGenerateUnusedInCodeCsv(object sender, EventArgs e)
        {
            if (_currentComparison == null || _currentComparison.UnusedInCode.Count == 0)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Save Unused in Code CSV",
                Filters = { new FileFilter("CSV Files", "*.csv") },
                FileName = "unused_in_code.csv"
            };

            if (dialog.ShowDialog(this) == DialogResult.Ok)
            {
                try
                {
                    _csvService.SaveCsvFile(_currentComparison.UnusedInCode, dialog.FileName);
                    MessageBox.Show(this, $"Successfully saved {_currentComparison.UnusedInCode.Count} unused in code items to {dialog.FileName}", MessageBoxType.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error saving CSV: {ex.Message}", MessageBoxType.Error);
                }
            }
        }

        private void OnScanMigrationClicked(object sender, EventArgs e)
        {
            try
            {
                var oldJsonDir = _oldJsonDirectoryPath.Text;
                var newJsonDir = _newJsonDirectoryPath.Text;
                var codeDir = _migrationCodeDirectoryPath.Text;
                var extensionsText = _migrationFileExtensionsInput.Text;

                if (string.IsNullOrWhiteSpace(oldJsonDir) || string.IsNullOrWhiteSpace(newJsonDir) || string.IsNullOrWhiteSpace(codeDir))
                {
                    MessageBox.Show(this, "Please select old JSON directory, new JSON directory, and code directory", MessageBoxType.Warning);
                    return;
                }

                _migrationStatusLabel.Text = "Scanning...";

                // Parse file extensions
                var extensions = extensionsText.Split(',')
                    .Select(ext => ext.Trim())
                    .Where(ext => !string.IsNullOrWhiteSpace(ext))
                    .ToArray();

                if (extensions.Length == 0)
                {
                    extensions = new[] { ".tsx", ".ts" };
                }

                // Generate migration plan
                _currentMigrationResult = _keyMigrationService.GenerateMigrationPlan(
                    oldJsonDir,
                    newJsonDir,
                    codeDir,
                    extensions);

                // Update UI
                UpdateMigrationResultsDisplay();

                _migrationStatusLabel.Text = $"Scan complete. Found {_currentMigrationResult.TotalEntries} migration opportunities. Pending: {_currentMigrationResult.PendingCount}, Needs Review: {_currentMigrationResult.NeedsReviewCount}, No Match: {_currentMigrationResult.NoMatchCount}";
                _applySelectedMigrationsButton.Enabled = _currentMigrationResult.PendingCount > 0 || _currentMigrationResult.NeedsReviewCount > 0;
                _applyAllMigrationsButton.Enabled = _currentMigrationResult.PendingCount > 0 || _currentMigrationResult.NeedsReviewCount > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error during migration scan: {ex.Message}", MessageBoxType.Error);
                _migrationStatusLabel.Text = "Error occurred";
            }
        }

        private void UpdateMigrationResultsDisplay()
        {
            var displayItems = new List<MigrationDisplayItem>();

            if (_currentMigrationResult != null)
            {
                foreach (var entry in _currentMigrationResult.Entries)
                {
                    if (entry.CodeUsages.Count > 0)
                    {
                        // Create one row per code usage
                        foreach (var usage in entry.CodeUsages)
                        {
                            var statusText = entry.Status.ToString();
                            if (entry.HasDuplicateValue)
                            {
                                statusText += " (Duplicate Value)";
                            }
                            
                            displayItems.Add(new MigrationDisplayItem
                            {
                                Entry = entry,
                                Status = statusText,
                                OldKey = entry.OldKey,
                                NewKey = entry.NewKey,
                                OldNamespace = entry.OldNamespace,
                                NewNamespace = entry.NewNamespace,
                                FilePath = usage.FilePath,
                                LineNumber = usage.LineNumber.ToString()
                            });
                        }
                    }
                    else
                    {
                        // No code usages, but still show the entry
                        var statusText = entry.Status.ToString();
                        if (entry.HasDuplicateValue)
                        {
                            statusText += " (Duplicate Value)";
                        }
                        
                        displayItems.Add(new MigrationDisplayItem
                        {
                            Entry = entry,
                            Status = statusText,
                            OldKey = entry.OldKey,
                            NewKey = entry.NewKey,
                            OldNamespace = entry.OldNamespace,
                            NewNamespace = entry.NewNamespace,
                            FilePath = string.Empty,
                            LineNumber = string.Empty
                        });
                    }
                }
            }

            _migrationResultsGrid.DataStore = displayItems;
        }

        private void OnMigrationSelectionChanged(object sender, EventArgs e)
        {
            if (_migrationResultsGrid.SelectedItem is MigrationDisplayItem item && item.Entry != null)
            {
                // Show diff for selected item
                var diffText = new StringBuilder();
                
                // Add duplicate value warning if applicable
                if (item.Entry.HasDuplicateValue)
                {
                    diffText.AppendLine("⚠️ DUPLICATE VALUE DETECTED ⚠️");
                    diffText.AppendLine();
                    
                    if (item.Entry.DuplicateOldKeys.Count > 0)
                    {
                        diffText.AppendLine($"This value also exists in other OLD keys:");
                        foreach (var dupKey in item.Entry.DuplicateOldKeys)
                        {
                            diffText.AppendLine($"  - {dupKey}");
                        }
                        diffText.AppendLine();
                    }
                    
                    if (item.Entry.DuplicateNewKeys.Count > 0)
                    {
                        diffText.AppendLine($"This value also exists in other NEW keys:");
                        foreach (var dupKey in item.Entry.DuplicateNewKeys)
                        {
                            diffText.AppendLine($"  - {dupKey}");
                        }
                        diffText.AppendLine();
                    }
                    
                    diffText.AppendLine("Please review carefully to ensure the correct key is selected.");
                    diffText.AppendLine();
                    diffText.AppendLine("---");
                    diffText.AppendLine();
                }
                
                if (!string.IsNullOrWhiteSpace(item.FilePath))
                {
                    var diff = _codeMigrationService.GenerateDiff(item.FilePath, item.Entry);
                    diffText.Append(diff);
                }
                else
                {
                    diffText.Append("No file selected or no code usage found for this entry.");
                }
                
                _diffViewer.Text = diffText.ToString();
            }
        }

        private void OnApplySelectedMigrationsClicked(object sender, EventArgs e)
        {
            if (_currentMigrationResult == null)
                return;

            var selectedItems = _migrationResultsGrid.SelectedItems?.Cast<MigrationDisplayItem>().ToList();
            if (selectedItems == null || selectedItems.Count == 0)
            {
                MessageBox.Show(this, "Please select migrations to apply", MessageBoxType.Warning);
                return;
            }

            // Get unique entries from selected items
            var entriesToApply = selectedItems
                .Select(item => item.Entry)
                .Where(entry => entry != null && (entry.Status == MigrationStatus.Pending || entry.Status == MigrationStatus.NeedsReview))
                .Distinct()
                .ToList();

            if (entriesToApply.Count == 0)
            {
                MessageBox.Show(this, "No applicable migrations selected", MessageBoxType.Warning);
                return;
            }

            var result = MessageBox.Show(this, 
                $"Are you sure you want to apply {entriesToApply.Count} migration(s)? This will modify your code files. Backups will be created automatically.",
                "Confirm Migration",
                MessageBoxButtons.YesNo,
                MessageBoxType.Question);

            if (result == DialogResult.Yes)
            {
                ApplyMigrations(entriesToApply);
            }
        }

        private void OnApplyAllMigrationsClicked(object sender, EventArgs e)
        {
            if (_currentMigrationResult == null)
                return;

            var entriesToApply = _currentMigrationResult.Entries
                .Where(entry => entry.Status == MigrationStatus.Pending || entry.Status == MigrationStatus.NeedsReview)
                .ToList();

            if (entriesToApply.Count == 0)
            {
                MessageBox.Show(this, "No migrations to apply", MessageBoxType.Warning);
                return;
            }

            var result = MessageBox.Show(this,
                $"Are you sure you want to apply {entriesToApply.Count} migration(s)? This will modify your code files. Backups will be created automatically.",
                "Confirm Migration",
                MessageBoxButtons.YesNo,
                MessageBoxType.Question);

            if (result == DialogResult.Yes)
            {
                ApplyMigrations(entriesToApply);
            }
        }

        private void ApplyMigrations(List<KeyMigrationEntry> entries)
        {
            try
            {
                _migrationStatusLabel.Text = "Applying migrations...";
                Application.Instance.AsyncInvoke(() =>
                {
                    try
                    {
                        var results = _codeMigrationService.ApplyMigrations(entries);

                        var successCount = results.Count(r => r.Success);
                        var failCount = results.Count(r => !r.Success);

                        // Update display
                        UpdateMigrationResultsDisplay();

                        _migrationStatusLabel.Text = $"Migration complete. Applied: {successCount}, Failed: {failCount}";

                        if (failCount > 0)
                        {
                            var failedFiles = results.Where(r => !r.Success).SelectMany(r => r.FailedFiles).Distinct().ToList();
                            MessageBox.Show(this,
                                $"Some migrations failed:\n{string.Join("\n", failedFiles.Take(10))}" +
                                (failedFiles.Count > 10 ? $"\n... and {failedFiles.Count - 10} more" : ""),
                                "Migration Results",
                                MessageBoxType.Warning);
                        }
                        else
                        {
                            MessageBox.Show(this, $"Successfully applied {successCount} migration(s)", MessageBoxType.Information);
                        }

                        _applySelectedMigrationsButton.Enabled = _currentMigrationResult.PendingCount > 0 || _currentMigrationResult.NeedsReviewCount > 0;
                        _applyAllMigrationsButton.Enabled = _currentMigrationResult.PendingCount > 0 || _currentMigrationResult.NeedsReviewCount > 0;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Error applying migrations: {ex.Message}", MessageBoxType.Error);
                        _migrationStatusLabel.Text = "Error occurred";
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error: {ex.Message}", MessageBoxType.Error);
            }
        }

        private class MigrationDisplayItem
        {
            public KeyMigrationEntry Entry { get; set; }
            public string Status { get; set; }
            public string OldKey { get; set; }
            public string NewKey { get; set; }
            public string OldNamespace { get; set; }
            public string NewNamespace { get; set; }
            public string FilePath { get; set; }
            public string LineNumber { get; set; }
        }

        private class ResultDisplayItem
        {
            public string Type { get; set; }
            public string SheetFilename { get; set; }
            public string SheetKey { get; set; }
            public string CodeFilename { get; set; }
            public string CodeKey { get; set; }
        }
    }
}
