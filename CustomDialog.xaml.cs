using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;

namespace WinTools
{
    public partial class CustomDialog : Window
    {
        public new enum DialogResult
        {
            None,
            OK,
            Cancel,
            Yes,
            No
        }

        public enum DialogType
        {
            OK,
            OKCancel,
            YesNo,
            YesNoCancel
        }

        public enum IconType
        {
            None,
            Warning,
            Error,
            Information,
            Question
        }

        private DialogResult _result = DialogResult.None;

        public CustomDialog(string title, string message, DialogType type = DialogType.OK, IconType icon = IconType.None, Window? owner = null)
        {
            InitializeComponent();

            // Establecer el owner si se proporciona
            if (owner != null)
            {
                Owner = owner;
            }

            // Configurar título y mensaje
            TitleTextBlock.Text = title;
            MessageTextBlock.Text = message;

            // Configurar icono
            ConfigureIcon(icon);

            // Configurar botones
            ConfigureButtons(type);

            // Manejar cierre de ventana
            this.Closing += (s, e) =>
            {
                if (_result == DialogResult.None)
                {
                    _result = (type == DialogType.OK) ? DialogResult.OK : DialogResult.Cancel;
                }
            };
        }

        private void ConfigureIcon(IconType icon)
        {
            switch (icon)
            {
                case IconType.Warning:
                    IconTextBlock.Text = "⚠️";
                    IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                    break;
                case IconType.Error:
                    IconTextBlock.Text = "❌";
                    IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                    break;
                case IconType.Information:
                    IconTextBlock.Text = "ℹ️";
                    IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0, 123, 255));
                    break;
                case IconType.Question:
                    IconTextBlock.Text = "❓";
                    IconTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0, 123, 255));
                    break;
                default:
                    IconTextBlock.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void ConfigureButtons(DialogType type)
        {
            switch (type)
            {
                case DialogType.OK:
                    OkButton.Visibility = Visibility.Visible;
                    break;
                case DialogType.OKCancel:
                    OkButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;
                case DialogType.YesNo:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    break;
                case DialogType.YesNoCancel:
                    YesButton.Visibility = Visibility.Visible;
                    NoButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    break;
            }
        }

        public new DialogResult ShowDialog()
        {
            base.ShowDialog();
            return _result;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            _result = DialogResult.Yes;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            _result = DialogResult.No;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _result = DialogResult.Cancel;
            Close();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _result = DialogResult.OK;
            Close();
        }

        // Métodos estáticos para facilitar el uso
        public static DialogResult Show(string title, string message, DialogType type = DialogType.OK, IconType icon = IconType.None)
        {
            // Encontrar la ventana principal como owner
            Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ??
                           Application.Current?.MainWindow;

            var dialog = new CustomDialog(title, message, type, icon, owner);
            return dialog.ShowDialog();
        }

        public static bool ShowQuestion(string title, string message)
        {
            // Encontrar la ventana principal como owner
            Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ??
                           Application.Current?.MainWindow;

            var dialog = new CustomDialog(title, message, DialogType.YesNo, IconType.Question, owner);
            var result = dialog.ShowDialog();
            return result == DialogResult.Yes;
        }

        public static bool ShowWarning(string title, string message)
        {
            // Encontrar la ventana principal como owner
            Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ??
                           Application.Current?.MainWindow;

            var dialog = new CustomDialog(title, message, DialogType.YesNo, IconType.Warning, owner);
            var result = dialog.ShowDialog();
            return result == DialogResult.Yes;
        }
    }
}
