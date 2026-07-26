using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HardwareTestApp
{
    public class AutoTests
    {
        // Classe de transporte para o JSON
        public class AutoDiagnosticsResult
        {
            public object drivers { get; set; }
            public object bitlocker { get; set; }
            public object partition { get; set; }
            public object activation { get; set; }
            public object smart { get; set; }
        }

        public class AutoDiagnosticsService
        {
            // ÚNICO MÉTODO de coleta (removi o duplicado que retornava 'object')
            public AutoDiagnosticsResult GetFullDiagnostics()
            {
                try
                {
                    // Executa cada teste individualmente
                    var drivers = CheckDrivers();
                    var bitlocker = CheckBitLocker();
                    var partitions = CheckPartitions();
                    var activation = CheckActivation();
                    var smart = CheckSmart();

                    Console.WriteLine("Executado auto test com sucesso.");

                    return new AutoDiagnosticsResult
                    {
                        drivers = drivers,
                        bitlocker = bitlocker,
                        partition = partitions,
                        activation = activation,
                        smart = smart
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro crítico no diagnóstico: {ex.Message}");
                    return null;
                }
            }

            private object CheckDrivers()
            {
                bool isOK = true;
                int errorCount = 0;
                var devices = new List<object>();

                try
                {
                    // Procura dispositivos com erro (ConfigManagerErrorCode != 0)
                    ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, Status, ConfigManagerErrorCode FROM Win32_PnPEntity");

                    foreach (ManagementObject device in searcher.Get())
                    {
                        var errorCode = device["ConfigManagerErrorCode"];
                        if (errorCode != null && Convert.ToUInt32(errorCode) != 0)
                        {
                            isOK = false;
                            errorCount++;
                            devices.Add(new { name = device["Name"]?.ToString(), code = errorCode });
                        }
                    }
                }
                catch { isOK = false; }

                return new { status = isOK ? "check" : "alert", details = new { errorCount, devices } };
            }

            private object CheckBitLocker()
            {
                try
                {
                    // Usando ShellObject como você definiu
                    var prop = Microsoft.WindowsAPICodePack.Shell.ShellObject.FromParsingName("C:").Properties.GetProperty("System.Volume.BitLockerProtection");
                    var value = prop.ValueAsObject;

                    // Se o valor for 1 ou maior, há proteção ativa
                    bool encrypted = value != null && (Convert.ToInt32(value) == 1 || Convert.ToInt32(value)==3 || Convert.ToInt32(value) == 4 || Convert.ToInt32(value) == 5);

                    return new { status = encrypted ? "alert" : "check", details = new { drive = "C:", statusCode = value } };
                }
                catch
                {
                    return new { status = "check", details = new { info = "Não foi possível ler BitLocker" } };
                }
            }

            private object CheckPartitions()
            {
                try
                {
                    long totalDisk = 0;
                    long totalPartitions = 0;

                    var diskSearcher = new ManagementObjectSearcher("SELECT Size FROM Win32_DiskDrive WHERE Index = 0");
                    foreach (var disk in diskSearcher.Get()) totalDisk = Convert.ToInt64(disk["Size"]);

                    var partSearcher = new ManagementObjectSearcher("SELECT Size FROM Win32_DiskPartition WHERE DiskIndex = 0");
                    foreach (var part in partSearcher.Get()) totalPartitions += Convert.ToInt64(part["Size"]);

                    double unallocatedMB = (totalDisk - totalPartitions) / 1024 / 1024;

                    // Se sobrar mais de 500MB não alocados, alerta
                    return new { status = unallocatedMB > 400 ? "alert" : "check", details = new { unallocatedGB = Math.Round(unallocatedMB / 1024, 2) } };
                }
                catch { return new { status = "check" }; }
            }

            private object CheckActivation()
            {
                bool activated = IsWindowsActivatedNEW();
                return new { status = activated ? "check" : "alert" };
            }

            private object CheckSmart()
            {
                try
                {
                    // Antes era um mock fixo pro HDSentinel (temp=35, health=100).
                    // Agora lemos de verdade via LibreHardwareMonitor, pela instância
                    // única compartilhada com o resto do projeto.
                    var computer = HardwareMonitorSingleton.Instance;
                    HardwareMonitorSingleton.RefreshAll();

                    foreach (var hardware in computer.Hardware)
                    {
                        if (hardware.HardwareType != LibreHardwareMonitor.Hardware.HardwareType.Storage)
                            continue;

                        float? temp = null;
                        float? health = null;

                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature && temp == null)
                                temp = sensor.Value;

                            if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Level &&
                                sensor.Name.IndexOf("Life", StringComparison.OrdinalIgnoreCase) >= 0)
                                health = sensor.Value;
                        }

                        // Fallback: se não achou um sensor "Life" específico, usa o
                        // primeiro sensor de nível/porcentagem disponível no disco.
                        if (health == null)
                        {
                            foreach (var sensor in hardware.Sensors)
                            {
                                if (sensor.SensorType == LibreHardwareMonitor.Hardware.SensorType.Level)
                                {
                                    health = sensor.Value;
                                    break;
                                }
                            }
                        }

                        double healthVal = health.HasValue ? Math.Round(health.Value, 1) : 100;
                        double tempVal = temp.HasValue ? Math.Round(temp.Value, 1) : 0;
                        bool ok = healthVal >= 70 && (temp == null || tempVal < 60);

                        return new
                        {
                            status = ok ? "check" : "alert",
                            details = new
                            {
                                modelo = hardware.Name,
                                temp = tempVal,
                                health = healthVal
                            }
                        };
                    }

                    // Nenhum disco reconhecido pelo LibreHardwareMonitor (raro, mas
                    // pode acontecer sem permissão de Administrador).
                    return new { status = "alert", details = new { modelo = "Desconhecido", temp = 0, health = 0, info = "Disco não detectado pelo LibreHardwareMonitor." } };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AutoTest] Falha ao ler SMART via LibreHardwareMonitor: {ex.Message}");
                    return new { status = "alert", details = new { modelo = "Erro", temp = 0, health = 0, info = ex.Message } };
                }
            }

            private bool IsWindowsActivatedNEW()
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "cscript.exe",
                        Arguments = $"/nologo \"{Environment.SystemDirectory}\\slmgr.vbs\" /xpr",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (Process proc = Process.Start(psi))
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();
                        return output.Contains("permanently") || output.Contains("ativado") || output.Contains("ativada");
                    }
                }
                catch { return false; }
            }
        }
    }
}