using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexModelCenter
{
    public sealed class Profile
    {
        public string id { get; set; }
        public string name { get; set; }
        public string providerId { get; set; }
        public string model { get; set; }
        public string baseUrl { get; set; }
        public string authMode { get; set; }
        public string envKey { get; set; }
        public string authCommand { get; set; }
        public string[] authArgs { get; set; }
        public string webSearch { get; set; }
    }

    static class Store
    {
        static string Folder(Environment.SpecialFolder folder, string variable)
        {
            string value = Environment.GetFolderPath(folder);
            if (string.IsNullOrWhiteSpace(value)) value = Environment.GetEnvironmentVariable(variable) ?? "";
            return value;
        }
        public static readonly string AppDir = Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData, "LOCALAPPDATA"), "CodexModelCenter");
        public static readonly string ProfilesPath = Path.Combine(AppDir, "profiles.json");
        public static readonly string CodexDir = Path.Combine(Folder(Environment.SpecialFolder.UserProfile, "USERPROFILE"), ".codex");
        public static readonly string ConfigPath = Path.Combine(CodexDir, "config.toml");
        public static readonly string BackupDir = Path.Combine(CodexDir, "codex-model-switcher-backups");
        public const string Begin = "# BEGIN CODEX MODEL SWITCHER MANAGED PROVIDERS";
        public const string End = "# END CODEX MODEL SWITCHER MANAGED PROVIDERS";
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static List<Profile> Load()
        {
            Directory.CreateDirectory(AppDir);
            if (!File.Exists(ProfilesPath))
            {
                string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles.json");
                string parent = Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)) ?? "", "profiles.json");
                string legacy = File.Exists(beside) ? beside : (File.Exists(parent) ? parent : null);
                if (legacy != null) File.Copy(legacy, ProfilesPath);
            }
            if (File.Exists(ProfilesPath))
            {
                var list = Json.Deserialize<List<Profile>>(File.ReadAllText(ProfilesPath, Encoding.UTF8));
                if (list != null && list.Count > 0) return list;
            }
            var defaults = new List<Profile> {
                new Profile { id = Guid.NewGuid().ToString("N"), name = "官方 ChatGPT / OpenAI", providerId = "openai", model = "gpt-5.6-sol", baseUrl = "", authMode = "builtin", envKey = "", authCommand = "", authArgs = new string[0], webSearch = "live" },
                new Profile { id = Guid.NewGuid().ToString("N"), name = "OpenRouter 模板", providerId = "openrouter", model = "请填写模型ID", baseUrl = "https://openrouter.ai/api/v1", authMode = "env", envKey = "OPENROUTER_API_KEY", authCommand = "", authArgs = new string[0], webSearch = "disabled" },
                new Profile { id = Guid.NewGuid().ToString("N"), name = "自定义 API 模板", providerId = "my_provider", model = "请填写模型ID", baseUrl = "https://example.com/v1", authMode = "env", envKey = "MY_PROVIDER_API_KEY", authCommand = "", authArgs = new string[0], webSearch = "disabled" },
                new Profile { id = Guid.NewGuid().ToString("N"), name = "本地模型模板", providerId = "local_responses", model = "请填写本地模型名", baseUrl = "http://127.0.0.1:11434/v1", authMode = "none", envKey = "", authCommand = "", authArgs = new string[0], webSearch = "disabled" }
            };
            Save(defaults);
            return defaults;
        }

        public static void Save(List<Profile> profiles)
        {
            Directory.CreateDirectory(AppDir);
            string temp = ProfilesPath + ".tmp";
            File.WriteAllText(temp, Json.Serialize(profiles), new UTF8Encoding(true));
            if (File.Exists(ProfilesPath)) File.Copy(ProfilesPath, ProfilesPath + ".bak", true);
            if (File.Exists(ProfilesPath)) File.Delete(ProfilesPath);
            File.Move(temp, ProfilesPath);
        }

        public static bool IsTemplate(Profile p)
        {
            return p != null && p.authMode != "builtin" &&
                ((p.model ?? "").StartsWith("请填写") || (p.baseUrl ?? "").Contains("example.com"));
        }

        public static string ActiveProvider()
        {
            if (!File.Exists(ConfigPath)) return "";
            var match = Regex.Match(File.ReadAllText(ConfigPath, Encoding.UTF8), @"(?m)^\s*model_provider\s*=\s*""([^""]+)""");
            return match.Success ? match.Groups[1].Value : "";
        }

        static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        static string SetTop(string text, string key, string value)
        {
            string line = key + " = " + Quote(value);
            var parts = Regex.Split(text, "\r?\n").ToList();
            int table = parts.FindIndex(x => Regex.IsMatch(x, @"^\s*\["));
            if (table < 0) table = parts.Count;
            int found = -1;
            for (int i = 0; i < table; i++)
                if (Regex.IsMatch(parts[i], @"^\s*" + Regex.Escape(key) + @"\s*=")) { found = i; break; }
            if (found >= 0) parts[found] = line;
            else parts.Insert(table, line);
            return string.Join("\r\n", parts).TrimEnd() + "\r\n";
        }

        public static void Validate(Profile p, IEnumerable<Profile> profiles)
        {
            if (string.IsNullOrWhiteSpace(p.name)) throw new InvalidOperationException("请填写显示名称。");
            if (string.IsNullOrWhiteSpace(p.model) || p.model.StartsWith("请填写")) throw new InvalidOperationException("请填写服务商提供的准确模型名称。");
            if (!Regex.IsMatch(p.providerId ?? "", "^[A-Za-z0-9_-]+$")) throw new InvalidOperationException("配置标识只能使用英文字母、数字、下划线和短横线。");
            if (p.authMode == "builtin")
            {
                if (p.providerId != "openai") throw new InvalidOperationException("官方登录的配置标识必须是 openai。");
            }
            else
            {
                if (new[] { "openai", "ollama", "lmstudio" }.Contains(p.providerId)) throw new InvalidOperationException("此配置标识由 Codex 保留，请换一个名称。");
                if (string.IsNullOrWhiteSpace(p.baseUrl) || p.baseUrl.Contains("example.com") || !Regex.IsMatch(p.baseUrl, @"^https?://", RegexOptions.IgnoreCase)) throw new InvalidOperationException("请填写有效的服务地址，须以 http:// 或 https:// 开头。");
                if (p.authMode == "env" && !Regex.IsMatch(p.envKey ?? "", "^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidOperationException("请填写有效的 API Key 环境变量名。");
                if (p.authMode == "command" && string.IsNullOrWhiteSpace(p.authCommand)) throw new InvalidOperationException("请填写取 Token 命令。");
            }
            if (profiles.Any(x => x.id != p.id && x.providerId == p.providerId)) throw new InvalidOperationException("配置标识已被其他模型使用，请换一个。");
        }

        static string ProviderBlock(Profile p)
        {
            var lines = new List<string> { "[model_providers." + p.providerId + "]", "name = " + Quote(p.name), "base_url = " + Quote(p.baseUrl.TrimEnd('/')), "wire_api = \"responses\"" };
            if (p.authMode == "env") lines.Add("env_key = " + Quote(p.envKey));
            if (p.authMode == "openai") lines.Add("requires_openai_auth = true");
            if (p.authMode == "command")
            {
                lines.Add("");
                lines.Add("[model_providers." + p.providerId + ".auth]");
                lines.Add("command = " + Quote(p.authCommand));
                lines.Add("args = [" + string.Join(", ", (p.authArgs ?? new string[0]).Where(x => !string.IsNullOrWhiteSpace(x)).Select(Quote)) + "]");
                lines.Add("timeout_ms = 5000");
                lines.Add("refresh_interval_ms = 300000");
            }
            return string.Join("\r\n", lines);
        }

        public static string BuildConfig(string oldText, Profile active, List<Profile> profiles)
        {
            string text = oldText ?? "";
            text = SetTop(text, "model", active.model);
            text = SetTop(text, "model_provider", active.providerId);
            text = SetTop(text, "web_search", active.webSearch ?? "disabled");
            return MergeProviderDefinitions(text, profiles);
        }

        public static string MergeProviderDefinitions(string oldText, List<Profile> profiles)
        {
            string text = oldText ?? "";
            string managedPattern = "(?ms)^" + Regex.Escape(Begin) + "\\r?\\n(.*?)^" + Regex.Escape(End) + @"\s*";
            var managedMatch = Regex.Match(text, managedPattern);
            string managedBody = managedMatch.Success ? managedMatch.Groups[1].Value : "";
            // Only remove a managed block when both markers are present. This
            // protects unrelated config content from an incomplete old block.
            if (managedMatch.Success) text = Regex.Replace(text, managedPattern, "").TrimEnd();

            // Retain definitions from earlier versions even when their profiles have
            // since been removed. Old conversations can still refer to those IDs.
            var blocks = new Dictionary<string, string>(StringComparer.Ordinal);
            var headers = Regex.Matches(managedBody, @"(?m)^\[model_providers\.([A-Za-z0-9_-]+)\]\s*$", RegexOptions.Multiline);
            for (int i = 0; i < headers.Count; i++)
            {
                int start = headers[i].Index;
                int end = i + 1 < headers.Count ? headers[i + 1].Index : managedBody.Length;
                blocks[headers[i].Groups[1].Value] = managedBody.Substring(start, end - start).Trim();
            }

            // User-maintained provider tables outside our block take precedence.
            var external = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(text, @"(?m)^\[model_providers\.([A-Za-z0-9_-]+)\]\s*$"))
                external.Add(match.Groups[1].Value);
            foreach (var p in profiles.Where(x => x.authMode != "builtin" && !IsTemplate(x)))
            {
                Validate(p, profiles);
                if (!external.Contains(p.providerId)) blocks[p.providerId] = ProviderBlock(p);
            }
            foreach (var id in external) blocks.Remove(id);
            if (blocks.Count > 0)
                text = text.TrimEnd() + "\r\n\r\n" + Begin + "\r\n" + string.Join("\r\n\r\n", blocks.Values) + "\r\n" + End + "\r\n";
            return text;
        }

        static void WriteConfig(string output, string backupPrefix)
        {
            Directory.CreateDirectory(CodexDir);
            Directory.CreateDirectory(BackupDir);
            string backup = null;
            if (File.Exists(ConfigPath))
            {
                backup = Path.Combine(BackupDir, backupPrefix + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".toml");
                File.Copy(ConfigPath, backup);
            }
            string temp = ConfigPath + ".model-center.tmp";
            try
            {
                File.WriteAllText(temp, output, new UTF8Encoding(false));
                if (string.IsNullOrWhiteSpace(File.ReadAllText(temp, Encoding.UTF8))) throw new IOException("配置写入为空。");
                if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, null);
                else File.Move(temp, ConfigPath);
            }
            catch
            {
                if (File.Exists(temp)) File.Delete(temp);
                if (backup != null && File.Exists(backup)) File.Copy(backup, ConfigPath, true);
                throw;
            }
        }

        public static bool SyncProviders(List<Profile> profiles)
        {
            string previous = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "";
            string output = MergeProviderDefinitions(previous, profiles);
            if (output == previous) return false;
            WriteConfig(output, "config.before-provider-sync");
            return true;
        }

        public static void Activate(Profile active, List<Profile> profiles)
        {
            Validate(active, profiles);
            string previous = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath, Encoding.UTF8) : "";
            string output = BuildConfig(previous, active, profiles);
            WriteConfig(output, "config.before-activate");
        }

        public static void RestoreLatest()
        {
            if (!Directory.Exists(BackupDir)) throw new InvalidOperationException("没有可恢复的配置备份。");
            var source = new DirectoryInfo(BackupDir).GetFiles("config.*.toml").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
            if (source == null) throw new InvalidOperationException("没有可恢复的配置备份。");
            if (File.Exists(ConfigPath)) File.Copy(ConfigPath, Path.Combine(BackupDir, "config.before-restore." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".toml"));
            File.Copy(source.FullName, ConfigPath, true);
        }
    }

    sealed class MainForm : Form
    {
        readonly Color Ink = Color.FromArgb(26, 39, 61);
        readonly Color Muted = Color.FromArgb(101, 115, 137);
        readonly Color Blue = Color.FromArgb(31, 94, 221);
        readonly Color Border = Color.FromArgb(221, 228, 238);
        readonly Font Body = new Font("Microsoft YaHei UI", 10);
        readonly Font Small = new Font("Microsoft YaHei UI", 9);
        List<Profile> profiles;
        Profile selected;
        string activeId;
        FlowLayoutPanel configuredList, templateList;
        TextBox filterBox;
        Label pageTitle, pageDescription, kindLabel, statusLabel;
        TextBox nameBox, providerBox, modelBox, urlBox, envBox, commandBox, argsBox, keyBox;
        ComboBox authBox, searchBox;
        Panel editor, keyPanel, commandPanel;
        Button activateButton, saveButton, deleteButton, cloneButton, testButton;

        public MainForm()
        {
            Text = "Codex 模型中心";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1080, 720);
            Size = new Size(1280, 840);
            BackColor = Color.FromArgb(246, 248, 252);
            Font = Body;
            AutoScaleMode = AutoScaleMode.Dpi;
            profiles = Store.Load();
            activeId = Store.ActiveProvider();
            BuildUi();
            RefreshList();
            SelectProfile(profiles.FirstOrDefault(x => x.providerId == activeId) ?? profiles.FirstOrDefault());
            try
            {
                if (Store.SyncProviders(profiles)) SetStatus("已同步旧模型配置，旧对话可以继续打开。");
            }
            catch (Exception ex)
            {
                SetStatus("Provider 同步失败：" + ex.Message);
            }
        }

        Label Label(string text, int size, bool bold, Color color)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0) };
        }

        Button Button(string text, bool primary, EventHandler click)
        {
            var b = new Button { Text = text, Height = 40, AutoSize = true, Padding = new Padding(15, 0, 15, 0), FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink, Font = Small, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
            b.FlatAppearance.BorderColor = primary ? Blue : Border;
            b.Click += click;
            return b;
        }

        Panel Field(string label, Control input)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 12), Height = 70 };
            var title = Label(label, 9, true, Muted); title.Location = new Point(0, 0);
            input.Location = new Point(0, 24); input.Height = 36; input.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(title); panel.Controls.Add(input);
            panel.Resize += (s, e) => input.Width = Math.Max(100, panel.ClientSize.Width - 2);
            return panel;
        }

        TextBox Input()
        {
            return new TextBox { Font = Body, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White, ForeColor = Ink };
        }

        ComboBox Select(params string[] choices)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = Body, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
            box.Items.AddRange(choices);
            return box;
        }

        TableLayoutPanel Grid(int columns, int rows)
        {
            var grid = new TableLayoutPanel { ColumnCount = columns, RowCount = rows, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0) };
            for (int i = 0; i < columns; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int i = 0; i < rows; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            return grid;
        }

        Panel Section(string title, string description)
        {
            var section = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.White, Padding = new Padding(24, 20, 24, 18), Margin = new Padding(0, 0, 0, 14) };
            var heading = Label(title, 11, true, Ink); heading.Dock = DockStyle.Top;
            section.Controls.Add(heading);
            if (!string.IsNullOrEmpty(description))
            {
                var sub = Label(description, 9, false, Muted); sub.Dock = DockStyle.Top; sub.Padding = new Padding(0, 4, 0, 14);
                section.Controls.Add(sub); section.Controls.SetChildIndex(heading, 0);
            }
            return section;
        }

        void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(24, 20, 24, 18), BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(Label("模型中心", 22, true, Ink));
            header.Controls[0].Location = new Point(0, 0);
            var tagline = Label("管理模型、服务地址和密钥。选好配置后，一键应用到 Codex。", 10, false, Muted);
            tagline.Location = new Point(2, 47); header.Controls.Add(tagline);
            root.Controls.Add(header, 0, 0);

            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 326)); main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(main, 0, 1);
            var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16), Margin = new Padding(0, 0, 16, 0) };
            main.Controls.Add(sidebar, 0, 0);
            var sideRoot = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            sideRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            sideRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            sideRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            sideRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            sidebar.Controls.Add(sideRoot);
            var sideHeader = new Panel { Dock = DockStyle.Fill };
            var sideTitle = Label("我的模型", 12, true, Ink); sideTitle.Location = new Point(0, 2); sideHeader.Controls.Add(sideTitle);
            filterBox = Input(); filterBox.Location = new Point(0, 36); filterBox.Height = 34; filterBox.Width = 290; filterBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            filterBox.TextChanged += (s, e) => RefreshList(); sideHeader.Controls.Add(filterBox);
            var searchHint = Label("搜索模型或服务商", 9, false, Muted); searchHint.Location = new Point(10, 44); searchHint.Enabled = false; sideHeader.Controls.Add(searchHint);
            filterBox.Enter += (s, e) => searchHint.Visible = false; filterBox.Leave += (s, e) => searchHint.Visible = string.IsNullOrEmpty(filterBox.Text);
            sideRoot.Controls.Add(sideHeader, 0, 0);
            configuredList = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.White };
            sideRoot.Controls.Add(configuredList, 0, 1);
            var templatesArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 16, 0, 0) };
            templatesArea.Controls.Add(Label("从模板开始", 10, true, Muted));
            templateList = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 32, 0, 0) };
            templatesArea.Controls.Add(templateList);
            sideRoot.Controls.Add(templatesArea, 0, 2);
            var add = Button("＋ 新建自定义模型", false, (s, e) => NewProfile()); add.Dock = DockStyle.Fill;
            sideRoot.Controls.Add(add, 0, 3);

            editor = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BackColor, Margin = new Padding(0) };
            main.Controls.Add(editor, 1, 0);
            var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            editor.Controls.Add(content);
            editor.Resize += (s, e) => content.Width = Math.Max(500, editor.ClientSize.Width - 16);

            var intro = new Panel { Dock = DockStyle.Top, Height = 78, Margin = new Padding(0, 0, 0, 8) };
            pageTitle = Label("配置详情", 18, true, Ink); pageTitle.Location = new Point(0, 0); intro.Controls.Add(pageTitle);
            pageDescription = Label("填写模型信息并选择连接方式", 9, false, Muted); pageDescription.Location = new Point(0, 39); intro.Controls.Add(pageDescription);
            kindLabel = Label("", 9, true, Blue); kindLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right; intro.Controls.Add(kindLabel);
            intro.Resize += (s, e) => kindLabel.Location = new Point(Math.Max(0, intro.ClientSize.Width - kindLabel.Width - 4), 8);
            content.Controls.Add(intro);

            var basics = Section("01  基本信息", "给配置起个名字，并填入服务商提供的模型名称。");
            var basicGrid = Grid(2, 2);
            nameBox = Input(); providerBox = Input(); modelBox = Input(); urlBox = Input();
            basicGrid.Controls.Add(Field("显示名称", nameBox), 0, 0);
            basicGrid.Controls.Add(Field("配置标识 · 旧对话会使用，保存后请勿改名", providerBox), 1, 0);
            basicGrid.Controls.Add(Field("模型名称", modelBox), 0, 1);
            basicGrid.Controls.Add(Field("服务地址 · 通常以 /v1 结尾", urlBox), 1, 1);
            basicGrid.Dock = DockStyle.Bottom; basics.Controls.Add(basicGrid);
            content.Controls.Add(basics);

            var connect = Section("02  连接方式", "大多数服务商选择 API Key；官方账号选择 ChatGPT 登录。");
            var connectGrid = Grid(2, 1);
            authBox = Select("API Key", "官方 ChatGPT 登录", "OpenAI 登录代理", "命令获取 Token", "无认证 / 本地");
            searchBox = Select("关闭", "实时搜索", "缓存搜索", "索引搜索");
            connectGrid.Controls.Add(Field("如何认证", authBox), 0, 0);
            connectGrid.Controls.Add(Field("联网搜索", searchBox), 1, 0);
            connectGrid.Dock = DockStyle.Top;
            connect.Controls.Add(connectGrid);
            keyPanel = new Panel { Dock = DockStyle.Top, Height = 132 };
            var keyGrid = Grid(2, 1); envBox = Input();
            var envCell = Field("密钥环境变量名 · 每家服务商建议使用不同名称", envBox);
            keyGrid.Controls.Add(envCell, 0, 0);
            var keyButton = Button("保存 API Key", false, (s, e) => SaveKey()); keyButton.Dock = DockStyle.Bottom;
            var keyButtonWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 22, 0, 12) }; keyButtonWrap.Controls.Add(keyButton);
            keyGrid.Controls.Add(keyButtonWrap, 1, 0);
            keyPanel.Controls.Add(keyGrid);
            keyBox = Input(); keyBox.UseSystemPasswordChar = true; keyBox.Width = 300;
            var keyHint = Label("输入密钥后点“保存 API Key”。密钥只保存在当前 Windows 用户环境变量。", 9, false, Muted);
            keyHint.Dock = DockStyle.Bottom; keyPanel.Controls.Add(keyHint);
            connect.Controls.Add(keyPanel);
            commandPanel = new Panel { Dock = DockStyle.Top, Height = 108, Visible = false };
            var commandGrid = Grid(2, 1); commandBox = Input(); argsBox = Input();
            commandGrid.Controls.Add(Field("取 Token 命令", commandBox), 0, 0);
            commandGrid.Controls.Add(Field("命令参数 · 用空格分隔", argsBox), 1, 0);
            commandPanel.Controls.Add(commandGrid); connect.Controls.Add(commandPanel);
            content.Controls.Add(connect);
            authBox.SelectedIndexChanged += (s, e) => UpdateAuth();

            var actions = Section("03  完成", "保存配置后，点“设为当前模型”使 Codex 使用它。");
            var actionBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, WrapContents = true };
            saveButton = Button("保存配置", false, (s, e) => Guard(() => SaveCurrent()));
            activateButton = Button("设为当前模型", true, (s, e) => Guard(() => ActivateCurrent()));
            testButton = Button("检查连接", false, (s, e) => Guard(() => TestConnection()));
            cloneButton = Button("复制", false, (s, e) => Guard(() => CloneCurrent()));
            deleteButton = Button("删除", false, (s, e) => Guard(() => DeleteCurrent()));
            actionBar.Controls.Add(saveButton); actionBar.Controls.Add(activateButton); actionBar.Controls.Add(testButton); actionBar.Controls.Add(cloneButton); actionBar.Controls.Add(deleteButton);
            actions.Controls.Add(actionBar);
            content.Controls.Add(actions);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(2, 8, 0, 0) };
            footer.Controls.Add(Button("打开 Codex 配置文件", false, (s, e) => Guard(() => { Directory.CreateDirectory(Store.CodexDir); if (!File.Exists(Store.ConfigPath)) File.WriteAllText(Store.ConfigPath, ""); System.Diagnostics.Process.Start("notepad.exe", "\"" + Store.ConfigPath + "\""); })));
            footer.Controls.Add(Button("恢复上次配置", false, (s, e) => Guard(() => { if (MessageBox.Show("恢复最近的 config.toml 备份？", "确认恢复", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { Store.RestoreLatest(); activeId = Store.ActiveProvider(); RefreshList(); SetStatus("已恢复配置。请重新启动 Codex。"); } })));
            content.Controls.Add(footer);
            statusLabel = Label("就绪 · 修改当前模型后，请重新启动 Codex。", 9, false, Muted); statusLabel.Dock = DockStyle.Top;
            content.Controls.Add(statusLabel);
        }

        void RefreshList()
        {
            configuredList.Controls.Clear(); templateList.Controls.Clear();
            string query = (filterBox == null ? "" : filterBox.Text).Trim().ToLowerInvariant();
            foreach (var p in profiles.Where(x => string.IsNullOrEmpty(query) || ((x.name ?? "") + " " + (x.providerId ?? "") + " " + (x.model ?? "")).ToLowerInvariant().Contains(query)))
            {
                bool template = Store.IsTemplate(p);
                var host = template ? templateList : configuredList;
                var card = new Panel { Height = 68, Width = Math.Max(240, host.ClientSize.Width - 24), BackColor = p == selected ? Color.FromArgb(234, 241, 255) : Color.White, Margin = new Padding(0, 0, 0, 6), Cursor = Cursors.Hand, Padding = new Padding(12, 8, 8, 4) };
                var title = Label(p.name, 10, true, p == selected ? Blue : Ink); title.Dock = DockStyle.Top; title.AutoEllipsis = true; title.AutoSize = false; title.Height = 26;
                var detail = Label(template ? "模板 · 点击创建个人配置" : (p.providerId == activeId ? "● 当前使用  ·  " : "") + (p.authMode == "builtin" ? "官方登录" : p.model), 9, false, Muted);
                detail.Dock = DockStyle.Bottom; detail.AutoEllipsis = true; detail.AutoSize = false; detail.Height = 22;
                card.Controls.Add(title); card.Controls.Add(detail);
                card.Click += (s, e) => SelectProfile(p); title.Click += (s, e) => SelectProfile(p); detail.Click += (s, e) => SelectProfile(p);
                host.Controls.Add(card);
            }
            configuredList.Resize += (s, e) => { foreach (Control c in configuredList.Controls) c.Width = Math.Max(240, configuredList.ClientSize.Width - 24); };
            templateList.Resize += (s, e) => { foreach (Control c in templateList.Controls) c.Width = Math.Max(240, templateList.ClientSize.Width - 24); };
        }

        static string AuthLabel(string mode)
        {
            switch (mode) { case "builtin": return "官方 ChatGPT 登录"; case "openai": return "OpenAI 登录代理"; case "command": return "命令获取 Token"; case "none": return "无认证 / 本地"; default: return "API Key"; }
        }
        static string AuthValue(string label)
        {
            switch (label) { case "官方 ChatGPT 登录": return "builtin"; case "OpenAI 登录代理": return "openai"; case "命令获取 Token": return "command"; case "无认证 / 本地": return "none"; default: return "env"; }
        }
        static string SearchLabel(string value)
        {
            switch (value) { case "live": return "实时搜索"; case "cached": return "缓存搜索"; case "indexed": return "索引搜索"; default: return "关闭"; }
        }
        static string SearchValue(string label)
        {
            switch (label) { case "实时搜索": return "live"; case "缓存搜索": return "cached"; case "索引搜索": return "indexed"; default: return "disabled"; }
        }

        void SelectProfile(Profile p)
        {
            selected = p;
            if (p == null) return;
            bool template = Store.IsTemplate(p);
            pageTitle.Text = p.name;
            pageDescription.Text = template ? "这是一个起点。点击下方按钮复制为你的配置，再填写真实模型与地址。" : (p.providerId == activeId ? "当前正在使用这项配置。" : "编辑并保存后，可以设为当前模型。");
            kindLabel.Text = template ? "模板" : (p.authMode == "builtin" ? "官方" : "自定义");
            nameBox.Text = p.name; providerBox.Text = p.providerId; modelBox.Text = p.model; urlBox.Text = p.baseUrl;
            envBox.Text = p.envKey; commandBox.Text = p.authCommand; argsBox.Text = string.Join(" ", p.authArgs ?? new string[0]);
            authBox.SelectedItem = AuthLabel(p.authMode); searchBox.SelectedItem = SearchLabel(p.webSearch);
            foreach (Control c in new Control[] { nameBox, providerBox, modelBox, urlBox, envBox, commandBox, argsBox, authBox, searchBox }) c.Enabled = !template;
            saveButton.Enabled = !template; activateButton.Enabled = !template; testButton.Enabled = !template; deleteButton.Enabled = !template && p.authMode != "builtin";
            cloneButton.Text = template ? "从模板创建" : "复制";
            UpdateAuth(); RefreshList();
        }

        void UpdateAuth()
        {
            string mode = AuthValue(Convert.ToString(authBox.SelectedItem));
            bool template = Store.IsTemplate(selected);
            bool builtin = mode == "builtin";
            providerBox.Enabled = !template && !builtin && selected != null && selected.providerId != activeId;
            urlBox.Enabled = !template && !builtin;
            keyPanel.Visible = !template && mode == "env";
            commandPanel.Visible = !template && mode == "command";
            testButton.Enabled = !template && (mode == "env" || mode == "none");
        }

        Profile ReadForm()
        {
            if (selected == null) throw new InvalidOperationException("请先选择一个模型。");
            selected.name = nameBox.Text.Trim(); selected.providerId = providerBox.Text.Trim(); selected.model = modelBox.Text.Trim(); selected.baseUrl = urlBox.Text.Trim();
            selected.authMode = AuthValue(Convert.ToString(authBox.SelectedItem)); selected.envKey = envBox.Text.Trim(); selected.authCommand = commandBox.Text.Trim();
            selected.authArgs = argsBox.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); selected.webSearch = SearchValue(Convert.ToString(searchBox.SelectedItem));
            return selected;
        }

        void SaveCurrent()
        {
            var p = ReadForm(); Store.Validate(p, profiles); Store.Save(profiles); RefreshList(); SetStatus("已保存“" + p.name + "”。");
        }

        void ActivateCurrent()
        {
            SaveCurrent(); Store.Activate(selected, profiles); activeId = selected.providerId; RefreshList();
            SetStatus("已设为当前模型。请完全退出并重新打开 Codex。");
            MessageBox.Show("已切换到“" + selected.name + "”。\n\n请完全退出并重新打开 Codex。旧模型配置会保留，旧对话仍可加载。", "切换完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void NewProfile()
        {
            string id = "provider_" + DateTime.Now.ToString("HHmmss");
            var p = new Profile { id = Guid.NewGuid().ToString("N"), name = "我的新模型", providerId = id, model = "", baseUrl = "", authMode = "env", envKey = id.ToUpperInvariant() + "_API_KEY", authCommand = "", authArgs = new string[0], webSearch = "disabled" };
            profiles.Add(p); Store.Save(profiles); SelectProfile(p); nameBox.Focus(); nameBox.SelectAll();
        }

        void CloneCurrent()
        {
            if (selected == null) return;
            var p = new Profile { id = Guid.NewGuid().ToString("N"), name = Store.IsTemplate(selected) ? "我的模型" : selected.name + " 副本", providerId = selected.providerId + "_copy_" + DateTime.Now.ToString("HHmm"), model = Store.IsTemplate(selected) ? "" : selected.model, baseUrl = Store.IsTemplate(selected) && selected.baseUrl.Contains("example.com") ? "" : selected.baseUrl, authMode = selected.authMode, envKey = selected.providerId.ToUpperInvariant() + "_COPY_API_KEY", authCommand = selected.authCommand, authArgs = selected.authArgs ?? new string[0], webSearch = selected.webSearch };
            profiles.Add(p); Store.Save(profiles); SelectProfile(p); nameBox.Focus(); nameBox.SelectAll();
        }

        void DeleteCurrent()
        {
            if (selected == null || selected.authMode == "builtin") return;
            if (MessageBox.Show("删除“" + selected.name + "”？\n\n如果旧对话使用它的配置标识，之后重新激活其他模型时可能无法继续旧对话。", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            profiles.Remove(selected); Store.Save(profiles); SelectProfile(profiles.FirstOrDefault());
        }

        void SaveKey()
        {
            if (!Regex.IsMatch(envBox.Text.Trim(), "^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidOperationException("请先填写有效的密钥环境变量名。");
            using (var dialog = new Form { Text = "保存 API Key", Size = new Size(480, 205), MinimumSize = new Size(480, 205), MaximumSize = new Size(480, 205), StartPosition = FormStartPosition.CenterParent, Font = Body, BackColor = Color.White })
            {
                var label = Label("将密钥保存到当前 Windows 用户环境变量：" + envBox.Text.Trim(), 9, false, Muted); label.Location = new Point(22, 20); dialog.Controls.Add(label);
                var secret = Input(); secret.UseSystemPasswordChar = true; secret.SetBounds(22, 54, 420, 32); dialog.Controls.Add(secret);
                var ok = Button("保存密钥", true, (s, e) => dialog.DialogResult = DialogResult.OK); ok.Location = new Point(307, 105); dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (string.IsNullOrWhiteSpace(secret.Text)) throw new InvalidOperationException("密钥不能为空。");
                    Environment.SetEnvironmentVariable(envBox.Text.Trim(), secret.Text, EnvironmentVariableTarget.User);
                    Environment.SetEnvironmentVariable(envBox.Text.Trim(), secret.Text, EnvironmentVariableTarget.Process);
                    secret.Clear(); SetStatus("密钥已保存。重新启动 Codex 后生效。");
                }
            }
        }

        void TestConnection()
        {
            var p = ReadForm(); Store.Validate(p, profiles);
            if (p.authMode != "env" && p.authMode != "none") throw new InvalidOperationException("此认证方式请在 Codex 中直接验证。");
            string token = p.authMode == "env" ? Environment.GetEnvironmentVariable(p.envKey, EnvironmentVariableTarget.Process) ?? Environment.GetEnvironmentVariable(p.envKey, EnvironmentVariableTarget.User) : null;
            if (p.authMode == "env" && string.IsNullOrEmpty(token)) throw new InvalidOperationException("找不到 API Key。请先点击“保存 API Key”。");
            var request = (HttpWebRequest)WebRequest.Create(p.baseUrl.TrimEnd('/') + "/models"); request.Method = "GET"; request.Timeout = 12000;
            if (token != null) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            using (var response = (HttpWebResponse)request.GetResponse())
                MessageBox.Show("连接成功（HTTP " + (int)response.StatusCode + "）。\n\n这只检查 /models；实际使用还需要服务支持 Responses API。", "连接结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void SetStatus(string text) { statusLabel.Text = text; }
        void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--sync-providers"))
            {
                try
                {
                    var profiles = Store.Load();
                    bool changed = Store.SyncProviders(profiles);
                    Console.WriteLine(changed ? "Provider sync: OK" : "Provider sync: already current");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }
            if (args.Contains("--self-test"))
            {
                var p = new Profile { id = "a", name = "测试", providerId = "test_provider", model = "test-model", baseUrl = "https://test.invalid/v1", authMode = "env", envKey = "TEST_KEY", authArgs = new string[0], webSearch = "disabled" };
                var list = new List<Profile> { p };
                string output = Store.BuildConfig("model_provider = \"openai\"\r\n", p, list);
                if (!output.Contains("[model_providers.test_provider]") || !output.Contains("model_provider = \"test_provider\"")) return 1;
                Console.WriteLine("ModelCenter self-test: OK");
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
