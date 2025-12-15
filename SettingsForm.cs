using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using HotCPU.Localization;

namespace HotCPU
{
    internal class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Action _onSettingsChanged;

        // Content Panels
        private Panel _contentPanel = null!;
        private Panel _panelGeneral = null!;
        private Panel _panelColors = null!;
        private Panel _panelSensors = null!;
        private Panel _panelTray = null!;
        private Panel _panelLogging = null!;
        
        // Nav Buttons
        private Button _btnGeneral = null!;
        private Button _btnColors = null!;
        private Button _btnSensors = null!;
        private Button _btnTray = null!;
        private Button _btnLogging = null!;

        // Original Fields
        private CheckedListBox _sensorsCheckList = null!;
        private CheckedListBox _traySensorsCheckList = null!;
        private readonly List<HardwareTemps> _availableHardware;

        // Logging Controls
        private Button _btnBrowseLog = null!;
        private CheckBox _chkEnableLogging = null!;
        private TextBox _txtLogPath = null!;
        private NumericUpDown _numLogInterval = null!;
        private ComboBox _cmbLogFormat = null!;
        private CheckedListBox _logSensorsCheckList = null!;
        private CheckBox _chkLogAvg = null!;
        private CheckBox _chkLogMin = null!;
        private CheckBox _chkLogMax = null!;

        // Controls
        private ComboBox _refreshIntervalCombo = null!;
        private NumericUpDown _warmThresholdNum = null!;
        private NumericUpDown _hotThresholdNum = null!;
        private NumericUpDown _criticalThresholdNum = null!;
        private CheckBox _startWithWindowsCheck = null!;
        private CheckBox _showTrayTempCheck = null!;
        private ComboBox _fontSizeCombo = null!;
        private CheckBox _useGradientCheck = null!;
        private Button _coolColorBtn = null!;
        private Button _warmColorBtn = null!;
        private Button _hotColorBtn = null!;
        private Button _criticalColorBtn = null!;
        private Button _saveButton = null!;
        private Button _cancelButton = null!;
        private ComboBox _languageCombo = null!;
        private Label _languageInfoLabel = null!;
        private List<LanguageOption> _languageOptions = new();

        // Live Updates
        private readonly TemperatureService? _tempService;
        private System.Windows.Forms.Timer? _refreshTimer;

        public SettingsForm(AppSettings settings, Action onSettingsChanged, List<HardwareTemps> availableHardware, TemperatureService? tempService = null)
        {
            _settings = settings;
            _onSettingsChanged = onSettingsChanged;
            _availableHardware = availableHardware;
            _tempService = tempService;
            
            // We can keep double buffering for smoothness, but no need for heavy composition if no bg image
            SetStyle(ControlStyles.DoubleBuffer | 
                     ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();

            InitializeComponent();
            LoadSettings();
            InitializeStartupState(); // Async check

            // Live Update Timer
            if (_tempService != null)
            {
                _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                _refreshTimer.Tick += OnRefreshTimerTick;
                _refreshTimer.Start();
            }
        }

        private void OnRefreshTimerTick(object? sender, EventArgs e)
        {
            if (_tempService == null) return;
            var reading = _tempService.CurrentReading;
            if (reading == null) return;

            var allSensors = reading.AllTemps.SelectMany(t => t.Sensors).ToDictionary(s => s.Identifier);

            // Update Sensors List
            for (int i = 0; i < _sensorsCheckList.Items.Count; i++)
            {
                if (_sensorsCheckList.Items[i] is SensorItem item && allSensors.TryGetValue(item.Id, out var sensor))
                {
                    item.Temperature = sensor.Temperature;
                }
            }
            // Force refresh of text
            _sensorsCheckList.Invalidate();

            // Update Logging List
            for (int i = 0; i < _logSensorsCheckList.Items.Count; i++)
            {
                if (_logSensorsCheckList.Items[i] is SensorItem item && allSensors.TryGetValue(item.Id, out var sensor))
                {
                    item.Temperature = sensor.Temperature;
                }
            }
            _logSensorsCheckList.Invalidate();
        }

        private async void InitializeStartupState()
        {
            try
            {
                // Check actual system state mostly for Store apps, but also Registry
                bool enabled = await StartupManager.IsStartupEnabledAsync();
                _startWithWindowsCheck.Checked = enabled;
            }
            catch { }
        }

        private void InitializeComponent()
        {
            Text = S("SettingsForm_Title");
            Size = new Size(600, 600);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // === Navigation Panel ===
            var navPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(200, 40, 40, 40),
                Padding = new Padding(5)
            };
            Controls.Add(navPanel);

            // === Content Panel ===
            _contentPanel = new BufferedPanel
            {
                Location = new Point(0, 40),
                Size = new Size(600, 440),
                BackColor = Color.Transparent
            };
            _contentPanel.Paint += (s, e) =>
            {
                using var brush = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
                e.Graphics.FillRectangle(brush, _contentPanel.ClientRectangle);
            };
            Controls.Add(_contentPanel);

            // Create Content Views
            _panelGeneral = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            BuildGeneralPanel(_panelGeneral);

            _panelColors = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            BuildColorsPanel(_panelColors);

            _panelSensors = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            BuildSensorsPanel(_panelSensors);

            _panelTray = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            BuildTrayPanel(_panelTray);

            _panelLogging = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            BuildLoggingPanel(_panelLogging);

            _btnGeneral = CreateNavButton("SettingsForm_Tab_General", navPanel, _panelGeneral);
            _btnColors = CreateNavButton("SettingsForm_Tab_Colors", navPanel, _panelColors);
            _btnSensors = CreateNavButton("SettingsForm_Tab_Sensors", navPanel, _panelSensors);
            _btnTray = CreateNavButton("SettingsForm_Tab_Tray", navPanel, _panelTray);
            _btnLogging = CreateNavButton("SettingsForm_Tab_Logging", navPanel, _panelLogging);

            // Default View
            ShowPanel(_panelGeneral);

            // Bottom Buttons
            var y = Size.Height - 80;
            var btnWidth = 80;
            var padding = 20;

            _saveButton = new Button
            {
                Text = S("Common_Save"),
                Size = new Size(btnWidth, 30),
                Location = new Point(ClientSize.Width - btnWidth - padding, y),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                UseVisualStyleBackColor = true
            };
            _saveButton.Click += SaveButton_Click;
            Controls.Add(_saveButton);

            _cancelButton = new Button
            {
                Text = S("Common_Cancel"),
                Size = new Size(btnWidth, 30),
                Location = new Point(_saveButton.Left - btnWidth - 10, y),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                UseVisualStyleBackColor = true
            };
            _cancelButton.Click += (s, e) => Close();
            Controls.Add(_cancelButton);
        }

        private Button CreateNavButton(string resourceKey, Panel parent, Panel targetPanel)
        {
            var btn = new Button
            {
                Text = S(resourceKey),
                Size = new Size(90, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Cursor = Cursors.Hand,
                Margin = new Padding(5, 0, 5, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => ShowPanel(targetPanel);
            parent.Controls.Add(btn);
            return btn;
        }

        private void ShowPanel(Panel panel)
        {
            _contentPanel.Controls.Clear();
            _contentPanel.Controls.Add(panel);
            
            // Validate visual state of buttons
            ResetNavButtons();
            if (panel == _panelGeneral) HighlightButton(_btnGeneral);
            else if (panel == _panelColors) HighlightButton(_btnColors);
            else if (panel == _panelSensors) HighlightButton(_btnSensors);
            else if (panel == _panelTray) HighlightButton(_btnTray);
            else if (panel == _panelLogging) HighlightButton(_btnLogging);
        }

        private void ResetNavButtons()
        {
            var c = Color.FromArgb(60, 60, 60);
            _btnGeneral.BackColor = c;
            _btnColors.BackColor = c;
            _btnSensors.BackColor = c;
            _btnTray.BackColor = c;
            _btnLogging.BackColor = c;
        }

        private void HighlightButton(Button btn)
        {
            btn.BackColor = Color.FromArgb(100, 100, 100); // Lighter selected
        }

        private void BuildGeneralPanel(Panel page)
        {
            int y = 20;
            int x = 20;

            AddLabel(page, S("SettingsForm_General_RefreshInterval"), x, y);
            _refreshIntervalCombo = CreateComboBox(page, 170, y - 3, 160);
            _refreshIntervalCombo.Items.AddRange(new object[]
            {
                S("SettingsForm_General_Interval_05"),
                S("SettingsForm_General_Interval_1"),
                S("SettingsForm_General_Interval_2"),
                S("SettingsForm_General_Interval_5")
            });
            y += 40;

            AddLabel(page, S("SettingsForm_General_WarmThreshold"), x, y);
            _warmThresholdNum = CreateNumericUpDown(page, 170, y - 3, 30, 100);
            y += 35;

            AddLabel(page, S("SettingsForm_General_HotThreshold"), x, y);
            _hotThresholdNum = CreateNumericUpDown(page, 170, y - 3, 30, 100);
            y += 35;

            AddLabel(page, S("SettingsForm_General_CriticalThreshold"), x, y);
            _criticalThresholdNum = CreateNumericUpDown(page, 170, y - 3, 30, 120);
            y += 40;

            AddLabel(page, S("SettingsForm_General_FontSize"), x, y);
            _fontSizeCombo = CreateComboBox(page, 170, y - 3, 160);
            _fontSizeCombo.Items.AddRange(new object[]
            {
                S("SettingsForm_General_FontSmall"),
                S("SettingsForm_General_FontMedium"),
                S("SettingsForm_General_FontLarge")
            });
            y += 40;

            AddLabel(page, S("SettingsForm_General_Language"), x, y);
            _languageCombo = CreateComboBox(page, 170, y - 3, 200);
            PopulateLanguageCombo();
            y += 40;

            _languageInfoLabel = new Label
            {
                Text = S("SettingsForm_General_LanguageRestart"),
                Location = new Point(x + 5, y),
                AutoSize = true,
                ForeColor = Color.DimGray
            };
            page.Controls.Add(_languageInfoLabel);
            y += 30;

            _startWithWindowsCheck = new CheckBox
            {
                Text = S("SettingsForm_General_StartWithWindows"),
                Location = new Point(x, y),
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(_startWithWindowsCheck);
            y += 30;

            _showTrayTempCheck = new CheckBox
            {
                Text = S("SettingsForm_General_ShowTrayTemperature"),
                Location = new Point(x, y),
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(_showTrayTempCheck);
            y += 30;
        }

        private void BuildColorsPanel(Panel page)
        {
            int y = 20;
            int x = 20;

            _useGradientCheck = new CheckBox
            {
                Text = S("SettingsForm_Colors_UseGradients"),
                Location = new Point(x, y),
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            _useGradientCheck.CheckedChanged += (s, e) => UpdateColorButtonsEnabled();
            page.Controls.Add(_useGradientCheck);
            y += 40;

            AddLabel(page, S("SettingsForm_Colors_Cool"), x, y);
            _coolColorBtn = CreateColorButton(page, 150, y - 3, Color.White);
            y += 40;

            AddLabel(page, S("SettingsForm_Colors_Warm"), x, y);
            _warmColorBtn = CreateColorButton(page, 150, y - 3, Color.Orange);
            y += 40;

            AddLabel(page, S("SettingsForm_Colors_Hot"), x, y);
            _hotColorBtn = CreateColorButton(page, 150, y - 3, Color.OrangeRed);
            y += 40;

            AddLabel(page, S("SettingsForm_Colors_Critical"), x, y);
            _criticalColorBtn = CreateColorButton(page, 150, y - 3, Color.Red);
            y += 40;
        }

        private void BuildSensorsPanel(Panel page)
        {
            var label = new Label
            {
                Text = S("SettingsForm_Sensors_Title"),
                Location = new Point(10, 10),
                AutoSize = true
            };
            page.Controls.Add(label);

            var chkSelectAll = new CheckBox 
            { 
                Text = S("SettingsForm_SelectAll"),
                Location = new Point(450, 8), 
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(chkSelectAll);

            _sensorsCheckList = new CheckedListBox
            {
                Location = new Point(10, 35),
                Size = new Size(540, 370),
                CheckOnClick = true
            };
            page.Controls.Add(_sensorsCheckList);

            chkSelectAll.CheckedChanged += (s, e) =>
            {
                for (int i = 0; i < _sensorsCheckList.Items.Count; i++)
                    _sensorsCheckList.SetItemChecked(i, chkSelectAll.Checked);
            };
        }

        private void BuildTrayPanel(Panel page)
        {
            var label = new Label
            {
                Text = S("SettingsForm_Tray_Title"),
                Location = new Point(10, 10),
                AutoSize = true
            };
            page.Controls.Add(label);

            var chkSelectAll = new CheckBox 
            { 
                Text = S("SettingsForm_SelectAll"),
                Location = new Point(450, 8), 
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(chkSelectAll);

            _traySensorsCheckList = new CheckedListBox
            {
                Location = new Point(10, 35),
                Size = new Size(540, 370),
                CheckOnClick = true
            };
            page.Controls.Add(_traySensorsCheckList);

            chkSelectAll.CheckedChanged += (s, e) =>
            {
                for (int i = 0; i < _traySensorsCheckList.Items.Count; i++)
                    _traySensorsCheckList.SetItemChecked(i, chkSelectAll.Checked);
            };
        }

        private void BuildLoggingPanel(Panel page)
        {
            int y = 20;
            int x = 20;

            _chkEnableLogging = new CheckBox
            {
                Text = S("SettingsForm_Logging_Enable"),
                Location = new Point(x, y),
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(_chkEnableLogging);
            y += 40;

            AddLabel(page, S("SettingsForm_Logging_LogPath"), x, y);
            y += 25;
            _txtLogPath = new TextBox
            {
                Location = new Point(x, y),
                Width = 440,
                BorderStyle = BorderStyle.Fixed3D
            };
            page.Controls.Add(_txtLogPath);

            _btnBrowseLog = new Button
            {
                Text = "...",
                Location = new Point(x + 450, y - 2),
                Size = new Size(40, 24),
                UseVisualStyleBackColor = true
            };
            _btnBrowseLog.Click += (s, e) => 
            {
                using var dlg = new SaveFileDialog { Filter = S("SettingsForm_Logging_BrowseFilter") };
                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtLogPath.Text = dlg.FileName;
            };
            page.Controls.Add(_btnBrowseLog);
            y += 40;

            AddLabel(page, S("SettingsForm_Logging_Interval"), x, y);
            _numLogInterval = CreateNumericUpDown(page, 150, y - 3, 1, 3600);
            y += 35;

            AddLabel(page, S("SettingsForm_Logging_Format"), x, y);
            _cmbLogFormat = CreateComboBox(page, 150, y - 3, 100);
            _cmbLogFormat.Items.AddRange(new object[]
            {
                S("SettingsForm_Logging_Format_Csv"),
                S("SettingsForm_Logging_Format_Json"),
                S("SettingsForm_Logging_Format_Txt")
            });
            y += 40;

            // Stats
            var grpStats = new GroupBox
            {
                Text = S("SettingsForm_Logging_Stats"),
                Location = new Point(x, y),
                Size = new Size(340, 50)
            };
            _chkLogAvg = new CheckBox { Text = S("SettingsForm_Logging_Stats_Avg"), Location = new Point(10, 20), AutoSize = true, UseVisualStyleBackColor = true };
            _chkLogMin = new CheckBox { Text = S("SettingsForm_Logging_Stats_Min"), Location = new Point(70, 20), AutoSize = true, UseVisualStyleBackColor = true };
            _chkLogMax = new CheckBox { Text = S("SettingsForm_Logging_Stats_Max"), Location = new Point(130, 20), AutoSize = true, UseVisualStyleBackColor = true };
            grpStats.Controls.Add(_chkLogAvg);
            grpStats.Controls.Add(_chkLogMin);
            grpStats.Controls.Add(_chkLogMax);
            page.Controls.Add(grpStats);
            y += 60;

            // Sensors
            AddLabel(page, S("SettingsForm_Logging_SelectSensors"), x, y);
            
            var chkSelectAll = new CheckBox 
            { 
                Text = S("SettingsForm_SelectAll"), 
                Location = new Point(x + 400, y - 2), 
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(chkSelectAll);

            y += 25;
            _logSensorsCheckList = new CheckedListBox
            {
                Location = new Point(x, y),
                Size = new Size(540, 120),
                CheckOnClick = true
            };
            page.Controls.Add(_logSensorsCheckList);

            chkSelectAll.CheckedChanged += (s, e) =>
            {
                for (int i = 0; i < _logSensorsCheckList.Items.Count; i++)
                    _logSensorsCheckList.SetItemChecked(i, chkSelectAll.Checked);
            };
        }

        private void AddLabel(Panel page, string text, int x, int y)
        {
            var label = new Label { Text = text, Location = new Point(x, y), AutoSize = true };
            page.Controls.Add(label);
        }

        private ComboBox CreateComboBox(Panel page, int x, int y, int width)
        {
            var combo = new ComboBox
            {
                Location = new Point(x, y), Width = width, DropDownStyle = ComboBoxStyle.DropDownList
            };
            page.Controls.Add(combo);
            return combo;
        }

        private NumericUpDown CreateNumericUpDown(Panel page, int x, int y, int min, int max)
        {
            var num = new NumericUpDown
            {
                Location = new Point(x, y), Width = 80, Minimum = min, Maximum = max
            };
            page.Controls.Add(num);
            return num;
        }

        private Button CreateColorButton(Panel page, int x, int y, Color defaultColor)
        {
            var btn = new Button
            {
                Location = new Point(x, y), Size = new Size(80, 24), BackColor = defaultColor, FlatStyle = FlatStyle.Standard, Text = ""
            };
            btn.Click += (s, e) =>
            {
                using var dialog = new ColorDialog { Color = btn.BackColor };
                if (dialog.ShowDialog() == DialogResult.OK) btn.BackColor = dialog.Color;
            };
            page.Controls.Add(btn);
            return btn;
        }

        private void UpdateColorButtonsEnabled()
        {
            var enabled = _useGradientCheck.Checked;
            _coolColorBtn.Enabled = enabled;
            _warmColorBtn.Enabled = enabled;
            _hotColorBtn.Enabled = enabled;
            _criticalColorBtn.Enabled = enabled;
        }

        private void PopulateLanguageCombo()
        {
            var options = new List<LanguageOption>
            {
                new LanguageOption(S("SettingsForm_General_LanguageAuto"), null)
            };

            var cultures = CultureInfo
                .GetCultures(CultureTypes.SpecificCultures | CultureTypes.NeutralCultures)
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .GroupBy(c => c.Name)
                .Select(g => g.First())
                .OrderBy(c => c.EnglishName, StringComparer.OrdinalIgnoreCase);

            foreach (var culture in cultures)
            {
                var displayName = $"{culture.EnglishName} [{culture.Name}]";
                options.Add(new LanguageOption(displayName, culture.Name));
            }

            _languageOptions = options;
            _languageCombo.DataSource = _languageOptions;
            _languageCombo.DisplayMember = nameof(LanguageOption.DisplayName);
            _languageCombo.ValueMember = nameof(LanguageOption.CultureCode);
        }

        private void LoadSettings()
        {
            _refreshIntervalCombo.SelectedIndex = _settings.RefreshIntervalMs switch
            {
                500 => 0, 1000 => 1, 2000 => 2, 5000 => 3, _ => 1
            };

            _warmThresholdNum.Value = _settings.WarmThreshold;
            _hotThresholdNum.Value = _settings.HotThreshold;
            _criticalThresholdNum.Value = _settings.CriticalThreshold;

            _fontSizeCombo.SelectedIndex = _settings.FontSize switch
            {
                10 => 0, 12 => 1, 14 => 2, _ => 2
            };

            if (_languageOptions.Count == 0)
            {
                PopulateLanguageCombo();
            }
            var languageValue = _settings.Language;
            var languageIndex = 0;
            if (!string.IsNullOrWhiteSpace(languageValue))
            {
                languageIndex = _languageOptions.FindIndex(option =>
                    string.Equals(option.CultureCode, languageValue, StringComparison.OrdinalIgnoreCase));
                if (languageIndex < 0)
                {
                    languageIndex = 0;
                }
            }
            _languageCombo.SelectedIndex = languageIndex;

            _startWithWindowsCheck.Checked = _settings.StartWithWindows;
            _showTrayTempCheck.Checked = _settings.ShowTrayIconTemperature;
            
            // Colors
            _useGradientCheck.Checked = _settings.UseGradientColors;
            _coolColorBtn.BackColor = _settings.GetCoolColorValue();
            _warmColorBtn.BackColor = _settings.GetWarmColorValue();
            _hotColorBtn.BackColor = _settings.GetHotColorValue();
            _criticalColorBtn.BackColor = _settings.GetCriticalColorValue();
            UpdateColorButtonsEnabled();

            // Sensors
            _sensorsCheckList.Items.Clear();
            foreach (var hw in _availableHardware)
            {
                foreach (var sensor in hw.Sensors)
                {
                    bool isVisible = !_settings.HiddenSensorIds.Contains(sensor.Identifier);
                    string displayName = $"{hw.Name} - {sensor.Name}";
                    _sensorsCheckList.Items.Add(new SensorItem(displayName, sensor.Identifier, sensor.Temperature), isVisible);
                }
            }

            // Tray Sensors
            _traySensorsCheckList.Items.Clear();
            foreach (var hw in _availableHardware)
            {
                foreach (var sensor in hw.Sensors)
                {
                    bool isSelected = _settings.TraySensorIds.Contains(sensor.Identifier);
                    string displayName = $"{hw.Name} - {sensor.Name}";
                    _traySensorsCheckList.Items.Add(new SensorItem(displayName, sensor.Identifier, sensor.Temperature), isSelected);
                }
            }

            // Logging
            _chkEnableLogging.Checked = _settings.LogEnabled;
            _txtLogPath.Text = _settings.LogPath;
            _numLogInterval.Value = Math.Max(1, _settings.LogIntervalSeconds);
            _cmbLogFormat.SelectedItem = _settings.LogFormat;
            
            _chkLogAvg.Checked = _settings.LogAverage;
            _chkLogMin.Checked = _settings.LogMin;
            _chkLogMax.Checked = _settings.LogMax;

            _logSensorsCheckList.Items.Clear();
            foreach (var hw in _availableHardware)
            {
                foreach (var sensor in hw.Sensors)
                {
                    bool isLogged = _settings.LogSensorIds.Contains(sensor.Identifier);
                    string displayName = $"{hw.Name} - {sensor.Name}";
                    _logSensorsCheckList.Items.Add(new SensorItem(displayName, sensor.Identifier, sensor.Temperature), isLogged);
                }
            }
        }
        
        private static string S(string key) => LocalizationService.GetString(key);

        private class SensorItem
        {
            public string Name { get; }
            public string Id { get; }
            public float Temperature { get; set; }
            public SensorItem(string name, string id, float temp) { Name = name; Id = id; Temperature = temp; }
            public override string ToString()
            {
                var roundedTemp = (int)Math.Round(Temperature);
                return LocalizationService.Format("SensorItem_Format", Name, roundedTemp);
            }
        }

        private sealed class LanguageOption
        {
            public LanguageOption(string displayName, string? cultureCode)
            {
                DisplayName = displayName;
                CultureCode = cultureCode;
            }

            public string DisplayName { get; }
            public string? CultureCode { get; }

            public override string ToString() => DisplayName;
        }

        private async void SaveButton_Click(object? sender, EventArgs e)
        {
            _settings.RefreshIntervalMs = _refreshIntervalCombo.SelectedIndex switch
            {
                0 => 500,
                1 => 1000,
                2 => 2000,
                3 => 5000,
                _ => 1000
            };

            _settings.WarmThreshold = (int)_warmThresholdNum.Value;
            _settings.HotThreshold = (int)_hotThresholdNum.Value;
            _settings.CriticalThreshold = (int)_criticalThresholdNum.Value;

            _settings.FontSize = _fontSizeCombo.SelectedIndex switch
            {
                0 => 10,
                1 => 12,
                2 => 14,
                _ => 14
            };

            if (_languageCombo.SelectedItem is LanguageOption selectedLanguage)
            {
                _settings.Language = selectedLanguage.CultureCode;
            }

            _settings.StartWithWindows = _startWithWindowsCheck.Checked;
            _settings.ShowTrayIconTemperature = _showTrayTempCheck.Checked;
            _settings.UseGradientColors = _useGradientCheck.Checked;

            _settings.SetCoolColor(_coolColorBtn.BackColor);
            _settings.SetWarmColor(_warmColorBtn.BackColor);
            _settings.SetHotColor(_hotColorBtn.BackColor);
            _settings.SetCriticalColor(_criticalColorBtn.BackColor);

            // Update hidden sensors
            _settings.HiddenSensorIds.Clear();
            for (int i = 0; i < _sensorsCheckList.Items.Count; i++)
            {
                // If UNCHECKED, it means hidden
                if (!_sensorsCheckList.GetItemChecked(i))
                {
                    if (_sensorsCheckList.Items[i] is SensorItem item)
                    {
                        _settings.HiddenSensorIds.Add(item.Id);
                    }
                }
            }

            // Update System Startup logic
            await StartupManager.SetStartupEnabledAsync(_startWithWindowsCheck.Checked);

            // Save Tray Sensors
            _settings.TraySensorIds.Clear();
            for (int i = 0; i < _traySensorsCheckList.Items.Count; i++)
            {
                if (_traySensorsCheckList.GetItemChecked(i))
                {
                    if (_traySensorsCheckList.Items[i] is SensorItem item)
                    {
                        _settings.TraySensorIds.Add(item.Id);
                    }
                }
            }

            // Save Logging settings
            _settings.LogEnabled = _chkEnableLogging.Checked;
            _settings.LogPath = _txtLogPath.Text;
            _settings.LogIntervalSeconds = (int)_numLogInterval.Value;
            _settings.LogFormat = _cmbLogFormat.SelectedItem?.ToString() ?? "CSV";
            _settings.LogAverage = _chkLogAvg.Checked;
            _settings.LogMin = _chkLogMin.Checked;
            _settings.LogMax = _chkLogMax.Checked;

            _settings.LogSensorIds.Clear();
            for (int i = 0; i < _logSensorsCheckList.Items.Count; i++)
            {
                if (_logSensorsCheckList.GetItemChecked(i))
                {
                    if (_logSensorsCheckList.Items[i] is SensorItem item)
                    {
                        _settings.LogSensorIds.Add(item.Id);
                    }
                }
            }

            _settings.Save();
            _onSettingsChanged();
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        private class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                SetStyle(ControlStyles.DoubleBuffer | 
                         ControlStyles.UserPaint | 
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.ResizeRedraw, true);
                UpdateStyles();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0014) // WM_ERASEBKGND
                {
                    m.Result = (IntPtr)1;
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
