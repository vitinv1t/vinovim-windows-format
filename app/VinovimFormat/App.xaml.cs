using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace VinovimFormat;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Necessário para ler a saída de dism/netsh/manage-bde, que usam a página de código OEM (ex.: 850).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        base.OnStartup(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Ocorreu um erro inesperado:\n\n" + e.Exception.Message,
            "Vinovim Windows Format", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
