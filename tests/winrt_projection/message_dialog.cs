using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Foundation;
using Windows.UI.Popups;
internal static class MessageDialogProjectionTest
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("combase.dll")] private static extern int RoInitialize(uint apartment);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Marshal.ThrowExceptionForHR(RoInitialize(0));
            string mode = args.Length == 0 ? "success" : args[0];
            string title = "MessageDialog fixture " + Guid.NewGuid();
            var dialog = new MessageDialog("Unicode content: café", title);
            Check(dialog.Title == title && dialog.Content == "Unicode content: café", "constructor content/title");
            var commands = dialog.Commands;
            int invoked = -1;
            UICommand chosen = null;
            for (int index = 0; mode != "close-default" && index < 3; ++index)
            {
                int id = index;
                var command = new UICommand("Choice " + index, value => { invoked = (int)value.Id; }, id);
                commands.Add(command);
                if (index == 2) chosen = command;
            }
            if (mode != "close-default") Check(commands.Count == 3 && (int)commands[2].Id == 2, "commands and boxed IDs");
            dialog.DefaultCommandIndex = mode == "close-default" ? (uint)0 : (uint)1;
            dialog.CancelCommandIndex = 0;
            Check(dialog.DefaultCommandIndex == (mode == "close-default" ? 0 : 1) && dialog.CancelCommandIndex == 0, "command index round trip");
            using (var typed = new ManualResetEvent(false))
            using (var displayed = new ManualResetEvent(false))
            {
                Exception driverError = null;
                var driver = new Thread(() =>
                {
                    try
                    {
                        IntPtr window = IntPtr.Zero;
                        for (int count = 0; window == IntPtr.Zero && count < 1000; ++count) { window = FindWindow("#32770", title); Thread.Sleep(10); }
                        Check(window != IntPtr.Zero, "dialog must actually display");
                        displayed.Set();
                        Check(typed.WaitOne(10000), "pending operation checks must finish");
                        if (mode == "cancel-operation") return;
                        uint message = mode == "cancel-key" || mode == "close-default" ? (uint)0x10 : mode == "default-key" ? (uint)0x100 : (uint)(0x400 + 102);
                        IntPtr button = mode == "default-key" ? new IntPtr(13) : new IntPtr(1002);
                        Check(PostMessage(window, message, button, IntPtr.Zero), "choose fixture command");
                    }
                    catch (Exception error) { driverError = error; }
                });
                driver.IsBackground = true; driver.Start();
                var operation = dialog.ShowAsync();
                Check(displayed.WaitOne(10000), "dialog must display before checking pending status");
                Check(operation.Status == AsyncStatus.Started, "displayed operation stays pending");
                try { operation.GetResults(); throw new Exception("pending results accepted"); }
                catch (COMException error) { Check(error.HResult == unchecked((int)0x8000000e), "pending results HRESULT"); }
                try { dialog.ShowAsync(); throw new Exception("concurrent show accepted"); }
                catch (COMException error) { Check(error.HResult == unchecked((int)0x8000000e), "concurrent show HRESULT"); }
                if (mode == "cancel-operation") operation.Cancel();
                typed.Set();
                IUICommand result = null;
                try { result = System.WindowsRuntimeSystemExtensions.AsTask(operation).GetAwaiter().GetResult(); Check(mode != "cancel-operation", "canceled dialog completed"); }
                catch (OperationCanceledException) { Check(mode == "cancel-operation" && invoked == -1, "cancel must not invoke a command"); }
                Check(driver.Join(15000) && driverError == null, "dialog driver completion " + driverError);
                if (mode == "success") Check(ReferenceEquals(result, chosen) && invoked == 2, "selected command identity and callback before result");
                else if (mode == "default-key") Check(ReferenceEquals(result, commands[1]) && invoked == 1, "Enter selects configured default");
                else if (mode == "cancel-key") Check(ReferenceEquals(result, commands[0]) && invoked == 0, "close selects configured cancel");
                else if (mode == "close-default") Check(result != null && result.Label == "Close" && invoked == -1, "implicit close command");
                if (mode == "cancel-operation")
                {
                    for (int count = 0; FindWindow("#32770", title) != IntPtr.Zero && count < 1000; ++count) Thread.Sleep(10);
                    Check(FindWindow("#32770", title) == IntPtr.Zero && invoked == -1, "cancellation closes UI without invoking a command");
                }
                operation.Close();
                try { operation.GetResults(); throw new Exception("closed results accepted"); }
                catch (COMException error) { Check(error.HResult == unchecked((int)0x80000013), "closed results HRESULT"); }
            }
            Console.WriteLine("PASS: MessageDialog renders custom buttons and preserves command/callback identity"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
