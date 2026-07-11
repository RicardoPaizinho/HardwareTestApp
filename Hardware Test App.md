# Hardware Test App

Este projeto é uma aplicação de testes de hardware desenvolvida em C# com uma interface de usuário construída com tecnologias web (HTML, CSS, JavaScript) e renderizada através do controle WebView2.

## Funcionalidades

- **Informações do PC:** Exibe o modelo e número de série do computador na barra superior.
- **Painel de Performance:** Um painel retrátil na lateral direita mostra o uso de CPU, memória e disco em tempo real.
- **Menu Lateral:** Um menu na lateral esquerda permite navegar entre as diferentes páginas de teste.
- **Cards de Teste:** A página central exibe cards para iniciar testes de hardware específicos, como speaker, microfone e câmera.

## Estrutura do Projeto

- `HardwareTestApp.csproj`: Arquivo de projeto C# que define as dependências e configurações.
- `Program.cs`: Ponto de entrada da aplicação C#.
- `MainForm.cs`: A janela principal do aplicativo WinForms, que hospeda o controle WebView2 e gerencia a comunicação entre C# e JavaScript.
- `SystemInfo.cs`: Classe C# responsável por coletar informações do sistema operacional (modelo do PC, número de série, uso de CPU, memória e disco).
- `wwwroot/`: Diretório que contém todos os arquivos da interface web.
    - `wwwroot/index.html`: O arquivo HTML principal que define a estrutura da interface.
    - `wwwroot/styles.css`: O arquivo CSS para estilização da interface.
    - `wwwroot/script.js`: O arquivo JavaScript que gerencia a interatividade da interface, a navegação, os testes de hardware (speaker, microfone, câmera) e a comunicação com o C#.

## Como Configurar e Executar (Ambiente Windows)

Esta aplicação foi projetada para ser executada em um ambiente Windows com o .NET 8.0 SDK instalado.

### Pré-requisitos

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (ou versão superior)
- [Visual Studio](https://visualstudio.microsoft.com/) (recomendado para desenvolvimento WinForms)
- O runtime do WebView2 (geralmente já vem com o Windows 10/11 ou Microsoft Edge)

### Passos para Executar

1.  **Crie o diretório do projeto:**
    Crie uma pasta vazia no seu sistema Windows, por exemplo, `C:\HardwareTestApp`.

2.  **Copie os arquivos:**
    Copie todos os arquivos gerados (`HardwareTestApp.csproj`, `Program.cs`, `MainForm.cs`, `SystemInfo.cs` e a pasta `wwwroot`) para o diretório `C:\HardwareTestApp`.

3.  **Abra o projeto no Visual Studio (ou via CLI):**
    - **Visual Studio:** Abra o Visual Studio, clique em `File > Open > Project/Solution` e selecione o arquivo `HardwareTestApp.csproj`.
    - **Linha de Comando:** Navegue até o diretório `C:\HardwareTestApp` no seu terminal (PowerShell ou Prompt de Comando).

4.  **Restaure as dependências NuGet:**
    Se estiver usando o Visual Studio, as dependências devem ser restauradas automaticamente. Caso contrário, ou via linha de comando, execute:
    ```bash
    dotnet restore
    ```

5.  **Compile e Execute:**
    - **Visual Studio:** Clique em `Start` (o botão verde de play) ou `Debug > Start Debugging`.
    - **Linha de Comando:** Execute:
    ```bash
    dotnet run
    ```

## Comunicação entre C# e JavaScript

A comunicação bidirecional entre o C# e o JavaScript é realizada através do objeto `window.chrome.webview` fornecido pelo WebView2.

### JavaScript para C#

O JavaScript envia mensagens para o C# usando `window.chrome.webview.postMessage(JSON.stringify(data))`. Por exemplo, para solicitar informações do PC:

```javascript
window.chrome.webview.postMessage(JSON.stringify({ type: 'requestPCInfo' }));
```

No C#, o evento `webView21.CoreWebView2.WebMessageReceived` é manipulado para receber e processar essas mensagens. As mensagens são analisadas como JSON para determinar o tipo de solicitação.

### C# para JavaScript

O C# envia dados para o JavaScript usando `webView21.CoreWebView2.PostWebMessageAsJson(json)`. Por exemplo, para atualizar as informações do PC:

```csharp
var data = new { type = "pcInfo", model = model, serialNumber = serial };
string json = JsonSerializer.Serialize(data);
webView21.CoreWebView2.PostWebMessageAsJson(json);
```

No JavaScript, um `eventListener` é adicionado a `window.chrome.webview` para escutar as mensagens do C# e atualizar a interface de usuário de acordo.

```javascript
window.chrome.webview.addEventListener("message", (event) => {
  const data = JSON.parse(event.data);
  switch (data.type) {
    case "pcInfo":
      updatePCInfo(data.model, data.serialNumber);
      break;
    case "performanceData":
      updatePerformance(data.cpu, data.memory, data.disk);
      break;
  }
});
```

## Observações

- Os testes de hardware (speaker, microfone, câmera) na interface web utilizam as APIs nativas do navegador (`Web Audio API`, `MediaDevices API`). Eles funcionarão desde que o WebView2 tenha permissão para acessá-los.
- A coleta de dados de performance (CPU, memória, disco) e informações do PC (modelo, número de série) é feita em C# usando `System.Management` e `PerformanceCounter`, que são APIs específicas do Windows. Essas funcionalidades não são compatíveis com ambientes Linux.

