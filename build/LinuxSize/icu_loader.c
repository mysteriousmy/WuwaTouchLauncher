/* ICU retains this immutable allocation for the lifetime of the process. */
#include <pthread.h>
#include <stdint.h>
#include <stdlib.h>
#include <sys/prctl.h>
#include <unicode/udata.h>
#define BrotliDecoderDecompress wuwa_fc_BrotliDecoderDecompress
#include <brotli/decode.h>

extern const unsigned char wuwa_icu_brotli_begin[];
extern const unsigned char wuwa_icu_brotli_end[];
extern int32_t __real_GlobalizationNative_LoadICU(void);

static pthread_once_t initialize = PTHREAD_ONCE_INIT;
static int initialized;

/* Run before NativeAOT initializes its GC. Applying this in managed Main is
 * too late for the heap's initial huge pages; no system-wide policy is changed. */
__attribute__((constructor))
static void configure_process_memory_pages(void)
{
    (void)prctl(PR_SET_THP_DISABLE, 1UL, 0UL, 0UL, 0UL);
}

static void load_embedded_data(void)
{
    size_t decoded_size = WUWA_ICU_DATA_SIZE;
    void *data = malloc(decoded_size);
    if (!data)
        return;
    size_t encoded_size = (size_t)(wuwa_icu_brotli_end - wuwa_icu_brotli_begin);
    if (BrotliDecoderDecompress(encoded_size, wuwa_icu_brotli_begin,
                               &decoded_size, data) != BROTLI_DECODER_RESULT_SUCCESS ||
        decoded_size != WUWA_ICU_DATA_SIZE)
    {
        free(data);
        return;
    }
    UErrorCode status = U_ZERO_ERROR;
    udata_setCommonData(data, &status);
    if (U_FAILURE(status))
    {
        free(data);
        return;
    }
    /* Do not free after initialization: ICU stores pointers into this buffer. */
    initialized = 1;
}

int32_t __wrap_GlobalizationNative_LoadICU(void)
{
    if (pthread_once(&initialize, load_embedded_data) != 0 || !initialized)
        return 0;
    return __real_GlobalizationNative_LoadICU();
}
