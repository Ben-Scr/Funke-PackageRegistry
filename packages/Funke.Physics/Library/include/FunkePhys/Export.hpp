#pragma once

#if (defined(_WIN32) || defined(_WIN64))
#ifdef FUNKE_PHYS_BUILD_DLL
#define FUNKE_PHYS_API __declspec(dllexport)
#elif FUNKE_PHYS_IMPORT_DLL
#define FUNKE_PHYS_API __declspec(dllimport)
#else
#define FUNKE_PHYS_API
#endif
#else
#define FUNKE_PHYS_API
#endif
