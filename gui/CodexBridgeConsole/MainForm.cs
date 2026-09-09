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

        // agent-limit-checker の画面 (renderer/style.css) と同じ優先順で選ぶ。
        // 同じ利用者が並べて使う道具であり、見た目を揃える。
        // 先頭の Segoe UI は日本語の字を持たないが、日本語の部分は Windows の
        // フォントリンクで後続の書体が使われる。CSS の指定と同じ振る舞いである。
        private static readonly string[] PreferredFontFamilies =
        {
            "Segoe UI",
            "Yu Gothic UI",
            "Meiryo"
        };

        private static string _baseFontFamily;

        private readonly ConsoleSettings _settings;
        private readonly Choices _choices;
        private Label _codexHomeLabel;
        private Label _codexVersionLabel;
        private Label _codexCatalogLabel;
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
        private Color _saveStatusDefaultForeColor;
        private bool _loadingControls;
        private bool _codexVersionStarted;

        // codex debug models から取れた目録。取れなかった間は null で、_choices の既定値を使う。
        private CodexModelCatalog _codexModelCatalog;

        private string _codexVersionText = "確認中...";

        // codex debug models の取得結果に応じた文言。GPT モデル一覧が目録由来か既定値かを利用者に示す。
        private string _codexCatalogText = "取得中...";

        private Control _layout;
        private TableLayoutPanel _definitionsTable;

        public MainForm()
        {
            _settings = new ConsoleSettings();
            _choices = Choices.Load();

            // 既定のシステムフォントより一回り大きくする。定義ファイルの値を読み取る画面であり、
            // モデル名や effort の綴りを取り違えないようにするためである。
            Font = CreateBaseFont(FontStyle.Regular);

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


            BuildControls();
            LoadControlsFromSettings();

            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;
        }

        private void BuildControls()
        {
            // 縦に積むだけの入れ物にする。表形式の入れ物は余った高さを行へ配るため、
            // 画面の高さと中身の高さが食い違うと、表がつぶれて余白だけが残る。
            var layout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(12)
            };

            // 横幅の基準は定義の表とする。画面の中で最も広い中身であるためである。
            _definitionsTable = (TableLayoutPanel)BuildDefinitionsTable();
            int contentWidth = _definitionsTable.PreferredSize.Width;

            layout.Controls.Add(BuildTargetPanel(contentWidth));

            _codexEnabledCheckBox = new CheckBox
            {
                Text = "GPT 系サブエージェント経路を有効にする (impl-light / impl-standard)",
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 6)
            };
            _codexEnabledCheckBox.CheckedChanged += CodexEnabledCheckBox_CheckedChanged;
            layout.Controls.Add(_codexEnabledCheckBox);

            layout.Controls.Add(_definitionsTable);
            layout.Controls.Add(BuildStatusPanel(contentWidth));

            // 警告が無いときは場所を取らない。空の行が余白として残ると読みにくい。
            // 高さは行数で固定するため、収まらない文字列は末尾を省略記号にする。
            // 中断までに保存されたファイルの一覧など、長い文言が切れて読めなくなるのを防ぐ。
            _missingFilesLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Visible = false,
                Width = contentWidth,
                Height = SingleLineHeight() * 2,
                ForeColor = Color.Firebrick,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 3)
            };
            layout.Controls.Add(_missingFilesLabel);

            var noticeLabel = new Label
            {
                Text = "保存後、Claude Code を再起動すると反映されます",
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 3)
            };
            layout.Controls.Add(noticeLabel);

            _saveStatusLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Width = contentWidth,
                Height = SingleLineHeight(),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 3)
            };
            _saveStatusDefaultForeColor = _saveStatusLabel.ForeColor;
            layout.Controls.Add(_saveStatusLabel);
            layout.Controls.Add(BuildButtonPanel(contentWidth));

            _layout = layout;
            Controls.Add(layout);
        }

        private Control BuildTargetPanel(int width)
        {
            _reloadButton = CreateActionButton("再読込");
            _reloadButton.Anchor = AnchorStyles.Right;
            _reloadButton.Click += ReloadButton_Click;

            var panel = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,

                // 高さは中身に決めさせる。組み立ての時点では、まだ画面の書体が
                // 子へ伝わっておらず、必要な高さを正しく測れない。
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(width, 0),
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var targetLabel = new Label
            {
                Text = "対象: " + _settings.RootDirectory,
                AutoEllipsis = true,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = SingleLineHeight(),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 6, 3)
            };
            panel.Controls.Add(targetLabel, 0, 0);
            panel.Controls.Add(_reloadButton, 1, 0);
            return panel;
        }

        private Control BuildDefinitionsTable()
        {
            var table = new TableLayoutPanel
            {
                ColumnCount = 5,
                RowCount = 4,

                // 縦に積む入れ物の中では、自分の大きさを自分で決める必要がある。
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                Margin = new Padding(3),
                Padding = new Padding(3)
            };
            // 列幅と行の高さは中身に決めさせる。見出しの文字が最も長いことが多く、
            // 画素で決めると書体を変えたときに切れる。
            for (int i = 0; i < 5; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            }

            for (int i = 0; i < 4; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            int claudeModelWidth = ComboBoxWidth(
                _choices.ClaudeModels,
                _settings.ImplHard.ClaudeModel,
                _settings.ImplStandard.ClaudeModel,
                _settings.ImplLight.ClaudeModel);
            int claudeEffortWidth = ComboBoxWidth(
                _choices.ClaudeEfforts,
                _settings.ImplHard.ClaudeEffort,
                _settings.ImplStandard.ClaudeEffort,
                _settings.ImplLight.ClaudeEffort);
            int gptModelWidth = ComboBoxWidth(
                _choices.GptModels,
                _settings.ImplStandard.CodexModel,
                _settings.ImplLight.CodexModel);
            int gptEffortWidth = ComboBoxWidth(
                _choices.GptEfforts,
                _settings.ImplStandard.CodexReasoningEffort,
                _settings.ImplLight.CodexReasoningEffort);

            table.Controls.Add(CreateHeaderLabel("区分"), 0, 0);
            table.Controls.Add(CreateHeaderLabel("Claude モデル (フォールバック時)"), 1, 0);
            table.Controls.Add(CreateHeaderLabel("effort (フォールバック時)"), 2, 0);
            table.Controls.Add(CreateHeaderLabel("GPT モデル"), 3, 0);
            table.Controls.Add(CreateHeaderLabel("effort"), 4, 0);

            table.Controls.Add(CreateRowLabel("hard"), 0, 1);
            _hardModelComboBox = CreateComboBox(claudeModelWidth);
            _hardEffortComboBox = CreateComboBox(claudeEffortWidth);
            _hardModelComboBox.TextChanged += ClaudeModelTextChanged;
            table.Controls.Add(_hardModelComboBox, 1, 1);
            table.Controls.Add(_hardEffortComboBox, 2, 1);
            table.Controls.Add(CreateCenteredLabel("(Codex を使わない)"), 3, 1);
            table.SetColumnSpan(table.Controls[table.Controls.Count - 1], 2);

            table.Controls.Add(CreateRowLabel("standard"), 0, 2);
            _standardModelComboBox = CreateComboBox(claudeModelWidth);
            _standardEffortComboBox = CreateComboBox(claudeEffortWidth);
            _standardGptModelComboBox = CreateComboBox(gptModelWidth);
            _standardGptEffortComboBox = CreateComboBox(gptEffortWidth);
            _standardModelComboBox.TextChanged += ClaudeModelTextChanged;
            _standardGptModelComboBox.TextChanged += GptModelTextChanged;
            table.Controls.Add(_standardModelComboBox, 1, 2);
            table.Controls.Add(_standardEffortComboBox, 2, 2);
            table.Controls.Add(_standardGptModelComboBox, 3, 2);
            table.Controls.Add(_standardGptEffortComboBox, 4, 2);

            table.Controls.Add(CreateRowLabel("light"), 0, 3);
            _lightModelComboBox = CreateComboBox(claudeModelWidth);
            _lightEffortComboBox = CreateComboBox(claudeEffortWidth);
            _lightGptModelComboBox = CreateComboBox(gptModelWidth);
            _lightGptEffortComboBox = CreateComboBox(gptEffortWidth);
            _lightModelComboBox.TextChanged += ClaudeModelTextChanged;
            _lightGptModelComboBox.TextChanged += GptModelTextChanged;
            table.Controls.Add(_lightModelComboBox, 1, 3);
            table.Controls.Add(_lightEffortComboBox, 2, 3);
            table.Controls.Add(_lightGptModelComboBox, 3, 3);
            table.Controls.Add(_lightGptEffortComboBox, 4, 3);

            return table;
        }

        private Control BuildStatusPanel(int width)
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                Width = width,
                Height = SingleLineHeight() * 3 + 12,
                Margin = new Padding(3, 6, 3, 6),
                Padding = new Padding(0)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));

            _codexHomeLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 0, 3, 0)
            };
            _codexVersionLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 0, 3, 0)
            };
            _codexCatalogLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 0, 3, 0)
            };
            panel.Controls.Add(_codexHomeLabel, 0, 0);
            panel.Controls.Add(_codexVersionLabel, 0, 1);
            panel.Controls.Add(_codexCatalogLabel, 0, 2);
            return panel;
        }

        private Control BuildButtonPanel(int width)
        {
            _closeButton = CreateActionButton("閉じる");
            _closeButton.Margin = new Padding(6, 0, 0, 0);
            _closeButton.Click += CloseButton_Click;

            _saveButton = CreateActionButton("保存");
            _saveButton.Margin = new Padding(6, 0, 0, 0);
            _saveButton.Click += SaveButton_Click;

            // 2 つのボタンの幅を広い方に揃える。文字数が違うだけで大きさが変わると落ち着かない。
            int buttonWidth = Math.Max(_closeButton.PreferredSize.Width, _saveButton.PreferredSize.Width);
            _closeButton.MinimumSize = new Size(buttonWidth, 0);
            _saveButton.MinimumSize = new Size(buttonWidth, 0);

            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(width, 0),
                Margin = new Padding(3, 12, 3, 3),
                Padding = new Padding(0)
            };

            panel.Controls.Add(_closeButton);
            panel.Controls.Add(_saveButton);
            return panel;
        }

        // 押しやすい大きさは文字の大きさで決まる。画素で決めると、書体を変えたときに
        // 文字が枠に収まらなかったり、上下の余白が偏ったりする。
        private Button CreateActionButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(16, 4, 16, 4),
                Margin = new Padding(3)
            };
        }

        // 中身の高さに画面を合わせる。警告の行が出入りすると必要な高さが変わる。
        private void AdjustWindowSize()
        {
            if (!IsHandleCreated || _layout == null)
            {
                return;
            }

            Size preferred = _layout.PreferredSize;
            if (ClientSize != preferred)
            {
                ClientSize = preferred;
            }
        }

        // 1 行分の高さ。書体や文字の大きさを変えても足りなくなるのを防ぐため、余白を足す。
        private int SingleLineHeight()
        {
            return Font.Height + 10;
        }

        // 一覧の中で最も長い値が収まる幅を求める。開閉のボタンと内側の余白の分を足す。
        private int ComboBoxWidth(IReadOnlyList<string> choices, params string[] currentValues)
        {
            int widest = 0;
            for (int i = 0; i < choices.Count; i++)
            {
                widest = Math.Max(widest, TextRenderer.MeasureText(choices[i], Font).Width);
            }

            for (int i = 0; i < currentValues.Length; i++)
            {
                if (!string.IsNullOrEmpty(currentValues[i]))
                {
                    widest = Math.Max(widest, TextRenderer.MeasureText(currentValues[i], Font).Width);
                }
            }

            return widest + SystemInformation.VerticalScrollBarWidth + 16;
        }

        private ComboBox CreateComboBox(int width)
        {
            var comboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Width = width,

                // 列の幅は中身の希望する大きさから決まる。最小の幅として渡さないと、
                // 一覧の値が入らない細さまで縮む。
                MinimumSize = new Size(width, 0),
                IntegralHeight = false,
                Margin = new Padding(3)
            };
            comboBox.TextChanged += ControlValueChanged;
            return comboBox;
        }

        // 表の中の文字は AutoSize に任せる。WinForms が自分の描き方で必要な大きさを
        // 計算するため、こちらで測るより確実に切れない。
        private static Label CreateHeaderLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(8, 6, 8, 6),
                Font = CreateBaseFont(FontStyle.Bold)
            };
        }

        private static Label CreateRowLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.None,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(8, 6, 8, 6)
            };
        }

        private static Font CreateBaseFont(FontStyle style)
        {
            if (_baseFontFamily == null)
            {
                _baseFontFamily = ResolveBaseFontFamily();
            }

            return new Font(_baseFontFamily, BaseFontSize, style);
        }

        private static string ResolveBaseFontFamily()
        {
            FontFamily[] installed = FontFamily.Families;
            for (int i = 0; i < PreferredFontFamilies.Length; i++)
            {
                for (int j = 0; j < installed.Length; j++)
                {
                    if (string.Equals(installed[j].Name, PreferredFontFamilies[i], StringComparison.OrdinalIgnoreCase))
                    {
                        return PreferredFontFamilies[i];
                    }
                }
            }

            // どれも入っていない環境では、その環境の標準の書体に任せる。
            return SystemFonts.MessageBoxFont.FontFamily.Name;
        }

        private static Label CreateCenteredLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.None,
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
                SetComboItems(
                    _hardEffortComboBox,
                    _choices.ClaudeEffortsFor(_settings.ImplHard.ClaudeModel),
                    _settings.ImplHard.ClaudeEffort);
                SetComboItems(_standardModelComboBox, _choices.ClaudeModels, _settings.ImplStandard.ClaudeModel);
                SetComboItems(
                    _standardEffortComboBox,
                    _choices.ClaudeEffortsFor(_settings.ImplStandard.ClaudeModel),
                    _settings.ImplStandard.ClaudeEffort);
                SetComboItems(_standardGptModelComboBox, _choices.GptModels, _settings.ImplStandard.CodexModel);
                SetComboItems(
                    _standardGptEffortComboBox,
                    _choices.GptEfforts,
                    _settings.ImplStandard.CodexReasoningEffort);
                SetComboItems(_lightModelComboBox, _choices.ClaudeModels, _settings.ImplLight.ClaudeModel);
                SetComboItems(
                    _lightEffortComboBox,
                    _choices.ClaudeEffortsFor(_settings.ImplLight.ClaudeModel),
                    _settings.ImplLight.ClaudeEffort);
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

            // 選択肢を組み立て直したので、取得済みの目録を当て直す。
            // 当てないと、再読込のたびに GPT 側が既定の選択肢へ戻る。
            if (_codexModelCatalog != null)
            {
                ApplyCodexModelCatalog();
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
            // 存在するときは何も添えない。存在しないときだけ示す。
            // 認証ホームが無いことは、ログインが済んでいない合図であるためである。
            string home = _settings.CodexHome;
            string homeNote = string.Empty;
            if (string.IsNullOrEmpty(home))
            {
                homeNote = " (未設定)";
            }
            else if (!_settings.CodexHomeExists)
            {
                homeNote = " (存在しない)";
            }

            _codexHomeLabel.Text = "codex_home: " + (home ?? "(未設定)")
                + homeNote + "    codex_sandbox: "
                + (_settings.CodexSandbox ?? "(未設定)");
            // バージョンの取得は起動時の 1 回だけなので、再読込では取得済みの結果を出し直す。
            _codexVersionLabel.Text = "codex --version: " + _codexVersionText;
            // 目録の取得も起動時の 1 回だけなので、再読込では取得済みの結果を出し直す。
            _codexCatalogLabel.Text = "GPT モデル一覧: " + _codexCatalogText;

            if (_settings.MissingFiles.Count > 0)
            {
                // ラベルは 2 行固定で末尾が省略記号になる。ファイル一覧は長くなりやすく、
                // 後ろに置くと配置手順の案内ごと切れてしまうため、案内を一覧より前に置く。
                var text = new StringBuilder("保存できない。定義の配置は docs/setup.md の手順に従う。");
                text.Append("見つからない: ").Append(string.Join(", ", _settings.MissingFiles)).Append('。');
                if (_settings.UnreadableFiles.Count > 0)
                {
                    text.Append("読めない: ").Append(string.Join(" / ", _settings.UnreadableFiles));
                }

                _missingFilesLabel.Text = text.ToString();
            }
            else if (_settings.UnreadableFiles.Count > 0)
            {
                // 読めないだけの場合、置き場所は分かっていて中身が壊れているだけなので配置手順は無関係である。
                _missingFilesLabel.Text = "保存できない。読めない: " + string.Join(" / ", _settings.UnreadableFiles);
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

            // 警告が無いときは行ごと隠す。空の行が余白として残ると読みにくい。
            _missingFilesLabel.Visible = _missingFilesLabel.Text.Length > 0;
            AdjustWindowSize();
        }

        // 状態行の文字色は失敗のときだけ警告色にする。成功と未保存は通常色で区別しない。
        private void SetSaveStatus(string text, bool isError)
        {
            _saveStatusLabel.Text = text;
            _saveStatusLabel.ForeColor = isError
                ? _missingFilesLabel.ForeColor
                : _saveStatusDefaultForeColor;
        }

        private void UpdateControlState()
        {
            _saveButton.Enabled = _settings.CanSave && _settings.HasChanges;
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
            SetSaveStatus(
                _settings.HasChanges ? "未保存の変更があります。" : "変更はありません。",
                false);
        }

        private void ReloadButton_Click(object sender, EventArgs e)
        {
            SyncSettingsFromControls();

            // 入力を保持して読み直すとき、Reload() が作り直す前の設定を控えておく。
            // SyncSettingsFromControls で画面の値は既に写してあるので、参照を持つだけでよい。
            AgentSettings keptHard = null;
            AgentSettings keptStandard = null;
            AgentSettings keptLight = null;
            bool keptCodexEnabled = false;
            bool keptCodexEnabledExplicit = false;
            bool preserveInput = false;
            if (_settings.HasChanges)
            {
                string message = "未保存の変更があります。定義ファイルを読み直しますか。"
                    + Environment.NewLine
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, _settings.DescribeChanges())
                    + Environment.NewLine
                    + Environment.NewLine
                    + "「はい」: 入力中の値を保持したまま定義ファイルを読み直します。"
                    + Environment.NewLine
                    + "「いいえ」: 入力を捨てて読み直します。"
                    + Environment.NewLine
                    + "「キャンセル」: 何もしません。";

                // 既定はキャンセルにする。Enter の連打で入力が消えないようにするためである。
                DialogResult result = MessageBox.Show(
                    this,
                    message,
                    "再読込の確認",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button3);
                if (result == DialogResult.Cancel)
                {
                    return;
                }

                preserveInput = result == DialogResult.Yes;
                if (preserveInput)
                {
                    keptHard = _settings.ImplHard;
                    keptStandard = _settings.ImplStandard;
                    keptLight = _settings.ImplLight;
                    keptCodexEnabled = _settings.CodexEnabled;
                    keptCodexEnabledExplicit = _settings.CodexEnabledExplicit;
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
                SetSaveStatus("再読込に失敗しました。表示は読み込み前のままです。", true);
                return;
            }

            if (preserveInput)
            {
                // ファイル側の基準値だけを更新し、画面の値は控えから戻す。
                // 次の保存で利用者の値がそのまま書かれ、入力し直しが要らない。
                _settings.ImplHard.CopyValuesFrom(keptHard);
                _settings.ImplStandard.CopyValuesFrom(keptStandard);
                _settings.ImplLight.CopyValuesFrom(keptLight);
                _settings.CodexEnabled = keptCodexEnabled;
                _settings.CodexEnabledExplicit = keptCodexEnabledExplicit;
            }

            LoadControlsFromSettings();
            SetSaveStatus(
                preserveInput
                    ? "再読込しました。入力中の値は保持しています。"
                    : "再読込しました。未保存の変更は破棄されています。",
                false);
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
                string message = "読み込み後に定義ファイルが外部で変更されたため保存できません。再読込で「はい」(入力を保持)を選んでから、もう一度保存してください。";
                if (_settings.LastChangedFiles.Count > 0)
                {
                    message += Environment.NewLine
                        + Environment.NewLine
                        + "中断までに保存されたファイル: " + savedFiles;
                    SetSaveStatus(
                        "保存を中断しました。中断までに保存されたファイル: " + savedFiles,
                        true);
                }
                else
                {
                    SetSaveStatus("保存を中断しました。書き換えられたファイルはありません。", true);
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
                SetSaveStatus(
                    _settings.LastChangedFiles.Count > 0
                        ? "保存に失敗しました。中断までに保存されたファイル: "
                            + string.Join(", ", _settings.LastChangedFiles)
                        : "保存に失敗しました。書き換えられたファイルはありません。",
                    true);

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
                SetSaveStatus("入力に不備があるため保存していません。", true);
                UpdateControlState();
                return false;
            }

            if (result.ChangedFiles.Count == 0)
            {
                SetSaveStatus("保存しました。変更はありません。", false);
            }
            else
            {
                SetSaveStatus(
                    "保存しました。書き換えたファイル: "
                        + string.Join(", ", result.ChangedFiles),
                    false);
            }

            UpdateStatusDisplay();
            UpdateControlState();
            return true;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // 中身の希望する大きさは、ハンドルが作られて配置が済むまで確定しない。
            // 組み立ての途中で決めると、列幅が縮んだままの大きさになる。
            AdjustWindowSize();
            CenterToScreen();

            if (_codexVersionStarted)
            {
                return;
            }

            _codexVersionStarted = true;
            LoadCodexVersionAsync();
            LoadCodexModelCatalogAsync();
        }

        // codex debug models の取得は画面の表示を待たせない。
        // 取得できないときは _choices の既定値のままにする。
        private async void LoadCodexModelCatalogAsync()
        {
            // CODEX_HOME は必ず明示する。既定の ~/.codex への暗黙依存を作らないためである。
            string codexHome = _settings.ExpandedCodexHome;
            if (string.IsNullOrEmpty(codexHome))
            {
                // codexHome が空だと CodexModelCatalog.Load は必ず null を返すため、起動もしない。
                _codexCatalogText = "認証ホーム未設定のため取得しない。既定値を使用";
                if (!IsDisposed && !Disposing)
                {
                    UpdateStatusDisplay();
                }

                return;
            }

            CodexModelCatalog catalog = await Task.Run(() => CodexModelCatalog.Load(codexHome));
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (catalog == null)
            {
                _codexCatalogText = "取得できないため既定値を使用";
                UpdateStatusDisplay();
                return;
            }

            _codexCatalogText = "codex debug models から取得";
            _codexModelCatalog = catalog;
            ApplyCodexModelCatalog();
            UpdateStatusDisplay();
        }

        private void ApplyCodexModelCatalog()
        {
            bool loading = _loadingControls;
            _loadingControls = true;
            try
            {
                SetComboItems(
                    _standardGptModelComboBox,
                    _codexModelCatalog.Models,
                    _standardGptModelComboBox.Text);
                SetComboItems(
                    _lightGptModelComboBox,
                    _codexModelCatalog.Models,
                    _lightGptModelComboBox.Text);
                ApplyGptEffortChoices(_standardGptModelComboBox, _standardGptEffortComboBox);
                ApplyGptEffortChoices(_lightGptModelComboBox, _lightGptEffortComboBox);
            }
            finally
            {
                _loadingControls = loading;
            }

            ResizeGptComboBoxes();
            AdjustWindowSize();
        }

        // effort の選べる値はモデルごとに違う。目録が知らないモデルには既定の一覧を残す。
        private void ApplyGptEffortChoices(ComboBox modelComboBox, ComboBox effortComboBox)
        {
            IReadOnlyList<string> efforts = _codexModelCatalog.EffortsFor(modelComboBox.Text);
            if (efforts.Count == 0)
            {
                efforts = _choices.GptEfforts;
            }

            SetComboItems(effortComboBox, efforts, effortComboBox.Text);
        }

        private void ClaudeModelTextChanged(object sender, EventArgs e)
        {
            // 読み込み直後は値を変えない。開いただけで定義が書き換わるのを避けるためである。
            if (_loadingControls)
            {
                return;
            }

            var modelComboBox = (ComboBox)sender;
            ComboBox effortComboBox = ClaudeEffortComboBoxFor(modelComboBox);

            // 利用者がモデルを変えたときは、そのモデルが受け付けない effort を残さない。
            // 残すと、選べるように見えて Claude Code 側で別の値へ落とされる組み合わせを保存できてしまう。
            IReadOnlyList<string> efforts = _choices.ClaudeEffortsFor(modelComboBox.Text);
            effortComboBox.Text = Choices.NearestSupportedEffort(efforts, effortComboBox.Text);

            bool loading = _loadingControls;
            _loadingControls = true;
            try
            {
                SetComboItems(effortComboBox, efforts, effortComboBox.Text);
            }
            finally
            {
                _loadingControls = loading;
            }

            ResizeClaudeComboBoxes();
            AdjustWindowSize();
        }

        private ComboBox ClaudeEffortComboBoxFor(ComboBox modelComboBox)
        {
            if (modelComboBox == _hardModelComboBox)
            {
                return _hardEffortComboBox;
            }

            return modelComboBox == _standardModelComboBox
                ? _standardEffortComboBox
                : _lightEffortComboBox;
        }

        // 選択肢を差し替えると必要な幅が変わる。列は中身の希望する大きさで決まるため、幅を計算し直す。
        private void ResizeClaudeComboBoxes()
        {
            int modelWidth = ComboBoxWidth(
                _choices.ClaudeModels,
                _hardModelComboBox.Text,
                _standardModelComboBox.Text,
                _lightModelComboBox.Text);

            var efforts = new List<string>();
            CollectItems(efforts, _hardEffortComboBox);
            CollectItems(efforts, _standardEffortComboBox);
            CollectItems(efforts, _lightEffortComboBox);
            int effortWidth = ComboBoxWidth(
                efforts,
                _hardEffortComboBox.Text,
                _standardEffortComboBox.Text,
                _lightEffortComboBox.Text);

            SetComboBoxWidth(_hardModelComboBox, modelWidth);
            SetComboBoxWidth(_standardModelComboBox, modelWidth);
            SetComboBoxWidth(_lightModelComboBox, modelWidth);
            SetComboBoxWidth(_hardEffortComboBox, effortWidth);
            SetComboBoxWidth(_standardEffortComboBox, effortWidth);
            SetComboBoxWidth(_lightEffortComboBox, effortWidth);
        }

        private void GptModelTextChanged(object sender, EventArgs e)
        {
            // 読み込みの途中でモデル欄に値が入ると、まだ読み直していない effort を見て
            // 既定値へ寄せてしまう。Claude 側の同じハンドラと扱いを揃える。
            if (_loadingControls || _codexModelCatalog == null)
            {
                return;
            }

            var modelComboBox = (ComboBox)sender;
            ComboBox effortComboBox = modelComboBox == _standardGptModelComboBox
                ? _standardGptEffortComboBox
                : _lightGptEffortComboBox;

            // 利用者がモデルを変えたときは、そのモデルが受け付けない effort を残さない。
            // 残すと、選べるように見えて Codex 側で弾かれる組み合わせを保存できてしまう。
            // 読み込み直後は値を変えない。開いただけで定義が書き換わるのを避けるためである。
            IReadOnlyList<string> efforts = _codexModelCatalog.EffortsFor(modelComboBox.Text);
            string effort = effortComboBox.Text;
            if (efforts.Count > 0 && !Contains(efforts, effort))
            {
                string defaultEffort = _codexModelCatalog.DefaultEffortFor(modelComboBox.Text);
                effortComboBox.Text = string.IsNullOrEmpty(defaultEffort) ? efforts[0] : defaultEffort;
            }

            bool loading = _loadingControls;
            _loadingControls = true;
            try
            {
                ApplyGptEffortChoices(modelComboBox, effortComboBox);
            }
            finally
            {
                _loadingControls = loading;
            }

            ResizeGptComboBoxes();
            AdjustWindowSize();
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // 選択肢を差し替えると必要な幅が変わる。列は中身の希望する大きさで決まるため、幅を計算し直す。
        private void ResizeGptComboBoxes()
        {
            IReadOnlyList<string> models = _codexModelCatalog != null
                ? _codexModelCatalog.Models
                : _choices.GptModels;
            int modelWidth = ComboBoxWidth(
                models,
                _standardGptModelComboBox.Text,
                _lightGptModelComboBox.Text);

            var efforts = new List<string>();
            CollectItems(efforts, _standardGptEffortComboBox);
            CollectItems(efforts, _lightGptEffortComboBox);
            int effortWidth = ComboBoxWidth(
                efforts,
                _standardGptEffortComboBox.Text,
                _lightGptEffortComboBox.Text);

            SetComboBoxWidth(_standardGptModelComboBox, modelWidth);
            SetComboBoxWidth(_lightGptModelComboBox, modelWidth);
            SetComboBoxWidth(_standardGptEffortComboBox, effortWidth);
            SetComboBoxWidth(_lightGptEffortComboBox, effortWidth);
        }

        private static void CollectItems(List<string> values, ComboBox comboBox)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                values.Add(Convert.ToString(comboBox.Items[i]));
            }
        }

        private static void SetComboBoxWidth(ComboBox comboBox, int width)
        {
            // 列の幅は中身の希望する大きさから決まる。最小の幅も同時に更新しないと、
            // 一覧の値が入らない細さまで縮む。
            comboBox.MinimumSize = new Size(width, 0);
            comboBox.Width = width;
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
                "未保存の変更があります。保存して閉じますか。"
                    + Environment.NewLine
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, _settings.DescribeChanges()),
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
