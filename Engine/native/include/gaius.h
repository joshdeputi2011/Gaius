#pragma once
#include <stddef.h>
#if defined(_WIN32) && defined(GAIUS_ENGINE_SHARED)
#ifdef GAIUS_ENGINE_BUILD
#define GAIUS_API __declspec(dllexport)
#else
#define GAIUS_API __declspec(dllimport)
#endif
#elif defined(_WIN32)
#define GAIUS_API
#else
#define GAIUS_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
/* UTF-8 input/output. Returns bytes needed including NUL; output is always
 * terminated when capacity > 0. No exceptions cross the ABI. Thread safe. */
GAIUS_API size_t gaius_calculate(const char* expression, const char* cells_json, char* output, size_t capacity);
GAIUS_API const char* gaius_version(void);
#ifdef __cplusplus
}
#endif
