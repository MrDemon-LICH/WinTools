using System.Configuration;
using System.Data;
using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinTools
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private const int SW_RESTORE = 9;

        // Mutex para control de instancia única
        private static Mutex? _singleInstanceMutex;

        // Propiedad para indicar si la aplicación debe iniciarse minimizada
        public static bool ShouldStartMinimized { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Verificar si se pasó el parámetro --close
            if (e.Args.Length > 0 && e.Args[0] == "--close")
            {
                // Buscar instancia existente de WinTools y cerrarla
                CloseExistingInstance();
                // Cerrar esta instancia inmediatamente
                Shutdown();
                return;
            }

            // Control de instancia única
            const string mutexName = "WinTools_SingleInstance_Mutex";
            bool createdNew;

            _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);

            if (!createdNew)
            {
                // Ya hay una instancia corriendo, activar la ventana existente
                ActivateExistingInstance();
                // Cerrar esta instancia
                _singleInstanceMutex.Close();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            // Verificar si se pasó el parámetro --minimized (desde autostart)
            ShouldStartMinimized = e.Args.Length > 0 && e.Args[0] == "--minimized";

            // Asegurar que el Mutex se libere al cerrar la aplicación
            this.Exit += (s, args) =>
            {
                _singleInstanceMutex?.Close();
                _singleInstanceMutex = null;
            };

            base.OnStartup(e);
        }

        private void CloseExistingInstance()
        {
            try
            {
                // Buscar procesos de WinTools
                var processes = Process.GetProcessesByName("WinTools");
                foreach (var process in processes)
                {
                    try
                    {
                        // No cerrar el proceso actual
                        if (process.Id != Process.GetCurrentProcess().Id)
                        {
                            // Intentar cerrar gracefully primero
                            process.CloseMainWindow();
                            // Esperar un poco
                            if (!process.WaitForExit(3000))
                            {
                                // Si no responde, forzar cierre
                                process.Kill();
                            }
                        }
                    }
                    catch
                    {
                        // Ignorar errores al cerrar procesos
                    }
                }
            }
            catch
            {
                // Ignorar errores generales
            }
        }

        private void ActivateExistingInstance()
        {
            try
            {
                // Buscar la ventana de WinTools por su título
                IntPtr hWnd = FindWindow(string.Empty, "WinTools");

                if (hWnd != IntPtr.Zero)
                {
                    // Si la ventana está minimizada, restaurarla
                    if (IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                    }

                    // Traer la ventana al frente
                    SetForegroundWindow(hWnd);
                }
            }
            catch
            {
                // Si hay algún error, ignorarlo y continuar
            }
        }
    }

}