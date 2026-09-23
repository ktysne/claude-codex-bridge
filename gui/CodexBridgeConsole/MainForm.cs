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

        // 目録由来の GPT モデルが増えても、一覧が画面の高さを超えない上限。
        private const int MaxVisibleDropDownItems = 20;

        // GPT モデルが未設定であることを表す選択肢の表示名。値としては空文字を意味する。
        // モデル名に使える文字は英数字と . _ - / だけである(ConsoleSettings が保存時に検証する)ため、
        // 括弧を含むこの表示名が実在のモデル名と衝突することはない。この不変条件があるので、
        // 表示名と値の変換を文字列の一致だけで行える。
        private const string UnsetGptModelText = "(未設定)";

        private const string UnsetValueText = "(未設定)";

        private const string CheckingVersionText = "確認中...";

        private const string FetchingCatalogText = "取得中...";

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

        private static readonly Color WarningForeColor = Color.Firebrick;

        private static string _baseFontFamily;

        private static readonly IReadOnlyList<string> EmptyList = new string[0];

        private readonly ConsoleSettings _settings;
        private readonly Choices _choices;
        private ComboBox _codexHomeComboBox;
        private Label _codexSandboxLabel;
        private Label _codexVersionLabel;
        private Label _codexCatalogLabel;
        private CheckBox _codexEnabledCheckBox;
        private ComboBox _hardModelComboBox;
        private ComboBox _hardEffortComboBox;
        private GptRow _hardGptRow;
        private ComboBox _standardModelComboBox;
        private ComboBox _standardEffortComboBox;
        private GptRow _standardGptRow;
        private ComboBox _lightModelComboBox;
        private ComboBox _lightEffortComboBox;
        private GptRow _lightGptRow;
        private GptRow[] _subagentGptRows;
        private CodexAgentRow[] _codexAgentRows;
        private GptRow[] _reviewGptRows;
        private Label _reviewVersionLabel;
        private Label _reviewCatalogLabel;
        private Label _reviewMissingFilesLabel;
        private Button _reloadButton;
        private Button _saveButton;
        private Button _closeButton;
        private Label _missingFilesLabel;
        private Label _noticeLabel;
        private Label _saveStatusLabel;
        private Color _saveStatusDefaultForeColor;
        private bool _loadingControls;
        private bool _codexVersionStarted;

        // 認証ホームの選択欄に並べた項目の値。表示は注記を添えることがあるため、値を別に持つ。
        private readonly List<string> _codexHomeValues = new List<string>();

        // 認証ホームごとの取得。取得中のものも入れておき、両タブが同じホームを求めても codex を 1 回だけ起動する。
        // 切り替えて戻したときや再読込のときも、同じ問い合わせを繰り返さない。
        private readonly Dictionary<string, Task<string>> _codexVersionByHome =
            new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Task<CatalogResult>> _codexCatalogByHome =
            new Dictionary<string, Task<CatalogResult>>(StringComparer.OrdinalIgnoreCase);

        // 取得を始めたときのホーム。結果を画面へ反映してよいのは、それがまだ選ばれているときだけである。
        private string _currentCodexHomeKey = string.Empty;

        private string _codexVersionText = CheckingVersionText;

        // codex debug models の取得結果に応じた文言。GPT モデル一覧が目録由来か既定値かを利用者に示す。
        private string _codexCatalogText = FetchingCatalogText;

        private Control _layout;
        private Control _targetPanel;
        private TabControl _tabControl;
        private Control _subagentContent;
        private Control _reviewContent;
        private Control _buttonPanel;
        private TableLayoutPanel _definitionsTable;
        private TableLayoutPanel _reviewTable;

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
            SetSaveStatus(IdleStatusText(), false);

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

            // 横幅の基準は 2 つのタブの表のうち広いほうとする。画面の中で最も広い中身であるためである。
            _definitionsTable = BuildDefinitionsTable();
            _reviewTable = BuildReviewTable();
            int contentWidth = Math.Max(_definitionsTable.PreferredSize.Width, _reviewTable.PreferredSize.Width);

            _targetPanel = BuildTargetPanel();
            layout.Controls.Add(_targetPanel);

            _subagentContent = BuildSubagentContent(contentWidth);
            _reviewContent = BuildReviewContent(contentWidth);

            // 大きさは AdjustWindowSize が決める。
            _tabControl = new TabControl
            {
                Margin = new Padding(0, 6, 0, 6)
            };
            _tabControl.TabPages.Add(CreateTabPage("サブエージェント", _subagentContent));
            _tabControl.TabPages.Add(CreateTabPage("レビューと実装補助", _reviewContent));
            _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
            layout.Controls.Add(_tabControl);

            // 幅を内容の幅に留めて折り返す。制限しないと、この文言の幅でダイアログの幅が決まり、画面からはみ出す。
            _noticeLabel = new Label
            {
                Text = "保存した値は次の委譲から効きます(GPT 側は次の Codex 呼び出しから、Claude 側は数秒後から)。再起動が要る条件は docs/setup.md の共通手順 6 を参照。",
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 3)
            };
            layout.Controls.Add(_noticeLabel);

            _saveStatusLabel = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Height = SingleLineHeight(),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 3)
            };
            _saveStatusDefaultForeColor = _saveStatusLabel.ForeColor;
            layout.Controls.Add(_saveStatusLabel);

            _buttonPanel = BuildButtonPanel();
            layout.Controls.Add(_buttonPanel);
            SetSharedWidth(contentWidth);

            _layout = layout;
            Controls.Add(layout);
        }

        private static TabPage CreateTabPage(string text, Control content)
        {
            var page = new TabPage(text)
            {
                Padding = new Padding(0),
                UseVisualStyleBackColor = true
            };

            // 中身はページに合わせて伸縮させず、自分の希望する大きさで置く。
            // タブの大きさは 2 つの中身の希望する大きさから決めるためである。
            content.Location = Point.Empty;
            page.Controls.Add(content);
            return page;
        }

        private static FlowLayoutPanel CreateTabContent()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(6)
            };
        }

        private Control BuildSubagentContent(int width)
        {
            FlowLayoutPanel content = CreateTabContent();

            _codexEnabledCheckBox = new CheckBox
            {
                Text = "GPT 系サブエージェント経路を有効にする (impl-hard / impl-standard / impl-light)",
                AutoSize = true,
                Margin = new Padding(3, 6, 3, 6)
            };
            _codexEnabledCheckBox.CheckedChanged += CodexEnabledCheckBox_CheckedChanged;
            content.Controls.Add(_codexEnabledCheckBox);

            content.Controls.Add(_definitionsTable);

            _codexVersionLabel = CreateStatusLabel(width);
            _codexCatalogLabel = CreateStatusLabel(width);
            content.Controls.Add(BuildStatusPanel(width, BuildCodexHomePanel(), _codexVersionLabel, _codexCatalogLabel));

            _missingFilesLabel = CreateWarningLabel(width);
            content.Controls.Add(_missingFilesLabel);
            return content;
        }

        private Control BuildReviewContent(int width)
        {
            FlowLayoutPanel content = CreateTabContent();
            content.Controls.Add(_reviewTable);

            _reviewVersionLabel = CreateWrappingStatusLabel(width);
            _reviewCatalogLabel = CreateWrappingStatusLabel(width);
            content.Controls.Add(BuildStatusPanel(width, _reviewVersionLabel, _reviewCatalogLabel));

            _reviewMissingFilesLabel = CreateWarningLabel(width);
            content.Controls.Add(_reviewMissingFilesLabel);
            return content;
        }

        // 警告が無いときは場所を取らない。空の行が余白として残ると読みにくい。
        // 高さは行数で固定するため、収まらない文字列は末尾を省略記号にする。
        // 中断までに保存されたファイルの一覧など、長い文言が切れて読めなくなるのを防ぐ。
        private Label CreateWarningLabel(int width)
        {
            return new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Visible = false,
                Width = width,
                Height = SingleLineHeight() * 2,
                ForeColor = WarningForeColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 3, 3, 3)
            };
        }

        private Control BuildTargetPanel()
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

        private static TableLayoutPanel CreateDefinitionTable(int columnCount, int rowCount)
        {
            var table = new TableLayoutPanel
            {
                ColumnCount = columnCount,
                RowCount = rowCount,

                // 縦に積む入れ物の中では、自分の大きさを自分で決める必要がある。
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                Margin = new Padding(3),
                Padding = new Padding(3)
            };
            // 列幅と行の高さは中身に決めさせる。見出しの文字が最も長いことが多く、
            // 画素で決めると書体を変えたときに切れる。
            for (int i = 0; i < columnCount; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            }

            for (int i = 0; i < rowCount; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            return table;
        }

        private TableLayoutPanel BuildDefinitionsTable()
        {
            TableLayoutPanel table = CreateDefinitionTable(5, 4);

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
                UnsetGptModelText,
                _settings.ImplHard.CodexModel,
                _settings.ImplStandard.CodexModel,
                _settings.ImplLight.CodexModel);
            int gptEffortWidth = ComboBoxWidth(
                _choices.GptEfforts,
                _settings.ImplHard.CodexReasoningEffort,
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
            _hardGptRow = CreateGptRow(gptModelWidth, gptEffortWidth, true);
            _hardModelComboBox.TextChanged += ClaudeModelTextChanged;
            table.Controls.Add(_hardModelComboBox, 1, 1);
            table.Controls.Add(_hardEffortComboBox, 2, 1);
            table.Controls.Add(_hardGptRow.ModelComboBox, 3, 1);
            table.Controls.Add(_hardGptRow.EffortComboBox, 4, 1);

            table.Controls.Add(CreateRowLabel("standard"), 0, 2);
            _standardModelComboBox = CreateComboBox(claudeModelWidth);
            _standardEffortComboBox = CreateComboBox(claudeEffortWidth);
            _standardGptRow = CreateGptRow(gptModelWidth, gptEffortWidth, true);
            _standardModelComboBox.TextChanged += ClaudeModelTextChanged;
            table.Controls.Add(_standardModelComboBox, 1, 2);
            table.Controls.Add(_standardEffortComboBox, 2, 2);
            table.Controls.Add(_standardGptRow.ModelComboBox, 3, 2);
            table.Controls.Add(_standardGptRow.EffortComboBox, 4, 2);

            table.Controls.Add(CreateRowLabel("light"), 0, 3);
            _lightModelComboBox = CreateComboBox(claudeModelWidth);
            _lightEffortComboBox = CreateComboBox(claudeEffortWidth);
            _lightGptRow = CreateGptRow(gptModelWidth, gptEffortWidth, true);
            _lightModelComboBox.TextChanged += ClaudeModelTextChanged;
            table.Controls.Add(_lightModelComboBox, 1, 3);
            table.Controls.Add(_lightEffortComboBox, 2, 3);
            table.Controls.Add(_lightGptRow.ModelComboBox, 3, 3);
            table.Controls.Add(_lightGptRow.EffortComboBox, 4, 3);

            _subagentGptRows = new[] { _hardGptRow, _standardGptRow, _lightGptRow };
            return table;
        }

        // codex_home と codex_sandbox は表示だけにする。書き換えない理由は
        // docs/gui-console-design.md「レビューと実装補助タブで書き換えない項目」にある。
        private TableLayoutPanel BuildReviewTable()
        {
            TableLayoutPanel table = CreateDefinitionTable(5, 3);

            int gptModelWidth = ComboBoxWidth(
                _choices.GptModels,
                _settings.CodexReview.CodexModel,
                _settings.CodexSubagent.CodexModel);
            int gptEffortWidth = ComboBoxWidth(
                _choices.GptEfforts,
                _settings.CodexReview.CodexReasoningEffort,
                _settings.CodexSubagent.CodexReasoningEffort);

            table.Controls.Add(CreateHeaderLabel("定義"), 0, 0);
            table.Controls.Add(CreateHeaderLabel("GPT モデル"), 1, 0);
            table.Controls.Add(CreateHeaderLabel("effort"), 2, 0);
            table.Controls.Add(CreateHeaderLabel("codex_home"), 3, 0);
            table.Controls.Add(CreateHeaderLabel("codex_sandbox"), 4, 0);

            _codexAgentRows = new[]
            {
                AddCodexAgentRow(table, 1, "codex-review", () => _settings.CodexReview, gptModelWidth, gptEffortWidth),
                AddCodexAgentRow(table, 2, "codex-subagent", () => _settings.CodexSubagent, gptModelWidth, gptEffortWidth)
            };

            _reviewGptRows = new GptRow[_codexAgentRows.Length];
            for (int i = 0; i < _codexAgentRows.Length; i++)
            {
                _reviewGptRows[i] = _codexAgentRows[i].Gpt;
            }

            return table;
        }

        private CodexAgentRow AddCodexAgentRow(
            TableLayoutPanel table,
            int rowIndex,
            string definitionName,
            Func<CodexAgentSettings> settings,
            int gptModelWidth,
            int gptEffortWidth)
        {
            // 「(未設定)」は加えない。この定義は codex_model が空だとフォールバックせずに失敗するためである。
            GptRow gpt = CreateGptRow(gptModelWidth, gptEffortWidth, false);

            // 表の幅を組み立ての時点で測るため、値は先に入れておく。
            Label homeLabel = CreateValueLabel(FormatDefinitionValue(settings().CodexHome));
            Label sandboxLabel = CreateValueLabel(FormatDefinitionValue(settings().CodexSandbox));

            table.Controls.Add(CreateRowLabel(definitionName), 0, rowIndex);
            table.Controls.Add(gpt.ModelComboBox, 1, rowIndex);
            table.Controls.Add(gpt.EffortComboBox, 2, rowIndex);
            table.Controls.Add(homeLabel, 3, rowIndex);
            table.Controls.Add(sandboxLabel, 4, rowIndex);
            return new CodexAgentRow(settings, gpt, homeLabel, sandboxLabel);
        }

        private GptRow CreateGptRow(int modelWidth, int effortWidth, bool allowsUnset)
        {
            var row = new GptRow(CreateComboBox(modelWidth), CreateComboBox(effortWidth), allowsUnset);
            row.ModelComboBox.TextChanged += GptModelTextChanged;
            return row;
        }

        private Control BuildStatusPanel(int width, params Control[] rows)
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = rows.Length,

                // 認証ホームの行はコンボボックスを持つため、1 行分の高さに収まらない。
                // 高さは中身に決めさせる。
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(width, 0),
                Margin = new Padding(3, 6, 3, 6),
                Padding = new Padding(0)
            };
            for (int i = 0; i < rows.Length; i++)
            {
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                panel.Controls.Add(rows[i], 0, i);
            }

            return panel;
        }

        private Label CreateStatusLabel(int width)
        {
            return new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Width = width,
                Height = SingleLineHeight(),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 0, 3, 0)
            };
        }

        // 2 つのホームの結果を並べる行は長くなりやすいため、省略せずに折り返す。
        // 1 行で収まるときは他の状態行と同じ高さにする。
        private Label CreateWrappingStatusLabel(int width)
        {
            return new Label
            {
                AutoSize = true,
                MinimumSize = new Size(width, SingleLineHeight()),
                MaximumSize = new Size(width, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 0, 3, 0)
            };
        }

        // 認証ホームは選んで切り替える。codex_sandbox は表示だけにし、GUI から緩められる経路を作らない。
        private Control BuildCodexHomePanel()
        {
            var panel = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            for (int i = 0; i < 3; i++)
            {
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            }

            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _codexHomeComboBox = new ComboBox
            {
                // 任意のパスは入力させない。実在する認証ホームだけを選ばせるためである。
                DropDownStyle = ComboBoxStyle.DropDownList,
                Anchor = AnchorStyles.Left,
                IntegralHeight = false,

                // 行の高さを実際の高さから決めさせる。表形式の入れ物は、AutoSize でない子については
                // コードから指定した大きさで行の高さを決める。選択専用(DropDownList)のコンボボックスは
                // ハンドル作成後にネイティブ側で書体に合わせて高さが変わり、それは指定した大きさに
                // 反映されないため、画面の拡大率によっては行が低いまま残り、下端が次の行に隠れる。
                // 幅は MinimumSize で保つ(SetComboBoxWidth を参照)。
                AutoSize = true,
                Margin = new Padding(3, 3, 12, 3)
            };
            SetComboBoxWidth(_codexHomeComboBox, CodexHomeComboBoxWidth());
            _codexHomeComboBox.SelectedIndexChanged += CodexHomeComboBox_SelectedIndexChanged;

            _codexSandboxLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.None,
                Margin = new Padding(3)
            };

            panel.Controls.Add(CreateCenteredLabel("codex_home:"), 0, 0);
            panel.Controls.Add(_codexHomeComboBox, 1, 0);
            panel.Controls.Add(_codexSandboxLabel, 2, 0);
            return panel;
        }

        // 一覧の値と、注記を添えた現在値のどちらも収まる幅にする。
        private int CodexHomeComboBoxWidth()
        {
            var values = new List<string>(_settings.CodexHomeChoices);
            values.Add(FormatUnlistedCodexHome(_settings.CodexHome));
            return ComboBoxWidth(values);
        }

        // 一覧に無い値は、なぜ選択肢として並ばないのかが分かるよう注記を添える。
        private string FormatUnlistedCodexHome(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "(未設定)";
            }

            return _settings.CodexHomeExists
                ? value + "  (一覧に無い)"
                : value + "  (存在しない)";
        }

        private static string FormatDefinitionValue(string value)
        {
            return string.IsNullOrEmpty(value) ? UnsetValueText : value;
        }

        private Control BuildButtonPanel()
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

        // タブの外の行はタブと同じ幅にし、どちらのタブを開いていても同じ位置に見せる。
        private void SetSharedWidth(int width)
        {
            _targetPanel.MinimumSize = new Size(width - _targetPanel.Margin.Horizontal, 0);
            _noticeLabel.MaximumSize = new Size(width - _noticeLabel.Margin.Horizontal, 0);
            _saveStatusLabel.Width = width - _saveStatusLabel.Margin.Horizontal;
            _buttonPanel.MinimumSize = new Size(width - _buttonPanel.Margin.Horizontal, 0);
        }

        // タブの大きさは 2 つの中身の大きいほうで決め、どちらのタブを開いていても変えない。
        // 画面は全体の中身に合わせる。警告の行が出入りすると必要な大きさが変わる。
        private void AdjustWindowSize()
        {
            if (!IsHandleCreated || _layout == null)
            {
                return;
            }

            Size subagent = _subagentContent.PreferredSize;
            Size review = _reviewContent.PreferredSize;
            Rectangle display = _tabControl.DisplayRectangle;
            var tabSize = new Size(
                Math.Max(subagent.Width, review.Width) + _tabControl.Width - display.Width,
                Math.Max(subagent.Height, review.Height) + _tabControl.Height - display.Height);
            if (_tabControl.Size != tabSize)
            {
                _tabControl.Size = tabSize;
            }

            SetSharedWidth(tabSize.Width);

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
            var comboBox = new SelectionPreservingComboBox
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

        private static Label CreateValueLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft,
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
                LoadGptRow(_hardGptRow, _settings.ImplHard.CodexModel, _settings.ImplHard.CodexReasoningEffort);
                SetComboItems(_standardModelComboBox, _choices.ClaudeModels, _settings.ImplStandard.ClaudeModel);
                SetComboItems(
                    _standardEffortComboBox,
                    _choices.ClaudeEffortsFor(_settings.ImplStandard.ClaudeModel),
                    _settings.ImplStandard.ClaudeEffort);
                LoadGptRow(
                    _standardGptRow,
                    _settings.ImplStandard.CodexModel,
                    _settings.ImplStandard.CodexReasoningEffort);
                SetComboItems(_lightModelComboBox, _choices.ClaudeModels, _settings.ImplLight.ClaudeModel);
                SetComboItems(
                    _lightEffortComboBox,
                    _choices.ClaudeEffortsFor(_settings.ImplLight.ClaudeModel),
                    _settings.ImplLight.ClaudeEffort);
                LoadGptRow(_lightGptRow, _settings.ImplLight.CodexModel, _settings.ImplLight.CodexReasoningEffort);
                _codexEnabledCheckBox.Checked = _settings.CodexEnabled;
                LoadCodexHomeItems();

                for (int i = 0; i < _codexAgentRows.Length; i++)
                {
                    CodexAgentRow row = _codexAgentRows[i];
                    CodexAgentSettings agent = row.Settings;
                    row.SetHomeKey(_settings.ExpandCodexHome(agent.CodexHome) ?? string.Empty);
                    LoadGptRow(row.Gpt, agent.CodexModel, agent.CodexReasoningEffort);
                }
            }
            finally
            {
                _loadingControls = false;
            }

            // 選択肢を組み立て直したので、取得済みの目録を当て直す。
            // 当てないと、再読込のたびに GPT 側が既定の選択肢へ戻る。
            ApplyGptChoices(AllGptRows());

            UpdateStatusDisplay();
            UpdateControlState();

            // 読み直しで認証ホームが変わっていることがある。表示中の取得結果を持ち越さない。
            RefreshCodexHomeInfo();
            RefreshReviewCodexHomeInfo();
        }

        private void LoadGptRow(GptRow row, string model, string effort)
        {
            SetGptModelItems(row, model);
            SetComboItems(row.EffortComboBox, _choices.GptEfforts, effort);
        }

        private void LoadCodexHomeItems()
        {
            _codexHomeValues.Clear();
            _codexHomeComboBox.Items.Clear();
            string current = _settings.CodexHome;

            // 一覧に無い現在値は先頭に足して選択状態にする。開いただけで別のホームへ切り替わらないためである。
            if (!_settings.CodexHomeIsListed)
            {
                _codexHomeValues.Add(current);
                _codexHomeComboBox.Items.Add(FormatUnlistedCodexHome(current));
            }

            IReadOnlyList<string> choices = _settings.CodexHomeChoices;
            for (int i = 0; i < choices.Count; i++)
            {
                _codexHomeValues.Add(choices[i]);
                _codexHomeComboBox.Items.Add(choices[i]);
            }

            _codexHomeComboBox.SelectedIndex = _codexHomeValues.IndexOf(current);
            SetComboBoxWidth(_codexHomeComboBox, CodexHomeComboBoxWidth());
        }

        private string SelectedCodexHome()
        {
            int index = _codexHomeComboBox.SelectedIndex;
            return index >= 0 && index < _codexHomeValues.Count
                ? _codexHomeValues[index]
                : _settings.CodexHome;
        }

        private static void SetComboItems(
            ComboBox comboBox,
            System.Collections.Generic.IReadOnlyList<string> choices,
            string currentValue)
        {
            SetComboItems(comboBox, choices, currentValue, 0);
        }

        // 一覧に無い現在値は unlistedIndex の位置に足す。開いただけで別の値へ切り替わらないためである。
        private static void SetComboItems(
            ComboBox comboBox,
            System.Collections.Generic.IReadOnlyList<string> choices,
            string currentValue,
            int unlistedIndex)
        {
            comboBox.Items.Clear();
            for (int i = 0; i < choices.Count; i++)
            {
                comboBox.Items.Add(choices[i]);
            }

            if (!string.IsNullOrEmpty(currentValue) && comboBox.Items.IndexOf(currentValue) < 0)
            {
                comboBox.Items.Insert(Math.Min(unlistedIndex, comboBox.Items.Count), currentValue);
            }

            // 一覧がスクロールすると、選択中の値より上の候補が隠れて選択肢に無いように見えるため、全件を並べる。
            comboBox.MaxDropDownItems = Math.Max(1, Math.Min(comboBox.Items.Count, MaxVisibleDropDownItems));
            comboBox.Text = currentValue ?? string.Empty;
        }

        // サブエージェントタブの GPT モデルは先頭を「(未設定)」で固定する。目録に切り替わっても、
        // choices.json の既定値を使っても、GPT 側を使わない設定を選べる状態を保つためである。
        private void SetGptModelItems(GptRow row, string currentValue)
        {
            IReadOnlyList<string> models = GptModelsFor(row);
            if (!row.AllowsUnset)
            {
                SetComboItems(row.ModelComboBox, models, currentValue);
                return;
            }

            var items = new List<string> { UnsetGptModelText };
            for (int i = 0; i < models.Count; i++)
            {
                if (!string.Equals(models[i], UnsetGptModelText, StringComparison.Ordinal))
                {
                    items.Add(models[i]);
                }
            }

            // 一覧に無い現在値は「(未設定)」の次に置き、先頭の固定枠を押し出さない。
            SetComboItems(row.ModelComboBox, items, ToGptModelText(currentValue), 1);
        }

        private IReadOnlyList<string> GptModelsFor(GptRow row)
        {
            return row.Catalog != null ? row.Catalog.Models : _choices.GptModels;
        }

        // 定義ファイルの値から選択欄の表示名へ直す。空値と未設定は「(未設定)」である。
        private static string ToGptModelText(string value)
        {
            return string.IsNullOrEmpty(value) ? UnsetGptModelText : value;
        }

        // 選択欄の表示名から定義ファイルへ書く値へ直す。「(未設定)」は空文字である。
        // 利用者が同じ文字列を手で入力した場合も未設定として扱う。
        private static string ToGptModelValue(string text)
        {
            return string.Equals(text, UnsetGptModelText, StringComparison.Ordinal)
                ? string.Empty
                : text;
        }

        private static string GptModelValue(GptRow row)
        {
            return row.AllowsUnset
                ? ToGptModelValue(row.ModelComboBox.Text)
                : row.ModelComboBox.Text;
        }

        // GPT モデルが選ばれている行かどうか。未設定の行は GPT 側を呼ばない。
        private static bool IsGptModelSet(GptRow row)
        {
            return GptModelValue(row).Length > 0;
        }

        private void UpdateStatusDisplay()
        {
            UpdateSubagentStatusDisplay();
            UpdateReviewStatusDisplay();
            AdjustWindowSize();
        }

        private void UpdateSubagentStatusDisplay()
        {
            // 認証ホームは選択欄が値と注記を示すため、ここでは sandbox だけを出す。
            _codexSandboxLabel.Text = "codex_sandbox: " + (_settings.CodexSandbox ?? "(未設定)");
            // バージョンの取得は認証ホームごとに 1 回だけなので、再読込では取得済みの結果を出し直す。
            _codexVersionLabel.Text = "codex --version: " + _codexVersionText;
            // 目録の取得も起動時の 1 回だけなので、再読込では取得済みの結果を出し直す。
            _codexCatalogLabel.Text = "GPT モデル一覧: " + _codexCatalogText;

            string unavailableText = FormatUnavailableFiles(_settings.MissingFiles, _settings.UnreadableFiles);
            if (unavailableText.Length > 0)
            {
                _missingFilesLabel.Text = unavailableText;
            }
            else if (_settings.CodexEnabledInvalidFiles.Count > 0)
            {
                _missingFilesLabel.Text = "codex_enabled の値が不正である: "
                    + string.Join(", ", _settings.CodexEnabledInvalidFiles)
                    + "。無効として表示している。保存すると表示どおりの値へ直す。";
            }
            else if (_settings.CodexEnabledMismatch)
            {
                _missingFilesLabel.Text = "impl-hard、impl-standard、impl-light の codex_enabled が食い違っている。"
                    + "3 定義すべてが有効なときだけ有効として表示する。チェックを変えて保存すると 3 定義に同じ値を書く。";
            }
            else if (_settings.CodexHomeMismatch)
            {
                _missingFilesLabel.Text = "impl-hard、impl-standard、impl-light の codex_home が食い違っている。"
                    + "impl-light の値を選択中として表示する。"
                    + (_settings.NeedsCodexHomeAlignment
                        ? "保存すると 3 定義に選択中の値を書く。"
                        : "一覧にある認証ホームを選んで保存すると 3 定義が揃う。");
            }
            else
            {
                _missingFilesLabel.Text = string.Empty;
            }

            // 警告が無いときは行ごと隠す。空の行が余白として残ると読みにくい。
            _missingFilesLabel.Visible = _missingFilesLabel.Text.Length > 0;
        }

        private void UpdateReviewStatusDisplay()
        {
            for (int i = 0; i < _codexAgentRows.Length; i++)
            {
                CodexAgentRow row = _codexAgentRows[i];
                row.HomeLabel.Text = FormatDefinitionValue(row.Settings.CodexHome);
                row.SandboxLabel.Text = FormatDefinitionValue(row.Settings.CodexSandbox);
            }

            UpdateReviewCodexStatusLabels();

            _reviewMissingFilesLabel.Text = FormatUnavailableFiles(
                _settings.ReviewMissingFiles,
                _settings.ReviewUnreadableFiles);
            _reviewMissingFilesLabel.Visible = _reviewMissingFilesLabel.Text.Length > 0;
        }

        // 2 定義の認証ホームが同じなら 1 つにまとめる。取得も 1 回だけであり、同じ結果を 2 度並べない。
        // 目録の取得元が同じホームも 1 つにまとめる。状態行は 1 行固定で、長いと末尾が省略されるためである。
        private void UpdateReviewCodexStatusLabels()
        {
            var homes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var versions = new List<string>();
            var catalogTexts = new List<string>();
            var homesByCatalogText = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int i = 0; i < _codexAgentRows.Length; i++)
            {
                CodexAgentRow row = _codexAgentRows[i];
                if (!homes.Add(row.HomeKey))
                {
                    continue;
                }

                string home = FormatDefinitionValue(row.Settings.CodexHome);
                versions.Add(home + " " + row.VersionText);

                List<string> homesWithSameCatalog;
                if (!homesByCatalogText.TryGetValue(row.CatalogText, out homesWithSameCatalog))
                {
                    homesWithSameCatalog = new List<string>();
                    homesByCatalogText.Add(row.CatalogText, homesWithSameCatalog);
                    catalogTexts.Add(row.CatalogText);
                }

                homesWithSameCatalog.Add(home);
            }

            var catalogs = new List<string>();
            for (int i = 0; i < catalogTexts.Count; i++)
            {
                catalogs.Add(string.Join("、", homesByCatalogText[catalogTexts[i]]) + " は " + catalogTexts[i]);
            }

            _reviewVersionLabel.Text = "codex --version: " + string.Join(" / ", versions);
            _reviewCatalogLabel.Text = "GPT モデル一覧: " + string.Join(" / ", catalogs);

            // 折り返しで行数が変わると、タブの中身の高さが変わる。
            AdjustWindowSize();
        }

        // 対象ファイルが欠けたタブに出す警告。欠けていなければ空を返す。
        private static string FormatUnavailableFiles(
            IReadOnlyList<string> missingFiles,
            IReadOnlyList<string> unreadableFiles)
        {
            if (missingFiles.Count > 0)
            {
                // ラベルは 2 行固定で末尾が省略記号になる。ファイル一覧は長くなりやすく、
                // 後ろに置くと配置手順の案内ごと切れてしまうため、案内を一覧より前に置く。
                var text = new StringBuilder("このタブは保存できない。定義の配置は docs/setup.md の手順に従う。");
                text.Append("見つからない: ").Append(string.Join(", ", missingFiles)).Append('。');
                if (unreadableFiles.Count > 0)
                {
                    text.Append("読めない: ").Append(string.Join(" / ", unreadableFiles));
                }

                return text.ToString();
            }

            if (unreadableFiles.Count > 0)
            {
                // 読めないだけの場合、置き場所は分かっていて中身が壊れているだけなので配置手順は無関係である。
                return "このタブは保存できない。読めない: " + string.Join(" / ", unreadableFiles);
            }

            return string.Empty;
        }

        // 未保存の変更が無いときの状態行。修復待ちがあるときは、ボタンが押せる理由を示す。
        private string IdleStatusText()
        {
            var reasons = new List<string>();
            if (_settings.CodexEnabledInvalidFiles.Count > 0)
            {
                reasons.Add("codex_enabled の不正値を直します");
            }

            if (_settings.NeedsCodexHomeAlignment)
            {
                reasons.Add("codex_home を選択中の値で揃えます");
            }

            return reasons.Count == 0
                ? "変更はありません。"
                : "保存すると " + string.Join("、", reasons.ToArray()) + "。";
        }

        // 状態行の文字色は失敗のときだけ警告色にする。成功と未保存は通常色で区別しない。
        private void SetSaveStatus(string text, bool isError)
        {
            _saveStatusLabel.Text = text;
            _saveStatusLabel.ForeColor = isError
                ? WarningForeColor
                : _saveStatusDefaultForeColor;
        }

        private void UpdateControlState()
        {
            _saveButton.Enabled = _settings.CanSave && _settings.NeedsSave;
            UpdateSubagentControlState();
            UpdateReviewControlState();
        }

        // 対象ファイルが欠けたタブは入力を止める。そのタブは保存の対象から外れ、入力しても書き込まれない。
        private void UpdateSubagentControlState()
        {
            bool available = _settings.SubagentTabAvailable;
            _codexEnabledCheckBox.Enabled = available;
            _codexHomeComboBox.Enabled = available;
            _hardModelComboBox.Enabled = available;
            _hardEffortComboBox.Enabled = available;
            _standardModelComboBox.Enabled = available;
            _standardEffortComboBox.Enabled = available;
            _lightModelComboBox.Enabled = available;
            _lightEffortComboBox.Enabled = available;

            bool gptEnabled = available && _codexEnabledCheckBox.Checked;
            for (int i = 0; i < _subagentGptRows.Length; i++)
            {
                GptRow row = _subagentGptRows[i];
                row.ModelComboBox.Enabled = gptEnabled;

                // モデルが未設定の行は GPT 側を呼ばないため effort を使わない。値は保持したまま操作だけを止める。
                // codex_enabled のチェックを外したときと同じ扱いである。
                row.EffortComboBox.Enabled = gptEnabled && IsGptModelSet(row);
            }
        }

        private void UpdateReviewControlState()
        {
            bool available = _settings.ReviewTabAvailable;
            for (int i = 0; i < _codexAgentRows.Length; i++)
            {
                CodexAgentRow row = _codexAgentRows[i];
                row.Gpt.ModelComboBox.Enabled = available;
                row.Gpt.EffortComboBox.Enabled = available;
                row.HomeLabel.Enabled = available;
                row.SandboxLabel.Enabled = available;
            }
        }

        // 対象ファイルが欠けたタブの欄は書き戻さない。欠けた定義の値は null で読まれるため、欄の空文字を
        // 書き戻すと未保存の変更に数えられ、もう片方のタブの保存まで検証で止まる。
        private void SyncSettingsFromControls()
        {
            if (_settings.SubagentTabAvailable)
            {
                _settings.ImplHard.ClaudeModel = _hardModelComboBox.Text;
                _settings.ImplHard.ClaudeEffort = _hardEffortComboBox.Text;
                _settings.ImplHard.CodexModel = GptModelValue(_hardGptRow);
                _settings.ImplHard.CodexReasoningEffort = _hardGptRow.EffortComboBox.Text;
                _settings.ImplStandard.ClaudeModel = _standardModelComboBox.Text;
                _settings.ImplStandard.ClaudeEffort = _standardEffortComboBox.Text;
                _settings.ImplStandard.CodexModel = GptModelValue(_standardGptRow);
                _settings.ImplStandard.CodexReasoningEffort = _standardGptRow.EffortComboBox.Text;
                _settings.ImplLight.ClaudeModel = _lightModelComboBox.Text;
                _settings.ImplLight.ClaudeEffort = _lightEffortComboBox.Text;
                _settings.ImplLight.CodexModel = GptModelValue(_lightGptRow);
                _settings.ImplLight.CodexReasoningEffort = _lightGptRow.EffortComboBox.Text;
                _settings.CodexEnabled = _codexEnabledCheckBox.Checked;
                _settings.CodexHome = SelectedCodexHome();
            }

            if (_settings.ReviewTabAvailable)
            {
                for (int i = 0; i < _codexAgentRows.Length; i++)
                {
                    CodexAgentRow row = _codexAgentRows[i];
                    CodexAgentSettings agent = row.Settings;
                    agent.CodexModel = GptModelValue(row.Gpt);
                    agent.CodexReasoningEffort = row.Gpt.EffortComboBox.Text;
                }
            }
        }

        private void CodexHomeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingControls)
            {
                return;
            }

            ControlValueChanged(sender, e);

            // 認証ホームを変えると codex の応答も変わる。前のホームの結果を出したままにしない。
            RefreshCodexHomeInfo();
        }

        private void CodexEnabledCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (_loadingControls)
            {
                return;
            }

            // 3 定義の値が食い違っているときは、表示上の値が変わらなくてもすべてへ書き戻す必要がある。
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
            SetSaveStatus(_settings.HasChanges ? "未保存の変更があります。" : IdleStatusText(), false);
        }

        // 表示していなかったタブの中身は、部品のハンドルが作られる前の大きさで測っている。
        // 初めて表示したときに測り直し、食い違っていたときだけ大きさを直す。
        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            AdjustWindowSize();
        }

        private void ReloadButton_Click(object sender, EventArgs e)
        {
            SyncSettingsFromControls();
            bool preserveInput = false;
            if (_settings.HasChanges)
            {
                string message = "未保存の変更があります。定義ファイルを読み直しますか。"
                    + Environment.NewLine
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, _settings.DescribeChanges())
                    + Environment.NewLine
                    + Environment.NewLine
                    + "「はい」: 上の項目は入力中の値を残し、それ以外は定義ファイルの値に読み直します。"
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
            }

            IReadOnlyList<string> conflicts = EmptyList;
            try
            {
                if (preserveInput)
                {
                    conflicts = _settings.ReloadPreservingEdits();
                }
                else
                {
                    _settings.Reload();
                }
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

            LoadControlsFromSettings();
            SetSaveStatus(
                preserveInput
                    ? "再読込しました。編集した項目は入力中の値を保持しています。"
                    : "再読込しました。未保存の変更は破棄されています。",
                false);

            // 編集した項目が外部でも変わっていたときは、どちらを採ったかを見せる。
            // 黙って入力中の値を残すと、外部の変更に気づかないまま上書き保存してしまう。
            if (conflicts.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "編集した項目が定義ファイル側でも変わっていました。入力中の値を残しています。"
                        + Environment.NewLine
                        + Environment.NewLine
                        + string.Join(Environment.NewLine, conflicts),
                    "外部の変更と重なった項目",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
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
            RefreshCodexHomeInfo();
            RefreshReviewCodexHomeInfo();
        }

        // 選択中の認証ホームで codex --version と codex debug models を引き直す。
        // 一度引いたホームの結果は持っておき、切り替えて戻したときに引き直さない。
        private void RefreshCodexHomeInfo()
        {
            // 画面が出る前は codex を起動しない。起動の待ちで表示が遅れないようにするためである。
            if (!_codexVersionStarted)
            {
                return;
            }

            string codexHome = _settings.ExpandedCodexHome ?? string.Empty;
            _currentCodexHomeKey = codexHome;
            LoadCodexVersionAsync(codexHome);
            LoadCodexModelCatalogAsync(codexHome);
        }

        // レビューと実装補助タブの 2 定義それぞれの認証ホームで引く。控えはサブエージェントタブと共有する。
        private void RefreshReviewCodexHomeInfo()
        {
            if (!_codexVersionStarted)
            {
                return;
            }

            for (int i = 0; i < _codexAgentRows.Length; i++)
            {
                CodexAgentRow row = _codexAgentRows[i];
                LoadReviewCodexVersionAsync(row, row.HomeKey);
                LoadReviewCodexModelCatalogAsync(row, row.HomeKey);
            }
        }

        private Task<string> CodexVersionFor(string codexHome)
        {
            Task<string> task;
            if (!_codexVersionByHome.TryGetValue(codexHome, out task))
            {
                // CODEX_HOME は必ず明示する。既定の ~/.codex への暗黙依存を作らないためである。
                task = Task.Run(() => GetCodexVersion(codexHome));
                _codexVersionByHome[codexHome] = task;
            }

            return task;
        }

        // 取得できないときの結果も控えに入れる。入れないと、取れないホームへ切り替えるたびに codex を起動し直す。
        private Task<CatalogResult> CodexCatalogFor(string codexHome)
        {
            Task<CatalogResult> task;
            if (!_codexCatalogByHome.TryGetValue(codexHome, out task))
            {
                if (codexHome.Length == 0)
                {
                    // codexHome が空だと CodexModelCatalog.Load は必ず null を返すため、起動もしない。
                    task = Task.FromResult(new CatalogResult(
                        null,
                        "認証ホーム未設定のため取得しない。既定値を使用",
                        "既定値 (認証ホーム未設定)"));
                }
                else
                {
                    // CODEX_HOME は必ず明示する。既定の ~/.codex への暗黙依存を作らないためである。
                    task = Task.Run(() =>
                    {
                        CodexModelCatalog catalog = CodexModelCatalog.Load(codexHome);
                        return catalog == null
                            ? new CatalogResult(null, "取得できないため既定値を使用", "既定値 (取得できない)")
                            : new CatalogResult(catalog, "codex debug models から取得", "codex debug models から取得");
                    });
                }

                _codexCatalogByHome[codexHome] = task;
            }

            return task;
        }

        // codex debug models の取得は画面の表示を待たせない。
        // 取得できないときは _choices の既定値へ戻す。
        private async void LoadCodexModelCatalogAsync(string codexHome)
        {
            Task<CatalogResult> task = CodexCatalogFor(codexHome);
            if (!task.IsCompleted)
            {
                SetCodexCatalogText(FetchingCatalogText);
            }

            CatalogResult result = await task;
            if (IsDisposed || Disposing)
            {
                return;
            }

            // 取得の間に別のホームへ切り替わっていたら、そちらの表示を上書きしない。
            if (string.Equals(_currentCodexHomeKey, codexHome, StringComparison.OrdinalIgnoreCase))
            {
                ApplyCatalogResult(result);
            }
        }

        private void ApplyCatalogResult(CatalogResult result)
        {
            SetCodexCatalogText(result.Text);
            for (int i = 0; i < _subagentGptRows.Length; i++)
            {
                _subagentGptRows[i].Catalog = result.Catalog;
            }

            ApplyGptChoices(_subagentGptRows);
        }

        // 取得結果は保持し、再読込では UpdateStatusDisplay が同じ文言を出し直す。
        private void SetCodexCatalogText(string text)
        {
            _codexCatalogText = text;
            _codexCatalogLabel.Text = "GPT モデル一覧: " + text;
        }

        private async void LoadReviewCodexVersionAsync(CodexAgentRow row, string codexHome)
        {
            Task<string> task = CodexVersionFor(codexHome);
            if (!task.IsCompleted)
            {
                row.VersionText = CheckingVersionText;
                UpdateReviewCodexStatusLabels();
            }

            string version = await task;
            if (IsDisposed || Disposing)
            {
                return;
            }

            // 取得の間に再読込でその行の認証ホームが変わっていたら、新しいホームの表示を上書きしない。
            if (string.Equals(row.HomeKey, codexHome, StringComparison.OrdinalIgnoreCase))
            {
                row.VersionText = version;
                UpdateReviewCodexStatusLabels();
            }
        }

        private async void LoadReviewCodexModelCatalogAsync(CodexAgentRow row, string codexHome)
        {
            Task<CatalogResult> task = CodexCatalogFor(codexHome);
            if (!task.IsCompleted)
            {
                row.CatalogText = FetchingCatalogText;
                UpdateReviewCodexStatusLabels();
            }

            CatalogResult result = await task;
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (!string.Equals(row.HomeKey, codexHome, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            row.CatalogText = result.Summary;
            row.Gpt.Catalog = result.Catalog;
            UpdateReviewCodexStatusLabels();
            ApplyGptChoices(new[] { row.Gpt });
        }

        private IEnumerable<GptRow> AllGptRows()
        {
            for (int i = 0; i < _subagentGptRows.Length; i++)
            {
                yield return _subagentGptRows[i];
            }

            for (int i = 0; i < _reviewGptRows.Length; i++)
            {
                yield return _reviewGptRows[i];
            }
        }

        // GPT 側の選択肢を行の目録から組み直す。目録が無いときは _choices の既定値を使う。
        // 入力中の値は選択肢に無くても残す。開いただけで定義が書き換わるのを避けるためである。
        private void ApplyGptChoices(IEnumerable<GptRow> rows)
        {
            bool loading = _loadingControls;
            _loadingControls = true;
            try
            {
                foreach (GptRow row in rows)
                {
                    SetGptModelItems(row, GptModelValue(row));
                    ApplyGptEffortChoices(row);
                }
            }
            finally
            {
                _loadingControls = loading;
            }

            ResizeGptComboBoxes();
            AdjustWindowSize();
        }

        // effort の選べる値はモデルごとに違う。目録が知らないモデルには既定の一覧を残す。
        private void ApplyGptEffortChoices(GptRow row)
        {
            IReadOnlyList<string> efforts = row.Catalog != null
                ? row.Catalog.EffortsFor(row.ModelComboBox.Text)
                : _choices.GptEfforts;
            if (efforts.Count == 0)
            {
                efforts = _choices.GptEfforts;
            }

            SetComboItems(row.EffortComboBox, efforts, row.EffortComboBox.Text);
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
            if (_loadingControls)
            {
                return;
            }

            GptRow row = GptRowFor((ComboBox)sender);
            CodexModelCatalog catalog = row.Catalog;
            if (catalog == null)
            {
                return;
            }

            // 利用者がモデルを変えたときは、そのモデルが受け付けない effort を残さない。
            // 残すと、選べるように見えて Codex 側で弾かれる組み合わせを保存できてしまう。
            // 読み込み直後は値を変えない。開いただけで定義が書き換わるのを避けるためである。
            // 未設定へ変えたときは effort を触らない。GPT 側を呼ばない行の値をここで書き換える理由が無い。
            if (IsGptModelSet(row))
            {
                IReadOnlyList<string> efforts = catalog.EffortsFor(row.ModelComboBox.Text);
                string effort = row.EffortComboBox.Text;
                if (efforts.Count > 0 && !Contains(efforts, effort))
                {
                    string defaultEffort = catalog.DefaultEffortFor(row.ModelComboBox.Text);
                    row.EffortComboBox.Text = string.IsNullOrEmpty(defaultEffort) ? efforts[0] : defaultEffort;
                }
            }

            bool loading = _loadingControls;
            _loadingControls = true;
            try
            {
                ApplyGptEffortChoices(row);
            }
            finally
            {
                _loadingControls = loading;
            }

            ResizeGptComboBoxes();
            AdjustWindowSize();
        }

        private GptRow GptRowFor(ComboBox modelComboBox)
        {
            foreach (GptRow row in AllGptRows())
            {
                if (row.ModelComboBox == modelComboBox)
                {
                    return row;
                }
            }

            throw new ArgumentException("GPT モデルの欄ではない", nameof(modelComboBox));
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

        private void ResizeGptComboBoxes()
        {
            ResizeGptComboBoxes(_subagentGptRows);
            ResizeGptComboBoxes(_reviewGptRows);
        }

        // 選択肢を差し替えると必要な幅が変わる。列は中身の希望する大きさで決まるため、幅を計算し直す。
        // 同じ表の行は列の幅を揃える。
        private void ResizeGptComboBoxes(IReadOnlyList<GptRow> rows)
        {
            var models = new List<string>();
            var efforts = new List<string>();
            var modelTexts = new string[rows.Count];
            var effortTexts = new string[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                CollectItems(models, rows[i].ModelComboBox);
                CollectItems(efforts, rows[i].EffortComboBox);
                modelTexts[i] = rows[i].ModelComboBox.Text;
                effortTexts[i] = rows[i].EffortComboBox.Text;
            }

            int modelWidth = ComboBoxWidth(models, modelTexts);
            int effortWidth = ComboBoxWidth(efforts, effortTexts);
            for (int i = 0; i < rows.Count; i++)
            {
                SetComboBoxWidth(rows[i].ModelComboBox, modelWidth);
                SetComboBoxWidth(rows[i].EffortComboBox, effortWidth);
            }
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

        private async void LoadCodexVersionAsync(string codexHome)
        {
            Task<string> task = CodexVersionFor(codexHome);
            if (!task.IsCompleted)
            {
                SetCodexVersionText(CheckingVersionText);
            }

            string version = await task;
            if (IsDisposed || Disposing)
            {
                return;
            }

            // 取得の間に別のホームへ切り替わっていたら、そちらの表示を上書きしない。
            if (string.Equals(_currentCodexHomeKey, codexHome, StringComparison.OrdinalIgnoreCase))
            {
                SetCodexVersionText(version);
            }
        }

        private void SetCodexVersionText(string text)
        {
            _codexVersionText = text;
            _codexVersionLabel.Text = "codex --version: " + text;
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

        // GPT モデルと effort の欄の組。目録は行の認証ホームで引くため、行ごとに持つ。
        private sealed class GptRow
        {
            public GptRow(ComboBox modelComboBox, ComboBox effortComboBox, bool allowsUnset)
            {
                ModelComboBox = modelComboBox;
                EffortComboBox = effortComboBox;
                AllowsUnset = allowsUnset;
            }

            public ComboBox ModelComboBox { get; private set; }

            public ComboBox EffortComboBox { get; private set; }

            // 「(未設定)」を選択肢に持つかどうか。Claude 側へフォールバックできる定義の行だけが持つ。
            public bool AllowsUnset { get; private set; }

            // 取れていない間は null で、_choices の既定値を使う。
            public CodexModelCatalog Catalog { get; set; }
        }

        // レビューと実装補助タブの 1 行。codex_home は定義ごとに違うため、取得結果も行ごとに持つ。
        private sealed class CodexAgentRow
        {
            private readonly Func<CodexAgentSettings> _settings;

            public CodexAgentRow(
                Func<CodexAgentSettings> settings,
                GptRow gpt,
                Label homeLabel,
                Label sandboxLabel)
            {
                _settings = settings;
                Gpt = gpt;
                HomeLabel = homeLabel;
                SandboxLabel = sandboxLabel;
                HomeKey = string.Empty;
                VersionText = CheckingVersionText;
                CatalogText = FetchingCatalogText;
            }

            // 再読込で設定クラス側の入れ物が差し替わるため、参照を持たずに毎回引く。
            public CodexAgentSettings Settings
            {
                get { return _settings(); }
            }

            public GptRow Gpt { get; private set; }

            public Label HomeLabel { get; private set; }

            public Label SandboxLabel { get; private set; }

            // 展開した codex_home。取得結果を反映してよいのは、取得を始めたときの値から変わっていないときだけである。
            public string HomeKey { get; private set; }

            public string VersionText { get; set; }

            public string CatalogText { get; set; }

            // 認証ホームが変わったら、前のホームの目録と取得結果を持ち越さない。
            public void SetHomeKey(string homeKey)
            {
                if (string.Equals(HomeKey, homeKey, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                HomeKey = homeKey;
                Gpt.Catalog = null;
                VersionText = CheckingVersionText;
                CatalogText = FetchingCatalogText;
            }
        }

        // 認証ホーム 1 つ分の目録の取得結果。取れなかったことも結果として持つ。
        // 持たないと、取れないホームへ切り替えるたびに codex を起動し直すことになる。
        private sealed class CatalogResult
        {
            public CatalogResult(CodexModelCatalog catalog, string text, string summary)
            {
                Catalog = catalog;
                Text = text;
                Summary = summary;
            }

            public CodexModelCatalog Catalog { get; private set; }

            public string Text { get; private set; }

            // 2 つのホームの結果を 1 行に並べるときの短い文言。
            public string Summary { get; private set; }
        }
    }
}
