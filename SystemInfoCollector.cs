// =====================================================================================
// SystemInfoCollector.cs
// -------------------------------------------------------------------------------------
// Coleta informações de hardware do PC (BIOS, Placa-mãe, Áudio, Bateria, Câmera,
// Monitor/LCD, Wi-Fi, Bluetooth, Lan, GPU e Sistema Operacional).
//
// FONTES DE DADOS:
//   - WMI (System.Management)      -> usado para quase tudo (root\CIMV2 e root\WMI)
//   - LibreHardwareMonitorLib      -> usado para complementar a GPU (VRAM real, uso,
//                                     temperatura), pois o WMI (Win32_VideoController)
//                                     tem limitações conhecidas (AdapterRAM estoura em
//                                     placas com mais de 4 GB, por exemplo).
//
// PACOTES NUGET NECESSÁRIOS:
//   Install-Package System.Management
//   Install-Package LibreHardwareMonitorLib
//
// OBSERVAÇÕES IMPORTANTES:
//   1) Rode a aplicação como Administrador. Sem isso, o LibreHardwareMonitor não
//      consegue acessar os sensores de hardware (GPU/CPU) e algumas classes WMI de
//      bateria (root\WMI) podem retornar vazio.
//   2) Resolução de câmera: o WMI não expõe resolução de captura. Para obter isso de
//      verdade seria necessário usar DirectShow/Media Foundation (ex.: AForge.NET ou
//      MediaFoundation via P/Invoke). Deixei o campo no DTO com um comentário indicando
//      essa limitação.
//   3) "Product Key" do Windows: strings de licença modernas (OEM/Retail) normalmente
//      não podem ser lidas em texto puro por questões de segurança do próprio Windows;
//      o valor disponível via WMI (SoftwareLicensingService.OA3xOriginalProductKey) é
//      a chave OEM gravada na BIOS (quando existir).
// =====================================================================================

using LibreHardwareMonitor.Hardware;
using System.Management;

namespace HardwareTestApp
{
    #region DTOs (modelos de dados)

    public class BiosInfo
    {
        public string SerialNumber { get; set; }
        public string Manufacturer { get; set; }
        public string ProductName { get; set; }
        public string ProductVersion { get; set; }
        public string BiosVersion { get; set; }
        public string BiosReleaseDate { get; set; }
        public string BiosManufacturer { get; set; }
    }

    public class MotherboardInfo
    {
        public string DeviceName { get; set; }
        public string BaseBoardVersion { get; set; }
        public string BaseBoardProduct { get; set; }
        public string SerialNumber { get; set; }
    }

    public class AudioInfo
    {
        public string DeviceName { get; set; }
        public string Manufacturer { get; set; }
        public string Bus { get; set; }
        public string DriverVersion { get; set; }
    }

    public class BatteryInfo
    {
        public string DeviceName { get; set; }
        public string ManufacturerName { get; set; }
        public string ChemistryType { get; set; }
        public string SerialNumber { get; set; }
        public string ChargingStatus { get; set; }
        public double? CurrentCapacityPercent { get; set; }
        public double? CurrentCapacityWh { get; set; }
        public double? FullChargeCapacityWh { get; set; }
        public double? DesignedCapacityWh { get; set; }
        public double? BatteryHealthPercent { get; set; }
        public double? VoltageV { get; set; }

        // --- Tempo real (root\WMI BatteryStatus) — usado pelo teste de carga/descarga ---
        public bool? IsCharging { get; set; }
        public bool? IsDischarging { get; set; }
        public bool? IsPowerOnline { get; set; }
        public double? ChargeRateW { get; set; }
        public double? DischargeRateW { get; set; }
    }

    public class CameraInfo
    {
        public string DeviceName { get; set; }
        public string Description { get; set; }
        // Resolução não é exposta pelo WMI. Necessário DirectShow/MediaFoundation.
        public string Resolution { get; set; } = "Não disponível via WMI";
    }

    public class LcdInfo
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string Manufacturer { get; set; }
        public string ManufactureDate { get; set; }
        public string EdidVersion { get; set; }
        public string MaxResolution { get; set; }
        public string MaxScreenSize { get; set; }
        public string InputType { get; set; }
        public double? FrequencyHz { get; set; }
    }

    public class WifiInfo
    {
        public string DeviceName { get; set; }
        public string Manufacturer { get; set; }
        public string MacAddress { get; set; }
    }

    public class BluetoothInfo
    {
        public string Name { get; set; }
        public string Status { get; set; }
    }

    public class LanInfo
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string MacAddress { get; set; }
    }

    public class GpuInfo
    {
        public string Model { get; set; }
        public string MemoriaVideo { get; set; }     // via WMI
        public double? MemoriaVideoTotalMB { get; set; } // via LibreHardwareMonitor
        public double? MemoriaVideoUsadaMB { get; set; } // via LibreHardwareMonitor
        public double? UsoPercent { get; set; }          // via LibreHardwareMonitor
        public double? TemperaturaC { get; set; }        // via LibreHardwareMonitor
    }

    public class SystemOsInfo
    {
        public string Sistema { get; set; }
        public string Ativacao { get; set; }
        public string Architecture { get; set; }
        public string Build { get; set; }
        public string ProductKey { get; set; }
        public string Version { get; set; }
    }

    public class FullHardwareReport
    {
        public BiosInfo Bios { get; set; }
        public MotherboardInfo Motherboard { get; set; }
        public List<AudioInfo> Audio { get; set; }
        public List<BatteryInfo> Baterias { get; set; }
        public List<CameraInfo> Cameras { get; set; }
        public List<LcdInfo> Monitores { get; set; }
        public List<WifiInfo> WifiAdapters { get; set; }
        public List<BluetoothInfo> BluetoothDevices { get; set; }
        public List<LanInfo> LanAdapters { get; set; }
        public GpuInfo Gpu { get; set; }
        public SystemOsInfo Sistema { get; set; }
    }

    #endregion

    public class SystemInfoCollector
    {
        // -----------------------------------------------------------------------------
        // Helper genérico para consultar WMI e devolver os objetos já "materializados"
        // -----------------------------------------------------------------------------
        private static List<ManagementBaseObject> Query(string wql, string scope = @"root\CIMV2")
        {
            var results = new List<ManagementBaseObject>();
            try
            {
                using var searcher = new ManagementObjectSearcher(scope, wql);
                foreach (ManagementBaseObject obj in searcher.Get())
                    results.Add(obj);
            }
            catch (Exception ex)
            {
              //  Console.WriteLine($"[WMI] Falha ao consultar '{wql}' em '{scope}': {ex.Message}");
            }
            return results;
        }

        private static string GetProp(ManagementBaseObject obj, string prop)
        {
            try
            {
                var value = obj[prop];
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }

        // -----------------------------------------------------------------------------
        // BIOS
        // -----------------------------------------------------------------------------
        public BiosInfo GetBiosInfo()
        {
            var info = new BiosInfo();

            var bios = Query("SELECT * FROM Win32_BIOS").FirstOrDefault();
            if (bios != null)
            {
                info.SerialNumber = GetProp(bios, "SerialNumber");
                info.BiosManufacturer = GetProp(bios, "Manufacturer");
                info.BiosVersion = GetProp(bios, "SMBIOSBIOSVersion");

                var releaseDateRaw = GetProp(bios, "ReleaseDate"); // formato WMI CIM_DATETIME
                info.BiosReleaseDate = ConvertWmiDate(releaseDateRaw);
            }

            var product = Query("SELECT * FROM Win32_ComputerSystemProduct").FirstOrDefault();
            if (product != null)
            {
                info.Manufacturer = GetProp(product, "Vendor");
                info.ProductName = GetProp(product, "Name");
                info.ProductVersion = GetProp(product, "Version");
            }

            return info;
        }

        // -----------------------------------------------------------------------------
        // PLACA-MÃE
        // -----------------------------------------------------------------------------
        public MotherboardInfo GetMotherboardInfo()
        {
            var info = new MotherboardInfo();

            var board = Query("SELECT * FROM Win32_BaseBoard").FirstOrDefault();
            if (board != null)
            {
                info.DeviceName = $"{GetProp(board, "Manufacturer")} {GetProp(board, "Product")}";
                info.BaseBoardVersion = GetProp(board, "Version");
                info.BaseBoardProduct = GetProp(board, "Product");
                info.SerialNumber = GetProp(board, "SerialNumber");
            }

            return info;
        }

        // -----------------------------------------------------------------------------
        // ÁUDIO
        // -----------------------------------------------------------------------------
        public List<AudioInfo> GetAudioDevices()
        {
            var list = new List<AudioInfo>();

            foreach (var dev in Query("SELECT * FROM Win32_SoundDevice"))
            {
                var audio = new AudioInfo
                {
                    DeviceName = GetProp(dev, "Name"),
                    Manufacturer = GetProp(dev, "Manufacturer")
                };

                // Tenta achar o driver correspondente (barramento + versão) via Win32_PnPSignedDriver
                var pnpId = GetProp(dev, "PNPDeviceID");
                if (!string.IsNullOrEmpty(pnpId))
                {
                    var driver = Query(
                        $"SELECT * FROM Win32_PnPSignedDriver WHERE DeviceID = '{pnpId.Replace("'", "''")}'"
                    ).FirstOrDefault();

                    if (driver != null)
                    {
                        audio.DriverVersion = GetProp(driver, "DriverVersion");
                        audio.Bus = GetProp(driver, "Description"); // aproximação; WMI não expõe "barramento" diretamente
                    }
                }

                list.Add(audio);
            }

            return list;
        }

        // -----------------------------------------------------------------------------
        // BATERIA
        // Combina Win32_Battery (dados básicos) com as classes de root\WMI
        // (BatteryStaticData, BatteryFullChargedCapacity, BatteryStatus) para os
        // detalhes de capacidade/saúde/tensão, exatamente como o HWiNFO/BatteryInfoView
        // fazem.
        // -----------------------------------------------------------------------------
        public List<BatteryInfo> GetBatteries()
        {
            var list = new List<BatteryInfo>();

            var batteries = Query("SELECT * FROM Win32_Battery");
            var staticData = Query("SELECT * FROM BatteryStaticData", @"root\WMI").FirstOrDefault();
            var fullCharge = Query("SELECT * FROM BatteryFullChargedCapacity", @"root\WMI").FirstOrDefault();
            var status = Query("SELECT * FROM BatteryStatus", @"root\WMI").FirstOrDefault();

            foreach (var bat in batteries)
            {
                var info = new BatteryInfo
                {
                    DeviceName = GetProp(bat, "Name"),
                    ChemistryType = MapBatteryChemistry(GetProp(bat, "Chemistry")),
                    ChargingStatus = MapBatteryStatus(GetProp(bat, "BatteryStatus"))
                };

                if (int.TryParse(GetProp(bat, "EstimatedChargeRemaining"), out int pct))
                    info.CurrentCapacityPercent = pct;

                if (staticData != null)
                {
                    info.ManufacturerName = GetProp(staticData, "ManufacturerName");
                    info.SerialNumber = GetProp(staticData, "SerialNumber");

                    if (uint.TryParse(GetProp(staticData, "DesignedCapacity"), out uint designed))
                        info.DesignedCapacityWh = designed / 1000.0;
                }

                if (fullCharge != null && uint.TryParse(GetProp(fullCharge, "FullChargedCapacity"), out uint full))
                {
                    info.FullChargeCapacityWh = full / 1000.0;

                    if (info.DesignedCapacityWh.HasValue && info.DesignedCapacityWh.Value > 0)
                        info.BatteryHealthPercent = Math.Round(full / 1000.0 / info.DesignedCapacityWh.Value * 100.0, 2);
                }

                if (status != null)
                {
                    if (uint.TryParse(GetProp(status, "RemainingCapacity"), out uint remaining))
                        info.CurrentCapacityWh = remaining / 1000.0;

                    if (uint.TryParse(GetProp(status, "Voltage"), out uint voltageMv))
                        info.VoltageV = Math.Round(voltageMv / 1000.0, 2);

                    if (info.CurrentCapacityWh.HasValue && info.FullChargeCapacityWh.HasValue && info.FullChargeCapacityWh > 0)
                        info.CurrentCapacityPercent = Math.Round(
                            info.CurrentCapacityWh.Value / info.FullChargeCapacityWh.Value * 100.0, 2);

                    // --- Estado de carga/descarga e taxa em tempo real ---
                    // A classe BatteryStatus é atualizada pelo Windows continuamente, então uma
                    // nova consulta a cada chamada já reflete o estado atual (sem cache).
                    if (bool.TryParse(GetProp(status, "Charging"), out bool charging))
                        info.IsCharging = charging;

                    if (bool.TryParse(GetProp(status, "Discharging"), out bool discharging))
                        info.IsDischarging = discharging;

                    if (bool.TryParse(GetProp(status, "PowerOnline"), out bool powerOnline))
                        info.IsPowerOnline = powerOnline;

                    // ChargeRate/DischargeRate vêm em mW (miliwatts)
                    if (uint.TryParse(GetProp(status, "ChargeRate"), out uint chargeRateMw) && chargeRateMw > 0)
                        info.ChargeRateW = Math.Round(chargeRateMw / 1000.0, 2);

                    if (uint.TryParse(GetProp(status, "DischargeRate"), out uint dischargeRateMw) && dischargeRateMw > 0)
                        info.DischargeRateW = Math.Round(dischargeRateMw / 1000.0, 2);

                    // Alguns fabricantes não populam Charging/Discharging corretamente;
                    // usamos PowerOnline + variação como fallback para não travar o teste.
                    if (info.IsCharging == null && info.IsDischarging == null)
                    {
                        info.IsCharging = info.IsPowerOnline == true;
                        info.IsDischarging = info.IsPowerOnline == false;
                    }
                }

                list.Add(info);
            }

            return list;
        }

        private static string MapBatteryChemistry(string code) => code switch
        {
            "1" => "Outra",
            "2" => "Desconhecida",
            "3" => "LeadAcid",
            "4" => "NiCd",
            "5" => "NiMH",
            "6" => "LIon",
            "7" => "Zinc Air",
            "8" => "LiPo",
            _ => code
        };

        private static string MapBatteryStatus(string code) => code switch
        {
            "1" => "Descarregando",
            "2" => "Carregando",
            "3" => "Totalmente carregada",
            "4" => "Baixa",
            "5" => "Crítica",
            "6" => "Carregando_",
            "7" => "Carregando e alta",
            "8" => "Carregando e baixa",
            "9" => "Carregando e crítica",
            "10" => "Desconhecida",
            "11" => "Parcialmente carregada",
            _ => code
        };

        // -----------------------------------------------------------------------------
        // CÂMERA
        // -----------------------------------------------------------------------------
        public List<CameraInfo> GetCameras()
        {
            var list = new List<CameraInfo>();

            // PNPClass "Image" ou "Camera" cobre a maioria das webcams (UVC)
            var devices = Query(
                "SELECT * FROM Win32_PnPEntity WHERE (PNPClass = 'Camera' OR PNPClass = 'Image') AND ConfigManagerErrorCode = 0"
            );

            foreach (var dev in devices)
            {
                list.Add(new CameraInfo
                {
                    DeviceName = GetProp(dev, "Name"),
                    Description = GetProp(dev, "Description")
                });
            }

            return list;
        }

        // -----------------------------------------------------------------------------
        // MONITOR / LCD (via root\WMI: WmiMonitorID e WmiMonitorBasicDisplayParams)
        // -----------------------------------------------------------------------------
        public List<LcdInfo> GetDisplays()
        {
          //  Aplicando o Teorema de Pitágoras: A diagonal(d) é calculada elevando a largura e a 
           //     altura ao quadrado:\(d = \sqrt{ 31 ^ 2 + 17 ^ 2}\)\(d = \sqrt{ 961 + 289}\)\(d = \sqrt{ 1250} \approx 35,35 \text{ cm}\)Convertendo para Polegadas: Como 1 polegada equivale a 2,54 cm, basta dividir o valor da diagonal:35,35 cm ÷ 2,54 = 13,91 polegadas.
            var list = new List<LcdInfo>();

            var ids = Query("SELECT * FROM WmiMonitorID", @"root\WMI");
            var basicParams = Query("SELECT * FROM WmiMonitorBasicDisplayParams", @"root\WMI");

            foreach (var idObj in ids)
            {
                var instanceName = GetProp(idObj, "InstanceName");

                var lcd = new LcdInfo
                {
                    DeviceId = ExtractDisplayId(instanceName),
                    Manufacturer = DecodeEdidString(idObj, "ManufacturerName"),
                    DeviceName = DecodeEdidString(idObj, "UserFriendlyName") is string ufn && !string.IsNullOrWhiteSpace(ufn)
                        ? ufn
                        : "Generic LCD",
                };

                if (ushort.TryParse(GetProp(idObj, "WeekOfManufacture"), out ushort week) &&
                    ushort.TryParse(GetProp(idObj, "YearOfManufacture"), out ushort year))
                {
                    lcd.ManufactureDate = $"{year}, week {week}";
                }

                var basic = basicParams.FirstOrDefault(b => GetProp(b, "InstanceName") == instanceName);
                if (basic != null)
                {
                    if (byte.TryParse(GetProp(basic, "MaxHorizontalImageSize"), out byte width) &&
                        byte.TryParse(GetProp(basic, "MaxVerticalImageSize"), out byte height))
                    {
                        lcd.MaxScreenSize = $"{width} cm x {height} cm";
                    }

                    var videoInputType = GetProp(basic, "VideoInputType");
                    lcd.InputType = videoInputType == "1" ? "Digital" : "Analógico";
                }

                list.Add(lcd);
            }

            return list;
        }

        // WmiMonitorID guarda strings como arrays de bytes (código EDID); decodifica para texto legível.
        private static string DecodeEdidString(ManagementBaseObject obj, string prop)
        {
            try
            {
                if (obj[prop] is ushort[] raw)
                {
                    var chars = raw.Where(c => c != 0).Select(c => (char)c).ToArray();
                    return new string(chars).Trim();
                }
            }
            catch { }
            return null;
        }

        private static string ExtractDisplayId(string instanceName)
        {
            // Instance name normalmente é algo como "DISPLAY\AUO1234\4&1a2b3c4d&0&UID..."
            if (string.IsNullOrEmpty(instanceName)) return null;
            var parts = instanceName.Split('\\');
            return parts.Length > 0 ? parts[0] : instanceName;
        }

        // -----------------------------------------------------------------------------
        // WI-FI / BLUETOOTH / LAN (todos via Win32_NetworkAdapter / Win32_PnPEntity)
        // -----------------------------------------------------------------------------
        public List<WifiInfo> GetWifiAdapters()
        {
            var list = new List<WifiInfo>();

            var adapters = Query(
                "SELECT * FROM Win32_NetworkAdapter WHERE (Name LIKE '%Wireless%' OR Name LIKE '%Wi-Fi%' OR Name LIKE '%WLAN%') AND PhysicalAdapter = TRUE"
            );

            foreach (var adapter in adapters)
            {
                list.Add(new WifiInfo
                {
                    DeviceName = GetProp(adapter, "Name"),
                    Manufacturer = GetProp(adapter, "Manufacturer"),
                    MacAddress = GetProp(adapter, "MACAddress")
                });
            }

            return list;
        }

        public List<BluetoothInfo> GetBluetoothDevices()
        {
            var list = new List<BluetoothInfo>();

            var devices = Query(
                "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth' AND ConfigManagerErrorCode = 0"
            );

            foreach (var dev in devices)
            {
                list.Add(new BluetoothInfo
                {
                    Name = GetProp(dev, "Name"),
                    Status = GetProp(dev, "Status")
                });
            }

            return list;
        }

        public List<LanInfo> GetLanAdapters()
        {
            var list = new List<LanInfo>();

            var adapters = Query(
                "SELECT * FROM Win32_NetworkAdapter WHERE AdapterTypeId = 0 AND PhysicalAdapter = TRUE AND (Name LIKE '%Ethernet%' OR Name LIKE '%Gigabit%' OR Name LIKE '%Realtek%')"
            );

            foreach (var adapter in adapters)
            {
                var statusCode = GetProp(adapter, "NetConnectionStatus");
                list.Add(new LanInfo
                {
                    Name = GetProp(adapter, "Name"),
                    Status = MapNetConnectionStatus(statusCode),
                    MacAddress = GetProp(adapter, "MACAddress")
                });
            }

            return list;
        }

        private static string MapNetConnectionStatus(string code) => code switch
        {
            "0" => "Desconectado",
            "1" => "Conectando",
            "2" => "Conectado",
            "3" => "Desconectando",
            "4" => "Hardware não presente",
            "5" => "Hardware desabilitado",
            "6" => "Falha no hardware",
            "7" => "Mídia desconectada",
            "9" => "Conectado (autenticando)",
            "11" => "Falha na autenticação",
            _ => "Desconhecido"
        };

        // -----------------------------------------------------------------------------
        // GPU — combina WMI (nome/registro básico) com LibreHardwareMonitor
        // (uso real, temperatura e VRAM sem os limites do WMI)
        // -----------------------------------------------------------------------------
        public GpuInfo GetGpuInfo()
        {
            var info = new GpuInfo();

            var gpu = Query("SELECT * FROM Win32_VideoController").FirstOrDefault();
            if (gpu != null)
            {
                info.Model = GetProp(gpu, "Name");

                if (uint.TryParse(GetProp(gpu, "AdapterRAM"), out uint ramBytes))
                {
                    var ramMb = ramBytes / 1024.0 / 1024.0;
                    info.MemoriaVideo = $"{Math.Round(ramMb)} MB";
                }
            }

            // Complemento via LibreHardwareMonitor (requer execução como Administrador).
            // Usa a instância única compartilhada por todo o projeto — NUNCA cria/abre/fecha
            // um Computer próprio aqui. Fazer isso derruba o driver Ring0 compartilhado e
            // quebra qualquer outra instância que já esteja aberta (ex: a do BackendAgent),
            // causando NullReferenceException em Update() nos ciclos seguintes.
            try
            {
                var computer = HardwareMonitorSingleton.Instance;
                HardwareMonitorSingleton.RefreshAll();

                foreach (var hardware in computer.Hardware)
                {
                    if (hardware.HardwareType is HardwareType.GpuNvidia
                                              or HardwareType.GpuAmd
                                              or HardwareType.GpuIntel)
                    {
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Core"))
                                info.UsoPercent = sensor.Value;

                            if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Core"))
                                info.TemperaturaC = sensor.Value;

                            if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Used"))
                                info.MemoriaVideoUsadaMB = sensor.Value;

                            if (sensor.SensorType == SensorType.SmallData && sensor.Name.Contains("Memory Total"))
                                info.MemoriaVideoTotalMB = sensor.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LibreHardwareMonitor] Falha ao ler sensores de GPU: {ex.Message}");
                Console.WriteLine("Dica: execute a aplicação como Administrador.");
            }

            return info;
        }

        // -----------------------------------------------------------------------------
        // SISTEMA OPERACIONAL
        // -----------------------------------------------------------------------------
        public SystemOsInfo GetSystemInfo()
        {
            var info = new SystemOsInfo();

            var os = Query("SELECT * FROM Win32_OperatingSystem").FirstOrDefault();
            if (os != null)
            {
                info.Sistema = GetProp(os, "Caption");
                info.Architecture = GetProp(os, "OSArchitecture");
                info.Build = GetProp(os, "BuildNumber");
                info.Version = GetProp(os, "Version");
            }

            var licensing = Query(
                "SELECT * FROM SoftwareLicensingProduct WHERE ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL"
            ).FirstOrDefault();

            if (licensing != null)
            {
                var licenseStatus = GetProp(licensing, "LicenseStatus");
                info.Ativacao = licenseStatus == "1" ? "Ativado" : "Não Ativado";
            }

            // Chave de produto original gravada na BIOS (OEM), se existir
            var oa3 = Query("SELECT * FROM SoftwareLicensingService").FirstOrDefault();
            if (oa3 != null)
            {
                var key = GetProp(oa3, "OA3xOriginalProductKey");
                info.ProductKey = string.IsNullOrWhiteSpace(key) ? "Não disponível" : key;
            }

            return info;
        }

        // -----------------------------------------------------------------------------
        // Converte data no formato WMI (CIM_DATETIME, ex: "20240316000000.000000+000")
        // para "MM/dd/yyyy"
        // -----------------------------------------------------------------------------
        private static string ConvertWmiDate(string wmiDate)
        {
            if (string.IsNullOrEmpty(wmiDate)) return null;
            try
            {
                var dt = ManagementDateTimeConverter.ToDateTime(wmiDate);
                return dt.ToString("MM/dd/yyyy");
            }
            catch
            {
                return wmiDate;
            }
        }

        // -----------------------------------------------------------------------------
        // Executa a coleta completa
        // -----------------------------------------------------------------------------
        public FullHardwareReport CollectAll()
        {
            return new FullHardwareReport
            {
                Bios = GetBiosInfo(),
                Motherboard = GetMotherboardInfo(),
                Audio = GetAudioDevices(),
                Baterias = GetBatteries(),
                Cameras = GetCameras(),
                Monitores = GetDisplays(),
                WifiAdapters = GetWifiAdapters(),
                BluetoothDevices = GetBluetoothDevices(),
                LanAdapters = GetLanAdapters(),
                Gpu = GetGpuInfo(),
                Sistema = GetSystemInfo()
            };
        }
    }

    // ===================================================================================
    // Exemplo de uso (pode remover essa classe se for consumir SystemInfoCollector
    // em outro projeto/Debug/serviço)
    // ===================================================================================
    class Program22
    {
        static void Main22()
        {
            Console.WriteLine("Coletando informações de hardware... (execute como Administrador)\n");

            var collector = new SystemInfoCollector();
            var report = collector.CollectAll();

            Console.WriteLine("=== BIOS ===");
            Console.WriteLine($"Fabricante: {report.Bios.Manufacturer}");
            Console.WriteLine($"Produto: {report.Bios.ProductName}");
            Console.WriteLine($"Serial: {report.Bios.SerialNumber}");
            Console.WriteLine($"BIOS: {report.Bios.BiosVersion} ({report.Bios.BiosReleaseDate}) - {report.Bios.BiosManufacturer}");

            Console.WriteLine("\n=== PLACA-MÃE ===");
            Console.WriteLine($"{report.Motherboard.DeviceName} | Serial: {report.Motherboard.SerialNumber}");

            Console.WriteLine("\n=== ÁUDIO ===");
            foreach (var a in report.Audio)
                Console.WriteLine($"{a.DeviceName} ({a.Manufacturer}) - Driver: {a.DriverVersion}");

            Console.WriteLine("\n=== BATERIA ===");
            foreach (var b in report.Baterias)
                Console.WriteLine($"{b.DeviceName} | {b.ChargingStatus} | {b.CurrentCapacityPercent}% | Saúde: {b.BatteryHealthPercent}% | {b.VoltageV} V");

            Console.WriteLine("\n=== CÂMERA ===");
            foreach (var c in report.Cameras)
                Console.WriteLine($"{c.DeviceName} - {c.Resolution}");

            Console.WriteLine("\n=== MONITOR =======");
            foreach (var m in report.Monitores) {
                Console.WriteLine($"{m.DeviceName} ({m.Manufacturer}) | {m.MaxScreenSize} | {m.InputType}");
                Console.WriteLine($"{m.DeviceId}" + "TESTE" );
            }

            Console.WriteLine("\n=== WI-FI ===");
            foreach (var w in report.WifiAdapters)
                Console.WriteLine($"{w.DeviceName} | MAC: {w.MacAddress}");

            Console.WriteLine("\n=== BLUETOOTH ===");
            foreach (var bt in report.BluetoothDevices)
                Console.WriteLine($"{bt.Name} - {bt.Status}");

            Console.WriteLine("\n=== LAN ===");
            foreach (var l in report.LanAdapters)
                Console.WriteLine($"{l.Name} - {l.Status}");

            Console.WriteLine("\n=== GPU ===");
            Console.WriteLine($"{report.Gpu.Model} | VRAM (WMI): {report.Gpu.MemoriaVideo} | VRAM (LHM): {report.Gpu.MemoriaVideoTotalMB} MB | Uso: {report.Gpu.UsoPercent}% | Temp: {report.Gpu.TemperaturaC}°C");

            Console.WriteLine("\n=== SISTEMA ===");
            Console.WriteLine($"{report.Sistema.Sistema} | {report.Sistema.Architecture} | Build {report.Sistema.Build} | Versão {report.Sistema.Version} | {report.Sistema.Ativacao}");
            Console.WriteLine($"Chave OEM: {report.Sistema.ProductKey}");
        }
    
    
    }
}
