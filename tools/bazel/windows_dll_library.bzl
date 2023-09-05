# Copyright 2022 The Bazel Authors. All rights reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#    http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

"""
This is a simple windows_dll_library rule for building a DLL Windows
that can be depended on by other cc rules.

Example usage:
  windows_dll_library(
      name = "hellolib",
      srcs = [
          "hello-library.cpp",
      ],
      hdrs = ["hello-library.h"],
      # Define COMPILING_DLL to export symbols when compiling the DLL.
      copts = ["/DCOMPILING_DLL"],
  )
"""

load("@rules_cc//cc:defs.bzl", "cc_binary", "cc_import", "cc_library")

def windows_dll_library(
        name,
        srcs = [],
        deps = [],
        hdrs = [],
        visibility = None,
        **kwargs):
    """A simple windows_dll_library rule for building a DLL for Windows."""
    dll_name = name + ".dll"
    import_lib_name = name + "_import_lib"
    import_target_name = name + "_dll_import"

    # Build the shared library
    cc_binary(
        name = dll_name,
        srcs = srcs + hdrs,
        deps = deps,
        linkshared = 1,
        **kwargs
    )

    # Get the import library for the dll
    native.filegroup(
        name = import_lib_name,
        srcs = [":" + dll_name],
        output_group = "interface_library",
    )

    # Because we cannot directly depend on cc_binary from other cc rules in deps attribute,
    # we use cc_import as a bridge to depend on the dll.
    cc_import(
        name = import_target_name,
        interface_library = ":" + import_lib_name,
        shared_library = ":" + dll_name,
    )

    # Create a new cc_library to also include the headers needed for the shared library
    cc_library(
        name = name,
        hdrs = hdrs,
        visibility = visibility,
        deps = deps + [
            ":" + import_target_name,
        ],
    )
