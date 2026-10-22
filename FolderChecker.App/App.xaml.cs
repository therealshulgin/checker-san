using System.Windows;
using FolderChecker.Core;

namespace FolderChecker.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppLanguage.Initialize();
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(L.T("Une erreur est survenue. Consultez l’état de l’opération avant de continuer.\n\n",
                "An error occurred. Check the operation status before continuing.\n\n") + args.Exception.Message,
                "Checker San", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
