using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace HardwareTestApp
{
    public enum PawnIoStatus
    {
        AlreadyInstalled,
        InstalledNow,
        InstallerNotFound,
        UserCancelled,
        InstallFailed
    }

    // -----------------------------------------------------------------------------
    // PawnIoBootstrap
    // -------------------------------------------------------------------------------------
    // Garante que o driver PawnIO (https://pawnio.eu) esteja instalado antes do
    // LibreHardwareMonitor tentar ler sensores que dependem de acesso a MSR (ex:
    // temperatura de CPU). O LibreHardwareMonitor migrou do WinRing0 pro PawnIO porque
    // o WinRing0 é bloqueado pelo Windows/Defender em máquinas mais novas (driver não
    // assinado corretamente / CVEs conhecidas).
    //
    // Chame EnsureInstalledAsync() UMA VEZ, no início do BackendAgent, ANTES de abrir
    // o HardwareMonitorSingleton — se o driver ainda não existir nesse ponto, os
    // sensores de temperatura de CPU simplesmente voltam null (LibreHardwareMonitor não
    // lança exceção, só não popula o sensor). SystemInfoCollector.GetCpuInfo() consulta
    // IsAvailable pra saber se vale a pena tentar ler via LibreHardwareMonitor ou se já
    // deve cair direto pro fallback via WMI.
    //
    // NÃO bloqueia o app se falhar: falha aqui só significa "sem leitura precisa de
    // temperatura de CPU" — o resto do agente (bateria, USB, teclado, etc.) continua
    // funcionando normalmente.
    // -----------------------------------------------------------------------------
    public static class PawnIoBootstrap
    {
        private const string UninstallKeyName = "PawnIO";
        private const string InstallerRelativePath = @"Redist\PawnIO_setup.exe";
        private const string InstallerArgs = "-install -silent";
        private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(30);

        public static PawnIoStatus LastStatus { get; private set; } = PawnIoStatus.InstallerNotFound;

        // Único jeito confiável de decidir, em runtime, se vale a pena tentar ler
        // temperatura de CPU via LibreHardwareMonitor (ver SystemInfoCollector.GetCpuInfo()).
        public static bool IsAvailable => LastStatus is PawnIoStatus.AlreadyInstalled or PawnIoStatus.InstalledNow;

        public static async Task<PawnIoStatus> EnsureInstalledAsync()
        {
            if (IsPawnIoInstalled())
            {
                LastStatus = PawnIoStatus.AlreadyInstalled;
                return LastStatus;
            }

            var installerPath = Path.Combine(AppContext.BaseDirectory, InstallerRelativePath);
            if (!File.Exists(installerPath))
            {
                Console.WriteLine(
                    $"[PawnIO] Driver não instalado e instalador não encontrado em '{installerPath}'. " +
                    "A leitura de temperatura de CPU vai cair pro fallback via WMI (menos preciso). " +
                    "Baixe o instalador em https://pawnio.eu e coloque em Redist/PawnIO_setup.exe.");
                LastStatus = PawnIoStatus.InstallerNotFound;
                return LastStatus;
            }

            try
            {
                // O agente inteiro já roda elevado (ver app.manifest: requireAdministrator),
                // então o instalador herda essa elevação sem pedir UAC de novo na prática.
                // Ainda assim tratamos ERROR_CANCELLED (1223) por segurança, caso isso mude
                // no futuro (ex: se alguém remover o requireAdministrator do manifest).
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = InstallerArgs,
                    UseShellExecute = true
                });

                if (process == null)
                {
                    Console.WriteLine("[PawnIO] Falha ao iniciar o instalador (Process.Start retornou null).");
                    LastStatus = PawnIoStatus.InstallFailed;
                    return LastStatus;
                }

                var exited = await WaitForExitWithTimeoutAsync(process, InstallTimeout);
                if (!exited)
                {
                    Console.WriteLine($"[PawnIO] Instalador ainda rodando após {InstallTimeout.TotalSeconds:0}s — seguindo sem esperar mais (não trava o app).");
                }

                // Confirma pelo registro em vez de confiar só no exit code (varia por instalador).
                if (IsPawnIoInstalled())
                {
                    Console.WriteLine("[PawnIO] Instalado com sucesso.");
                    LastStatus = PawnIoStatus.InstalledNow;
                }
                else
                {
                    Console.WriteLine("[PawnIO] Instalador rodou, mas o driver não aparece no registro depois. Verifique manualmente.");
                    LastStatus = PawnIoStatus.InstallFailed;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED (UAC recusado)
            {
                Console.WriteLine("[PawnIO] Instalação cancelada pelo usuário (UAC recusado).");
                LastStatus = PawnIoStatus.UserCancelled;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PawnIO] Falha ao instalar: {ex.Message}");
                LastStatus = PawnIoStatus.InstallFailed;
            }

            return LastStatus;
        }

        private static async Task<bool> WaitForExitWithTimeoutAsync(Process process, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        // Verifica a chave de desinstalação exata pedida (HKLM\...\Uninstall\PawnIO, e o
        // espelho de 32 bits), com fallback pra uma busca mais ampla por DisplayName —
        // o nome da subchave pode variar conforme o instalador (ex: builds Inno Setup
        // costumam gerar algo como "PawnIO_is1" em vez do nome exato "PawnIO").
        private static bool IsPawnIoInstalled()
        {
            string[] uninstallRoots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var root in uninstallRoots)
            {
                try
                {
                    using var exactKey = Registry.LocalMachine.OpenSubKey($@"{root}\{UninstallKeyName}");
                    if (exactKey != null)
                    {
                        var installLocation = exactKey.GetValue("InstallLocation") as string;
                        Console.WriteLine($"[PawnIO] Encontrado em HKLM\\{root}\\{UninstallKeyName}. InstallLocation: {(string.IsNullOrEmpty(installLocation) ? "(vazio)" : installLocation)}");
                        return true;
                    }

                    using var uninstallRoot = Registry.LocalMachine.OpenSubKey(root);
                    if (uninstallRoot == null) continue;

                    foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
                    {
                        using var subKey = uninstallRoot.OpenSubKey(subKeyName);
                        var displayName = subKey?.GetValue("DisplayName") as string;
                        if (!string.IsNullOrEmpty(displayName) &&
                            displayName.IndexOf("PawnIO", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PawnIO] Falha ao consultar registro ({root}): {ex.Message}");
                }
            }

            return false;
        }
    }
}
