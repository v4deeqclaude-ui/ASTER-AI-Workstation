using System.Windows;

namespace ASTER.Workstation.App;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        AsterConnectServiceManager.Stop();
        base.OnExit(e);
    }
}