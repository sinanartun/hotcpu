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
        
        // Category tab controls (sensor visibility)
        private FlowLayoutPanel? _sensorNavPanel;
        private Panel? _sensorContentPanel;
        private Dictionary<string, Button> _categoryButtons = new();
        private Dictionary<string, CheckedListBox> _categoryLists = new();

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
        private TextBox _refreshIntervalNum = null!;
        private NumericUpDown _warmThresholdNum = null!;
        private NumericUpDown _hotThresholdNum = null!;
        private NumericUpDown _criticalThresholdNum = null!;
        private CheckBox _startWithWindowsCheck = null!;
        private CheckBox _showTrayTempCheck = null!;
        private ComboBox _fontSizeCombo = null!;
        private ComboBox _fontFamilyCombo = null!;
        private List<string> _allFontFamilies = new();
        private Button _fontSizeMinusButton = null!;
        private Button _fontSizePlusButton = null!;
        private ComboBox _themeCombo = null!;
        private Button _lightTextColorBtn = null!;
        private Button _darkTextColorBtn = null!;
        private CheckBox _useGradientCheck = null!;
        private Button _coolColorBtn = null!;
        private Button _warmColorBtn = null!;
        private Button _hotColorBtn = null!;
        private Button _criticalColorBtn = null!;
        private GradientThresholdSlider _gradientSlider = null!;
        private Button _saveButton = null!;
        private Button _cancelButton = null!;
        private ComboBox _languageCombo = null!;
        private Label _languageInfoLabel = null!;
        private List<LanguageOption> _languageOptions = new();
        private int _initialLanguageIndex = -1;
        private bool _languageWarningShown = false;
        private bool _isLoadingSettings = false;
        private static readonly int[] FontSizes = { 10, 12, 13, 14, 15, 16 };

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
            ApplyTheme();
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

            var allSensors = reading.AllTemps.SelectMany(t => t.Sensors).DistinctBy(s => s.Identifier).ToDictionary(s => s.Identifier);

            // Update all category lists
            foreach (var list in _categoryLists.Values)
            {
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (list.Items[i] is SensorItem item && allSensors.TryGetValue(item.Id, out var sensor))
                    {
                        // Debug: Log extreme temperature values during refresh
                        if (item.Unit == "°C" && (sensor.Value > 500 || sensor.Value < -50))
                        {
                            System.Diagnostics.Debug.WriteLine($"[HotCPU REFRESH] EXTREME TEMP in CurrentReading: {item.Name} = {sensor.Value}°C (ID: {item.Id})");
                        }
                        item.Value = sensor.Value;
                    }
                }
                list.Invalidate();
            }

            // Update Logging List
            for (int i = 0; i < _logSensorsCheckList.Items.Count; i++)
            {
                if (_logSensorsCheckList.Items[i] is SensorItem item && allSensors.TryGetValue(item.Id, out var sensor))
                {
                    item.Value = sensor.Value;
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

        private void HandleStartupResult(StartupChangeResult result, bool requestedEnable)
        {
            // Only bother the user when they asked to turn auto-start ON and it didn't take.
            if (!requestedEnable || result == StartupChangeResult.Success) return;

            string message = result switch
            {
                StartupChangeResult.DisabledByUser =>
                    "Windows is blocking HotCPU from starting automatically.\n\n" +
                    "Open Task Manager \u2192 Startup apps and enable HotCPU, or enable it in Settings \u2192 Apps \u2192 Startup.",
                StartupChangeResult.DisabledByPolicy =>
                    "Your organization's policy prevents apps from starting with Windows.\n\n" +
                    "Contact your IT administrator to enable this.",
                _ =>
                    "Could not enable 'Start with Windows'. Please try again or set it manually from Windows Settings \u2192 Apps \u2192 Startup."
            };

            MessageBox.Show(this, message, "Start with Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                // BackColor set by ApplyTheme
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
                bool isDark = Helpers.ThemeHelper.IsDarkMode(_settings);
                Color overlayColor = isDark ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(150, 255, 255, 255);
                using var brush = new SolidBrush(overlayColor);
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
                // Colors set by ApplyTheme
                Cursor = Cursors.Hand,
                Margin = new Padding(5, 0, 5, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (s, e) => ShowPanel(targetPanel);
            parent.Controls.Add(btn);
            return btn;
        }

        private void ApplyTheme()
        {
            bool isDark = Helpers.ThemeHelper.IsDarkMode(_settings);
            Color bgColor = Helpers.ThemeHelper.GetBackgroundColor(isDark);
            Color surfaceColor = Helpers.ThemeHelper.GetSurfaceColor(isDark);
            Color textColor = Helpers.ThemeHelper.GetTextColor(isDark);
            Color navColor = Helpers.ThemeHelper.GetNavBackgroundColor(isDark);
            Color borderColor = Helpers.ThemeHelper.GetBorderColor(isDark);

            BackColor = bgColor;
            ForeColor = textColor;

            // Update Navigation Panel
            foreach (Control control in Controls)
            {
                if (control is FlowLayoutPanel nav)
                {
                    // "Industrial" Header Background
                    nav.BackColor = isDark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(230, 230, 230);
                    // Buttons are updated below
                }
            }

            // Sync Top Nav Buttons
            ResetNavButtons();
            if (_contentPanel.Controls.Contains(_panelGeneral)) HighlightButton(_btnGeneral);
            else if (_contentPanel.Controls.Contains(_panelColors)) HighlightButton(_btnColors);
            else if (_contentPanel.Controls.Contains(_panelSensors)) HighlightButton(_btnSensors);
            else if (_contentPanel.Controls.Contains(_panelTray)) HighlightButton(_btnTray);
            else if (_contentPanel.Controls.Contains(_panelLogging)) HighlightButton(_btnLogging);

            // Update All Content Panels (visible and hidden)
            _contentPanel.Invalidate(); // Redraw background
            UpdateControlTheme(_panelGeneral, surfaceColor, textColor, borderColor);
            UpdateControlTheme(_panelColors, surfaceColor, textColor, borderColor);
            UpdateControlTheme(_panelSensors, surfaceColor, textColor, borderColor);
            UpdateControlTheme(_panelTray, surfaceColor, textColor, borderColor);
            UpdateControlTheme(_panelLogging, surfaceColor, textColor, borderColor);

            // Bottom Buttons
            // Bottom Buttons
            _saveButton.BackColor = isDark ? navColor : SystemColors.ButtonFace;
            _saveButton.ForeColor = textColor;
            _saveButton.FlatStyle = FlatStyle.Flat;
            _saveButton.FlatAppearance.BorderColor = borderColor;

            _cancelButton.BackColor = isDark ? navColor : SystemColors.ButtonFace;
            _cancelButton.ForeColor = textColor;
            _cancelButton.FlatStyle = FlatStyle.Flat;
            _cancelButton.FlatAppearance.BorderColor = borderColor;

            // Refresh Sensor Nav Buttons (restore active/inactive states)
            var activeCategory = _categoryLists.FirstOrDefault(x => x.Value.Visible).Key;
            if (activeCategory != null)
            {
                ShowSensorCategory(activeCategory);
            }
        }

        private void UpdateControlTheme(Control parent, Color surfaceColor, Color textColor, Color borderColor)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Panel p && p != _contentPanel)
                {
                    UpdateControlTheme(p, surfaceColor, textColor, borderColor);
                }
                else if (c is Label || c is CheckBox || c is GroupBox)
                {
                    c.ForeColor = textColor;
                    if (c is GroupBox gb) UpdateControlTheme(gb, surfaceColor, textColor, borderColor);
                }
                else if (c is Button btn)
                {
                    // Skip Color buttons (they have their own specific colors)
                    if (IsColorButton(btn))
                        continue;

                    // Small buttons (like +/-)
                    btn.BackColor = surfaceColor;
                    btn.ForeColor = textColor;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = borderColor;
                }
                else if (c is TextBox || c is NumericUpDown || c is ComboBox || c is ListBox)
                {
                    c.BackColor = surfaceColor;
                    c.ForeColor = textColor;
                    if (c is ListBox lb) 
                    { 
                        lb.BorderStyle = BorderStyle.FixedSingle; 
                    }
                }
                else if (c is GradientThresholdSlider slider)
                {
                    slider.BackColor = surfaceColor;
                    slider.ForeColor = textColor;
                }
                // TabControl case removed as we no longer use it
            }
        }

        private bool IsColorButton(Button btn)
        {
            return btn == _coolColorBtn || btn == _warmColorBtn || btn == _hotColorBtn || btn == _criticalColorBtn;
        }

        private System.Windows.Forms.Timer _repeatTimer = null!;
        private int _repeatStep;

        private void StepInterval(int step)
        {
            if (decimal.TryParse(_refreshIntervalNum.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal currentVal))
            {
                decimal newValue = currentVal + (step * 0.1M);
                newValue = Math.Clamp(newValue, 0.1M, 60.0M);
                _refreshIntervalNum.Text = newValue.ToString("0.0", CultureInfo.InvariantCulture);
            }
            else
            {
                _refreshIntervalNum.Text = "1.0";
            }
        }

        private void StartRepeat(int step)
        {
            _repeatStep = step;
            StepInterval(_repeatStep); // Immediate first step
            
            if (_repeatTimer == null)
            {
                _repeatTimer = new System.Windows.Forms.Timer { Interval = 40 }; // 40ms repeat rate (Fast)
                _repeatTimer.Tick += (s, e) => StepInterval(_repeatStep);
            }
            _repeatTimer.Start();
        }

        private void StopRepeat()
        {
            _repeatTimer?.Stop();
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
            bool isDark = Helpers.ThemeHelper.IsDarkMode(_settings);
            
            // "Industrial" Standard:
            // Light: Header is Light Gray (#E6E6E6). Inactive tabs are same color. Text is DimGray.
            // Dark: Header is Dark Gray (#2D2D2D). Inactive tabs are same. Text is LightGray.
            
            Color c = isDark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(230, 230, 230);
            Color text = isDark ? Color.LightGray : Color.DimGray;

            _btnGeneral.BackColor = c; _btnGeneral.ForeColor = text;
            _btnColors.BackColor = c; _btnColors.ForeColor = text;
            _btnSensors.BackColor = c; _btnSensors.ForeColor = text;
            _btnTray.BackColor = c; _btnTray.ForeColor = text;
            _btnLogging.BackColor = c; _btnLogging.ForeColor = text;
            
            // Reset Font to regular
            _btnGeneral.Font = new Font(Font, FontStyle.Regular);
            _btnColors.Font = new Font(Font, FontStyle.Regular);
            _btnSensors.Font = new Font(Font, FontStyle.Regular);
            _btnTray.Font = new Font(Font, FontStyle.Regular);
            _btnLogging.Font = new Font(Font, FontStyle.Regular);
        }

        private void HighlightButton(Button btn)
        {
            bool isDark = Helpers.ThemeHelper.IsDarkMode(_settings);
            // Active state: High contrast against the header
            // Light: White background, Black text
            // Dark: Slightly lighter than header/background? Or different shade? 
            // Let's use darker than header for active in dark mode to look "pressed" or lighter to look "raised"?
            // Standard approach: Active tab matches content background.
            
            btn.BackColor = isDark ? Helpers.ThemeHelper.GetBackgroundColor(true) : Color.White;
            btn.ForeColor = isDark ? Color.White : Color.Black;
            btn.Font = new Font(Font, FontStyle.Bold);
        }

        private void BuildGeneralPanel(Panel page)
        {
            int y = 20;
            int x = 20;

            AddLabel(page, S("SettingsForm_General_RefreshInterval"), x, y);
            
            _refreshIntervalNum = new TextBox
            {
                Location = new Point(170, y - 3),
                Width = 110,
                Text = "1.0",
                TextAlign = HorizontalAlignment.Right
            };
            // Basic validation to allow only numbers and decimal point
            _refreshIntervalNum.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && (e.KeyChar != '.') && (e.KeyChar != ','))
                {
                    e.Handled = true;
                }
                // Convert comma to dot? Or handle culture? Using InvariantCulture for logic, so let's stick to dot visual if possible or allow system.
            };
            // Validate on leave
            _refreshIntervalNum.Leave += (s, e) =>
            {
                if (decimal.TryParse(_refreshIntervalNum.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
                {
                    val = Math.Clamp(val, 0.1M, 60.0M);
                    _refreshIntervalNum.Text = val.ToString("0.0", CultureInfo.InvariantCulture);
                }
                else
                {
                    _refreshIntervalNum.Text = "1.0";
                }
            };
            page.Controls.Add(_refreshIntervalNum);
            
            // Interval Buttons
            int btnX = 170 + 110 + 6;
            int btnSize = 24;
            
            var btnIntervalMinus = new Button
            {
                Text = "-",
                Size = new Size(btnSize, btnSize),
                Location = new Point(btnX, y - 3),
                UseVisualStyleBackColor = true
            };
            btnIntervalMinus.MouseDown += (s, e) => StartRepeat(-1);
            btnIntervalMinus.MouseUp += (s, e) => StopRepeat();
            btnIntervalMinus.MouseLeave += (s, e) => StopRepeat();
            page.Controls.Add(btnIntervalMinus);

            var btnIntervalPlus = new Button
            {
                Text = "+",
                Size = new Size(btnSize, btnSize),
                Location = new Point(btnX + btnSize + 4, y - 3),
                UseVisualStyleBackColor = true
            };
            btnIntervalPlus.MouseDown += (s, e) => StartRepeat(1);
            btnIntervalPlus.MouseUp += (s, e) => StopRepeat();
            btnIntervalPlus.MouseLeave += (s, e) => StopRepeat();
            page.Controls.Add(btnIntervalPlus);

            y += 40;


            // Thresholds moved to Colors tab
            
            AddLabel(page, S("SettingsForm_General_FontSize"), x, y);
            _fontSizeCombo = CreateComboBox(page, 170, y - 3, 110);
            _fontSizeCombo.Items.AddRange(FontSizes.Select(s => s.ToString()).Cast<object>().ToArray());
            _fontSizeCombo.SelectedIndexChanged += (s, e) => ApplyFontSizeImmediate();

            int fontButtonsX = 170 + 110 + 6;
            int fontButtonsY = y - 3;
            int fontButtonSize = 24;

            _fontSizeMinusButton = new Button
            {
                Text = "-",
                Size = new Size(fontButtonSize, fontButtonSize),
                Location = new Point(fontButtonsX, fontButtonsY),
                UseVisualStyleBackColor = true
            };
            _fontSizeMinusButton.Click += (s, e) => StepFontSize(-1);
            page.Controls.Add(_fontSizeMinusButton);

            _fontSizePlusButton = new Button
            {
                Text = "+",
                Size = new Size(fontButtonSize, fontButtonSize),
                Location = new Point(fontButtonsX + fontButtonSize + 4, fontButtonsY),
                UseVisualStyleBackColor = true
            };
            _fontSizePlusButton.Click += (s, e) => StepFontSize(1);
            page.Controls.Add(_fontSizePlusButton);
            y += 40;

            // Tray Font Family
            AddLabel(page, "Tray Text Font", x, y);
            _fontFamilyCombo = CreateComboBox(page, 170, y - 3, 220);
            _fontFamilyCombo.DrawMode = DrawMode.OwnerDrawFixed;
            _fontFamilyCombo.DropDownStyle = ComboBoxStyle.DropDown; 
            _fontFamilyCombo.AutoCompleteMode = AutoCompleteMode.None; // Disable standard auto-complete
            _fontFamilyCombo.DropDownWidth = 250; 
            _fontFamilyCombo.ItemHeight = 20; 
            _fontFamilyCombo.MaxDropDownItems = 15; 
            _fontFamilyCombo.DrawItem -= OnDrawComboItem; 
            _fontFamilyCombo.DrawItem += OnDrawFontFamilyItem;
            _fontFamilyCombo.TextUpdate += OnFontFamilyTextUpdate; // Handle typing
            
            // Validate input on leave
            _fontFamilyCombo.Leave += (s, e) => {
                if (!_allFontFamilies.Contains(_fontFamilyCombo.Text, StringComparer.OrdinalIgnoreCase))
                {
                    if (_fontFamilyCombo.Items.Count > 0)
                        _fontFamilyCombo.SelectedIndex = 0; // Or restore prev
                    else
                        _fontFamilyCombo.Text = _settings.TrayFontFamily;
                }
            };
            
            // Populate with system fonts
            var installedFonts = new System.Drawing.Text.InstalledFontCollection();
            _allFontFamilies = installedFonts.Families.Select(f => f.Name).ToList();
            _fontFamilyCombo.Items.AddRange(_allFontFamilies.ToArray());

            // Set current selection
            if (_fontFamilyCombo.Items.Contains(_settings.TrayFontFamily))
            {
                _fontFamilyCombo.SelectedItem = _settings.TrayFontFamily;
            }
            else if (_fontFamilyCombo.Items.Contains("Segoe UI"))
            {
                _fontFamilyCombo.SelectedItem = "Segoe UI";
            }
            else if (_fontFamilyCombo.Items.Count > 0)
            {
                _fontFamilyCombo.SelectedIndex = 0;
            }
            
            y += 40;

            AddLabel(page, S("SettingsForm_General_Theme"), x, y);
            _themeCombo = CreateComboBox(page, 170, y - 3, 200);
            _themeCombo.Items.AddRange(new object[]
            {
                S("SettingsForm_General_Theme_Auto"),
                S("SettingsForm_General_Theme_Light"),
                S("SettingsForm_General_Theme_Dark")
            });
            _themeCombo.SelectedIndexChanged += (s, e) => {
                if (!_isLoadingSettings) {
                    _settings.ThemeMode = _themeCombo.SelectedIndex switch {
                        1 => "Light",
                        2 => "Dark",
                        _ => "Auto"
                    };
                    ApplyTheme();
                }
            };
            y += 30;

            AddLabel(page, S("SettingsForm_General_TextColorLight"), x, y);
            _lightTextColorBtn = CreateColorButton(page, 170, y - 3, Color.Black);
            y += 30;

            AddLabel(page, S("SettingsForm_General_TextColorDark"), x, y);
            _darkTextColorBtn = CreateColorButton(page, 170, y - 3, Color.White);
            y += 30;

            AddLabel(page, S("SettingsForm_General_Language"), x, y);
            _languageCombo = CreateComboBox(page, 170, y - 3, 200);
            PopulateLanguageCombo();
            _languageCombo.SelectedIndexChanged += OnLanguageChanged;
            y += 30;

            _languageInfoLabel = new Label
            {
                Text = S("SettingsForm_General_LanguageRestart"),
                Location = new Point(x + 5, y),
                AutoSize = true,
                ForeColor = Color.DimGray
            };
            page.Controls.Add(_languageInfoLabel);
            y += 20;

            _startWithWindowsCheck = new CheckBox
            {
                Text = S("SettingsForm_General_StartWithWindows"),
                Location = new Point(x, y),
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(_startWithWindowsCheck);
            y += 25;

            _showTrayTempCheck = new CheckBox
            {
                Text = S("SettingsForm_General_ShowTrayTemperature"),
                Location = new Point(x, y),
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(_showTrayTempCheck);
            y += 25;
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

            // === Gradient Slider ===
            AddLabel(page, "Temperature Thresholds", x, y);
            y += 25;

            _gradientSlider = new GradientThresholdSlider
            {
                Location = new Point(x, y),
                Size = new Size(500, 40),
                WarmThreshold = _settings.WarmThreshold,
                HotThreshold = _settings.HotThreshold,
                CriticalThreshold = _settings.CriticalThreshold,
                CoolColor = _settings.GetCoolColorValue(),
                WarmColor = _settings.GetWarmColorValue(),
                HotColor = _settings.GetHotColorValue(),
                CriticalColor = _settings.GetCriticalColorValue()
            };
            page.Controls.Add(_gradientSlider);
            y += 70;

            // === Threshold Inputs (Synced with Slider) ===
            int colWidth = 120;
            int col1 = x;
            int col2 = x + colWidth + 20;
            int col3 = x + (colWidth + 20) * 2;
            int col4 = x + (colWidth + 20) * 3;

            // Header Labels
            // AddLabel(page, S("SettingsForm_General_WarmThreshold"), col2, y);
            // AddLabel(page, S("SettingsForm_General_HotThreshold"), col3, y);
            
            // Numeric Input Row
            _warmThresholdNum = CreateNumericUpDown(page, col2, y, 30, 99);
            _warmThresholdNum.Width = 80;
            
            _hotThresholdNum = CreateNumericUpDown(page, col3, y, 30, 99);
            _hotThresholdNum.Width = 80;

            _criticalThresholdNum = CreateNumericUpDown(page, col4, y, 30, 99);
            _criticalThresholdNum.Width = 80;
            
            // Labels for inputs
             var l1 =  new Label { Text = "Warm", Location = new Point(col2, y - 20), AutoSize = true };
             var l2 =  new Label { Text = "Hot", Location = new Point(col3, y - 20), AutoSize = true };
             var l3 =  new Label { Text = "Critical", Location = new Point(col4, y - 20), AutoSize = true };
             page.Controls.Add(l1); page.Controls.Add(l2); page.Controls.Add(l3);

            y += 60;

            // === Color Pickers Row ===
            
            // Cool Color
            AddLabel(page, S("SettingsForm_Colors_Cool"), col1, y - 20);
            _coolColorBtn = CreateColorButton(page, col1, y, Color.White);
            _coolColorBtn.Width = 100;

            // Warm Color
            AddLabel(page, S("SettingsForm_Colors_Warm"), col2, y - 20);
            _warmColorBtn = CreateColorButton(page, col2, y, Color.Orange);
            _warmColorBtn.Width = 100;

            // Hot Color
            AddLabel(page, S("SettingsForm_Colors_Hot"), col3, y - 20);
            _hotColorBtn = CreateColorButton(page, col3, y, Color.OrangeRed);
            _hotColorBtn.Width = 100;
            
            // Critical Color
            AddLabel(page, S("SettingsForm_Colors_Critical"), col4, y - 20);
            _criticalColorBtn = CreateColorButton(page, col4, y, Color.Red);
            _criticalColorBtn.Width = 100;

            y += 50;

            // === Event Wiring ===
            _gradientSlider.ThresholdsChanged += (s, e) =>
            {
                _warmThresholdNum.Value = Math.Clamp(_gradientSlider.WarmThreshold, _warmThresholdNum.Minimum, _warmThresholdNum.Maximum);
                _hotThresholdNum.Value = Math.Clamp(_gradientSlider.HotThreshold, _hotThresholdNum.Minimum, _hotThresholdNum.Maximum);
                _criticalThresholdNum.Value = Math.Clamp(_gradientSlider.CriticalThreshold, _criticalThresholdNum.Minimum, _criticalThresholdNum.Maximum);
            };

            _warmThresholdNum.ValueChanged += (s, e) => _gradientSlider.SetThresholds((int)_warmThresholdNum.Value, _gradientSlider.HotThreshold, _gradientSlider.CriticalThreshold);
            _hotThresholdNum.ValueChanged += (s, e) => _gradientSlider.SetThresholds(_gradientSlider.WarmThreshold, (int)_hotThresholdNum.Value, _gradientSlider.CriticalThreshold);
            _criticalThresholdNum.ValueChanged += (s, e) => _gradientSlider.SetThresholds(_gradientSlider.WarmThreshold, _gradientSlider.HotThreshold, (int)_criticalThresholdNum.Value);

            // Color Changes update slider
            _coolColorBtn.BackColorChanged += (s, e) => { _gradientSlider.CoolColor = _coolColorBtn.BackColor; _gradientSlider.Invalidate(); };
            _warmColorBtn.BackColorChanged += (s, e) => { _gradientSlider.WarmColor = _warmColorBtn.BackColor; _gradientSlider.Invalidate(); };
            _hotColorBtn.BackColorChanged += (s, e) => { _gradientSlider.HotColor = _hotColorBtn.BackColor; _gradientSlider.Invalidate(); };
            _criticalColorBtn.BackColorChanged += (s, e) => { _gradientSlider.CriticalColor = _criticalColorBtn.BackColor; _gradientSlider.Invalidate(); };
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
                Location = new Point(10, 415), 
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            page.Controls.Add(chkSelectAll);

            var btnSelectNonZero = new Button
            {
                Text = "Non-Zero",
                Location = new Point(120, 415), // Adjusted position slightly right
                Size = new Size(80, 23),
                UseVisualStyleBackColor = true
            };
            btnSelectNonZero.Click += (s, e) =>
            {
                 // Find currently visible list
                 var visibleList = _categoryLists.Values.FirstOrDefault(l => l.Visible);
                 if (visibleList != null)
                 {
                     for (int i = 0; i < visibleList.Items.Count; i++)
                     {
                         if (visibleList.Items[i] is SensorItem item)
                         {
                             // Select if value is significantly non-zero (e.g. > 0.01)
                             bool shouldSelect = Math.Abs(item.Value) > 0.01f;
                             visibleList.SetItemChecked(i, shouldSelect);
                         }
                     }
                 }
            };
            page.Controls.Add(btnSelectNonZero);



            // === Custom "Tab" Layout for Sensors (to support Dark Mode) ===
            
            // 1. Navigation Buttons Container
            _sensorNavPanel = new FlowLayoutPanel
            {
                Location = new Point(10, 35),
                Size = new Size(540, 30),
                BackColor = Color.Transparent, // Let theme handling set this or keep transparent
                Padding = new Padding(0)
            };
            page.Controls.Add(_sensorNavPanel);

            // 2. Content Container
            _sensorContentPanel = new Panel
            {
                Location = new Point(10, 65),
                Size = new Size(540, 340),
                BorderStyle = BorderStyle.FixedSingle // Themed border
            };
            page.Controls.Add(_sensorContentPanel);

            // Create "tabs" (Buttons + Lists)
            string[] categories = { "CPU", "GPU", "RAM", "Network" };
            
            foreach (var category in categories)
            {
                // Nav Button
                var btn = new Button
                {
                    Text = category,
                    Size = new Size(70, 30),
                    FlatStyle = FlatStyle.Flat,
                    Margin = new Padding(0, 0, 5, 0),
                    Tag = category
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += (s, e) => ShowSensorCategory(category);
                
                _sensorNavPanel.Controls.Add(btn);
                _categoryButtons[category] = btn;

                // Content List
                var list = new CheckedListBox
                {
                    CheckOnClick = true,
                    Dock = DockStyle.Fill,
                    Visible = false // Hidden by default
                };
                _sensorContentPanel.Controls.Add(list);
                _categoryLists[category] = list;
            }

            // Select first category by default
            if (categories.Any())
                ShowSensorCategory(categories[0]);

            // Keep reference to CPU list (compatibility)
            _sensorsCheckList = _categoryLists["CPU"];

            chkSelectAll.CheckedChanged += (s, e) =>
            {
                // Find currently visible list
                var visibleList = _categoryLists.Values.FirstOrDefault(l => l.Visible);
                if (visibleList != null)
                {
                    for (int i = 0; i < visibleList.Items.Count; i++)
                        visibleList.SetItemChecked(i, chkSelectAll.Checked);
                }
            };
        }

        private void ShowSensorCategory(string category)
        {
            // 1. Update Buttons
            bool isDark = Helpers.ThemeHelper.IsDarkMode(_settings);
            Color activeColor = isDark ? Color.FromArgb(80, 80, 80) : Color.FromArgb(180, 180, 180);
            Color inactiveColor = isDark ? Color.FromArgb(50, 50, 50) : Color.FromArgb(220, 220, 220);
            Color textColor = Helpers.ThemeHelper.GetTextColor(isDark);

            foreach (var kvp in _categoryButtons)
            {
                if (kvp.Key == category)
                {
                    kvp.Value.BackColor = activeColor;
                    kvp.Value.Font = new Font(Font, FontStyle.Bold);
                }
                else
                {
                    kvp.Value.BackColor = inactiveColor;
                    kvp.Value.Font = new Font(Font, FontStyle.Regular);
                }
                kvp.Value.ForeColor = textColor;
            }

            // 2. Show Content
            foreach (var kvp in _categoryLists)
            {
                kvp.Value.Visible = (kvp.Key == category);
            }
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
                Location = new Point(10, 415), 
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

        private ComboBox CreateComboBox(Panel parent, int x, int y, int width)
        {
            var cmb = new ComboBox
            {
                Location = new Point(x, y),
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed // Enable custom drawing for dark mode
            };
            cmb.DrawItem += OnDrawComboItem; // Handler for painting
            parent.Controls.Add(cmb);
            return cmb;
        }

        private void OnDrawComboItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not ComboBox cmb) return;
            if (e.Index < 0) return;

            bool isDark = Helpers.ThemeHelper.IsDarkMode(_settings);
            Color backColor = isDark ? Helpers.ThemeHelper.GetSurfaceColor(true) : SystemColors.Window;
            Color textColor = isDark ? Helpers.ThemeHelper.GetTextColor(true) : SystemColors.WindowText;
            
            // Draw Background
            if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
            {
                // Hover/Selection color
                backColor = isDark ? Helpers.ThemeHelper.GetSelectionColor(true) : SystemColors.Highlight;
                textColor = isDark ? Color.White : SystemColors.HighlightText;
            }

            using (var brush = new SolidBrush(backColor))
                e.Graphics.FillRectangle(brush, e.Bounds);

            // Draw Text
            string text = cmb.Items[e.Index]?.ToString() ?? "";
            TextRenderer.DrawText(e.Graphics, text, cmb.Font, e.Bounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
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

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            // Only show warning once per session, and only if actually changed from initial
            if (!_languageWarningShown && 
                _initialLanguageIndex >= 0 && 
                _languageCombo.SelectedIndex != _initialLanguageIndex)
            {
                _languageWarningShown = true;
                var result = MessageBox.Show(
                    S("SettingsForm_General_LanguageRestart") + "\n\n" + S("SettingsForm_General_RestartNow"),
                    "HotCPU",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                
                if (result == DialogResult.Yes)
                {
                    // Save settings first
                    SaveCurrentSettings();
                    _settings.Save();
                    
                    // Restart application. Prefer ProcessPath over
                    // Application.ExecutablePath (more reliable under MSIX).
                    try
                    {
                        var exe = Environment.ProcessPath;
                        if (string.IsNullOrEmpty(exe))
                            exe = Application.ExecutablePath;
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exe,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Error("Settings", "Restart after language change failed", ex);
                    }
                    Application.Exit();
                }
            }
        }
        
        private void SaveCurrentSettings()
        {
            if (decimal.TryParse(_refreshIntervalNum.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal refreshVal))
            {
                _settings.RefreshIntervalMs = (int)(refreshVal * 1000);
            }
            else
            {
                _settings.RefreshIntervalMs = 1000;
            }
            _settings.WarmThreshold = (int)_warmThresholdNum.Value;
            _settings.HotThreshold = (int)_hotThresholdNum.Value;
            _settings.CriticalThreshold = (int)_criticalThresholdNum.Value;
            if (_fontSizeCombo.SelectedIndex >= 0 && _fontSizeCombo.SelectedIndex < FontSizes.Length)
                _settings.FontSize = FontSizes[_fontSizeCombo.SelectedIndex];
            else
                _settings.FontSize = 14;

            _settings.ThemeMode = _themeCombo.SelectedIndex switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "Auto"
            };
            _settings.SetLightTextColor(_lightTextColorBtn.BackColor);
            _settings.SetDarkTextColor(_darkTextColorBtn.BackColor);
            if (_languageCombo.SelectedItem is LanguageOption selectedLanguage)
            {
                _settings.Language = selectedLanguage.CultureCode;
            }
        }

        private void PopulateLanguageCombo()
        {
            var options = new List<LanguageOption>
            {
                new LanguageOption(S("SettingsForm_General_LanguageAuto"), null)
            };

            string[] popularCultures =
            {
                "en-US", // English
                "zh-CN", // Chinese (Simplified)
                "es-ES", // Spanish
                "hi-IN", // Hindi
                "ar-SA", // Arabic
                "bn-BD", // Bengali
                "pt-BR", // Portuguese
                "ru-RU", // Russian
                "ja-JP", // Japanese
                "pa-IN", // Punjabi
                "de-DE", // German
                "fr-FR", // French
                "ur-PK", // Urdu
                "id-ID", // Indonesian
                "vi-VN", // Vietnamese
                "ko-KR", // Korean
                "it-IT", // Italian
                "tr-TR", // Turkish
                "ta-IN", // Tamil
                "te-IN", // Telugu
                "mr-IN", // Marathi
                "fa-IR", // Persian
                "sw-KE", // Swahili
                "nl-NL", // Dutch
                "pl-PL"  // Polish
            };

            foreach (var code in popularCultures)
            {
                try
                {
                    var culture = CultureInfo.GetCultureInfo(code);
                    var displayName = $"{culture.EnglishName} [{culture.Name}]";
                    options.Add(new LanguageOption(displayName, culture.Name));
                }
                catch (CultureNotFoundException)
                {
                    // Skip any cultures not supported by the current framework/runtime.
                }
            }

            _languageOptions = options;
            _languageCombo.DataSource = _languageOptions;
            _languageCombo.DisplayMember = nameof(LanguageOption.DisplayName);
            _languageCombo.ValueMember = nameof(LanguageOption.CultureCode);
        }

        private void LoadSettings()
        {
            _isLoadingSettings = true;
            _isLoadingSettings = true;
            decimal intervalSeconds = _settings.RefreshIntervalMs / 1000.0M;
            intervalSeconds = Math.Clamp(intervalSeconds, 0.1M, 60.0M);
            _refreshIntervalNum.Text = intervalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

            _warmThresholdNum.Value = _settings.WarmThreshold;
            _hotThresholdNum.Value = _settings.HotThreshold;
            _criticalThresholdNum.Value = _settings.CriticalThreshold;

            var fontIndex = Array.IndexOf(FontSizes, _settings.FontSize);
            _fontSizeCombo.SelectedIndex = fontIndex >= 0 ? fontIndex : Array.IndexOf(FontSizes, 14);

            _themeCombo.SelectedIndex = _settings.ThemeMode?.ToLowerInvariant() switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0
            };
            _lightTextColorBtn.BackColor = _settings.GetLightTextColorValue();
            _darkTextColorBtn.BackColor = _settings.GetDarkTextColorValue();

            // Ensure combo is populated
            if (_languageCombo.Items.Count == 0)
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
            if (_languageCombo.Items.Count > 0)
            {
                if (languageIndex >= _languageCombo.Items.Count)
                    languageIndex = 0;
                _languageCombo.SelectedIndex = languageIndex;
                _initialLanguageIndex = languageIndex;
            }

            _startWithWindowsCheck.Checked = _settings.StartWithWindows;
            _showTrayTempCheck.Checked = _settings.ShowTrayIconTemperature;
            
            // Colors
            _useGradientCheck.Checked = _settings.UseGradientColors;
            _coolColorBtn.BackColor = _settings.GetCoolColorValue();
            _warmColorBtn.BackColor = _settings.GetWarmColorValue();
            _hotColorBtn.BackColor = _settings.GetHotColorValue();
            _criticalColorBtn.BackColor = _settings.GetCriticalColorValue();
            UpdateColorButtonsEnabled();

            // Sensors - populate by category
            foreach (var list in _categoryLists.Values)
                list.Items.Clear();
                
            foreach (var hw in _availableHardware)
            {
                // Determine which category this hardware belongs to
                string category = GetHardwareCategory(hw.Type);
                
                // Debug: Log hardware types
                System.Diagnostics.Debug.WriteLine($"[HotCPU SETTINGS] Hardware: {hw.Name}, Type: {hw.Type}, Category: {category}, Sensors: {hw.Sensors.Count}");
                
                if (_categoryLists.TryGetValue(category, out var targetList))
                {
                    foreach (var sensor in hw.Sensors)
                    {
                        bool isVisible = !_settings.HiddenSensorIds.Contains(sensor.Identifier);
                        string displayName = $"{hw.Name} - {sensor.Name}";
                        targetList.Items.Add(new SensorItem(displayName, sensor.Identifier, sensor.Value, sensor.Unit), isVisible);
                    }
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
                    _traySensorsCheckList.Items.Add(new SensorItem(displayName, sensor.Identifier, sensor.Value, sensor.Unit), isSelected);
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
                    _logSensorsCheckList.Items.Add(new SensorItem(displayName, sensor.Identifier, sensor.Value, sensor.Unit), isLogged);
                }
            }
            _isLoadingSettings = false;
        }

        private void StepFontSize(int delta)
        {
            if (_fontSizeCombo.Items.Count == 0) return;
            int idx = _fontSizeCombo.SelectedIndex;
            if (idx < 0)
                idx = Array.IndexOf(FontSizes, 14);
            if (idx < 0)
                idx = 0;
            idx = Math.Clamp(idx + delta, 0, FontSizes.Length - 1);
            _fontSizeCombo.SelectedIndex = idx;
            ApplyFontSizeImmediate();
        }

        private void ApplyFontSizeImmediate()
        {
            if (_isLoadingSettings) return;
            if (_fontSizeCombo.SelectedIndex < 0 || _fontSizeCombo.SelectedIndex >= FontSizes.Length)
                return;

            _settings.FontSize = FontSizes[_fontSizeCombo.SelectedIndex];
            _onSettingsChanged();
            _settings.Save();
        }
        
        private void OnDrawSensorTab(object? sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabs) return;
            
            bool isDark = Helpers.ThemeHelper.IsDarkMode(_settings);
            Color bgColor = Helpers.ThemeHelper.GetBackgroundColor(isDark);
            Color surfaceColor = Helpers.ThemeHelper.GetSurfaceColor(isDark);
            Color textColor = Helpers.ThemeHelper.GetTextColor(isDark);
            Color selectedColor = isDark ? Color.FromArgb(70, 70, 70) : Color.White;
            Color unselectedColor = isDark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(240, 240, 240);

            // Paint background of the tab header area
            // Use e.Bounds to only paint the specific tab, but we also want the empty space to be dark?
            // The TabControl itself handles the empty space background via its BackColor, which we set in ApplyTheme.
            
            var tabRect = tabs.GetTabRect(e.Index);
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            using (var brush = new SolidBrush(isSelected ? selectedColor : unselectedColor))
            {
                e.Graphics.FillRectangle(brush, tabRect);
            }

            var tabText = tabs.TabPages[e.Index].Text;
            TextRenderer.DrawText(e.Graphics, tabText, tabs.Font, tabRect, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static string S(string key) => LocalizationService.GetString(key);
        
        private string GetHardwareCategory(string hardwareType)
        {
            return hardwareType switch
            {
                "Cpu" => "CPU",
                "GpuNvidia" or "GpuAmd" or "GpuIntel" => "GPU",
                "Motherboard" or "SuperIO" or "WMI_ACPI" or "WMI_CIM" or "ThermalZone" => "Motherboard",
                "Storage" or "WMI_Storage" => "Storage",
                "Memory" => "RAM",
                "Network" => "Network",
                _ => "Other" // Fan, PSU, Battery, Controller, etc.
            };
        }

        public class SensorItem
        {
            public string Name { get; }
            public string Id { get; }
            public float Value { get; set; }
            public string Unit { get; }
            
            public SensorItem(string name, string id, float value, string unit) 
            { 
                Name = name; 
                Id = id; 
                Value = value; 
                Unit = unit;
                
                // Debug: Log extreme temperature values
                if (unit == "°C" && (value > 500 || value < -50))
                {
                    System.Diagnostics.Debug.WriteLine($"[HotCPU SETTINGS] EXTREME TEMP in SensorItem: {name} = {value}°C (ID: {id})");
                }
            }
            
            public override string ToString()
            {
                var rounded = (int)Math.Round(Value);
                // If unit is small/integer based, round it. If float based like Volts, keep decimals
                string valStr = Unit == "°C" || Unit == "RPM" || Unit == "%" ? rounded.ToString() : Value.ToString("F1");
                
                // Fallback format if resource missing or minimal
                return $"{Name} ({valStr}{Unit})";
            }
        }

        public sealed class LanguageOption
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
            if (decimal.TryParse(_refreshIntervalNum.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal refreshVal))
            {
                _settings.RefreshIntervalMs = (int)(refreshVal * 1000);
            }
            else
            {
                _settings.RefreshIntervalMs = 1000;
            }

            _settings.WarmThreshold = (int)_warmThresholdNum.Value;
            _settings.HotThreshold = (int)_hotThresholdNum.Value;
            _settings.CriticalThreshold = (int)_criticalThresholdNum.Value;

            if (_fontSizeCombo.SelectedIndex >= 0 && _fontSizeCombo.SelectedIndex < FontSizes.Length)
                _settings.FontSize = FontSizes[_fontSizeCombo.SelectedIndex];
            else
                _settings.FontSize = 14;

            if (_fontFamilyCombo.SelectedItem != null)
                _settings.TrayFontFamily = _fontFamilyCombo.SelectedItem.ToString() ?? "Segoe UI";

            _settings.ThemeMode = _themeCombo.SelectedIndex switch
            {
                1 => "Light",
                2 => "Dark",
                _ => "Auto"
            };
            _settings.SetLightTextColor(_lightTextColorBtn.BackColor);
            _settings.SetDarkTextColor(_darkTextColorBtn.BackColor);

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

            // Update hidden sensors - collect from all category lists
            _settings.HiddenSensorIds.Clear();
            foreach (var list in _categoryLists.Values)
            {
                for (int i = 0; i < list.Items.Count; i++)
                {
                    // If UNCHECKED, it means hidden
                    if (!list.GetItemChecked(i))
                    {
                        if (list.Items[i] is SensorItem item)
                        {
                            _settings.HiddenSensorIds.Add(item.Id);
                        }
                    }
                }
            }

            // Update System Startup logic and surface the real outcome. Silent failure here
            // used to leave the checkbox on while Windows ignored the request.
            var startupResult = await StartupManager.TrySetStartupEnabledAsync(_startWithWindowsCheck.Checked);
            HandleStartupResult(startupResult, _startWithWindowsCheck.Checked);

            // Re-read actual state so the persisted flag reflects reality, not intent.
            _settings.StartWithWindows = await StartupManager.IsStartupEnabledAsync();
            _startWithWindowsCheck.Checked = _settings.StartWithWindows;

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

        private void OnFontFamilyTextUpdate(object? sender, EventArgs e)
        {
            if (sender is not ComboBox cmb) return;

            string text = cmb.Text;
            int selectionStart = cmb.SelectionStart;

            cmb.BeginUpdate();
            cmb.Items.Clear();

            if (string.IsNullOrWhiteSpace(text))
            {
                cmb.Items.AddRange(_allFontFamilies.ToArray());
            }
            else
            {
                var matches = _allFontFamilies
                    .Where(x => x.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();
                cmb.Items.AddRange(matches);
            }
            cmb.EndUpdate();
            
            // Restore text and state
            cmb.DroppedDown = true;
            cmb.Cursor = Cursors.Default; // Explicitly reset cursor icon just in case

            // Restoring text/selection is tricky as setting Text moves cursor
            // We set DroppedDown first to ensure it's visible
            cmb.SelectionStart = selectionStart;
            cmb.SelectionLength = 0;
            
            // For ComboBox in DropDown mode, modifying Items clears text in some versions.
            // We need to re-set the text if it was cleared.
            if (cmb.Text != text)
            {
                cmb.Text = text;
                cmb.SelectionStart = selectionStart;
            }
            // Avoid auto-selection logic messing with typing
            cmb.SelectedIndex = -1;
        }

        private void OnDrawFontFamilyItem(object? sender, DrawItemEventArgs e)
        {
            if (sender is not ComboBox cmb || e.Index < 0) return;

            string fontName = cmb.Items[e.Index]?.ToString() ?? "";
            
            bool isDark = Helpers.ThemeHelper.IsDarkMode(_settings);
            Color mbBack = isDark ? Helpers.ThemeHelper.GetSurfaceColor(true) : SystemColors.Window;
            Color mbText = isDark ? Helpers.ThemeHelper.GetTextColor(true) : SystemColors.WindowText;

            if ((e.State & DrawItemState.Selected) == DrawItemState.Selected)
            {
                mbBack = isDark ? Helpers.ThemeHelper.GetSelectionColor(true) : SystemColors.Highlight;
                mbText = isDark ? Color.White : SystemColors.HighlightText;
            }

            using (var brush = new SolidBrush(mbBack))
                e.Graphics.FillRectangle(brush, e.Bounds);

            // 1. Draw Font Name
            int nameWidth = 180; // Increased width for name since we have more space
            Rectangle nameRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, nameWidth, e.Bounds.Height); // Added 4px padding
            TextRenderer.DrawText(e.Graphics, fontName, cmb.Font, nameRect, mbText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 2. Draw Sample Temp
            // Get current temp
            float currentTemp = 0;
            if (_tempService?.CurrentReading != null && _settings.TraySensorIds.Count > 0) 
            {
                // Try to find the first monitored sensor
                var targetId = _settings.TraySensorIds[0];
                var sensor = _tempService.CurrentReading.AllTemps
                    .SelectMany(h => h.Sensors)
                    .FirstOrDefault(s => s.Identifier == targetId);
                if (sensor != null) currentTemp = sensor.Value;
            }
            
            // If no temp (or 0), show dummy
            string sampleText = currentTemp > 0 ? $"{(int)Math.Round(currentTemp)}" : "88";
            
            // Determine Color
            Color sampleColor = _settings.GetCoolColorValue();
            if (currentTemp >= _settings.CriticalThreshold) sampleColor = _settings.GetCriticalColorValue();
            else if (currentTemp >= _settings.HotThreshold) sampleColor = _settings.GetHotColorValue();
            else if (currentTemp >= _settings.WarmThreshold) sampleColor = _settings.GetWarmColorValue();

            // Draw Sample
            using (var font = new Font(fontName, cmb.Font.Size + 2, FontStyle.Regular))
            using (var brush = new SolidBrush(sampleColor))
            {
                // Right align
                // We use Graphics.DrawString for the custom font/brush
                e.Graphics.DrawString(sampleText, font, brush, e.Bounds.X + nameWidth + 5, e.Bounds.Y + 1);
            }
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

        public class GradientThresholdSlider : Control
        {
            public event EventHandler? ThresholdsChanged;

            public int WarmThreshold { get; set; } = 60;
            public int HotThreshold { get; set; } = 80;
            public int CriticalThreshold { get; set; } = 90;

            public Color CoolColor { get; set; } = Color.White;
            public Color WarmColor { get; set; } = Color.Orange;
            public Color HotColor { get; set; } = Color.OrangeRed;
            public Color CriticalColor { get; set; } = Color.Red;

            private int _min = 0;
            private int _max = 99; // 0 to 99 C
            private int _thumbSize = 12;
            private int _dragIndex = -1; // 0=Warm, 1=Hot, 2=Critical

            public GradientThresholdSlider()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }

            public void SetThresholds(int warm, int hot, int critical)
            {
                WarmThreshold = Math.Clamp(warm, _min, _max);
                HotThreshold = Math.Clamp(hot, WarmThreshold + 1, _max);
                CriticalThreshold = Math.Clamp(critical, HotThreshold + 1, _max);
                Invalidate();
                ThresholdsChanged?.Invoke(this, EventArgs.Empty);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // Clear background with set color (important for AllPaintingInWmPaint)
                e.Graphics.Clear(BackColor);
                
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                int trackHeight = 10;
                int trackY = (Height - trackHeight) / 2;
                Rectangle trackRect = new Rectangle(_thumbSize / 2, trackY, Width - _thumbSize, trackHeight);

                // Draw Track Background
                using (var path = GetRoundedRect(trackRect, 4))
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(trackRect, CoolColor, CriticalColor, 0f))
                {
                    // Create a blended gradient based on positions
                    var blend = new System.Drawing.Drawing2D.ColorBlend(4);
                    blend.Colors = new[] { CoolColor, WarmColor, HotColor, CriticalColor };
                    
                    // Normalize positions to 0.0 - 1.0
                    float posWarm = Normalize(WarmThreshold);
                    float posHot = Normalize(HotThreshold);
                    float posCrit = Normalize(CriticalThreshold);
                    
                    // Validate order for blend (must be strictly increasing)
                    // If squeezed, spread them slightly for rendering
                    if (posWarm <= 0) posWarm = 0.01f;
                    if (posHot <= posWarm) posHot = posWarm + 0.01f;
                    if (posCrit <= posHot) posCrit = posHot + 0.01f;
                    if (posCrit >= 1) posCrit = 0.99f;

                    blend.Positions = new[] { 0.0f, posWarm, posHot, 1.0f }; // Using Crit as 1.0 anchor or spread?
                    // Actually, let's map: 0=CoolData, Warm=WarmData, Hot=HotData, 100+=CritData
                    // Or simplified: Just 4 stops.
                    // Let's use simpler logic: 
                    // 0 -> Warm : Cool to Warm
                    // Warm -> Hot : Warm to Hot
                    // Hot -> Crit : Hot to Crit
                    // Crit -> Max : Crit (solid)
                    
                    // Re-do brush with 4 points
                    blend.Positions = new[] { 0.0f, posWarm, posHot, posCrit };
                    // Ensure last is < 1? No, ColorBlend ends at 1.0 usually unless wrap mode.
                    // LinearGradientBrush needs 0.0 and 1.0 anchors.
                    // Let's fake it:
                    blend.Colors = new[] { CoolColor, WarmColor, HotColor, CriticalColor, CriticalColor };
                    blend.Positions = new[] { 0.0f, posWarm, posHot, posCrit, 1.0f };

                    brush.InterpolationColors = blend;
                    g.FillPath(brush, path);
                }

                // Draw Thumbs
                DrawThumb(g, trackRect, WarmThreshold, WarmColor);
                DrawThumb(g, trackRect, HotThreshold, HotColor);
                DrawThumb(g, trackRect, CriticalThreshold, CriticalColor);
            }

            private void DrawThumb(Graphics g, Rectangle trackRect, int value, Color color)
            {
                float normalized = Normalize(value);
                int x = (int)(trackRect.X + (trackRect.Width * normalized));
                int y = trackRect.Y + (trackRect.Height / 2);
                
                var thumbRect = new Rectangle(x - _thumbSize / 2, y - _thumbSize / 2, _thumbSize, _thumbSize);
                
                // Shadow
                using (var brush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                    g.FillEllipse(brush, thumbRect.X + 1, thumbRect.Y + 1, thumbRect.Width, thumbRect.Height);

                // Fill
                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, thumbRect);
                
                // Border
                using (var pen = new Pen(Color.White, 2))
                    g.DrawEllipse(pen, thumbRect);
            }

            private float Normalize(int val)
            {
                return Math.Clamp((float)(val - _min) / (_max - _min), 0f, 1f);
            }

            private int CheckThumbHit(int mouseX, Rectangle trackRect)
            {
                // Find closest thumb
                int[] vals = { WarmThreshold, HotThreshold, CriticalThreshold };
                for (int i = 0; i < 3; i++)
                {
                    float norm = Normalize(vals[i]);
                    int x = (int)(trackRect.X + (trackRect.Width * norm));
                    if (Math.Abs(mouseX - x) < _thumbSize) return i;
                }
                return -1;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                int trackHeight = 10;
                int trackY = (Height - trackHeight) / 2;
                Rectangle trackRect = new Rectangle(_thumbSize / 2, trackY, Width - _thumbSize, trackHeight);
                
                _dragIndex = CheckThumbHit(e.X, trackRect);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (e.Button == MouseButtons.Left && _dragIndex != -1)
                {
                    int trackHeight = 10;
                    int trackY = (Height - trackHeight) / 2;
                    Rectangle trackRect = new Rectangle(_thumbSize / 2, trackY, Width - _thumbSize, trackHeight);

                    float norm = (float)(e.X - trackRect.X) / trackRect.Width;
                    int val = (int)(_min + (norm * (_max - _min)));
                    val = Math.Clamp(val, _min, _max);

                    if (_dragIndex == 0) // Warm
                    {
                        WarmThreshold = Math.Clamp(val, _min, HotThreshold - 1);
                    }
                    else if (_dragIndex == 1) // Hot
                    {
                        HotThreshold = Math.Clamp(val, WarmThreshold + 1, CriticalThreshold - 1);
                    }
                    else if (_dragIndex == 2) // Crit
                    {
                        CriticalThreshold = Math.Clamp(val, HotThreshold + 1, _max);
                    }
                    
                    Invalidate();
                    ThresholdsChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _dragIndex = -1;
            }

            private System.Drawing.Drawing2D.GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
            {
                int diameter = radius * 2;
                Size size = new Size(diameter, diameter);
                Rectangle arc = new Rectangle(bounds.Location, size);
                System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();

                if (radius == 0)
                {
                    path.AddRectangle(bounds);
                    return path;
                }

                // Top left arc
                path.AddArc(arc, 180, 90);

                // Top right arc
                arc.X = bounds.Right - diameter;
                path.AddArc(arc, 270, 90);

                // Bottom right arc
                arc.Y = bounds.Bottom - diameter;
                path.AddArc(arc, 0, 90);

                // Bottom left arc
                arc.X = bounds.Left;
                path.AddArc(arc, 90, 90);

                path.CloseFigure();
                return path;
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
