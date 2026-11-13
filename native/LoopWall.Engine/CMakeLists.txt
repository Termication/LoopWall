cmake_minimum_required(VERSION 3.24)
project(LoopWallEngine VERSION 0.1.0 LANGUAGES CXX)

add_library(LoopWall.Engine SHARED
    src/Engine.cpp
)

target_include_directories(LoopWall.Engine
    PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
)

target_compile_features(LoopWall.Engine PRIVATE cxx_std_20)
target_compile_definitions(LoopWall.Engine PRIVATE LOOPWALL_ENGINE_EXPORTS)

if(MSVC)
    target_compile_options(LoopWall.Engine PRIVATE /W4 /permissive-)
endif()
