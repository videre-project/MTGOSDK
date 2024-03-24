#pragma once

#include "targetver.h"

// Exclude rarely-used stuff from Windows headers
// See: https://learn.microsoft.com/en-us/windows/win32/winprog/using-the-windows-headers?redirectedfrom=MSDN
#define WIN32_LEAN_AND_MEAN

// Windows Header Files:
#include <windows.h>

#pragma comment(linker, "/SUBSYSTEM:WINDOWS /DLL")
