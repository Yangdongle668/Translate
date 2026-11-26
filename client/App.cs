using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace Translate
{
    internal static class App
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                var application = new Application();
                Window window;
                using (var layout = Assembly.GetExecutingAssembly().GetManifestResourceStream("Translate.MainWindow.xaml"))
                    window = (Window)XamlReader.Load(layout);

                using (var api = new ServerClient())
                {
                    new TranslationWindow(window, api);
                    application.Run(window);
                }
            }
            catch (Exception error)
            {
                MessageBox.Show("无法启动 Translate。\n" + error.Message, "Translate", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.ExitCode = 1;
            }
        }
    }

    internal sealed class TranslationWindow
    {
        private static readonly Language[] Languages =
        {
            new Language("简体中文", "zh-CN"), new Language("繁体中文", "zh-TW"),
            new Language("英语", "en"), new Language("日语", "ja"), new Language("韩语", "ko"),
            new Language("法语", "fr"), new Language("德语", "de"), new Language("西班牙语", "es"),
            new Language("意大利语", "it"), new Language("葡萄牙语", "pt"), new Language("俄语", "ru"),
            new Language("阿拉伯语", "ar"), new Language("印地语", "hi"), new Language("泰语", "th"),
            new Language("越南语", "vi"), new Language("印度尼西亚语", "id"), new Language("土耳其语", "tr")
        };
        private static readonly string AddressFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Translate", "server.txt");

        private readonly Window window;
        private readonly ServerClient api;
        private readonly TextBox address;
        private readonly TextBox source;
        private readonly TextBox result;
        private readonly ComboBox sourceLanguage;
        private readonly ComboBox targetLanguage;
        private readonly TextBlock connection;
        private readonly TextBlock message;
        private readonly Button translate;
        private readonly Button copy;
        private readonly DispatcherTimer checkTimer;
        private CancellationTokenSource checkRequest;
        private CancellationTokenSource translationRequest;
        private string savedAddress;
        private bool closing;

        internal TranslationWindow(Window window, ServerClient api)
        {
            this.window = window;
            this.api = api;
            address = (TextBox)window.FindName("ServerAddress");
            source = (TextBox)window.FindName("SourceText");
            result = (TextBox)window.FindName("ResultText");
            sourceLanguage = (ComboBox)window.FindName("SourceLanguage");
            targetLanguage = (ComboBox)window.FindName("TargetLanguage");
            connection = (TextBlock)window.FindName("ConnectionStatus");
            message = (TextBlock)window.FindName("Message");
            translate = (Button)window.FindName("TranslateButton");
            copy = (Button)window.FindName("CopyButton");

            sourceLanguage.Items.Add(new Language("自动识别", "auto"));
            foreach (var language in Languages) sourceLanguage.Items.Add(language);
            targetLanguage.ItemsSource = Languages;
            sourceLanguage.SelectedIndex = 0;
            targetLanguage.SelectedIndex = 0;

            savedAddress = ReadAddress();
            address.Text = savedAddress;
            checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            checkTimer.Tick += CheckServer;
            address.TextChanged += (sender, args) => ScheduleServerCheck();
            source.TextChanged += (sender, args) => UpdateButtons();
            translate.Click += async (sender, args) => await TranslateAsync();
            copy.Click += CopyResult;
            window.PreviewKeyDown += async (sender, args) =>
            {
                if (args.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control) return;
                args.Handled = true;
                await TranslateAsync();
            };
            window.Loaded += (sender, args) =>
            {
                ScheduleServerCheck();
                if (string.IsNullOrWhiteSpace(address.Text)) address.Focus();
                else source.Focus();
            };
            window.Closing += (sender, args) =>
            {
                closing = true;
                StopServerCheck();
                if (translationRequest != null) translationRequest.Cancel();
            };
        }

        private void SetStatus(TextBlock label, string text, string color)
        {
            label.Text = text;
            label.Foreground = (Brush)window.FindResource(color);
        }

        private void UpdateButtons()
        {
            Uri origin;
            var idle = translationRequest == null;
            translate.IsEnabled = idle && !string.IsNullOrWhiteSpace(source.Text) &&
                ServerClient.TryParseAddress(address.Text, out origin);
            copy.IsEnabled = idle && result.Text.Length > 0;
        }

        private void StopServerCheck()
        {
            checkTimer.Stop();
            if (checkRequest == null) return;
            var previous = checkRequest;
            checkRequest = null;
            previous.Cancel();
        }

        private void ScheduleServerCheck()
        {
            StopServerCheck();
            UpdateButtons();
            if (string.IsNullOrWhiteSpace(address.Text))
            {
                SetStatus(connection, "等待服务器地址", "Muted");
                return;
            }

            SetStatus(connection, "正在检查", "Muted");
            checkTimer.Start();
        }

        private async void CheckServer(object sender, EventArgs args)
        {
            checkTimer.Stop();
            Uri origin;
            if (!ServerClient.TryParseAddress(address.Text, out origin))
            {
                SetStatus(connection, "地址格式无效", "Danger");
                return;
            }

            using (var request = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
            {
                checkRequest = request;
                var online = false;
                try { online = await api.IsOnlineAsync(origin, request.Token); }
                catch (TaskCanceledException) { }
                catch (HttpRequestException) { }
                catch (SerializationException) { }

                if (checkRequest != request) return;
                checkRequest = null;
                SetStatus(connection, online ? "服务已连接" : "连接失败", online ? "Success" : "Danger");
                if (online) SaveAddress();
            }
        }

        private async Task TranslateAsync()
        {
            if (translationRequest != null) return;
            Uri origin;
            if (!ServerClient.TryParseAddress(address.Text, out origin))
            {
                SetStatus(message, "请输入服务器的 IP 地址。", "Danger");
                address.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(source.Text))
            {
                SetStatus(message, "请输入要翻译的文本。", "Danger");
                source.Focus();
                return;
            }

            var sourceCode = ((Language)sourceLanguage.SelectedItem).Code;
            var targetCode = ((Language)targetLanguage.SelectedItem).Code;
            using (var request = new CancellationTokenSource())
            {
                translationRequest = request;
                result.Clear();
                SetStatus(message, "正在翻译…", "Muted");
                SetBusy(true);
                try
                {
                    var response = await api.TranslateAsync(origin, source.Text, sourceCode, targetCode, request.Token);
                    if (closing) return;
                    result.Text = response.Text;
                    SetStatus(connection, "服务已连接", "Success");
                    SaveAddress();
                    var language = Array.Find(Languages,
                        item => string.Equals(item.Code, response.Source, StringComparison.OrdinalIgnoreCase));
                    var name = language == null ? response.Source : language.Name;
                    SetStatus(message, string.IsNullOrEmpty(name) ? "翻译完成" : "源语言 · " + name, "Success");
                }
                catch (TranslationException error)
                {
                    if (!closing) SetStatus(message, error.Message, "Danger");
                }
                catch (HttpRequestException)
                {
                    if (!closing)
                    {
                        SetStatus(connection, "连接失败", "Danger");
                        SetStatus(message, "无法连接服务器。请检查 IP、端口和防火墙。", "Danger");
                    }
                }
                catch (TaskCanceledException)
                {
                    if (!closing) SetStatus(message, "等待服务器响应超时，请重试。", "Danger");
                }
                catch (SerializationException)
                {
                    if (!closing) SetStatus(message, "服务器响应无法识别，请检查部署地址。", "Danger");
                }
                finally
                {
                    translationRequest = null;
                    if (!closing) SetBusy(false);
                }
            }
        }

        private void SetBusy(bool busy)
        {
            address.IsReadOnly = busy;
            source.IsReadOnly = busy;
            sourceLanguage.IsEnabled = !busy;
            targetLanguage.IsEnabled = !busy;
            UpdateButtons();
        }

        private void CopyResult(object sender, RoutedEventArgs args)
        {
            try
            {
                Clipboard.SetText(result.Text);
                SetStatus(message, "译文已复制", "Success");
            }
            catch (ExternalException)
            {
                SetStatus(message, "剪贴板正在使用，请重试或手动复制。", "Danger");
            }
        }

        private static string ReadAddress()
        {
            try { return File.ReadAllText(AddressFile); }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }

        private void SaveAddress()
        {
            var value = address.Text.Trim();
            if (value == savedAddress) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AddressFile));
                File.WriteAllText(AddressFile, value);
                savedAddress = value;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class Language
        {
            internal Language(string name, string code)
            {
                Name = name;
                Code = code;
            }

            public string Name { get; private set; }
            public string Code { get; private set; }
        }
    }
}
