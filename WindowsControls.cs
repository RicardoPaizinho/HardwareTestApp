using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace HardwareTestApp
{
    [ComVisible(true)]
    public class WindowControls
    {
        private Form _form;
        private Control _webView; // Precisamos do webview aqui para devolver o foco

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        public WindowControls(Form form, Control webView)
        {
            _form = form;
            _webView = webView;
        }

        public void CloseApp() => Application.Exit();

        public void MinimizeApp() => _form.WindowState = FormWindowState.Minimized;

        public void MaximizeApp()
        {
            _form.Invoke(new MethodInvoker(() => {
                if (_form.WindowState == FormWindowState.Maximized)
                    _form.WindowState = FormWindowState.Normal;
                else
                    _form.WindowState = FormWindowState.Maximized;

                // Sempre que maximizar/restaurar, garanta o foco no webview
                _webView.Focus();
            }));
        }

        public void DragWindow()
        {
            // O Invoke deve ser síncrono para capturar o mouse no momento exato do clique
            _form.Invoke(new MethodInvoker(() => {
                ReleaseCapture();
                SendMessage(_form.Handle, 0xA1, 0x2, 0);

                // --- O SEGREDO ESTÁ AQUI ---
                // Após soltar o arrasto, o Windows "esquece" o foco interno.
                // Forçamos o retorno do foco para o WebView para o Gamepad voltar.
                _webView.Focus();
            }));
        }
    }
}