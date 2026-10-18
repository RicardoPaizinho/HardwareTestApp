using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace HardwareTestApp
{
    // Corpo do POST /api/stress/start: duração e, opcionalmente, só alguns núcleos
    // (pelo APIC ID — ex: o núcleo apontado por erros WHEA). Sem ApicIds = todos.
    public class StressPedido
    {
        public int DuracaoSeg { get; set; } = 300;
        public List<int>? ApicIds { get; set; }
    }

    // -----------------------------------------------------------------------------
    // CPU STRESS (System Diagnostic > CPU Stress — CpuStress.vue)
    // -----------------------------------------------------------------------------
    // Objetivo principal: achar ERROS (núcleo calculando errado, WHEA novo, tela
    // azul) sob carga máxima — o benchmark (pontos) é consequência.
    //
    // Cada thread fica presa (afinidade) a um processador lógico e roda "lotes"
    // com resposta conhecida: crivo de primos (inteiros/memória), SHA-256
    // encadeado (unidades de criptografia) e FMA vetorial AVX (ponto flutuante —
    // o que mais esquenta e puxa tensão). Resultado diferente do esperado = erro
    // de cálculo daquele núcleo, o mesmo princípio do Prime95.
    //
    // A topologia vem do CPUID executado em cada processador lógico: APIC ID
    // (o mesmo número que o WHEA-Logger grava em "ID APIC do Processador") e o
    // tipo do núcleo em CPUs híbridas da Intel (P-core / E-core).
    // -----------------------------------------------------------------------------
    public static class CpuStressService
    {
        [DllImport("kernel32.dll")] private static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr mask);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessorNumber();

        public class ProcessadorLogico
        {
            public int Indice { get; set; }      // número do processador lógico no Windows
            public int ApicId { get; set; }      // x2APIC ID (igual ao WHEA)
            public int Nucleo { get; set; }      // núcleo físico (agrupa os 2 threads do Hyper-Threading)
            public string Tipo { get; set; } = ""; // "P", "E" ou "" (CPU não híbrida)
        }

        // ---------------- TOPOLOGIA ----------------
        private static List<ProcessadorLogico>? _topologia;

        public static List<ProcessadorLogico> Topologia()
        {
            if (_topologia != null) return _topologia;
            var lista = new List<ProcessadorLogico>();
            int total = Math.Min(Environment.ProcessorCount, 64); // grupo 0 (notebooks não passam de 64)
            for (int i = 0; i < total; i++)
            {
                ProcessadorLogico? info = null;
                var t = new Thread(() => info = LerCpuidNoProcessador(i));
                t.Start();
                t.Join();
                if (info != null) lista.Add(info);
            }
            _topologia = lista;
            return lista;
        }

        private static ProcessadorLogico? LerCpuidNoProcessador(int indice)
        {
            try
            {
                SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << indice));
                // Garante que o Windows já moveu o thread pro processador pedido
                for (int tentativa = 0; tentativa < 50 && GetCurrentProcessorNumber() != indice; tentativa++) Thread.Sleep(1);
                if (!X86Base.IsSupported) return new ProcessadorLogico { Indice = indice, ApicId = indice, Nucleo = indice };

                int maxLeaf = X86Base.CpuId(0, 0).Eax;
                int apic, nucleo;
                if (maxLeaf >= 0xB)
                {
                    var leafB = X86Base.CpuId(0xB, 0);
                    apic = leafB.Edx;
                    nucleo = apic >> (leafB.Eax & 0x1F); // remove os bits do SMT
                }
                else
                {
                    apic = (X86Base.CpuId(1, 0).Ebx >> 24) & 0xFF;
                    nucleo = apic;
                }

                string tipo = "";
                if (maxLeaf >= 0x1A)
                {
                    int coreType = (X86Base.CpuId(0x1A, 0).Eax >> 24) & 0xFF;
                    tipo = coreType == 0x20 ? "E" : coreType == 0x40 ? "P" : "";
                }
                return new ProcessadorLogico { Indice = indice, ApicId = apic, Nucleo = nucleo, Tipo = tipo };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CPU Stress] Falha ao ler CPUID no processador {indice}: {ex.Message}");
                return null;
            }
        }

        public static string NomeProcessador()
        {
            using var chave = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (chave?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Desconhecido";
        }

        // Revisão do microcode carregado (ex: 0x12B). O valor fica nos 4 bytes
        // altos do "Update Revision" do registro.
        public static string? RevisaoMicrocode()
        {
            using var chave = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (chave?.GetValue("Update Revision") is byte[] bytes && bytes.Length >= 8)
                return "0x" + BitConverter.ToUInt32(bytes, 4).ToString("X");
            return null;
        }

        // Intel Core 13ª/14ª geração (Raptor Lake): família com relatos de
        // degradação por tensão alta — o front mostra o alerta de tensão.
        public static bool IntelRaptorLake(string nome) =>
            nome.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(nome, @"i[3579]-1[34]\d{3}", RegexOptions.IgnoreCase);

        // ---------------- ESTADO DO TESTE ----------------
        private static readonly object _lock = new();
        private static CancellationTokenSource? _cts;
        private static bool _rodando;
        private static DateTime _inicio;
        private static int _duracaoSeg;
        private static string _fim = "";
        private static List<ProcessadorLogico> _alvos = new();
        private static long[] _lotes = Array.Empty<long>();
        private static long[] _erros = Array.Empty<long>();
        private static readonly ConcurrentQueue<string> _mensagensErro = new();
        private static readonly List<object> _amostras = new();
        private static readonly List<WheaService.EventoWhea> _wheaNovos = new();
        private static float? _tempMax, _voltMax, _potMax, _clockMax;
        private static float? _tempAtual, _voltAtual, _potAtual, _clockAtual;

        // Respostas conhecidas dos lotes (calculadas uma vez, fora dos núcleos testados)
        private const int LimiteCrivo = 2_000_000;
        private const int PrimosEsperados = 148_933; // primos abaixo de 2.000.000
        private static byte[] _shaEsperado = Array.Empty<byte>();
        private static double _fmaEsperado;
        private static readonly byte[] _shaEntrada = Enumerable.Range(0, 1 << 20).Select(i => (byte)(i * 31 + 7)).ToArray();

        public static object Iniciar(int duracaoSeg, List<int>? apicIds)
        {
            lock (_lock)
            {
                if (_rodando) return new { success = false, message = "Já existe um teste em andamento." };

                var topologia = Topologia();
                var alvos = apicIds is { Count: > 0 }
                    ? topologia.Where(p => apicIds.Contains(p.ApicId)).ToList()
                    : topologia.ToList();
                if (alvos.Count == 0) return new { success = false, message = "Nenhum processador corresponde aos APIC IDs pedidos." };

                _shaEsperado = Sha(_shaEntrada);
                _fmaEsperado = CalcularFma(1);

                _alvos = alvos;
                _lotes = new long[alvos.Count];
                _erros = new long[alvos.Count];
                while (_mensagensErro.TryDequeue(out _)) { }
                lock (_amostras) _amostras.Clear();
                lock (_wheaNovos) _wheaNovos.Clear();
                _tempMax = _voltMax = _potMax = _clockMax = null;
                _duracaoSeg = Math.Clamp(duracaoSeg, 10, 3600);
                _inicio = DateTime.Now;
                _fim = "";
                _rodando = true;
                _cts = new CancellationTokenSource();

                var token = _cts.Token;
                for (int i = 0; i < alvos.Count; i++)
                {
                    int slot = i;
                    var t = new Thread(() => Trabalhar(slot, token)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = $"stress-{alvos[slot].ApicId}" };
                    t.Start();
                }
                new Thread(() => Monitorar(token)) { IsBackground = true, Name = "stress-monitor" }.Start();

                Console.WriteLine($"[CPU Stress] Iniciado em {alvos.Count} processador(es) por {_duracaoSeg}s (APIC: {string.Join(",", alvos.Select(a => a.ApicId))})");
                return new { success = true };
            }
        }

        public static void Parar(string motivo = "Interrompido pelo técnico")
        {
            lock (_lock)
            {
                if (!_rodando) return;
                _fim = motivo;
                _rodando = false;
                _cts?.Cancel();
                Console.WriteLine($"[CPU Stress] Encerrado: {motivo}");
            }
        }

        public static object Status()
        {
            double decorrido = _rodando ? (DateTime.Now - _inicio).TotalSeconds : Math.Min((DateTime.Now - _inicio).TotalSeconds, _duracaoSeg);
            var porProcessador = _alvos.Select((p, i) => new
            {
                p.Indice, p.ApicId, p.Nucleo, p.Tipo,
                lotes = Interlocked.Read(ref _lotes[i]),
                erros = Interlocked.Read(ref _erros[i]),
                // pontos por núcleo = lotes por segundo × 100
                pontos = decorrido > 0 ? Math.Round(Interlocked.Read(ref _lotes[i]) / decorrido * 100) : 0
            }).ToList();

            List<object> amostras;
            lock (_amostras) amostras = _amostras.ToList();
            List<WheaService.EventoWhea> whea;
            lock (_wheaNovos) whea = _wheaNovos.ToList();

            return new
            {
                rodando = _rodando,
                fim = _fim,
                inicio = _inicio.ToString("yyyy-MM-ddTHH:mm:ss"),
                duracaoSeg = _duracaoSeg,
                decorridoSeg = Math.Round(decorrido),
                pontosTotal = porProcessador.Sum(p => p.pontos),
                errosTotal = porProcessador.Sum(p => p.erros),
                mensagensErro = _mensagensErro.Take(50).ToList(),
                processadores = porProcessador,
                sensores = new { tempAtual = _tempAtual, tempMax = _tempMax, voltAtual = _voltAtual, voltMax = _voltMax, clockAtual = _clockAtual, clockMax = _clockMax, potAtual = _potAtual, potMax = _potMax },
                amostras,
                wheaNovos = whea
            };
        }

        // ---------------- CARGA ----------------
        private static void Trabalhar(int slot, CancellationToken token)
        {
            var alvo = _alvos[slot];
            SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << alvo.Indice));
            var crivo = new bool[LimiteCrivo];
            string nome = $"APIC {alvo.ApicId}{(alvo.Tipo != "" ? $" ({alvo.Tipo}-core)" : "")}";

            while (!token.IsCancellationRequested)
            {
                try
                {
                    int primos = Crivo(crivo);
                    if (primos != PrimosEsperados) RegistrarErro(slot, $"{nome}: crivo de primos deu {primos} (esperado {PrimosEsperados})");

                    var sha = Sha(_shaEntrada);
                    if (!sha.AsSpan().SequenceEqual(_shaEsperado)) RegistrarErro(slot, $"{nome}: SHA-256 com resultado diferente do esperado");

                    double fma = CalcularFma(1);
                    if (BitConverter.DoubleToInt64Bits(fma) != BitConverter.DoubleToInt64Bits(_fmaEsperado))
                        RegistrarErro(slot, $"{nome}: FMA/AVX deu {fma:R} (esperado {_fmaEsperado:R})");

                    Interlocked.Increment(ref _lotes[slot]);
                }
                catch (Exception ex)
                {
                    RegistrarErro(slot, $"{nome}: exceção durante o cálculo — {ex.Message}");
                }
            }
        }

        private static void RegistrarErro(int slot, string mensagem)
        {
            Interlocked.Increment(ref _erros[slot]);
            var texto = $"{DateTime.Now:HH:mm:ss} {mensagem}";
            if (_mensagensErro.Count < 200) _mensagensErro.Enqueue(texto);
            Console.WriteLine($"[CPU Stress] ERRO {texto}");
        }

        private static int Crivo(bool[] composto)
        {
            Array.Clear(composto);
            int contagem = 0;
            for (int i = 2; i < composto.Length; i++)
            {
                if (composto[i]) continue;
                contagem++;
                for (long j = (long)i * i; j < composto.Length; j += i) composto[j] = true;
            }
            return contagem;
        }

        private static byte[] Sha(byte[] entrada)
        {
            var hash = SHA256.HashData(entrada);
            for (int i = 0; i < 7; i++) hash = SHA256.HashData(Concat(hash, entrada));
            return hash;
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        // Laço FMA em 4 vetores de 256 bits (AVX2/FMA3); sem suporte, usa escalar.
        // Os coeficientes mantêm os valores limitados (não estoura nem zera).
        private static double CalcularFma(int semente)
        {
            const int Iteracoes = 4_000_000;
            if (Fma.IsSupported && Avx.IsSupported)
            {
                var a = Vector256.Create(0.9999999);
                var b = Vector256.Create(0.0000001 * semente);
                var v0 = Vector256.Create(1.0, 1.1, 1.2, 1.3);
                var v1 = Vector256.Create(0.5, 0.6, 0.7, 0.8);
                var v2 = Vector256.Create(2.0, 2.1, 2.2, 2.3);
                var v3 = Vector256.Create(0.1, 0.2, 0.3, 0.4);
                for (int i = 0; i < Iteracoes; i++)
                {
                    v0 = System.Runtime.Intrinsics.X86.Fma.MultiplyAdd(v0, a, b);
                    v1 = System.Runtime.Intrinsics.X86.Fma.MultiplyAdd(v1, a, b);
                    v2 = System.Runtime.Intrinsics.X86.Fma.MultiplyAdd(v2, a, b);
                    v3 = System.Runtime.Intrinsics.X86.Fma.MultiplyAdd(v3, a, b);
                }
                var soma = Avx.Add(Avx.Add(v0, v1), Avx.Add(v2, v3));
                return soma.GetElement(0) + soma.GetElement(1) + soma.GetElement(2) + soma.GetElement(3);
            }

            double x0 = 1.0, x1 = 0.5, x2 = 2.0, x3 = 0.1;
            for (int i = 0; i < Iteracoes; i++)
            {
                x0 = x0 * 0.9999999 + 0.0000001 * semente;
                x1 = x1 * 0.9999999 + 0.0000001 * semente;
                x2 = x2 * 0.9999999 + 0.0000001 * semente;
                x3 = x3 * 0.9999999 + 0.0000001 * semente;
            }
            return x0 + x1 + x2 + x3;
        }

        // ---------------- MONITORAMENTO ----------------
        private const float TemperaturaLimite = 105f; // acima disso o teste para sozinho

        private static void Monitorar(CancellationToken token)
        {
            var proximaChecagemWhea = DateTime.Now.AddSeconds(10);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    LerSensores();
                    lock (_amostras)
                    {
                        if (_amostras.Count < 3600)
                            _amostras.Add(new { t = Math.Round((DateTime.Now - _inicio).TotalSeconds), temp = _tempAtual, volt = _voltAtual, clock = _clockAtual, pot = _potAtual });
                    }

                    if (_tempAtual >= TemperaturaLimite)
                        Parar($"Interrompido por segurança: temperatura chegou a {_tempAtual:F0} °C");
                    else if ((DateTime.Now - _inicio).TotalSeconds >= _duracaoSeg)
                        Parar("Concluído");

                    if (DateTime.Now >= proximaChecagemWhea)
                    {
                        ChecarWheaNovos();
                        proximaChecagemWhea = DateTime.Now.AddSeconds(10);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CPU Stress] Falha no monitoramento: {ex.Message}");
                }
                token.WaitHandle.WaitOne(1000);
            }
            try { LerSensores(); ChecarWheaNovos(); } catch { }
        }

        // Temperatura = maior entre os núcleos (ou o pacote); tensão = maior VID
        // de núcleo (precisa do PawnIO pra ler MSR — sem ele fica null); clock =
        // maior clock de núcleo; potência = pacote.
        private static void LerSensores()
        {
            HardwareMonitorSingleton.RefreshAll();
            float? temp = null, volt = null, clock = null, pot = null;
            foreach (var hw in HardwareMonitorSingleton.Instance.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
            {
                foreach (var s in hw.Sensors)
                {
                    if (!s.Value.HasValue) continue;
                    var v = s.Value.Value;
                    switch (s.SensorType)
                    {
                        case SensorType.Temperature when s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                            temp = Math.Max(temp ?? v, v); break;
                        case SensorType.Voltage when s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) && v > 0.3f && v < 2f:
                            volt = Math.Max(volt ?? v, v); break;
                        case SensorType.Clock when s.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("P-Core", StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith("E-Core", StringComparison.OrdinalIgnoreCase):
                            clock = Math.Max(clock ?? v, v); break;
                        case SensorType.Power when s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                            pot = v; break;
                    }
                }
            }
            _tempAtual = temp; _voltAtual = volt; _clockAtual = clock; _potAtual = pot;
            if (temp.HasValue) _tempMax = Math.Max(_tempMax ?? temp.Value, temp.Value);
            if (volt.HasValue) _voltMax = Math.Max(_voltMax ?? volt.Value, volt.Value);
            if (clock.HasValue) _clockMax = Math.Max(_clockMax ?? clock.Value, clock.Value);
            if (pot.HasValue) _potMax = Math.Max(_potMax ?? pot.Value, pot.Value);
        }

        private static void ChecarWheaNovos()
        {
            var desde = _inicio.ToString("yyyy-MM-ddTHH:mm:ss");
            var novos = WheaService.LerEventos(1, 200).Where(w => string.CompareOrdinal(w.DataIso, desde) >= 0).ToList();
            lock (_wheaNovos)
            {
                _wheaNovos.Clear();
                _wheaNovos.AddRange(novos);
            }
        }
    }
}
