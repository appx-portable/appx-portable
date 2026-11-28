#pragma once
#include <inspectable.h>

namespace appx {
MIDL_INTERFACE("1f08c8f1-7f70-4fd7-ae0f-fba8325a8179") IXamlFocusTarget : IUnknown {
    virtual HRESULT STDMETHODCALLTYPE RaiseFocus(boolean focused) = 0;
};
}
