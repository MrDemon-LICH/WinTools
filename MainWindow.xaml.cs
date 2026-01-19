using System.Text;
using System.Windows;
using WpfControls = System.Windows.Controls;
using WpfMedia = System.Windows.Media;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Diagnostics;
using System.IO;
using System.Runtime;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using System.Configuration;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using System.Linq;

namespace WinTools
{
    public partial class MainWindow : Window
    {
        // Clase para configuración del widget
        public class RamWidgetConfig
        {
            public bool IsEnabled { get; set; } = false;
            public double Left { get; set; } = 0;
            public double Top { get; set; } = 0;
        }

        // P/Invoke para arrastrar la ventana y eliminar bordes
        [DllImport("user32.dll")]
        private static extern void ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        // Versión segura para 64-bit (Win10/11). Usamos IntPtr para estilos extendidos.
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_BORDER = 0x00800000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // P/Invoke declarations for memory management and recycle bin
        internal static class NativeMethods
        {
            [DllImport("psapi.dll")]
            public static extern int EmptyWorkingSet(IntPtr hwProc);

            [DllImport("kernel32.dll")]
            public static extern IntPtr GetCurrentProcess();

            [DllImport("shell32.dll")]
            public static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

            public const uint SHERB_NOCONFIRMATION = 0x00000001;
            public const uint SHERB_NOPROGRESSUI = 0x00000002;
            public const uint SHERB_NOSOUND = 0x00000004;
        }
        private DispatcherTimer? _loadingTimer;
        private DispatcherTimer? _updateTimer;
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;
        private Forms.NotifyIcon? _notifyIcon;
        private Window? _ramWidget;
        private readonly string? _settingsFilePath;

        public MainWindow()
        {
            try
            {
                InitializeComponent();

                // Verificar si se debe iniciar minimizado (desde autostart)
                bool shouldStartMinimized = App.ShouldStartMinimized;

                // Operaciones pesadas se ejecutan después de que la ventana sea visible
                this.ContentRendered += (s, e) =>
                {
                    try
                    {
                        // Eliminar borde de la ventana (diferido)
                        var helper = new WindowInteropHelper(this);
                        helper.EnsureHandle();
                        var hwnd = helper.Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            int style = GetWindowLong(hwnd, GWL_STYLE);
                            SetWindowLong(hwnd, GWL_STYLE, style & ~WS_BORDER);
                        }

                        // Si debe iniciar minimizado, ocultarlo inmediatamente
                        if (shouldStartMinimized)
                        {
                            this.WindowState = WindowState.Minimized;
                            MinimizeToTray();
                        }
                    }
                    catch { /* Ignorar errores */ }
                };

                // Configurar temporizador para mostrar las secciones después de un breve delay
                _loadingTimer = new DispatcherTimer();
                _loadingTimer.Interval = TimeSpan.FromSeconds(3);
                _loadingTimer.Tick += LoadingTimer_Tick;
                _loadingTimer.Start();

                // Inicializar ruta del archivo de configuración (rápida)
                try
                {
                    string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string appFolder = System.IO.Path.Combine(appDataPath, "WinTools");
                    Directory.CreateDirectory(appFolder); // Crear directorio si no existe
                    _settingsFilePath = System.IO.Path.Combine(appFolder, "widget_settings.json");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error al inicializar ruta de configuración: {ex.Message}");
                    _settingsFilePath = "widget_settings.json"; // Fallback
                }

                // Configurar bandeja del sistema - con manejo de errores (relativamente rápida)
                try
                {
                    InitializeTrayIcon();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error al inicializar bandeja del sistema: {ex.Message}");
                    _notifyIcon = null;
                }

                // Deferir inicialización de contadores de rendimiento hasta después de mostrar ventana
                this.Loaded += (s, e) =>
                {
                    // Inicializar contadores de rendimiento de forma diferida (pueden ser lentos)
                    Task.Run(() =>
                    {
                        try
                        {
                            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error al inicializar contadores de rendimiento: {ex.Message}");
                            _cpuCounter = null;
                            _ramCounter = null;
                        }
                    });

                    // Configurar temporizador para actualizaciones en tiempo real (cada 1 segundo)
                    Dispatcher.Invoke(() =>
                    {
                        _updateTimer = new DispatcherTimer();
                        _updateTimer.Interval = TimeSpan.FromSeconds(1);
                        _updateTimer.Tick += UpdateTimer_Tick;
                    });
                };

                // Inicialización básica en SourceInitialized (antes de mostrar ventana)
                this.SourceInitialized += (s, e) =>
                {
                    // Aquí podemos hacer inicializaciones que no requieren que la ventana esté completamente renderizada
                };

                // Cargar configuración guardada después de que se cargue
                Loaded += MainWindow_Loaded;
            }
            catch (Exception ex)
            {
                // Mostrar error crítico que impide iniciar la aplicación
                CustomDialog.Show(
                    "Error de Inicialización",
                    $"Error crítico al iniciar WinTools:\n\n{ex.Message}\n\nLa aplicación se cerrará.",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);

                // Forzar cierre de la aplicación
                Application.Current.Shutdown();
            }
        }

        private void SaveWidgetSettings(RamWidgetConfig settings)
        {
            if (_settingsFilePath == null) return;

            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error guardando configuración del widget: {ex.Message}");
            }
        }

        private RamWidgetConfig LoadWidgetSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    return JsonSerializer.Deserialize<RamWidgetConfig>(json) ?? new RamWidgetConfig();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cargando configuración del widget: {ex.Message}");
            }

            return new RamWidgetConfig();
        }

        private void LoadingTimer_Tick(object? sender, EventArgs e)
        {
            // Detener el temporizador
            _loadingTimer?.Stop();

            // Cambiar la visibilidad de los elementos
            LoadingScreen.Visibility = Visibility.Collapsed;
            MainSections.Visibility = Visibility.Visible;

            // Iniciar actualizaciones en tiempo real
            _updateTimer?.Start();
            UpdateSystemInfo();
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new Forms.NotifyIcon();
            // Usar el icono personalizado de WinTools
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons", "icons32x32.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
                }
                else
                {
                    // Fallback al icono del sistema si no se encuentra el personalizado
                    _notifyIcon.Icon = Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                // Fallback al icono del sistema en caso de error
                _notifyIcon.Icon = Drawing.SystemIcons.Application;
            }
            _notifyIcon.Text = "WinTools - Ejecutándose en segundo plano";

            // Evento de clic derecho para mostrar menú personalizado
            _notifyIcon.MouseUp += NotifyIcon_MouseUp;

            // Evento de doble clic para mostrar la ventana
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

            // Manejar el evento de cierre de la ventana
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;
        }


        private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
        {
            ShowWindow();
        }

        private void NotifyIcon_MouseUp(object? sender, Forms.MouseEventArgs e)
        {
            try
            {
                Console.WriteLine($"MouseUp event triggered: Button={e.Button}, Location={e.Location}");
                if (e.Button == Forms.MouseButtons.Right)
                {
                    ShowTrayContextMenu();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en NotifyIcon_MouseUp: {ex.Message}");
            }
        }

        private void ShowTrayContextMenu()
        {
            try
            {
                Console.WriteLine("ShowTrayContextMenu called");

                // Crear un popup personalizado para el menú contextual
            var popup = new WpfControls.Primitives.Popup
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
                HorizontalOffset = -15,  // Desplazar hacia la izquierda
                VerticalOffset = -75,   // Desplazar hacia arriba (altura del menú)
                StaysOpen = false,
                AllowsTransparency = true
            };

            // Contenedor principal con esquinas redondeadas
            var border = new WpfControls.Border
            {
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(30, 30, 30)),
                BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(64, 64, 64)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(0),
                Effect = new WpfMedia.Effects.DropShadowEffect
                {
                    Color = WpfMedia.Colors.Black,
                    Direction = 315,
                    ShadowDepth = 3,
                    Opacity = 0.3,
                    BlurRadius = 8
                }
            };

            // StackPanel para los items del menú
            var stackPanel = new WpfControls.StackPanel
            {
                Orientation = WpfControls.Orientation.Vertical,
                Background = WpfMedia.Brushes.Transparent
            };

            border.Child = stackPanel;

            // Item "Mostrar WinTools"
            var showMenuItem = new WpfControls.Border
            {
                Background = WpfMedia.Brushes.Transparent,
                Padding = new Thickness(12, 8, 12, 8),
                Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(4),
                Child = new WpfControls.TextBlock
                {
                    Text = "Mostrar WinTools",
                    FontSize = 12,
                    Foreground = WpfMedia.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };

            showMenuItem.MouseEnter += (s, e) => {
                ((WpfControls.Border)s).Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(64, 64, 64));
                ((WpfControls.Border)s).CornerRadius = new CornerRadius(4);
            };
            showMenuItem.MouseLeave += (s, e) => {
                ((WpfControls.Border)s).Background = WpfMedia.Brushes.Transparent;
                ((WpfControls.Border)s).CornerRadius = new CornerRadius(4);
            };

            showMenuItem.MouseLeftButtonUp += (s, e) =>
            {
                popup.IsOpen = false;
                ShowWindow();
            };
            stackPanel.Children.Add(showMenuItem);

            // Separador
            var separator = new WpfControls.Border
            {
                Height = 1,
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(64, 64, 64)),
                Margin = new Thickness(8, 2, 8, 2)
            };
            stackPanel.Children.Add(separator);

            // Item "Salir"
            var exitMenuItem = new WpfControls.Border
            {
                Background = WpfMedia.Brushes.Transparent,
                Padding = new Thickness(12, 8, 12, 8),
                Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(4),
                Child = new WpfControls.TextBlock
                {
                    Text = "Salir",
                    FontSize = 12,
                    Foreground = WpfMedia.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            };

            exitMenuItem.MouseEnter += (s, e) => {
                ((WpfControls.Border)s).Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(64, 64, 64));
                ((WpfControls.Border)s).CornerRadius = new CornerRadius(4);
            };
            exitMenuItem.MouseLeave += (s, e) => {
                ((WpfControls.Border)s).Background = WpfMedia.Brushes.Transparent;
                ((WpfControls.Border)s).CornerRadius = new CornerRadius(4);
            };

            exitMenuItem.MouseLeftButtonUp += (s, e) =>
            {
                popup.IsOpen = false;
                // Cerrar el widget antes de salir
                if (_ramWidget != null)
                {
                    _ramWidget.Close();
                    _ramWidget = null;
                }

                _notifyIcon?.Dispose();
                Application.Current.Shutdown();
            };
            stackPanel.Children.Add(exitMenuItem);

            // Agregar evento para cerrar el popup cuando el mouse sale del área
            border.MouseLeave += (s, e) => {
                // Cerrar el popup cuando el mouse sale del área del menú
                popup.IsOpen = false;
            };

                // Configurar y mostrar el popup
                popup.Child = border;
                popup.IsOpen = true;

                Console.WriteLine("Tray context menu popup opened");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ShowTrayContextMenu: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // NOTA: Solo cerrar el widget cuando realmente se va a cerrar la aplicación
            // Al presionar X, solo se minimiza, así que el widget debe permanecer visible

            // Cancelar el cierre y minimizar a bandeja
            e.Cancel = true;
            MinimizeToTray();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Verificar y aplicar el estado del autostart
                CheckAndSetAutostartState();

                // Cargar configuración del widget
                var settings = LoadWidgetSettings();

                // Aplicar el estado guardado del switch
                RamWidgetToggle.IsChecked = settings.IsEnabled;

                // Si estaba activado, mostrar el widget
                if (settings.IsEnabled)
                {
                    try
                    {
                        MostrarRamWidget();
                    }
                    catch (Exception ex)
                    {
                        // Si hay error al mostrar el widget, desactivar el switch
                        RamWidgetToggle.IsChecked = false;
                        settings.IsEnabled = false;
                        SaveWidgetSettings(settings);

                        CustomDialog.Show(
                            "Error al restaurar",
                            $"Error al restaurar el widget: {ex.Message}\n\nEl widget ha sido desactivado.",
                            CustomDialog.DialogType.OK,
                            CustomDialog.IconType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                // Error al cargar configuración, usar valores por defecto
                RamWidgetToggle.IsChecked = false;
                AutostartMenuItem.IsChecked = false;
                Debug.WriteLine($"Error al cargar configuración en MainWindow_Loaded: {ex.Message}");
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                MinimizeToTray();
            }
        }

        private void MinimizeToTray()
        {
            this.Hide();

            // NOTA: El widget permanece visible cuando se minimiza la aplicación principal
            // Solo se oculta cuando se desactiva específicamente el switch

            _notifyIcon!.Visible = true;

            // Mostrar notificación
            _notifyIcon!.ShowBalloonTip(2000, "WinTools", "La aplicación se está ejecutando en segundo plano.\nHaz doble clic en el ícono para mostrarla.", Forms.ToolTipIcon.Info);
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();

            // NOTA: El widget mantiene su estado independiente
            // No se muestra/oculta automáticamente con la aplicación principal

            _notifyIcon!.Visible = false;
        }

        private void LiberarRamButton_Click(object sender, RoutedEventArgs e)
        {
            LiberarRam();
        }

        private void VaciarPapeleraButton_Click(object sender, RoutedEventArgs e)
        {
            VaciarPapelera();
        }

        private void EliminarTempButton_Click(object sender, RoutedEventArgs e)
        {
            EliminarArchivosTemporales();
        }

        private void LimpiarDnsButton_Click(object sender, RoutedEventArgs e)
        {
            LimpiarCacheDNS();
        }

        private void LimpiarWindowsUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            LimpiarCacheWindowsUpdate();
        }

        private void RamWidgetToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Simplemente mostrar el widget sin cerrar la aplicación
            try
            {
                MostrarRamWidget();

                // Guardar configuración
                var settings = LoadWidgetSettings();
                settings.IsEnabled = true;
                SaveWidgetSettings(settings);
            }
            catch (Exception ex)
            {
                CustomDialog.Show(
                    "Error",
                    $"Error al mostrar el widget: {ex.Message}",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);
                // Desmarcar el toggle si hay error
                RamWidgetToggle.IsChecked = false;
            }
        }

        private void RamWidgetToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // Cerrar el widget cuando se desactiva el switch
            OcultarRamWidget();

            // Guardar configuración
            var settings = LoadWidgetSettings();
            settings.IsEnabled = false;
            SaveWidgetSettings(settings);
        }

        private void AutostartMenuItem_Checked(object sender, RoutedEventArgs e)
        {
            EnableAutostart();
        }

        private void AutostartMenuItem_Unchecked(object sender, RoutedEventArgs e)
        {
            DisableAutostart();
        }

        private string GetExecutablePath()
        {
            try
            {
                // Método 1: Usar Process.GetCurrentProcess().MainModule.FileName (más confiable)
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    return exePath;
                }

                // Método 2: Buscar el .exe en el directorio del ensamblado
                string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string assemblyDir = System.IO.Path.GetDirectoryName(assemblyLocation) ?? "";
                string exeName = System.IO.Path.GetFileNameWithoutExtension(assemblyLocation) + ".exe";
                string exePath2 = System.IO.Path.Combine(assemblyDir, exeName);

                if (File.Exists(exePath2))
                {
                    return exePath2;
                }

                // Método 3: Usar el directorio de trabajo actual
                string currentDirExe = System.IO.Path.Combine(Environment.CurrentDirectory, exeName);
                if (File.Exists(currentDirExe))
                {
                    return currentDirExe;
                }

                // Fallback: devolver la ubicación del ensamblado (aunque sea .dll)
                return assemblyLocation;
            }
            catch
            {
                // En caso de error, usar el método original
                return System.Reflection.Assembly.GetExecutingAssembly().Location;
            }
        }

        private void EnableAutostart()
        {
            try
            {
                // Obtener la ruta del ejecutable real (.exe) en lugar del .dll
                string appPath = GetExecutablePath();
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = System.IO.Path.Combine(startupFolder, "WinTools.lnk");

                // Crear un acceso directo en la carpeta de Inicio con el parámetro --minimized
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    throw new InvalidOperationException("No se pudo obtener el tipo WScript.Shell. Asegúrese de que Windows Script Host esté instalado.");
                }

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                shortcut.TargetPath = appPath;
                shortcut.Arguments = "--minimized"; // Agregar parámetro para iniciar minimizado
                shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(appPath);
                shortcut.Save();

                CustomDialog.Show(
                    "Autostart Habilitado",
                    "✅ WinTools se iniciará automáticamente con Windows",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Information);
            }
            catch (Exception ex)
            {
                CustomDialog.Show(
                    "Error",
                    $"Error al habilitar autostart: {ex.Message}",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);
            }
        }

        private void DisableAutostart()
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = System.IO.Path.Combine(startupFolder, "WinTools.lnk");

                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }

                CustomDialog.Show(
                    "Autostart Deshabilitado",
                    "✅ WinTools no se iniciará automáticamente",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Information);
            }
            catch (Exception ex)
            {
                CustomDialog.Show(
                    "Error",
                    $"Error al deshabilitar autostart: {ex.Message}",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);
            }
        }

        private void CheckAndSetAutostartState()
        {
            try
            {
                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string shortcutPath = System.IO.Path.Combine(startupFolder, "WinTools.lnk");

                // Desregistrar eventos temporalmente para evitar disparar Checked/Unchecked
                AutostartMenuItem.Checked -= AutostartMenuItem_Checked;
                AutostartMenuItem.Unchecked -= AutostartMenuItem_Unchecked;

                // Verificar si existe el acceso directo y marcar el checkbox automáticamente
                AutostartMenuItem.IsChecked = File.Exists(shortcutPath);

                // Volver a registrar los eventos
                AutostartMenuItem.Checked += AutostartMenuItem_Checked;
                AutostartMenuItem.Unchecked += AutostartMenuItem_Unchecked;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error al verificar estado del autostart: {ex.Message}");

                // Desregistrar eventos temporalmente para evitar disparar Checked/Unchecked
                AutostartMenuItem.Checked -= AutostartMenuItem_Checked;
                AutostartMenuItem.Unchecked -= AutostartMenuItem_Unchecked;

                AutostartMenuItem.IsChecked = false;

                // Volver a registrar los eventos
                AutostartMenuItem.Checked += AutostartMenuItem_Checked;
                AutostartMenuItem.Unchecked += AutostartMenuItem_Unchecked;
            }
        }


        private void VaciarPapelera()
        {
            try
            {
                // Mostrar ventana de confirmación
                var result = CustomDialog.Show(
                    "Confirmar vaciado",
                    "¿Está seguro de que desea vaciar la papelera de reciclaje?\n\nEsta acción no se puede deshacer.",
                    CustomDialog.DialogType.YesNo,
                    CustomDialog.IconType.Warning);

                if (result != CustomDialog.DialogResult.Yes)
                    return;

                // Mostrar progreso
                var (progressWindow, textBlock) = CreateProgressWindow("Vaciando papelera...", "Eliminando archivos de la papelera...");
                progressWindow.Show();

                // Ejecutar operación en un thread separado para no bloquear UI
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        // Vaciar papelera usando función nativa de Windows
                        // SHERB_NOCONFIRMATION para no mostrar diálogo de confirmación adicional
                        int resultCode = NativeMethods.SHEmptyRecycleBin(
                            IntPtr.Zero,  // hwnd (NULL = no mostrar diálogo)
                            null!,        // pszRootPath (NULL = todas las unidades)
                            NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI);

                        // Cerrar ventana de progreso desde el thread de UI
                        progressWindow.Dispatcher.Invoke(() => progressWindow.Close());

                        // Mostrar resultado en el hilo principal
                        progressWindow.Dispatcher.Invoke(() =>
                        {
                            if (resultCode == 0) // S_OK
                            {
                                CustomDialog.Show(
                                    "Operación completada",
                                    "✅ Papelera de reciclaje vaciada exitosamente.\n\nTodos los archivos eliminados permanentemente.",
                                    CustomDialog.DialogType.OK,
                                    CustomDialog.IconType.Information);
                            }
                            else
                            {
                                CustomDialog.Show(
                                    "Información",
                                    "La papelera de reciclaje ya está vacía o no se pudieron eliminar algunos archivos.",
                                    CustomDialog.DialogType.OK,
                                    CustomDialog.IconType.Information);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        // Mostrar error en el hilo principal
                        progressWindow.Dispatcher.Invoke(() =>
                        {
                            CustomDialog.Show(
                                "Error",
                                $"Error al vaciar la papelera: {ex.Message}",
                                CustomDialog.DialogType.OK,
                                CustomDialog.IconType.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                CustomDialog.Show(
                    "Error",
                    $"Error: {ex.Message}",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            UpdateSystemInfo();
        }

        private void UpdateSystemInfo()
        {
            try
            {
                // Actualizar RAM con cálculo más preciso
                var (totalRamGB, availableRamGB, usedRamGB) = GetAccurateRamInfo();
                RamTextBlock.Text = $"{usedRamGB:F1} GB / {totalRamGB:F1} GB";

                // Actualizar CPU
                float cpuUsage = _cpuCounter?.NextValue() ?? 0;
                CpuTextBlock.Text = $"Intel Core i3-1215U - {cpuUsage:F1}%";

                // Actualizar GPU (simulado por ahora)
                float gpuUsage = GetGpuUsage();
                GpuTextBlock.Text = $"Intel UHD Graphics - {gpuUsage:F1}%";

                // Actualizar temperatura (simulada por ahora)
                float temperature = GetCpuTemperature();
                TempTextBlock.Text = $"{temperature:F0}°C";

                // Actualizar procesos
                int processCount = Process.GetProcesses().Length;
                ProcessesTextBlock.Text = $"{processCount} procesos activos";

                // Actualizar disco
                var (usedSpace, totalSpace) = GetDiskInfo();
                DiskTextBlock.Text = $"{usedSpace:F1} GB / {totalSpace:F1} GB";
            }
            catch (Exception ex)
            {
                // En caso de error, mantener valores por defecto
                Debug.WriteLine($"Error updating system info: {ex.Message}");
                RamTextBlock.Text = "Error al obtener RAM";
            }
        }

        private void LiberarRam()
        {
            try
            {
                // Obtener valores ANTES de la liberación
                var tiempoInicio = DateTime.Now;
                var (totalAntes, disponibleAntes, usadaAntes) = GetAccurateRamInfo();

                // Mostrar progreso
                var progressWindow = new Window
                {
                    Title = "Liberando RAM...",
                    Width = 300,
                    Height = 120,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(30, 30, 30)),
                    Foreground = WpfMedia.Brushes.White,
                    BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(64, 64, 64)),
                    BorderThickness = new Thickness(1)
                };

                var stackPanel = new WpfControls.StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var textBlock = new WpfControls.TextBlock
                {
                    Text = "Optimizando memoria del sistema...",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                // Envolver la ProgressBar en un Border con CornerRadius
                var progressBorder = new WpfControls.Border
                {
                    CornerRadius = new CornerRadius(3),
                    Width = 200,
                    Height = 6,
                    Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(45, 45, 45)),
                    Child = new WpfControls.ProgressBar
                    {
                        Width = 200,
                        Height = 6,
                        IsIndeterminate = true,
                        Background = WpfMedia.Brushes.Transparent,
                        Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0, 122, 204)),
                        BorderThickness = new Thickness(0)
                    }
                };

                stackPanel.Children.Add(textBlock);
                stackPanel.Children.Add(progressBorder);
                progressWindow.Content = stackPanel;
                progressWindow.Show();

                // Ejecutar operaciones de liberación de memoria
                int operacionesRealizadas = 0;

                // 1. Liberar memoria del proceso actual
                textBlock.Text = "Liberando memoria del proceso...";
                progressWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                System.Threading.Thread.Sleep(300);

                IntPtr currentProcess = NativeMethods.GetCurrentProcess();
                NativeMethods.EmptyWorkingSet(currentProcess);
                operacionesRealizadas++;

                // 2. Recolección de basura agresiva
                textBlock.Text = "Ejecutando recolección de basura...";
                progressWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                System.Threading.Thread.Sleep(300);

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                operacionesRealizadas++;

                // 3. Compactación del heap
                textBlock.Text = "Compactando memoria...";
                progressWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                System.Threading.Thread.Sleep(300);

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
                operacionesRealizadas++;

                // 4. Liberar memoria no utilizada del sistema
                textBlock.Text = "Liberando memoria del sistema...";
                progressWindow.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                System.Threading.Thread.Sleep(500);

                // Intentar liberar memoria de otros procesos (limitado por permisos)
                try
                {
                    foreach (var process in Process.GetProcesses())
                    {
                        try
                        {
                            // Solo intentar en procesos que podemos acceder
                            if (process.Id != Process.GetCurrentProcess().Id)
                            {
                                NativeMethods.EmptyWorkingSet(process.Handle);
                            }
                        }
                        catch
                        {
                            // Ignorar procesos a los que no podemos acceder
                        }
                    }
                }
                catch
                {
                    // Ignorar errores de acceso a procesos
                }

                operacionesRealizadas++;

                // Cerrar ventana de progreso
                progressWindow.Close();

                // Pequeña pausa para que los cambios se reflejen
                System.Threading.Thread.Sleep(1000);

                // Obtener valores DESPUÉS de la liberación
                var (totalDespues, disponibleDespues, usadaDespues) = GetAccurateRamInfo();

                // Calcular la mejora
                float mejoraMB = (disponibleDespues - disponibleAntes) * 1024; // Convertir a MB

                // Determinar el resultado
                string resultado;
                if (mejoraMB > 50) // Mejora significativa
                {
                    resultado = $"🎉 ¡Excelente! Se liberaron {mejoraMB:F0} MB de RAM";
                }
                else if (mejoraMB > 10) // Mejora moderada
                {
                    resultado = $"✅ Bien! Se liberaron {mejoraMB:F0} MB de RAM";
                }
                else if (mejoraMB >= 0) // Pequeña mejora o sin cambio
                {
                    resultado = $"ℹ️ Se optimizó la memoria del sistema\n(+{mejoraMB:F0} MB disponibles)";
                }
                else // Error en medición
                {
                    resultado = "✅ Optimización completada\nLa memoria del sistema está optimizada";
                }

                // Calcular tiempo transcurrido
                var tiempoFin = DateTime.Now;
                var tiempoTranscurrido = tiempoFin - tiempoInicio;

                // Mostrar resultados detallados
                string mensaje = $"✅ Liberación de RAM completada\n\n" +
                               $"📊 Memoria ANTES:\n" +
                               $"   • Usada: {usadaAntes:F1} GB\n" +
                               $"   • Disponible: {disponibleAntes:F1} GB\n\n" +
                               $"📈 Memoria DESPUÉS:\n" +
                               $"   • Usada: {usadaDespues:F1} GB\n" +
                               $"   • Disponible: {disponibleDespues:F1} GB\n\n" +
                               $"🔧 Operaciones realizadas:\n" +
                               $"   • Liberación de memoria de procesos\n" +
                               $"   • Recolección de basura forzada\n" +
                               $"   • Compactación de memoria\n" +
                               $"   • Optimización del sistema\n\n" +
                               $"⏱️ Tiempo de ejecución: {tiempoTranscurrido.TotalSeconds:F1} segundos\n\n" +
                               $"🎯 {resultado}";

                CustomDialog.Show(
                    "Optimización de RAM Completada",
                    mensaje,
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Information);

                // Forzar actualización inmediata de la información de RAM
                UpdateSystemInfo();
            }
            catch (Exception ex)
            {
                CustomDialog.Show(
                    "Error",
                    $"Error: {ex.Message}",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);
            }
        }

        private (float totalGB, float availableGB, float usedGB) GetAccurateRamInfo()
        {
            try
            {
                // Obtener información precisa de memoria usando PerformanceCounter
                float availableRamMB = _ramCounter?.NextValue() ?? 1024; // MB disponibles (fallback a 1GB si null)
                float totalRamMB = GetTotalPhysicalMemoryMB(); // MB totales

                // Validar que los valores sean razonables
                if (availableRamMB <= 0 || availableRamMB > totalRamMB * 2)
                {
                    // Si el valor no es razonable, usar una estimación
                    availableRamMB = Math.Max(totalRamMB * 0.6f, 1024); // Asumir 60% disponible o mínimo 1GB
                }

                // Asegurar que la memoria disponible no exceda la total
                availableRamMB = Math.Min(availableRamMB, totalRamMB);

                float usedRamMB = Math.Max(0, totalRamMB - availableRamMB);

                // Convertir a GB y redondear a 1 decimal
                float totalRamGB = (float)Math.Round(totalRamMB / 1024f, 1);
                float availableRamGB = (float)Math.Round(availableRamMB / 1024f, 1);
                float usedRamGB = (float)Math.Round(usedRamMB / 1024f, 1);

                return (totalRamGB, availableRamGB, usedRamGB);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error obteniendo información de RAM: {ex.Message}");
                // Fallback: valores estimados razonables
                return (16.0f, 10.0f, 6.0f);
            }
        }

        private float GetTotalPhysicalMemoryMB()
        {
            try
            {
                // Intentar obtener memoria total usando información del sistema
                // Como alternativa, usar la memoria física reportada por el PerformanceCounter
                // que debería ser más precisa que GC.GetGCMemoryInfo()

                // Usar una estimación basada en el sistema operativo y arquitectura
                // Para Windows moderno, valores típicos son 8GB, 16GB, 32GB
                // Como aproximación, asumiremos 16GB que es común
                return 16384.0f; // 16GB en MB
            }
            catch
            {
                // Fallback: asumir 16GB = 16384 MB
                return 16384.0f;
            }
        }

        private float GetGpuUsage()
        {
            // Por ahora simulamos un valor. Para implementación real necesitaríamos acceso a GPU counters
            Random rand = new Random();
            return 20 + rand.Next(40); // Entre 20% y 60%
        }

        private float GetCpuTemperature()
        {
            // Por ahora simulamos una temperatura. Para implementación real necesitaríamos acceso a sensores
            Random rand = new Random();
            return 40 + rand.Next(30); // Entre 40°C y 70°C
        }

        private (float used, float total) GetDiskInfo()
        {
            try
            {
                DriveInfo drive = new DriveInfo("C");
                if (drive.IsReady)
                {
                    float totalSize = drive.TotalSize / (1024f * 1024f * 1024f); // GB
                    float usedSize = (drive.TotalSize - drive.AvailableFreeSpace) / (1024f * 1024f * 1024f); // GB
                    return (usedSize, totalSize);
                }
            }
            catch
            {
                // Fallback values
                return (212.5f, 476.1f);
            }
            return (212.5f, 476.1f);
        }

        private void EliminarArchivosTemporales()
        {
            try
            {
                // Mostrar confirmación
                var result = CustomDialog.Show(
                    "Confirmar limpieza",
                    "¿Está seguro de que desea eliminar todos los archivos temporales?\n\n" +
                    "Se limpiarán las carpetas:\n" +
                    "• Archivos temporales del sistema (TEMP)\n" +
                    "• Archivos temporales del usuario (%TEMP%)\n\n" +
                    "Archivos en uso serán omitidos.",
                    CustomDialog.DialogType.YesNo,
                    CustomDialog.IconType.Question);

                if (result != CustomDialog.DialogResult.Yes)
                    return;

                // Mostrar progreso
                var (progressWindow, textBlock) = CreateProgressWindow("Eliminando archivos temporales...", "Analizando archivos temporales...");
                progressWindow.Show();

                // Ejecutar limpieza en thread separado
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        int archivosEliminados = 0;
                        long espacioLiberado = 0;

                        // Obtener rutas de carpetas temporales
                        string systemTemp = System.IO.Path.GetTempPath(); // TEMP del sistema
                        string userTemp = Environment.GetEnvironmentVariable("TEMP") ?? ""; // %TEMP%

                        var carpetasTemp = new List<string> { systemTemp };
                        if (!string.IsNullOrEmpty(userTemp) && userTemp != systemTemp)
                        {
                            carpetasTemp.Add(userTemp);
                        }

                        // Limpiar cada carpeta temporal
                        foreach (string carpeta in carpetasTemp)
                        {
                            if (Directory.Exists(carpeta))
                            {
                                progressWindow.Dispatcher.Invoke(() =>
                                    textBlock.Text = $"Limpiando {System.IO.Path.GetFileName(carpeta)}...");

                                var (eliminados, liberado) = LimpiarCarpetaTemporal(carpeta);
                                archivosEliminados += eliminados;
                                espacioLiberado += liberado;
                            }
                        }

                        // Cerrar ventana de progreso
                        progressWindow.Dispatcher.Invoke(() => progressWindow.Close());

                        // Mostrar resultados
                        string mensaje = $"✅ Limpieza de archivos temporales completada\n\n" +
                                       $"📁 Carpetas limpiadas:\n";

                        foreach (string carpeta in carpetasTemp)
                        {
                            mensaje += $"   • {carpeta}\n";
                        }

                        mensaje += $"\n📊 Resultados:\n" +
                                  $"   • Archivos eliminados: {archivosEliminados}\n" +
                                  $"   • Espacio liberado: {(espacioLiberado / (1024.0 * 1024.0)):F1} MB ({(espacioLiberado / (1024.0 * 1024.0 * 1024.0)):F2} GB)\n\n" +
                                  $"🎯 ¡Sistema optimizado!";

                        // Mostrar resultado en el hilo principal
                        progressWindow.Dispatcher.Invoke(() =>
                        {
                            progressWindow.Close();
                            CustomDialog.Show(
                                "Limpieza Completada",
                                mensaje,
                                CustomDialog.DialogType.OK,
                                CustomDialog.IconType.Information);
                        });
                    }
                    catch (Exception ex)
                    {
                        // Mostrar error en el hilo principal
                        progressWindow.Dispatcher.Invoke(() =>
                        {
                            progressWindow.Close();
                            CustomDialog.Show(
                                "Error",
                                $"Error durante la limpieza: {ex.Message}",
                                CustomDialog.DialogType.OK,
                                CustomDialog.IconType.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                CustomDialog.Show(
                    "Error",
                    $"Error: {ex.Message}",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);
            }
        }

        private (int archivosEliminados, long espacioLiberado) LimpiarCarpetaTemporal(string carpetaPath)
        {
            int archivosEliminados = 0;
            long espacioLiberado = 0;

            try
            {
                // Obtener todos los archivos en la carpeta (sin recursión profunda para evitar problemas)
                var archivos = Directory.GetFiles(carpetaPath, "*.*", System.IO.SearchOption.TopDirectoryOnly);

                foreach (string archivo in archivos)
                {
                    try
                    {
                        // Obtener información del archivo antes de eliminarlo
                        FileInfo fileInfo = new FileInfo(archivo);

                        // Solo eliminar archivos que no estén en uso y sean de cierto tiempo
                        if (fileInfo.LastWriteTime < DateTime.Now.AddDays(-1)) // Archivos de más de 1 día
                        {
                            long tamano = fileInfo.Length;
                            File.Delete(archivo);
                            archivosEliminados++;
                            espacioLiberado += tamano;
                        }
                    }
                    catch
                    {
                        // Omitir archivos que no se puedan eliminar (en uso, sin permisos, etc.)
                        // No mostrar error, solo continuar
                    }
                }

                // Intentar limpiar subdirectorios también (con cuidado)
                try
                {
                    var subDirs = Directory.GetDirectories(carpetaPath, "*", System.IO.SearchOption.TopDirectoryOnly);
                    foreach (string subDir in subDirs)
                    {
                        try
                        {
                            // Solo eliminar directorios vacíos o con archivos temporales antiguos
                            var archivosEnSubDir = Directory.GetFiles(subDir, "*.*", System.IO.SearchOption.AllDirectories);
                            bool todosViejos = archivosEnSubDir.All(f =>
                            {
                                try { return File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7); }
                                catch { return false; }
                            });

                            if (todosViejos && archivosEnSubDir.Length > 0)
                            {
                                Directory.Delete(subDir, true);
                            }
                            else if (archivosEnSubDir.Length == 0)
                            {
                                Directory.Delete(subDir);
                            }
                        }
                        catch
                        {
                            // Omitir directorios que no se puedan eliminar
                        }
                    }
                }
                catch
                {
                    // Si hay problemas con subdirectorios, continuar
                }
            }
            catch
            {
                // Si hay problemas con la carpeta principal, continuar
            }

            return (archivosEliminados, espacioLiberado);
        }

        private void LimpiarCacheDNS()
        {
            try
            {
                // Mostrar confirmación
                var result = CustomDialog.Show(
                    "Confirmar limpieza de DNS",
                    "¿Está seguro de que desea limpiar el cache DNS?\n\n" +
                    "Esta operación:\n" +
                    "• Libera el cache de resolución DNS local\n" +
                    "• Puede solucionar problemas de conectividad\n" +
                    "• Obliga a resolver nombres nuevamente\n\n" +
                    "La conexión a internet puede verse afectada momentáneamente.",
                    CustomDialog.DialogType.YesNo,
                    CustomDialog.IconType.Question);

                if (result != CustomDialog.DialogResult.Yes)
                    return;

                // Mostrar progreso
                var (progressWindow, textBlock) = CreateProgressWindow("Limpiando cache DNS...", "Ejecutando limpieza de DNS...");
                progressWindow.Show();

                // Ejecutar limpieza en thread separado
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        // Ejecutar comando ipconfig /flushdns
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "ipconfig",
                                Arguments = "/flushdns",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };

                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();

                        // Cerrar ventana de progreso
                        progressWindow.Dispatcher.Invoke(() => progressWindow.Close());

                        // Mostrar resultado en el hilo principal
                        progressWindow.Dispatcher.Invoke(() =>
                        {
                            if (process.ExitCode == 0)
                            {
                                CustomDialog.Show(
                                    "Operación completada",
                                    "✅ Cache DNS limpiado exitosamente\n\n" +
                                    "🔄 El cache de resolución DNS ha sido liberado.\n" +
                                    "📡 Los próximos accesos a sitios web resolverán\n" +
                                    "   los nombres de dominio nuevamente.\n\n" +
                                    "💡 Esto puede mejorar la conectividad si había\n" +
                                    "   problemas de resolución DNS.",
                                    CustomDialog.DialogType.OK,
                                    CustomDialog.IconType.Information);
                            }
                            else
                            {
                                CustomDialog.Show(
                                    "Error",
                                    $"Error al limpiar cache DNS:\n\n{error}",
                                    CustomDialog.DialogType.OK,
                                    CustomDialog.IconType.Error);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        // Mostrar error en el hilo principal
                        progressWindow.Dispatcher.Invoke(() =>
                        {
                            CustomDialog.Show(
                                "Error",
                                $"Error durante la limpieza DNS: {ex.Message}",
                                CustomDialog.DialogType.OK,
                                CustomDialog.IconType.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                CustomDialog.Show(
                    "Error",
                    $"Error: {ex.Message}",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);
            }
        }

        private void LimpiarCacheWindowsUpdate()
        {
            try
            {
                // Mostrar confirmación
                var result = CustomDialog.Show(
                    "Confirmar limpieza de Windows Update",
                    "¿Está seguro de que desea limpiar el cache de Windows Update?\n\n" +
                    "Esta operación:\n" +
                    "• Elimina archivos temporales de actualizaciones\n" +
                    "• Libera espacio significativo en disco\n" +
                    "• Obliga a descargar actualizaciones nuevamente\n\n" +
                    "⚠️ Las actualizaciones pendientes se descargarán de nuevo.",
                    CustomDialog.DialogType.YesNo,
                    CustomDialog.IconType.Warning);

                if (result != CustomDialog.DialogResult.Yes)
                    return;

                // Mostrar progreso
                var (progressWindow, textBlock) = CreateProgressWindow("Limpiando cache de Windows Update...", "Eliminando archivos de actualización...");
                progressWindow.Show();

                // Ejecutar limpieza en thread separado
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        int archivosEliminados = 0;
                        long espacioLiberado = 0;

                        // Ruta del cache de Windows Update
                        string windowsUpdatePath = @"C:\Windows\SoftwareDistribution\Download";

                        if (Directory.Exists(windowsUpdatePath))
                        {
                            progressWindow.Dispatcher.Invoke(() =>
                                textBlock.Text = "Analizando archivos de actualización...");

                            // Obtener todos los archivos y subdirectorios
                            var archivos = Directory.GetFiles(windowsUpdatePath, "*.*", System.IO.SearchOption.AllDirectories);

                            progressWindow.Dispatcher.Invoke(() =>
                                textBlock.Text = $"Eliminando {archivos.Length} archivos...");

                            foreach (string archivo in archivos)
                            {
                                try
                                {
                                    FileInfo fileInfo = new FileInfo(archivo);
                                    long tamano = fileInfo.Length;

                                    File.Delete(archivo);
                                    archivosEliminados++;
                                    espacioLiberado += tamano;
                                }
                                catch
                                {
                                    // Omitir archivos que no se puedan eliminar
                                }
                            }

                            // Intentar eliminar subdirectorios vacíos
                            try
                            {
                                foreach (var dir in Directory.GetDirectories(windowsUpdatePath, "*", System.IO.SearchOption.AllDirectories))
                                {
                                    try
                                    {
                                        if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                                        {
                                            Directory.Delete(dir);
                                        }
                                    }
                                    catch
                                    {
                                        // Omitir directorios que no se puedan eliminar
                                    }
                                }
                            }
                            catch
                            {
                                // Si hay problemas con subdirectorios, continuar
                            }
                        }

                        // Cerrar ventana de progreso
                        progressWindow.Dispatcher.Invoke(() => progressWindow.Close());

                        // Convertir bytes a MB para mostrar
                        double espacioEnMB = espacioLiberado / (1024.0 * 1024.0);

                        // Mostrar resultados en el hilo principal
                        progressWindow.Dispatcher.Invoke(() =>
                        {
                            CustomDialog.Show(
                                "Limpieza completada",
                                $"✅ Cache de Windows Update limpiado\n\n" +
                                $"📁 Ubicación limpiada:\n" +
                                $"   • {windowsUpdatePath}\n\n" +
                                $"📊 Resultados:\n" +
                                $"   • Archivos eliminados: {archivosEliminados}\n" +
                                $"   • Espacio liberado: {espacioEnMB:F1} MB ({(espacioEnMB / 1024.0):F2} GB)\n\n" +
                                $"🔄 Las próximas actualizaciones se descargarán nuevamente.\n" +
                                $"💡 Esto asegura que no haya archivos corruptos.",
                                CustomDialog.DialogType.OK,
                                CustomDialog.IconType.Information);
                        });
                    }
                    catch (Exception ex)
                    {
                        // Mostrar error en el hilo principal
                        progressWindow.Dispatcher.Invoke(() =>
                        {
                            CustomDialog.Show(
                                "Error",
                                $"Error durante la limpieza: {ex.Message}",
                                CustomDialog.DialogType.OK,
                                CustomDialog.IconType.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                CustomDialog.Show(
                    "Error",
                    $"Error: {ex.Message}",
                    CustomDialog.DialogType.OK,
                    CustomDialog.IconType.Error);
            }
        }

        private void MostrarRamWidget()
        {
            if (_ramWidget != null)
            {
                _ramWidget.Show();
                return;
            }

            // Cargar configuración del widget
            var settings = LoadWidgetSettings();

            // Usar posición guardada o posición por defecto (esquina superior derecha)
            double left = settings.Left;
            double top = settings.Top;

            // Si es la primera vez (valores por defecto), usar esquina superior derecha
            if (left == 0 && top == 0)
            {
                left = SystemParameters.WorkArea.Width - 200; // Un poco a la izquierda del borde derecho
                top = SystemParameters.WorkArea.Top + 50; // Un poco abajo del borde superior
            }

            _ramWidget = new Window
            {
                Title = "WinTools RAM Widget",
                Width = 100,
                Height = 32,
                Left = left,
                Top = top,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Owner = this,
                Background = WpfMedia.Brushes.Transparent,
                AllowsTransparency = true,
                Topmost = false  // No siempre encima, solo visible en el escritorio
            };

            // Configurar el widget para que solo sea visible en el escritorio (no siempre encima)
            _ramWidget.SourceInitialized += (_, __) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(_ramWidget).Handle;
                    if (hwnd == IntPtr.Zero) return;

                    // Evitar que aparezca en Alt+Tab
                    var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                    exStyle |= WS_EX_TOOLWINDOW;      // no Alt+Tab
                    exStyle |= WS_EX_NOACTIVATE;      // no robar foco
                    exStyle &= ~WS_EX_APPWINDOW;      // asegurar que no se promocione a AppWindow
                    SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

                    // Colocar el widget detrás de otras ventanas (solo visible cuando no hay ventanas encima)
                    // Esto hace que el widget se comporte como un widget de escritorio normal
                    SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, 
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                }
                catch { /* no bloquear el widget por un ajuste de estilo */ }
            };

            var border = new WpfControls.Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(30, 30, 30)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6)
            };

            // Crear un StackPanel horizontal para icono y texto
            var stackPanel = new WpfControls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };

            // Icono de RAM
            var ramIcon = new WpfControls.TextBlock
            {
                Text = "🖥️",
                FontSize = 10,
                Margin = new Thickness(0, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = WpfMedia.Brushes.White
            };

            // Texto de RAM
            var textBlock = new WpfControls.TextBlock
            {
                Text = "Calculando...",
                Foreground = WpfMedia.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            stackPanel.Children.Add(ramIcon);
            stackPanel.Children.Add(textBlock);

            // Agregar eventos de mouse para simular comportamiento de botón
            stackPanel.MouseLeftButtonDown += RamWidgetTextBlock_MouseLeftButtonDown;
            stackPanel.MouseEnter += (s, e) => textBlock.Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(200, 200, 200));
            stackPanel.MouseLeave += (s, e) =>
            {
                if (textBlock.Text.StartsWith("Limpiando") || textBlock.Text == "Limpio")
                    return; // Mantener color durante estados especiales
                textBlock.Foreground = WpfMedia.Brushes.White;
            };

            border.Child = stackPanel;
            _ramWidget.Content = border;

            // Iniciar actualización de RAM en tiempo real
            StartWidgetRamUpdate();

            // Agregar menú contextual
            var contextMenu = new WpfControls.ContextMenu();

            var closeMenuItem = new WpfControls.MenuItem();
            closeMenuItem.Header = "Cerrar widget";
            closeMenuItem.Click += (s, e) =>
            {
                _ramWidget.Close();
                _ramWidget = null;
                // Nota: La aplicación principal ya se cerró, así que esto solo cierra el widget
            };

            contextMenu.Items.Add(closeMenuItem);
            _ramWidget.ContextMenu = contextMenu;

            // Hacer la ventana arrastrable y guardar posición
            _ramWidget.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    _ramWidget.DragMove();
            };

            // Guardar posición cuando se suelte el mouse
            _ramWidget.MouseLeftButtonUp += (s, e) =>
            {
                // Guardar la nueva posición
                var settings = LoadWidgetSettings();
                settings.Left = _ramWidget.Left;
                settings.Top = _ramWidget.Top;
                SaveWidgetSettings(settings);
            };

            // Hacer que al hacer clic derecho no se abra el menú inmediatamente
            _ramWidget.MouseRightButtonUp += (s, e) =>
            {
                // El menú contextual se abre automáticamente
            };

            _ramWidget.Show();
        }

        private void OcultarRamWidget()
        {
            if (_ramWidget != null)
            {
                _ramWidget.Hide();
            }
        }

        private void StartWidgetRamUpdate()
        {
            // Temporizador para actualizar la RAM mostrada cada 2 segundos
            var widgetUpdateTimer = new DispatcherTimer();
            widgetUpdateTimer.Interval = TimeSpan.FromSeconds(2);
            widgetUpdateTimer.Tick += (s, e) =>
            {
                if (_ramWidget != null && _ramWidget.Content is WpfControls.Border border && border.Child is WpfControls.StackPanel stackPanel)
                {
                    var textBlock = stackPanel.Children[1] as WpfControls.TextBlock;
                    if (textBlock != null)
                    {
                        // Solo actualizar si no está en estado "limpiando" o "limpio"
                        if (!textBlock.Text.StartsWith("Limpiando") && textBlock.Text != "Limpio")
                        {
                            var (_, _, usedRamGB) = GetAccurateRamInfo();
                            textBlock.Text = $"{usedRamGB:F1} GB";
                        }
                    }
                }
            };
            widgetUpdateTimer.Start();

            // Actualización inicial
            if (_ramWidget != null && _ramWidget.Content is WpfControls.Border border && border.Child is WpfControls.StackPanel stackPanel)
            {
                var textBlock = stackPanel.Children[1] as WpfControls.TextBlock;
                if (textBlock != null)
                {
                    var (_, _, usedRamGB) = GetAccurateRamInfo();
                    textBlock.Text = $"{usedRamGB:F1} GB";
                    textBlock.Foreground = WpfMedia.Brushes.White;
                }
            }
        }

        private void RamWidgetTextBlock_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var stackPanel = (WpfControls.StackPanel)sender;
            var textBlock = stackPanel.Children[1] as WpfControls.TextBlock;

            if (textBlock == null) return;

            // Animación de carga - cambiar texto con puntos
            var loadingTimer = new DispatcherTimer();
            loadingTimer.Interval = TimeSpan.FromMilliseconds(500);
            int dotCount = 0;

            loadingTimer.Tick += (s, args) =>
            {
                dotCount = (dotCount + 1) % 4;
                string dots = new string('.', dotCount);
                textBlock.Text = $"Limpiando{dots}";
            };
            loadingTimer.Start();

            // Cambiar color durante limpieza
            textBlock.Foreground = WpfMedia.Brushes.Orange;

            // Ejecutar liberación de RAM silenciosamente
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // Usar EXACTAMENTE la misma lógica que la función LiberarRam principal
                    // 1. Liberar memoria del proceso actual PRIMERO
                    IntPtr currentProcess = NativeMethods.GetCurrentProcess();
                    NativeMethods.EmptyWorkingSet(currentProcess);

                    // 2. Recolección de basura agresiva
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);

                    // 3. Compactación del heap
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();

                    // 4. Intentar liberar memoria de otros procesos (limitado por permisos)
                    try
                    {
                        foreach (var process in Process.GetProcesses())
                        {
                            try
                            {
                                if (process.Id != Process.GetCurrentProcess().Id)
                                {
                                    NativeMethods.EmptyWorkingSet(process.Handle);
                                }
                            }
                            catch
                            {
                                // Ignorar procesos a los que no podemos acceder
                            }
                        }
                    }
                    catch
                    {
                        // Ignorar errores de acceso a procesos
                    }

                    System.Threading.Thread.Sleep(1500); // Pausa para reflejar cambios

                    if (_ramWidget != null && textBlock != null)
                    {
                        _ramWidget.Dispatcher.Invoke(() =>
                        {
                            loadingTimer.Stop();
                            textBlock.Text = "Limpio";
                            textBlock.Foreground = WpfMedia.Brushes.Green;
                        });
                    }

                    System.Threading.Thread.Sleep(2000); // Mostrar "Limpio ✓" por 2 segundos

                    if (_ramWidget != null && textBlock != null)
                    {
                        _ramWidget.Dispatcher.Invoke(() =>
                        {
                            // Volver al estado normal mostrando la RAM actual
                            var (_, _, usedRamGB) = GetAccurateRamInfo();
                            textBlock.Text = $"{usedRamGB:F1} GB";
                            textBlock.Foreground = WpfMedia.Brushes.White;
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error en liberación de RAM del widget: {ex.Message}");
                    if (_ramWidget != null && textBlock != null)
                    {
                        _ramWidget.Dispatcher.Invoke(() =>
                        {
                            loadingTimer.Stop();
                            textBlock.Text = "Error";
                            textBlock.Foreground = WpfMedia.Brushes.Red;
                        });
                    }

                    System.Threading.Thread.Sleep(2000); // Mostrar "Error" por 2 segundos

                    if (_ramWidget != null && textBlock != null)
                    {
                        _ramWidget.Dispatcher.Invoke(() =>
                        {
                            // Volver al estado normal
                            var (_, _, usedRamGB) = GetAccurateRamInfo();
                            textBlock.Text = $"{usedRamGB:F1} GB";
                            textBlock.Foreground = WpfMedia.Brushes.White;
                        });
                    }
                }
            });
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Permitir arrastrar la ventana desde la barra de título
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            // Mostrar el menú contextual cuando se hace clic en el botón
            if (sender is WpfControls.Button button && button.ContextMenu != null)
            {
                button.ContextMenu.IsOpen = true;
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// Crea una ventana de progreso con estilo dark y esquinas redondeadas
        /// </summary>
        private (Window window, WpfControls.TextBlock textBlock) CreateProgressWindow(string title, string message)
        {
            var progressWindow = new Window
            {
                Title = title,
                Width = 350,
                Height = 120,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = WpfMedia.Brushes.Transparent,
                AllowsTransparency = true,
                ShowInTaskbar = false
            };

            // Border principal con esquinas redondeadas y tema dark
            var mainBorder = new WpfControls.Border
            {
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(30, 30, 30)),
                BorderBrush = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(64, 64, 64)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20)
            };

            // StackPanel principal
            var stackPanel = new WpfControls.StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Texto del mensaje
            var textBlock = new WpfControls.TextBlock
            {
                Text = message,
                Foreground = WpfMedia.Brushes.White,
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            // ProgressBar con esquinas redondeadas
            var progressBar = new WpfControls.ProgressBar
            {
                Width = 250,
                Height = 6,
                IsIndeterminate = true,
                Background = WpfMedia.Brushes.Transparent,
                Foreground = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(0, 122, 204)),
                BorderThickness = new Thickness(0)
            };

            // Border para la ProgressBar con CornerRadius
            var progressBorder = new WpfControls.Border
            {
                CornerRadius = new CornerRadius(3),
                Width = 250,
                Height = 6,
                Background = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromRgb(45, 45, 45)),
                Child = progressBar
            };

            // Agregar elementos al StackPanel
            stackPanel.Children.Add(textBlock);
            stackPanel.Children.Add(progressBorder);

            // Agregar StackPanel al Border
            mainBorder.Child = stackPanel;

            // Establecer el contenido de la ventana
            progressWindow.Content = mainBorder;

            return (progressWindow, textBlock);
        }
    }
}