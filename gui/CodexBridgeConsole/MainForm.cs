using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexBridgeConsole
{
    public sealed class MainForm : Form
    {
        private const int CodexVersionTimeoutMilliseconds = 5000;

        private readonly ConsoleSettings _settings;
        private readonly Choices _choices;
        private Label _codexHomeLabel;
        private Label _codexVersionLabel;
        private CheckBox _codexEnabledCheckBox;
        private ComboBox _hardModelComboBox;
        private ComboBox _hardEffortComboBox;
        private ComboBox _standardModelComboBox;
        private ComboBox _standardEffortComboBox;
        private ComboBox _standardGptModelComboBox;
        private ComboBox _standardGptEffortComboBox;
        private ComboBox _lightModelComboBox;
        private ComboBox _lightEffortComboBox;
        private ComboBox _lightGptModelComboBox;
        private ComboBox _lightGptEffortComboBox;
        private Button _reloadButton;
        private Button _saveButton;
        private Button _closeButton;
        private Label _missingFilesLabel;
        private Label _saveStatusLabel;
        private bool _loadingControls;
        private bool _codexVersionStarted;

        public MainForm()
        {
            _settings = new ConsoleSettings();
            _choices = Choices.Load();

            Text = "claude-codex-bridge 設定コンソール";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(820, 480);

            BuildControls();
            LoadControlsFromSettings();

            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;
        }

        private void BuildControls()
        {
            var layout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 8,
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                AutoSize = false
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 168F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            layout.Controls.Add(BuildTargetPanel(), 0, 0);

            _codexEnabledCheckBox = new CheckBox
            {
                Text = "GPT 系サブエージェント経路を有効にする (impl-light / impl-standard)",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 6, 3, 3)
            };
            _codexEnabledCheckBox.CheckedChanged += ControlValueChanged;
            layout.Controls.Add(_codexEnabledCheckBox, 0, 1);

            layout.Controls.Add(BuildDefinitionsTable(), 0, 2);
            layout.Controls.Add(BuildStatusPanel(), 0, 3);

            _missingFilesLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.Firebrick,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 3)
            };
            layout.Controls.Add(_missingFilesLabel, 0, 4);

            var noticeLabel = new Label
            {
                Text = "保存後、Claude Code を再起動すると反映される",
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 3)
            };
            layout.Controls.Add(noticeLabel, 0, 5);

            _saveStatusLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 3)
            };
            layout.Controls.Add(_saveStatusLabel, 0, 6);
            layout.Controls.Add(BuildButtonPanel(), 0, 7);

            Controls.Add(layout);
        }

        private Control BuildTargetPanel()
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));

            var targetLabel = new Label
            {
                Text = "対象: " + _settings.RootDirectory,
                AutoEllipsis = true,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 6, 3)
            };
            panel.Controls.Add(targetLabel, 0, 0);

            _reloadButton = new Button
            {
                Text = "再読込",
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(3)
            };
            _reloadButton.Click += ReloadButton_Click;
            panel.Controls.Add(_reloadButton, 1, 0);
            return panel;
        }

        private Control BuildDefinitionsTable()
        {
            var table = new TableLayoutPanel
            {
                ColumnCount = 5,
                RowCount = 4,
                Dock = DockStyle.Fill,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                Margin = new Padding(3),
                Padding = new Padding(3)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16F));
            for (int i = 0; i < 4; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            }

            table.Controls.Add(CreateHeaderLabel("区分"), 0, 0);
            table.Controls.Add(CreateHeaderLabel("Claude モデル (フォールバック時)"), 1, 0);
            table.Controls.Add(CreateHeaderLabel("effort (フォールバック時)"), 2, 0);
            table.Controls.Add(CreateHeaderLabel("GPT モデル"), 3, 0);
            table.Controls.Add(CreateHeaderLabel("effort"), 4, 0);

            table.Controls.Add(CreateRowLabel("hard"), 0, 1);
            _hardModelComboBox = CreateComboBox();
            _hardEffortComboBox = CreateComboBox();
            table.Controls.Add(_hardModelComboBox, 1, 1);
            table.Controls.Add(_hardEffortComboBox, 2, 1);
            table.Controls.Add(CreateCenteredLabel("(Codex を使わない)"), 3, 1);
            table.SetColumnSpan(table.Controls[table.Controls.Count - 1], 2);

            table.Controls.Add(CreateRowLabel("standard"), 0, 2);
            _standardModelComboBox = CreateComboBox();
            _standardEffortComboBox = CreateComboBox();
            _standardGptModelComboBox = CreateComboBox();
            _standardGptEffortComboBox = CreateComboBox();
            table.Controls.Add(_standardModelComboBox, 1, 2);
            table.Controls.Add(_standardEffortComboBox, 2, 2);
            table.Controls.Add(_standardGptModelComboBox, 3, 2);
            table.Controls.Add(_standardGptEffortComboBox, 4, 2);

            table.Controls.Add(CreateRowLabel("light"), 0, 3);
            _lightModelComboBox = CreateComboBox();
            _lightEffortComboBox = CreateComboBox();
            _lightGptModelComboBox = CreateComboBox();
            _lightGptEffortComboBox = CreateComboBox();
            table.Controls.Add(_lightModelComboBox, 1, 3);
            table.Controls.Add(_lightEffortComboBox, 2, 3);
            table.Controls.Add(_lightGptModelComboBox, 3, 3);
            table.Controls.Add(_lightGptEffortComboBox, 4, 3);

            return table;
        }

        private Control BuildStatusPanel()
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Padding = new Padding(0)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _codexHomeLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3)
            };
            _codexVersionLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3)
            };
            panel.Controls.Add(_codexHomeLabel, 0, 0);
            panel.Controls.Add(_codexVersionLabel, 0, 1);
            return panel;
        }

        private Control BuildButtonPanel()
        {
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 5, 0, 0),
                Margin = new Padding(0)
            };

            _closeButton = new Button
            {
                Text = "閉じる",
                Width = 88,
                Height = 28,
                Margin = new Padding(6, 0, 0, 0)
            };
            _closeButton.Click += CloseButton_Click;

            _saveButton = new Button
            {
                Text = "保存",
                Width = 88,
                Height = 28,
                Margin = new Padding(6, 0, 0, 0)
            };
            _saveButton.Click += SaveButton_Click;

            panel.Controls.Add(_closeButton);
            panel.Controls.Add(_saveButton);
            return panel;
        }

        private ComboBox CreateComboBox()
        {
            var comboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                Margin = new Padding(3)
            };
            comboBox.TextChanged += ControlValueChanged;
            return comboBox;
        }

        private static Label CreateHeaderLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(3),
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
            };
        }

        private static Label CreateRowLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(3)
            };
        }

        private static Label CreateCenteredLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(3)
            };
        }

        private void LoadControlsFromSettings()
        {
            _loadingControls = true;
            try
            {
                SetComboItems(_hardModelComboBox, _choices.ClaudeModels, _settings.ImplHard.ClaudeModel);
                SetComboItems(_hardEffortComboBox, _choices.ClaudeEfforts, _settings.ImplHard.ClaudeEffort);
                SetComboItems(_standardModelComboBox, _choices.ClaudeModels, _settings.ImplStandard.ClaudeModel);
                SetComboItems(_standardEffortComboBox, _choices.ClaudeEfforts, _settings.ImplStandard.ClaudeEffort);
                SetComboItems(_standardGptModelComboBox, _choices.GptModels, _settings.ImplStandard.CodexModel);
                SetComboItems(
                    _standardGptEffortComboBox,
                    _choices.GptEfforts,
                    _settings.ImplStandard.CodexReasoningEffort);
                SetComboItems(_lightModelComboBox, _choices.ClaudeModels, _settings.ImplLight.ClaudeModel);
                SetComboItems(_lightEffortComboBox, _choices.ClaudeEfforts, _settings.ImplLight.ClaudeEffort);
                SetComboItems(_lightGptModelComboBox, _choices.GptModels, _settings.ImplLight.CodexModel);
                SetComboItems(
                    _lightGptEffortComboBox,
                    _choices.GptEfforts,
                    _settings.ImplLight.CodexReasoningEffort);
                _codexEnabledCheckBox.Checked = _settings.CodexEnabled;
            }
            finally
            {
                _loadingControls = false;
            }

            UpdateStatusDisplay();
            UpdateGptControlState();
            UpdateControlState();
        }

        private static void SetComboItems(
            ComboBox comboBox,
            System.Collections.Generic.IReadOnlyList<string> choices,
            string currentValue)
        {
            comboBox.Items.Clear();
            for (int i = 0; i < choices.Count; i++)
            {
                comboBox.Items.Add(choices[i]);
            }

            if (!string.IsNullOrEmpty(currentValue) && comboBox.Items.IndexOf(currentValue) < 0)
            {
                comboBox.Items.Insert(0, currentValue);
            }

            comboBox.Text = currentValue ?? string.Empty;
        }

        private void UpdateStatusDisplay()
        {
            string home = _settings.CodexHome;
            string homeState = string.IsNullOrEmpty(home)
                ? "未設定"
                : (_settings.CodexHomeExists ? "存在する" : "存在しない");
            _codexHomeLabel.Text = "codex_home: " + (home ?? "(未設定)")
                + " (" + homeState + ")    codex_sandbox: "
                + (_settings.CodexSandbox ?? "(未設定)");
            _codexVersionLabel.Text = "codex --version: 確認中...";

            if (_settings.CanSave)
            {
                _missingFilesLabel.Text = string.Empty;
            }
            else
            {
                _missingFilesLabel.Text = "保存できない。見つからない定義ファイル: "
                    + string.Join(", ", _settings.MissingFiles);
            }
        }

        private void UpdateControlState()
        {
            _saveButton.Enabled = _settings.CanSave;
            UpdateGptControlState();
        }

        private void UpdateGptControlState()
        {
            bool enabled = _codexEnabledCheckBox.Checked;
            _standardGptModelComboBox.Enabled = enabled;
            _standardGptEffortComboBox.Enabled = enabled;
            _lightGptModelComboBox.Enabled = enabled;
            _lightGptEffortComboBox.Enabled = enabled;
        }

        private void SyncSettingsFromControls()
        {
            _settings.ImplHard.ClaudeModel = _hardModelComboBox.Text;
            _settings.ImplHard.ClaudeEffort = _hardEffortComboBox.Text;
            _settings.ImplStandard.ClaudeModel = _standardModelComboBox.Text;
            _settings.ImplStandard.ClaudeEffort = _standardEffortComboBox.Text;
            _settings.ImplStandard.CodexModel = _standardGptModelComboBox.Text;
            _settings.ImplStandard.CodexReasoningEffort = _standardGptEffortComboBox.Text;
            _settings.ImplLight.ClaudeModel = _lightModelComboBox.Text;
            _settings.ImplLight.ClaudeEffort = _lightEffortComboBox.Text;
            _settings.ImplLight.CodexModel = _lightGptModelComboBox.Text;
            _settings.ImplLight.CodexReasoningEffort = _lightGptEffortComboBox.Text;
            _settings.CodexEnabled = _codexEnabledCheckBox.Checked;
        }

        private void ControlValueChanged(object sender, EventArgs e)
        {
            if (_loadingControls)
            {
                return;
            }

            SyncSettingsFromControls();
            UpdateControlState();
        }

        private void ReloadButton_Click(object sender, EventArgs e)
        {
            SyncSettingsFromControls();
            if (_settings.HasChanges)
            {
                DialogResult result = MessageBox.Show(
                    this,
                    "未保存の変更を破棄して再読込しますか。",
                    "再読込の確認",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            _settings.Reload();
            LoadControlsFromSettings();
            _saveStatusLabel.Text = "再読込しました。未保存の変更は破棄されています。";
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            SaveSettings();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        private bool SaveSettings()
        {
            SyncSettingsFromControls();
            ConsoleSettingsSaveResult result;
            try
            {
                result = _settings.Save();
            }
            catch (FrontMatterFileChangedException)
            {
                // 書き込みは 1 ファイルずつ行うため、中断までに保存されたファイルが残る。どれが残ったかを示す。
                string savedFiles = string.Join(", ", _settings.LastChangedFiles);
                string message = "読み込み後に定義ファイルが外部で変更されたため保存できません。再読込してから、もう一度保存してください。";
                if (_settings.LastChangedFiles.Count > 0)
                {
                    message += Environment.NewLine
                        + Environment.NewLine
                        + "中断までに保存されたファイル: " + savedFiles;
                    _saveStatusLabel.Text = "保存を中断しました。中断までに保存されたファイル: " + savedFiles;
                }
                else
                {
                    _saveStatusLabel.Text = "保存を中断しました。書き換えられたファイルはありません。";
                }

                MessageBox.Show(
                    this,
                    message,
                    "保存できない",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
            catch (IOException exception)
            {
                MessageBox.Show(
                    this,
                    "定義ファイルを保存できませんでした。\n" + exception.Message,
                    "保存に失敗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            if (!result.Succeeded)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, result.ValidationErrors),
                    "入力を確認",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                UpdateControlState();
                return false;
            }

            if (result.ChangedFiles.Count == 0)
            {
                _saveStatusLabel.Text = "変更されたファイルはありません。";
            }
            else
            {
                _saveStatusLabel.Text = "書き換えたファイル: "
                    + string.Join(", ", result.ChangedFiles);
            }

            UpdateControlState();
            return true;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            if (_codexVersionStarted)
            {
                return;
            }

            _codexVersionStarted = true;
            LoadCodexVersionAsync();
        }

        private async void LoadCodexVersionAsync()
        {
            string version = await Task.Run(() => GetCodexVersion());
            if (IsDisposed || Disposing)
            {
                return;
            }

            _codexVersionLabel.Text = "codex --version: " + version;
        }

        private static string GetCodexVersion()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "codex",
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    if (!process.WaitForExit(CodexVersionTimeoutMilliseconds))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                            // タイムアウト後にプロセスが終了していても、画面の起動を待たせない。
                        }

                        return "タイムアウト";
                    }

                    string standardOutput = process.StandardOutput.ReadToEnd().Trim();
                    string standardError = process.StandardError.ReadToEnd().Trim();
                    if (process.ExitCode == 0 && standardOutput.Length > 0)
                    {
                        return standardOutput;
                    }

                    if (standardError.Length > 0)
                    {
                        return "取得失敗 (" + standardError + ")";
                    }

                    return "取得失敗";
                }
            }
            catch (Win32Exception)
            {
                return "見つからない";
            }
            catch (InvalidOperationException)
            {
                return "取得失敗";
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SyncSettingsFromControls();
            if (!_settings.HasChanges)
            {
                return;
            }

            DialogResult result = MessageBox.Show(
                this,
                "未保存の変更があります。保存して閉じますか。",
                "終了の確認",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                e.Cancel = !SaveSettings();
            }
            else if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
            }
        }
    }
}
