using System.Windows;

namespace FolderChecker.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("Une erreur est survenue. Consultez l’état de l’opération avant de continuer.\n\n" + args.Exception.Message, "Checker San", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
