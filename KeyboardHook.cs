using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Concurrent;

public static class KeyboardInterceptor
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    public static bool IsEnabled = false;

    private static LowLevelKeyboardProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;

    // Fila segura para threads que vai acumular as teclas capturadas
    private static readonly ConcurrentQueue<object> _capturedKeysQueue = new ConcurrentQueue<object>();

    public static void SetHook()
    {
        if (_hookID != IntPtr.Zero) return; // Evita registrar o hook duplicado

        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule)
        {
            _hookID = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    public static void Unhook()
    {
        if (_hookID != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookID);
            _hookID = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Retorna e limpa todos os eventos de teclas acumulados desde a última leitura da API Web
    /// </summary>
    public static System.Collections.Generic.List<object> FlushEvents()
    {
        var list = new System.Collections.Generic.List<object>();
        while (_capturedKeysQueue.TryDequeue(out var keyEvent))
        {
            list.Add(keyEvent);
        }
        return list;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsEnabled)
        {
            int msg = (int)wParam;

            // Verifica se o evento é de tecla pressionada (KeyDown ou SysKeyDown para combinações com Alt)
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                // Captura a tecla e joga para a fila em background
                NotifyKeyToQueue(vkCode);

                // Opcional: Se quiser continuar bloqueando teclas de sistema como Win (91, 92) ou Sleep (95)
                if (vkCode == 91 || vkCode == 92 || vkCode == 95)
                {
                    return (IntPtr)1; // Bloqueia a execução da tecla no Windows
                }
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private static void NotifyKeyToQueue(int vkCode)
    {
        // Adiciona o evento estruturado à fila concorrente que a API lê
        _capturedKeysQueue.Enqueue(new
        {
            code = vkCode,
            time = DateTime.Now.ToString("HH:mm:ss.fff")
        });
    }

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
}