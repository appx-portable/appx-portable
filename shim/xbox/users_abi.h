#pragma once
#include "marketplace_abi.h"
namespace appx::xbox {
// Microsoft.Xbox.UserCollection, the result of User.GetFriendsAsync; identical
// in SDK 1.0, 1.2 and 2.0. Collection generic IIDs use the runtime-class
// signature rc(Microsoft.Xbox.User;{d7fba214-e744-4093-9fee-ab68795b70a0}).
MIDL_INTERFACE("3bae39a4-a149-49f6-b2c1-a3acf79711a0") IUserCollection : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Items(IInspectable * *value) = 0;
};
MIDL_INTERFACE("f825d71f-5601-57ba-b66c-050aafe17130") IUserIterator : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE get_Current(IInspectable * *value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_HasCurrent(boolean * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE MoveNext(boolean * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetMany(UINT32 capacity, IInspectable * *values, UINT32 * actual) = 0;
};
MIDL_INTERFACE("9917a785-b75b-5aaf-b2f1-745b93251e64") IUserIterable : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE First(IUserIterator * *value) = 0;
};
MIDL_INTERFACE("0c553cb0-9e08-502e-8e50-8f919fcab8f8") IUserVectorView : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE GetAt(UINT32 index, IInspectable * *value) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Size(UINT32 * value) = 0;
    virtual HRESULT STDMETHODCALLTYPE IndexOf(IInspectable * item, UINT32 * index, boolean * found) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetMany(UINT32 start, UINT32 capacity, IInspectable * *values,
                                              UINT32 * actual) = 0;
};
template <>
struct __declspec(uuid("7febd7fe-36e1-5c55-af24-8f3916224f2e")) OperationAbi<IUserCollection> : IInspectable {
    virtual HRESULT STDMETHODCALLTYPE put_Completed(IUnknown* handler) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_Completed(IUnknown** handler) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetResults(IUserCollection** value) = 0;
};
}
