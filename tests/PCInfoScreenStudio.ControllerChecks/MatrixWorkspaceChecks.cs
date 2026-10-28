using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PCInfoScreenStudio.Controls;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Rendering;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;

internal static class MatrixWorkspaceChecks
{
    public static void Check(string root, Action<Func<bool>> pump, string? captures)
    {
        MatrixWidgetChecks.Check(); MatrixGraphicsChecks.Check(); MatrixTypographyChecks.Check(pump);
        var path = Path.Combine(root, "matrix-workspace.json");
        var settings = new SettingsController(new AppSettingsService(path)) { FirstRunCompleted = true, AutoStartDisplay = false, CloseToTray = false, StartWithWindows = false };
        var vm = new MainViewModel(settings, new SessionStateService(Path.Combine(root, "matrix-session")));
        var window = new PCInfoScreenStudio.MainWindow(vm);
        Guid textId = Guid.Empty;
        try
        {
            window.Show(); pump(() => window.IsEnabled); vm.SetEditorActive(false);
            var document = vm.Document; var original = (document.CanvasWidth, document.CanvasHeight, document.Mode, vm.IsDirty, vm.SelectedWidget);
            var docks = ((WorkspaceDock)window.FindName("LeftDock")).CaptureLayout();
            var selector = (ComboBox)window.FindName("ScreenSelector");
            selector.SetCurrentValue(Selector.SelectedValueProperty, DisplayTarget.Matrix900); selector.GetBindingExpression(Selector.SelectedValueProperty)!.UpdateSource(); Flush(window); pump(() => vm.CanChooseScreen);
            var matrix = (MatrixWorkspaceView)window.FindName("MatrixWorkspace"); var model = vm.Matrix;
            Require(matrix.IsVisible && vm.IsMatrixWorkspace && !vm.IsCanvasActive && !vm.LivePreview, "Matrix retains its separate LED workspace and suspends the LCD editor.");
            Require((document.CanvasWidth, document.CanvasHeight, document.Mode, vm.IsDirty, vm.SelectedWidget) == original, "Opening Matrix preserves LCD theme geometry, selection and dirty state.");
            Require(model.Snapshot.Widgets!.Count == 3 && model.Snapshot.Widgets[1].X == 30, "Existing three-column content migrates to layers at its previous position.");
            Require(!model.ApplyCommand.CanExecute(null), "A disconnected preview cannot claim hardware output.");
            var surface = (MatrixSurface)matrix.FindName("MatrixPreview");
            Capture(window, captures, "Matrix900-layers-migrated.png");
            foreach (var layer in model.Layers) layer.Visible = false; model.LayerFlagsChanged();
            Execute(matrix, "AddTextButton"); textId = model.SelectedWidgetId!.Value;
            Require(!Descendants<TextBox>(matrix).Any(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Matrix widget name"), "Widget type is a read-only heading, without a redundant editable Name field.");
            model.Text = "CPU"; model.Width = 16; model.X = 1; model.Y = 1;
            var size = (ComboBox)matrix.FindName("WidgetSizeSelector"); Require(size.Items.Count == 4, "Every widget offers exactly 10-, 6-, 5- and 4-dot heights.");
            foreach (int h in new[] { 4, 10, 6, 5 })
            { Set(size, h); Flush(window); Require(model.WidgetHeight == h && model.Y + h <= 10, "The actual size control changes height and clamps the widget inside the LED grid."); }
            var font = (ComboBox)matrix.FindName("FiveDotFontSelector"); Set(font, MatrixFiveDotFont.Petty5x5); Flush(window);
            var alignment = (ComboBox)matrix.FindName("AlignmentSelector"); Set(alignment, MatrixAlignment.Centre); Flush(window);
            model.Y = 1;
            Execute(matrix, "AddValueButton"); var valueId = model.SelectedWidgetId; model.X = 22; model.Source = "GPU.Usage"; model.WidgetHeight = 4;
            Require(ReferenceEquals(model.DataSources, vm.DataSources), "Matrix uses the same live full source catalog as Info Screen.");
            var sourceSearch = (TextBox)matrix.FindName("SourceSearchBox"); var sourcePicker = (SourcePicker)matrix.FindName("SensorSourcePicker");
            sourceSearch.SetCurrentValue(TextBox.TextProperty, "GPU"); sourceSearch.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); Flush(window);
            Require(sourcePicker.IsDropDownOpen && sourcePicker.Items.Count == 9 && sourcePicker.Items.Contains("GPU.Power"), "The actual Matrix search opens the shared sensor picker with all GPU choices.");
            sourcePicker.SelectedItem = "GPU.Power"; sourcePicker.IsDropDownOpen = false; Flush(window);
            var prefixBox = (TextBox)matrix.FindName("ValueLabelBox"); prefixBox.SetCurrentValue(TextBox.TextProperty, "GPU:"); prefixBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); Flush(window);
            model.UpdateMetrics(new Dictionary<string, MetricValue> { ["GPU.Power"] = new(85.5, Unit: " W") });
            Require(model.Source == "GPU.Power" && model.Prefix == "GPU:" && model.PreviewReading.EndsWith(" W") && !model.PreviewReading.Contains("°C"), "Value label editing and the selected source's actual units reach the preview.");
            int valueWidth = model.Width; model.Prefix = "GPU board power:"; Require(model.Width > valueWidth, "Value boxes expand with their label instead of clipping it."); model.Prefix = "GPU:";
            model.SourceSearchText = "no-such-source"; Flush(window); Require(sourcePicker.Items.Count == 0 && sourcePicker.Text == "GPU.Power" && model.Source == "GPU.Power", "An empty search preserves the selected source.");
            model.ClearSourceSearchCommand.Execute(null); Flush(window); Require(sourcePicker.Items.Count == vm.DataSources.Count, "Clear restores every shared source.");
            var advanced = (CheckBox)matrix.FindName("AdvancedSensorsCheckBox"); advanced.SetCurrentValue(ToggleButton.IsCheckedProperty, true); advanced.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            const string rawSource = "Sensor: GPU > Test board power"; vm.DataSources.Add(rawSource); model.SourceSearchText = "GPU board"; Flush(window);
            Require(vm.ShowAdvancedSensors && sourcePicker.Items.Contains(rawSource), "Advanced sensor discovery updates the same live source list in Matrix.");
            sourcePicker.SelectedItem = rawSource; sourcePicker.IsDropDownOpen = false; Flush(window); Require(model.Snapshot.Widgets!.Single(w => w.Id == valueId).Source == rawSource, "Advanced sensor selection survives validated snapshots.");
            model.UpdateMetrics(new Dictionary<string, MetricValue> { [rawSource] = new(90, Unit: " W") }); model.SourceSearchText = ""; Flush(window);
            Capture(window, captures, "Matrix900-sensor-label-and-full-picker.png");
            model.X = 22;
            Execute(matrix, "AddClockButton"); var clockId = model.SelectedWidgetId; model.X = 45; model.Y = 5; model.Width = 22;
            Require(model.SelectedWidget!.Content == MatrixContent.Clock && model.WidgetHeight == 5, "Clock button adds an independent clock layer.");
            model.SelectedWidgetId = textId; Flush(window);
            Require(model.FiveDotFont == MatrixFiveDotFont.Petty5x5 && model.Alignment == MatrixAlignment.Centre && model.WidgetHeight == 5, "Switching layers restores each widget's font, alignment and size.");
            var list = (ListBox)matrix.FindName("LayerList"); Require((Guid)list.SelectedValue == textId && list.Items.Count == 6, "The Layers list and canvas share selection and include migrated hidden layers.");
            Require(surface.BeginDrag(PixelPoint(surface, 2, 2)), "An unlocked widget can be grabbed on the actual WPF canvas.");
            surface.DragTo(PixelPoint(surface, 4.4, 3.4)); surface.EndDrag();
            Require(model.X == 3 && model.Y == 2, "Dragging rounds movement to individual LED columns and rows.");
            Require(surface.BeginDrag(PixelPoint(surface, 4, 3)), "The moved layer can be dragged again.");
            surface.DragTo(PixelPoint(surface, 200, 50)); Require(model.X == 74 && model.Y == 5, "Dragging cannot move any part of a widget beyond the 90 by 10 grid.");
            surface.EndDrag(true); Require(model.X == 3 && model.Y == 2, "Cancel restores the position before the drag.");
            surface.Focus(); surface.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Left) { RoutedEvent = Keyboard.KeyDownEvent });
            Require(model.X == 2, "Canvas arrow keys nudge a layer by one LED.");
            Flush(window); var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(model.SelectedWidget);
            var lockBox = Descendants<CheckBox>(container).Single(c => c.Content?.ToString() == "Lock");
            lockBox.SetCurrentValue(ToggleButton.IsCheckedProperty, true); lockBox.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource(); Flush(window);
            Require(model.Tile.Locked && !model.IsEditable && !model.DeleteCommand.CanExecute(null), "The actual layer lock checkbox protects editing and deletion.");
            int lockedX = model.X; model.X = 40; Require(model.X == lockedX && !surface.BeginDrag(PixelPoint(surface, lockedX + 1, 3)), "Locked layers cannot be moved by properties or dragging.");
            lockBox.SetCurrentValue(ToggleButton.IsCheckedProperty, false); lockBox.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource(); Flush(window);
            Execute(matrix, "AddPixelArtButton"); var artId = model.SelectedWidgetId!.Value; model.Width = 6; model.WidgetHeight = 4; model.X = 74;
            model.IsPainting = true; surface.Focus(); surface.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            Require(model.Tile.Pixels.Take(3).SequenceEqual(new byte[] { 33, 230, 232 }), "Pixel-art painting writes into the selected widget's local bitmap.");
            model.DuplicateCommand.Execute(null); Require(model.SelectedWidgetId != artId && model.Tile.Pixels[1] == 230, "Duplicate creates an independent widget with copied artwork.");
            model.PaintPixel(model.X, model.Y, true); Require(model.Snapshot.Widgets!.Single(w => w.Id == artId).Pixels[1] == 230, "Editing duplicate artwork leaves the source layer unchanged.");
            model.DeleteCommand.Execute(null); Require(!model.Layers.Any(w => w.Name.EndsWith(" copy")), "Delete removes only the selected duplicate.");
            Execute(matrix, "AddEmojiButton"); model.WidgetHeight = 10; model.X = 89; model.Text = "💕👌"; model.Scroll = false;
            Require(model.AutoWidth && model.X + model.Width <= 90 && !((TextBox)matrix.FindName("WidgetWidthBox")).IsEnabled, "Emoji width follows rendered content and stays inside the screen; automatic width is read-only.");
            var fittedEmoji = model.Snapshot.Widgets!.Single(w => w.Id == model.SelectedWidgetId); var wideEmoji = model.Snapshot.Widgets!.Single(w => w.Id == model.SelectedWidgetId);
            fittedEmoji.X = 0; wideEmoji.X = 0; wideEmoji.AutoWidth = false; wideEmoji.Width = 90;
            Require(MatrixLayerRenderer.Compose(new() { Widgets = [fittedEmoji] }, _ => null, DateTime.UnixEpoch).SequenceEqual(TrimLeft(MatrixLayerRenderer.Compose(new() { Widgets = [wideEmoji] }, _ => null, DateTime.UnixEpoch))), "The fitted emoji frame preserves every coloured pixel from a full-width box after trimming outer blank columns.");
            Require(model.Validation.Length == 0, "Emoji text is supported instead of being rejected by the old ASCII-only validation.");
            var emojiPixels = model.PreviewPixels();
            Require(Enumerable.Range(0, emojiPixels.Length / 3).Any(i => emojiPixels[i * 3] > 30 && emojiPixels[i * 3 + 1] > 10 && emojiPixels[i * 3] != emojiPixels[i * 3 + 1] && emojiPixels[i * 3] != 33), "Emojis preserve their own coloured palette in the actual LED RGB frame.");
            var family = new MatrixSettings { Widgets = [new() { Text = "👨‍👩‍👧‍👦", Height = 10, Width = 20 }] }.ValidatedCopy();
            Require(MatrixLayerRenderer.Compose(family, _ => null, DateTime.UnixEpoch).Any(b => b != 0), "Joined emoji graphemes render as a shaped glyph without surrogate splitting.");
            foreach (int h in new[] { 4, 5, 6, 10 })
            {
                var invisible = new MatrixSettings { Widgets = [new() { Text = "\uFE0F \uFE0F \uFE0F \uFE0F \uFE0F", Height = h, Width = 90, Scroll = false }] }.ValidatedCopy();
                var fallback = MatrixLayerRenderer.Compose(invisible, _ => null, DateTime.UnixEpoch);
                Require(fallback.Length == 2700 && fallback.Any(b => b != 0), "Invisible emoji selectors recover to visible glyphs without invalid spacing at every height.");
            }
            Capture(window, captures, "Matrix900-widgets-layers-emojis.png");
            model.SelectedWidgetId = textId; Flush(window); Capture(window, captures, "Matrix900-widget-properties.png");
            model.Scroll=true;Flush(window);
            var animationArea=(ComboBox)matrix.FindName("AnimationAreaSelector");Require(animationArea.Items.Count==2,"Moving text offers the 60- and 90-dot animation areas.");
            Set(animationArea,90);var direction=(ComboBox)matrix.FindName("FlightDirectionSelector");Set(direction,MatrixFlightDirection.LeftToRight);
            Require(model.Scroll && model.Width==90 && !model.AutoWidth && model.AnimationAreaVisibility==Visibility.Visible,"Text animation uses the selected direction and full Matrix area.");
            var movingTextFrame=model.PreviewPixels();Set(direction,MatrixFlightDirection.RightToLeft);Require(!movingTextFrame.SequenceEqual(model.PreviewPixels()),"Changing animated text direction changes its current LED frame.");
            Capture(window,captures,"Matrix900-animated-text.png");model.Scroll=false;model.X=2;model.Y=2;
            window.Width = 1080; window.Height = 680; Flush(window);
            var bounds = surface.TransformToAncestor(window).TransformBounds(new Rect(surface.RenderSize));
            Require(bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight, "The complete LED canvas fits a compact window with both side panels open.");
            Capture(window, captures, "Matrix900-widgets-compact.png");
            window.Width=1440;window.Height=900;Flush(window);
            foreach(var layer in model.Layers)layer.Visible=false;model.LayerFlagsChanged();
            Execute(matrix,"AddProgressBarButton");
            var styles=(ComboBox)matrix.FindName("BarStyleSelector");Require(styles.Items.Count==12,"All twelve progress-bar styles are available in the actual Properties selector.");
            var sensorToggle=(CheckBox)matrix.FindName("BarUseSensorCheckBox");sensorToggle.SetCurrentValue(ToggleButton.IsCheckedProperty,false);sensorToggle.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateSource();
            Set(styles,MatrixBarStyle.Arrows);model.ManualValue=65;
            var barWidths=(ComboBox)matrix.FindName("BarWidthSelector");Require(barWidths.Items.Count==3,"Progress bars offer exactly 30-, 45- and 60-dot widths.");
            foreach(int width in new[] {30,45,60}) { Set(barWidths,width);Require(model.Width==width,"The progress-bar width selector applies each supported span."); }
            var barLabel=(TextBox)matrix.FindName("BarLabelBox");barLabel.SetCurrentValue(TextBox.TextProperty,"CPU 👌");barLabel.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            foreach(int h in new[] {4,5,6,10}) { Set(size,h);Flush(window);Require(model.Tile.Content==MatrixContent.ProgressBar && model.WidgetHeight==h && !model.AutoWidth,"The bar widget retains its selected span at all four native heights."); }
            Require(model.Prefix=="CPU 👌" && model.PreviewPixels().Any(b=>b!=0),"Text and emoji before a progress bar reach the LED preview.");
            Capture(window,captures,"Matrix900-progress-bar-and-panel-guide.png");
            var bank=(Expander)matrix.FindName("ArtBankExpander");bank.IsExpanded=true;Flush(window);
            var bankItems=(ItemsControl)matrix.FindName("ArtBankItems");Require(bankItems.Items.Count==26,"The pixel-art bank includes the 21 earlier designs and five supplied cat faces.");
            var catFace=Descendants<Button>(bankItems).Single(b=>Equals(b.CommandParameter,"cat-face-orange"));catFace.Command.Execute(catFace.CommandParameter);
            Require(model.Tile.Content==MatrixContent.PixelArt && model.Tile.ArtKey=="cat-face-orange" && model.Tile.Pixels.Any(b=>b!=0),"Clicking a supplied cat-face thumbnail adds an editable pixel-art layer.");
            Set(size,4);Require(model.Width==4 && model.Tile.ArtWidth==4,"Changing cat-face height selects its native size and updates width.");
            Capture(window,captures,"Matrix900-pixel-art-bank.png");
            bank.IsExpanded=false;
            foreach(var layer in model.Layers)layer.Visible=false;model.LayerFlagsChanged();
            var animations=(Expander)matrix.FindName("AnimationBankExpander");animations.IsExpanded=true;Flush(window);
            var animationItems=(ItemsControl)matrix.FindName("AnimationBankItems");Require(animationItems.Items.Count==12,"The Animations bank includes the supplied UFO and all earlier moving options.");
            var rocketButton=Descendants<Button>(animationItems).Single(b=>Equals(b.CommandParameter,MatrixAnimationStyle.Rocket));rocketButton.Command.Execute(rocketButton.CommandParameter);
            Set(direction,MatrixFlightDirection.LeftToRight);
            model.AnimationPlaying=false;
            Require(model.HitWidget(50,5) is null,"Empty parts of a flight area do not steal canvas clicks from widgets underneath.");
            foreach(int h in new[] {4,5,6,10}) { Set(size,h);Require(model.Tile.ArtWidth==MatrixArtBank.Get("rocket",h).Width && model.Width==90,"Rocket sizing preserves the flight area and selects the correct native sprite."); }
            Set(animationArea,60);model.AnimationPlaying=true;Flush(window);
            long phase=model.PreviewAnimationStep;var beforeFlight=model.PreviewPixels();
            pump(()=>model.PreviewAnimationStep>=phase+3);
            Require(!beforeFlight.SequenceEqual(model.PreviewPixels()) && model.Width==60,"The real preview timer animates rocket flight and its fire trail in the selected area while disconnected.");
            Capture(window,captures,"Matrix900-rocket-flight-right.png");
            Set(direction,MatrixFlightDirection.RightToLeft);Capture(window,captures,"Matrix900-rocket-flight-left.png");
            var catButton=Descendants<Button>(animationItems).Single(b=>Equals(b.CommandParameter,MatrixAnimationStyle.Cat));catButton.Command.Execute(catButton.CommandParameter);
            var counts=(ComboBox)matrix.FindName("InstanceCountSelector");Require(counts.Items.Count==3,"Discrete moving objects offer one, two or three instances.");Set(counts,3);
            var cats=(ComboBox)matrix.FindName("CatStyleSelector");Require(cats.Items.Count==5,"Cat animation offers all five supplied colours.");Set(cats,MatrixCatStyle.Orange);Set(direction,MatrixFlightDirection.LeftToRight);Flush(window);
            Require(model.Tile.AnimationStyle==MatrixAnimationStyle.Cat && model.InstanceCount==3 && model.CatStyle==MatrixCatStyle.Orange,"Cat colour, direction and object count apply to the selected animation layer.");
            Capture(window,captures,"Matrix900-cat-animation.png");
            var starButton=Descendants<Button>(animationItems).Single(b=>Equals(b.CommandParameter,MatrixAnimationStyle.Starfield));starButton.Command.Execute(starButton.CommandParameter);
            var stars=(ComboBox)matrix.FindName("StarStyleSelector");var shooting=(ComboBox)matrix.FindName("ShootingStarStyleSelector");Require(stars.Items.Count==3&&shooting.Items.Count==3,"Starfield exposes every supplied star image plus Off/White/Yellow shooting-star choices.");
            Set(stars,MatrixStarStyle.White2);Set(shooting,MatrixShootingStarStyle.Yellow);Set(direction,MatrixFlightDirection.RightToLeft);Flush(window);
            Require(model.StarStyle==MatrixStarStyle.White2&&model.ShootingStarStyle==MatrixShootingStarStyle.Yellow,"Independent star and shooting-star choices apply to the selected layer.");
            Capture(window,captures,"Matrix900-starfield-assets.png");
            var ufoButton=Descendants<Button>(animationItems).Single(b=>Equals(b.CommandParameter,MatrixAnimationStyle.Ufo));ufoButton.Command.Execute(ufoButton.CommandParameter);Set(counts,2);Flush(window);
            Require(model.Tile.AnimationStyle==MatrixAnimationStyle.Ufo&&model.InstanceCount==2,"The supplied UFO supports the shared object-count control.");
            Capture(window,captures,"Matrix900-ufo-animation.png");
            animations.IsExpanded=true;Flush(window);Capture(window,captures,"Matrix900-animations-bank.png");
            model.AnimationPlaying=false;
            vm.SelectedDisplayTarget = DisplayTarget.Turing52; Flush(window); pump(() => vm.CanChooseScreen);
            Require(!matrix.IsVisible && vm.IsEditorPage, "The LCD editor returns when a different screen is selected.");
            Require(((WorkspaceDock)window.FindName("LeftDock")).CaptureLayout().Panels.Select(p => (p.Id, p.IsOpen)).SequenceEqual(docks.Panels.Select(p => (p.Id, p.IsOpen))), "Matrix layers preserve LCD dock preferences.");
            vm.SelectedDisplayTarget = DisplayTarget.Matrix900; pump(() => vm.CanChooseScreen); model.SelectedWidgetId = textId; model.Text = "PERSIST ME";
        }
        finally { window.MarkSessionSavedForExit(); window.Close(); }
        var loaded = new SettingsController(new AppSettingsService(path));
        var text = loaded.Matrix.Widgets!.Single(w => w.Id == textId);
        Require(text.Text == "PERSIST ME" && text.X == 2 && text.Y == 2 && text.FiveDotFont == MatrixFiveDotFont.Petty5x5 && text.Alignment == MatrixAlignment.Centre, "Restart preserves layer identity, geometry, content, fonts and alignment.");
        var savedValue = loaded.Matrix.Widgets!.Single(w => w.Prefix == "GPU:");
        Require(savedValue.Source == "Sensor: GPU > Test board power" && savedValue.AutoWidth, "Restart preserves the chosen advanced source, value label and automatic sizing.");
        using var second = new MainViewModel(loaded, new SessionStateService(Path.Combine(root, "matrix-second")));
        Require(second.IsMatrixWorkspace && second.Matrix.Snapshot.Widgets!.Count == loaded.Matrix.Widgets!.Count, "Restart restores the layer workspace without migrating or duplicating it again.");
        Console.WriteLine("PASS: WPF Matrix widget buttons, layer selection/lock/duplicate/delete, pixel-snapped drag/cancel/nudge, all heights, coloured/joined emojis, compact layout and restart.");
    }
    private static void Execute(MatrixWorkspaceView view, string name)
    { var button = (Button)view.FindName(name); Require(button.Command.CanExecute(null), name + " is available."); button.Command.Execute(null); }
    private static void Set(ComboBox control, object value) { control.SetCurrentValue(Selector.SelectedValueProperty, value); control.GetBindingExpression(Selector.SelectedValueProperty)!.UpdateSource(); }
    private static void Flush(Window window) { Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle); window.UpdateLayout(); }
    private static byte[] TrimLeft(byte[] frame)
    {
        int left = Enumerable.Range(0, 90).First(x => Enumerable.Range(0, 10).Any(y => frame.Skip((y * 90 + x) * 3).Take(3).Any(b => b != 0)));
        var trimmed = new byte[2700];
        for (int y = 0; y < 10; y++) Array.Copy(frame, (y * 90 + left) * 3, trimmed, y * 270, (90 - left) * 3);
        return trimmed;
    }
    private static Point PixelPoint(MatrixSurface surface, double x, double y)
    { double s = Math.Min(surface.ActualWidth / 90, surface.ActualHeight / 10); return new((surface.ActualWidth - s * 90) / 2 + (x + .5) * s, (surface.ActualHeight - s * 10) / 2 + (y + .5) * s); }
    private static IEnumerable<T> Descendants<T>(DependencyObject item) where T : DependencyObject
    { if (item is T found) yield return found; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(item, i))) yield return child; }
    private static void Capture(Window window, string? directory, string name)
    {
        if (directory is null) return; Directory.CreateDirectory(directory); Flush(window); var element = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(element); using var file = File.Create(Path.Combine(directory, name)); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(file);
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
