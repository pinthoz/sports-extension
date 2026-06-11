using System.Windows;

namespace SportsOverlayApp.Utils
{
    // Goal/update sounds were removed on purpose: score changes are signalled
    // visually (the chip holds a green highlight), never with audio.
    public static class Notifications
    {
        public static void ShowToast(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
