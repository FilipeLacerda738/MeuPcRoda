using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace MeuPcRoda
{
    internal static class Diagnostics
    {
        private static void Check(bool condition, string name, List<string> passed)
        {
            if (!condition) throw new InvalidOperationException("Falhou: " + name);
            passed.Add(name);
        }
        public static int Run(string reportPath, bool integration)
        {
            var passed = new List<string>();
            try
            {
                var r = Compatibility.Parse("<strong>Minimum:</strong><br><ul><li><strong>OS *:</strong> Windows 10</li><li><strong>Memory:</strong> 8 GB RAM</li><li><strong>Storage:</strong> 50 GB available space</li><li><strong>Graphics:</strong> NVIDIA GeForce GTX 1060</li></ul>");
                Check(r.Ram == 8 && r.Storage == 50 && r.Os == "Windows 10" && r.Gpu == "NVIDIA GeForce GTX 1060", "Extração dos requisitos da Steam", passed);
                Check(Compatibility.Gigabytes("512 MB RAM") == .5 && Compatibility.Gigabytes("1,5 GB") == 1.5 && Compatibility.Gigabytes("1 TB") == 1024, "Conversão de unidades e decimais", passed);
                var h = new Hardware { Ram = 8 }; h.Cpus.Add("Intel(R) Core(TM) i5-12400 CPU @ 2.5GHz"); h.Gpus.Add("NVIDIA GeForce GTX 1060"); h.Disks.Add(new Disk { Free = 40 }); h.Disks.Add(new Disk { Free = 30 });
                var rows = Compatibility.Compare(h, r);
                Check(rows.Single(x => x.Key == "storage").State == "fail", "Espaço não é somado entre discos", passed);
                Check(rows.Single(x => x.Key == "gpu").State == "pass", "Modelo explícito de GPU", passed);
                Check(Compatibility.Exact(h.Cpus[0], "Intel Core i5-12400") && !Compatibility.Exact(h.Gpus[0], "NVIDIA GeForce GTX 1060 Ti"), "Sufixo de GPU e normalização de CPU", passed);
                r.Ram = 16; Check(Compatibility.Compare(h, r).Single(x => x.Key == "ram").State == "fail", "RAM insuficiente", passed);
                Check(Compatibility.Compare(new Hardware(), new Requirement()).All(x => x.State == "unknown"), "Dados ausentes não aprovam componentes", passed);
                h.Disks.Clear(); h.Disks.Add(new Disk { Free = 0 }); r.Storage = 1;
                Check(Compatibility.Compare(h, r).Single(x => x.Key == "storage").State == "fail", "Disco sem espaço é reprovado", passed);
                Check(Compatibility.ShoppingQuery(new Requirement { Gpu = "256 MB video memory" }, new List<ResultRow>()) == "", "Exigência genérica não sugere modelo de compra", passed);
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                {
                    var window = (Window)XamlReader.Load(resource);
                    Check(window.FindName("DetectButton") is Button && window.FindName("SearchInput") is TextBox && ((TextBox)window.FindName("SearchInput")).MinHeight >= 48, "Janela WPF e campo de busca carregados do próprio EXE", passed);
                    window.Close();
                }
                if (integration)
                {
                    var actual = HardwareReader.Read();
                    Check(actual.Cpus.Count > 0 && actual.Ram.HasValue && actual.Disks.Count > 0, "Leitura real de hardware via WMI, sem processo auxiliar", passed);
                    using (var steam = new SteamClient())
                    {
                        var games = steam.Search("Stardew", CancellationToken.None).GetAwaiter().GetResult();
                        Check(games.Any(x => x.Id == "413150"), "Busca real na Steam", passed);
                        var game = steam.Details("413150", CancellationToken.None).GetAwaiter().GetResult();
                        Check(game.Minimum.Ram.HasValue && Compatibility.Compare(actual, game.Minimum).Count == 5, "Requisitos reais e comparação", passed);
                        bool rejected = false;
                        try { steam.Details("440820", CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidOperationException) { rejected = true; }
                        Check(rejected, "Trilha sonora rejeitada como jogo", passed);
                    }
                }
                File.WriteAllText(reportPath, new JavaScriptSerializer().Serialize(new { success = true, passed = passed, inventorySaved = false }));
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(reportPath, new JavaScriptSerializer().Serialize(new { success = false, passed = passed, error = error.Message }));
                return 1;
            }
        }
    }
}
