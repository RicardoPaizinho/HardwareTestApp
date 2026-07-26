using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Management;

namespace HardwareTestApp
{
    // -----------------------------------------------------------------------------
    // Monitora conexão/desconexão de dispositivos USB via eventos WMI (push, não
    // polling), acumulando os eventos numa fila — mesmo padrão do KeyboardInterceptor
    // — que o endpoint HTTP consome e esvazia a cada requisição do front.
    // -----------------------------------------------------------------------------
    public static class UsbMonitorService
    {
        private static ManagementEventWatcher _insertWatcher;
        private static ManagementEventWatcher _removeWatcher;
        private static readonly ConcurrentQueue<object> _eventQueue = new ConcurrentQueue<object>();

        public static bool IsMonitoring { get; private set; } = false;

        public static void Start()
        {
            if (IsMonitoring) return;
            try
            {
                var insertQuery = new WqlEventQuery(
                    "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_PnPEntity'");
                _insertWatcher = new ManagementEventWatcher(insertQuery);
                _insertWatcher.EventArrived += (s, e) => OnDeviceEvent(e, "connected");
                _insertWatcher.Start();

                var removeQuery = new WqlEventQuery(
                    "SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_PnPEntity'");
                _removeWatcher = new ManagementEventWatcher(removeQuery);
                _removeWatcher.EventArrived += (s, e) => OnDeviceEvent(e, "disconnected");
                _removeWatcher.Start();

                IsMonitoring = true;
                Console.WriteLine("[USB] Monitoramento ativado.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[USB] Falha ao iniciar monitor: {ex.Message}");
            }
        }

        public static void Stop()
        {
            try { _insertWatcher?.Stop(); _insertWatcher?.Dispose(); } catch { /* ignora */ }
            try { _removeWatcher?.Stop(); _removeWatcher?.Dispose(); } catch { /* ignora */ }
            _insertWatcher = null;
            _removeWatcher = null;
            IsMonitoring = false;
            Console.WriteLine("[USB] Monitoramento desativado.");
        }

        private static void OnDeviceEvent(EventArrivedEventArgs e, string action)
        {
            try
            {
                var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                var deviceId = targetInstance["DeviceID"]?.ToString() ?? "";
                var name = targetInstance["Name"]?.ToString() ?? "Dispositivo desconhecido";

                // Filtra só dispositivos realmente ligados via barramento USB (evita
                // disparar para qualquer PnP genérico: monitores, Bluetooth, etc.)
                if (deviceId.IndexOf("USB", StringComparison.OrdinalIgnoreCase) < 0) return;

                _eventQueue.Enqueue(new
                {
                    action,
                    deviceName = name,
                    time = DateTime.Now.ToString("HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[USB] Erro ao processar evento: {ex.Message}");
            }
        }

        // Retorna e limpa todos os eventos acumulados desde a última leitura da API.
        public static List<object> FlushEvents()
        {
            var list = new List<object>();
            while (_eventQueue.TryDequeue(out var evt)) list.Add(evt);
            return list;
        }
    }
}
