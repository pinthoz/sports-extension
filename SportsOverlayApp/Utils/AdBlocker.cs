using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace SportsOverlayApp.Utils
{
    /// <summary>
    /// Keeps ads out of the embedded FlashScore browsers: requests to ad and
    /// betting-promo servers are refused, and the page's ad slots are hidden
    /// so no empty boxes are left behind. Also saves the hidden browsers the
    /// work of loading banners.
    /// </summary>
    public static class AdBlocker
    {
        // Servers seen delivering FlashScore's banners, plus the usual ad networks.
        private static readonly string[] AdHosts =
        {
            "bannerflow.net", "bannerflow.com",
            "googlesyndication.com", "doubleclick.net", "googleadservices.com",
            "adservice.google.com", "googletagservices.com", "2mdn.net",
            "entainpartners.com", "etoro.com", "etorostatic.com",
            "amazon-adsystem.com", "adnxs.com", "criteo.com", "criteo.net",
            "taboola.com", "outbrain.com", "pubmatic.com", "rubiconproject.com",
            "smartadserver.com", "teads.tv", "openx.net"
        };

        // FlashScore's ad slots ("zones"): top banner, right column, the box in
        // the middle of the list, the match page's side banners and the footer ad.
        private const string HideCss =
            "iframe.zone__content, div[id^='zoneContainer'], .container__bannerZone, #rc-top," +
            " .scrolling-banner-wrap, .boxOverContentRevive, #box-over-content-revive," +
            " .lmc__bannerCont, .footer__advert, .selfPromo__boxItem.page-advertise" +
            " { display: none !important; }";

        private static readonly string InjectCss =
            "(() => { if (!/flashscore/.test(location.hostname)) return;" +
            " const add = () => { if (document.getElementById('so-noads')) return;" +
            " const s = document.createElement('style'); s.id = 'so-noads';" +
            $" s.textContent = \"{HideCss}\";" +
            " (document.head || document.documentElement).appendChild(s); };" +
            " if (document.documentElement) add(); else document.addEventListener('DOMContentLoaded', add); })();";

        public static async Task ApplyAsync(CoreWebView2 web)
        {
            foreach (var host in AdHosts)
            {
                // Banners load inside iframes, so match requests from every frame.
                web.AddWebResourceRequestedFilter($"*://{host}/*", CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
                web.AddWebResourceRequestedFilter($"*://*.{host}/*", CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
            }
            web.WebResourceRequested += (s, e) =>
                e.Response = web.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            await web.AddScriptToExecuteOnDocumentCreatedAsync(InjectCss);
        }
    }
}
