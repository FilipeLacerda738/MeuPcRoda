using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

[assembly: AssemblyTitle("MeuPcRoda")]
[assembly: AssemblyDescription("Assistente de compatibilidade de jogos para Windows")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyCopyright("MeuPcRoda")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace MeuPcRoda
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && (args[0] == "--self-test" || args[0] == "--integration-test")) return Diagnostics.Run(args[1], args[0] == "--integration-test");
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\MeuPcRoda.Desktop", out firstInstance))
            {
                if (!firstInstance)
                {
                    foreach (var process in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
                    {
                        using (process)
                        {
                            if (process.Id == Process.GetCurrentProcess().Id || process.MainWindowHandle == IntPtr.Zero) continue;
                            ShowWindow(process.MainWindowHandle, 9); SetForegroundWindow(process.MainWindowHandle); break;
                        }
                    }
                    return 0;
                }
                return RunApplication();
            }
        }
        private static int RunApplication()
        {
            var app = new Application();
            app.DispatcherUnhandledException += (sender, e) => {
                MessageBox.Show("O aplicativo encontrou um problema. Feche e abra o MeuPcRoda novamente. Seus arquivos pessoais não foram alterados.", "MeuPcRoda", MessageBoxButton.OK, MessageBoxImage.Information);
                e.Handled = true;
            };
            try
            {
                var controller = new MainController();
                return app.Run(controller.Window);
            }
            catch (Exception)
            {
                MessageBox.Show("Não foi possível abrir o MeuPcRoda. Confirme se o .NET Framework 4.8 está instalado no Windows.", "MeuPcRoda", MessageBoxButton.OK, MessageBoxImage.Information);
                return 1;
            }
        }
    }

    internal sealed class MainController
    {
        public readonly Window Window;
        private readonly SteamClient steam = new SteamClient();
        private Hardware hardware;
        private Game game;
        private bool recommended;
        private int generation;
        private CancellationTokenSource network;
        private bool closed;
        private static readonly Brush Green = Color("#C2F76A"), Muted = Color("#A2ADBD");
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public MainController()
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) Window = (Window)XamlReader.Load(resource);
            Window.Width = Math.Max(Window.MinWidth, Math.Min(Window.Width, SystemParameters.WorkArea.Width - 32));
            Window.Height = Math.Max(Window.MinHeight, Math.Min(Window.Height, SystemParameters.WorkArea.Height - 32));
            Window.SourceInitialized += delegate {
                try { int on = 1; DwmSetWindowAttribute(new WindowInteropHelper(Window).Handle, 20, ref on, 4); } catch (DllNotFoundException) { }
            };
            // Start on the first action instead of animating a text caret while idle.
            Window.ContentRendered += delegate { Find<Button>("DetectButton").Focus(); };
            Window.Closed += delegate { closed = true; if (network != null) { network.Cancel(); network.Dispose(); } steam.Dispose(); };
            Find<Button>("DetectButton").Click += async (s, e) => await Detect();
            Find<Button>("SearchButton").Click += async (s, e) => await Search();
            Find<TextBox>("SearchInput").KeyDown += async (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Search(); } };
            Find<TextBox>("SearchInput").TextChanged += (s, e) => Find<TextBlock>("SearchPlaceholder").Visibility = string.IsNullOrEmpty(Find<TextBox>("SearchInput").Text) ? Visibility.Visible : Visibility.Collapsed;
            Find<Border>("SearchSurface").MouseDown += (s, e) => { Find<TextBox>("SearchInput").Focus(); };
            Find<TextBox>("SearchInput").GotKeyboardFocus += (s, e) => Find<Border>("SearchSurface").BorderBrush = Green;
            Find<TextBox>("SearchInput").LostKeyboardFocus += (s, e) => Find<Border>("SearchSurface").BorderBrush = Color("#465260");
            Suggest("CyberpunkSuggestion", "Cyberpunk 2077"); Suggest("EldenSuggestion", "Elden Ring"); Suggest("StardewSuggestion", "Stardew Valley");
            Find<Button>("MinimumButton").Click += (s, e) => { recommended = false; RenderGame(); };
            Find<Button>("RecommendedButton").Click += (s, e) => { recommended = true; RenderGame(); };
            Find<Button>("SourceButton").Click += (s, e) => { if (game != null) Open("https://store.steampowered.com/app/" + game.Id + "/"); };
        }
        private T Find<T>(string name) where T : class { return (T)Window.FindName(name); }
        private static SolidColorBrush Color(string color)
        {
            var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush;
        }
        private static TextBlock Text(string value, double size, Brush color)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.5 };
        }
        private static string Labels(string state) { return state == "pass" ? "Atende a este item" : state == "fail" ? "Abaixo do requisito" : "Precisa verificar"; }
        private void Status(string message) { if (!closed) Find<TextBlock>("Status").Text = message; }
        private void Suggest(string name, string query)
        {
            Find<Button>(name).Click += async (s, e) => { Find<TextBox>("SearchInput").Text = query; await Search(); };
        }
        private CancellationToken BeginNetwork()
        {
            if (network != null) { network.Cancel(); network.Dispose(); }
            network = new CancellationTokenSource();
            generation++;
            return network.Token;
        }
        private async Task Detect()
        {
            var button = Find<Button>("DetectButton"); button.IsEnabled = false; button.Content = "Identificando suas peças…";
            Find<TextBlock>("HardwareStatus").Text = "Consultando o Windows. Aguarde um instante…";
            try
            {
                Hardware detected = await Task.Run(() => HardwareReader.Read());
                if (closed) return;
                hardware = detected;
                var list = Find<StackPanel>("HardwareList"); list.Children.Clear();
                HardwareItem(list, "PROCESSADOR", hardware.Cpus.Count > 0 ? string.Join(", ", hardware.Cpus) : "Não identificado");
                HardwareItem(list, "PLACA DE VÍDEO", hardware.Gpus.Count > 0 ? string.Join(", ", hardware.Gpus) : "Não identificada");
                HardwareItem(list, "MEMÓRIA RAM", hardware.Ram.HasValue ? Compatibility.Amount(hardware.Ram) : "Não identificada");
                HardwareItem(list, "PLACA-MÃE", hardware.Board); HardwareItem(list, "SISTEMA", hardware.Os);
                foreach (Disk disk in hardware.Disks) HardwareItem(list, "ARMAZENAMENTO · " + disk.Name, Compatibility.Amount(disk.Free) + " livres de " + Compatibility.Amount(disk.Total));
                Find<TextBlock>("HardwareStatus").Text = hardware.Warnings.Count > 0 ? "Alguns dados não foram lidos: " + string.Join(", ", hardware.Warnings) + ". A comparação desses itens ficará pendente." : "Peças identificadas. Seu PC está pronto para comparar.";
                if (game != null) RenderAnalysis();
            }
            catch (Exception) { if (!closed) Find<TextBlock>("HardwareStatus").Text = "Não conseguimos ler seus componentes. Verifique o serviço de gerenciamento do Windows e tente novamente."; }
            finally { if (!closed) { button.IsEnabled = true; button.Content = hardware == null ? "Analisar meu computador  ↗" : "Analisar novamente  ↗"; } }
        }
        private static void HardwareItem(StackPanel panel, string label, string value)
        {
            var content = new StackPanel(); content.Children.Add(Text(label, 10, Muted));
            var text = Text(value, 13, Brushes.White); text.Margin = new Thickness(0, 5, 0, 0); content.Children.Add(text);
            panel.Children.Add(new Border { Child = content, BorderBrush = Color("#29313C"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 13, 0, 13) });
        }
        private async Task Search()
        {
            string query = Find<TextBox>("SearchInput").Text.Trim();
            if (query.Length < 2) { Status("Digite pelo menos 2 letras do nome do jogo."); Find<TextBox>("SearchInput").Focus(); return; }
            if (query.Length > 100) { Status("Use até 100 caracteres para buscar."); return; }
            CancellationToken token = BeginNetwork(); int version = generation;
            var button = Find<Button>("SearchButton"); button.IsEnabled = false; button.Content = "Buscando…";
            Status("Procurando jogos na Steam…");
            try
            {
                var items = await steam.Search(query, token);
                if (closed || version != generation) return;
                var panel = Find<StackPanel>("SearchResults"); panel.Children.Clear();
                Find<Border>("EmptyState").Visibility = Visibility.Collapsed;
                foreach (GameItem item in items)
                {
                    string id = item.Id;
                    var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition()); content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var title = Text(item.Name, 14, Brushes.White); title.Margin = new Thickness(0, 0, 14, 0); content.Children.Add(title);
                    var arrow = Text("→", 18, Green); Grid.SetColumn(arrow, 1); content.Children.Add(arrow);
                    var resultButton = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(16, 12, 16, 12) };
                    AutomationProperties.SetName(resultButton, "Selecionar " + item.Name);
                    resultButton.Click += async (s, e) => await SelectGame(id);
                    panel.Children.Add(resultButton);
                }
                Status(items.Count == 0 ? "Nenhum jogo encontrado. Confira o nome ou tente outro título." : "Selecione o jogo. A lista também pode incluir extras e trilhas sonoras.");
            }
            catch (OperationCanceledException) { if (!closed && version == generation) Status("A consulta demorou mais que o esperado. Verifique a conexão e tente novamente."); }
            catch (Exception) { if (!closed && version == generation) Status("Não foi possível consultar a Steam. Verifique sua conexão e tente novamente."); }
            finally { if (!closed && version == generation) { button.IsEnabled = true; button.Content = "Buscar  →"; } }
        }
        private async Task SelectGame(string id)
        {
            CancellationToken token = BeginNetwork(); int version = generation;
            Find<Button>("SearchButton").IsEnabled = false;
            Status("Carregando requisitos do jogo…");
            try
            {
                Game selected = await steam.Details(id, token);
                if (closed || version != generation) return;
                game = selected; recommended = false;
                Find<StackPanel>("SearchResults").Children.Clear();
                Find<Border>("EmptyState").Visibility = Visibility.Collapsed;
                RenderGame();
                Status(hardware == null ? "Jogo selecionado! Clique em Analisar meu computador para comparar." : "Jogo selecionado! Confira a comparação abaixo.");
            }
            catch (InvalidOperationException e) { if (!closed && version == generation) Status(e.Message); }
            catch (OperationCanceledException) { if (!closed && version == generation) Status("A consulta demorou mais que o esperado. Tente novamente."); }
            catch (Exception) { if (!closed && version == generation) Status("Não foi possível carregar os requisitos. Verifique sua conexão e tente novamente."); }
            finally { if (!closed && version == generation) { Find<Button>("SearchButton").IsEnabled = true; Find<Button>("SearchButton").Content = "Buscar  →"; } }
        }
        private void RenderGame()
        {
            if (game == null) return;
            Find<Border>("GameCard").Visibility = Visibility.Visible;
            Find<TextBlock>("GameTitle").Text = game.Name;
            Find<TextBlock>("GameDate").Text = "Fonte: publicadora na Steam · Consulta em " + game.CheckedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            Requirement req = recommended ? game.Recommended : game.Minimum;
            Find<TextBlock>("RequirementsText").Text = string.IsNullOrWhiteSpace(req.Text) ? "A publicadora não informou estes requisitos." : req.Text;
            Find<TextBlock>("RequirementsHint").Text = string.IsNullOrWhiteSpace(req.Text) ? "Estes requisitos não foram publicados. Os itens sem informação ficam pendentes." : recommended ? "Os recomendados são a referência da publicadora para uma experiência melhor. Não representam uma garantia de FPS." : "Os mínimos são a referência de entrada da publicadora. Atendê-los não garante uma qualidade gráfica ou FPS específicos.";
            Find<Button>("MinimumButton").Background = recommended ? Color("#202630") : Green;
            Find<Button>("MinimumButton").Foreground = recommended ? Brushes.White : Color("#15200C");
            Find<Button>("RecommendedButton").Background = recommended ? Green : Color("#202630");
            Find<Button>("RecommendedButton").Foreground = recommended ? Color("#15200C") : Brushes.White;
            RenderAnalysis();
        }
        private void RenderAnalysis()
        {
            if (hardware == null || game == null) return;
            Requirement req = recommended ? game.Recommended : game.Minimum;
            var rows = Compatibility.Compare(hardware, req);
            bool fail = rows.Any(x => x.State == "fail");
            Find<Border>("AnalysisCard").Visibility = Visibility.Visible;
            Find<TextBlock>("Verdict").Text = fail ? "Há requisitos abaixo do necessário." : "Ainda precisamos conferir alguns itens.";
            Find<TextBlock>("AnalysisHint").Text = fail ? "Confira o que está abaixo dos requisitos e quais componentes ainda precisam de verificação." : "Os itens aprovados estão indicados abaixo. A análise ainda não confirma se o jogo vai rodar, pois há itens pendentes.";
            var list = Find<StackPanel>("ComparisonList"); list.Children.Clear();
            foreach (ResultRow row in rows)
            {
                var content = new StackPanel();
                var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = Text(row.Label, 14, Brushes.White); label.FontWeight = FontWeights.SemiBold; top.Children.Add(label);
                var badge = Text(Labels(row.State), 11, row.State == "pass" ? Green : row.State == "fail" ? Color("#FF9C90") : Color("#F0C36A")); badge.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(badge, 1); top.Children.Add(badge);
                content.Children.Add(top);
                var actual = Text("Seu PC: " + row.Actual, 13, Color("#D2D9E3")); actual.Margin = new Thickness(0, 8, 0, 3); content.Children.Add(actual);
                content.Children.Add(Text("O jogo pede: " + row.Required, 12, Muted));
                list.Children.Add(new Border { Child = content, Padding = new Thickness(0, 16, 0, 16), BorderBrush = Color("#29313C"), BorderThickness = new Thickness(0, 1, 0, 0) });
            }
            Find<Border>("UpgradeCard").Visibility = Visibility.Visible;
            string query = Compatibility.ShoppingQuery(req, rows);
            var tips = new List<string>();
            if (rows.Any(x => x.Key == "ram" && x.State == "fail")) tips.Add("A RAM está abaixo do requisito. Confira o tipo de memória, os slots e a capacidade da placa-mãe antes de ampliar.");
            if (rows.Any(x => x.Key == "storage" && x.State == "fail")) tips.Add("Libere espaço em uma unidade ou avalie armazenamento adicional. O espaço de discos separados não é somado.");
            tips.Add(query.Length > 0 ? "Você pode começar uma pesquisa nas lojas usando um dos requisitos do jogo. Isso ainda não é uma recomendação de compra para seu PC." : "Ainda não há um modelo específico de peça para pesquisar. Confira os itens pendentes antes de considerar uma compra.");
            Find<TextBlock>("UpgradeText").Text = string.Join("\n\n", tips);
            var shops = Find<WrapPanel>("ShopButtons"); shops.Children.Clear();
            if (query.Length > 0)
            {
                Shop(shops, "KaBuM! ↗", "https://www.kabum.com.br/busca/" + Uri.EscapeDataString(query));
                Shop(shops, "Amazon ↗", "https://www.amazon.com.br/s?k=" + Uri.EscapeDataString(query));
                Shop(shops, "Mercado Livre ↗", "https://lista.mercadolivre.com.br/" + Uri.EscapeDataString(query));
            }
        }
        private void Shop(WrapPanel panel, string name, string url)
        {
            var button = new Button { Content = name, Margin = new Thickness(0, 0, 8, 8), Foreground = Green };
            button.Click += (s, e) => Open(url); panel.Children.Add(button);
        }
        private void Open(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https") return;
            string[] allowed = { "store.steampowered.com", "www.kabum.com.br", "www.amazon.com.br", "lista.mercadolivre.com.br" };
            if (!allowed.Contains(uri.Host)) return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception) { Status("Não foi possível abrir o navegador padrão do Windows."); }
        }
    }
}
