#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _ACRTIMP
#include <windows.h>
void mainCRTStartup(void) {
    static WCHAR output[32768];
    if (GetEnvironmentVariableW(L"WUWA_CHILD_STARTED",output,32768)) {
        HANDLE file=CreateFileW(output,GENERIC_WRITE,FILE_SHARE_READ,NULL,CREATE_ALWAYS,0,NULL);
        if (file!=INVALID_HANDLE_VALUE) CloseHandle(file);
    }
    // Give the helper time to deliver a request; never show a window or read input.
    Sleep(3000);
    ExitProcess(0);
}
