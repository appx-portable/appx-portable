using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Xbox;
using Microsoft.Xbox.Foundation;

class XboxSignedOutTest
{
    [DllImport("combase.dll")] private static extern int RoInitialize(uint apartment);

    [STAThread]
    static void Main()
    {
        Marshal.ThrowExceptionForHR(RoInitialize(0));
        EventHandler<SignedOutEventArgs> handler = (sender, args) => { throw new Exception("Offline service fired SignedOut"); };
        WindowsRuntimeMarshal.AddEventHandler(XboxLIVEService.add_SignedOut, XboxLIVEService.remove_SignedOut, handler);
        WindowsRuntimeMarshal.RemoveEventHandler(XboxLIVEService.remove_SignedOut, handler);
        Console.WriteLine("PASS: generic Xbox SignedOut subscription and removal");
    }
}
