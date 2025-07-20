using System.Media;
using System.Windows;

namespace SportsOverlayApp.Utils
{
    public static class Notifications
    {
        public static void PlayGoalSound()
        {
            SystemSounds.Exclamation.Play();
        }

        public static void PlayUpdateSound()
        {
            SystemSounds.Beep.Play();
        }

        public static void ShowToast(string title, string message)
        {
            // Simple toast notification using MessageBox
            // In a production app, you might want to use Windows Toast notifications
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
