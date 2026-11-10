using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

[assembly: AssemblyTitle("battle.net account switcher")]
[assembly: AssemblyProduct("battle.net account switcher")]
[assembly: AssemblyDescription("Local account switcher for Battle.net")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal sealed class Account
{
    public string Login { get; set; }
    public string Key { get; set; }
    public string Name { get; set; }
    public string Initials { get; set; }
    public Brush Avatar { get; set; }
    public Visibility Recent { get; set; }
    public string AccessibleName { get { return Name + ", " + Login; } }
}

internal sealed class PickerException : IOException
{
    public PickerException(string message) : base(message) { }
}

internal static class GitHubReleases
{
    private const string Repository = "GooseRuby/battle.net-account-switcher";

    public static string ReleaseUrl(string tag)
    {
        return "https://github.com/" + Repository + "/releases/tag/" + Uri.EscapeDataString(tag);
    }

    public static Version VersionFromTag(string tag)
    {
        Version version;
        string number = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
        if (!Version.TryParse(number, out version))
            return null;
        return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    public static string LatestTag()
    {
        var request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repository + "/releases/latest");
        request.UserAgent = "BattleNetAccountSwitcher";
        request.Accept = "application/vnd.github+json";
        request.AllowAutoRedirect = false;
        request.Timeout = request.ReadWriteTimeout = 10000;
        try
        {
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream()))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new PickerException(UiText.T("GitHub вернул неожиданный ответ.", "GitHub returned an unexpected response."));
                var release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                object tagValue;
                if (release == null || !release.TryGetValue("tag_name", out tagValue) ||
                    !(tagValue is string) || string.IsNullOrWhiteSpace((string)tagValue))
                    throw new PickerException(UiText.T("В ответе GitHub нет тега релиза.", "The GitHub response did not contain a release tag."));
                return (string)tagValue;
            }
        }
        catch (WebException ex)
        {
            using (var response = ex.Response as HttpWebResponse)
            {
                if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                    throw new PickerException(UiText.T("Репозиторий недоступен или в нём ещё нет опубликованных стабильных релизов.", "The repository is unavailable or has no published stable releases yet."));
                if (response != null && ((int)response.StatusCode == 429 || response.Headers["X-RateLimit-Remaining"] == "0"))
                    throw new PickerException(UiText.T("Достигнут лимит запросов к GitHub. Попробуйте позже.", "GitHub rate limit reached. Try again later."));
            }
            throw new PickerException(UiText.T("Не удалось связаться с GitHub. Проверьте подключение и попробуйте ещё раз.", "Could not reach GitHub. Check your connection and try again."));
        }
    }
}

internal sealed class Preferences
{
    public Preferences() { AutoCheckUpdates = true; }

    public string Language { get; set; }
    public bool AutoCheckUpdates { get; set; }
}

internal sealed class LauncherConfig
{
    private const string AccountField = "SavedAccountNames";
    private static readonly Regex AccountValue = new Regex(
        "(?<=\\x22SavedAccountNames\\x22)\\s*:\\s*(?<value>\\x22(?:[^\\x22\\\\]|\\\\.)*\\x22)",
        RegexOptions.CultureInvariant);
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Battle.net", "Battle.net.config");
    private const string BackupSuffix = ".account-picker.bak";

    private string original;
    private string savedNames;
    private Encoding encoding;
    public string[] Logins { get; private set; }

    public static LauncherConfig Read()
    {
        var config = new LauncherConfig { Logins = new string[0] };
        try
        {
            using (var reader = new StreamReader(ConfigPath, new UTF8Encoding(false), true))
            {
                config.original = reader.ReadToEnd();
                config.encoding = reader.CurrentEncoding;
            }
        }
        catch (FileNotFoundException) { return config; }
        catch (DirectoryNotFoundException) { return config; }

        try
        {
            var root = Json.Deserialize<Dictionary<string, object>>(config.original);
            object clientValue;
            object namesValue;
            if (root == null || !root.TryGetValue("Client", out clientValue))
                throw new InvalidDataException();
            var client = clientValue as Dictionary<string, object>;
            if (client == null)
                throw new InvalidDataException();
            if (!client.TryGetValue(AccountField, out namesValue) || namesValue == null)
                return config;
            config.savedNames = namesValue as string;
            if (config.savedNames == null)
                throw new InvalidDataException();
            config.Logins = config.savedNames.Split(',').Select(name => name.Trim())
                .Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return config;
        }
        catch (ArgumentException) { throw InvalidConfig(); }
        catch (InvalidOperationException) { throw InvalidConfig(); }
        catch (InvalidDataException) { throw InvalidConfig(); }
    }

    private static InvalidDataException InvalidConfig()
    {
        return new InvalidDataException(UiText.T("Не удалось прочитать список аккаунтов. Откройте Battle.net и обновите список.", "Could not read the account list. Open Battle.net and refresh the list."));
    }

    public void StartWith(string login, string launcher)
    {
        string selected = Logins.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item, login));
        if (selected == null)
            throw new InvalidDataException(UiText.T("Аккаунт больше не сохранён в Battle.net. Обновите список.", "This account is no longer saved in Battle.net. Refresh the list."));
        if (selected == Logins[0])
        {
            Start(launcher);
            return;
        }

        var matches = AccountValue.Matches(original);
        if (matches.Count != 1 || Json.Deserialize<string>(matches[0].Groups["value"].Value) != savedNames)
            throw InvalidConfig();

        var field = matches[0].Groups["value"];
        var reordered = new[] { selected }.Concat(Logins.Where(item => item != selected));
        string newValue = Json.Serialize(string.Join(",", reordered));
        string updated = original.Substring(0, field.Index) + newValue + original.Substring(field.Index + field.Length);
        ReplaceIfUnchanged(original, updated, ConfigPath + BackupSuffix);
        try
        {
            Start(launcher);
        }
        catch (PickerException)
        {
            ReplaceIfUnchanged(updated, original, null);
            throw;
        }
    }

    private void ReplaceIfUnchanged(string expected, string replacement, string backup)
    {
        string temporary = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, replacement, encoding);
            if (File.ReadAllText(ConfigPath) != expected)
                throw new PickerException(UiText.T("Battle.net обновил список аккаунтов. Нажмите «Обновить» и попробуйте ещё раз.", "Battle.net updated its account list. Click Refresh and try again."));
            File.Replace(temporary, ConfigPath, backup);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static void Start(string launcher)
    {
        try
        {
            using (var process = Process.Start(new ProcessStartInfo(launcher) {
                WorkingDirectory = Path.GetDirectoryName(launcher), UseShellExecute = true
            })) { }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new PickerException(UiText.T("Не удалось открыть Battle.net. Проверьте, запускается ли лаунчер с обычного ярлыка.", "Could not open Battle.net. Check whether the launcher opens from its usual shortcut."));
        }
    }

    private static Process[] RunningClients()
    {
        int session;
        using (var current = Process.GetCurrentProcess())
            session = current.SessionId;
        var clients = new List<Process>();
        foreach (string name in new[] { "Battle.net", "Battle.net Launcher" })
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (process.SessionId == session && !process.HasExited)
                    {
                        clients.Add(process);
                        continue;
                    }
                }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
        }
        return clients.ToArray();
    }

    public static void CloseClients()
    {
        var clients = RunningClients();
        try
        {
            foreach (Process client in clients)
            {
                if (!client.HasExited)
                    client.CloseMainWindow();
            }
            var timer = Stopwatch.StartNew();
            foreach (Process client in clients)
            {
                int remaining = Math.Max(0, 2400 - (int)timer.ElapsedMilliseconds);
                if (!client.WaitForExit(remaining))
                {
                    try { client.Kill(); }
                    catch (InvalidOperationException) { if (!client.HasExited) throw; }
                    if (!client.WaitForExit(3000))
                        throw new PickerException(UiText.T("Battle.net всё ещё работает. Выйдите из него через значок рядом с часами.", "Battle.net is still running. Quit it from its system tray icon."));
                }
            }
        }
        finally
        {
            foreach (Process client in clients)
                client.Dispose();
        }
        var remainingClients = RunningClients();
        foreach (Process client in remainingClients)
            client.Dispose();
        if (remainingClients.Length > 0)
            throw new PickerException(UiText.T("Battle.net снова запустился. Выйдите из него через значок рядом с часами.", "Battle.net restarted. Quit it from its system tray icon."));
    }
}

internal sealed class Picker
{
    private static readonly Color[] AvatarColors = {
        Color.FromRgb(41, 131, 201), Color.FromRgb(137, 89, 192),
        Color.FromRgb(31, 158, 138), Color.FromRgb(198, 110, 59),
        Color.FromRgb(86, 112, 213), Color.FromRgb(188, 84, 137)
    };
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BattleNetAccountPicker");
    private static readonly string AliasPath = Path.Combine(SettingsDirectory, "names.json");
    private static readonly string HiddenPath = Path.Combine(SettingsDirectory, "hidden.json");
    private static readonly string PreferencesPath = Path.Combine(SettingsDirectory, "preferences.json");
    private static readonly Version CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version;

    private readonly Window window;
    private readonly ListBox accounts;
    private readonly Button launch;
    private readonly Button rename;
    private readonly Button hide;
    private readonly Button restore;
    private readonly Button hiddenList;
    private readonly Grid renamePanel;
    private readonly TextBox profileName;
    private readonly Border settingsPanel;
    private readonly TextBlock updateStatus;
    private readonly Button releaseButton;
    private readonly Button updateNotice;
    private readonly ComboBox languageSelect;
    private readonly CheckBox autoUpdateCheck;
    private readonly TextBlock status;
    private Dictionary<string, string> aliases;
    private HashSet<string> hidden;
    private bool showHidden;
    private bool busy;
    private bool checkingUpdate;
    private string launcher;
    private Preferences preferences;
    private string latestTag;
    private string updateError;

    [STAThread]
    public static void Main()
    {
        var preferences = ReadSetting(PreferencesPath, new Preferences());
        if (preferences.Language == "en" || preferences.Language == "ru")
            UiText.English = preferences.Language == "en";
        using (var instance = new Mutex(false, @"Local\BattleNetAccountPicker"))
        {
            bool acquired;
            try { acquired = instance.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                MessageBox.Show(UiText.T("Переключатель уже открыт.", "The account switcher is already open."), "battle.net account switcher");
                return;
            }

            try
            {
                var app = new Application();
                UiText.Apply(app.Resources);
                var picker = new Picker(preferences);
                app.Run(picker.window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiText.T("Не удалось открыть переключатель. ", "Could not open the account switcher. ") + ex.GetType().Name,
                    "battle.net account switcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { instance.ReleaseMutex(); }
        }
    }

    private Picker(Preferences preferences)
    {
        this.preferences = preferences;
        window = (Window)Application.LoadComponent(new Uri("Switcher.xaml", UriKind.Relative));
        window.Width = Math.Min(window.Width, SystemParameters.WorkArea.Width - 16);
        window.Height = Math.Min(window.Height, SystemParameters.WorkArea.Height - 16);

        accounts = Control<ListBox>("Accounts");
        launch = Control<Button>("LaunchButton");
        rename = Control<Button>("RenameButton");
        hide = Control<Button>("HideButton");
        restore = Control<Button>("RestoreButton");
        hiddenList = Control<Button>("HiddenListButton");
        renamePanel = Control<Grid>("RenamePanel");
        profileName = Control<TextBox>("ProfileName");
        settingsPanel = Control<Border>("SettingsPanel");
        updateStatus = Control<TextBlock>("UpdateStatus");
        releaseButton = Control<Button>("ReleaseButton");
        updateNotice = Control<Button>("UpdateNotice");
        languageSelect = Control<ComboBox>("LanguageSelect");
        autoUpdateCheck = Control<CheckBox>("AutoUpdateCheck");
        UpdateVersionLabel();
        status = Control<TextBlock>("StatusText");
        aliases = ReadSetting(AliasPath, new Dictionary<string, string>());
        hidden = new HashSet<string>(ReadSetting(HiddenPath, new string[0]), StringComparer.Ordinal);
        launcher = FindLauncher();

        Control<Grid>("TitleBar").MouseLeftButtonDown += (sender, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed)
                window.DragMove();
        };
        Control<Button>("MinimizeButton").Click += (sender, args) => window.WindowState = WindowState.Minimized;
        Control<Button>("CloseButton").Click += (sender, args) => window.Close();
        Control<Button>("SettingsButton").Click += (sender, args) => OpenSettings();
        Control<Button>("SaveSettingsButton").Click += (sender, args) => SaveSettings();
        Control<Button>("CheckUpdateButton").Click += async (sender, args) => await CheckUpdate(true);
        releaseButton.Click += (sender, args) => OpenRelease();
        updateNotice.Click += (sender, args) => OpenRelease();
        Control<Button>("RefreshButton").Click += (sender, args) => Refresh();
        Control<Button>("OpenClientButton").Click += (sender, args) => OpenWithoutSelecting();
        Control<Button>("CancelRename").Click += (sender, args) => CloseRename();
        Control<Button>("SaveRename").Click += (sender, args) => SaveName();
        rename.Click += (sender, args) => OpenRename();
        hide.Click += (sender, args) => ToggleSelectedVisibility();
        restore.Click += (sender, args) => ToggleSelectedVisibility();
        hiddenList.Click += (sender, args) =>
        {
            showHidden = !showHidden;
            Refresh();
        };
        launch.Click += async (sender, args) => await LaunchSelected();
        accounts.SelectionChanged += (sender, args) => UpdateActions();
        accounts.MouseDoubleClick += async (sender, args) =>
        {
            var origin = args.OriginalSource as DependencyObject;
            if (args.ChangedButton == MouseButton.Left && origin != null &&
                ItemsControl.ContainerFromElement(accounts, origin) is ListBoxItem)
                await LaunchSelected();
        };
        window.PreviewKeyDown += async (sender, args) =>
        {
            if (settingsPanel.Visibility == Visibility.Visible)
            {
                if (args.Key == Key.Escape && !languageSelect.IsDropDownOpen)
                { args.Handled = true; CloseSettings(); }
            }
            else if (renamePanel.Visibility == Visibility.Visible)
            {
                if (args.Key == Key.Escape) { args.Handled = true; CloseRename(); }
                if (args.Key == Key.Enter && profileName.IsKeyboardFocusWithin)
                { args.Handled = true; SaveName(); }
            }
            else if (args.Key == Key.Enter && accounts.IsKeyboardFocusWithin)
            {
                args.Handled = true;
                await LaunchSelected();
            }
            else if (args.Key == Key.F5 && !busy)
            {
                args.Handled = true;
                Refresh();
            }
        };
        window.Closing += (sender, args) => args.Cancel = busy;
        window.ContentRendered += CheckOnStartup;
        Refresh();
    }

    private async void CheckOnStartup(object sender, EventArgs args)
    {
        window.ContentRendered -= CheckOnStartup;
        if (preferences.AutoCheckUpdates)
            await CheckUpdate(false);
    }

    private T Control<T>(string name) where T : class { return (T)window.FindName(name); }
    private Account Selected { get { return accounts.SelectedItem as Account; } }

    private static T ReadSetting<T>(string path, T fallback) where T : class
    {
        try
        {
            return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(path)) ?? fallback;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        return fallback;
    }

    private static void WriteSetting<T>(string path, T value)
    {
        Directory.CreateDirectory(SettingsDirectory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private void Refresh(string preserveLogin = null)
    {
        string selection = preserveLogin ?? (Selected == null ? null : Selected.Login);
        Account[] cards;
        string error = null;
        try
        {
            var config = LauncherConfig.Read();
            cards = config.Logins.OrderBy(login => login, StringComparer.OrdinalIgnoreCase)
                .Select((login, index) => BuildAccount(login, index + 1, login == config.Logins[0]))
                .ToArray();
        }
        catch (InvalidDataException ex) { cards = new Account[0]; error = ex.Message; }
        catch (IOException) { cards = new Account[0]; error = UiText.T("Настройки Battle.net заняты. Попробуйте обновить список.", "The Battle.net settings file is busy. Try refreshing the list."); }
        catch (UnauthorizedAccessException) { cards = new Account[0]; error = UiText.T("Windows не дал прочитать настройки Battle.net.", "Windows denied access to the Battle.net settings file."); }

        int hiddenCount = cards.Count(card => hidden.Contains(card.Key));
        if (hiddenCount == 0)
            showHidden = false;

        var visibleCards = cards.Where(card => hidden.Contains(card.Key) == showHidden).ToArray();
        accounts.ItemsSource = visibleCards;
        accounts.SelectedItem = visibleCards.FirstOrDefault(card => StringComparer.OrdinalIgnoreCase.Equals(card.Login, selection))
            ?? visibleCards.FirstOrDefault(card => card.Recent == Visibility.Visible)
            ?? visibleCards.FirstOrDefault();
        string listTitle = showHidden ? UiText.T("Скрытые аккаунты", "Hidden accounts") : UiText.T("Сохранённые аккаунты", "Saved accounts");
        Control<TextBlock>("ListTitle").Text = listTitle.ToUpperInvariant();
        System.Windows.Automation.AutomationProperties.SetName(accounts, listTitle);
        hiddenList.Visibility = hiddenCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        hiddenList.Content = showHidden ? UiText.T("К списку аккаунтов", "Back to accounts") : UiText.T("Скрытые (", "Hidden (") + hiddenCount + ")";
        Control<TextBlock>("AccountCount").Text = visibleCards.Length.ToString();
        Control<Border>("EmptyPanel").Visibility = visibleCards.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        bool allHidden = cards.Length > 0 && visibleCards.Length == 0;
        Control<TextBlock>("EmptyTitle").Text = allHidden ? UiText.T("Все аккаунты скрыты", "All accounts are hidden") : UiText.T("Нет сохранённых аккаунтов", "No saved accounts");
        Control<TextBlock>("EmptyHint").Text = allHidden
            ? UiText.T("Откройте «Скрытые», чтобы вернуть аккаунт в список.", "Open Hidden to restore an account to the list.")
            : UiText.T("Войдите в Battle.net с сохранением входа, затем нажмите «Обновить».", "Sign in to Battle.net, enable the option to stay signed in, then click Refresh.");
        Control<Button>("OpenClientButton").Visibility = allHidden ? Visibility.Collapsed : Visibility.Visible;
        UpdateActions();
        if (error != null)
            status.Text = error;
    }

    private Account BuildAccount(string login, int number, bool recent)
    {
        byte[] hash;
        using (var sha = SHA256.Create())
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(login.ToUpperInvariant()));
        string key = Convert.ToBase64String(hash);
        string alias;
        if (!aliases.TryGetValue(key, out alias))
            alias = null;
        string name = string.IsNullOrWhiteSpace(alias) ? UiText.T("Профиль ", "Profile ") + number : alias.Trim();
        string first = System.Globalization.StringInfo.GetNextTextElement(name).ToUpperInvariant();
        string suffix = Regex.Match(name, @"\s+([0-9]{1,3})\z").Groups[1].Value;
        Color light = AvatarColors[hash[0] % AvatarColors.Length];
        Color shade = Color.FromRgb((byte)((light.R + 47) / 2), (byte)((light.G + 67) / 2), (byte)((light.B + 97) / 2));
        var brush = new LinearGradientBrush(light, shade, 58);
        brush.Freeze();
        return new Account {
            Login = login,
            Key = key,
            Name = name,
            Initials = first + suffix,
            Avatar = brush,
            Recent = recent ? Visibility.Visible : Visibility.Collapsed
        };
    }

    private void UpdateActions()
    {
        launch.IsEnabled = rename.IsEnabled = !busy && !showHidden && Selected != null;
        launch.Visibility = rename.Visibility = showHidden ? Visibility.Collapsed : Visibility.Visible;
        hide.IsEnabled = restore.IsEnabled = !busy && Selected != null;
        hide.Visibility = showHidden ? Visibility.Collapsed : Visibility.Visible;
        restore.Visibility = showHidden ? Visibility.Visible : Visibility.Collapsed;
        hiddenList.IsEnabled = !busy;
        Control<Button>("SettingsButton").IsEnabled = !busy;
        if (busy)
            return;

        bool empty = Selected == null;
        if (showHidden)
            status.Text = UiText.T("Выберите аккаунт, который хотите вернуть.", "Select an account to restore.");
        else if (empty)
            status.Text = hiddenList.Visibility == Visibility.Visible
                ? UiText.T("Скрытые аккаунты можно вернуть в список.", "You can restore hidden accounts to the list.")
                : UiText.T("Войдите в Battle.net, чтобы добавить профиль.", "Sign in to Battle.net to add a profile.");
        else
            status.Text = UiText.T("При смене профиля Battle.net перезапустится.", "Battle.net will restart when you switch profiles.");
        Control<TextBlock>("StatusHint").Text = showHidden
            ? UiText.T("Аккаунты скрыты только в переключателе.", "Accounts are hidden only in the picker.")
            : UiText.T("Подтверждение входа, если нужно, появится в Battle.net.", "Battle.net will ask you to verify sign-in if needed.");
    }

    private void ToggleSelectedVisibility()
    {
        Account account = Selected;
        if (busy || account == null)
            return;
        var updated = new HashSet<string>(hidden, StringComparer.Ordinal);
        if (showHidden)
            updated.Remove(account.Key);
        else
            updated.Add(account.Key);

        try
        {
            WriteSetting(HiddenPath, updated.ToArray());
        }
        catch (IOException) { ShowSaveError(UiText.T("Не удалось изменить список скрытых аккаунтов.", "Could not save the hidden accounts list.")); return; }
        catch (UnauthorizedAccessException) { ShowSaveError(UiText.T("Не удалось изменить список скрытых аккаунтов.", "Could not save the hidden accounts list.")); return; }

        hidden = updated;
        Refresh(account.Login);
    }

    private void OpenRename()
    {
        if (Selected == null || showHidden)
            return;
        string alias;
        Control<TextBlock>("RenameHint").Text = Selected.Login;
        profileName.Text = aliases.TryGetValue(Selected.Key, out alias) ? alias : Selected.Name;
        renamePanel.Visibility = Visibility.Visible;
        profileName.Focus();
        profileName.SelectAll();
    }

    private void CloseRename()
    {
        renamePanel.Visibility = Visibility.Collapsed;
        rename.Focus();
    }

    private void SaveName()
    {
        Account account = Selected;
        if (account == null)
        {
            CloseRename();
            return;
        }
        var updated = new Dictionary<string, string>(aliases);
        string value = profileName.Text.Trim();
        if (value.Length == 0)
            updated.Remove(account.Key);
        else
            updated[account.Key] = value;

        try
        {
            WriteSetting(AliasPath, updated);
            aliases = updated;
            CloseRename();
            Refresh(account.Login);
        }
        catch (IOException) { ShowSaveError(UiText.T("Не удалось сохранить имя профиля.", "Could not save the profile name.")); }
        catch (UnauthorizedAccessException) { ShowSaveError(UiText.T("Не удалось сохранить имя профиля.", "Could not save the profile name.")); }
    }

    private void ShowSaveError(string message)
    {
        MessageBox.Show(window, message, window.Title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OpenSettings()
    {
        if (settingsPanel.Visibility == Visibility.Visible)
            return;
        languageSelect.SelectedValue = UiText.English ? "en" : "ru";
        autoUpdateCheck.IsChecked = preferences.AutoCheckUpdates;
        RenderUpdateStatus();
        settingsPanel.Visibility = Visibility.Visible;
        languageSelect.Focus();
    }

    private void CloseSettings()
    {
        languageSelect.IsDropDownOpen = false;
        settingsPanel.Visibility = Visibility.Collapsed;
        Control<Button>("SettingsButton").Focus();
    }

    private void UpdateVersionLabel()
    {
        int fields = CurrentVersion.Revision > 0 ? 4 : CurrentVersion.Build > 0 ? 3 : 2;
        Control<TextBlock>("VersionLabel").Text = UiText.T("Версия ", "Version ") + CurrentVersion.ToString(fields);
    }

    private void SaveSettings()
    {
        bool english = (string)languageSelect.SelectedValue == "en";
        var next = new Preferences {
            Language = english ? "en" : "ru",
            AutoCheckUpdates = autoUpdateCheck.IsChecked == true
        };
        try
        {
            if (english != UiText.English || next.AutoCheckUpdates != preferences.AutoCheckUpdates)
            {
                WriteSetting(PreferencesPath, next);
                preferences = next;
                if (english != UiText.English)
                    updateError = null;
                UiText.English = english;
                UiText.Apply(Application.Current.Resources);
                UpdateVersionLabel();
                Refresh();
                RenderUpdateStatus();
            }
            CloseSettings();
        }
        catch (IOException) { ShowSaveError(UiText.T("Не удалось сохранить настройки.", "Could not save settings.")); }
        catch (UnauthorizedAccessException) { ShowSaveError(UiText.T("Не удалось сохранить настройки.", "Could not save settings.")); }
    }

    private void RenderUpdateStatus()
    {
        Control<Button>("CheckUpdateButton").IsEnabled = !checkingUpdate;
        Version latest = latestTag == null ? null : GitHubReleases.VersionFromTag(latestTag);
        bool newer = latest != null && latest > CurrentVersion;
        releaseButton.Visibility = !checkingUpdate && latestTag != null && (newer || latest == null)
            ? Visibility.Visible : Visibility.Collapsed;
        updateNotice.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        if (newer)
            updateNotice.Content = string.Format(UiText.T("Обновление {0}", "Update {0}"), latestTag);

        if (checkingUpdate)
            updateStatus.Text = UiText.T("Проверяем релизы на GitHub…", "Checking GitHub releases…");
        else if (updateError != null)
            updateStatus.Text = updateError;
        else if (newer)
            updateStatus.Text = string.Format(UiText.T("Доступно обновление {0}.", "Update {0} is available."), latestTag);
        else if (latest != null)
            updateStatus.Text = UiText.T("У вас актуальная версия.", "You’re up to date.");
        else if (latestTag != null)
            updateStatus.Text = string.Format(UiText.T("Последний релиз: {0}. Тег не удалось сравнить с версией приложения.",
                "Latest release: {0}. Its tag could not be compared with the app version."), latestTag);
        else
            updateStatus.Text = "";
        updateStatus.Visibility = updateStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task CheckUpdate(bool manual)
    {
        if (checkingUpdate)
            return;

        checkingUpdate = true;
        updateError = null;
        RenderUpdateStatus();
        try
        {
            string tag = await Task.Run(() => GitHubReleases.LatestTag());
            if (manual || preferences.AutoCheckUpdates)
                latestTag = tag;
        }
        catch (PickerException ex) { if (manual) updateError = ex.Message; }
        catch (IOException) { if (manual) updateError = UiText.T("Соединение с GitHub прервалось.", "The connection to GitHub was interrupted."); }
        catch (ArgumentException) { if (manual) updateError = UiText.T("Не удалось прочитать ответ GitHub.", "Could not read the GitHub response."); }
        catch (InvalidOperationException) { if (manual) updateError = UiText.T("Не удалось прочитать ответ GitHub.", "Could not read the GitHub response."); }
        finally
        {
            checkingUpdate = false;
            RenderUpdateStatus();
        }
    }

    private void OpenRelease()
    {
        if (latestTag == null)
            return;
        try
        {
            using (Process.Start(new ProcessStartInfo(GitHubReleases.ReleaseUrl(latestTag)) { UseShellExecute = true })) { }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            updateError = UiText.T("Не удалось открыть браузер.", "Could not open the browser.");
            RenderUpdateStatus();
            if (settingsPanel.Visibility != Visibility.Visible)
                status.Text = updateError;
        }
    }

    private static string FindLauncher()
    {
        foreach (var location in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string root = Environment.GetFolderPath(location);
            if (root.Length == 0)
                continue;
            string candidate = Path.Combine(root, "Battle.net", "Battle.net Launcher.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private bool EnsureLauncher()
    {
        if (File.Exists(launcher))
            return true;
        var dialog = new OpenFileDialog {
            Title = UiText.T("Укажите файл лаунчера Battle.net", "Choose the Battle.net launcher file"),
            Filter = UiText.T("Лаунчер Battle.net|Battle.net Launcher.exe;Battle.net.exe", "Battle.net launcher|Battle.net Launcher.exe;Battle.net.exe"),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(window) != true)
            return false;
        launcher = dialog.FileName;
        return true;
    }

    private void OpenWithoutSelecting()
    {
        if (!EnsureLauncher())
            return;
        try { LauncherConfig.Start(launcher); }
        catch (IOException ex) { status.Text = ex.Message; }
    }

    private async Task LaunchSelected()
    {
        Account account = Selected;
        if (busy || showHidden || account == null || !EnsureLauncher())
            return;
        busy = true;
        accounts.IsEnabled = Control<Button>("RefreshButton").IsEnabled = false;
        UpdateActions();
        status.Text = UiText.T("Открываем Battle.net…", "Opening Battle.net…");
        string error = null;
        try
        {
            await Task.Run(() =>
            {
                var config = LauncherConfig.Read();
                if (!config.Logins.Contains(account.Login, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException(UiText.T("Аккаунт больше не сохранён. Нажмите «Обновить».", "This account is no longer saved. Click Refresh."));
                if (!StringComparer.OrdinalIgnoreCase.Equals(config.Logins[0], account.Login))
                {
                    LauncherConfig.CloseClients();
                    config = LauncherConfig.Read();
                }
                config.StartWith(account.Login, launcher);
            });
        }
        catch (InvalidDataException ex) { error = ex.Message; }
        catch (PickerException ex) { error = ex.Message; }
        catch (IOException) { error = UiText.T("Не удалось обновить настройки Battle.net. Закройте лаунчер и попробуйте снова.", "Could not update Battle.net settings. Close the launcher and try again."); }
        catch (UnauthorizedAccessException) { error = UiText.T("Windows не дал изменить настройки Battle.net.", "Windows denied permission to update Battle.net settings."); }
        catch (System.ComponentModel.Win32Exception) { error = UiText.T("Закройте Battle.net через значок рядом с часами и попробуйте снова.", "Quit Battle.net from its system tray icon and try again."); }
        finally
        {
            busy = false;
            accounts.IsEnabled = Control<Button>("RefreshButton").IsEnabled = true;
            UpdateActions();
        }
        if (error == null)
            window.Close();
        else
            status.Text = error;
    }
}
