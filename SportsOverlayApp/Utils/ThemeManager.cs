using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SportsOverlayApp.Models;

namespace SportsOverlayApp.Utils
{
    public static class ThemeManager
    {
        public static void ApplyLiquidGlassTheme(Window window, UserPreferences preferences)
        {
            // Apply liquid glass effect with blur and transparency
            var isDark = preferences.UseDarkTheme;
            var opacity = preferences.OverlayOpacity;

            // Set window background with glass effect
            if (isDark)
            {
                window.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 20, 20, 25));
            }
            else
            {
                window.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 245, 245, 250));
            }

            // Add blur effect for liquid glass look
            var blurEffect = new BlurEffect
            {
                Radius = 10,
                KernelType = KernelType.Gaussian
            };

            // Add drop shadow for depth
            var dropShadow = new DropShadowEffect
            {
                Color = isDark ? Colors.Black : Colors.Gray,
                Direction = 315,
                ShadowDepth = 5,
                Opacity = 0.3,
                BlurRadius = 15
            };

            // Apply effects to window content
            if (window.Content is FrameworkElement content)
            {
                content.Effect = dropShadow;
                ApplyTextTheme(content, isDark);
            }
        }

        private static void ApplyTextTheme(DependencyObject parent, bool isDark)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is TextBlock textBlock)
                {
                    textBlock.Foreground = isDark ? 
                        new SolidColorBrush(Color.FromRgb(240, 240, 245)) : 
                        new SolidColorBrush(Color.FromRgb(20, 20, 25));
                        
                    // Add subtle text shadow for better readability
                    textBlock.Effect = new DropShadowEffect
                    {
                        Color = isDark ? Colors.Black : Colors.White,
                        Direction = 315,
                        ShadowDepth = 1,
                        Opacity = 0.5,
                        BlurRadius = 2
                    };
                }

                ApplyTextTheme(child, isDark);
            }
        }

        public static Brush GetGradientBrush(bool isDark)
        {
            var gradient = new LinearGradientBrush();
            gradient.StartPoint = new Point(0, 0);
            gradient.EndPoint = new Point(1, 1);

            if (isDark)
            {
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(200, 40, 40, 50), 0.0));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(180, 20, 20, 30), 1.0));
            }
            else
            {
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(220, 255, 255, 255), 0.0));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(200, 240, 245, 255), 1.0));
            }

            return gradient;
        }
    }
}