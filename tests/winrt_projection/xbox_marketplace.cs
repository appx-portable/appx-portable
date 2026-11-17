using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Microsoft.Xbox.Marketplace;
using Windows.Foundation;

internal static class MarketplaceProjectionTest
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static int Main(string[] args)
    {
        bool local = args.Length != 0 && args[0] == "local";
        try
        {
            var operation = MarketplaceService.EnumerateOffersAsync(0, 0, 100, (ItemType)0, 0, (OrderingOption)0, true);
            Check(operation != null, "native marketplace operation");
            try
            {
            int completed = 0;
            operation.Completed = (sender, status) =>
            {
                Check(ReferenceEquals(sender, operation), "completed operation identity");
                Check(status == (local ? AsyncStatus.Completed : AsyncStatus.Error), "native completion status");
                completed++;
            };
            Check(completed == 1, "one terminal completion callback");
            if (local)
            {
                var result = operation.GetResults();
                var property = typeof(OfferCollection).GetProperty("Items");
                Check(result != null && property != null, "offer collection projection");
                var items = (IReadOnlyList<Offer>)property.GetValue(result, null);
                Check(items.Count == 1, "configured native offer collection");
                Check(items[0] != null, "native offer element projection");
                Check(items[0].OfferName.Length != 0, "native offer metadata");
                try { var unused = items[-1]; throw new Exception("negative vector index accepted"); }
                catch (ArgumentOutOfRangeException) { }
                try { var unused = items[items.Count]; throw new Exception("out-of-range vector index accepted"); }
                catch (ArgumentOutOfRangeException) { }
            }
            else
            {
                const int offline = unchecked((int)0x800704cf);
                Check(operation.Status == AsyncStatus.Error && operation.ErrorCode.HResult == offline, "native offline HRESULT");
                try { operation.GetResults(); throw new Exception("failed operation returned a result"); }
                catch (COMException error) { Check(error.HResult == offline, "GetResults must preserve native offline HRESULT"); }
            }
            }
            finally { operation.Close(); }
            try { operation.GetResults(); throw new Exception("closed results accepted"); }
            catch (COMException error) { Check(error.HResult == unchecked((int)0x80000013), "closed operation HRESULT"); }
            Console.WriteLine("PASS: native Xbox marketplace " + (local ? "local offers" : "offline operation")); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
