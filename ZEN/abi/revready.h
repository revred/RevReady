#pragma once
#include <stddef.h>
#include <stdint.h>
#ifdef _WIN32
  #ifdef REVEXPORTS
    #define REV_API __declspec(dllexport)
  #else
    #define REV_API __declspec(dllimport)
  #endif
#else
  #define REV_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
#define REVREADY_API_VERSION 0x00010000u
typedef struct { void* ptr; uint32_t len; } rev_span_t;
typedef struct { rev_span_t in; uint32_t algo; uint32_t level; rev_span_t out; uint32_t flags; } rev_desc_t;
typedef struct { uint32_t count; rev_desc_t* descs; } rev_batch_t;
typedef struct { uint32_t code; uint32_t bytes; } rev_result_t;
REV_API rev_result_t rev_reduce_batch(rev_batch_t* batch);
REV_API rev_result_t rev_inflate_batch(rev_batch_t* batch);
REV_API void         rev_free(void* p);
REV_API const char*  rev_last_error(void);
#ifdef __cplusplus
}
#endif
