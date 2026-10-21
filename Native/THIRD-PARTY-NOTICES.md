# EasyTry E57 native dependencies

`EasyTryE57Native.dll` is built from the following open-source projects:

- **libE57Format**, commit `2b84ff0d4ee451d9c191a5eab48a889db8823cc6`  
  Source: <https://github.com/asmaloney/libE57Format>  
  License: Boost Software License 1.0
- **Apache Xerces-C++ 3.3.0**  
  Source: <https://github.com/apache/xerces-c/tree/v3.3.0>  
  License: Apache License 2.0

The reproducible local build entry point is `build-e57-native.ps1`. The
resulting x64 bridge is linked with the static forms of these libraries.
