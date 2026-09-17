namespace ACOdysseyUMM;

internal sealed class MainForm : Form
{
    private static readonly Color Surface = Color.FromArgb(16, 13, 15);
    private static readonly Color SurfaceRaised = Color.FromArgb(24, 19, 21);
    private static readonly Color Accent = Color.FromArgb(124, 22, 34);
    private static readonly Color AccentHover = Color.FromArgb(158, 30, 45);
    private static readonly Color Border = Color.FromArgb(67, 48, 52);
    private static readonly Color TextPrimary = Color.FromArgb(238, 232, 226);
    private static readonly Color TextMuted = Color.FromArgb(166, 156, 153);

    private readonly PatchManifest _manifest;
    private PatchEngine _engine;
    private ForgeLevel255Manager _forgeManager;
    private UbisoftDualExeManager _ubisoftManager;
    private readonly Image _installerArt;
    private readonly MemoryStream _installerAudioStream;
    private readonly System.Media.SoundPlayer _installerAudioPlayer;

    private readonly TextBox _exePath = new();
    private readonly Label _buildValue = new() { AutoSize = true, Text = "Not analyzed" };
    private readonly Label _hashValue = new() { AutoSize = true, Text = "-" };
    private readonly Label _statusValue = new() { AutoSize = true, Text = "Select ACOdyssey.exe" };

    private readonly RadioButton _levelOff = new()
    {
        AutoSize = true,
        Text = "Off"
    };

    private readonly RadioButton _curveLight = new()
    {
        AutoSize = true,
        Text = "Light"
    };

    private readonly RadioButton _curveLinear = new()
    {
        AutoSize = true,
        Checked = true,
        Text = "Linear"
    };

    private readonly RadioButton _pressureX3 = new()
    {
        AutoSize = true,
        Checked = true,
        Text = "x3 - max 15 hunters"
    };

    private readonly RadioButton _pressureX6 = new()
    {
        AutoSize = true,
        Text = "x6 - max 30 hunters"
    };

    private readonly CheckBox _immediateRecycle = new()
    {
        AutoSize = true,
        Checked = false,
        Text = "Immediate Recycle"
    };

    private readonly Button _browseButton = new() { Text = "BROWSE" };
    private readonly Button _analyzeButton = new() { Text = "ANALYZE" };
    private readonly Button _verifyButton = new() { Text = "VERIFY", Enabled = false };
    private readonly Button _applyButton = new() { Text = "INSTALL MAYHEM", Enabled = false };
    private readonly Button _restoreButton = new() { Text = "RESTORE VANILLA", Enabled = false };
    private readonly Button _backupStoreButton = new() { Text = "APPDATA" };
    private readonly Button _audioButton = new() { Text = "AUDIO ON" };
    private readonly Button _closeButton = new() { Text = "×" };
    private readonly RichTextBox _log = new();
    private readonly ToolTip _toolTip = new();
    private readonly ThinProgressLine _installProgress = new();

    private TargetAnalysis? _analysis;
    private UbisoftSecondaryAnalysis? _secondaryAnalysis;
    private BackupStorageMode _backupStorageMode;
    private bool _busy;
    private bool _forgePairValid;
    private bool _secondaryPairValid = true;
    private bool _audioEnabled = true;
    private bool _dragging;
    private Point _dragOffset;

    public MainForm()
    {
        Text = "MAYHEM - Assassin's Creed Odyssey | Narzelith";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1180, 700);
        MinimumSize = new Size(1180, 700);
        MaximumSize = new Size(1180, 700);
        MaximizeBox = false;
        Icon = LoadEmbeddedIcon();
        BackColor = Color.Black;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;

        _installerArt = LoadEmbeddedArt();
        BackgroundImage = _installerArt;
        BackgroundImageLayout = ImageLayout.Zoom;

        _installerAudioStream = LoadEmbeddedAudio();
        _installerAudioPlayer = new System.Media.SoundPlayer(_installerAudioStream);
        _installerAudioPlayer.Load();

        try
        {
            _manifest = PatchEngine.LoadManifest();
            _backupStorageMode = DetectInitialBackupStorageMode();
            _engine = CreateEngine(_backupStorageMode);
            _forgeManager = CreateForgeManager(_backupStorageMode);
            _ubisoftManager = CreateUbisoftManager(_backupStorageMode);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MAYHEM installer error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            throw;
        }

        Controls.Add(BuildInstallerCard());
        UpdateBackupStoreVisual();
        ConfigureWindowChrome();
        MouseDown += BeginWindowDrag;
        MouseMove += ContinueWindowDrag;
        MouseUp += EndWindowDrag;

        _exePath.TextChanged += (_, _) => InvalidateAnalysisAfterPathEdit();
        _levelOff.CheckedChanged += (_, _) => SelectionChanged();
        _curveLight.CheckedChanged += (_, _) => SelectionChanged();
        _curveLinear.CheckedChanged += (_, _) => SelectionChanged();
        _pressureX3.CheckedChanged += (_, _) => SelectionChanged();
        _pressureX6.CheckedChanged += (_, _) => SelectionChanged();
        _immediateRecycle.CheckedChanged += (_, _) => SelectionChanged();
        _browseButton.Click += (_, _) => BrowseTarget();
        _analyzeButton.Click += async (_, _) => await AnalyzeAsync();
        _verifyButton.Click += async (_, _) => await VerifyAsync();
        _applyButton.Click += async (_, _) => await ApplyAsync();
        _restoreButton.Click += async (_, _) => await RestoreAsync();
        _backupStoreButton.Click += (_, _) => ToggleBackupStore();
        _audioButton.Click += (_, _) => ToggleAudio();
        _closeButton.Click += (_, _) => Close();

        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                System.Media.SystemSounds.Beep.Play();
            }
        };
        FormClosed += (_, _) =>
        {
            _installerAudioPlayer.Stop();
            _installerAudioPlayer.Dispose();
            _installerAudioStream.Dispose();
            _installerArt.Dispose();
            _toolTip.Dispose();
        };

        _installerAudioPlayer.PlayLooping();
        UpdateFeatureControlState();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            var createParams = base.CreateParams;
            createParams.ClassStyle |= CsDropShadow;
            return createParams;
        }
    }

    private void ConfigureWindowChrome()
    {
        _closeButton.SetBounds(ClientSize.Width - 42, 10, 32, 28);
        _closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _closeButton.AutoSize = false;
        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.FlatAppearance.MouseOverBackColor = Accent;
        _closeButton.FlatAppearance.MouseDownBackColor = AccentHover;
        _closeButton.BackColor = Color.FromArgb(12, 9, 10);
        _closeButton.ForeColor = Color.FromArgb(214, 197, 191);
        _closeButton.Font = new Font("Segoe UI", 14F, FontStyle.Regular);
        _closeButton.TextAlign = ContentAlignment.MiddleCenter;
        _closeButton.TabStop = false;
        _closeButton.UseVisualStyleBackColor = false;
        Controls.Add(_closeButton);
        _closeButton.BringToFront();

        Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(92, 24, 34));
            e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        };
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _dragging = true;
        _dragOffset = e.Location;
        Capture = true;
    }

    private void ContinueWindowDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;
        var mouse = MousePosition;
        Location = new Point(mouse.X - _dragOffset.X, mouse.Y - _dragOffset.Y);
    }

    private void EndWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        _dragging = false;
        Capture = false;
    }

    private Control BuildInstallerCard()
    {
        var card = new BorderedPanel
        {
            Location = new Point(30, 392),
            Size = new Size(700, 282),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            BackColor = Surface,
            BorderColor = Border
        };

        var accentLine = new Panel
        {
            Dock = DockStyle.Top,
            Height = 3,
            BackColor = Accent
        };
        card.Controls.Add(accentLine);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 9, 14, 10),
            ColumnCount = 1,
            RowCount = 8,
            BackColor = Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sectionTitle = new Label
        {
            AutoSize = true,
            Text = "INSTALLATION",
            ForeColor = Color.FromArgb(202, 169, 160),
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            Padding = new Padding(0, 1, 0, 0),
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left
        };

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Surface
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titleRow.Controls.Add(sectionTitle, 0, 0);

        _installProgress.Dock = DockStyle.Fill;
        _installProgress.Margin = new Padding(12, 0, 0, 0);
        _installProgress.TrackColor = Color.FromArgb(70, 27, 33);
        _installProgress.FillColor = AccentHover;
        _installProgress.Value = 0d;
        titleRow.Controls.Add(_installProgress, 1, 0);
        layout.Controls.Add(titleRow, 0, 0);

        var targetRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Surface
        };
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));

        StyleTextBox(_exePath);
        _exePath.PlaceholderText = "Select the game's ACOdyssey.exe";
        _exePath.Margin = new Padding(0, 3, 8, 3);
        StyleButton(_browseButton, false);
        StyleButton(_analyzeButton, false);
        _browseButton.Margin = new Padding(0, 2, 7, 2);
        _analyzeButton.Margin = new Padding(0, 2, 0, 2);
        targetRow.Controls.Add(_exePath, 0, 0);
        targetRow.Controls.Add(_browseButton, 1, 0);
        targetRow.Controls.Add(_analyzeButton, 2, 0);
        layout.Controls.Add(targetRow, 0, 1);

        StyleChoice(_levelOff);
        StyleChoice(_curveLight);
        StyleChoice(_curveLinear);
        _levelOff.BackColor = Surface;
        _curveLight.BackColor = Surface;
        _curveLinear.BackColor = Surface;
        _levelOff.Margin = new Padding(0, 0, 14, 0);
        _curveLight.Margin = new Padding(0, 0, 14, 0);
        _curveLinear.Margin = Padding.Empty;

        var curveSelector = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 1),
            BackColor = Surface
        };
        curveSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        curveSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        curveSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        curveSelector.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));

        var curveLeft = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            BackColor = Surface
        };
        curveLeft.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        curveLeft.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        curveLeft.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "MERCENARY LEVEL UNLOCK",
            ForeColor = Color.FromArgb(112, 101, 100),
            Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold),
            Margin = Padding.Empty
        }, 0, 0);

        var curveChoices = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 1, 0, 0),
            BackColor = Surface
        };
        curveChoices.Controls.Add(_levelOff);
        curveChoices.Controls.Add(_curveLight);
        curveChoices.Controls.Add(_curveLinear);
        curveLeft.Controls.Add(curveChoices, 0, 1);
        curveSelector.Controls.Add(curveLeft, 0, 0);

        var backupStorePanel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 7, 0),
            Padding = new Padding(0, 0, 1, 1),
            BackColor = Surface
        };

        var backupStoreLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 12,
            Text = "BACKUP PATH",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(112, 101, 100),
            Font = new Font("Segoe UI Semibold", 6.5F, FontStyle.Bold),
            Margin = Padding.Empty
        };
        backupStorePanel.Controls.Add(backupStoreLabel);

        StyleButton(_backupStoreButton, false);
        _backupStoreButton.Font = new Font("Segoe UI Semibold", 7F, FontStyle.Bold);
        _backupStoreButton.Dock = DockStyle.Bottom;
        _backupStoreButton.Height = 22;
        _backupStoreButton.AutoSize = false;
        _backupStoreButton.TextAlign = ContentAlignment.MiddleCenter;
        _backupStoreButton.Margin = Padding.Empty;
        backupStorePanel.Controls.Add(_backupStoreButton);
        curveSelector.Controls.Add(backupStorePanel, 1, 0);

        StyleButton(_audioButton, false);
        _audioButton.Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold);
        _audioButton.Dock = DockStyle.None;
        _audioButton.AutoSize = false;
        _audioButton.Size = new Size(92, 31);
        _audioButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _audioButton.TextAlign = ContentAlignment.MiddleCenter;
        _audioButton.Margin = new Padding(0, 3, 0, 0);
        curveSelector.Controls.Add(_audioButton, 2, 0);

        layout.Controls.Add(curveSelector, 0, 2);

        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 2, 0, 3),
            BackColor = Surface
        };
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));

        ConfigureStatusValue(_buildValue, TextMuted);
        ConfigureStatusValue(_statusValue, TextPrimary);
        ConfigureStatusValue(_hashValue, Color.FromArgb(120, 112, 111));

        statusPanel.Controls.Add(MakeMetaLabel("BUILD"), 0, 0);
        statusPanel.Controls.Add(_buildValue, 1, 0);
        statusPanel.Controls.Add(MakeMetaLabel("STATE"), 0, 1);

        var stateLine = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = Surface
        };
        stateLine.Controls.Add(_statusValue);
        var hashCaption = new Label
        {
            AutoSize = true,
            Text = "  SHA  ",
            ForeColor = Color.FromArgb(102, 92, 92),
            Padding = new Padding(6, 2, 0, 0)
        };
        stateLine.Controls.Add(hashCaption);
        stateLine.Controls.Add(_hashValue);
        statusPanel.Controls.Add(stateLine, 1, 1);
        layout.Controls.Add(statusPanel, 0, 3);

        var modulesPanel = new BorderedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = SurfaceRaised,
            BorderColor = Color.FromArgb(55, 39, 43),
            Margin = new Padding(0, 1, 0, 4),
            Padding = new Padding(11, 4, 11, 3)
        };

        StyleChoice(_pressureX3);
        StyleChoice(_pressureX6);
        StyleChoice(_immediateRecycle);
        _pressureX3.Margin = new Padding(0, 1, 12, 0);
        _pressureX6.Margin = new Padding(0, 1, 12, 0);
        _immediateRecycle.Margin = new Padding(0, 1, 12, 0);
        _toolTip.SetToolTip(_immediateRecycle, "Immediately creates a fresh procedural mercenary after death while vanilla corpse/actor teardown continues.");

        var pressureChoices = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            BackColor = SurfaceRaised
        };
        pressureChoices.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "HUNTER PRESSURE",
            ForeColor = Color.FromArgb(112, 101, 100),
            Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold),
            Margin = new Padding(0, 4, 18, 0)
        });
        pressureChoices.Controls.Add(_pressureX3);
        pressureChoices.Controls.Add(_pressureX6);
        pressureChoices.Controls.Add(_immediateRecycle);
        pressureChoices.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Fast Travel 1s + Refill included",
            ForeColor = Color.FromArgb(112, 101, 100),
            Font = new Font("Segoe UI", 7.5F),
            Margin = new Padding(0, 4, 0, 0)
        });

        modulesPanel.Controls.Add(pressureChoices);
        layout.Controls.Add(modulesPanel, 0, 4);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Surface
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));

        StyleButton(_applyButton, true);
        StyleButton(_verifyButton, false);
        StyleButton(_restoreButton, false);
        _applyButton.Margin = new Padding(0, 2, 8, 3);
        _verifyButton.Margin = new Padding(0, 2, 8, 3);
        _restoreButton.Margin = new Padding(0, 2, 0, 3);
        actions.Controls.Add(_applyButton, 0, 0);
        actions.Controls.Add(_verifyButton, 1, 0);
        actions.Controls.Add(_restoreButton, 2, 0);
        layout.Controls.Add(actions, 0, 5);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.DetectUrls = false;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Color.FromArgb(10, 9, 10);
        _log.ForeColor = Color.FromArgb(150, 141, 139);
        _log.Font = new Font("Consolas", 8F);
        _log.Margin = new Padding(0, 2, 0, 0);
        _log.Text = "MAYHEM installer ready. Select ACOdyssey.exe to begin." + Environment.NewLine;
        layout.Controls.Add(_log, 0, 6);

        var footer = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "Exact-build patching • verified backup • vanilla restore • fail-closed",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(112, 101, 100),
            Font = new Font("Segoe UI", 7.5F),
            Padding = new Padding(0, 1, 0, 0)
        };
        layout.Controls.Add(footer, 0, 7);

        card.Controls.Add(layout);
        layout.BringToFront();
        accentLine.BringToFront();
        return card;
    }

    private static Icon LoadEmbeddedIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ACOdysseyUMM.Resources.Mayhem.ico")
            ?? throw new InvalidDataException("Embedded MAYHEM application icon is missing.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }

    private static Image LoadEmbeddedArt()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ACOdysseyUMM.Resources.InstallerArt.png")
            ?? throw new InvalidDataException("Embedded MAYHEM installer artwork is missing.");
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static MemoryStream LoadEmbeddedAudio()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ACOdysseyUMM.Resources.InstallerAudio.wav")
            ?? throw new InvalidDataException("Embedded MAYHEM installer audio is missing.");
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    private static string AppDataStorageRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Narzelith",
        "MAYHEM");

    private static string InstallerStorageRoot() => Path.Combine(AppContext.BaseDirectory, "Backup");

    private static bool StorageHasRecoveryArtifacts(string root) =>
        File.Exists(Path.Combine(root, "umm-state.json")) ||
        File.Exists(Path.Combine(root, "umm-transaction.json")) ||
        File.Exists(Path.Combine(root, "umm-plus-state.json")) ||
        File.Exists(Path.Combine(root, "umm-plus-transaction.json")) ||
        File.Exists(Path.Combine(root, "mayhem-ubisoft-dual-transaction.json")) ||
        File.Exists(Path.Combine(root, "mayhem-forge-state.json")) ||
        File.Exists(Path.Combine(root, "mayhem-forge-transaction.json"));

    private static DateTime LatestStorageArtifactUtc(string root)
    {
        var latest = DateTime.MinValue;
        foreach (var fileName in new[] { "umm-state.json", "umm-transaction.json", "umm-plus-state.json", "umm-plus-transaction.json", "mayhem-ubisoft-dual-transaction.json", "mayhem-forge-state.json", "mayhem-forge-transaction.json" })
        {
            var path = Path.Combine(root, fileName);
            if (File.Exists(path))
                latest = DateTime.Compare(latest, File.GetLastWriteTimeUtc(path)) >= 0 ? latest : File.GetLastWriteTimeUtc(path);
        }
        return latest;
    }

    private static BackupStorageMode DetectInitialBackupStorageMode()
    {
        var appDataRoot = AppDataStorageRoot();
        var installerRoot = InstallerStorageRoot();
        var appDataHasState = StorageHasRecoveryArtifacts(appDataRoot);
        var installerHasState = StorageHasRecoveryArtifacts(installerRoot);

        if (installerHasState && !appDataHasState)
            return BackupStorageMode.Installer;
        if (appDataHasState && !installerHasState)
            return BackupStorageMode.AppData;
        if (appDataHasState && installerHasState)
            return LatestStorageArtifactUtc(installerRoot) > LatestStorageArtifactUtc(appDataRoot)
                ? BackupStorageMode.Installer
                : BackupStorageMode.AppData;

        return BackupStorageMode.AppData;
    }

    private PatchEngine CreateEngine(BackupStorageMode mode) => mode switch
    {
        BackupStorageMode.AppData => new PatchEngine(_manifest, AppDataStorageRoot()),
        BackupStorageMode.Installer => new PatchEngine(_manifest, InstallerStorageRoot(), InstallerStorageRoot()),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown backup storage mode.")
    };

    private static ForgeLevel255Manager CreateForgeManager(BackupStorageMode mode) => mode switch
    {
        BackupStorageMode.AppData => new ForgeLevel255Manager(AppDataStorageRoot(), Path.Combine(AppDataStorageRoot(), "Backups")),
        BackupStorageMode.Installer => new ForgeLevel255Manager(InstallerStorageRoot(), InstallerStorageRoot()),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown backup storage mode.")
    };

    private UbisoftDualExeManager CreateUbisoftManager(BackupStorageMode mode) => mode switch
    {
        BackupStorageMode.AppData => new UbisoftDualExeManager(_manifest, AppDataStorageRoot(), Path.Combine(AppDataStorageRoot(), "Backups")),
        BackupStorageMode.Installer => new UbisoftDualExeManager(_manifest, InstallerStorageRoot(), InstallerStorageRoot()),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown backup storage mode.")
    };

    private string CurrentBackupDirectory() => _backupStorageMode switch
    {
        BackupStorageMode.AppData => Path.Combine(AppDataStorageRoot(), "Backups"),
        BackupStorageMode.Installer => InstallerStorageRoot(),
        _ => throw new ArgumentOutOfRangeException(nameof(_backupStorageMode), _backupStorageMode, "Unknown backup storage mode.")
    };

    private string CurrentStorageRoot() => _backupStorageMode switch
    {
        BackupStorageMode.AppData => AppDataStorageRoot(),
        BackupStorageMode.Installer => InstallerStorageRoot(),
        _ => throw new ArgumentOutOfRangeException(nameof(_backupStorageMode), _backupStorageMode, "Unknown backup storage mode.")
    };

    private void UpdateBackupStoreVisual()
    {
        _backupStoreButton.Text = _backupStorageMode == BackupStorageMode.AppData ? "APPDATA" : "INSTALLER";
        _backupStoreButton.ForeColor = _backupStorageMode == BackupStorageMode.AppData ? TextPrimary : Color.FromArgb(221, 187, 177);
        _toolTip.SetToolTip(_backupStoreButton, $"Vanilla backup location: {CurrentBackupDirectory()}");
    }

    private void ToggleBackupStore()
    {
        if (_busy)
            return;

        if (_analysis?.MatchesSavedPatchedState == true || StorageHasRecoveryArtifacts(CurrentStorageRoot()))
        {
            System.Media.SystemSounds.Beep.Play();
            MessageBox.Show(
                this,
                "This backup store contains active MAYHEM patch/recovery state. Restore or analyze that state before changing the backup store.",
                "Backup store locked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var next = _backupStorageMode == BackupStorageMode.AppData
            ? BackupStorageMode.Installer
            : BackupStorageMode.AppData;

        try
        {
            var nextEngine = CreateEngine(next);
            var nextForgeManager = CreateForgeManager(next);
            var nextUbisoftManager = CreateUbisoftManager(next);
            _backupStorageMode = next;
            _engine = nextEngine;
            _forgeManager = nextForgeManager;
            _ubisoftManager = nextUbisoftManager;
            UpdateBackupStoreVisual();
            InvalidateAnalysisAfterBackupStoreChange();
            Log($"Backup store: {_backupStorageMode}; vanilla backup root: {CurrentBackupDirectory()}");
        }
        catch (Exception ex)
        {
            Log("BACKUP STORE CHANGE FAILED: " + ex.Message);
            MessageBox.Show(ex.Message, "Backup store change failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleAudio()
    {
        _audioEnabled = !_audioEnabled;
        if (_audioEnabled)
        {
            _installerAudioPlayer.PlayLooping();
            _audioButton.Text = "AUDIO ON";
            _audioButton.ForeColor = TextPrimary;
        }
        else
        {
            _installerAudioPlayer.Stop();
            _audioButton.Text = "AUDIO OFF";
            _audioButton.ForeColor = TextMuted;
        }
    }

    private static Label MakeMetaLabel(string text) => new()
    {
        AutoSize = true,
        Text = text,
        ForeColor = Color.FromArgb(112, 101, 100),
        Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold),
        Padding = new Padding(0, 3, 0, 0)
    };

    private static void ConfigureStatusValue(Label label, Color color)
    {
        label.ForeColor = color;
        label.BackColor = Surface;
        label.Padding = new Padding(0, 2, 0, 0);
    }

    private static void StyleTextBox(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Color.FromArgb(9, 8, 9);
        box.ForeColor = TextPrimary;
        box.Font = new Font("Segoe UI", 9F);
    }

    private static void StyleChoice(Control control)
    {
        control.ForeColor = TextPrimary;
        control.BackColor = SurfaceRaised;
        control.Font = new Font("Segoe UI", 8.5F);
        control.Margin = new Padding(0, 2, 10, 0);
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.Dock = DockStyle.Fill;
        button.Height = 32;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(174, 44, 58) : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(42, 31, 34);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(91, 16, 26);
        button.BackColor = primary ? Accent : Color.FromArgb(27, 21, 23);
        button.ForeColor = TextPrimary;
        button.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private void BrowseTarget()
    {
        if (_busy) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Assassin's Creed Odyssey|ACOdyssey.exe|Executable files|*.exe",
            FileName = "ACOdyssey.exe",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select Assassin's Creed Odyssey executable"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _exePath.Text = dialog.FileName;
    }

    private void SelectionChanged()
    {
        UpdateFeatureControlState();
        UpdateActionState();
    }

    private MercenaryInstallSelection CurrentSelection() => new(
        _levelOff.Checked
            ? MercenaryLevelMode.Off
            : _curveLight.Checked
                ? MercenaryLevelMode.Light
                : MercenaryLevelMode.Linear,
        _pressureX6.Checked ? HunterPressureMode.X6 : HunterPressureMode.X3,
        _immediateRecycle.Checked);

    private IReadOnlyList<string> ResolveCurrentPatchIds() =>
        MercenaryPatchSelectionResolver.Resolve(CurrentSelection());

    private async Task AnalyzeAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(_exePath.Text)) return;
        SetBusy(true);
        try
        {
            await RefreshAnalysisCoreAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task VerifyAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(_exePath.Text)) return;
        SetBusy(true);
        try
        {
            var targetPath = _exePath.Text.Trim();
            var result = await _engine.VerifyAsync(targetPath);
            var secondaryResult = await _ubisoftManager.VerifySecondaryAsync(_engine, targetPath, result.BuildId, result.IsPatched);
            var forgeResult = await _forgeManager.VerifyPairAsync(targetPath);
            Log(result.Status);
            Log($"Primary EXE SHA-256: {result.Sha256}; operations verified: {result.VerifiedOperationCount}");
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
                Log($"Original primary EXE backup: {result.BackupPath}");
            if (secondaryResult.Required)
            {
                Log(secondaryResult.Status);
                if (!string.IsNullOrWhiteSpace(secondaryResult.Sha256))
                    Log($"Ubisoft plus EXE SHA-256: {secondaryResult.Sha256}; operations verified: {secondaryResult.VerifiedOperationCount}");
                if (!string.IsNullOrWhiteSpace(secondaryResult.BackupPath))
                    Log($"Original Ubisoft plus EXE backup: {secondaryResult.BackupPath}");
            }
            Log(forgeResult.Status);
            if (!string.IsNullOrWhiteSpace(forgeResult.Sha256))
                Log($"Forge SHA-256: {forgeResult.Sha256}");
            if (!string.IsNullOrWhiteSpace(forgeResult.BackupPath))
                Log($"Original Forge backup: {forgeResult.BackupPath}");
            if (!result.IsValid || !secondaryResult.IsValid || !forgeResult.IsValid)
                MessageBox.Show("MAYHEM verification failed. Check the installer log for the exact EXE/Forge mismatch.", "Verify failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            await RefreshAnalysisCoreAsync();
        }
        catch (Exception ex)
        {
            Log("VERIFY FAILED: " + ex.Message);
            MessageBox.Show(ex.Message, "Verify failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshAnalysisCoreAsync()
    {
        try
        {
            var targetPath = _exePath.Text.Trim();
            await _forgeManager.RecoverInterruptedAsync(targetPath, _engine);
            await _ubisoftManager.RecoverInterruptedAsync(targetPath, _engine, _forgeManager);
            _analysis = await _engine.AnalyzeAsync(targetPath);
            _secondaryAnalysis = await _ubisoftManager.AnalyzeSecondaryAsync(_engine, _analysis);
            _secondaryPairValid = _secondaryAnalysis.IsValid;
            _buildValue.Text = _analysis.Build?.DisplayName ?? (_analysis.MatchesSavedPatchedState ? "MAYHEM patched" : "Unknown");
            _hashValue.Text = _analysis.Sha256.Length >= 12 ? _analysis.Sha256[..12] + "..." : _analysis.Sha256;
            _statusValue.Text = _analysis.Status;
            _statusValue.ForeColor = _analysis.HasStateConflict ? Color.FromArgb(227, 105, 105) : Color.FromArgb(214, 205, 198);
            ValidatePublishedModuleSet(_analysis);
            var forgeResult = await _forgeManager.VerifyPairAsync(targetPath);
            _forgePairValid = forgeResult.IsValid;
            if (!_secondaryAnalysis.IsValid)
            {
                _statusValue.Text = _secondaryAnalysis.Status;
                _statusValue.ForeColor = Color.FromArgb(227, 105, 105);
            }
            else if (!forgeResult.IsValid)
            {
                _statusValue.Text = forgeResult.Status;
                _statusValue.ForeColor = Color.FromArgb(227, 105, 105);
            }
            Log($"Analyzed: {_analysis.Path}");
            Log($"Primary EXE SHA-256: {_analysis.Sha256}");
            Log($"PE timestamp: 0x{_analysis.PeTimestamp:X8}; size: {_analysis.Size:N0} bytes");
            Log(_analysis.Status);
            if (_secondaryAnalysis.Required)
            {
                Log(_secondaryAnalysis.Status);
                if (_secondaryAnalysis.Analysis is not null)
                    Log($"Ubisoft plus EXE SHA-256: {_secondaryAnalysis.Analysis.Sha256}; PE timestamp: 0x{_secondaryAnalysis.Analysis.PeTimestamp:X8}; size: {_secondaryAnalysis.Analysis.Size:N0} bytes");
            }
            Log(forgeResult.Status);
            if (!string.IsNullOrWhiteSpace(forgeResult.Sha256))
                Log($"Forge SHA-256: {forgeResult.Sha256}");
        }
        catch (Exception ex)
        {
            _analysis = null;
            _secondaryAnalysis = null;
            _forgePairValid = false;
            _secondaryPairValid = false;
            _buildValue.Text = "Error";
            _hashValue.Text = "-";
            _statusValue.Text = ex.Message;
            _statusValue.ForeColor = Color.FromArgb(227, 105, 105);
            Log("ERROR: " + ex.Message);
        }

        UpdateFeatureControlState();
        UpdateActionState();
    }

    private void ValidatePublishedModuleSet(TargetAnalysis analysis)
    {
        if (analysis.Build is null)
            return;

        // Manifest completeness is a property of the embedded manifest, not of the current
        // target/state applicability. GetApplicablePatches intentionally returns an empty set
        // on state conflict, which previously caused a stale state to be misreported as every
        // mercenary module being missing.
        var available = _engine.Patches
            .Where(p => p.Targets.Any(t => string.Equals(t.BuildId, analysis.Build.Id, StringComparison.Ordinal)))
            .Select(p => p.Id)
            .ToHashSet(StringComparer.Ordinal);

        var required = new[]
        {
            MercenaryPatchSelectionResolver.LevelLinearPatchId,
            MercenaryPatchSelectionResolver.LevelLightPatchId,
            MercenaryPatchSelectionResolver.PressureX3PatchId,
            MercenaryPatchSelectionResolver.PressureX6PatchId,
            MercenaryPatchSelectionResolver.Selector255PatchId,
            MercenaryPatchSelectionResolver.FastTravelRedispatchPatchId,
            MercenaryPatchSelectionResolver.ImmediateRecyclePatchId
        };

        var missing = required.Where(x => !available.Contains(x)).ToList();
        if (missing.Count != 0)
            throw new InvalidDataException("Mercenary module manifest is incomplete: " + string.Join(", ", missing));

        if (UbisoftDualExeManager.IsUbisoftPrimaryBuild(analysis.Build.Id))
            _ubisoftManager.ValidatePublishedModuleSet(required);
    }

    private async Task ApplyAsync()
    {
        if (_busy || _analysis?.Build is null || !_secondaryPairValid) return;
        var selection = CurrentSelection();
        var selected = ResolveCurrentPatchIds();
        if (selected.Count == 0) return;

        var available = _engine.GetApplicablePatches(_analysis).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(x => !available.Contains(x)))
        {
            MessageBox.Show("The selected MAYHEM module composition is not published for this exact build.", "Unsupported selection", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var description = MercenaryPatchSelectionResolver.Describe(selection);
        var targetDescription = _secondaryAnalysis?.Required == true
            ? "the verified Ubisoft Connect executable pair (ACOdyssey.exe + ACOdyssey_plus.exe)"
            : "the verified ACOdyssey.exe";
        var prompt = $"Install the selected MAYHEM modules into {targetDescription}?{Environment.NewLine}{Environment.NewLine}{description}";
        if (MessageBox.Show(
                this,
                prompt,
                "Install MAYHEM",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var analyzedSize = _analysis.Size;
        var analyzedTimestamp = _analysis.PeTimestamp;
        var hadUbisoftPair = _secondaryAnalysis?.Required == true;
        var requiresForge = ForgeLevel255Manager.RequiresExtendedStats(selected);
        var installProgress = new Progress<double>(SetInstallProgress);
        SetInstallProgress(0d);
        SetBusy(true);
        try
        {
            Log("Resolved selection: " + description);
            Log("Low-level patch variants: " + string.Join(", ", selected));
            Log(hadUbisoftPair
                ? "Ubisoft Connect target: both supported executables will be patched and restored as one managed pair."
                : "Single-executable target detected.");
            Log(requiresForge
                ? "Preflight + EXE/Forge backup + staged 255-stat Forge reconstruction started."
                : "Preflight + EXE backup started; Level Unlock Off requires exact vanilla Forge.");

            var state = await _ubisoftManager.ApplyCoordinatedAsync(
                _engine,
                _forgeManager,
                _analysis,
                selected,
                progress: installProgress);

            // ApplyCoordinatedAsync returns only after the EXE transaction(s), Forge commit and
            // their authoritative post-write verification have succeeded. Do not immediately
            // re-read the same 3.26 GB Forge + backup again just to repaint the UI.
            SetInstallProgress(1d);
            Log($"Installed: {string.Join(", ", state.AppliedPatchIds)}");
            Log($"Patched primary EXE SHA-256: {state.PatchedSha256}");
            Log($"Original primary EXE backup: {state.BackupPath}");
            if (hadUbisoftPair)
                Log("Verified patched Ubisoft sibling ACOdyssey_plus.exe and original backup during the coordinated transaction.");

            if (requiresForge)
            {
                Log("Verified MAYHEM 1.2 extended-stat Forge during the coordinated commit.");
                Log($"Forge SHA-256: {ForgeLevel255Manager.PatchedForgeSha256}");
                Log($"Original Forge backup: {_forgeManager.ManagedVanillaBackupPath}");
            }
            else
            {
                Log("Verified exact vanilla Forge during the coordinated install.");
                Log($"Forge SHA-256: {ForgeLevel255Manager.VanillaForgeSha256}");
            }

            _analysis = new TargetAnalysis(
                state.GameExePath,
                state.PatchedSha256,
                analyzedSize,
                analyzedTimestamp,
                null,
                true,
                false,
                "Known MAYHEM patched state.");
            _forgePairValid = true;
            _secondaryPairValid = true;
            _buildValue.Text = "MAYHEM patched";
            _hashValue.Text = state.PatchedSha256[..12] + "...";
            _statusValue.Text = "Known MAYHEM patched state.";
            _statusValue.ForeColor = Color.FromArgb(214, 205, 198);
            UpdateFeatureControlState();
            UpdateActionState();
        }
        catch (Exception ex)
        {
            Log("INSTALL ABORTED: " + ex.Message);
            MessageBox.Show(ex.Message, "Install aborted", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RestoreAsync()
    {
        if (_busy || _analysis is null) return;
        var restoreTarget = _secondaryAnalysis?.Required == true
            ? "the exact hash-verified vanilla Ubisoft executable pair and DataPC_patch_01.forge backups"
            : "the exact hash-verified vanilla ACOdyssey.exe and DataPC_patch_01.forge backups";
        if (MessageBox.Show(
                this,
                $"Restore {restoreTarget} used by the installed MAYHEM configuration?",
                "Restore vanilla",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        SetBusy(true);
        try
        {
            await _ubisoftManager.RestoreCoordinatedAsync(_engine, _forgeManager, _analysis.Path);
            var primaryResult = await _engine.VerifyAsync(_analysis.Path);
            var secondaryResult = await _ubisoftManager.VerifySecondaryAsync(_engine, _analysis.Path, primaryResult.BuildId, primaryResult.IsPatched);
            var forgeResult = await _forgeManager.VerifyPairAsync(_analysis.Path);
            if (!primaryResult.IsValid || primaryResult.IsPatched || !secondaryResult.IsValid || secondaryResult.IsPatched || !forgeResult.IsValid || forgeResult.IsPatched)
                throw new InvalidDataException("Post-restore executable/Forge verification failed.");
            Log(_secondaryAnalysis?.Required == true
                ? "Both Ubisoft executables and Forge restored to exact vanilla identities and SHA-256 verified."
                : "Vanilla executable and Forge state restored and SHA-256 verified.");
            if (secondaryResult.Required)
                Log(secondaryResult.Status);
            Log(forgeResult.Status);
            await RefreshAnalysisCoreAsync();
        }
        catch (Exception ex)
        {
            Log("RESTORE ABORTED: " + ex.Message);
            MessageBox.Show(ex.Message, "Restore aborted", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void InvalidateAnalysisAfterPathEdit()
    {
        if (_busy) return;
        _analysis = null;
        _secondaryAnalysis = null;
        _forgePairValid = false;
        _secondaryPairValid = false;
        SetInstallProgress(0d);
        _buildValue.Text = "Not analyzed";
        _hashValue.Text = "-";
        _statusValue.Text = "Target path changed. Analyze again.";
        _statusValue.ForeColor = TextPrimary;
        UpdateFeatureControlState();
        UpdateActionState();
    }

    private void InvalidateAnalysisAfterBackupStoreChange()
    {
        if (_busy) return;
        _analysis = null;
        _secondaryAnalysis = null;
        _forgePairValid = false;
        _secondaryPairValid = false;
        SetInstallProgress(0d);
        _buildValue.Text = "Not analyzed";
        _hashValue.Text = "-";
        _statusValue.Text = $"Backup store: {_backupStorageMode}. Analyze again.";
        _statusValue.ForeColor = TextPrimary;
        UpdateFeatureControlState();
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        var selectedCount = ResolveCurrentPatchIds().Count;
        _verifyButton.Enabled = !_busy && _analysis is not null;
        _applyButton.Enabled = !_busy && _forgePairValid && _secondaryPairValid && _analysis?.Build is not null && _analysis.HasStateConflict == false && selectedCount > 0;
        _restoreButton.Enabled = !_busy && _analysis is not null && _analysis.MatchesSavedPatchedState;
    }

    private void UpdateFeatureControlState()
    {
        _levelOff.Enabled = !_busy;
        _curveLight.Enabled = !_busy;
        _curveLinear.Enabled = !_busy;
        _pressureX3.Enabled = !_busy;
        _pressureX6.Enabled = !_busy;
        _immediateRecycle.Enabled = !_busy;
        _backupStoreButton.Enabled = !_busy &&
            _analysis?.MatchesSavedPatchedState != true &&
            !StorageHasRecoveryArtifacts(CurrentStorageRoot());
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UseWaitCursor = busy;
        _exePath.Enabled = !busy;
        _browseButton.Enabled = !busy;
        _analyzeButton.Enabled = !busy;
        UpdateFeatureControlState();
        UpdateActionState();
    }

    private void SetInstallProgress(double value)
    {
        var normalized = Math.Clamp(value, 0d, 1d);
        if (InvokeRequired)
        {
            BeginInvoke(() => _installProgress.Value = normalized);
            return;
        }
        _installProgress.Value = normalized;
    }

    private void Log(string text)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private sealed class ThinProgressLine : Control
    {
        private double _value;

        public Color TrackColor { get; set; } = Color.FromArgb(70, 27, 33);
        public Color FillColor { get; set; } = AccentHover;

        public double Value
        {
            get => _value;
            set
            {
                var normalized = Math.Clamp(value, 0d, 1d);
                if (Math.Abs(_value - normalized) < 0.0001d)
                    return;
                _value = normalized;
                Invalidate();
            }
        }

        public ThinProgressLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Surface;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            const int lineHeight = 2;
            var y = Math.Max(0, (ClientSize.Height - lineHeight) / 2);
            using var trackBrush = new SolidBrush(TrackColor);
            e.Graphics.FillRectangle(trackBrush, 0, y, ClientSize.Width, lineHeight);

            var fillWidth = _value >= 1d
                ? ClientSize.Width
                : (int)Math.Floor(ClientSize.Width * _value);
            if (fillWidth <= 0)
                return;

            using var fillBrush = new SolidBrush(FillColor);
            e.Graphics.FillRectangle(fillBrush, 0, y, fillWidth, lineHeight);
        }
    }

    private sealed class BorderedPanel : Panel
    {
        public Color BorderColor { get; set; } = Border;

        public BorderedPanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(BorderColor);
            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.DrawRectangle(pen, rect);
        }
    }
}
