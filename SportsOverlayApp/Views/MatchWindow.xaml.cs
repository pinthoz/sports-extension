using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace SportsOverlayApp.Views
{
    /// <summary>
    /// Small browser showing one game's FlashScore page (summary, stats,
    /// line-ups), opened by clicking a chip. Reused for every game.
    /// </summary>
    public partial class MatchWindow : Window
    {
        private bool initialized;

        public MatchWindow()
        {
            InitializeComponent();
        }

        /// <summary>Opens the game page for a scraped row id like "g_1_Wz2KLX8r".</summary>
        public async Task ShowMatchAsync(string gameId)
        {
            var mid = gameId.Substring(gameId.LastIndexOf('_') + 1);
            if (mid == "") return;

            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();

            if (!initialized)
            {
                // Its own profile: game pages need no login, and a separate
                // folder avoids clashing with the other embedded browsers.
                var dataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SportsOverlay", "WebView2-Match");
                var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
                await Browser.EnsureCoreWebView2Async(env);
                initialized = true;
            }
            // FlashScore redirects the short form to the game's canonical page.
            Browser.CoreWebView2.Navigate($"https://www.flashscore.com/match/{mid}/#/match-summary");
        }
    }
}
