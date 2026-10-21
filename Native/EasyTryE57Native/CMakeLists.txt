cmake_minimum_required(VERSION 3.21)

project(EasyTryE57Native LANGUAGES CXX)

find_package(E57Format CONFIG REQUIRED)

add_library(EasyTryE57Native SHARED
    EasyTryE57Native.cpp
)

add_executable(EasyTryE57Reader
    EasyTryE57Reader.cpp
)

target_compile_features(EasyTryE57Native PRIVATE cxx_std_17)
target_compile_definitions(EasyTryE57Native PRIVATE EASYTRY_E57_NATIVE_EXPORTS)
target_link_libraries(EasyTryE57Native PRIVATE E57Format)

target_compile_features(EasyTryE57Reader PRIVATE cxx_std_17)
target_link_libraries(EasyTryE57Reader PRIVATE E57Format)

if(MSVC)
    target_compile_options(EasyTryE57Native PRIVATE /utf-8 /EHsc)
    target_compile_options(EasyTryE57Reader PRIVATE /utf-8 /EHsc)
endif()

set_target_properties(EasyTryE57Native PROPERTIES
    OUTPUT_NAME "EasyTryE57Native"
)
