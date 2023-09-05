# TODO: Pass through 'TARGET_CPU' variable to build for x86 or x64 targets.

cc_binary(
    name = "Launcher",
    srcs = ["Launcher.cpp"],
    deps = [":Injection", ":stdafx"],
    visibility = ["//visibility:public"]
)

cc_library(
    name = "Injection",
    srcs = ["Injection.cpp"],
    hdrs = [
        "HCommonEnsureCleanup.h",
        "Injection.h"
    ],
)

cc_library(
    name = "stdafx",
    srcs = ["stdafx.cpp"],
    hdrs = [
        "stdafx.h",
        "targetver.h"
    ],
)
