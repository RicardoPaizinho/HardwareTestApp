using System;
using System.Runtime.InteropServices; // Necessário para [ComVisible]
using System.Net.NetworkInformation;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Management;
//using Windows.Devices.Bluetooth;
//using Windows.Devices.Enumeration;


namespace HardwareTestApp
{
    [ComVisible(true)] // ESSENCIAL para o WebView2 reconhecer a classe
    public class NetworkService
    {
        // Classe auxiliar para enviar os dados de uma vez só para o Vue
        public class NetworkStatus
        {
            public bool LanOk { get; set; }
            public string LanDesc { get; set; }
            public bool WifiOk { get; set; }
            public string WifiDesc { get; set; }
            public string WifiSsid { get; set; }
            public int WifiSignal { get; set; }
            public bool BtOk { get; set; }
            public string BtDesc { get; set; }
            public bool InternetOk { get; set; }
        }

        public async Task<string> GetNetworkDataJson()
        {
            var status = new NetworkStatus();
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var ni in interfaces)
            {
                string desc = ni.Description.ToLower();
                bool isUp = ni.OperationalStatus == OperationalStatus.Up;

                if (desc.Contains("ethernet") || ni.Name.ToLower().Contains("ethernet"))
                {
                    status.LanOk = isUp;
                    status.LanDesc = ni.Description;
                }
                else if (desc.Contains("wi-fi") || desc.Contains("wireless") || ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    status.WifiOk = isUp;
                    status.WifiDesc = ni.Description;
                }
                else if (desc.Contains("bluetooth"))
                {
                    status.BtOk = isUp;
                    status.BtDesc = ni.Description;
                }
            }

            status.WifiSignal = GetWifiSignal();
            status.WifiSsid = GetWifiSsid();
            status.InternetOk = await CheckInternet();

            // Retornamos como JSON para facilitar no Vue
            return System.Text.Json.JsonSerializer.Serialize(status);
        }



        private int GetWifiSignal()
        {
            try
            {
                var output = RunNetsh("wlan show interfaces");

                // Este Regex agora procura por "Sinal" ou "Signal" (case-insensitive)
                var match = Regex.Match(output, @"(?:Sinal|Signal)\s*:\s*(\d+)%", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    return int.Parse(match.Groups[1].Value);
                }

                // Debug: Se continuar vindo 0, imprima o output no console para ver o que o Windows está retornando
                // Console.WriteLine(output); 

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
            return p.StandardOutput.ReadToEnd();
        }

        public async Task<List<string>> GetConnectedBluetoothDevicesAsync()
        {
            var devices = new List<string>();
            //var selector = BluetoothDevice.GetDeviceSelector();
          //  var deviceCollection = await DeviceInformation.FindAllAsync(selector);

            /*foreach (var device in deviceCollection)
            {
                if (device.Properties.ContainsKey("System.Devices.Aep.IsConnected") &&
                    (bool)device.Properties["System.Devices.Aep.IsConnected"])
                {
                    devices.Add(device.Name);
                }
            }   */
            return devices;
        }




    }
}