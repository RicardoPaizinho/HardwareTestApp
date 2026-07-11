using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HardwareTestApp
{
    using System;
    using System.Collections.Generic;
    using LibreHardwareMonitor.Hardware;

    public class HardwareMonitorService
    {
        public static Dictionary<string, string> GetLHMInfo()
        {
            var hardwareInfo = new Dictionary<string, string>();

            // Inicializa o objeto principal que gerencia o hardware
            Computer computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsPowerMonitorEnabled = true,
                IsStorageEnabled = true
            };

            try
            {
                computer.Open();

                // Percorre cada componente de hardware detectado (CPU, GPU, etc.)
                foreach (IHardware hardware in computer.Hardware)
                {
                    // Força a atualização dos dados do componente
                    hardware.Update();

                    // Percorre sub-hardwares (ex: canais específicos em algumas placas)
                    foreach (IHardware subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                    }

                    // Percorre todos os sensores disponíveis neste hardware
                    foreach (ISensor sensor in hardware.Sensors)
                    {
                        // Cria uma chave estruturada e legível
                        // Exemplo: [CPU] AMD Ryzen 5 5600X - CPU Core #1 (Temperature)
                        string key = $"[{hardware.HardwareType}] {hardware.Name} - {sensor.Name} ({sensor.SensorType})";

                        // Formata o valor atual do sensor (evita nulos)
                        string value = sensor.Value?.ToString("F1") ?? "N/A";

                        if (!hardwareInfo.ContainsKey(key))
                        {
                            hardwareInfo.Add(key, value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                hardwareInfo.Add("Erro de Leitura", ex.Message);
            }
            finally
            {
                // Sempre fechar para liberar os drivers e recursos do sistema
                computer.Close();
            }

            return hardwareInfo;
        }
    }

}
