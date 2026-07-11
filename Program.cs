using System;
using System.Diagnostics;
using System.Linq;

namespace HardwareTestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            // ➔ VERIFICAÇÃO DE INSTÂNCIA DUPLICADA
            KillExistingInstances();

            Console.Title = "CDQ Agente de Diagnósticos - Local API Server";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=========================================================");
            Console.WriteLine("       INICIANDO AGENTE DE DIAGNÓSTICOS LOCAL            ");
            Console.WriteLine("=========================================================");
            Console.ResetColor();

            try
            {
                using (var agent = new BackendAgent())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n[SUCESSO] Servidor local Kestrel iniciado!");
                    Console.ResetColor();

                    while (true)
                    {
                        System.Threading.Thread.Sleep(5000);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERRO FATAL]: {ex.Message}");
                Console.ResetColor();
                Console.ReadLine();
            }
        }

        /// <summary>
        /// Procura por outras instâncias do app rodando e finaliza para evitar conflito de porta e arquivo
        /// </summary>
        private static void KillExistingInstances()
        {
            Process currentProcess = Process.GetCurrentProcess();

            // Busca todos os processos com o mesmo nome, exceto o atual
            var runningProcesses = Process.GetProcessesByName(currentProcess.ProcessName)
                                          .Where(p => p.Id != currentProcess.Id);

            foreach (var process in runningProcesses)
            {
                try
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[AVISO] Fechando instância antiga ativa (PID: {process.Id})...");
                    Console.ResetColor();

                    process.Kill();
                    process.WaitForExit(1000); // Aguarda até 1 segundo para o Windows liberar o arquivo
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Não foi possível fechar o processo antigo: {ex.Message}");
                }
            }
        }
    }
}