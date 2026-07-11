using System.Diagnostics;

namespace HardwareTestApp
{

    public class SmartService
    {
        private readonly string _smartctlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smart", "smartctl.exe");

        public async Task<string> GetDiskDataJson(string devicePath)
        {
            // Comando completo para pegar tudo de um disco
            string test = await ExecuteCommand($"--all --json {devicePath}");
            Console.WriteLine("Verificando Smart: "+ test );
            return test;
        }

        public async Task<string> ExecuteCommand(string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = _smartctlPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo);
            using StreamReader reader = process.StandardOutput;
            return await reader.ReadToEndAsync();
        }
    }

}