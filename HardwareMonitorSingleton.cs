using System;
using LibreHardwareMonitor.Hardware;

namespace HardwareTestApp
{
    // -----------------------------------------------------------------------------
    // Única instância de Computer (LibreHardwareMonitor) para todo o processo.
    //
    // NUNCA crie outro "new Computer()" em nenhum outro arquivo do projeto. O LHM usa
    // um driver de kernel (Ring0) compartilhado por processo para ler MSRs de CPU/GPU;
    // se uma segunda instância de Computer for aberta e depois fechada (Close()) enquanto
    // a instância principal continua "aberta", esse Close() derruba o driver compartilhado
    // e a instância principal passa a lançar NullReferenceException dentro de
    // GenericCpu.Update() (ou de qualquer outro hardware) na próxima leitura.
    //
    // Use sempre HardwareMonitorSingleton.Instance para ler sensores, e
    // HardwareMonitorSingleton.RefreshAll() antes de ler os valores.
    // -----------------------------------------------------------------------------
    public static class HardwareMonitorSingleton
    {
        private static readonly object _lock = new object();
        private static Computer _computer;

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
                            IsStorageEnabled = true,
                            IsMemoryEnabled = true
                        };
                        _computer.Open();
                    }
                    return _computer;
                }
            }
        }

        // Atualiza todos os hardwares/sub-hardwares monitorados (CPU, GPU, disco, etc.).
        // Chame isso antes de ler qualquer sensor para garantir valores atuais.
        public static void RefreshAll()
        {
            lock (_lock)
            {
                if (_computer == null) return;
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

        // Só deve ser chamado UMA vez, no encerramento do processo (Dispose do BackendAgent).
        public static void Shutdown()
        {
            lock (_lock)
            {
                try { _computer?.Close(); } catch { /* ignore */ }
                _computer = null;
            }
        }
    }
}
