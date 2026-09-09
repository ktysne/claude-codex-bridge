using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexBridgeConsole
{
    public sealed class MainForm : Form
    {
        private const int CodexVersionTimeoutMilliseconds = 5000;

        private const int CommandNotFoundExitCode = 9009;

        private const int KillTimeoutMilliseconds = 2000;

        private const float BaseFontSize = 10F;

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

        private string _codexVersionText = "確認中...";

        public MainForm()
        {
            _settings = new ConsoleSettings();
            _choices = Choices.Load();

            // 既定のシステムフォントより一回り大きくする。定義ファイルの値を読み取る画面であり、
            // モデル名や effort の綴りを取り違えないようにするためである。
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, BaseFontSize);

            Text = "claude-codex-bridge 設定コンソール";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            // 画面の拡大率に合わせて配置ごと拡大する。
            // app.manifest で高 DPI 対応を宣言しているため、基準を 96 dpi と決めておかないと
            // 文字だけが大きくなり、画素で指定した行の高さからはみ出す。
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(960, 560);

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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 208F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));

            layout.Controls.Add(BuildTargetPanel(), 0, 0);

            _codexEnabledCheckBox = new CheckBox
            {
                Text = "GPT 系サブエージェント経路を有効にする (impl-light / impl-standard)",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(3, 6, 3, 3)
            };
            _codexEnabledCheckBox.CheckedChanged += CodexEnabledCheckBox_CheckedChanged;
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
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));

            // 行の高さを指定しないと、子が親より高く配置されて中身が切り取られる。
            // 対象パスと再読込ボタンの文字が見えなくなる。
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

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
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            // 見出しは 2 行に折り返すことがあるため、本文の行より高くする。
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            for (int i = 0; i < 3; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 3F));
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
                Width = 104,
                Height = 34,
                Margin = new Padding(6, 0, 0, 0)
            };
            _closeButton.Click += CloseButton_Click;

            _saveButton = new Button
            {
                Text = "保存",
                Width = 104,
                Height = 34,
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
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, BaseFontSize, FontStyle.Bold)
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
            // バージョンの取得は起動時の 1 回だけなので、再読込では取得済みの結果を出し直す。
            _codexVersionLabel.Text = "codex --version: " + _codexVersionText;

            if (_settings.MissingFiles.Count > 0 || _settings.UnreadableFiles.Count > 0)
            {
                var reasons = new List<string>();
                if (_settings.MissingFiles.Count > 0)
                {
                    reasons.Add("見つからない: " + string.Join(", ", _settings.MissingFiles));
                }

                if (_settings.UnreadableFiles.Count > 0)
                {
                    reasons.Add("読めない: " + string.Join(" / ", _settings.UnreadableFiles));
                }

                _missingFilesLabel.Text = "保存できない。" + string.Join("  ", reasons);
            }
            else if (_settings.CodexEnabledInvalidFiles.Count > 0)
            {
                _missingFilesLabel.Text = "codex_enabled の値が不正である: "
                    + string.Join(", ", _settings.CodexEnabledInvalidFiles)
                    + "。無効として表示している。保存すると表示どおりの値へ直す。";
            }
            else if (_settings.CodexEnabledMismatch)
            {
                _missingFilesLabel.Text = "impl-light と impl-standard の codex_enabled が食い違っている。"
                    + "両方が有効なときだけ有効として表示する。チェックを変えて保存すると両方に同じ値を書く。";
            }
            else
            {
                _missingFilesLabel.Text = string.Empty;
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

        private void CodexEnabledCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_loadingControls)
            {
                return;
            }

            // 2 定義の値が食い違っているときは、表示上の値が変わらなくても両方へ書き戻す必要がある。
            // 利用者がトグルを操作したことを保存側へ伝える。
            _settings.CodexEnabledExplicit = true;
            ControlValueChanged(sender, e);
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

            try
            {
                _settings.Reload();
            }
            catch (Exception exception) when (
                exception is IOException
                || exception is UnauthorizedAccessException
                || exception is InvalidDataException
                || exception is DecoderFallbackException)
            {
                // 定義ファイルが外部で壊された場合に画面ごと落とさない。読み込み前の状態を保つ。
                MessageBox.Show(
                    this,
                    "定義ファイルを読み込めませんでした。" + Environment.NewLine + exception.Message,
                    "再読込に失敗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                _saveStatusLabel.Text = "再読込に失敗しました。表示は読み込み前のままです。";
                return;
            }

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
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 読み取り専用や権限不足は UnauthorizedAccessException で来る。IOException から派生しないため個別に受ける。
                string message = "定義ファイルを保存できませんでした。" + Environment.NewLine + exception.Message;
                if (_settings.LastChangedFiles.Count > 0)
                {
                    message += Environment.NewLine
                        + Environment.NewLine
                        + "中断までに保存されたファイル: "
                        + string.Join(", ", _settings.LastChangedFiles);
                }

                // ダイアログを閉じた後に前回の成功表示が残らないよう、状態行も更新する。
                _saveStatusLabel.Text = _settings.LastChangedFiles.Count > 0
                    ? "保存に失敗しました。中断までに保存されたファイル: "
                        + string.Join(", ", _settings.LastChangedFiles)
                    : "保存に失敗しました。書き換えられたファイルはありません。";

                MessageBox.Show(
                    this,
                    message,
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

            UpdateStatusDisplay();
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
            // CODEX_HOME は必ず明示する。既定の ~/.codex への暗黙依存を作らないためである。
            string codexHome = _settings.ExpandedCodexHome;
            string version = await Task.Run(() => GetCodexVersion(codexHome));
            if (IsDisposed || Disposing)
            {
                return;
            }

            _codexVersionText = version;
            _codexVersionLabel.Text = "codex --version: " + version;
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                using (var killer = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/PID " + process.Id + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }))
                {
                    if (killer != null)
                    {
                        killer.WaitForExit(KillTimeoutMilliseconds);
                    }
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception || exception is InvalidOperationException)
            {
                // taskkill を起動できない場合に備え、少なくとも自分が起動した cmd は止める。
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception || exception is InvalidOperationException)
            {
                // 既に終了している場合は何もしない。画面の起動を待たせないためである。
            }
        }

        private static string GetCodexVersion(string codexHome)
        {
            // 認証ホームが分からないまま codex を起動しない。
            // 既定の ~/.codex や親プロセスの環境変数へ暗黙に依存する呼び出しを作らないためである。
            if (string.IsNullOrEmpty(codexHome))
            {
                return "認証ホーム未設定のため確認しない";
            }

            try
            {
                // Windows の npm は codex.cmd を置く。UseShellExecute = false で "codex" を直接起動すると
                // .cmd を解決できず、導入済みでも見つからない扱いになる。PATH の解決を cmd に任せる。
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c codex --version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                // 認証ホームは常に明示する。既定の ~/.codex に暗黙に依存する呼び出しを作らない。
                startInfo.EnvironmentVariables["CODEX_HOME"] = codexHome;

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    if (!process.WaitForExit(CodexVersionTimeoutMilliseconds))
                    {
                        // 起動したのは cmd であり、codex 本体はその子である。
                        // cmd だけを止めても子が残るため、プロセスツリーごと落とす。
                        KillProcessTree(process);
                        return "タイムアウト";
                    }

                    string standardOutput = process.StandardOutput.ReadToEnd().Trim();
                    string standardError = process.StandardError.ReadToEnd().Trim();
                    if (process.ExitCode == 0 && standardOutput.Length > 0)
                    {
                        return standardOutput;
                    }

                    // cmd は目的のコマンドが見つからないとき 9009 を返す。
                    // cmd 自体は起動できるため Win32Exception にはならない。
                    if (process.ExitCode == CommandNotFoundExitCode)
                    {
                        return "見つからない";
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
