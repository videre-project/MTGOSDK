#include <metahost.h>
#include <io.h>
#include <fcntl.h>
#include <corerror.h>

#pragma comment(lib, "mscoree.lib")
#include <stdio.h>

#include "stdafx.h"
#include "Bootstrapper.h"


DllExport void AdapterEntryPoint(const wchar_t* adapterDllArg)
{
	BOOL consoleAllocated = false;
	HRESULT hr;

	const auto parts = split(adapterDllArg, L"*");
	if (parts.size() < 4) return;

	const auto& managedDllLocation = parts.at(0);
	const auto& managedDllClass = parts.at(1);
	const auto& managedDllFunction = parts.at(2);
	const auto& scubaDiverArg = parts.at(3);

	ICLRRuntimeHost* pClr = StartCLR(L"v4.0.30319");
	if (pClr != NULL)
	{
		DWORD result;
		hr = pClr->ExecuteInDefaultAppDomain(
			managedDllLocation.c_str(),
			managedDllClass.c_str(),
			managedDllFunction.c_str(),
			scubaDiverArg.c_str(),
			&result);
	}
}

ICLRRuntimeHost* StartCLR(LPCWSTR dotNetVersion)
{
	HRESULT hr;

	ICLRMetaHost* pClrMetaHost = NULL;
	ICLRRuntimeInfo* pClrRuntimeInfo = NULL;
	ICLRRuntimeHost* pClrRuntimeHost = NULL;

	// Get the CLRMetaHost that tells us about .NET on this machine
	hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&pClrMetaHost);
	if (hr == S_OK)
	{
		// Get the runtime information for the particular version of .NET
		hr = pClrMetaHost->GetRuntime(dotNetVersion, IID_PPV_ARGS(&pClrRuntimeInfo));
		if (hr == S_OK)
		{
			// Check if the specified runtime can be loaded into the process. This
			// method will take into account other runtimes that may already be
			// loaded into the process and set pbLoadable to TRUE if this runtime can
			// be loaded in an in-process side-by-side fashion.
			BOOL fLoadable;
			hr = pClrRuntimeInfo->IsLoadable(&fLoadable);
			if ((hr == S_OK) && fLoadable)
			{
				// Load the CLR into the process and return a runtime interface pointer.
				hr = pClrRuntimeInfo->GetInterface(CLSID_CLRRuntimeHost,
						IID_PPV_ARGS(&pClrRuntimeHost));
				if (hr == S_OK)
				{
					// Start it. This is okay to call even if the CLR is already running
					hr = pClrRuntimeHost->Start();
					return pClrRuntimeHost;
				}
			}
		}
	}

	// Cleanup if failed
	if (pClrRuntimeHost)
	{
		pClrRuntimeHost->Release();
		pClrRuntimeHost = NULL;
	}
	if (pClrRuntimeInfo)
	{
		pClrRuntimeInfo->Release();
		pClrRuntimeInfo = NULL;
	}
	if (pClrMetaHost)
	{
		pClrMetaHost->Release();
		pClrMetaHost = NULL;
	}

	return NULL;
}
