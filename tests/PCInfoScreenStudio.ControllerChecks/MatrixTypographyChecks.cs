using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.Services.Matrix;
using PCInfoScreenStudio.ViewModels;

internal static class MatrixTypographyChecks
{
    public static void Check(Action<Func<bool>> pump)
    {
        MatrixFontChecks.Check();
        var time = new DateTime(2026, 10, 27, 12, 34, 0, DateTimeKind.Utc);
        var settings = new MatrixSettings { Layout = 1 };
        settings.Tiles[0].Text = "H   H"; settings.Tiles[0].Scroll = false;
        byte[] frame = Frame(settings, time);
        Require(Enumerable.Range(0, 10).All(y => RowLit(frame, y)), "Text uses the complete ten-row height.");
        var columns = Enumerable.Range(0, 90).Where(x => Enumerable.Range(0, 10).Any(y => frame[(y * 90 + x) * 3] != 0)).ToArray();
        var gaps = columns.Zip(columns.Skip(1)).Where(pair => pair.Second - pair.First > 1).Select(pair => pair.Second - pair.First - 1).ToArray();
        Require(gaps.SequenceEqual(new[] { 3 }), "Even repeated spaces create exactly three empty columns between words.");
        settings.Layout = 9;
        foreach (var tile in settings.Tiles) { tile.Content = MatrixContent.Text; tile.Text = "HH"; tile.Scroll = false; }
        frame = Frame(settings, time);
        Require(Enumerable.Range(0, 10).All(y => RowLit(frame, y) == (y is not (4 or 5))), "Default two-row content fills four rows each and leaves exactly two rows dark.");
        settings.RowHeight = 5; frame = Frame(settings, time);
        var narrow = frame;
        settings.LegacyFiveDotFont = MatrixFiveDotFont.Pixel5x5Thin; frame = Frame(settings, time);
        Require(!frame.SequenceEqual(narrow), "Font choice changes actual RGB pixels.");
        Require(settings.ValidatedCopy().Tiles[0].FiveDotFont == MatrixFiveDotFont.Pixel5x5Thin, "Font survives validated snapshots.");
        settings.LegacyFiveDotFont = MatrixFiveDotFont.TomThumb;
        Require(Enumerable.Range(0, 10).All(y => RowLit(frame, y)), "Five-row content fills both rows without a gap.");
        settings.RowHeight = 4;
        foreach (var tile in settings.Tiles) { tile.Content = MatrixContent.PixelArt; Array.Fill(tile.Pixels, (byte)255); }
        frame = Frame(settings, time);
        Require(!RowLit(frame, 4) && !RowLit(frame, 5) && settings.Tiles[0].Pixels[30 * 4 * 3] == 255, "Artwork respects the gap while hidden original pixels are preserved.");
        settings.Layout = 1; settings.Tiles[0].Content = MatrixContent.Clock;
        Require(!Frame(settings, time).SequenceEqual(Frame(settings, time.AddMinutes(1))), "The UTC clock uses the shared pixel renderer and changes with time.");
        settings.Tiles[0].Content = MatrixContent.Text; settings.Tiles[0].Text = new string('H', 50); settings.Tiles[0].Scroll = true;
        Require(!Frame(settings, time).SequenceEqual(MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(settings, _ => 42, time, 3))), "Long scrolling text moves through the bounded viewport.");

        settings = new MatrixSettings { Layout = 1 };
        settings.Tiles[0].Text = "HELLO"; settings.Tiles[0].Scroll = false;
        var fake = new Matrix900ProtocolChecks.Connection();
        using (var vm = new MatrixWorkspaceViewModel(settings, _ => { }, new Matrix900Device(() => fake)))
        {
            vm.Initialize(); Run(vm.ToggleAsync(), pump); Run(vm.ApplyAsync(), pump);
            Require(LastFrame(fake).SequenceEqual(vm.PreviewPixels()), "Actual Apply sends exactly the pixels shown by the ten-row preview.");
            Require(fake.Writes.All(r => r[1] != 2) && fake.Writes.Any(r => r[1] == 3 && r[7] == 4 && r[8] == 1), "The typography reaches the display as pixel content, rather than falling back to firmware fonts.");
            int count = PageCount(fake); vm.Text = "DRAFT"; Run(vm.TickAsync(), pump);
            Require(PageCount(fake) == count, "An unapplied draft edit does not replace the active output.");
        }
        foreach (int rowHeights in new[] { 4, 5, 45, 54 })
        {
            var mixed = new MatrixSettings { Layout = 9, RowHeight = rowHeights, LegacyFourDotFont = MatrixFourDotFont.Able4, LegacyFiveDotFont = MatrixFiveDotFont.Petty5H };
            foreach (var item in mixed.Tiles) { item.Content = MatrixContent.Text; item.Text = "H"; item.Scroll = false; }
            for (int i = 0; i < 6; i++)
            {
                mixed.Tiles[i].FourDotFont = (MatrixFourDotFont)(i % 4);
                mixed.Tiles[i].FiveDotFont = (MatrixFiveDotFont)(i % 6);
                mixed.Tiles[i].TenDotFont = (MatrixTenDotFont)(i % 4);
                mixed.Tiles[i].Alignment = (MatrixAlignment)(i % 3);
            }
            var mixedFake = new Matrix900ProtocolChecks.Connection();
            using var mixedVm = new MatrixWorkspaceViewModel(mixed, _ => { }, new Matrix900Device(() => mixedFake));
            mixedVm.Initialize(); Run(mixedVm.ToggleAsync(), pump); Run(mixedVm.ApplyAsync(), pump);
            var uploaded = LastFrame(mixedFake);
            Require(uploaded.Length == 2700, "Free layers are composited into one native full-panel image before upload.");
            Require(uploaded.SequenceEqual(mixedVm.PreviewPixels()), "Every migrated mixed-height Apply uploads exactly the composited pixels shown in the preview.");
        }
        settings.Tiles[0].Content = MatrixContent.Sensor; settings.Tiles[0].Source = "GPU.Usage";
        settings.Tiles[0].Alarm = true; settings.Tiles[0].AlarmThreshold = 70;
        fake = new Matrix900ProtocolChecks.Connection();
        using (var vm = new MatrixWorkspaceViewModel(settings, _ => { }, new Matrix900Device(() => fake)))
        {
            vm.Initialize(); vm.UpdateMetrics(new Dictionary<string, MetricValue> { ["GPU.Usage"] = new(42) });
            Run(vm.ToggleAsync(), pump); Run(vm.ApplyAsync(), pump); int count = PageCount(fake);
            vm.UpdateMetrics(new Dictionary<string, MetricValue> { ["GPU.Usage"] = new(88) }); Run(vm.TickAsync(), pump);
            Require(PageCount(fake) == count + 49 && LastFrame(fake).SequenceEqual(vm.PreviewPixels()), "A changed sensor updates its acknowledged pixels and matches the preview, including alarm colour.");
            count = PageCount(fake); Run(vm.TickAsync(), pump);
            Require(PageCount(fake) == count, "Unchanged sensor content does not resend image pages.");
            vm.UpdateMetrics(new Dictionary<string, MetricValue>()); Run(vm.TickAsync(), pump);
            Require(fake.Writes.Last()[1] == 4 && fake.Writes.Last()[6] == 0, "Missing data blanks the output instead of drawing a zero.");
            vm.UpdateMetrics(new Dictionary<string, MetricValue> { ["GPU.Usage"] = new(88) }); Run(vm.TickAsync(), pump);
            Require(fake.Writes.Last(r => r[1] == 4)[6] > 0 && vm.Status.Contains("resumed"), "Fresh data resumes the applied pixel output.");
            fake.Acknowledge = false;
            vm.UpdateMetrics(new Dictionary<string, MetricValue> { ["GPU.Usage"] = new(30) }); Run(vm.TickAsync(), pump);
            Require(!vm.IsConnected && fake.Disposed && vm.Status.StartsWith("Matrix disconnected"), "A failed live pixel acknowledgement stops output and releases HID.");
        }
        var fullSensor = new MatrixSettings { Widgets = [new() { Content = MatrixContent.Sensor, Source = "Cooling.FanRPM", Prefix = "Fan:", Height = 5, Scroll = false }] };
        var sensorFake = new Matrix900ProtocolChecks.Connection();
        using (var vm = new MatrixWorkspaceViewModel(fullSensor, _ => { }, new Matrix900Device(() => sensorFake)))
        {
            vm.Initialize(); vm.UpdateMetrics(new Dictionary<string, MetricValue> { ["Cooling.FanRPM"] = new(1500, Unit: " RPM") });
            Require(vm.PreviewReading == "Fan: 1500 RPM", "Full-source readings retain their actual unit and prefix instead of using temperature formatting.");
            Run(vm.ToggleAsync(), pump); Run(vm.ApplyAsync(), pump);
            var expected = new MatrixSettings { Widgets = [new() { Text = "Fan: 1500 RPM", Height = 5, Scroll = false }] };
            Require(LastFrame(sensorFake).SequenceEqual(MatrixLayerRenderer.Compose(expected, _ => null, time)), "A labelled source outside the four firmware sensor IDs reaches the acknowledged HID frame exactly.");
            vm.Prefix = ""; vm.UpdateMetrics(new Dictionary<string, MetricValue> { ["Cooling.FanRPM"] = new(1, Unit: " RPM") }); vm.X = 89; Run(vm.ApplyAsync(), pump);
            foreach (int rpm in new[] { 15000, 1 })
            {
                vm.UpdateMetrics(new Dictionary<string, MetricValue> { ["Cooling.FanRPM"] = new(rpm, Unit: " RPM") }); Run(vm.TickAsync(), pump);
                Require(LastFrame(sensorFake).SequenceEqual(vm.PreviewPixels()), "Live growing and shrinking readings at the right edge keep preview, selection geometry and output aligned.");
            }
            vm.DataSources.Add("Hardware.GPUName"); vm.Source = "Hardware.GPUName"; vm.Prefix = "GPU:";
            vm.UpdateMetrics(new Dictionary<string, MetricValue> { ["Hardware.GPUName"] = new(Text: "RX 7800 XT") });
            Run(vm.ApplyAsync(), pump); Require(vm.PreviewReading == "GPU: RX 7800 XT" && LastFrame(sensorFake).SequenceEqual(vm.PreviewPixels()), "Text-valued catalog sources also support Apply and exact preview-to-output pixels.");
        }
        var barFake=new Matrix900ProtocolChecks.Connection();
        using(var vm=new MatrixWorkspaceViewModel(new() { Widgets=[new() { Content=MatrixContent.ProgressBar,Source="GPU.Usage",Width=30,Height=5,AutoWidth=false,BarStyle=MatrixBarStyle.Potion,Colour="#43D536" }] },_=>{},new Matrix900Device(()=>barFake)))
        {
            vm.Initialize();vm.UpdateMetrics(new Dictionary<string,MetricValue>{["GPU.Usage"]=new(25,Unit:"%")});Run(vm.ToggleAsync(),pump);Run(vm.ApplyAsync(),pump);
            Require(LastFrame(barFake).SequenceEqual(vm.PreviewPixels()),"A sensor-driven bar uploads the exact preview pixels through the existing ACK-gated transport.");
            var before=LastFrame(barFake);vm.UpdateMetrics(new Dictionary<string,MetricValue>{["GPU.Usage"]=new(85,Unit:"%")});Run(vm.TickAsync(),pump);
            Require(!before.SequenceEqual(LastFrame(barFake)),"Live sensor values change the applied progress fill.");
        }
        var rocket=MatrixArtBank.Get("rocket",10);var flightFake=new Matrix900ProtocolChecks.Connection();
        using(var vm=new MatrixWorkspaceViewModel(new() { Widgets=[new() { Content=MatrixContent.FlyingArt,ArtKey="rocket",ArtWidth=rocket.Width,Pixels=rocket.Pixels,Width=60,Height=10,AutoWidth=false,FlightSpeed=5 }] },_=>{},new Matrix900Device(()=>flightFake)))
        {
            vm.Initialize();Run(vm.ToggleAsync(),pump);Run(vm.ApplyAsync(),pump);var first=LastFrame(flightFake);int pages=PageCount(flightFake);
            pump(()=>PageCount(flightFake)>=pages+49);
            Require(!first.SequenceEqual(LastFrame(flightFake)),"Applied rocket flight updates acknowledged frames without enabling the editor preview timer.");
        }
        Console.WriteLine("PASS: native Matrix typography, real sensor units/prefixes, sensor-driven progress bars, applied rocket animation, output identity, changed-only updates and failed-update cleanup.");
    }
    private static byte[] Frame(MatrixSettings settings, DateTime time) => MatrixPixelRenderer.Compose(MatrixPixelRenderer.Render(settings, _ => 42, time));
    private static bool RowLit(byte[] rgb, int row) => rgb.Skip(row * 270).Take(270).Any(b => b != 0);
    private static int PageCount(Matrix900ProtocolChecks.Connection c) => c.Writes.Count(r => r[1] == 5 && r[2] != 255);
    private static byte[] LastFrame(Matrix900ProtocolChecks.Connection c) => c.Writes.Where(r => r[1] == 5 && r[2] == 0).TakeLast(49).SelectMany(r => r.Skip(8).Take(r[5] - 2)).ToArray();
    private static void Run(Task task, Action<Func<bool>> pump) { pump(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
