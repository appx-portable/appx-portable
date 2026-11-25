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
    private static int Main(string[] args)
    {
        bool local = args.Length != 0 && args[0] == "local";
        try
        {
            CheckLeaderboardActions();
            CheckPurchaseActions(local);
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
