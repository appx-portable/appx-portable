using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

internal static class TaskBridgeProjectionTest
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static IAsyncAction Wrap(Task task)
    {
        var method = typeof(System.WindowsRuntimeSystemExtensions).GetMethod("AsAsyncAction", new[] { typeof(Task) });
        Check(method != null, "Task.AsAsyncAction must be projected");
        return (IAsyncAction)method.Invoke(null, new object[] { task });
    }
    private static void Wait(IAsyncAction action)
    {
        for (int count = 0; action.Status == AsyncStatus.Started && count < 500; ++count) Thread.Sleep(10);
        Check(action.Status != AsyncStatus.Started, "task must complete its action");
    }
    private static int Main()
    {
        try
        {
            var done = Wrap(Task.FromResult(1));
            Check(done.Status == AsyncStatus.Completed, "completed task status"); done.GetResults();
            var pending = new TaskCompletionSource<bool>();
            var action = Wrap(pending.Task);
            Check(action.Status == AsyncStatus.Started, "pending task status");
            int completed = 0;
            action.Completed = (sender, status) => { Check(ReferenceEquals(sender, action) && status == AsyncStatus.Completed, "completion identity/status"); Interlocked.Increment(ref completed); };
            pending.SetResult(true);
            Wait(action);
            for (int count = 0; completed == 0 && count < 500; ++count) Thread.Sleep(10);
            Check(completed == 1 && action.ErrorCode == null, "one task completion");
            action.GetResults();
            var marker = new InvalidOperationException("task marker");
            var failed = new TaskCompletionSource<bool>(); failed.SetException(marker);
            var error = Wrap(failed.Task); Wait(error);
            Check(error.Status == AsyncStatus.Error && ReferenceEquals(error.ErrorCode, marker), "original task failure");
            try { error.GetResults(); throw new Exception("faulted task succeeded"); }
            catch (InvalidOperationException result) { Check(ReferenceEquals(result, marker), "GetResults exception identity"); }
            var canceled = new TaskCompletionSource<bool>(); canceled.SetCanceled();
            var cancellation = Wrap(canceled.Task); Wait(cancellation);
            Check(cancellation.Status == AsyncStatus.Canceled, "canceled task status");
            try { cancellation.GetResults(); throw new Exception("canceled task succeeded"); }
            catch (OperationCanceledException) { }
            var faultedCancellation = new TaskCompletionSource<bool>(); faultedCancellation.SetException(new OperationCanceledException());
            var fault = Wrap(faultedCancellation.Task); Wait(fault);
            Check(fault.Status == AsyncStatus.Error, "faulted cancellation exception must remain an error");
            var callbackTask = new TaskCompletionSource<bool>();
            var callbackAction = Wrap(callbackTask.Task);
            string callbackMarker = "TaskBridge callback failure " + Guid.NewGuid();
            callbackAction.Completed = (sender, status) => { throw new InvalidOperationException(callbackMarker); };
            callbackTask.SetResult(true);
            Wait(callbackAction);
            string log = "";
            for (int count = 0; count < 500; ++count)
            {
                if (System.IO.File.Exists("appx-portable.log")) log = System.IO.File.ReadAllText("appx-portable.log");
                if (log.Contains(callbackMarker)) break;
                Thread.Sleep(10);
            }
            Check(log.Contains(callbackMarker), "completion callback failure must be logged");
            Check(callbackAction.Status == AsyncStatus.Completed && callbackAction.ErrorCode == null, "callback must not change task result");
            callbackAction.GetResults();
            Console.WriteLine("PASS: Task.AsAsyncAction completion, identity, faults and cancellation"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
