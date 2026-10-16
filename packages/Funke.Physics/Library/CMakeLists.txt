# Standalone build of the Funke-Physics library with its tests and benchmark.
# The package's native layer compiles the same sources directly; this file is
# only for working on the library itself:
#   cmake -S Library -B build && cmake --build build --config Release
#   ctest --test-dir build -C Release && build/Release/FunkePhysicsBench
cmake_minimum_required(VERSION 3.20)
project(FunkePhysics LANGUAGES CXX)
enable_testing()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

file(GLOB FUNKE_PHYS_SOURCES CONFIGURE_DEPENDS src/*.cpp)
add_library(FunkePhysics STATIC ${FUNKE_PHYS_SOURCES})
target_include_directories(FunkePhysics PUBLIC include)
if(MSVC)
    target_compile_options(FunkePhysics PRIVATE /W4)
else()
    target_compile_options(FunkePhysics PRIVATE -Wall -Wextra)
endif()

file(GLOB FUNKE_PHYS_TESTS CONFIGURE_DEPENDS tests/*.cpp)
add_executable(FunkePhysicsTests ${FUNKE_PHYS_TESTS})
target_link_libraries(FunkePhysicsTests PRIVATE FunkePhysics)
add_test(NAME FunkePhysicsTests COMMAND FunkePhysicsTests)

add_executable(FunkePhysicsBench bench/Bench.cpp)
target_link_libraries(FunkePhysicsBench PRIVATE FunkePhysics)

# Optional: the same benchmark against an older Index-Physics checkout, for before/after numbers.
set(FUNKE_PHYS_BASELINE_DIR "" CACHE PATH "Index-Physics source root to benchmark against")
set(FUNKE_PHYS_BASELINE_GLM "" CACHE PATH "glm include dir the baseline needs")
if(FUNKE_PHYS_BASELINE_DIR)
    file(GLOB FUNKE_PHYS_BASELINE_SOURCES ${FUNKE_PHYS_BASELINE_DIR}/src/*.cpp)
    add_executable(FunkePhysicsBenchBaseline bench/Bench.cpp ${FUNKE_PHYS_BASELINE_SOURCES})
    target_compile_definitions(FunkePhysicsBenchBaseline PRIVATE FUNKE_PHYS_BENCH_BASELINE)
    target_include_directories(FunkePhysicsBenchBaseline PRIVATE ${FUNKE_PHYS_BASELINE_DIR}/include ${FUNKE_PHYS_BASELINE_GLM})
endif()
