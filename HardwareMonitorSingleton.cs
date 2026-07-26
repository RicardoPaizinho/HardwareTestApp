using System;
using LibreHardwareMonitor.Hardware;

namespace HardwareTestApp
{
    // -----------------------------------------------------------------------------
    // ÚNICA instância de LibreHardwareMonitor.Hardware.Computer para todo o processo.
    //
    // O LibreHardwareMonitor usa um driver de kernel (Ring0) compartilhado pelo
    // processo inteiro para ler MSRs de CPU/GPU. Quando duas partes do código abrem
    // suas próprias instâncias de Computer (ex: BackendAgent E SystemInfoCollector.
    // GetGpuInfo() E HardwareMonitorService), e uma delas chama Close() na sua
    // instância, isso derruba o driver Ring0 para TODO o processo — inclusive para
    // as outras instâncias que continuam "abertas". A próxima leitura de sensor
    // (hardware.Update()) então bate num ponteiro nulo internamente, gerando:
    //   System.NullReferenceException em LibreHardwareMonitor.Hardware.Cpu.GenericCpu.Update()
    //
    // Regra do projeto: NUNCA crie outro "new Computer()" em nenhum outro arquivo.
    // Sempre use HardwareMonitorSingleton.Instance, e só chame Shutdown() uma única
    // vez, ao encerrar o processo (BackendAgent.Dispose()).
    // -----------------------------------------------------------------------------
    public static class HardwareMonitorSingleton
    {
        private static readonly object _lock = new object();
        private static Computer _computer;
        private static bool _isOpen = false;

        public static Computer Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_computer == null)
                    {
                        _computer = new Computer
                        {
                            IsCpuEnabled = true,
                            IsGpuEnabled = true,
                            IsBatteryEnabled = true,
                            IsMotherboardEnabled = true,
                            IsStorageEnabled = true
                        };
                    }
                    if (!_isOpen)
                    {
                        _computer.Open();
                        _isOpen = true;
                    }
                    return _computer;
                }
            }
        }

        // Atualiza todos os sensores (e sub-hardwares, como núcleos de GPU) de uma vez.
        public static void RefreshAll()
        {
            lock (_lock)
            {
                if (_computer == null || !_isOpen) return;
                try
                {
                    foreach (var hardware in _computer.Hardware)
                    {
                        hardware.Update();
                        foreach (var sub in hardware.SubHardware)
                        {
                            sub.Update();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HardwareMonitorSingleton] Falha ao atualizar sensores: {ex.Message}");
                }
            }
        }

        // Só deve ser chamado UMA vez, ao encerrar o processo inteiro (nunca no meio
        // da execução, ou qualquer leitura de sensor em andamento vai quebrar).
        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_computer != null && _isOpen)
                {
                    try { _computer.Close(); } catch { /* ignora erro no encerramento */ }
                    _isOpen = false;
                }
            }
        }
    }
}