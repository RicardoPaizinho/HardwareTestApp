using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HardwareTestApp
{
    // -----------------------------------------------------------------------------
    // MINIDUMP: QUAL DRIVER CAUSOU A TELA AZUL
    // -----------------------------------------------------------------------------
    // O evento BugCheck do Windows só guarda o código e os 4 parâmetros. O nome do
    // driver ("O que falhou: nvlddmkm.sys" da tela azul) fica no minidump, que
    // traz a lista de drivers carregados com o endereço de cada um.
    //
    // Vários códigos têm, num dos parâmetros, o endereço da instrução que falhou
    // (ou um ponteiro pra dentro do driver responsável, no VIDEO_TDR_FAILURE).
    // Basta achar em qual driver da lista esse endereço cai — sem WinDbg.
    //
    // Formato: "triage dump" de 64 bits (o minidump padrão do Windows):
    //   0x0000  "PAGEDU64"
    //   0x0038  código do bugcheck (uint32) / 0x0040  4 parâmetros (uint64)
    //   0x0F98  DumpType (4 = triage/minidump)
    //   0x2000  TRIAGE_DUMP64: +0x30 DriverListOffset, +0x34 DriverCount
    //   Cada driver = 0x90 bytes: +0x00 offset do nome (DUMP_STRING: uint32
    //   tamanho em caracteres + UTF-16), +0x38 DllBase, +0x48 SizeOfImage.
    // Validado com dumps reais: código e parâmetros batem com os eventos BugCheck.
    // -----------------------------------------------------------------------------
    public static class MinidumpService
    {
        public class Analise
        {
            public string Arquivo { get; set; } = "";
            public string Data { get; set; } = "";
            public uint Codigo { get; set; }
            public ulong[] Parametros { get; set; } = new ulong[4];
            public string? Driver { get; set; }      // ex: "nvlddmkm.sys"
            public int? Parametro { get; set; }      // qual parâmetro (1..4) apontou o driver
            // true = parâmetro documentado pra esse código; false = achado varrendo
            // os 4 parâmetros (indício, não confirmação)
            public bool Confirmado { get; set; }
            public int TotalDrivers { get; set; }
        }

        // Parâmetro que contém o endereço do código que falhou, por bugcheck.
        // Fonte: documentação dos Bug Check Codes da Microsoft.
        private static readonly Dictionary<uint, int> ParametroEndereco = new()
        {
            [0x0A] = 4,        // IRQL_NOT_LESS_OR_EQUAL: endereço da instrução
            [0xD1] = 4,        // DRIVER_IRQL_NOT_LESS_OR_EQUAL
            [0x1E] = 2,        // KMODE_EXCEPTION_NOT_HANDLED
            [0x1000001E] = 2,
            [0x3B] = 2,        // SYSTEM_SERVICE_EXCEPTION
            [0x50] = 3,        // PAGE_FAULT_IN_NONPAGED_AREA (quando conhecido)
            [0x7E] = 2,        // SYSTEM_THREAD_EXCEPTION_NOT_HANDLED
            [0x1000007E] = 2,
            [0x8E] = 2,        // KERNEL_MODE_EXCEPTION_NOT_HANDLED
            [0x1000008E] = 2,
            [0x116] = 2,       // VIDEO_TDR_FAILURE: ponteiro pra dentro do driver responsável
            [0x117] = 2,       // VIDEO_TDR_TIMEOUT_DETECTED
            [0xC5] = 4,        // DRIVER_CORRUPTED_EXPOOL: endereço que acessou a memória
        };

        private static readonly Dictionary<string, Analise?> _cache = new();

        // Analisa todos os minidumps de C:\Windows\Minidump (com cache por nome+tamanho)
        public static List<Analise> AnalisarPastaPadrao()
        {
            var lista = new List<Analise>();
            try
            {
                var pasta = new DirectoryInfo(@"C:\Windows\Minidump");
                if (!pasta.Exists) return lista;
                foreach (var arquivo in pasta.GetFiles("*.dmp"))
                {
                    var chave = $"{arquivo.Name}|{arquivo.Length}";
                    if (!_cache.TryGetValue(chave, out var analise))
                    {
                        analise = Analisar(arquivo.FullName);
                        _cache[chave] = analise;
                    }
                    if (analise != null) lista.Add(analise);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Minidump] Falha ao ler a pasta: {ex.Message}");
            }
            return lista;
        }

        public static Analise? Analisar(string caminho)
        {
            try
            {
                var d = File.ReadAllBytes(caminho);
                if (d.Length < 0x2100 || Encoding.ASCII.GetString(d, 0, 8) != "PAGEDU64") return null;

                var analise = new Analise
                {
                    Arquivo = Path.GetFileName(caminho),
                    Data = File.GetLastWriteTime(caminho).ToString("dd/MM/yyyy HH:mm"),
                    Codigo = BitConverter.ToUInt32(d, 0x38),
                };
                for (int i = 0; i < 4; i++) analise.Parametros[i] = BitConverter.ToUInt64(d, 0x40 + i * 8);

                // Só o minidump (triage, tipo 4) tem a lista de drivers nesse formato
                if (BitConverter.ToUInt32(d, 0xF98) != 4) return analise;

                uint inicioLista = BitConverter.ToUInt32(d, 0x2000 + 0x30);
                uint quantidade = BitConverter.ToUInt32(d, 0x2000 + 0x34);
                if (quantidade == 0 || quantidade > 2000 || inicioLista + quantidade * 0x90UL > (ulong)d.Length) return analise;

                var drivers = new List<(string nome, ulong inicio, uint tamanho)>();
                for (uint i = 0; i < quantidade; i++)
                {
                    int e = (int)(inicioLista + i * 0x90);
                    uint offNome = BitConverter.ToUInt32(d, e);
                    ulong inicio = BitConverter.ToUInt64(d, e + 0x38);
                    uint tamanho = BitConverter.ToUInt32(d, e + 0x48);
                    if (offNome + 4 > d.Length) continue;
                    uint chars = BitConverter.ToUInt32(d, (int)offNome);
                    if (chars == 0 || chars > 520 || offNome + 4 + chars * 2 > d.Length) continue;
                    var nome = Encoding.Unicode.GetString(d, (int)offNome + 4, (int)chars * 2);
                    drivers.Add((Path.GetFileName(nome.Replace('\\', '/')), inicio, tamanho));
                }
                analise.TotalDrivers = drivers.Count;

                string? DonoDoEndereco(ulong endereco) =>
                    endereco < 0xFFFF000000000000UL ? null // endereços de kernel ficam no topo do espaço de 64 bits
                    : drivers.FirstOrDefault(dr => endereco >= dr.inicio && endereco < dr.inicio + dr.tamanho).nome;

                // 1º: o parâmetro documentado para esse código; 2º: qualquer parâmetro que caia num driver
                if (ParametroEndereco.TryGetValue(analise.Codigo, out int p) && DonoDoEndereco(analise.Parametros[p - 1]) is string dono)
                {
                    analise.Driver = dono;
                    analise.Parametro = p;
                    analise.Confirmado = true;
                }
                else
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (DonoDoEndereco(analise.Parametros[i]) is string nome)
                        {
                            analise.Driver = nome;
                            analise.Parametro = i + 1;
                            break;
                        }
                    }
                }
                return analise;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Minidump] Falha ao analisar {Path.GetFileName(caminho)}: {ex.Message}");
                return null;
            }
        }

        // Liga um evento BugCheck ("0x0000000a (0x..., 0x..., 0x..., 0x...)") ao
        // minidump com o mesmo código e os mesmos 4 parâmetros.
        private static readonly Regex RegexBugcheck = new(
            @"0x([0-9a-fA-F]{8})\s*\(\s*0x([0-9a-fA-F]+),\s*0x([0-9a-fA-F]+),\s*0x([0-9a-fA-F]+),\s*0x([0-9a-fA-F]+)\s*\)");

        public static Analise? AcharParaEvento(string? texto, List<Analise> analises)
        {
            if (string.IsNullOrEmpty(texto) || analises.Count == 0) return null;
            var m = RegexBugcheck.Match(texto);
            if (!m.Success) return null;
            uint codigo = Convert.ToUInt32(m.Groups[1].Value, 16);
            var parametros = Enumerable.Range(2, 4).Select(g => Convert.ToUInt64(m.Groups[g].Value, 16)).ToArray();
            // 0x1000007F e 0x7F são o mesmo bugcheck (o bit 0x10000000 só indica a
            // variante); o evento às vezes grava um e o dump o outro
            const uint SemVariante = 0xEFFFFFFF;
            return analises.FirstOrDefault(a => (a.Codigo & SemVariante) == (codigo & SemVariante) && a.Parametros.SequenceEqual(parametros));
        }
    }
}
