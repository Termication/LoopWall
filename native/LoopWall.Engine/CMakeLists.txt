cmake_minimum_required(VERSION 3.24)
project(LoopWallEngine VERSION 0.2.0 LANGUAGES CXX)

add_library(LoopWall.Engine SHARED
    src/Engine.cpp
    src/PlaybackEngine.cpp
)

target_include_directories(LoopWall.Engine
    PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_compile_features(LoopWall.Engine PRIVATE cxx_std_20)
target_compile_definitions(LoopWall.Engine PRIVATE LOOPWALL_ENGINE_EXPORTS)

if(MSVC)
    target_compile_options(LoopWall.Engine PRIVATE /W4 /permissive-)
endif()

if(MINGW)
    target_compile_options(LoopWall.Engine PRIVATE -Wall -Wextra)
    target_link_options(LoopWall.Engine PRIVATE -static-libgcc -static-libstdc++ -Wl,-Bstatic -lwinpthread -Wl,-Bdynamic)
endif()

target_link_libraries(LoopWall.Engine PRIVATE
    mfplat
    mf
    mfuuid
    evr
    ole32
    oleaut32
    uuid
)

set_target_properties(LoopWall.Engine PROPERTIES
    PREFIX ""
    OUTPUT_NAME "LoopWall.Engine"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/bin"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_SOURCE_DIR}/bin"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}/bin"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/bin"
)
