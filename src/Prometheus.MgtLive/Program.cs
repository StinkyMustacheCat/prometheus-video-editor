using CefSharp;
using CefSharp.OffScreen;
using Prometheus.Core.Model;
using System.Drawing;
using System.Text.Json;
using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using System.Runtime.InteropServices;
using static Vortice.Direct3D11.D3D11;

namespace Prometheus.MgtLive;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (args.Length > 0 && args[0].StartsWith("--serve", StringComparison.Ordinal))
                Console.WriteLine("ERROR " + error.Message.Replace('\r', ' ').Replace('\n', ' '));
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--export-credits") return ExportCredits(args[1]);
        if (args.Length == 6 && args[0] == "--serve-subtitles" && int.TryParse(args[2], out var sw) && int.TryParse(args[3], out var sh) &&
            int.TryParse(args[4], out var dw) && int.TryParse(args[5], out var dh)) return Serve(args[1], sw, sh, dw, dh);
        if (args.Length == 4 && args[0] == "--serve" &&
            int.TryParse(args[2], out var serveWidth) && int.TryParse(args[3], out var serveHeight))
            return Serve(args[1], serveWidth, serveHeight);
        if (args.Length == 2 && args[0] == "--client-probe") return ClientProbe(args[1]);
        if (args.Length == 2 && args[0] == "--inspect")
        {
            var asset = MgtLiveAsset.Inspect(args[1]);
            var stream = asset.Streams[0];
            Console.WriteLine($"mgt-ok format={asset.ContainerFormat} size={stream.Width}x{stream.Height} duration={asset.RecommendedDuration!.Value.Seconds}");
            return 0;
        }
        var mgtProbe = args.Length == 2 && args[0] == "--mgt-probe";
        if (!mgtProbe && (args.Length != 1 || args[0] != "--gpu-probe"))
        {
            Console.Error.WriteLine("Uso: Prometheus.MgtLive --gpu-probe | --mgt-probe archivo.mgt.json");
            return 2;
        }

        return RunWithCef(() =>
        {
            var asset = mgtProbe ? MgtLiveAsset.Inspect(args[1]) : null;
            var width = asset?.Streams[0].Width ?? 320;
            var height = asset?.Streams[0].Height ?? 180;
            using var browser = new ChromiumWebBrowser("", automaticallyCreateBrowser: false,
                useLegacyRenderHandler: false);
            using var paint = new GpuMgtPaintHandler(browser, width, height);
            browser.RenderHandler = paint;
            browser.Size = new Size(width, height);
            using var window = WindowInfo.Create();
            window.SetAsWindowless(IntPtr.Zero);
            window.SharedTextureEnabled = true;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            browser.BrowserInitialized += (_, _) => ready.TrySetResult();
            using var browserSettings = new BrowserSettings { WindowlessFrameRate = 60 };
            browser.CreateBrowser(window, browserSettings);
            if (!ready.Task.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("CEF no abrió el navegador MGT.");
            var page = mgtProbe
                ? asset?.ContainerFormat == "mgtproj"
                    ? MgtProjectScenePage.Build(args[1], width, height, width, height)
                    : MgtScenePage.Build(File.ReadAllText(args[1]), width, height)
                : "<!doctype html><html><body style='margin:0;background:transparent'>" +
                  "<div style='width:160px;height:90px;background:#f80'></div></body></html>";
            browser.LoadHtml(page,
                "https://mgt.prometheus.invalid/probe");
            var until = DateTime.UtcNow.AddSeconds(15);
            while (paint.Generation == 0 && DateTime.UtcNow < until)
                Thread.Sleep(50);
            if (paint.Generation == 0)
            {
                Console.Error.WriteLine("CEF no entregó textura acelerada.");
                return 4;
            }
            if (mgtProbe)
            {
                var revision = paint.Generation;
                var result = browser.EvaluateScriptAsync("window.renderAt(1)").GetAwaiter().GetResult();
                if (!result.Success || Convert.ToInt32(result.Result) != 1)
                    throw new InvalidOperationException("MGT no pudo dibujar el fotograma 1: " + result.Message);
                until = DateTime.UtcNow.AddSeconds(10);
                while (paint.Generation == revision && DateTime.UtcNow < until) Thread.Sleep(50);
                if (paint.Generation == revision)
                    throw new TimeoutException("MGT no entregó el segundo fotograma por GPU.");
            }
            Console.WriteLine($"gpu-ok generation={paint.Generation} size={width}x{height} handle={paint.SharedHandle}");
            return 0;
        });
    }

    // The credits are exported by the bundled Chromium, so they match the shipped runtime.
    private static int ExportCredits(string destination) => RunWithCef(() =>
    {
        using var browser = new ChromiumWebBrowser("chrome://credits");
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (browser.IsBrowserInitialized && browser.CanExecuteJavascriptInMainFrame && !browser.IsLoading)
            {
                var html = browser.GetSourceAsync().GetAwaiter().GetResult();
                if (html.Contains("Copyright", StringComparison.OrdinalIgnoreCase) && html.Length > 50000)
                {
                    File.WriteAllText(destination, html);
                    Console.WriteLine("credits-ok bytes=" + new FileInfo(destination).Length);
                    return 0;
                }
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("Chromium no entregó sus avisos de terceros.");
    });

    private static int Serve(string path, int width, int height, int? designWidth = null, int? designHeight = null)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(width));
        var asset = MgtLiveAsset.Inspect(path);
        var page = asset.ContainerFormat == "mgtproj"
            ? MgtProjectScenePage.Build(path, width, height,
                asset.Streams[0].Width!.Value, asset.Streams[0].Height!.Value)
            : MgtScenePage.Build(File.ReadAllText(path), width, height,
                designWidth ?? asset.Streams[0].Width, designHeight ?? asset.Streams[0].Height, liveSubtitles: designWidth != null);
        return RunWithCef(() =>
        {
            using var browser = new ChromiumWebBrowser("", automaticallyCreateBrowser: false,
                useLegacyRenderHandler: false);
            using var paint = new GpuMgtPaintHandler(browser, width, height);
            browser.RenderHandler = paint;
            browser.Size = new Size(width, height);
            using var window = WindowInfo.Create();
            window.SetAsWindowless(IntPtr.Zero);
            window.SharedTextureEnabled = true;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            browser.BrowserInitialized += (_, _) => ready.TrySetResult();
            using var browserSettings = new BrowserSettings { WindowlessFrameRate = 120 };
            browser.CreateBrowser(window, browserSettings);
            if (!ready.Task.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("CEF no abrió MGT.");
            browser.LoadHtml(page, "https://mgt.prometheus.invalid/live");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            var scriptReady = false;
            while (DateTime.UtcNow < deadline)
            {
                if (!browser.CanExecuteJavascriptInMainFrame) { Thread.Sleep(20); continue; }
                var result = browser.EvaluateScriptAsync("window.mgtSceneReady === true").GetAwaiter().GetResult();
                if (result.Success && result.Result is bool readyFlag && readyFlag) { scriptReady = true; break; }
                Thread.Sleep(20);
            }
            if (!scriptReady) throw new TimeoutException("La escena MGT no se inicializó.");
            browser.GetBrowserHost().Invalidate(PaintElementType.View);
            while (paint.Generation == 0 && DateTime.UtcNow < deadline) Thread.Sleep(10);
            if (paint.Generation == 0) throw new TimeoutException("MGT no entregó el primer fotograma.");
            Console.WriteLine($"READY {paint.SharedHandle.ToInt64()} {paint.Generation}");
            Console.Out.Flush();
            long requestSequence = 0;
            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                if (line == "QUIT") break;
                try
                {
                    using var command = JsonDocument.Parse(line);
                    var root = command.RootElement;
                    var frame = root.GetProperty("frame").GetInt32();
                    var overrides = root.TryGetProperty("overrides", out var parameters) ? parameters.GetRawText() : "{}";
                    var exact = root.TryGetProperty("exact", out var exactValue) && exactValue.ValueKind == JsonValueKind.True;
                    var previous = paint.Generation;
                    var token = ++requestSequence;
                    var update = root.TryGetProperty("subtitleData", out var subtitles) && subtitles.ValueKind == JsonValueKind.Object
                        ? $"window.mgtSetSubtitles({subtitles.GetRawText()});" : "";
                    var clock = root.TryGetProperty("clock", out var clockValue) && clockValue.ValueKind == JsonValueKind.Object ? clockValue.GetRawText() : "null";
                    var script = update + $"window.renderAt({frame},{overrides},{token},{clock})";
                    var result = browser.EvaluateScriptAsync(script).GetAwaiter().GetResult();
                    if (!result.Success) throw new InvalidOperationException(result.Message);
                    deadline = DateTime.UtcNow.AddSeconds(3);
                    var acknowledged = false;
                    while (DateTime.UtcNow < deadline)
                    {
                        var ack = browser.EvaluateScriptAsync($"window.mgtPaintToken === {token}")
                            .GetAwaiter().GetResult();
                        if (ack.Success && ack.Result is true) { acknowledged = true; break; }
                        Thread.Sleep(2);
                    }
                    if (!acknowledged) throw new TimeoutException("MGT no confirmó el cambio de escena.");
                    previous = paint.Generation;
                    browser.GetBrowserHost().Invalidate(PaintElementType.View);
                    deadline = DateTime.UtcNow.AddSeconds(3);
                    while (paint.Generation == previous && DateTime.UtcNow < deadline) Thread.Sleep(2);
                    if (paint.Generation == previous) throw new TimeoutException("CEF no entregó el fotograma solicitado.");
                    if (exact)
                    {
                        previous = paint.Generation;
                        browser.GetBrowserHost().Invalidate(PaintElementType.View);
                        deadline = DateTime.UtcNow.AddSeconds(3);
                        while (paint.Generation == previous && DateTime.UtcNow < deadline) Thread.Sleep(2);
                        if (paint.Generation == previous) throw new TimeoutException("CEF no confirmó el fotograma exacto.");
                    }
                    Console.WriteLine($"FRAME {frame} {paint.Generation}");
                }
                catch (Exception ex) { Console.WriteLine("ERROR " + ex.Message.Replace('\r', ' ').Replace('\n', ' ')); }
                Console.Out.Flush();
            }
            return 0;
        });
    }

    private static int RunWithCef(Func<int> render)
    {
        // Each worker can coexist with other scenes and exports without Chromium forwarding its launch.
        var drive = Path.GetPathRoot(AppContext.BaseDirectory)!;
        using var profile = new MgtWorkerProfile(Path.Combine(drive, "Prometheus", "MgtLiveProfiles"));
        var settings = new CefSettings
        {
            WindowlessRenderingEnabled = true,
            RootCachePath = profile.Path,
            LogFile = @"\\.\NUL",
            LogSeverity = LogSeverity.Disable,
            CommandLineArgsDisabled = true
        };
        if (!Cef.Initialize(settings)) throw new InvalidOperationException("CEF no pudo iniciarse.");
        try { return render(); }
        finally { Cef.Shutdown(); }
    }

    private static int ClientProbe(string path)
    {
        var asset = MgtLiveAsset.Inspect(path);
        var designWidth = asset.Streams[0].Width!.Value;
        var designHeight = asset.Streams[0].Height!.Value;
        var edge = 480.0;
        var scale = Math.Min(1, edge / Math.Max(designWidth, designHeight));
        var width = Math.Max(1, (int)Math.Round(designWidth * scale));
        var height = Math.Max(1, (int)Math.Round(designHeight * scale));
        using var process = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            CreateNoWindow = true, ArgumentList = { "--serve", path, width.ToString(), height.ToString() }
        }};
        process.Start();
        try
        {
            var header = process.StandardOutput.ReadLine() ?? throw new IOException("El trabajador MGT terminó.");
            var parts = header.Split(' ');
            if (parts.Length != 3 || parts[0] != "READY") throw new IOException(header);
            var handle = new IntPtr(long.Parse(parts[1]));
            D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
                out var device, out _, out var context).CheckError();
            using (device)
            using (context)
            using (var texture = device.OpenSharedResource<ID3D11Texture2D>(handle))
            using (var mutex = texture.QueryInterface<IDXGIKeyedMutex>())
            {
                mutex.AcquireSync(0, 1000);
                mutex.ReleaseSync(0);
                process.StandardInput.WriteLine("{\"frame\":1,\"overrides\":{\"color\":\"#00ff00\",\"title\":\"Prueba\"},\"exact\":true}");
                var response = process.StandardOutput.ReadLine();
                if (response?.StartsWith("FRAME 1 ", StringComparison.Ordinal) != true)
                    throw new IOException("No llegó el fotograma: " + response);
                using var readback = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm,
                    (uint)width, (uint)height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
                mutex.AcquireSync(0, 1000);
                try { context.CopyResource(readback, texture); context.Flush(); }
                finally { mutex.ReleaseSync(0); }
                var mapped = context.Map(readback, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var transparentAlpha = Marshal.ReadByte(mapped.DataPointer, 3);
                    var opaqueOffset = (int)(height / 2 * mapped.RowPitch + width / 2 * 4 + 3);
                    var visibleAlpha = Marshal.ReadByte(mapped.DataPointer, opaqueOffset);
                    if (transparentAlpha != 0 || visibleAlpha == 0)
                        throw new InvalidDataException($"Alfa MGT incorrecto: esquina={transparentAlpha}, centro={visibleAlpha}");
                    var colorOffset = (int)(height / 2 * mapped.RowPitch + width / 4 * 4);
                    Console.WriteLine($"pixel BGRA={Marshal.ReadByte(mapped.DataPointer, colorOffset)},{Marshal.ReadByte(mapped.DataPointer, colorOffset + 1)},{Marshal.ReadByte(mapped.DataPointer, colorOffset + 2)},{Marshal.ReadByte(mapped.DataPointer, colorOffset + 3)}");
                    var green = Marshal.ReadByte(mapped.DataPointer, colorOffset + 1);
                    if (green < 200)
                        throw new InvalidDataException($"El parámetro de color no actualizó la textura: verde={green}");
                    Console.WriteLine($"alpha-ok corner={transparentAlpha} center={visibleAlpha}");
                    Console.WriteLine($"parameter-ok green={green}");
                }
                finally { context.Unmap(readback, 0); }
                Console.WriteLine("cross-process-gpu-ok " + response);
                var watch = Stopwatch.StartNew();
                for (var frame = 2; frame < 32; frame++)
                {
                    process.StandardInput.WriteLine($"{{\"frame\":{frame},\"overrides\":{{\"color\":\"#00ff00\"}}}}");
                    var next = process.StandardOutput.ReadLine();
                    if (next?.StartsWith($"FRAME {frame} ", StringComparison.Ordinal) != true)
                        throw new IOException("Se perdió un fotograma MGT: " + next);
                }
                watch.Stop();
                Console.WriteLine($"live-30-frames-ms={watch.ElapsedMilliseconds}");
            }
            process.StandardInput.WriteLine("QUIT");
            process.WaitForExit(5000);
            return process.ExitCode;
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
