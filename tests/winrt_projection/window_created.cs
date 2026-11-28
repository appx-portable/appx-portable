using System;
using Windows.UI.Xaml;
class WindowCreatedApp : Application
{
    private bool created;
    protected override void OnWindowCreated(WindowCreatedEventArgs args)
    {
        if (created || args.Window == null || !ReferenceEquals(args.Window, Window.Current))
            throw new Exception("WindowCreated did not deliver Window.Current exactly once");
        base.OnWindowCreated(args);
        created = true;
    }
    protected override void OnLaunched(Windows.ApplicationModel.Activation.LaunchActivatedEventArgs args)
    {
        if (!created) throw new Exception("Launch preceded WindowCreated");
        Console.WriteLine("PASS: window created before launch");
        Exit();
    }
    [STAThread]
    static void Main() { Application.Start(p => new WindowCreatedApp()); }
}
