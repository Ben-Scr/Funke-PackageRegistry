#pragma once
#if defined(_WIN32)
#define FUNKE_PACKAGE_API __declspec(dllexport)
#else
#define FUNKE_PACKAGE_API __attribute__((visibility("default")))
#endif
