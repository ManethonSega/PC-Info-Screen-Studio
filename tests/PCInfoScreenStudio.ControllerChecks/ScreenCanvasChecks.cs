using System.IO;
using PCInfoScreenStudio.Controllers;
using PCInfoScreenStudio.Models;
using PCInfoScreenStudio.Services;
using PCInfoScreenStudio.ViewModels;

internal static class ScreenCanvasChecks
{
    public static void Check(string root, Action<Task> awaitTask, Action<Func<bool>> pump)
    {
        var settings=new SettingsController(new AppSettingsService(Path.Combine(root,"canvas-selection.json")))
        {FirstRunCompleted=true, StartWithWindows=false, AutoStartDisplay=false};
        using var vm=new MainViewModel(settings,new SessionStateService(Path.Combine(root,"canvas-session")));
        vm.SetEditorActive(false);
        vm.CanvasSize=DisplayCanvasSize.Turing52;
        pump(()=>!vm.IsDeviceBusy);
        Assert(vm.Document.CanvasWidth==1280 && vm.Document.CanvasHeight==720,"Screen selection must resize the canvas immediately.");
        vm.Workspace.FilePath=Path.Combine(root,"canvas-before-blank.t3theme");
        awaitTask(vm.SaveCurrentThemeAsync());
        // The resize also dirties the two empty mode presets. Establish a clean
        // fixture before testing theme loading, so no discard confirmation is expected.
        var sessions=(ThemeSessionController)typeof(MainViewModel).GetField("_themeSessions",
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(vm)!;
        sessions.Loaded(ScreenMode.PhotoFrame,null);
        sessions.Loaded(ScreenMode.Hybrid,null);
        vm.Workspace.IsDirty=false;
        Assert(!vm.HasUnsavedChanges,"The saved canvas fixture must be clean before exercising theme loading.");
        vm.SelectedTheme=vm.Themes.First(t=>t.BuiltInId=="blank");
        vm.LoadThemeCommand.Execute(null);
        Assert(vm.CanvasSize==DisplayCanvasSize.Turing52 && vm.Document.CanvasWidth==1280 && vm.Document.Widgets.Count==0,"Blank theme must preserve the selected physical screen and its canvas.");
        var packages=new ThemePackageService();
        var oldPath=Path.Combine(root,"small-authored.t3theme");
        using(var old=packages.CreateNewWorkspace())
        {
            old.Document.Widgets.Add(new WidgetModel{Type=WidgetType.Value,X=48,Y=32,Width=96,Height=64,FontSize=20});
            awaitTask(packages.SaveAsync(old,oldPath));
        }
        awaitTask(vm.OpenThemeFileAsync(oldPath));
        Assert(vm.CanvasSize==DisplayCanvasSize.Turing52 && vm.Document.CanvasWidth==1280 && vm.Document.Widgets[0].X==128 && vm.Document.Widgets[0].FontSize==45,"Loading a 3.5-inch theme must fit the selected 5.2-inch screen.");
        awaitTask(vm.SaveCurrentThemeAsync());
        vm.Document.Mode=ScreenMode.Hybrid;
        var infoFont=vm.Document.Widgets[0].FontSize;
        var hybrid=new ThemeDocument{Mode=ScreenMode.Hybrid};
        hybrid.HybridWidgets.Add(new WidgetModel{X=48,Y=32,Width=96,Height=64,FontSize=20});
        var modeService=new ModeThemeService(root);
        var hybridPath=modeService.Save(hybrid,ScreenMode.Hybrid,"small-overlay");
        vm.LoadModeThemeFile(hybridPath);
        Assert(vm.Document.HybridWidgets[0].X==128 && vm.Document.HybridWidgets[0].FontSize==45 && vm.Document.Widgets[0].FontSize==infoFont,"Importing an overlay must scale it once without changing Info Screen typography.");
        vm.CanvasSize=DisplayCanvasSize.Serial35;
        pump(()=>!vm.IsDeviceBusy);
        Assert(vm.Document.CanvasWidth==480 && vm.Document.CanvasHeight==320 && settings.PreferredCanvasSize==DisplayCanvasSize.Serial35,"Changing back must resize and persist the new screen choice.");
        Console.WriteLine("PASS: selected-screen canvas, blank theme, authored theme and Hybrid import without compounded scaling.");
    }
    private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
