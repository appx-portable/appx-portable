using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Microsoft.Xbox.Marketplace;
using Microsoft.Xbox.Leaderboards;
using Windows.Foundation;

internal static class MarketplaceProjectionTest
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void CheckAction(IAsyncAction action, AsyncStatus expected)
    {
        Check(action != null && action.Status == expected, "native action status");
        int completed = 0;
        action.Completed = (sender, status) =>
        {
            Check(ReferenceEquals(sender, action) && status == expected, "action callback identity/status");
            completed++;
        };
        Check(completed == 1, "one action completion callback");
        Check(action.Completed != null, "action completion getter");
        const int offline = unchecked((int)0x800704cf);
        if (expected == AsyncStatus.Completed)
        {
            Check(action.ErrorCode == null, "successful action error");
            action.GetResults();
        }
        else
        {
            Check(action.ErrorCode != null && action.ErrorCode.HResult == offline, "action native offline HRESULT");
            try { action.GetResults(); throw new Exception("signed-out post succeeded"); }
            catch (COMException error) { Check(error.HResult == offline, "action GetResults error"); }
        }
        action.Close();
    }
    private static void CheckActionTask(IAsyncAction action, bool failed)
    {
        try
        {
            try
            {
                System.WindowsRuntimeSystemExtensions.AsTask(action).GetAwaiter().GetResult();
                Check(!failed, "failed action task succeeded");
            }
            catch (COMException error) { Check(failed && error.HResult == unchecked((int)0x800704cf), "action task error"); }
        }
        finally { action.Close(); }
    }
    private static void CheckLeaderboardActions()
    {
        var leaderboards = new LeaderboardService();
        CheckAction(leaderboards.PostResultAsync(19, (LeaderboardAggregation)0, long.MaxValue), AsyncStatus.Completed);
        CheckActionTask(leaderboards.PostResultAsync(19, (LeaderboardAggregation)0, 4294967296L), false);
        var client = Microsoft.Xbox.XboxLIVEService.ServiceClient;
        CheckAction(client.SignOutAsync(), AsyncStatus.Completed);
        Check(Microsoft.Xbox.XboxLIVEService.SignedInUserIdentity == null, "native sign-out reached");
        try
        {
            CheckAction(leaderboards.PostResultAsync(19, (LeaderboardAggregation)0, long.MinValue), AsyncStatus.Error);
            CheckActionTask(leaderboards.PostResultAsync(19, (LeaderboardAggregation)0, -4294967296L), true);
        }
        finally { CheckAction(client.SignInAsync(), AsyncStatus.Completed); }
        Check(Microsoft.Xbox.XboxLIVEService.SignedInUserIdentity != null, "native sign-in reached");
    }
    private static void CheckPurchaseActions(bool local)
    {
        var offer = new Guid("01234567-89ab-cdef-0123-456789abcdef");
        CheckAction(MarketplaceService.ShowPurchaseAsync(offer), local ? AsyncStatus.Completed : AsyncStatus.Error);
        CheckActionTask(MarketplaceService.ShowPurchaseAsync(offer), !local);
        if (!local) return;
        var balanceOperation = MarketplaceService.GetAssetsAsync(0, 0, 0, null);
        try
        {
            var balances = (IReadOnlyList<AssetBalance>)typeof(AssetBalanceCollection).GetProperty("Items").GetValue(balanceOperation.GetResults(), null);
            Check(balances.Count == 1 && balances[0].Json.Contains("\"id\":17") && balances[0].Json.Contains("\"quantity\":14"),
                "exact 16-byte offer GUID must match the configured native asset grant");
        }
        finally { balanceOperation.Close(); }
        var receiptOperation = MarketplaceService.GetReceiptsAsync(0, 0, 0, null);
        try
        {
            var receipts = (IReadOnlyList<Receipt>)typeof(ReceiptCollection).GetProperty("Items").GetValue(receiptOperation.GetResults(), null);
            Check(receipts.Count == 2 && receipts[0].Json.IndexOf(offer.ToString(), StringComparison.OrdinalIgnoreCase) >= 0,
                "native receipts must retain the offer GUID");
            Check((Guid)typeof(Receipt).GetMethod("get_OfferId").Invoke(receipts[0], null) == offer,
                "receipt GUID property must retain all 16 bytes");
            var transaction = (Guid)typeof(Receipt).GetMethod("get_TransactionId").Invoke(receipts[0], null);
            Check(transaction != Guid.Empty, "native transaction GUID");
            var lookup = MarketplaceService.GetReceiptAsync(0, offer);
            try { Check((Guid)typeof(Receipt).GetMethod("get_OfferId").Invoke(lookup.GetResults(), null) == offer, "GUID receipt lookup"); }
            finally { lookup.Close(); }
        }
        finally { receiptOperation.Close(); }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ViewGetAt(IntPtr self, uint index, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ViewSize(IntPtr self, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ViewIndexOf(IntPtr self, IntPtr value, out uint index, out byte found);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ViewGetMany(IntPtr self, uint start, uint capacity, IntPtr values, out uint count);
    private static int ViewCall(IntPtr view, int slot, Type type, object[] args)
    {
        IntPtr method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(view), slot * IntPtr.Size);
        return (int)Marshal.GetDelegateForFunctionPointer(method, type).DynamicInvoke(args);
    }
    private static void CheckViewArgument()
    {
        var first = new AssetBalance(17, 1);
        var items = new List<AssetBalance> { first, new AssetBalance(17, 2) };
        Guid viewIid = new Guid("47f16fe9-252a-5aa6-b2e2-343fa27fdecb");
        Guid itemIid = new Guid("ae09ce48-3ab9-4a70-ab51-d7ff94edabde");
        var type = typeof(AssetBalance).Assembly.GetType("AppxPortable.WinRT.ObjectViewArgument");
        var held = (IDisposable)type.GetMethod("Hold", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .MakeGenericMethod(typeof(AssetBalance)).Invoke(null, new object[] { items, viewIid, itemIid });
        IntPtr pointer = (IntPtr)held.GetType().GetProperty("Pointer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(held, null);
        IntPtr retained;
        Check(Marshal.QueryInterface(pointer, ref viewIid, out retained) == 0, "closed view QI");
        held.Dispose(); // The native reference must keep the snapshot alive.
        items.Clear();
        IntPtr buffer = Marshal.AllocHGlobal(2 * IntPtr.Size);
        try
        {
            object[] args = { retained, (uint)0 };
            Check(ViewCall(retained, 7, typeof(ViewSize), args) == 0 && (uint)args[1] == 2, "view snapshots count");
            args = new object[] { retained, (uint)2, IntPtr.Zero };
            Check(ViewCall(retained, 6, typeof(ViewGetAt), args) == unchecked((int)0x8000000b) && (IntPtr)args[2] == IntPtr.Zero, "GetAt bounds");
            using (var lease = ((AppxPortable.WinRT.Interop.INativeObject)first).AcquireInterface(itemIid))
            {
                args = new object[] { retained, lease.DangerousGetHandle(), (uint)99, (byte)0 };
                Check(ViewCall(retained, 8, typeof(ViewIndexOf), args) == 0 && (uint)args[2] == 0 && (byte)args[3] == 1, "IndexOf COM identity");
            }
            args = new object[] { retained, IntPtr.Zero, (uint)99, (byte)1 };
            Check(ViewCall(retained, 8, typeof(ViewIndexOf), args) == 0 && (byte)args[3] == 0, "IndexOf absent");
            args = new object[] { retained, (uint)0, (uint)2, buffer, (uint)0 };
            Check(ViewCall(retained, 9, typeof(ViewGetMany), args) == 0 && (uint)args[4] == 2, "GetMany full count");
            for (int i = 0; i < 2; ++i) { IntPtr item = Marshal.ReadIntPtr(buffer, i * IntPtr.Size); Check(item != IntPtr.Zero, "GetMany owned item"); Marshal.Release(item); }
            args = new object[] { retained, (uint)2, (uint)2, buffer, (uint)99 };
            Check(ViewCall(retained, 9, typeof(ViewGetMany), args) == 0 && (uint)args[4] == 0, "GetMany end");
            args = new object[] { retained, (uint)3, (uint)2, buffer, (uint)99 };
            Check(ViewCall(retained, 9, typeof(ViewGetMany), args) == unchecked((int)0x8000000b) && (uint)args[4] == 0, "GetMany bounds");
            args = new object[] { retained, (uint)0, (uint)2, IntPtr.Zero, (uint)99 };
            Check(ViewCall(retained, 9, typeof(ViewGetMany), args) == unchecked((int)0x80004003) && (uint)args[4] == 0, "GetMany null buffer");
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.Release(retained); }
    }
    private static uint Balance()
    {
        var operation = MarketplaceService.GetAssetsAsync(0, 0, 0, null);
        try
        {
            var items = (IReadOnlyList<AssetBalance>)typeof(AssetBalanceCollection).GetProperty("Items").GetValue(operation.GetResults(), null);
            return items.Count == 0 ? 0 : (uint)typeof(AssetBalance).GetMethod("get_Quantity").Invoke(items[0], null);
        }
        finally { operation.Close(); }
    }
    private static void CheckConsume(bool local)
    {
        IReadOnlyList<AssetBalance> requested = new List<AssetBalance> { new AssetBalance(17, 6), new AssetBalance(17, 3) };
        var action = MarketplaceService.ConsumeAssetsAsync(0, requested);
        try
        {
            Check(action.Status == (local ? AsyncStatus.Completed : AsyncStatus.Error), "consume native action status");
            if (local) action.GetResults();
            else Check(action.ErrorCode.HResult == unchecked((int)0x800704cf), "consume offline HRESULT");
        }
        finally { action.Close(); }
        if (!local) return;
        Check(Balance() == 5, "consume duplicate asset entries exactly once");
        foreach (var invalid in new IReadOnlyList<AssetBalance>[] { null, new AssetBalance[] { null } })
        {
            try { MarketplaceService.ConsumeAssetsAsync(0, invalid); throw new Exception("null assets accepted"); }
            catch (Exception error) { Check(error.HResult == unchecked((int)0x80070057), "null assets rejected natively"); }
        }
        CheckViewArgument();
        action = MarketplaceService.ConsumeAssetsAsync(0, new[] { new AssetBalance(17, 6) });
        try { Check(action.Status == AsyncStatus.Error && action.ErrorCode.HResult == unchecked((int)0x80070057), "insufficient balance fails"); }
        finally { action.Close(); }
        Check(Balance() == 5, "failed consume preserves balance");
        CheckAction(MarketplaceService.ConsumeAssetsAsync(0, new List<AssetBalance>()), AsyncStatus.Completed);
        CheckAction(MarketplaceService.ConsumeAssetsAsync(0, new[] { new AssetBalance(17, 5) }), AsyncStatus.Completed);
        Check(Balance() == 0, "complete consume removes balance");
    }
    private static int Main(string[] args)
    {
        bool local = args.Length != 0 && args[0] == "local";
        try
        {
            CheckLeaderboardActions();
            CheckPurchaseActions(local);
            CheckConsume(local);
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
