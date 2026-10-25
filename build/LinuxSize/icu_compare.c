/* Compare the ICU operations used by .NET against the original data package. */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <unicode/ucal.h>
#include <unicode/ucol.h>
#include <unicode/udat.h>
#include <unicode/udata.h>
#include <unicode/uldnames.h>
#include <unicode/uloc.h>
#include <unicode/unum.h>
#include <unicode/usearch.h>

static uint64_t digest = UINT64_C(14695981039346656037);
static void record(const void *data, size_t count)
{
    const unsigned char *bytes = data;
    for (size_t i = 0; i < count; i++)
        digest = (digest ^ bytes[i]) * UINT64_C(1099511628211);
}
static void check(UErrorCode status, const char *locale, const char *operation)
{
    if (U_FAILURE(status))
    {
        fprintf(stderr, "%s: %s: %s\n", locale, operation, u_errorName(status));
        exit(1);
    }
    record(&status, sizeof(status));
}
int main(int argc, char **argv)
{
    if (argc != 2) return 2;
    FILE *file = fopen(argv[1], "rb");
    if (!file || fseek(file, 0, SEEK_END)) return 2;
    long size = ftell(file);
    if (size <= 0 || size > 40000000 || fseek(file, 0, SEEK_SET)) return 2;
    void *data = malloc((size_t)size);
    if (!data || fread(data, 1, (size_t)size, file) != (size_t)size) return 2;
    fclose(file);
    UErrorCode status = U_ZERO_ERROR;
    udata_setCommonData(data, &status);
    check(status, "root", "load");
    udata_setFileAccess(UDATA_NO_FILES, &status);
    check(status, "root", "isolate");
    const UChar sample[] = {0x4e2d, 0x6587, 0xe9, 0x20, 0x65, 0x301, 0x20, 0xe44, 0xe17, 0xe22, 0};
    const UChar needle[] = {0x65, 0x301, 0};
    const UChar zone[] = {'A','s','i','a','/','T','o','k','y','o',0};
    const UNumberFormatStyle styles[] = {UNUM_DECIMAL, UNUM_CURRENCY, UNUM_PERCENT, UNUM_SCIENTIFIC};
    int count = uloc_countAvailable();
    for (int i = 0; i < count; i++)
    {
        const char *locale = uloc_getAvailable(i);
        UChar buffer[2048];
        status = U_ZERO_ERROR;
        UDateFormat *date = udat_open(UDAT_FULL, UDAT_FULL, locale, zone, -1, NULL, 0, &status);
        check(status, locale, "date open");
        status = U_ZERO_ERROR;
        int length = udat_format(date, 1771072496000.0, buffer, 2048, NULL, &status);
        check(status, locale, "date format"); record(buffer, (size_t)length * sizeof(UChar));
        udat_close(date);
        for (unsigned n = 0; n < sizeof(styles) / sizeof(styles[0]); n++)
        {
            status = U_ZERO_ERROR;
            UNumberFormat *number = unum_open(styles[n], NULL, 0, locale, NULL, &status);
            check(status, locale, "number open"); status = U_ZERO_ERROR;
            length = unum_formatDouble(number, -123456.75, buffer, 2048, NULL, &status);
            check(status, locale, "number format"); record(buffer, (size_t)length * sizeof(UChar));
            unum_close(number);
        }
        status = U_ZERO_ERROR;
        ULocaleDisplayNames *names = uldn_open(locale, ULDN_STANDARD_NAMES, &status);
        check(status, locale, "display names"); status = U_ZERO_ERROR;
        length = uldn_localeDisplayName(names, "zh_Hant_TW", buffer, 2048, &status);
        check(status, locale, "locale name"); record(buffer, (size_t)length * sizeof(UChar));
        status = U_ZERO_ERROR;
        length = uldn_regionDisplayName(names, "JP", buffer, 2048, &status);
        check(status, locale, "region name"); record(buffer, (size_t)length * sizeof(UChar));
        uldn_close(names);
        status = U_ZERO_ERROR;
        UCollator *collator = ucol_open(locale, &status);
        check(status, locale, "collator");
        uint8_t key[2048];
        length = ucol_getSortKey(collator, sample, -1, key, sizeof(key));
        if (length > (int)sizeof(key)) return 1;
        record(key, (size_t)length);
        status = U_ZERO_ERROR;
        UStringSearch *search = usearch_openFromCollator(needle, -1, sample, -1, collator, NULL, &status);
        check(status, locale, "search"); status = U_ZERO_ERROR;
        int match = usearch_first(search, &status);
        check(status, locale, "match"); record(&match, sizeof(match));
        usearch_close(search); ucol_close(collator);
        printf("%s %016llx\n", locale, (unsigned long long)digest);
    }
    printf("ICU_LOCALES_PASS %d %016llx\n", count, (unsigned long long)digest);
    return 0;
}
