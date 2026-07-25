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
                // Usa a instância única compartilhada por todo o projeto (HardwareMonitorSingleton).
                // Antes este método abria E FECHAVA seu próprio Computer, o que derrubava o
                // driver Ring0 compartilhado e quebrava qualquer outra instância aberta em
                // paralelo (ex: a do BackendAgent) — causando NullReferenceException em
                // Update() nos ciclos seguintes.
                var computer = HardwareMonitorSingleton.Instance;
                HardwareMonitorSingleton.RefreshAll();

                // Percorre cada componente de hardware detectado (CPU, GPU, etc.)
                foreach (IHardware hardware in computer.Hardware)
                {
                    // Percorre sub-hardwares (ex: canais específicos em algumas placas)
                    foreach (IHardware subHardware in hardware.SubHardware)
                    {
                        // Já atualizado por HardwareMonitorSingleton.RefreshAll()
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

            // NOTA: NÃO fechamos o Computer aqui. Ele só é fechado uma vez, no
            // encerramento do processo, via HardwareMonitorSingleton.Shutdown()
            // (chamado em BackendAgent.Dispose()).

            return hardwareInfo;
        }
    }

}

