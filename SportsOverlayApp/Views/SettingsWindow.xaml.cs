using System.Windows;
using SportsOverlayApp.Models;
using SportsOverlayApp.Services;

namespace SportsOverlayApp.Views
{
    public partial class SettingsWindow : Window
    {
        private UserPreferences preferences = new();

        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrentPreferences();
        }

        private void LoadCurrentPreferences()
        {
            preferences = CacheService.LoadPreferences();

            SourceSelector.SelectedIndex = preferences.DataSource == DataSource.Extension ? 1 : 0;
            PositionSelector.SelectedIndex = preferences.BarPosition switch
            {
                BarPosition.Top => 1,
                BarPosition.Taskbar => 2,
                _ => 0
            };
            DarkModeToggle.IsChecked = preferences.UseDarkTheme;
            OpacitySlider.Value = preferences.OverlayOpacity * 100;
            NotificationsToggle.IsChecked = preferences.EnableNotifications;
            StartupToggle.IsChecked = preferences.StartWithWindows;
            PortInput.Text = preferences.WebSocketPort.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            preferences.DataSource = SourceSelector.SelectedIndex == 1
                ? DataSource.Extension
                : DataSource.BuiltIn;
            preferences.BarPosition = PositionSelector.SelectedIndex switch
            {
                1 => BarPosition.Top,
                2 => BarPosition.Taskbar,
                _ => BarPosition.Bottom
            };
            preferences.UseDarkTheme = DarkModeToggle.IsChecked ?? true;
            preferences.OverlayOpacity = OpacitySlider.Value / 100.0;
            preferences.EnableNotifications = NotificationsToggle.IsChecked ?? true;
            preferences.StartWithWindows = StartupToggle.IsChecked ?? false;

            if (int.TryParse(PortInput.Text, out var port) && port >= 1024 && port <= 65535)
            {
                preferences.WebSocketPort = port;
            }
            else
            {
                MessageBox.Show("Port must be a number between 1024 and 65535.", "Invalid port",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CacheService.SavePreferences(preferences);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
