using System.Runtime.InteropServices; // Necessário para [ComVisible]
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Management;

namespace HardwareTestApp
{
    [ComVisible(true)] // ESSENCIAL para o WebView2 reconhecer a classe (uso legado)
    public class NetworkService
    {
        // Classe auxiliar para enviar os dados de uma vez só para o Vue
        public class NetworkStatus
        {
            public bool LanPresent { get; set; }
            public bool LanOk { get; set; }
            public string LanDesc { get; set; }

            public bool WifiPresent { get; set; }
            public bool WifiOk { get; set; }
            public string WifiDesc { get; set; }
            public string WifiSsid { get; set; }
            public int WifiSignal { get; set; }

            public bool BtOk { get; set; }
            public string BtDesc { get; set; }
            public List<string> BluetoothDevices { get; set; } = new List<string>();

            public bool InternetOk { get; set; }
        }

        // -----------------------------------------------------------------------------
        // Adaptadores "reais" (exclui loopback, túneis VPN, adaptadores virtuais/hyper-v,
        // que poluiriam a detecção de LAN/Wi-Fi com falsos positivos/negativos).
        // -----------------------------------------------------------------------------
        private static bool IsVirtualOrIrrelevant(NetworkInterface ni)
        {
            var desc = (ni.Description ?? "").ToLower();
            var name = (ni.Name ?? "").ToLower();
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) return true;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return true;

            string[] blockedTerms = { "virtual", "vpn", "hyper-v", "loopback", "vmware", "virtualbox",
                                       "tap-", "tailscale", "wsl", "bluetooth device (personal area network)" };

            return blockedTerms.Any(term => desc.Contains(term) || name.Contains(term));
        }

        public async Task<string> GetNetworkDataJson()
        {
            var status = new NetworkStatus();
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var ni in interfaces)
            {
                if (IsVirtualOrIrrelevant(ni)) continue;

                string desc = (ni.Description ?? "").ToLower();
                string name = (ni.Name ?? "").ToLower();
                bool isUp = ni.OperationalStatus == OperationalStatus.Up;

                bool looksLikeWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                                      || desc.Contains("wi-fi") || desc.Contains("wireless") || desc.Contains("wlan")
                                      || name.Contains("wi-fi") || name.Contains("wireless");

                bool looksLikeLan = !looksLikeWifi &&
                                     (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                                      || ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet
                                      || desc.Contains("ethernet") || name.Contains("ethernet"));

                if (looksLikeLan)
                {
                    status.LanPresent = true;
                    // Se houver múltiplos adaptadores LAN, priorizamos o que está conectado
                    if (isUp || !status.LanOk)
                    {
                        status.LanOk = isUp;
                        status.LanDesc = ni.Description;
                    }
                }
                else if (looksLikeWifi)
                {
                    status.WifiPresent = true;
                    if (isUp || !status.WifiOk)
                    {
                        status.WifiOk = isUp;
                        status.WifiDesc = ni.Description;
                    }
                }
            }

            // Estado do Wi-Fi (SSID/sinal), mesmo que a interface exista mas esteja desconectada
            if (status.WifiPresent)
            {
                status.WifiSignal = GetWifiSignal();
                status.WifiSsid = GetWifiSsid();
            }

            status.BluetoothDevices = GetPairedBluetoothDeviceNames();
            status.BtOk = status.BluetoothDevices.Count > 0;
            status.BtDesc = status.BtOk ? string.Join(", ", status.BluetoothDevices) : "Nenhum dispositivo pareado";

            status.InternetOk = await CheckInternet();

            // Retornamos como JSON para facilitar no Vue
            return System.Text.Json.JsonSerializer.Serialize(status);
        }

        private int GetWifiSignal()
        {
            try
            {
                var output = RunNetsh("wlan show interfaces");
                var match = Regex.Match(output, @"(?:Sinal|Signal)\s*:\s*(\d+)%", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return int.Parse(match.Groups[1].Value);
                }
                return 0;
            }
            catch { return 0; }
        }

        private string GetWifiSsid()
        {
            try
            {
                var output = RunNetsh("wlan show interfaces");
                var match = Regex.Match(output, @"^\s*SSID\s*:\s*(.*)$", RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value.Trim() : "Desconectado";
            }
            catch { return "N/A"; }
        }

        private async Task<bool> CheckInternet()
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync("8.8.8.8", 1500);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch { return false; }
        }

        private string RunNetsh(string args)
        {
            Process p = new Process();
            p.StartInfo.FileName = "netsh";
            p.StartInfo.Arguments = args;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.CreateNoWindow = true;
            p.Start();
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return output;
        }

        // -----------------------------------------------------------------------------
        // Bluetooth — o teste não é obrigatório, só precisamos exibir o(s) nome(s) dos
        // dispositivos pareados/conectados. Usamos WMI (Win32_PnPEntity) para não depender
        // das APIs UWP de Bluetooth (Windows.Devices.Bluetooth), que exigem projeto UWP/WinRT.
        // -----------------------------------------------------------------------------
        private List<string> GetPairedBluetoothDeviceNames()
        {
            var names = new List<string>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth' AND ConfigManagerErrorCode = 0"
                );

                foreach (ManagementBaseObject dev in searcher.Get())
                {
                    var deviceName = dev["Name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(deviceName)) continue;

                    // Filtra o próprio adaptador/rádio Bluetooth do PC (não é um "dispositivo pareado")
                    var lower = deviceName.ToLower();
                    if (lower.Contains("bluetooth radio") || lower.Contains("bluetooth adapter") ||
                        lower.Contains("bluetooth device (rfcomm") || lower.Contains("generic bluetooth"))
                        continue;

                    if (!names.Contains(deviceName)) names.Add(deviceName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bluetooth] Falha ao consultar dispositivos pareados: {ex.Message}");
            }
            return names;
        }

        // Mantido por compatibilidade com chamadas antigas via WebView2 hostObjects.
        public async Task<List<string>> GetConnectedBluetoothDevicesAsync()
        {
            return await Task.FromResult(GetPairedBluetoothDeviceNames());
        }
    }
}
