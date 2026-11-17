using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;

internal static class StorageBufferProjectionTest
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Result<T>(IAsyncOperation<T> operation)
    {
        for (int count = 0; operation.Status == AsyncStatus.Started && count < 500; ++count) Thread.Sleep(10);
        Check(operation.Status != AsyncStatus.Started, "operation completion");
        return operation.GetResults();
    }
    private static void Bytes(IBuffer buffer, byte[] expected)
    {
        Check(buffer.Length == expected.Length, "exact buffer length");
        var actual = new byte[expected.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(actual);
        for (int i = 0; i < actual.Length; ++i) Check(actual[i] == expected[i], "byte " + i);
    }
    private static int Main()
    {
        try
        {
            string path = Path.Combine(Environment.CurrentDirectory, "bytes-đá.bin");
            byte[] bytes = { 0, 1, 127, 128, 255, 13, 10, 0 };
            File.WriteAllBytes(path, bytes);
            var read = PathIO.ReadBufferAsync(path);
            Bytes(Result(read), bytes);
            var file = Result(StorageFile.GetFileFromPathAsync(path));
            Bytes(Result(FileIO.ReadBufferAsync(file)), bytes);
            File.WriteAllBytes(path, new byte[0]);
            Bytes(Result(PathIO.ReadBufferAsync(path)), new byte[0]);
            File.Delete(path);
            var missing = PathIO.ReadBufferAsync(path);
            Check(missing.Status == AsyncStatus.Error, "missing path must return an errored operation");
            try { missing.GetResults(); throw new Exception("missing path succeeded"); }
            catch (Exception error) { Check(error.HResult == unchecked((int)0x80070002), "missing path HRESULT"); }
            try { PathIO.ReadBufferAsync(null); throw new Exception("null path accepted"); }
            catch (ArgumentNullException) { }
            try { PathIO.ReadBufferAsync(""); throw new Exception("empty path accepted"); }
            catch (ArgumentException error) { Check(error.HResult == unchecked((int)0x80070057), "empty path HRESULT"); }
            try { FileIO.ReadBufferAsync(null); throw new Exception("null file accepted"); }
            catch (Exception error) { Check(error.HResult == unchecked((int)0x80004003), "null file HRESULT"); }
            Console.WriteLine("PASS: native PathIO/FileIO byte reads, Unicode path, empty file, missing file and invalid input"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
