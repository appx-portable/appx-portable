#include <windows.h>
#include <roapi.h>
#include <windows.storage.h>
#include <windows.storage.streams.h>
#include <robuffer.h>
#include <wrl.h>
#include <wrl/wrappers/corewrappers.h>
#include <cstdio>
#include <cstring>
#include <filesystem>
using namespace Microsoft::WRL;
using namespace Microsoft::WRL::Wrappers;
using namespace ABI::Windows::Storage;
using namespace ABI::Windows::Storage::Streams;
using namespace ABI::Windows::Foundation;
#define CHECK(x)                                                                                                       \
    do {                                                                                                               \
        if (!(x)) {                                                                                                    \
            printf("FAIL line %d: %s\n", __LINE__, #x);                                                                \
            return 1;                                                                                                  \
        }                                                                                                              \
    } while (0)
using WriteOperation = IAsyncOperationWithProgress<UINT32, UINT32>;
using WriteHandler = IAsyncOperationWithProgressCompletedHandler<UINT32, UINT32>;
static bool WaitComplete(IInspectable* operation) {
    ComPtr<IAsyncInfo> info;
    if (!operation || FAILED(operation->QueryInterface(IID_PPV_ARGS(&info)))) return false;
    const auto deadline = GetTickCount64() + 5000;
    AsyncStatus status = AsyncStatus::Started;
    do {
        if (FAILED(info->get_Status(&status))) return false;
        if (status != AsyncStatus::Started) return status == AsyncStatus::Completed;
        Sleep(1);
    } while (GetTickCount64() < deadline);
    return false;
}

int main() {
    CHECK(SUCCEEDED(RoInitialize(RO_INIT_MULTITHREADED)));
    wchar_t temp[MAX_PATH]{};
    CHECK(GetTempPathW(MAX_PATH, temp));
    const auto path = std::filesystem::path(temp) / (L"appx-filestream-" + std::to_wstring(GetCurrentProcessId()));
    struct Cleanup {
        std::filesystem::path path;
        ~Cleanup() { DeleteFileW(path.c_str()); }
    } cleanup{path};
    FileHandle initial(CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                   nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
    CHECK(initial.IsValid());
    DWORD written = 0;
    CHECK(WriteFile(initial.Get(), "old save with a longer tail", 26, &written, nullptr) && written == 26);
    initial.Close();

    auto module = LoadLibraryW(L"appxportable.dll");
    CHECK(module);
    auto get =
        reinterpret_cast<HRESULT(WINAPI*)(HSTRING, IActivationFactory**)>(GetProcAddress(module, "ShimGetFactory"));
    CHECK(get);
    ComPtr<IActivationFactory> activation;
    CHECK(SUCCEEDED(get(HStringReference(L"Windows.Storage.StorageFile").Get(), &activation)));
    ComPtr<IStorageFileStatics> factory;
    CHECK(SUCCEEDED(activation.As(&factory)));
    ComPtr<IAsyncOperation<StorageFile*>> lookup;
    ComPtr<IStorageFile> file;
    CHECK(SUCCEEDED(factory->GetFileFromPathAsync(HStringReference(path.c_str()).Get(), &lookup)));
    CHECK(WaitComplete(lookup.Get()) && SUCCEEDED(lookup->GetResults(&file)));
    ComPtr<IAsyncOperation<IRandomAccessStream*>> opening;
    const auto opened = file->OpenAsync(FileAccessMode_ReadWrite, &opening);
    printf("OpenAsync(ReadWrite): %08lx\n", opened);
    CHECK(SUCCEEDED(opened) && opening);
    ComPtr<IRandomAccessStream> stream;
    CHECK(WaitComplete(opening.Get()) && SUCCEEDED(opening->GetResults(&stream)) && stream);
    ComPtr<IAsyncOperation<IRandomAccessStream*>> invalid;
    CHECK(file->OpenAsync(static_cast<FileAccessMode>(99), &invalid) == E_INVALIDARG && !invalid);
    boolean readable = false, writable = false;
    CHECK(SUCCEEDED(stream->get_CanRead(&readable)) && readable);
    CHECK(SUCCEEDED(stream->get_CanWrite(&writable)) && writable);
    CHECK(SUCCEEDED(stream->put_Size(0))); // Autosaves must discard the previous file's tail.
    UINT64 size = 99, position = 99;
    CHECK(SUCCEEDED(stream->get_Size(&size)) && size == 0);

    CHECK(SUCCEEDED(get(HStringReference(L"Windows.Storage.Streams.Buffer").Get(), &activation)));
    ComPtr<IBufferFactory> buffers;
    CHECK(SUCCEEDED(activation.As(&buffers)));
    ComPtr<IBuffer> buffer;
    CHECK(SUCCEEDED(buffers->Create(32, &buffer)));
    ComPtr<::Windows::Storage::Streams::IBufferByteAccess> bytes;
    byte* data = nullptr;
    CHECK(SUCCEEDED(buffer.As(&bytes)) && SUCCEEDED(bytes->Buffer(&data)));
    memset(data, 0xcc, 32);
    data[0] = 'x';
    data[1] = 0;
    data[2] = 'z';
    CHECK(SUCCEEDED(buffer->put_Length(3)));
    ComPtr<IOutputStream> output;
    CHECK(SUCCEEDED(stream->GetOutputStreamAt(2, &output)));
    ComPtr<WriteOperation> write;
    CHECK(SUCCEEDED(output->WriteAsync(buffer.Get(), &write)) && write);
    UINT32 count = 0;
    CHECK(WaitComplete(write.Get()) && SUCCEEDED(write->GetResults(&count)) && count == 3);
    unsigned completions = 0;
    Event finished(CreateEventW(nullptr, FALSE, FALSE, nullptr));
    CHECK(finished.IsValid());
    auto completed = Callback<WriteHandler>([&](WriteOperation* sender, AsyncStatus status) -> HRESULT {
        UINT32 result = 0;
        if (sender != write.Get() || status != AsyncStatus::Completed || FAILED(sender->GetResults(&result)) ||
            result != 3)
            return E_FAIL;
        ++completions;
        SetEvent(finished.Get());
        return S_OK;
    });
    CHECK(SUCCEEDED(write->put_Completed(completed.Get())));
    CHECK(WaitForSingleObject(finished.Get(), 5000) == WAIT_OBJECT_0 && completions == 1);
    CHECK(write->put_Completed(completed.Get()) == E_ILLEGAL_DELEGATE_ASSIGNMENT);
    CHECK(SUCCEEDED(stream->get_Position(&position)) && position == 0);
    ComPtr<IRandomAccessStream> outputPosition;
    CHECK(SUCCEEDED(output.As(&outputPosition)));
    CHECK(SUCCEEDED(outputPosition->get_Position(&position)) && position == 5);
    CHECK(SUCCEEDED(stream->get_Size(&size)) && size == 5);
    ComPtr<IAsyncOperation<bool>> flush;
    CHECK(SUCCEEDED(output->FlushAsync(&flush)));
    boolean flushed = false;
    CHECK(WaitComplete(flush.Get()) && SUCCEEDED(flush->GetResults(&flushed)) && flushed);

    ComPtr<IRandomAccessStream> clone;
    CHECK(SUCCEEDED(stream->CloneStream(&clone)));
    CHECK(SUCCEEDED(clone->get_Position(&position)) && position == 0);
    CHECK(SUCCEEDED(clone->get_CanWrite(&writable)) && writable);
    ComPtr<IInputStream> input;
    CHECK(SUCCEEDED(clone.As(&input)));
    ComPtr<IAsyncOperationWithProgress<IBuffer*, UINT32>> read;
    CHECK(SUCCEEDED(input->ReadAsync(buffer.Get(), 32, InputStreamOptions_None, &read)));
    ComPtr<IBuffer> result;
    CHECK(WaitComplete(read.Get()) && SUCCEEDED(read->GetResults(&result)));
    UINT32 length = 0;
    CHECK(SUCCEEDED(result->get_Length(&length)) && length == 5);
    const byte expected[] = {0, 0, 'x', 0, 'z'};
    CHECK(SUCCEEDED(result.As(&bytes)) && SUCCEEDED(bytes->Buffer(&data)));
    CHECK(memcmp(data, expected, sizeof(expected)) == 0);

    // Truncation and reopening prove that writes reached disk, not a detached memory buffer.
    CHECK(SUCCEEDED(stream->put_Size(3)));
    ComPtr<IClosable> close;
    CHECK(SUCCEEDED(stream.As(&close)) && SUCCEEDED(close->Close()));
    CHECK(stream->GetOutputStreamAt(0, &output) == RO_E_CLOSED && !output);
    CHECK(SUCCEEDED(file->OpenAsync(FileAccessMode_Read, &opening)));
    ComPtr<IRandomAccessStream> readonly;
    CHECK(WaitComplete(opening.Get()) && SUCCEEDED(opening->GetResults(&readonly)));
    CHECK(SUCCEEDED(readonly->get_CanWrite(&writable)) && !writable);
    CHECK(SUCCEEDED(readonly->get_Size(&size)) && size == 3);
    CHECK(readonly->GetOutputStreamAt(0, &output) == E_ACCESSDENIED && !output);
    CHECK(SUCCEEDED(readonly.As(&output)));
    CHECK(output->WriteAsync(buffer.Get(), &write) == E_ACCESSDENIED && !write);
    CHECK(SUCCEEDED(readonly.As(&input)));
    CHECK(SUCCEEDED(input->ReadAsync(buffer.Get(), 32, InputStreamOptions_None, &read)));
    CHECK(WaitComplete(read.Get()) && SUCCEEDED(read->GetResults(&result)) && SUCCEEDED(result->get_Length(&length)) &&
          length == 3);
    CHECK(SUCCEEDED(result.As(&bytes)) && SUCCEEDED(bytes->Buffer(&data)) && memcmp(data, expected, 3) == 0);
    // The autosave keeps its writable stream open while handing a detached writer
    // buffer to FileIO. The FileIO handle must permit that existing writer.
    CHECK(SUCCEEDED(get(HStringReference(L"Windows.Storage.Streams.DataWriter").Get(), &activation)));
    ComPtr<IDataWriterFactory> writers;
    CHECK(SUCCEEDED(activation.As(&writers)) && SUCCEEDED(clone.As(&output)));
    ComPtr<IDataWriter> writer;
    CHECK(SUCCEEDED(writers->CreateDataWriter(output.Get(), &writer)));
    byte saved[] = {'S', 0, 0xff, 'V'};
    CHECK(SUCCEEDED(writer->WriteBytes(ARRAYSIZE(saved), saved)) && SUCCEEDED(writer->DetachBuffer(&buffer)));
    CHECK(SUCCEEDED(get(HStringReference(L"Windows.Storage.FileIO").Get(), &activation)));
    ComPtr<IFileIOStatics> files;
    ComPtr<IAsyncAction> save;
    CHECK(SUCCEEDED(activation.As(&files)) && SUCCEEDED(files->WriteBufferAsync(file.Get(), buffer.Get(), &save)));
    CHECK(WaitComplete(save.Get()) && SUCCEEDED(save->GetResults()));
    CHECK(SUCCEEDED(readonly->Seek(0)));
    CHECK(SUCCEEDED(input->ReadAsync(buffer.Get(), ARRAYSIZE(saved), InputStreamOptions_None, &read)));
    CHECK(WaitComplete(read.Get()) && SUCCEEDED(read->GetResults(&result)) && SUCCEEDED(result->get_Length(&length)) &&
          length == ARRAYSIZE(saved));
    CHECK(SUCCEEDED(result.As(&bytes)) && SUCCEEDED(bytes->Buffer(&data)) && memcmp(data, saved, sizeof(saved)) == 0);
    // A new game deletes the previous save through IStorageItem before writing.
    const auto doomed = std::filesystem::path(temp) / (L"appx-delete-" + std::to_wstring(GetCurrentProcessId()));
    struct DoomedCleanup {
        std::filesystem::path path;
        ~DoomedCleanup() { DeleteFileW(path.c_str()); }
    } doomedCleanup{doomed};
    FileHandle created(
        CreateFileW(doomed.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr));
    CHECK(created.IsValid());
    created.Close();
    ComPtr<IStorageFile> doomedFile;
    CHECK(SUCCEEDED(factory->GetFileFromPathAsync(HStringReference(doomed.c_str()).Get(), &lookup)));
    CHECK(WaitComplete(lookup.Get()) && SUCCEEDED(lookup->GetResults(&doomedFile)));
    ComPtr<IStorageItem> item;
    CHECK(SUCCEEDED(doomedFile.As(&item)));
    ComPtr<IAsyncAction> deleting;
    CHECK(SUCCEEDED(item->DeleteAsyncOverloadDefaultOptions(&deleting)) && deleting);
    CHECK(WaitComplete(deleting.Get()) && SUCCEEDED(deleting->GetResults()));
    CHECK(GetFileAttributesW(doomed.c_str()) == INVALID_FILE_ATTRIBUTES);
    // A missing file fails through the action, as on Windows, not the call.
    CHECK(SUCCEEDED(item->DeleteAsync(StorageDeleteOption_PermanentDelete, &deleting)) && deleting);
    CHECK(!WaitComplete(deleting.Get()) && deleting->GetResults() == HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND));
    puts("filestreamtest: writable saves, completion, cursors, cloning, truncation, read-only access and deletion "
         "passed");
    return 0;
}
