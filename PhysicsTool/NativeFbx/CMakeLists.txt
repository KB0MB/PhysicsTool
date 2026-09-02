cmake_minimum_required(VERSION 3.20)
project(PhysicsToolFbxBridge LANGUAGES CXX)

set(FBX_SDK_ROOT "C:/Program Files/Autodesk/FBX/FBX SDK/2020.3.7" CACHE PATH "Autodesk FBX SDK root")

add_library(PhysicsToolFbxBridge SHARED PhysicsToolFbxBridge.cpp)
target_compile_features(PhysicsToolFbxBridge PRIVATE cxx_std_17)
target_compile_definitions(PhysicsToolFbxBridge PRIVATE LIBXML_STATIC _CRT_SECURE_NO_WARNINGS)
target_include_directories(PhysicsToolFbxBridge PRIVATE "${FBX_SDK_ROOT}/include" "${FBX_SDK_ROOT}/include/libxml2")
set_property(TARGET PhysicsToolFbxBridge PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_link_libraries(PhysicsToolFbxBridge PRIVATE
    "${FBX_SDK_ROOT}/lib/x64/$<IF:$<CONFIG:Debug>,debug,release>/libfbxsdk-mt.lib"
    "${FBX_SDK_ROOT}/lib/x64/$<IF:$<CONFIG:Debug>,debug,release>/libxml2-mt.lib"
    "${FBX_SDK_ROOT}/lib/x64/$<IF:$<CONFIG:Debug>,debug,release>/zlib-mt.lib"
    wininet ws2_32)
set_target_properties(PhysicsToolFbxBridge PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_SOURCE_DIR}"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}")
