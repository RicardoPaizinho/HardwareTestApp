using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace HardwareTestApp
{

    public class Memoria
    {
        // Armazena a capacidade em bytes para podermos fazer cálculos
        public ulong CapacityBytes { get; set; }

        // Propriedade "calculada" que retorna o texto formatado para a UI
        public string Capacity
        {
            get
            {
                // Converte bytes para GB. Usamos F0 para arredondar para o inteiro mais próximo.
                return $"{(CapacityBytes / 1073741824.0):F0} GB";
            }
        }

        public string ConfiguredClockSpeed { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string IsOnBoard { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string MemoryType { get; set; } = string.Empty;
    }

    public class Gpu
    {
        public string AdapterRAM { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public uint? HorizontalResolution { get; set; }
        public uint? VerticalResolution { get; set; }
        public uint? RefreshRate { get; set; }
    }

    public class ReadSystemInfo
    {
        #region Propriedades (Todas as originais foram mantidas)
        public string CdqUser { get; set; }
        public string CdqUserTipo { get; set; }
        // Informações da Placa-Mãe e BIOS
        public string Manufacturer { get; private set; }
        public string ProductName { get; private set; }
        public string SystemFamily { get; private set; } // Adicionado de volta
        public string SystemSKUNumber { get; private set; } // Adicionado de volta
        public string SerialNumber { get; private set; }
        public string BiosVersion { get; private set; }

        // Sistema Operacional e Ativação
        public string OperatingSystem { get; private set; }
        public string WindowsFeatureVersion { get; private set; }

        public string WindowsActivationStatus { get; private set; }
        public string WindowsProductKey { get; private set; }
        public bool IsWindowsActivated { get; private set; }

        // Processador
        public string? ProcessorName { get; private set; }
        public string ProcessorManufacturer { get; private set; } // Adicionado de volta
        public string ProcessorCurrentClockSpeed { get; private set; } // Adicionado de volta
        public string ProcessorMaxClockSpeed { get; private set; } // Adicionado de volta

        // Memória
        public string MemoryTotal { get; private set; }
        public List<Memoria> Memorias { get; private set; } = new List<Memoria>();

        // Discos
        public List<string> HddInfo { get; private set; } = new List<string>();
        public List<String> HddSizes;
        // Vídeo e LCD
        public List<Gpu> Gpus { get; private set; } = new List<Gpu>();
        public string LcdDetalhado { get; private set; } // Adicionado de volta

        // Bateria
        public string BatteryStatus { get; private set; } // Adicionado de volta
        public String MemoriasDetalhadas;
        public String HdsDetalhados;
        #endregion

        /// <summary>
        /// Carrega todas as informações do sistema de forma assíncrona para não travar a UI.
        /// </summary>
        public void Load()
        {
            var stopwatch = Stopwatch.StartNew();

     
                // Agrupando consultas para mais eficiência
                GetComputerSystemInfo();
                GetBiosInfo();
                GetOperatingSystemInfo();
                GetWindowsFeatureVersion();
                //GetProcessorInfo();
                GetGpuInfo();
              //  GetMemoryInfo();
                GetDiskInfo();
                // GetBatteryInfo();
                IsWindowsActivated = IsWindowsActivatedNEW();
                // GetWindowsActivationInfo();
            

            // Funções que dependem de dados já coletados
            BuildLcdString();
           
           

            stopwatch.Stop();
            Console.WriteLine($"Tempo total para carregar informações: {stopwatch.ElapsedMilliseconds} ms");
        }

        #region Métodos de Coleta de Dados
        private void GetComputerSystemInfo()
        {
            try
            {
                string query = "SELECT Manufacturer, Model, SystemFamily, SystemSKUNumber FROM Win32_ComputerSystem";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();
                    if (mo == null)
                    {
                        Console.WriteLine("Nenhuma informação de sistema foi encontrada.");
                        return;
                    }

                    Manufacturer = mo.Properties["Manufacturer"]?.Value?.ToString() ?? "Desconhecido";
                    ProductName = mo.Properties["Model"]?.Value?.ToString() ?? "Desconhecido";
                    SystemFamily = mo.Properties["SystemFamily"]?.Value?.ToString() ?? "Desconhecido";
                    SystemSKUNumber = mo.Properties["SystemSKUNumber"]?.Value?.ToString() ?? "Desconhecido";
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
        }

        private void GetBiosInfo()
        {
            try
            {
                string query = "SELECT SerialNumber, SMBIOSBIOSVersion FROM Win32_BIOS";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();
                    if (mo == null)
                    {
                        Console.WriteLine("Nenhuma informação de BIOS foi encontrada.");
                        return;
                    }

                    SerialNumber = mo.Properties["SerialNumber"]?.Value?.ToString() ?? "Desconhecido";
                    BiosVersion = mo.Properties["SMBIOSBIOSVersion"]?.Value?.ToString() ?? "Desconhecido";
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
        }

        private void GetWindowsFeatureVersion()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        var displayVersion = key.GetValue("DisplayVersion")?.ToString();
                        WindowsFeatureVersion = displayVersion ?? "Desconhecido";
                    }
                    else
                    {
                        WindowsFeatureVersion = "Chave de registro não encontrada";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao acessar o registro: {ex.Message}");
                WindowsFeatureVersion = "Erro";
            }
        }



        private void GetOperatingSystemInfo()
        {
            try
            {
                string query = "SELECT Caption FROM Win32_OperatingSystem";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();
                    if (mo == null)
                    {
                        Console.WriteLine("Nenhuma informação de sistema operacional foi encontrada.");
                        return;
                    }

                    OperatingSystem = mo.Properties["Caption"]?.Value?.ToString() ?? "Desconhecido";
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
        }

        private void GetProcessorInfo()
        {
            try
            {
                string query = "SELECT Name, Manufacturer, CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();
                    if (mo == null)
                    {
                        Console.WriteLine("Nenhuma informação de processador foi encontrada.");
                        return;
                    }

                    ProcessorName = mo.Properties["Name"]?.Value?.ToString() ?? "Desconhecido";
                    ProcessorManufacturer = mo.Properties["Manufacturer"]?.Value?.ToString() ?? "Desconhecido";
                    ProcessorCurrentClockSpeed = mo.Properties["CurrentClockSpeed"]?.Value?.ToString() ?? "Desconhecido";
                    ProcessorMaxClockSpeed = mo.Properties["MaxClockSpeed"]?.Value?.ToString() ?? "Desconhecido";
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
        }

        private void GetGpuInfo()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    var ram = mo["AdapterRAM"] as uint?;
                    Gpus.Add(new Gpu
                    {
                        Caption = mo["Caption"]?.ToString(),
                        AdapterRAM = ram.HasValue ? $"{ram.Value / (1024 * 1024):N0} MB" : "N/A",
                        DriverVersion = mo["DriverVersion"]?.ToString(),
                        HorizontalResolution = (uint?)mo["CurrentHorizontalResolution"],
                        VerticalResolution = (uint?)mo["CurrentVerticalResolution"],
                        RefreshRate = (uint?)mo["CurrentRefreshRate"]
                    });
                }
            }
        }

        public String GetMemoryInfo()
        {
            String serial = "00000000";
            try
            {
                Memorias.Clear();
                UInt64 totalMemoryBytes = 0;

                // O dicionário usará o Serial Number como chave para agrupar as memórias.
                
                UInt64 onBoard = 0;
                var memoriaAgrupada = new Dictionary<string, Memoria>();

                string query = "SELECT Capacity, ConfiguredClockSpeed, Manufacturer, PartNumber, SerialNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory";

                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    foreach (ManagementObject mo in collection)
                    {
                        // 1. Coleta os dados do pente de memória atual
                        var capacityObj = mo["Capacity"];
                        ulong capacity = capacityObj != null ? Convert.ToUInt64(capacityObj) : 0;
                        totalMemoryBytes += capacity;

                        // Normaliza o serial para evitar problemas com espaços em branco
                        var serialNumber = mo["SerialNumber"]?.ToString()?.Trim() ?? "Desconhecido";

                        // 2. Verifica se uma memória com este serial JÁ EXISTE no dicionário
                        if (memoriaAgrupada.ContainsKey(serialNumber) )  //se repetir ou serial igual a 00000
                        {
                            // Se existe, APENAS SOMA a capacidade ao item existente
                            memoriaAgrupada[serialNumber].CapacityBytes += capacity;
                            serial = serialNumber.ToString();
                            //MessageBox.Show("Serial Repetido: "+serial);
                        }
                        else
                        {
                            // Se NÃO existe, cria um NOVO item e o adiciona ao dicionário
                            var clockSpeed = mo["ConfiguredClockSpeed"]?.ToString() ?? "Desconhecido";
                            var manufacturer = mo["Manufacturer"]?.ToString() ?? "Desconhecido";
                            var partNumber = mo["PartNumber"]?.ToString() ?? "Desconhecido";

                            string memoryType = "Desconhecido";
                            var memoryTypeObj = mo["SMBIOSMemoryType"];
                            if (memoryTypeObj != null && ushort.TryParse(memoryTypeObj.ToString(), out ushort typeCode))
                            {
                                memoryType = GetMemoryTypeString(typeCode);
                            }

                            //MessageBox.Show("Serial Ñ Repetido: " + serialNumber);
                            memoriaAgrupada.Add(serialNumber, new Memoria
                            {
                                CapacityBytes = capacity, // Define a capacidade inicial
                                ConfiguredClockSpeed = clockSpeed,
                                Manufacturer = manufacturer,
                                PartNumber = partNumber,
                                SerialNumber = serialNumber,
                                MemoryType = memoryType
                            });
                        }
                    }
                }

                // 3. Após o loop, o dicionário contém os dados agrupados.
                // Agora, converte os valores do dicionário para a lista final.
                if (memoriaAgrupada.ContainsKey(serial))
                {
                    // Se existe, APENAS SOMA a capacidade ao item existente
                    onBoard = memoriaAgrupada[serial].CapacityBytes;
                   
                }
                else if (memoriaAgrupada.ContainsKey("00000000"))
                {
                    onBoard = memoriaAgrupada[serial].CapacityBytes;
                }
                    Memorias = memoriaAgrupada.Values.ToList();

                // A formatação do total continua a mesma
                MemoryTotal = $"{Math.Round(totalMemoryBytes / 1073741824.0, 2)}GB";
                String MemoryOnBoard = $"{Math.Round(onBoard / 1073741824.0, 2)}GB";
                if (onBoard != 0) {
                    return (MemoryTotal + " " + Memorias[0].MemoryType + " " + Memorias[0].ConfiguredClockSpeed + "Mhz com "+ MemoryOnBoard+" OnBoard");
                }
                return (MemoryTotal+" "+ Memorias[0].MemoryType+" "+ Memorias[0].ConfiguredClockSpeed+ "Mhz");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro em GetMemoryInfo: {ex.Message}");
                MemoryTotal = "Erro ao ler memória"+ ex.Message;
                return MemoryTotal;
            }
        }


        // Não se esqueça da sua função auxiliar para traduzir o tipo de memória
        private string GetMemoryTypeStringold(ushort typeCode)
        {
            // ... seu switch case aqui ...
            switch (typeCode)
            {
                case 0x14: return "DDR";
                case 0x15: return "DDR2";
                case 0x18: return "DDR3";
                case 0x1D: return "LPDDR3";
                case 0x1A: return "DDR4";
                case 0x1E: return "LPDDR4";
                case 0x20: return "DDR5";
                case 0x21: return "LPDDR5";
                default: return "Desconhecido";
            }
        }

        public void GetDiskInfo()
        {
            HddInfo = new List<String>();
            StorageInfo storageInfo = new StorageInfo();
            HddInfo = storageInfo.Verifica();

            HddSizes = new List<String>();
            double convert = 0;
            ManagementClass mc = new ManagementClass("Win32_DiskDrive");
            ManagementObjectCollection moc = mc.GetInstances();
            if (moc.Count != 0)
            {
                foreach (ManagementObject mo in mc.GetInstances())
                {
                    if (!mo["InterfaceType"].ToString().Contains("USB"))
                    {

                        convert = Convert.ToDouble(mo["Size"]) / 1073741824;
                        //Console.WriteLine("Logical Disk Size = " + convert.ToString("F") + " Gygabytes");
                        HddSizes.Add(convert.ToString("F"));
                        //  HddName.Add(Convert.ToString(mo["Caption"]) + "/ " + convert.ToString("F") + " GB/ " + (mo["InterfaceType"]));
                    }

                }
            }


        }



        private void GetBatteryInfo()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery"))
            using (var collection = searcher.Get())
            {
                var mo = collection.OfType<ManagementObject>().FirstOrDefault();
                if (mo == null)
                {
                    BatteryStatus = "Nenhuma bateria encontrada.";
                    return;
                }
                var percentage = mo["EstimatedChargeRemaining"];
                BatteryStatus = $"Carga: {percentage}%";
            }
        }

        private void GetWindowsActivationInfo_Lento()
        {
            // Verificação de Status
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM SoftwareLicensingProduct WHERE ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' AND LicenseStatus = 1"))
                {
                    WindowsActivationStatus = searcher.Get().Count > 0 ? "Windows Ativado" : "Windows NÃO Ativado";
                }
            }
            catch { WindowsActivationStatus = "Status de ativação desconhecido"; }

            // Verificação de Chave
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM SoftwareLicensingService"))
                using (var collection = searcher.Get())
                {
                    var mo = collection.OfType<ManagementObject>().FirstOrDefault();
                    WindowsProductKey = mo?["OA3xOriginalProductKey"]?.ToString() ?? "Chave não encontrada na BIOS";
                }
            }
            catch { WindowsProductKey = "Erro ao ler a chave"; }
        }
        public static bool IsWindowsActivatedNEW()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cscript.exe",
                    Arguments = $"/nologo \"{Environment.SystemDirectory}\\slmgr.vbs\" /xpr",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    // Verifica se a saída contém a frase indicando ativação permanente
                    return output.IndexOf("permanently activated", StringComparison.OrdinalIgnoreCase) >= 0
                        || output.IndexOf("ativado permanentemente", StringComparison.OrdinalIgnoreCase) >= 0
                         || output.IndexOf("ativada permanentemente", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }


    

    #endregion

    #region Métodos Auxiliares
    // Reconstrói a lógica original da sua função VerLcdDetalhado

    private void BuildLcdString()
        {
            var primaryGpu = Gpus[0];
            if (Gpus.Count == 1)
            {
                
                goto geraString;
            }


            if (Gpus.Count > 1)
            {

                if (Gpus[0].HorizontalResolution == null) { primaryGpu = Gpus[1]; }
                if (Gpus[1].HorizontalResolution == null) { primaryGpu = Gpus[0]; }
                if (Gpus[1].HorizontalResolution > Gpus[0].HorizontalResolution)
                {
                    primaryGpu = Gpus[1];


                }

                goto geraString;
            }

            else
            {
                LcdDetalhado = "Resolução não disponível" + Gpus.Count;
                return;
            }

        geraString:

            var hRes = primaryGpu.HorizontalResolution.Value;
            var vRes = primaryGpu.VerticalResolution.Value;
            string resType = " ";

            if (hRes >= 5120) resType = "5K";
            else if (hRes >= 3840) resType = "4K UHD";
            else if (hRes >= 2880) resType = "2.8K QHD+";
            else if (hRes >= 2560) resType = "QHD/WQXGA";
            else if (hRes >= 2240 & vRes == 1400) resType = "2.2K";
            else if (hRes >= 1920 & vRes == 1200) resType = "WUXGA";
            else if (hRes >= 1920 & vRes == 1080) resType = "FHD";
            else if (hRes >= 1366) resType = "HD";



            string refresh = "";
            if (primaryGpu.RefreshRate > 60)
            {
                refresh = primaryGpu.RefreshRate + " Hz";
            }

            LcdDetalhado = $"{hRes} x {vRes} {resType}".Trim()+" "+ refresh;
            //  LcdDetalhado = Gpus[1].HorizontalResolution + "x";
        }
        private string GetMemoryTypeString(ushort typeCode)
        {
            switch (typeCode)
            {
                case 0x14: return "DDR";
                case 0x15: return "DDR2";
                case 0x18: return "DDR3";
                case 0x1A: return "DDR4";
                case 0x1E: return "LPDDR4";
                case 0x1D: return "LPDDR3";
                case 0x22: return "DDR5";
                case 0x23: return "LPDDR5";
                // Adicione outros casos conforme necessário
                default: return "Desconhecido";
            }
        }
        #endregion
    }
}