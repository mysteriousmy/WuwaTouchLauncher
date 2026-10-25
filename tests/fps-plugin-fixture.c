// Test-owned DLL: observes the original plugin protocol without touching a game.
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _ACRTIMP
#include <windows.h>
static DWORD WINAPI Receive(void *unused) {
    (void)unused;
    static WCHAR output[32768];
    if (!GetEnvironmentVariableW(L"WUWA_FPS_PROBE",output,32768)) return 1;
    HANDLE pipe=CreateNamedPipeW(L"\\\\.\\pipe\\55984705-F24C-45C2-B2B7-27F047B43A56",
        PIPE_ACCESS_INBOUND,PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,1,4096,4096,0,NULL);
    if (pipe==INVALID_HANDLE_VALUE) return 2;
    if (ConnectNamedPipe(pipe,NULL) || GetLastError()==ERROR_PIPE_CONNECTED) {
        char data[64]; DWORD count=0, written=0;
        if (ReadFile(pipe,data,sizeof(data),&count,NULL)) {
            HANDLE file=CreateFileW(output,GENERIC_WRITE,FILE_SHARE_READ,NULL,CREATE_ALWAYS,0,NULL);
            if (file!=INVALID_HANDLE_VALUE) { WriteFile(file,data,count,&written,NULL); CloseHandle(file); }
        }
    }
    CloseHandle(pipe);
    return 0;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,void *reserved) {
    (void)instance; (void)reserved;
    if (reason==DLL_PROCESS_ATTACH) {
        HANDLE thread=CreateThread(NULL,0,Receive,NULL,0,NULL);
        if (!thread) return FALSE;
        CloseHandle(thread);
    }
    return TRUE;
}
