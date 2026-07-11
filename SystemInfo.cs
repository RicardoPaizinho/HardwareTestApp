using System;
using System.Management;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using Vortice.DXGI;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware.Motherboard;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;


namespace HardwareTestApp
{
    public class SystemInfo
    {
        private Computer _computer;
        public static object GetPCInfo()
        {
            ReadSystemInfo readSystemInfo = new ReadSystemInfo();
            readSystemInfo.Load();

            List<String> discos = new List<String> { "SSD 256GB", "HDD 1TB" };
            List<String> gpus = new List<String> { "NVIDIA GeForce RTX 4050 Laptop GPU", "Intel(R) UHD Graphics" };
            //string smartList = ListPhysicalDisks();
            //Console.WriteLine("TEste Smart 1");
            Console.WriteLine("TEste Smart 2");
           // Console.WriteLine("TEste Smart");
            //LH();
            var infoProcessor = GetProcessorInfo();
            var infoSystem = GetOperacionalSystemInfo();
            var infoMb = GetMbInfo();

            return new
            {
                type = "pcInfo",
                processador_Name = infoProcessor["Name"],
                processador_ClockSpeed = infoProcessor["CurrentClockSpeed"],
                processador_MaxClockSpeed = infoProcessor["MaxClockSpeed"],
                processador_NumberOfCores = infoProcessor["NumberOfCores"],
                processador_NumberOfLogicalProcessors = infoProcessor["NumberOfLogicalProcessors"],
                processador_SerialNumber = infoProcessor["SerialNumber"],

                mb_ProductName = infoMb["Product"],
                mb_SerialNumber = infoMb["SerialNumber"],
                mb_Manufacturer = infoMb["Manufacturer"],
                mb_Version = infoMb["Version"],

                system = infoSystem["Sistema"],
                systemBuild = infoSystem["BuildNumber"],
                systemVersion = infoSystem["Version"],
                Architecture = infoSystem["Architecture"],
                WindowsKey = infoSystem["WindowsKey"],

                //batteryHealth = 100 - GetSensorValue(HardwareType.Battery, SensorType.Level, "Degradation Level") ?? 0,
                //batteryHealth = 18,
                //result["Product"] = TryGetProperty(mo, "Product");
                //result["Manufacturer"] = TryGetProperty(mo, "Manufacturer");
                //result["SerialNumber"] = TryGetProperty(mo, "SerialNumber");
                //result["Version"] = TryGetProperty(mo, "Version");


                model = GetComputerModel(),
                systemFamily = GetSystemFamily(),
                serialNumber = GetSerialNumber(),

                memoria = readSystemInfo.GetMemoryInfo(),
                armazenamento = readSystemInfo.HddInfo,
                placaVideo = VerificarGpu(), //readSystemInfo.Gpus[0]
                lcd = readSystemInfo.LcdDetalhado,

            };
        }

        public static async Task<object> GetPerformanceMetricsAsync()
        {
            return new
            {
                type = "performanceData",
                memory = await GetMemoryUsageAsync(),
                cpu = await GetCpuUsageAsync(), // Use await para obter o resultado

                disk = await GetTotalDiskUsageAsync()
            };
        }

        public static string GetComputerModel()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (ManagementObject mo in searcher.Get())
                {
                    return $" {mo["Model"]}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Modelo do PC: {ex.Message}");
            }
            return "Desconhecido";
        }

        public static string GetSerialNumber()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
                foreach (ManagementObject mo in searcher.Get())
                {
                    return mo["SerialNumber"]?.ToString() ?? "Desconhecido";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Número de série: {ex.Message}");
            }
            return "Desconhecido";
        }



        public static Dictionary<string, string> GetProcessorInfo()
        {
            var result = new Dictionary<string, string>();
            string[] palavrasParaRemover = { " CPU @", "(TM)", "(R)" };
            string padrao = string.Join("|", Array.ConvertAll(palavrasParaRemover, Regex.Escape));

            try
            {
                // 1. SEMPRE use "SELECT *" para evitar erros de nomes de propriedades inexistentes
                string query = "SELECT * FROM Win32_Processor";

                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();

                    if (mo != null)
                    {
                        // 2. Usamos uma função auxiliar para ler com segurança cada propriedade
                        result["Name"] = Regex.Replace(TryGetProperty(mo, "Name"), padrao, "");
                        result["Manufacturer"] = TryGetProperty(mo, "Manufacturer");
                        result["CurrentClockSpeed"] = TryGetProperty(mo, "CurrentClockSpeed");
                        result["MaxClockSpeed"] = TryGetProperty(mo, "MaxClockSpeed");
                        result["NumberOfCores"] = TryGetProperty(mo, "NumberOfCores");
                        result["NumberOfLogicalProcessors"] = TryGetProperty(mo, "NumberOfLogicalProcessors");

                        // Aqui você pode tentar ler o SerialNumber real ou o 'SerialNumber2' de teste
                        // Se não existir, ele apenas retornará "Desconhecido" em vez de travar tudo.
                        result["SerialNumber"] = TryGetProperty(mo, "SerialNumber2");
                    }
                    else
                    {
                        result["Error"] = "Nenhum processador detectado.";
                    }
                }
            }
            catch (Exception ex)
            {
                result["Error"] = $"Erro Crítico: {ex.Message}";
            }

            return result;
        }

        // 3. FUNÇÃO AUXILIAR: Evita erros caso o wmi não encontre a palavra buscada na consulta
        private static string TryGetProperty(ManagementObject mo, string propertyName)
        {
            try
            {
                // Tenta acessar a propriedade. Se o nome não existir no WMI, gera erro e cai no catch local.
                var value = mo[propertyName];
                return value?.ToString() ?? "N/A";
            }
            catch
            {
                // Se a propriedade não existir (como o SerialNumber2), retorna isso em vez de quebrar o app
                return "Não Suportado";
            }
        }


        // --- ASSINATURAS NATIVAS CORRIGIDAS ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() => dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        // --- MÉTODOS ---

        public static async Task<double> GetCpuUsageAsync()
        {
            try
            {
                using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
                await Task.Delay(1000); // Delay necessário apenas para CPU
                return Math.Round(cpuCounter.NextValue(), 2);
            }
            catch { return 0.0; }
        }

        public static async Task<double> GetMemoryUsageAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var memStatus = new MEMORYSTATUSEX();
                    // Se a chamada nativa funcionar, temos o valor idêntico ao Gerenciador de Tarefas
                    if (GlobalMemoryStatusEx(memStatus))
                    {
                        return (double)memStatus.dwMemoryLoad;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FALLBACK] Erro Memory API: {ex.Message}");
                }

                // PLANO B: Caso a API nativa falhe em algum sistema específico
                try
                {
                    using var ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                    return Math.Round(ramCounter.NextValue(), 2);
                }
                catch { return 0.0; }
            });
        }

        /// <summary>
        /// Retorna a porcentagem de ESPAÇO OCUPADO no disco principal (C:)
        /// De forma assíncrona para não travar a UI em discos lentos.
        /// </summary>
        // --- DISCO (ATIVIDADE DO SISTEMA) ---
        // Esta versão usa contadores de performance, que não travam o PC
        public static async Task<double> GetTotalDiskUsageAsync()
        {
            return await Task.Run(async () =>
            {
                try
                {
                    // "PhysicalDisk" mede a carga de trabalho do HD (Igual ao Gerenciador de Tarefas)
                    using var diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total");
                    diskCounter.NextValue();
                    await Task.Delay(500);
                    double value = diskCounter.NextValue();

                    // O Windows às vezes reporta > 100% se houver picos, então limitamos
                    return Math.Min(Math.Max(Math.Round(value, 2), 0), 100);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro Disco: {ex.Message}");
                    return 0.0;
                }
            });
        }



        public static string GetSystemFamily()
        {
            String Manufacturer = " ";
            String SystemFamily = " ";

            try
            {
                string query = "SELECT Manufacturer, SystemFamily FROM Win32_ComputerSystem";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();
                    if (mo == null)
                    {
                        return (" - ");
                    }
                    Manufacturer = mo.Properties["Manufacturer"]?.Value?.ToString() ?? "Desconhecido";
                    SystemFamily = mo.Properties["SystemFamily"]?.Value?.ToString() ?? "Desconhecido";

                }
            }
            catch (ManagementException ex)
            {
                Console.WriteLine($"Erro ao acessar WMI: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro inesperado: {ex.Message}");
            }
            return Manufacturer + " " + SystemFamily;

        }

        public static Dictionary<string, string> GetMbInfo()
        {
            var result = new Dictionary<string, string>();
            string[] palavrasParaRemover = { " CPU @", "(TM)", "(R)" };
            string padrao = string.Join("|", Array.ConvertAll(palavrasParaRemover, Regex.Escape));

            try
            {
                // 1. SEMPRE use "SELECT *" para evitar erros de nomes de propriedades inexistentes
                string query = "SELECT * FROM Win32_BaseBoard";

                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();

                    if (mo != null)
                    {
                        // 2. Usamos uma função auxiliar para ler com segurança cada propriedade
                        result["Product"] = TryGetProperty(mo, "Product");
                        result["Manufacturer"] = TryGetProperty(mo, "Manufacturer");
                        result["SerialNumber"] = TryGetProperty(mo, "SerialNumber");
                        result["Version"] = TryGetProperty(mo, "Version");

                    }
                    else
                    {
                        result["Error"] = "Erro ao obter informações da motherboard";
                    }
                }
            }
            catch (Exception ex)
            {
                result["Error"] = $"Erro Crítico: {ex.Message}";
            }

            return result;
        }


        private static List<string> VerificarGpu()
        {
            List<String> GpuList = new List<string>();

            try
            {
                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory6>();

                for (uint i = 0; ; i++)
                {
                    var result = factory.EnumAdapterByGpuPreference(i, GpuPreference.HighPerformance, out IDXGIAdapter1 adapter);
                    if (!result.Success || adapter == null) break;

                    using (adapter)
                    {
                        var desc = adapter.Description1;
                        string name = desc.Description.Trim();

                        if (name.Contains("Microsoft Basic Render Driver"))
                            continue;

                        double totalMB = desc.DedicatedVideoMemory / (1024.0 * 1024.0);
                        //  double totalMB1000 = desc.DedicatedVideoMemory / (1000.0 * 1000.0);
                        string memFormatada = "";
                        //double teste512 = 485.77734375;
                        // double qteste512 = 496.77734375;

                        if (totalMB < 1000)
                        {
                            if (totalMB > 470 && totalMB <= 512) { totalMB = 512; }

                            memFormatada = ($":: {totalMB} MB");

                        }
                        else
                        {
                            // Divide o número por 1000
                            double resultadoDivisao = totalMB / 1000;
                            // Arredonda o resultado para o inteiro mais próximo
                            int resultadoArredondado = (int)Math.Round(resultadoDivisao);
                            memFormatada = ($":: {resultadoArredondado} Gb");

                        }


                        // output.AppendLine($"Memória dedicada total: {totalMB:F2} MB");
                        double usagePercent = 0;
                        if (adapter.QueryInterfaceOrNull<IDXGIAdapter4>() is IDXGIAdapter4 adapter4)
                        {
                            using (adapter4)
                            {
                                var memoryInfo = adapter4.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                                double currentUsageMB = memoryInfo.CurrentUsage / (1024.0 * 1024.0);
                                double budgetMB = memoryInfo.Budget / (1024.0 * 1024.0);
                                usagePercent = (budgetMB > 0) ? (currentUsageMB / budgetMB) * 100.0 : 0.0;

                                //   output.AppendLine($"Memória dedicada em uso: {currentUsageMB:F2} MB");
                                // output.AppendLine($"Orçamento de memória disponível: {budgetMB:F2} MB");
                                //  output.AppendLine($"Uso de VRAM: {usagePercent:F1}%");
                            }
                        }
                        String gpu = name + " " + memFormatada;
                        GpuList.Add(name + " " + memFormatada + " ");
                        //  gpus.add(gpu)
                        //  output.AppendLine(new string('-', 40));
                    }
                }

                return GpuList;
            }
            catch (Exception ex)
            {
                GpuList.Add("Falha ao verificar Gpu");
                return GpuList;

            }
        }

        public static Dictionary<string, string> GetOperacionalSystemInfo()
        {
            var result = new Dictionary<string, string>();
           
            try
            {
                // 1. SEMPRE use "SELECT *" para evitar erros de nomes de propriedades inexistentes
                string query = "SELECT * FROM Win32_OperatingSystem";

                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();

                    if (mo != null)
                    {
                        result["Sistema"] = TryGetProperty(mo, "Caption");
                        result["BuildNumber"] = TryGetProperty(mo, "BuildNumber");
                        result["Version"] = GetWindowsFeatureVersion();
                        result["Architecture"] = TryGetProperty(mo, "OSArchitecture");
                        result["WindowsKey"] = GetSystemKey();

                    }
                    else
                    {
                        result["Error"] = "Nenhum processador detectado.";
                    }
                }
            }
            catch (Exception ex)
            {
                result["Error"] = $"Erro Crítico: {ex.Message}";
            }

            return result;
        }

        public static String GetSystemKey()
        {
            var result = new Dictionary<string, string>();
            string key=" ";
            try
            {
                // 1. Obtendo a Chave do Windows (Digital ou BIOS)
                using (var searcher = new ManagementObjectSearcher("SELECT OA3xOriginalProductKey FROM SoftwareLicensingService"))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();
                    key = mo?["OA3xOriginalProductKey"]?.ToString();

                    key = !string.IsNullOrEmpty(key) ? key : "Chave Digital/Não encontrada";
                }

               
            }
            catch (Exception ex)
            {
                result["Error"] = $"Erro ao obter licença: {ex.Message}";
            }
            return key;
        }

        private static String GetWindowsFeatureVersion()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        var displayVersion = key.GetValue("DisplayVersion")?.ToString();
                        return displayVersion ?? "Desconhecido";
                    }
                    else
                    {
                        return "Chave de registro não encontrada";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao acessar o registro: {ex.Message}");
                return "Erro";
            }
        }

        private float? GetSensorValue(HardwareType hwType, SensorType sType, string sensorName = null)
        {
            if (_computer == null) return null;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == hwType)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == sType)
                        {
                            if (!string.IsNullOrEmpty(sensorName))
                            {
                                if (sensor.Name.IndexOf(sensorName, StringComparison.OrdinalIgnoreCase) >= 0) return sensor.Value;
                                continue;
                            }
                            if (sensor.Value.HasValue) return sensor.Value;
                        }
                    }
                }
            }
            return null;
        }

        private static void LH()
        {
            // 1. Chama o método que criamos para obter todos os dados de hardware
            Dictionary<string, string> dadosHardware = HardwareMonitorService.GetLHMInfo();

            // 2. Inicializa as strings para guardar as propriedades do disco
            string nomeDoDisco = "Não encontrado";
            string temperaturaDisco = "N/A";
            string usoDisco = "N/A";
            string taxaEscrita = "N/A";

            // 3. Filtra as chaves do dicionário que pertencem ao tipo 'Storage' (Armazenamento)
            // Exemplo de chave gerada pela classe anterior: "[Storage] Kingston SA400S37240G - Temperature (Temperature)"
            var chavesDoDisco = dadosHardware.Keys
                .Where(k => k.StartsWith("[Storage]"))
                .ToList();

            if (chavesDoDisco.Count > 0)
            {
                foreach (var chave in chavesDoDisco)
                {
                    // Extrai o nome do componente a partir da chave se ainda não tiver capturado
                    if (nomeDoDisco == "Não encontrado")
                    {
                        // Remove o prefixo "[Storage] " e quebra no primeiro hífen para pegar só o nome do SSD/HDD
                        nomeDoDisco = chave.Replace("[Storage] ", "").Split('-')[0].Trim();
                    }

                    // Identifica e distribui as propriedades para suas respectivas strings
                    if (chave.Contains("(Temperature)"))
                    {
                        temperaturaDisco = dadosHardware[chave] + " °C";
                    }
                    else if (chave.Contains("Remaining Life") || chave.Contains("Total Activity"))
                    {
                        usoDisco = dadosHardware[chave] + " %";
                    }
                    else if (chave.Contains("Write Rate"))
                    {
                        taxaEscrita = dadosHardware[chave] + " MB/s";
                    }
                }
            }

            // 4. Mostra as strings individuais preenchidas
            Console.WriteLine($"=== INFORMAÇÕES DO DISCO ===");
            Console.WriteLine($"Modelo do Disco: {nomeDoDisco}");
            Console.WriteLine($"Temperatura:     {temperaturaDisco}");
            Console.WriteLine($"Uso / Vida Útil: {usoDisco}");
            Console.WriteLine($"Taxa de Escrita: {taxaEscrita}");
        }

        public static string GetPanelModel()
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"Root\WMI", "SELECT UserFriendlyName FROM WmiMonitorID"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    ushort[] name = (ushort[])mo["UserFriendlyName"];
                    if (name != null)
                    {
                        return System.Text.Encoding.Unicode.GetString(Array.ConvertAll(name, x => (byte)x)).TrimEnd('\0');
                    }
                }
            }
            return "Modelo não identificado";
        }

       // public static async SmartMoonTools()
             public static async Task<string> ExecuteSmartctl(string disk)
        {
            SmartService smartService = new SmartService();
           // smartService.GetDiskDataJson("c");

            // ... dentro do seu método async
            string rawJson = await smartService.GetDiskDataJson("/dev/sda");

            try
            {
                // Transforma em um objeto e formata com espaços (WriteIndented)
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(rawJson);
                string prettyJson = JsonSerializer.Serialize(jsonElement, new JsonSerializerOptions { WriteIndented = true });
         
                Console.WriteLine(prettyJson);
                return prettyJson;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao formatar JSON: " + ex.Message);
                Console.WriteLine(rawJson); // Imprime o bruto se der erro
            }
            return "Erro SmartMon Tools";
        }

        public async Task<List<string>> ListPhysicalDisks()
        {
            var disks = new List<string>();
            // --scan --json retorna a lista de dispositivos detectados
            string rawJson = await ExecuteSmartctl("--scan --json");

            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("devices", out var devices))
            {
                foreach (var dev in devices.EnumerateArray())
                {
                    disks.Add(dev.GetProperty("name").GetString());
                }
            }
            return disks;
        }

        public async Task<string> RunSmartTest(string devicePath, string testType)
        {
            // testType pode ser "short" ou "long"
            // O comando é -t ou --test
            return await ExecuteSmartctl($"-t {testType} --json {devicePath}");
        }




            private readonly SmartService _smartService = new SmartService();

        // 1. LISTAGEM DE DISCOS (Para o select do Vue)
        public async Task<List<DiskInfo>> GetSmartDiskListAsync()
        {
            Console.WriteLine("[DEBUG] Entrou em GetSmartDiskListAsync");

            // 1. Obtém a pasta onde o executável está rodando
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 2. Combina com a pasta 'smart' e o nome do arquivo
            // Path.Combine trata as barras automaticamente para evitar duplicação
            string smartctlPath = Path.Combine(baseDir, "smart", "smartctl.exe");

            Console.WriteLine($"[DEBUG] Caminho Base: {baseDir}");
            Console.WriteLine($"[DEBUG] Caminho Construído: {smartctlPath}");

            if (!File.Exists(smartctlPath))
            {
                Console.WriteLine("[DEBUG] ERRO CRÍTICO: smartctl.exe NÃO encontrado no caminho acima!");
                // Tenta uma busca alternativa caso a pasta 'smart' não exista
                string altPath = Path.Combine(baseDir, "smartctl.exe");
                Console.WriteLine($"[DEBUG] Tentando caminho alternativo: {altPath}");

                if (File.Exists(altPath))
                {
                    smartctlPath = altPath;
                    Console.WriteLine("[DEBUG] smartctl encontrado na raiz!");
                }
                else
                {
                    return new List<DiskInfo>(); // Retorna lista vazia para o Vue parar o "carregando"
                }
            }

            // 3. Execução do Processo (Exemplo de chamada segura)
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = smartctlPath,
                    Arguments = "--scan --json", // Escaneia os discos em formato JSON
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    Console.WriteLine($"[DEBUG] Saída do smartctl capturada. Tamanho: {output.Length}");
                    // Aqui entra sua lógica de parse do JSON para retornar a List<DiskInfo>
                    return ParseSmartDiskList(output);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Erro ao executar smartctl: {ex.Message}");
                return new List<DiskInfo>();
            }
        }



        // 2. DADOS DETALHADOS DE UM DISCO ESPECÍFICO
        public async Task<string> GetDetailedSmartDataAsync(string devicePath)
            {
                // Retorna o JSON bruto do smartctl diretamente para o Vue processar
                // Isso economiza memória no C# e deixa o processamento pesado de UI para o Chrome/Vue
                return await _smartService.GetDiskDataJson(devicePath);
            }

            // 3. EXECUTAR TESTES (Short/Long)
            public async Task<object> RunDeviceTestAsync(string devicePath, string testType)
            {
                // Comando: smartctl -t short /dev/sda
                string result = await _smartService.ExecuteCommand($"-t {testType} --json {devicePath}");
                return new { type = "testStarted", raw = result };
            }


        private List<DiskInfo> ParseSmartDiskList(string jsonRaw)
        {
            var diskList = new List<DiskInfo>();

            if (string.IsNullOrWhiteSpace(jsonRaw))
            {
                Console.WriteLine("[DEBUG] ParseSmartDiskList: JSON recebido está vazio.");
                return diskList;
            }

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(jsonRaw))
                {
                    JsonElement root = doc.RootElement;

                    // O smartctl --scan --json retorna um array chamado "devices"
                    if (root.TryGetProperty("devices", out JsonElement devicesElement))
                    {
                        foreach (JsonElement device in devicesElement.EnumerateArray())
                        {
                            diskList.Add(new DiskInfo
                            {
                                // Pega o caminho do dispositivo (ex: /dev/sda ou /dev/nvme0)
                                Device = device.GetProperty("name").GetString(),
                                // Pega o tipo (ex: ata, nvme)
                                Type = device.GetProperty("type").GetString(),
                                // Pega o protocolo
                                Protocol = device.GetProperty("protocol").GetString()
                            });
                        }
                    }
                }
                Console.WriteLine($"[DEBUG] ParseSmartDiskList: {diskList.Count} discos encontrados.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Erro ao fazer Parse do JSON do smartctl: {ex.Message}");
            }

            return diskList;
        }

        public async Task<string> RunSmartTestAsync(string diskPath, string testType)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // O comando para iniciar o teste é: smartctl -t short /dev/sda
                    // O parâmetro 'testType' deve ser "short" ou "long"
                    string args = $"-t {testType} {diskPath}";

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smart", "smartctl.exe"),
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using (Process process = Process.Start(psi))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        if (!string.IsNullOrEmpty(error))
                        {
                            Console.WriteLine($"[SMART TEST ERROR] {error}");
                            return $"Erro: {error}";
                        }

                        Console.WriteLine($"[SMART TEST] Comando enviado: {output}");
                        return "Sucesso: Teste iniciado.";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SMART TEST CRITICAL] {ex.Message}");
                    return $"Falha crítica: {ex.Message}";
                }
            });
        }
    }

    public class DiskInfo
    {
        public string Name { get; set; }
        public string SerialNumber { get; set; }
        public string Size { get; set; }
        public string Type { get; set; }
        public string Bus { get; set; }
        public int HealthPercent { get; set; } // Estilo HD Sentinel
        public string Status { get; set; }

        public string Device { get; set; }
        public string Protocol { get; set; }
    }
}