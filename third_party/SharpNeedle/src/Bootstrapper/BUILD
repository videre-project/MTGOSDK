load("//tools/bazel/windows_dll_library.bzl", "windows_dll_library")

# TODO: Pass through 'TARGET_CPU' variable to build for x86 or x64 targets.

windows_dll_library(
    name = "Bootstrapper",
    srcs = [
        "Bootstrapper.cpp",
        "dllmain.cpp"
    ],
    hdrs = ["Bootstrapper.h"],
    deps = [":stdafx"],
    # Define COMPILING_DLL to export symbols during the DLL compilation.
    copts = ["-DCOMPILING_DLL"],
    linkopts = ["-Wl,user32.lib"],
    visibility = ["//visibility:public"]
)

cc_library(
    name = "stdafx",
    srcs = ["stdafx.cpp"],
    hdrs = [
        "stdafx.h",
        "targetver.h"
    ],
)
