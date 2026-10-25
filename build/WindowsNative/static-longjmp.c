/* StaticLink's PNG objects import longjmp even with a static VCRuntime.
   Supply only the import-address slot, pointing to MSVC libvcruntime's routine.
   This is link-time ABI glue; it does not implement longjmp or any UI behavior. */
#include <setjmp.h>
void (__cdecl *const __imp_longjmp)(jmp_buf, int) = longjmp;
