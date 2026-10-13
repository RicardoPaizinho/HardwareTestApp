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
//using Windows.ApplicationModel;

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
        private int _eventLogsDias = 0; // período (em dias) que está no cache acima
        private readonly object _eventLogsLock = new object();

        // Progresso dos testes salvo localmente (ver seção "/api/progress" abaixo) —
        // sobrevive a fechamentos inesperados do navegador/app, já que fica em disco.
        private static readonly string ProgressFilePath = Path.Combine(Path.GetTempPath(), "cdqweb_test_progress.json");

        public BackendAgent()
        {
            si = new SystemInfo();

            // 0. Garante o driver PawnIO (substituto do WinRing0, bloqueado pelo
            // Windows/Defender em máquinas novas) ANTES de abrir o LibreHardwareMonitor —
            // sem isso, sensores de temperatura de CPU que dependem de MSR não respondem.
            // Bloqueia por no máximo alguns segundos (ver PawnIoBootstrap.InstallTimeout);
            // se falhar ou não achar o instalador, só loga e segue — o resto do agente
            // (bateria, USB, teclado, etc.) não depende disso pra funcionar.
            try
            {
                var pawnIoStatus = PawnIoBootstrap.EnsureInstalledAsync().GetAwaiter().GetResult();
                Console.WriteLine($"[PawnIO] Status: {pawnIoStatus}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PawnIO] Falha inesperada no bootstrap: {ex.Message}");
            }

            // 1. Inicializa o monitoramento de Hardware nativo
            InitHardwareMonitor();

            // 2. Dispara a geração inicial de diagnósticos automáticos
            _ = RunInitialDiagnosticsAsync();

            // 2.1. Busca o histórico de falhas (BSOD/erros críticos) do Visualizador
            // de Eventos uma vez no startup — antes UpdateLogsCache() existia mas
            // nunca era chamado em lugar nenhum, então o cache ficava sempre vazio.
            // Roda em background (Task.Run) porque ler o EventLog pode ser lento.
            _ = System.Threading.Tasks.Task.Run(() => UpdateLogsCache(30));

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

                // 1. Configura o Kestrel para escutar em HTTP puro (5000).
                // Removemos o listener HTTPS (5001/UseHttps) de propósito: ele exigia um
                // certificado de desenvolvimento (dotnet dev-certs) que não existe/expira em
                // outras máquinas, quebrando o agente ao publicar em um PC novo.
                // Isso NÃO quebra a câmera/microfone do site nem exige HTTPS aqui: o front
                // (https://api.cdqweb.com.br) roda em HTTPS normalmente, e todos os navegadores
                // modernos (Chrome, Edge, Firefox) tratam requisições para "localhost"/127.0.0.1
                // como uma exceção ao bloqueio de "mixed content" — uma página HTTPS pode
                // chamar http://localhost sem erro, exatamente pra viabilizar agentes locais
                // como este. Só a página em si (api.cdqweb.com.br) precisa de HTTPS.
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(5000); // HTTP puro — sem certificado necessário
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
                //testes();
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
                        // Lista detalhada por pente de memória (fabricante, serial,
                        // part number, clock, tipo, on-board) — usada pelo relatório
                        // final pra preencher o campo "memoriaDetalhada" no banco.
                        memoriaDetalhada = pcInfo.memoriaDetalhada,
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
                        realtime = new { cpuTemp = 0, gpuTemp = 0, gpuUsage = 0, storageTemp = 0, batteryLevel = 0 }
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

                // ==========================================
                //     TEMPERATURA/USO DE CPU (Monitor.vue)
                // ==========================================
                // Leitura sob demanda (sem cache), mesmo padrão do /api/battery: o valor
                // muda a cada chamada e a chamada em si já é rápida (GetCpuInfo só lê
                // sensores já abertos pelo HardwareMonitorSingleton, não abre nada novo).
                // Ficou como rota dedicada em vez de entrar no payload do /api/performance
                // (que já é pollado a cada 2s só pra telemetria leve do gráfico) porque
                // carrega campos extras que não fazem sentido nesse ritmo (Model,
                // TemperaturaMaxima) e porque segue o padrão de DTO do SystemInfoCollector
                // (igual GetGpuInfo()), que o /api/performance não usa.
                app.MapGet("/api/cpu-info", () => {
                    try
                    {
                        var collector = new SystemInfoCollector();
                        var cpuInfo = collector.GetCpuInfo();
                        return Results.Json(cpuInfo);
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
                //     TESTE DE USB (UsbTest.vue)
                // ==========================================
                // Liga/desliga o monitor de conexão/desconexão USB (evento WMI).
                app.MapPost("/api/usb/monitor", (UsbMonitorRequest req) => {
                    if (req?.enabled == true) UsbMonitorService.Start();
                    else UsbMonitorService.Stop();
                    return Results.Json(new { success = true, monitoring = UsbMonitorService.IsMonitoring });
                });

                // O front consome e limpa a fila a cada poll (mesmo padrão do teclado).
                app.MapGet("/api/usb/eventos", () => {
                    var eventos = UsbMonitorService.FlushEvents();
                    return Results.Json(eventos);
                });

                // ==========================================
                //     DISPOSITIVOS PRESENTES (Teste Final — TestesView.vue)
                // ==========================================
                // Diz se o notebook tem leitor biométrico e tela touch, pra montar
                // a lista do teste sequencial sem incluir o que não existe.
                app.MapGet("/api/devices/presence", () => Results.Json(DetectarDispositivos()));

                // ==========================================
                //     DETALHES PRO RELATÓRIO (testes LCD e Rede)
                // ==========================================
                // Resolução/frequência/ID do painel e nome do adaptador Bluetooth —
                // o front junta isso ao PASS/FAIL salvo no banco.
                app.MapGet("/api/report-details", () => Results.Json(LerDetalhesRelatorio()));

                // ==========================================
                //     HISTÓRICO DE FALHAS (EventLogAnalyzer.vue)
                // ==========================================
                // Serve o cache computado no startup (UpdateLogsCache, chamado
                // no construtor) — antes ficava calculado só na memória sem
                // nenhum endpoint expondo pro front (mesmo problema do auto-diagnóstico).
                // "?dias=30|60|90|120" escolhe o período (padrão 30). Se o período
                // pedido for diferente do que está em cache, refaz a busca.
                app.MapGet("/api/eventlog", (int? dias) => {
                    int periodo = NormalizarDiasEventLog(dias);
                    if (periodo != _eventLogsDias) UpdateLogsCache(periodo);
                    return Results.Json(_eventLogsCached);
                });

                // Permite reexecutar a busca no Visualizador de Eventos sob demanda
                // (botão "ATUALIZAR" do EventLogAnalyzer.vue).
                app.MapPost("/api/eventlog/refresh", (int? dias) => {
                    UpdateLogsCache(NormalizarDiasEventLog(dias));
                    return Results.Json(_eventLogsCached);
                });

                // ==========================================
                //     PROGRESSO LOCAL DOS TESTES (store.js)
                // ==========================================
                // Guarda o estado dos testes (resultados + serial da placa-mãe pra
                // conferência) num arquivo temporário — se o técnico fechar a aba ou o
                // app cair no meio do diagnóstico, ao reabrir a gente recupera de onde
                // parou em vez de voltar do zero. O front decide o formato do JSON e só
                // confia no valor de volta se o serial bater com o equipamento atual
                // (globalState.tryRestoreProgress); aqui só armazenamos/servimos o
                // conteúdo cru, sem entender sua estrutura.
                app.MapGet("/api/progress", () => {
                    try
                    {
                        if (!File.Exists(ProgressFilePath))
                            return Results.Json(new { found = false });

                        var json = File.ReadAllText(ProgressFilePath);
                        return Results.Content($"{{\"found\":true,\"data\":{json}}}", "application/json");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Progresso] Falha ao ler progresso salvo: {ex.Message}");
                        return Results.Json(new { found = false });
                    }
                });

                app.MapPost("/api/progress", async (HttpContext context) => {
                    try
                    {
                        using var reader = new StreamReader(context.Request.Body);
                        var body = await reader.ReadToEndAsync();
                        await File.WriteAllTextAsync(ProgressFilePath, body);
                        return Results.Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Progresso] Falha ao salvar progresso: {ex.Message}");
                        return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
                    }
                });

                // Chamado depois que o relatório final é enviado com sucesso pro banco
                // remoto (não faz mais sentido restaurar um teste que já foi salvo).
                app.MapDelete("/api/progress", () => {
                    try
                    {
                        if (File.Exists(ProgressFilePath)) File.Delete(ProgressFilePath);
                        return Results.Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Progresso] Falha ao limpar progresso: {ex.Message}");
                        return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
                    }
                });

                // ==========================================
                //     DIAGNÓSTICO AUTOMÁTICO (AutoTest.vue)
                // ==========================================
                // Serve o resultado já calculado no startup (RunInitialDiagnosticsAsync),
                // que antes ficava só em cache sem nenhum endpoint expondo pro front —
                // por isso o AutoTest.vue nunca recebia dados (dependia da ponte antiga
                // window.chrome.webview.hostObjects, que não existe rodando via navegador).
                app.MapGet("/api/auto-diagnostics", () => {
                    return Results.Content(_cachedAutoJson, "application/json");
                });

                // Permite reexecutar o diagnóstico automático sob demanda (opcional).
                app.MapPost("/api/auto-diagnostics/refresh", async () => {
                    await RunInitialDiagnosticsAsync();
                    return Results.Content(_cachedAutoJson, "application/json");
                });

                // Substitui os antigos comandos da ponte WebView2 (OpenDevMgmt, etc.) por
                // um endpoint HTTP simples que o front chama via fetch/POST.
                app.MapPost("/api/auto-diagnostics/action", (AutoActionRequest req) => {
                    try
                    {
                        RunSystemAction(req?.action);
                        return Results.Json(new { success = true });
                    }
                    catch (Exception ex)
                    {
                        return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
                    }
                });

                // ==========================================

                _webApp = app;

                // Assim que o Kestrel confirma que subiu de verdade (não apenas "tentou"),
                // abrimos o site em uma janela de navegador anônima/modo app.
                app.Lifetime.ApplicationStarted.Register(() => {
                    Console.WriteLine("[Kestrel] API local no ar em http://localhost:5000");
                    LaunchBrowserWindow();
                });

                // IMPORTANTE: app.Run() é bloqueante — ele travava esta thread (a do
                // construtor do BackendAgent) para sempre enquanto o servidor estivesse no
                // ar, então o "[SUCESSO]" do Program.cs só aparecia quando o Kestrel FALHAVA
                // (como no erro de certificado). RunAsync() não bloqueia: o Kestrel continua
                // rodando em background e o resto da inicialização (e o Program.cs) segue.
                _ = app.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agente Erro] Falha ao subir API Kestrel: {ex.Message}");
            }
        }

        // -----------------------------------------------------------------------------
        // NAVEGADOR EM MODO APP/ANÔNIMO
        // -----------------------------------------------------------------------------
        // Abre o api.cdqweb.com.br assim que o backend termina de subir. Usamos:
        //   --incognito / --inprivate  -> não pede pra salvar usuário/senha
        //   --app=URL                  -> janela "modo app", sem barra de endereço/abas
        // Tenta Chrome primeiro (melhor suporte), cai para Edge (vem no Windows), e como
        // último recurso abre no navegador padrão do sistema (sem os modos acima).
        private static void LaunchBrowserWindow()
        {
            const string url = "https://api.cdqweb.com.br";
            try
            {
                var chromePath = FindBrowserPath("chrome.exe", @"Google\Chrome\Application\chrome.exe");
                if (chromePath != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = chromePath,
                        Arguments = $"--incognito --new-window --app=\"{url}\"",
                        UseShellExecute = true
                    });
                    Console.WriteLine("[Browser] Chrome aberto em modo app/anônimo.");
                    return;
                }

                var edgePath = FindBrowserPath("msedge.exe", @"Microsoft\Edge\Application\msedge.exe");
                if (edgePath != null)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = edgePath,
                        Arguments = $"--inprivate --new-window --app=\"{url}\"",
                        UseShellExecute = true
                    });
                    Console.WriteLine("[Browser] Edge aberto em modo app/anônimo (InPrivate).");
                    return;
                }

                // Último recurso: navegador padrão do sistema (sem modo app/anônimo garantido)
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                Console.WriteLine("[Browser] Chrome/Edge não encontrados — aberto no navegador padrão.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Browser] Falha ao abrir o navegador: {ex.Message}");
            }
        }

        // Procura o executável de duas formas:
        //  1) Registro do Windows (App Paths) — método oficial que funciona não
        //     importa onde o navegador foi instalado (padrão, custom, por usuário).
        //  2) Caminhos fixos (Program Files / AppData\Local) como último recurso.
        private static string FindBrowserPath(string exeName, string relativeExePath)
        {
            var fromRegistry = FindBrowserPathFromRegistry(exeName);
            if (fromRegistry != null) return fromRegistry;

            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("LocalAppData")
            };

            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                var fullPath = Path.Combine(root, relativeExePath);
                if (File.Exists(fullPath)) return fullPath;
            }
            return null;
        }

        // App Paths é a chave que o próprio instalador do Chrome/Edge registra
        // no Windows apontando pro executável real — funciona mesmo se o
        // navegador foi instalado num caminho fora do padrão. Checamos tanto
        // HKLM (instalação para todos os usuários) quanto HKCU (por usuário,
        // comum quando o Chrome é instalado sem permissão de admin).
        private static string FindBrowserPathFromRegistry(string exeName)
        {
            string[] registryRoots =
            {
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}",
                $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\{exeName}"
            };

            foreach (var keyPath in registryRoots)
            {
                try
                {
                    using var hklmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                    var hklmValue = hklmKey?.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(hklmValue) && File.Exists(hklmValue)) return hklmValue;

                    using var hkcuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath);
                    var hkcuValue = hkcuKey?.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(hkcuValue) && File.Exists(hkcuValue)) return hkcuValue;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Browser] Falha ao consultar registro ({keyPath}): {ex.Message}");
                }
            }
            return null;
        }

        // -----------------------------------------------------------------------------
        // AÇÕES DO AUTOTEST (substituem os antigos comandos da ponte WebView2)
        // -----------------------------------------------------------------------------
        public class AutoActionRequest
        {
            public string action { get; set; }
        }

        public class UsbMonitorRequest
        {
            public bool enabled { get; set; }
        }

        private static void RunSystemAction(string action)
        {
            switch (action)
            {
                case "OpenDevMgmt":
                    Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
                    break;

                case "OpenBitlockerSettings":
                    Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.BitLockerDriveEncryption") { UseShellExecute = true });
                    break;

                case "OpenDiskMgmt":
                    Process.Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true });
                    break;

                case "OpenActivationSettings":
                    Process.Start(new ProcessStartInfo("ms-settings:activation") { UseShellExecute = true });
                    break;

                case "OpenFingerprintEnrollment":
                    Process.Start(new ProcessStartInfo("ms-settings:signinoptions-launchfingerprintenrollment") { UseShellExecute = true });
                    break;

                case "OpenHDSentinel":
                    TryOpenHdSentinel();
                    break;

                default:
                    throw new ArgumentException($"Ação desconhecida: {action}");
            }
        }

        private static void TryOpenHdSentinel()
        {
            string[] candidatePaths =
            {
                @"C:\Program Files\HDSentinel\HDSentinel.exe",
                @"C:\Program Files (x86)\HDSentinel\HDSentinel.exe"
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    return;
                }
            }
            throw new FileNotFoundException("HD Sentinel não encontrado neste equipamento.");
        }

        private void InitHardwareMonitor()
        {
            try
            {
                // Usa a instância única compartilhada por todo o projeto — nunca abre
                // um Computer próprio aqui (isso é o que causava o NullReferenceException
                // em GenericCpu.Update() quando outra parte do código fechava sua própria
                // instância e derrubava o driver Ring0 compartilhado).
                _computer = HardwareMonitorSingleton.Instance;
                HardwareMonitorSingleton.RefreshAll();
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
                        // "GPU Core" é o sensor de uso do motor 3D principal (o mesmo
                        // número que aparece como uso de GPU no Gerenciador de Tarefas),
                        // não depende do PawnIO — funciona com ou sem ele instalado.
                        gpuUsage = GetSensorValue(HardwareType.GpuIntel, SensorType.Load, "Core") ??
                                   GetSensorValue(HardwareType.GpuNvidia, SensorType.Load, "Core") ??
                                   GetSensorValue(HardwareType.GpuAmd, SensorType.Load, "Core") ?? 0,
                        storageTemp = GetSensorValue(HardwareType.Storage, SensorType.Temperature, "Temperature") ?? 0,
                        // Nome do mesmo disco físico já usado pra ler storageTemp acima (o
                        // primeiro Storage que o LibreHardwareMonitor encontra) — em notebooks
                        // com um único SSD/NVMe isso é sempre o disco de sistema (C:). Em
                        // desktops com múltiplos discos não há garantia de que seja o C:
                        // especificamente (não estamos resolvendo a letra de unidade via WMI).
                        storageModel = GetHardwareName(HardwareType.Storage),
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

        // Antes o filtro era `EntryType == 0 || EntryType == 1`: EventLogEntryType nunca vale 0,
        // então isso só pegava os últimos 30 eventos "Error" do log System, sem distinguir
        // tela azul de qualquer erro genérico de driver/serviço — por isso a tela ficava poluída.
        // Agora filtra só pelas fontes/IDs que o Windows realmente usa pra registrar
        // bugcheck (tela azul) e desligamento anormal.
        private static bool IsCriticalCrashEvent(EventLogEntry e)
        {
            var source = e.Source ?? "";
            int id = (int)(e.InstanceId & 0xFFFF);

            if (source.Equals("BugCheck", StringComparison.OrdinalIgnoreCase)) return true;
            if (source.Equals("Microsoft-Windows-WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) && id == 1001) return true;
            if (source.Equals("Microsoft-Windows-Kernel-Power", StringComparison.OrdinalIgnoreCase) && id == 41) return true;
            if (source.Equals("EventLog", StringComparison.OrdinalIgnoreCase) && id == 6008) return true;
            // "O driver de vídeo X parou de responder e foi recuperado" (TDR). Não é tela
            // azul, mas é o único evento que traz o NOME do driver de vídeo envolvido —
            // o front usa isso pra apontar se o 0x116/0x117 veio da dedicada ou da onboard.
            if (IsDisplayTdrEvent(source, id)) return true;

            return false;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_MAXIMUMTOUCHES = 95; // > 0 quando há digitalizador touch

        // Leitor biométrico = dispositivo PnP da classe "Biometric" (o Windows Hello
        // usa essa classe). Sem driver instalado ele aparece como "dispositivo
        // desconhecido", fora dessa classe — nesse caso conta como não detectado,
        // e o front pede pro técnico reparar antes do Teste Final.
        private static object DetectarDispositivos()
        {
            var leitores = new List<object>();
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE PNPClass = 'Biometric'");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    leitores.Add(new {
                        nome = mo["Name"]?.ToString() ?? "Leitor biométrico",
                        // 0 = funcionando; outro valor = erro de driver/dispositivo
                        codigoErro = Convert.ToInt32(mo["ConfigManagerErrorCode"] ?? 0)
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Dispositivos] Falha ao consultar leitor biométrico: {ex.Message}");
            }

            int toquesMaximos = 0;
            try { toquesMaximos = GetSystemMetrics(SM_MAXIMUMTOUCHES); } catch { }

            return new {
                fingerprint = leitores.Count > 0,
                leitoresBiometricos = leitores,
                touchscreen = toquesMaximos > 0,
                toquesMaximos
            };
        }

        private static object LerDetalhesRelatorio()
        {
            int? largura = null, altura = null, frequencia = null;
            try
            {
                using var vc = new System.Management.ManagementObjectSearcher(
                    "SELECT CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
                foreach (System.Management.ManagementObject mo in vc.Get())
                {
                    if (mo["CurrentHorizontalResolution"] == null) continue; // GPU sem saída ativa
                    largura = Convert.ToInt32(mo["CurrentHorizontalResolution"]);
                    altura = Convert.ToInt32(mo["CurrentVerticalResolution"]);
                    frequencia = mo["CurrentRefreshRate"] != null ? Convert.ToInt32(mo["CurrentRefreshRate"]) : null;
                    break;
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Relatório] Falha ao ler resolução: {ex.Message}"); }

            // ID do painel no formato do Gerenciador de Dispositivos
            // (ex: DISPLAY\LGD05F2\4&2A06D433&0&UID8388688). O InstanceName do
            // WmiMonitorID é igual, só com um sufixo "_0" no fim.
            string? idTela = null;
            string? nomeTela = null;
            try
            {
                using var mon = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
                foreach (System.Management.ManagementObject mo in mon.Get())
                {
                    var instancia = mo["InstanceName"]?.ToString();
                    if (string.IsNullOrEmpty(instancia)) continue;
                    idTela = System.Text.RegularExpressions.Regex.Replace(instancia, @"_\d+$", "").ToUpperInvariant();
                    if (mo["UserFriendlyName"] is ushort[] raw)
                        nomeTela = new string(raw.Where(c => c != 0).Select(c => (char)c).ToArray()).Trim();
                    break; // o primeiro costuma ser o painel interno do notebook
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Relatório] Falha ao ler ID do monitor: {ex.Message}"); }

            // Bluetooth: o adaptador de rede PAN ("Bluetooth Device (Personal Area
            // Network)") é o nome que aparece junto dos adaptadores de rede; sem ele,
            // usa o primeiro rádio Bluetooth do Gerenciador de Dispositivos.
            string? bluetooth = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(ni => (ni.Description ?? "").Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                ?.Description;
            if (bluetooth == null)
            {
                try
                {
                    using var bt = new System.Management.ManagementObjectSearcher(
                        "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth' AND ConfigManagerErrorCode = 0");
                    foreach (System.Management.ManagementObject mo in bt.Get())
                    {
                        var nome = mo["Name"]?.ToString() ?? "";
                        if (nome.Contains("Enumerator", StringComparison.OrdinalIgnoreCase)) continue;
                        bluetooth = nome;
                        break;
                    }
                }
                catch { }
            }

            return new {
                tela = new { largura, altura, frequencia, id = idTela, nome = nomeTela },
                bluetooth
            };
        }

        private static bool IsDisplayTdrEvent(string source, int id) =>
            source.Equals("Display", StringComparison.OrdinalIgnoreCase) && id == 4101;

        private static readonly int[] DiasEventLogPermitidos = { 30, 60, 90, 120 };
        private const int MaxEventosEventLog = 500;

        private static int NormalizarDiasEventLog(int? dias) =>
            dias.HasValue && DiasEventLogPermitidos.Contains(dias.Value) ? dias.Value : 30;

        // Busca os eventos de falha dos últimos "dias" dias. Percorre o log do mais
        // novo pro mais antigo (Entries vem em ordem cronológica) e para assim que
        // passa da data de corte — bem mais rápido do que varrer o log inteiro.
        private void UpdateLogsCache(int dias)
        {
            lock (_eventLogsLock)
            {
                try
                {
                    var corte = DateTime.Now.AddDays(-dias);
                    var resultado = new List<object>();

                    using var systemLog = new EventLog("System");
                    var entries = systemLog.Entries;
                    for (int i = entries.Count - 1; i >= 0 && resultado.Count < MaxEventosEventLog; i--)
                    {
                        EventLogEntry e;
                        try { e = entries[i]; } catch { continue; } // log pode girar durante a leitura
                        if (e.TimeGenerated < corte) break;
                        if (!IsCriticalCrashEvent(e)) continue;

                        var dados = e.ReplacementStrings ?? Array.Empty<string>();
                        string? driver = null;
                        if (IsDisplayTdrEvent(e.Source ?? "", (int)(e.InstanceId & 0xFFFF)) && dados.Length > 0
                            && !string.IsNullOrWhiteSpace(dados[0]))
                        {
                            driver = dados[0].Trim();
                            if (!driver.EndsWith(".sys", StringComparison.OrdinalIgnoreCase)) driver += ".sys";
                        }

                        resultado.Add(new {
                            level = "Critical",
                            eventId = e.InstanceId,
                            date = e.TimeGenerated.ToString("G"),
                            dateIso = e.TimeGenerated.ToString("yyyy-MM-ddTHH:mm:ss"),
                            origin = e.Source,
                            msg = e.Message,
                            // Parâmetros crus do evento (ex: Kernel-Power 41 traz o BugcheckCode
                            // em decimal na posição 0 quando o desligamento foi uma tela azul)
                            dados,
                            driver
                        });
                    }

                    _eventLogsCached = resultado;
                    _eventLogsDias = dias;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EventLog] Falha ao ler o log System: {ex.Message}");
                }
            }
        }

        // Chama Update() em cada hardware monitorado (e em seus sub-hardwares, como os
        // núcleos de GPU) para que os sensores tragam valores atuais a cada ciclo.
        // Delega para o singleton — nunca mexe em uma instância própria de Computer.
        private void RefreshHardwareSensors()
        {
            HardwareMonitorSingleton.RefreshAll();
        }

        private string GetHardwareName(HardwareType hwType)
        {
            if (_computer == null) return null;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == hwType) return hardware.Name;
            }
            return null;
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
            UsbMonitorService.Stop();
            // Fecha o Computer aqui — este é o ÚNICO lugar do projeto que deve chamar
            // Shutdown()/Close(), pois é o encerramento do processo inteiro.
            HardwareMonitorSingleton.Shutdown();
            try { _webApp?.StopAsync(); } catch { }
        }
    }
}