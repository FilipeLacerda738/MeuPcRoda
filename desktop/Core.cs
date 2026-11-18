using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MeuPcRoda
{
    internal sealed class Hardware
    {
        public readonly List<string> Cpus = new List<string>();
        public readonly List<string> Gpus = new List<string>();
        public readonly List<Disk> Disks = new List<Disk>();
        public readonly List<string> Warnings = new List<string>();
        public double? Ram;
        public string Board = "Não identificada", Os = "Não identificado";
    }
    internal sealed class Disk { public string Name; public double Free, Total; }
    internal sealed class Requirement
    {
        public string Text = "", Cpu = "", Gpu = "", Os = "";
        public double? Ram, Storage;
    }
    internal sealed class Game
    {
        public string Id, Name;
        public Requirement Minimum, Recommended;
        public DateTime CheckedAt;
    }
    internal sealed class GameItem { public string Id, Name; }
    internal sealed class ResultRow
    {
        public string Key, Label, Actual, Required, State;
    }

    internal static class HardwareReader
    {
        private static void Query(Hardware hw, string query, string label, Action<ManagementBaseObject> read)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(12);
                    using (var values = searcher.Get())
                        foreach (ManagementBaseObject value in values)
                            using (value) read(value);
                }
            }
            catch (Exception error)
            {
                if (!(error is ManagementException) && !(error is UnauthorizedAccessException) && !(error is System.Runtime.InteropServices.COMException)) throw;
                hw.Warnings.Add(label);
            }
        }
        public static Hardware Read()
        {
            var h = new Hardware();
            Query(h, "SELECT Name FROM Win32_Processor", "processador", x => h.Cpus.Add(Convert.ToString(x["Name"]).Trim()));
            Query(h, "SELECT Name FROM Win32_VideoController", "placa de vídeo", x => h.Gpus.Add(Convert.ToString(x["Name"]).Trim()));
            Query(h, "SELECT Capacity FROM Win32_PhysicalMemory", "memória", x => {
                if (x["Capacity"] != null) h.Ram = (h.Ram ?? 0) + Convert.ToDouble(x["Capacity"]) / 1073741824.0;
            });
            Query(h, "SELECT Manufacturer, Product FROM Win32_BaseBoard", "placa-mãe", x => h.Board = Convert.ToString(x["Manufacturer"]) + " " + Convert.ToString(x["Product"]));
            Query(h, "SELECT Caption, OSArchitecture FROM Win32_OperatingSystem", "Windows", x => h.Os = Convert.ToString(x["Caption"]) + " · " + Convert.ToString(x["OSArchitecture"]));
            Query(h, "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3", "armazenamento", x => {
                if (x["FreeSpace"] != null && x["Size"] != null) h.Disks.Add(new Disk { Name = Convert.ToString(x["DeviceID"]), Free = Convert.ToDouble(x["FreeSpace"]) / 1073741824.0, Total = Convert.ToDouble(x["Size"]) / 1073741824.0 });
            });
            if (h.Cpus.Count == 0 && !h.Ram.HasValue && h.Gpus.Count == 0) throw new InvalidOperationException("O Windows não disponibilizou os componentes. Verifique o serviço de Instrumentação de Gerenciamento do Windows e tente novamente.");
            return h;
        }
    }

    internal static class Compatibility
    {
        public static string Plain(string html)
        {
            string text = Regex.Replace(html ?? "", @"<\s*(br\s*/?|/li|/p)\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]*>", "");
            return WebUtility.HtmlDecode(text).Trim();
        }
        private static string Field(string text, string labels)
        {
            var m = Regex.Match(text, @"^\s*(?:" + labels + @")\s*\*?\s*:\s*([^\r\n]*)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }
        public static double? Gigabytes(string text)
        {
            var m = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(GB|MB|TB)\b", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            double value;
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return null;
            string unit = m.Groups[2].Value.ToUpperInvariant();
            return value * (unit == "MB" ? 1.0 / 1024 : unit == "TB" ? 1024 : 1);
        }
        public static Requirement Parse(string html)
        {
            string text = Plain(html);
            return new Requirement { Text = text, Cpu = Field(text, "Processor|Processador"), Gpu = Field(text, "Graphics|Gráficos|Placa de vídeo"), Os = Field(text, "OS|Sistema operacional"), Ram = Gigabytes(Field(text, "Memory|Memória")), Storage = Gigabytes(Field(text, "Storage|Armazenamento|Hard Drive|Espaço em disco")) };
        }
        private static string Normalize(string value)
        {
            return Regex.Replace(Regex.Replace(value.ToLowerInvariant(), @"\(r\)|\(tm\)|®|™", ""), "[^a-z0-9]", "");
        }
        public static bool Exact(string actual, string required)
        {
            string name = Normalize(Regex.Replace(actual, @"\s+(CPU|Processor)\b.*$|\s+@.*$", "", RegexOptions.IgnoreCase));
            return name.Length >= 9 && Regex.IsMatch(name, "[0-9]") && Regex.Split(required ?? "", @"\s+or\s+|\s+ou\s+|/|;", RegexOptions.IgnoreCase).Any(x => Normalize(x) == name);
        }
        public static string Amount(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) + " GB" : "Não informado";
        }
        private static ResultRow Numeric(string key, string label, double? actual, double? needed, string description)
        {
            return new ResultRow { Key = key, Label = label, Actual = description, Required = Amount(needed), State = !actual.HasValue || !needed.HasValue ? "unknown" : actual.Value >= needed.Value ? "pass" : "fail" };
        }
        public static List<ResultRow> Compare(Hardware h, Requirement r)
        {
            double? free = h.Disks.Count == 0 ? (double?)null : h.Disks.Max(x => x.Free);
            return new List<ResultRow> {
                new ResultRow { Key="cpu", Label="Processador", Actual=h.Cpus.Count>0 ? string.Join(", ",h.Cpus) : "Não identificado", Required=Missing(r.Cpu), State=h.Cpus.Any(x=>Exact(x,r.Cpu)) ? "pass" : "unknown" },
                new ResultRow { Key="gpu", Label="Placa de vídeo", Actual=h.Gpus.Count>0 ? string.Join(", ",h.Gpus) : "Não identificada", Required=Missing(r.Gpu), State=h.Gpus.Any(x=>Exact(x,r.Gpu)) ? "pass" : "unknown" },
                Numeric("ram","Memória RAM",h.Ram,r.Ram,h.Ram.HasValue ? Amount(h.Ram)+" instalados" : "Não identificada"),
                Numeric("storage","Espaço disponível",free,r.Storage,free.HasValue ? Amount(free)+" na unidade com mais espaço" : "Não identificado"),
                new ResultRow { Key="os", Label="Windows", Actual=h.Os, Required=Missing(r.Os), State="unknown" }
            };
        }
        private static string Missing(string value) { return string.IsNullOrWhiteSpace(value) ? "Não informado" : value; }
        public static string ShoppingQuery(Requirement r, List<ResultRow> rows)
        {
            if (rows.Any(x => x.Key == "ram" && x.State == "fail")) return "memória RAM " + Amount(r.Ram);
            var m = Regex.Match(r.Gpu, @"(?:GeForce\s+)?(?:GTX|RTX|GT)\s*\d{3,4}(?:\s*(?:Ti|SUPER))?|(?:Radeon\s+)?(?:RX\s*\d{3,4}(?:\s*(?:XTX|XT))?|R[579]\s+\d{3})|(?:Intel\s+)?Arc\s+[AB]\d{3}", RegexOptions.IgnoreCase);
            return m.Success ? "placa de vídeo " + m.Value : "";
        }
    }

    internal sealed class SteamClient : IDisposable
    {
        private readonly HttpClient client;
        private readonly Dictionary<string, Game> games = new Dictionary<string, Game>();
        private readonly object cacheLock = new object();
        internal static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        internal static object Value(Dictionary<string, object> data, string key) { object value; return data.TryGetValue(key, out value) ? value : null; }
        internal static string Text(Dictionary<string, object> data, string key) { return Convert.ToString(Value(data, key), CultureInfo.InvariantCulture); }
        public SteamClient()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            client.BaseAddress = new Uri("https://store.steampowered.com/");
            client.Timeout = TimeSpan.FromSeconds(18);
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MeuPcRoda/1.0");
        }
        private async Task<Dictionary<string, object>> Get(string path, CancellationToken token)
        {
            using (var response = await client.GetAsync(path, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return Map(new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.DeserializeObject(json));
            }
        }
        public async Task<List<GameItem>> Search(string query, CancellationToken token)
        {
            var data = await Get("api/storesearch/?term=" + Uri.EscapeDataString(query) + "&l=portuguese&cc=BR", token).ConfigureAwait(false);
            var items = Value(data, "items") as object[] ?? new object[0];
            return items.Select(Map).Where(x => true.Equals(Value(Map(Value(x, "platforms")), "windows"))).Take(12).Select(x => new GameItem { Id = Text(x, "id"), Name = Text(x, "name") }).ToList();
        }
        public async Task<Game> Details(string id, CancellationToken token)
        {
            if (!Regex.IsMatch(id, @"^\d{1,10}$")) throw new InvalidOperationException("Escolha um jogo válido.");
            Game cached;
            lock (cacheLock) if (games.TryGetValue(id, out cached) && DateTime.UtcNow - cached.CheckedAt < TimeSpan.FromMinutes(5)) return cached;
            var result = Map(Value(await Get("api/appdetails?appids=" + id + "&l=english&cc=BR", token).ConfigureAwait(false), id));
            var data = Map(Value(result, "data"));
            if (!true.Equals(Value(result, "success")) || Text(data, "type") != "game" || !true.Equals(Value(Map(Value(data, "platforms")), "windows"))) throw new InvalidOperationException("Este item não é um jogo para Windows. Selecione o jogo principal; extras e trilhas sonoras não são analisados.");
            var requirements = Map(Value(data, "pc_requirements"));
            var game = new Game { Id = id, Name = Text(data, "name"), Minimum = Compatibility.Parse(Text(requirements, "minimum")), Recommended = Compatibility.Parse(Text(requirements, "recommended")), CheckedAt = DateTime.UtcNow };
            lock (cacheLock)
            {
                if (games.Count >= 24) games.Clear();
                games[id] = game;
            }
            return game;
        }
        public void Dispose() { client.Dispose(); }
    }
}
