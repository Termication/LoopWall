cmake_minimum_required(VERSION 3.24)
project(LoopWallEngine VERSION 0.2.0 LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "LoopWall.Engine uses Windows Media Foundation and can only be built on Windows.")
endif()

add_library(LoopWall.Engine SHARED
    src/Engine.cpp
)

target_include_directories(LoopWall.Engine
    PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_compile_features(LoopWall.Engine PRIVATE cxx_std_20)
target_compile_definitions(LoopWall.Engine PRIVATE WIN32_LEAN_AND_MEAN LOOPWALL_ENGINE_EXPORTS)
target_link_libraries(LoopWall.Engine PRIVATE mfplat mfreadwrite mfuuid ole32)

set_target_properties(LoopWall.Engine PROPERTIES
    OUTPUT_NAME "LoopWall.Engine"
    WINDOWS_EXPORT_ALL_SYMBOLS OFF
)

if(MSVC)
    target_compile_options(LoopWall.Engine PRIVATE /W4 /permissive- /EHsc)
endif()
