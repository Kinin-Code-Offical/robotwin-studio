using System.Windows;

namespace RobotWin.Studio.Pages
{
    /// <summary>[components] Unity's ? on a component header: the guide at that component's section (Shell/CardHelp.cs).</summary>
    public partial class WorldPage
    {
        private void OpenCardHelp(string title)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ShowHelp(Shell.CardHelp.Section(title, Shell.Lang.Current == "en", Shell.Lang.T));
        }
    }
}
