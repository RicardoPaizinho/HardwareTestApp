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

            try
            {
                // Usa a instância única compartilhada por todo o projeto — nunca abre
                // nem fecha um Computer próprio aqui (isso fechava o driver Ring0 pra
                // todo o processo, quebrando o BackendAgent com NullReferenceException).
                Computer computer = HardwareMonitorSingleton.Instance;
                HardwareMonitorSingleton.RefreshAll();

                // Percorre cada componente de hardware detectado (CPU, GPU, etc.)
                foreach (IHardware hardware in computer.Hardware)
                {
                    // Percorre sub-hardwares (ex: canais específicos em algumas placas)
                    foreach (IHardware subHardware in hardware.SubHardware)
                    {
                        // já atualizados por HardwareMonitorSingleton.RefreshAll()
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
            // SEM finally/Close() aqui — quem encerra o Computer é sempre
            // HardwareMonitorSingleton.Shutdown(), uma única vez, no fim do processo.

            return hardwareInfo;
        }
    }

}