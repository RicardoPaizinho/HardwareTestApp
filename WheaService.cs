using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace HardwareTestApp
{
    // -----------------------------------------------------------------------------
    // ERROS DE HARDWARE (WHEA) + ARQUIVO DE PROVAS
    // -----------------------------------------------------------------------------
    // Substitui o script Capturar-Crash.ps1: lê os eventos do provedor
    // Microsoft-Windows-WHEA-Logger (com os campos estruturados — ApicId, MCABank,
    // MciStat... — em vez de depender do texto traduzido da mensagem) e grava um
    // relatório único por equipamento em Redist\logs\<Modelo>_<Serial>.txt.
    //
    // Usa EventLogReader (e não a classe EventLog antiga) porque é o único jeito
    // de pegar os campos pelo NOME (EventData/Data Name="ApicId").
    // -----------------------------------------------------------------------------
    public static class WheaService
    {
        private const string ProvedorWhea = "Microsoft-Windows-WHEA-Logger";

        public class EventoWhea
        {
            public string DataIso { get; set; } = "";
            public string Data { get; set; } = "";
            public int Id { get; set; }
            public string Nivel { get; set; } = "";
            public string Mensagem { get; set; } = "";
            public Dictionary<string, string> Campos { get; set; } = new();
            // Atalhos dos campos mais usados (null quando o evento não tem)
            public int? ApicId { get; set; }
            public int? ErrorSource { get; set; }
            public int? McaBank { get; set; }
            public string? MciStat { get; set; }
            public int? ErrorType { get; set; }
            public string? Componente { get; set; }
            public string? TipoErro { get; set; }
        }

        public static List<EventoWhea> LerEventos(int dias, int maximo = 1000)
        {
            long janelaMs = (long)TimeSpan.FromDays(dias).TotalMilliseconds;
            return Ler(new EventLogQuery("System", PathType.LogName,
                $"*[System[Provider[@Name='{ProvedorWhea}'] and TimeCreated[timediff(@SystemTime) <= {janelaMs}]]]")
            {
                ReverseDirection = true // mais novo primeiro
            }, maximo);
        }

        // Lê os WHEA de um System.evtx exportado de outro equipamento (provas de cliente)
        public static List<EventoWhea> LerEventosDeArquivo(string caminhoEvtx, int maximo = 1000) =>
            Ler(new EventLogQuery(caminhoEvtx, PathType.FilePath, $"*[System[Provider[@Name='{ProvedorWhea}']]]")
            {
                ReverseDirection = true
            }, maximo);

        private static List<EventoWhea> Ler(EventLogQuery consulta, int maximo)
        {
            var lista = new List<EventoWhea>();

            try
            {
                using var leitor = new EventLogReader(consulta);
                for (EventRecord? registro = leitor.ReadEvent(); registro != null && lista.Count < maximo; registro = leitor.ReadEvent())
                {
                    using (registro)
                    {
                        lista.Add(Converter(registro));
                    }
                }
            }
            catch (Exception ex)
            {
                // WinPE não tem o serviço de log de eventos — sem WHEA nesse caso
                Console.WriteLine($"[WHEA] Falha ao ler eventos: {ex.Message}");
            }
            return lista;
        }

        private static EventoWhea Converter(EventRecord registro)
        {
            var quando = registro.TimeCreated ?? DateTime.MinValue;
            var evento = new EventoWhea
            {
                DataIso = quando.ToString("yyyy-MM-ddTHH:mm:ss"),
                Data = quando.ToString("dd/MM/yyyy HH:mm:ss"),
                Id = registro.Id,
                Nivel = registro.LevelDisplayName ?? "",
            };

            try { evento.Mensagem = registro.FormatDescription() ?? ""; }
            catch { evento.Mensagem = ""; }

            // Campos nomeados do EventData (o RawData é enorme e não serve pro front)
            try
            {
                var xml = XDocument.Parse(registro.ToXml());
                XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
                foreach (var dado in xml.Descendants(ns + "Data"))
                {
                    var nome = dado.Attribute("Name")?.Value;
                    if (string.IsNullOrEmpty(nome) || nome == "RawData") continue;
                    evento.Campos[nome] = dado.Value;
                }
            }
            catch { }

            evento.ApicId = Inteiro(evento.Campos, "ApicId");
            evento.ErrorSource = Inteiro(evento.Campos, "ErrorSource");
            evento.McaBank = Inteiro(evento.Campos, "MCABank");
            evento.ErrorType = Inteiro(evento.Campos, "ErrorType");
            evento.MciStat = evento.Campos.TryGetValue("MciStat", out var stat) ? stat : null;

            // Componente e tipo vêm traduzidos no texto da mensagem (pt-BR ou inglês)
            evento.Componente = LinhaDaMensagem(evento.Mensagem, "Relatado pelo componente", "Reported by component");
            evento.TipoErro = LinhaDaMensagem(evento.Mensagem, "Tipo de Erro", "Error Type");
            return evento;
        }

        private static int? Inteiro(Dictionary<string, string> campos, string nome) =>
            campos.TryGetValue(nome, out var valor) && int.TryParse(valor, out var n) ? n : null;

        private static string? LinhaDaMensagem(string mensagem, params string[] rotulos)
        {
            foreach (var rotulo in rotulos)
            {
                var m = Regex.Match(mensagem, Regex.Escape(rotulo) + @"\s*:\s*(.+)", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return null;
        }

        // --- ARQUIVO DE PROVAS --------------------------------------------------
        // Um arquivo por equipamento (sobrescrito a cada captura): eventos de
        // travamento, WHEA, minidumps existentes e estado do sistema.
        public static string SalvarProvas(string modelo, string serial, IEnumerable<object>? eventosTravamento)
        {
            var pasta = Path.Combine(AppContext.BaseDirectory, "Redist", "logs");
            Directory.CreateDirectory(pasta);
            var arquivo = Path.Combine(pasta, $"{NomeSeguro(modelo)}_{NomeSeguro(serial)}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("============================================================");
            sb.AppendLine(" PROVAS DE TRAVAMENTO / ERROS DE HARDWARE — CDQ");
            sb.AppendLine("============================================================");
            sb.AppendLine($"Modelo:  {modelo}");
            sb.AppendLine($"Serial:  {serial}");
            sb.AppendLine($"Captura: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine();

            sb.AppendLine("--- 1. EVENTOS DE TRAVAMENTO (tela azul / desligamento) ---");
            var travamentos = eventosTravamento?.ToList() ?? new List<object>();
            if (travamentos.Count == 0) sb.AppendLine("(nenhum)");
            foreach (dynamic ev in travamentos)
            {
                sb.AppendLine($"{ev.date} | {ev.origin} {ev.eventId & 0xFFFF}");
                sb.AppendLine("    " + ((string)(ev.msg ?? "")).Replace("\r", "").Replace("\n", "\n    ").Trim());
            }
            sb.AppendLine();

            sb.AppendLine("--- 2. ERROS WHEA (hardware) — últimos 120 dias ---");
            var whea = LerEventos(120);
            if (whea.Count == 0) sb.AppendLine("(nenhum)");
            else
            {
                // Resumo por APIC: erro recorrente no mesmo núcleo é o sinal mais forte
                foreach (var grupo in whea.Where(w => w.ApicId.HasValue).GroupBy(w => w.ApicId).OrderByDescending(g => g.Count()))
                    sb.AppendLine($"APIC {grupo.Key}: {grupo.Count()} erro(s) — último em {grupo.First().Data}");
                sb.AppendLine();
                foreach (var w in whea)
                {
                    sb.AppendLine($"TimeCreated : {w.Data}");
                    sb.AppendLine($"Id          : {w.Id}");
                    sb.AppendLine($"Message     : {w.Mensagem.Replace("\r", "").Replace("\n", "\n              ").Trim()}");
                    if (w.MciStat != null) sb.AppendLine($"MCA         : banco {w.McaBank}, MciStat {w.MciStat}, ErrorSource {w.ErrorSource}, ErrorType {w.ErrorType}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("--- 3. MINIDUMPS EM C:\\Windows\\Minidump ---");
            try
            {
                var dumps = new DirectoryInfo(@"C:\Windows\Minidump").Exists
                    ? new DirectoryInfo(@"C:\Windows\Minidump").GetFiles("*.dmp").OrderByDescending(f => f.LastWriteTime).ToList()
                    : new List<FileInfo>();
                if (dumps.Count == 0) sb.AppendLine("(nenhum)");
                foreach (var d in dumps) sb.AppendLine($"{d.LastWriteTime:dd/MM/yyyy HH:mm}  {d.Name}  ({d.Length / 1048576.0:F1} MB)");
            }
            catch (Exception ex) { sb.AppendLine($"(erro ao listar: {ex.Message})"); }
            sb.AppendLine();

            sb.AppendLine("--- 4. ESTADO DO SISTEMA ---");
            try
            {
                using var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                sb.AppendLine($"Windows: {nt?.GetValue("ProductName")} {nt?.GetValue("DisplayVersion")} build {nt?.GetValue("CurrentBuild")}.{nt?.GetValue("UBR")}");
                using var crash = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl");
                sb.AppendLine($"Dump: CrashDumpEnabled={crash?.GetValue("CrashDumpEnabled")} AlwaysKeepMemoryDump={crash?.GetValue("AlwaysKeepMemoryDump")}");
                sb.AppendLine($"Processador: {CpuStressService.NomeProcessador()} | microcode {CpuStressService.RevisaoMicrocode() ?? "?"}");
            }
            catch (Exception ex) { sb.AppendLine($"(erro parcial: {ex.Message})"); }

            File.WriteAllText(arquivo, sb.ToString(), new UTF8Encoding(true));
            Console.WriteLine($"[WHEA] Provas salvas em {arquivo}");
            return arquivo;
        }

        private static string NomeSeguro(string texto)
        {
            var limpo = string.Join("_", (texto ?? "").Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            limpo = Regex.Replace(limpo, @"\s+", "-");
            return string.IsNullOrEmpty(limpo) ? "desconhecido" : limpo;
        }
    }
}
