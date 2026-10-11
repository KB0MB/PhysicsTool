cmake_minimum_required(VERSION 3.18)
project(PhysicsToolMeshCodec LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
file(GLOB MESH_SOURCES CONFIGURE_DEPENDS Upstream/src/*.cpp)
set(ZSTD Upstream/lib/zstd/lib)
add_executable(PhysicsTool.MeshCodec Native/main.cpp ${MESH_SOURCES}
    ${ZSTD}/common/debug.c ${ZSTD}/common/entropy_common.c ${ZSTD}/common/error_private.c
    ${ZSTD}/common/fse_decompress.c ${ZSTD}/common/xxhash.c ${ZSTD}/common/zstd_common.c
    ${ZSTD}/decompress/huf_decompress.c ${ZSTD}/decompress/zstd_ddict.c
    ${ZSTD}/decompress/zstd_decompress.c ${ZSTD}/decompress/zstd_decompress_block.c)
target_include_directories(PhysicsTool.MeshCodec PRIVATE Upstream/src Upstream/src/include Upstream/lib/float16
    ${ZSTD} ${ZSTD}/common ${ZSTD}/decompress)
target_compile_definitions(PhysicsTool.MeshCodec PRIVATE ZSTD_DISABLE_ASM _CRT_SECURE_NO_WARNINGS)
install(TARGETS PhysicsTool.MeshCodec RUNTIME DESTINATION .)
