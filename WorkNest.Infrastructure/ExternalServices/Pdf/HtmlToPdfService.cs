using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using WorkNest.Application.Interfaces;

namespace WorkNest.Infrastructure.ExternalServices.Pdf
{
    public class HtmlToPdfService : IHtmlToPdfService, IAsyncDisposable
    {
        private static readonly SemaphoreSlim _browserLock = new SemaphoreSlim(1, 1);
        private static IBrowser? _browserInstance;
        private static bool _isBrowserDownloaded = false;

        private async Task<IBrowser> GetOrLaunchBrowserAsync()
        {
            if (_browserInstance != null && !_browserInstance.IsClosed && _browserInstance.IsConnected)
            {
                return _browserInstance;
            }

            await _browserLock.WaitAsync();
            try
            {
                if (_browserInstance != null && !_browserInstance.IsClosed && _browserInstance.IsConnected)
                {
                    return _browserInstance;
                }

                if (!_isBrowserDownloaded)
                {
                    var browserFetcher = new BrowserFetcher();
                    await browserFetcher.DownloadAsync();
                    _isBrowserDownloaded = true;
                }

                var launchOptions = new LaunchOptions
                {
                    Headless = true,
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu",
                        "--disable-extensions",
                        "--no-first-run",
                        "--no-zygote"
                    }
                };

                _browserInstance = await Puppeteer.LaunchAsync(launchOptions);
                return _browserInstance;
            }
            finally
            {
                _browserLock.Release();
            }
        }

        public async Task<byte[]> ConvertHtmlToPdfAsync(string htmlContent)
        {
            var browser = await GetOrLaunchBrowserAsync();
            await using var page = await browser.NewPageAsync();

            string fullHtml = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        @page {{
            size: A4;
            margin: 25mm 20mm 25mm 20mm;
        }}
        body {{
            font-family: Arial, sans-serif;
            font-size: 10pt;
            line-height: 1.4;
            color: #1a1a1a;
            margin: 0;
            padding: 0;
        }}
        h1, h2, h3, h4 {{
            color: #0f172a;
            margin-top: 18px;
            margin-bottom: 8px;
        }}
        h2 {{
            font-size: 11pt;
            border-bottom: 1px solid #e2e8f0;
            padding-bottom: 4px;
        }}
        p {{
            margin-top: 0;
            margin-bottom: 8px;
            text-align: justify;
        }}
        ul, ol {{
            margin-top: 4px;
            margin-bottom: 8px;
            padding-left: 20px;
        }}
        li {{
            margin-bottom: 4px;
        }}
        table {{
            width: 100%;
            border-collapse: collapse;
            margin: 12px 0;
        }}
        th, td {{
            border: 1px solid #cbd5e1;
            padding: 6px 10px;
            font-size: 9.5pt;
        }}
        th {{
            background-color: #f1f5f9;
            font-weight: bold;
        }}
        .legal-document {{
            width: 100%;
        }}
    </style>
</head>
<body>
    {htmlContent}
</body>
</html>";

            await page.SetContentAsync(fullHtml, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
            });

            var pdfOptions = new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                MarginOptions = new MarginOptions
                {
                    Top = "20mm",
                    Bottom = "20mm",
                    Left = "15mm",
                    Right = "15mm"
                }
            };

            return await page.PdfDataAsync(pdfOptions);
        }

        public async ValueTask DisposeAsync()
        {
            if (_browserInstance != null && !_browserInstance.IsClosed)
            {
                try
                {
                    await _browserInstance.CloseAsync();
                    _browserInstance.Dispose();
                }
                catch { }
            }
        }
    }
}

