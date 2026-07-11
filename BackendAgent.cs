using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using static HardwareTestApp.AutoTests;
using Windows.ApplicationModel;

namespace HardwareTestApp
{
    public class BackendAgent : IDisposable
    {
        private Computer _computer;
        private System.Threading.Timer _monitorTimer;
        private bool _isUpdating = false;
        private float _cachedStorageLife = 100;
        private string _cachedAutoJson = "{}";
        private SystemInfo si;
        private WebApplication _webApp;

        // Caches para alimentar a API HTTP instantaneamente
        private object _pcInfoCached = null;
        private object _performanceDataCached = null;
        private List<object> _eventLogsCached = new List<object>();

        public BackendAgent()
        {
            si = new SystemInfo();

            // 1. Inicializa o monitoramento de Hardware nativo
            InitHardwareMonitor();

            // 2. Dispara a geração inicial de diagnósticos automáticos
            _ = RunInitialDiagnosticsAsync();

            // 3. Inicia o Timer de desempenho (atualiza a cada 2 segundos)
            _monitorTimer = new System.Threading.Timer(async _ => await UpdateAllData(), null, 0, 2000);

            // 4. Inicia o Servidor Web Kestrel
            StartLocalApiServer();
        }


        private void StartLocalApiServer()
        {
            try
            {
                var builder = WebApplication.CreateBuilder();

                // 1. Configura o Kestrel para escutar em HTTP (5000) e HTTPS (5001)
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(5000); // HTTP comum
                    options.ListenLocalhost(5001, listenOptions => listenOptions.UseHttps()); // HTTPS Seguro para a Vercel
                });

                // 2. Configura a política de CORS para permitir que o front da Vercel acesse a API local
                builder.Services.AddCors(options =>
                {
                    options.AddPolicy("AllowOnlineFront", policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    });
                });

                var app = builder.Build();

                // Ativa o CORS antes de mapear as rotas
                app.UseCors("AllowOnlineFront");

                // ==========================================
                // ENDPOINTS DO MONITOR DE HARDWARE (Exemplos)
                // ==========================================
                dynamic pcInfo = SystemInfo.GetPCInfo();
                testes();
                app.MapGet("/api/pc-info", () => {
                    // Retorne o objeto com os dados estáticos do seu hardware aqui
                    return Results.Json(new
                    {
                        //model = "Modelo do PC",
                        // serialNumber = "123456"
                        //_pcInfoCache,
                        model = (string)pcInfo.model,
                        systemFamily = (string)pcInfo.systemFamily,
                        serialNumber = (string)pcInfo.serialNumber,
                        processador_Name = (string)pcInfo.processador_Name,
                        processador_ClockSpeed = (string)pcInfo.processador_ClockSpeed,
                        processador_MaxClockSpeed = (string)pcInfo.processador_MaxClockSpeed,
                        processador_NumberOfCores = (string)pcInfo.processador_NumberOfCores,
                        processador_NumberOfLogicalProcessors = (string)pcInfo.processador_NumberOfLogicalProcessors,
                        processador_SerialNumber = (string)pcInfo.processador_SerialNumber,

                        storageLife = _cachedStorageLife,
                        batteryHealth = 100 - GetSensorValue(HardwareType.Battery, SensorType.Level, "Degradation Level") ?? 0,

                        memoria = (string)pcInfo.memoria,
                        //armazenamento = pcInfo.armazenamento,
                        armazenamento = pcInfo.armazenamento,
                        placaVideo = pcInfo.placaVideo,

                        system = (string)pcInfo.system,
                        systemBuild = (string)pcInfo.systemBuild,
                        systemVersion = (string)pcInfo.systemVersion,
                        systemArchitecture = (string)pcInfo.Architecture,
                        systemKey = (string)pcInfo.WindowsKey,

                        lcd = (string)pcInfo.lcd
                        // ... adicione as propriedades que o seu front espera ...
                    });
                });

                app.MapGet("/api/performance", () => {
                    // Serve os dados reais coletados pelo timer a cada 2s (UpdateAllData).
                    // Antes retornava valores fixos de exemplo (cpu=110 etc.) e nunca
                    // reflatia o hardware de verdade — por isso as barras de progresso
                    // pareciam "travadas". Enquanto a primeira leitura não chega,
                    // devolvemos um objeto zerado (o front já trata isso normalmente).
                    if (_performanceDataCached != null)
                    {
                        return Results.Json(_performanceDataCached);
                    }

                    return Results.Json(new
                    {
                        cpu = 0,
                        memory = 0,
                        disk = 0,
                        realtime = new { cpuTemp = 0, gpuTemp = 0, storageTemp = 0, batteryLevel = 0 }
                    });
                });

                app.MapGet("/api/battery", () => {
                    // Leitura sob demanda (sem cache) para refletir o estado de
                    // carga/descarga em tempo real durante o teste de bateria.
                    try
                    {
                        var collector = new SystemInfoCollector();
                        var baterias = collector.GetBatteries();
                        return Results.Json(baterias);
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(new { error = ex.Message }, statusCode: 500);
                    }
                });

                app.MapGet("/api/network", async () => {
                    try
                    {
                        var netService = new NetworkService();
                        var json = await netService.GetNetworkDataJson();
                        return Results.Content(json, "application/json");
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(new { error = ex.Message }, statusCode: 500);
                    }
                });

                // ==========================================
                //        ENDPOINTS DO HOOK DE TECLADO
                // ==========================================

                // Controla o status de ativação do Hook Global (Ativado quando entra na tela de teste)
                app.MapPost("/api/teclado/status", (bool active) =>
                {
                    KeyboardInterceptor.IsEnabled = active;

                    if (active)
                    {
                        KeyboardInterceptor.SetHook();
                    }
                    else
                    {
                        KeyboardInterceptor.Unhook();
                    }

                    Console.WriteLine($"[Teclado] Status alterado. Ativo: {active}");
                    return Results.Json(new { keyboardTestActive = active });
                });

                // O front na Vercel consome e limpa a fila concorrente a cada 150ms por aqui
                app.MapGet("/api/teclado/eventos", () =>
                {
                    var eventos = KeyboardInterceptor.FlushEvents();
                    return Results.Json(eventos);
                });

                // ==========================================

                // Inicializa o servidor em background
                app.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agente Erro] Falha ao subir API Kestrel: {ex.Message}");
            }
        }

        private void InitHardwareMonitor()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsBatteryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = true
                };
                _computer.Open();
                RefreshHardwareSensors();
                _cachedStorageLife = GetSensorValue(HardwareType.Storage, SensorType.Level, "Life") ?? 100;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao abrir LibreHardwareMonitor: {ex.Message}");
            }
        }

        private async Task RunInitialDiagnosticsAsync()
        {
            try
            {
                var settings = new Newtonsoft.Json.JsonSerializerSettings
                {
                    ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                };

                var service = new AutoDiagnosticsService();
                var data = await Task.Run(() => service.GetFullDiagnostics());
                _cachedAutoJson = Newtonsoft.Json.JsonConvert.SerializeObject(data, settings);
            }
            catch (Exception ex)
            {
                _cachedAutoJson = $"{{\"error\": \"{ex.Message}\"}}";
            }
        }

        private async Task UpdateAllData()
        {
            if (_isUpdating) return;
            _isUpdating = true;

            try
            {
                // Atualiza os sensores do LibreHardwareMonitor antes de ler os valores.
                // Sem isso, Sensor.Value nunca é atualizado após a primeira leitura e os
                // dados de temperatura/bateria ficam "congelados" (bug reportado).
                RefreshHardwareSensors();

                // Alimenta os dados fixos na primeira iteração
                if (_pcInfoCached == null)
                {
                    dynamic pcInfo = SystemInfo.GetPCInfo();
                    _pcInfoCached = new
                    {
                        model = (string)pcInfo.model,
                        systemFamily = (string)pcInfo.systemFamily,
                        serialNumber = (string)pcInfo.serialNumber,
                        processador_Name = (string)pcInfo.processador_Name,
                        processador_ClockSpeed = (string)pcInfo.processador_ClockSpeed,
                        processador_MaxClockSpeed = (string)pcInfo.processador_MaxClockSpeed,
                        processador_NumberOfCores = (string)pcInfo.processador_NumberOfCores,
                        processador_NumberOfLogicalProcessors = (string)pcInfo.processador_NumberOfLogicalProcessors,
                        processador_SerialNumber = (string)pcInfo.processador_SerialNumber,
                        storageLife = _cachedStorageLife,
                        batteryHealth = 100 - GetSensorValue(HardwareType.Battery, SensorType.Level, "Degradation Level") ?? 0,
                        memoria = (string)pcInfo.memoria,
                        armazenamento = pcInfo.armazenamento,
                        placaVideo = pcInfo.placaVideo,
                        system = (string)pcInfo.system,
                        systemBuild = (string)pcInfo.systemBuild,
                        systemVersion = (string)pcInfo.systemVersion,
                        systemArchitecture = (string)pcInfo.Architecture,
                        systemKey = (string)pcInfo.WindowsKey,
                        lcd = (string)pcInfo.lcd
                    };
                }

                // Lê os dados de performance dinâmicos do sistema operacional
                dynamic metrics = await SystemInfo.GetPerformanceMetricsAsync();
                _performanceDataCached = new
                {
                    cpu = Convert.ToSingle(metrics.cpu),
                    memory = Convert.ToSingle(metrics.memory),
                    disk = Convert.ToSingle(metrics.disk),
                    realtime = new
                    {
                        cpuTemp = GetSensorValue(HardwareType.Cpu, SensorType.Temperature, "CPU Package") ?? 0,
                        gpuTemp = GetSensorValue(HardwareType.GpuIntel, SensorType.Temperature, "") ??
                                  GetSensorValue(HardwareType.GpuNvidia, SensorType.Temperature, "") ??
                                  GetSensorValue(HardwareType.GpuAmd, SensorType.Temperature, "") ?? 0,
                        storageTemp = GetSensorValue(HardwareType.Storage, SensorType.Temperature, "Temperature") ?? 0,
                        batteryLevel = GetSensorValue(HardwareType.Battery, SensorType.Level, "Charge Level") ?? 0,
                    }
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro na rotina de coleta: {ex.Message}");
            }
            finally { _isUpdating = false; }
        }

        private void UpdateLogsCache()
        {
            try
            {
                EventLog systemLog = new EventLog("System");
                _eventLogsCached = systemLog.Entries.Cast<EventLogEntry>()
                    .Where(e => (int)e.EntryType == 0 || (int)e.EntryType == 1)
                    .OrderByDescending(e => e.TimeGenerated)
                    .Take(30)
                    .Select(e => new {
                        level = (int)e.EntryType == 0 ? "Critical" : "Error",
                        eventId = e.InstanceId,
                        date = e.TimeGenerated.ToString("G"),
                        origin = e.Source,
                        msg = e.Message
                    }).Cast<object>().ToList();
            }
            catch { }
        }

        // Chama Update() em cada hardware monitorado (e em seus sub-hardwares, como os
        // núcleos de GPU) para que os sensores tragam valores atuais a cada ciclo.
        private void RefreshHardwareSensors()
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
                Console.WriteLine($"[HardwareMonitor] Falha ao atualizar sensores: {ex.Message}");
            }
        }

        private float? GetSensorValue(HardwareType hwType, SensorType sType, string sensorName = null)
        {
            if (_computer == null) return null;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == hwType)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == sType)
                        {
                            if (!string.IsNullOrEmpty(sensorName))
                            {
                                if (sensor.Name.IndexOf(sensorName, StringComparison.OrdinalIgnoreCase) >= 0) return sensor.Value;
                                continue;
                            }
                            if (sensor.Value.HasValue) return sensor.Value;
                        }
                    }
                }
            }
            return null;
        }

        static void testes()
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

            Console.WriteLine("\n=== MONITOR ===");
            foreach (var m in report.Monitores)
                Console.WriteLine($"{m.DeviceName} ({m.Manufacturer}) | {m.MaxScreenSize} | {m.InputType}  | {m.DeviceId} | {m.EdidVersion}");

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


        public void Dispose()
        {
            _monitorTimer?.Dispose();
            _computer?.Close();
            try { _webApp?.StopAsync(); } catch { }
        }
    }
}