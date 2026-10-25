// A small, runtime-free Wine/Win32 bridge for the Native AOT launcher.
// The mouse callback runs here. Optional FPS support loads the user-supplied
// original launcher's plugin only into the child this helper just created.
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE
#define _ACRTIMP
#include <windows.h>
#include <shellapi.h>
#include <tlhelp32.h>
#include <shobjidl.h>

#define MAX_TARGETS 64
#define MAX_WINDOWS 256
#ifndef MOD_NOREPEAT
#define MOD_NOREPEAT 0x4000
#endif
static const WCHAR tabletProperty[] = L"MicrosoftTabletPenServiceProperty";
static WCHAR gamePath[32768], statusPath[32768], stopPath[32768], tempPath[32768];
static WCHAR commandLine[32768], workingDirectory[32768];
static WCHAR fpsPluginPath[32768];
static UINT fpsTarget;
static DWORD fpsProcessId, fpsError;
static ULONGLONG fpsDeadline;
static const char *fpsState="disabled";
static DWORD targetPids[MAX_TARGETS], targetCount, blockedButtons;
static DWORD blockedMouse, passedMouse, configuredWindows, errorCode, exitCode;
static HHOOK mouseHook;
static BOOL filtering, everSeen, selfTest, hotkeyRegistered;
static BOOL allowFilter=TRUE, startFilter=TRUE, enableHotkey=TRUE;
static UINT hotkeyModifiers=MOD_CONTROL | MOD_ALT, hotkeyKey=VK_F10;
static ULONGLONG startTime, lastSeen;
static const char *phase = "starting";
typedef struct { HWND hwnd; DWORD pid; HANDLE original; HANDLE applied; } WindowState;
static WindowState windows[MAX_WINDOWS];
static DWORD windowCount;
static HANDLE instanceMutex;
static BOOL selfTestPassed;
static DWORD probeMouse, probeKeys, probeStage, probeBaseline, probeBlocked;
static HWND probeWindow;

// The compiler may emit these even with no C runtime.
#if defined(_MSC_VER) && !defined(__clang__)
#pragma function(memset, memcpy)
#endif
void *memset(void *p, int v, size_t n) { unsigned char *d=p; while(n--) *d++=(unsigned char)v; return p; }
void *memcpy(void *p, const void *s, size_t n) { unsigned char *d=p; const unsigned char *q=s; while(n--) *d++=*q++; return p; }

#include "memory-graphics.h"

static BOOL IsTarget(DWORD pid) {
    for (DWORD i=0; i<targetCount; i++) if (pid == targetPids[i]) return TRUE;
    return FALSE;
}

static DWORD ButtonBit(WPARAM message, const MSLLHOOKSTRUCT *data, BOOL *up) {
    *up=FALSE;
    switch(message) {
    case WM_LBUTTONUP: *up=TRUE; /* fall through */
    case WM_LBUTTONDOWN: return 1;
    case WM_RBUTTONUP: *up=TRUE; /* fall through */
    case WM_RBUTTONDOWN: return 2;
    case WM_MBUTTONUP: *up=TRUE; /* fall through */
    case WM_MBUTTONDOWN: return 4;
    case WM_XBUTTONUP: *up=TRUE; /* fall through */
    case WM_XBUTTONDOWN: return HIWORD(data->mouseData)==XBUTTON1 ? 8 : 16;
    default: return 0;
    }
}

static LRESULT CALLBACK MouseProc(int code, WPARAM message, LPARAM pointer) {
    if (code == HC_ACTION && filtering && message >= WM_MOUSEMOVE && message <= WM_MOUSEHWHEEL) {
        DWORD pid=0;
        GetWindowThreadProcessId(GetForegroundWindow(), &pid);
        BOOL up=FALSE;
        DWORD bit=ButtonBit(message, (const MSLLHOOKSTRUCT*)pointer, &up);
        BOOL block=IsTarget(pid) || (up && (blockedButtons & bit));
        if (block) {
            if (bit) { if (up) blockedButtons &= ~bit; else blockedButtons |= bit; }
            blockedMouse++;
            return 1;
        }
        passedMouse++;
    }
    return CallNextHookEx(mouseHook, code, message, pointer);
}

static BOOL CALLBACK ConfigureWindow(HWND hwnd, LPARAM unused) {
    (void)unused;
    DWORD pid=0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (!IsTarget(pid)) return TRUE;
    for (DWORD i=0; i<windowCount; i++) if (windows[i].hwnd==hwnd && windows[i].pid==pid) return TRUE;
    if (windowCount==MAX_WINDOWS) return TRUE;
    HANDLE original=GetPropW(hwnd, tabletProperty);
    HANDLE applied=(HANDLE)((ULONG_PTR)original | 1); // TABLET_DISABLE_PRESSANDHOLD
    if (SetPropW(hwnd, tabletProperty, applied)) {
        windows[windowCount++]=(WindowState){hwnd,pid,original,applied};
        configuredWindows++;
    }
    return TRUE;
}

static void RestoreWindows(void) {
    for (DWORD i=0; i<windowCount; i++) {
        WindowState *w=&windows[i];
        DWORD pid=0;
        GetWindowThreadProcessId(w->hwnd, &pid);
        if (pid!=w->pid || GetPropW(w->hwnd, tabletProperty)!=w->applied) continue;
        if (w->original) SetPropW(w->hwnd, tabletProperty, w->original);
        else RemovePropW(w->hwnd, tabletProperty);
    }
    windowCount=0;
}

static void ReleaseFilter(void) {
    filtering=FALSE;
    if (mouseHook) { UnhookWindowsHookEx(mouseHook); mouseHook=NULL; }
    blockedButtons=0;
    RestoreWindows();
    phase="released";
}

static BOOL InstallFilter(void) {
    if (!allowFilter) return FALSE;
    if (filtering) return TRUE;
    mouseHook=SetWindowsHookExW(WH_MOUSE_LL, MouseProc, GetModuleHandleW(NULL), 0);
    if (!mouseHook) { errorCode=GetLastError(); phase="hook-error"; return FALSE; }
    filtering=TRUE;
    errorCode=0;
    phase="active";
    return TRUE;
}

static void ToggleFilter(void) {
    if (!allowFilter) return;
    if (filtering) ReleaseFilter();
    else if (!InstallFilter()) phase="released"; // A retry failure must not end an existing game session.
}

static BOOL ParseNumber(const WCHAR *text, UINT *value) {
    UINT n=0;
    if (!*text) return FALSE;
    while (*text) {
        if (*text<L'0' || *text>L'9' || n>65535) return FALSE;
        n=n*10+(UINT)(*text++-L'0');
    }
    *value=n;
    return TRUE;
}

// Use the OS shell API in native C so the managed application needs no dynamic COM support.
static DWORD CreateShortcut(int argc, WCHAR **argv) {
    if (argc!=6) return 2; // --create-shortcut LINK TARGET ARGUMENTS WORKING_DIRECTORY
    HRESULT init=CoInitializeEx(NULL,COINIT_APARTMENTTHREADED);
    if (FAILED(init)) return 30;
    const GUID clsid={0x00021401,0,0,{0xc0,0,0,0,0,0,0,0x46}};
    const GUID iid={0x000214f9,0,0,{0xc0,0,0,0,0,0,0,0x46}};
    const GUID persistIid={0x0000010b,0,0,{0xc0,0,0,0,0,0,0,0x46}};
    IShellLinkW *link=NULL;
    IPersistFile *persist=NULL;
    HRESULT hr=CoCreateInstance(&clsid,NULL,CLSCTX_INPROC_SERVER,&iid,(void**)&link);
    if (SUCCEEDED(hr)) hr=link->lpVtbl->SetPath(link,argv[3]);
    if (SUCCEEDED(hr)) hr=link->lpVtbl->SetArguments(link,argv[4]);
    if (SUCCEEDED(hr)) hr=link->lpVtbl->SetWorkingDirectory(link,argv[5]);
    if (SUCCEEDED(hr)) hr=link->lpVtbl->SetDescription(link,L"Wuthering Waves quick launch");
    if (SUCCEEDED(hr)) hr=link->lpVtbl->QueryInterface(link,&persistIid,(void**)&persist);
    if (SUCCEEDED(hr)) hr=persist->lpVtbl->Save(persist,argv[2],TRUE);
    if (persist) persist->lpVtbl->Release(persist);
    if (link) link->lpVtbl->Release(link);
    CoUninitialize();
    return SUCCEEDED(hr)?0:31;
}
static void WriteStatus(void) {
    if (!*statusPath) return;
    char json[1024];
    int length=wsprintfA(json,
        "{\"version\":1,\"phase\":\"%s\",\"filtering\":%s,\"bridgePid\":%lu,"
        "\"targetCount\":%lu,\"blockedMouse\":%lu,\"passedMouse\":%lu,\"configuredWindows\":%lu,"
        "\"emergencyHotkey\":%s,\"win32Error\":%lu,\"elapsedMs\":%lu,\"exitCode\":%lu,"
        "\"selfTestPassed\":%s,\"probeMouse\":%lu,\"probeKeys\":%lu,"
        "\"fpsUnlockTarget\":%u,\"fpsUnlockState\":\"%s\",\"fpsUnlockError\":%lu,"
        "\"memoryGraphicsState\":\"%s\",\"memoryGraphicsCount\":%lu,\"memoryGraphicsApplied\":%lu,"
        "\"memoryGraphicsWrites\":%lu,\"memoryGraphicsError\":%lu,\"memoryFrameGenerationState\":\"%s\","
        "\"memoryGraphicsSkipped\":%lu}\n",
        phase,filtering?"true":"false",GetCurrentProcessId(),targetCount,blockedMouse,passedMouse,
        configuredWindows,hotkeyRegistered?"true":"false",errorCode,(DWORD)(GetTickCount64()-startTime),
        exitCode,selfTestPassed?"true":"false",probeMouse,probeKeys,fpsTarget,fpsState,fpsError,
        graphicsState,graphicsCount,graphicsApplied,graphicsWrites,graphicsError,graphicsFrameGenerationState,graphicsSkipped);
    HANDLE file=CreateFileW(tempPath, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file==INVALID_HANDLE_VALUE) return;
    DWORD written;
    BOOL ok=WriteFile(file,json,(DWORD)length,&written,NULL);
    CloseHandle(file);
    if (ok && written==(DWORD)length) MoveFileExW(tempPath,statusPath,MOVEFILE_REPLACE_EXISTING);
}

static void RefreshTargets(void) {
    targetCount=0;
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS,0);
    if (snapshot==INVALID_HANDLE_VALUE) return;
    PROCESSENTRY32W entry={0};
    entry.dwSize=sizeof(entry);
    if (Process32FirstW(snapshot,&entry)) do {
        if (entry.th32ProcessID==GetCurrentProcessId()) continue;
        HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,entry.th32ProcessID);
        if (!process) continue;
        static WCHAR path[32768];
        DWORD length=32768;
        if (QueryFullProcessImageNameW(process,0,path,&length) &&
            lstrcmpiW(path,gamePath)==0 && targetCount<MAX_TARGETS)
            targetPids[targetCount++]=entry.th32ProcessID;
        CloseHandle(process);
    } while (Process32NextW(snapshot,&entry));
    CloseHandle(snapshot);
}

// Quote each argv element using the Windows CommandLineToArgvW/CRT convention.
static BOOL AddQuoted(const WCHAR *arg, DWORD *position) {
    DWORD p=*position;
    if (p && p<32760) commandLine[p++]=L' ';
    if (p>=32760) return FALSE;
    commandLine[p++]=L'"';
    while (*arg) {
        DWORD slashes=0;
        while (*arg==L'\\') { slashes++; arg++; }
        DWORD count=(*arg==L'"' || !*arg) ? slashes*2 : slashes;
        if (p+count+3>=32768) return FALSE;
        while(count--) commandLine[p++]=L'\\';
        if (*arg==L'"') commandLine[p++]=L'\\';
        if (*arg) commandLine[p++]=*arg++;
    }
    commandLine[p++]=L'"';
    commandLine[p]=0;
    *position=p;
    return TRUE;
}

static BOOL LoadFpsPlugin(HANDLE process) {
    fpsState="loading";
    SIZE_T size=((SIZE_T)lstrlenW(fpsPluginPath)+1)*sizeof(WCHAR), written=0;
    void *remote=VirtualAllocEx(process,NULL,size,MEM_COMMIT | MEM_RESERVE,PAGE_READWRITE);
    if (!remote) { fpsError=GetLastError(); return FALSE; }
    BOOL ok=WriteProcessMemory(process,remote,fpsPluginPath,size,&written) && written==size;
    HANDLE thread=NULL;
    DWORD waited=WAIT_FAILED, loaded=0;
    if (ok) {
        FARPROC load=GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"LoadLibraryW");
        if (load) thread=CreateRemoteThread(process,NULL,0,(LPTHREAD_START_ROUTINE)load,remote,0,NULL);
        ok=thread!=NULL;
    }
    if (ok) {
        waited=WaitForSingleObject(thread,10000);
        ok=waited==WAIT_OBJECT_0 && GetExitCodeThread(thread,&loaded) && loaded!=0;
    }
    if (!ok) {
        fpsError=waited==WAIT_TIMEOUT ? ERROR_TIMEOUT : GetLastError();
        if (!fpsError) fpsError=ERROR_DLL_INIT_FAILED;
    }
    // A timed-out loader might still read its argument. The caller terminates
    // this newly created suspended child, which releases the allocation.
    if (!thread || waited==WAIT_OBJECT_0) VirtualFreeEx(process,remote,0,MEM_RELEASE);
    if (thread) CloseHandle(thread);
    fpsState=ok ? "waiting-for-plugin" : "failed";
    return ok;
}

static void SendFpsRequest(void) {
    if (!fpsTarget || lstrcmpA(fpsState,"waiting-for-plugin")!=0) return;
    if (GetTickCount64()>fpsDeadline) {
        fpsState="failed"; fpsError=ERROR_TIMEOUT; return;
    }
    HANDLE pipe=CreateFileW(L"\\\\.\\pipe\\55984705-F24C-45C2-B2B7-27F047B43A56",
        GENERIC_WRITE,0,NULL,OPEN_EXISTING,0,NULL);
    if (pipe==INVALID_HANDLE_VALUE) return;
    ULONG server=0;
    // The original plugin uses one fixed name. Never send to another game.
    if (!GetNamedPipeServerProcessId(pipe,&server) || server!=fpsProcessId) {
        CloseHandle(pipe); fpsState="failed"; fpsError=ERROR_INVALID_OWNER; return;
    }
    char request[64];
    DWORD size=(DWORD)wsprintfA(request,"0,0,45.0,0,0,%u",fpsTarget), written=0;
    if (WriteFile(pipe,request,size,&written,NULL) && written==size) fpsState="requested";
    else { fpsState="failed"; fpsError=GetLastError(); }
    CloseHandle(pipe);
}

static BOOL LaunchGame(int argc, WCHAR **argv, int firstArgument) {
    DWORD p=0;
    if (!AddQuoted(gamePath,&p)) return FALSE;
    for (int i=firstArgument;i<argc;i++) if (!AddQuoted(argv[i],&p)) { errorCode=ERROR_BUFFER_OVERFLOW; return FALSE; }
    lstrcpyW(workingDirectory,gamePath);
    for (int i=lstrlenW(workingDirectory)-1;i>=0;i--)
        if (workingDirectory[i]==L'\\') { workingDirectory[i]=0; break; }
    STARTUPINFOW startup={0};
    PROCESS_INFORMATION process={0};
    startup.cb=sizeof(startup);
    if (!CreateProcessW(gamePath,commandLine,NULL,NULL,FALSE,CREATE_SUSPENDED,NULL,workingDirectory,&startup,&process)) {
        errorCode=GetLastError(); return FALSE;
    }
    targetPids[targetCount++]=process.dwProcessId;
    if (fpsTarget && !LoadFpsPlugin(process.hProcess)) {
        errorCode=fpsError;
        TerminateProcess(process.hProcess,errorCode);
        WaitForSingleObject(process.hProcess,2000);
        CloseHandle(process.hThread); CloseHandle(process.hProcess); return FALSE;
    }
    // Install after the optional loader wait, so the OS never times out the
    // low-level callback while this thread waits for plugin initialization.
    if (allowFilter && startFilter && !InstallFilter()) {
        TerminateProcess(process.hProcess,errorCode);
        CloseHandle(process.hThread); CloseHandle(process.hProcess); return FALSE;
    }
    if (ResumeThread(process.hThread)==(DWORD)-1) {
        errorCode=GetLastError();
        // Only our newly created, still-suspended child is terminated on failure.
        TerminateProcess(process.hProcess,errorCode);
        CloseHandle(process.hThread); CloseHandle(process.hProcess); return FALSE;
    }
    if (fpsTarget) { fpsProcessId=process.dwProcessId; fpsDeadline=GetTickCount64()+30000; }
    GraphicsAttach(process.hProcess,process.dwProcessId);
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
    return TRUE;
}

static LRESULT CALLBACK ProbeProc(HWND hwnd, UINT message, WPARAM w, LPARAM l) {
    if (message==WM_LBUTTONDOWN) probeMouse++;
    if (message==WM_KEYDOWN && w==L'A') probeKeys++;
    return DefWindowProcW(hwnd,message,w,l);
}

static void ProbeInput(void) {
    INPUT input[4]={0};
    input[0].type=INPUT_MOUSE; input[0].mi.dwFlags=MOUSEEVENTF_LEFTDOWN;
    input[1].type=INPUT_MOUSE; input[1].mi.dwFlags=MOUSEEVENTF_LEFTUP;
    input[2].type=INPUT_KEYBOARD; input[2].ki.wVk=L'A';
    input[3].type=INPUT_KEYBOARD; input[3].ki.wVk=L'A'; input[3].ki.dwFlags=KEYEVENTF_KEYUP;
    if (SendInput(4,input,sizeof(INPUT))!=4) { errorCode=GetLastError(); exitCode=21; }
}

static void ProbeHotkey(void) {
    INPUT input[10]={0};
    const UINT masks[4]={MOD_CONTROL,MOD_ALT,MOD_SHIFT,MOD_WIN};
    const WORD keys[4]={VK_CONTROL,VK_MENU,VK_SHIFT,VK_LWIN};
    UINT count=0;
    for (UINT i=0;i<4;i++) if (hotkeyModifiers & masks[i]) {
        input[count].type=INPUT_KEYBOARD; input[count++].ki.wVk=keys[i];
    }
    input[count].type=INPUT_KEYBOARD; input[count++].ki.wVk=(WORD)hotkeyKey;
    input[count].type=INPUT_KEYBOARD; input[count].ki.wVk=(WORD)hotkeyKey;
    input[count++].ki.dwFlags=KEYEVENTF_KEYUP;
    for (UINT i=4;i>0;i--) if (hotkeyModifiers & masks[i-1]) {
        input[count].type=INPUT_KEYBOARD; input[count].ki.wVk=keys[i-1];
        input[count++].ki.dwFlags=KEYEVENTF_KEYUP;
    }
    if (SendInput(count,input,sizeof(INPUT))!=count) { errorCode=GetLastError(); exitCode=29; }
}

static void ProbeTick(void) {
    switch (probeStage++) {
    case 0:
        // Baseline: a non-target foreground process must keep mouse and keyboard.
        SetForegroundWindow(probeWindow); SetFocus(probeWindow);
        SetCursorPos(200,160);
        ProbeInput(); break;
    case 1:
        if (!probeMouse || !probeKeys) { exitCode=22; break; }
        probeBaseline=probeMouse;
        targetPids[0]=GetCurrentProcessId(); targetCount=1;
        SetPropW(probeWindow,tabletProperty,(HANDLE)8);
        EnumWindows(ConfigureWindow,0);
        if (GetPropW(probeWindow,tabletProperty)!=(HANDLE)9) { exitCode=23; break; }
        ProbeInput(); break;
    case 2:
        if (probeMouse!=probeBaseline || probeKeys<2 || blockedMouse<2) { exitCode=24; break; }
        probeBlocked=blockedMouse;
        ReleaseFilter();
        if (GetPropW(probeWindow,tabletProperty)!=(HANDLE)8) { exitCode=25; break; }
        ProbeInput(); break;
    case 3:
        if (probeMouse<=probeBaseline || probeKeys<3 || blockedMouse!=probeBlocked) { exitCode=26; break; }
        // Exercise the configured chord through actual input, not a direct call to ToggleFilter.
        if (!hotkeyRegistered) { exitCode=27; break; }
        ProbeHotkey(); break;
    case 4:
        EnumWindows(ConfigureWindow,0);
        if (!filtering || GetPropW(probeWindow,tabletProperty)!=(HANDLE)9) { exitCode=27; break; }
        ProbeInput(); break;
    case 5:
        if (probeMouse!=probeBaseline+1 || blockedMouse<=probeBlocked || probeKeys<4) { exitCode=28; break; }
        ProbeHotkey(); break;
    case 6:
        if (filtering || GetPropW(probeWindow,tabletProperty)!=(HANDLE)8) { exitCode=28; break; }
        selfTestPassed=TRUE;
        phase="self-test-passed"; PostQuitMessage(0); break;
    }
    if (exitCode) { phase="self-test-failed"; PostQuitMessage((int)exitCode); }
    WriteStatus();
}

static BOOL BeginProbe(void) {
    WNDCLASSW wc={0};
    wc.lpfnWndProc=ProbeProc; wc.hInstance=GetModuleHandleW(NULL); wc.lpszClassName=L"WuwaTouchProbe";
    if (!RegisterClassW(&wc)) return FALSE;
    probeWindow=CreateWindowExW(0,wc.lpszClassName,L"Wuwa touch isolated input probe",WS_OVERLAPPEDWINDOW | WS_VISIBLE,
        100,100,500,300,NULL,NULL,wc.hInstance,NULL);
    if (!probeWindow) return FALSE;
    ShowWindow(probeWindow,SW_SHOW); UpdateWindow(probeWindow);
    return InstallFilter();
}

void mainCRTStartup(void) {
    startTime=GetTickCount64(); lastSeen=startTime;
    int argc=0, firstArgument=0;
    WCHAR **argv=CommandLineToArgvW(GetCommandLineW(),&argc);
    if (!argv) ExitProcess(2);
    if (argc>1 && lstrcmpW(argv[1],L"--create-shortcut")==0) {
        DWORD result=CreateShortcut(argc,argv);
        LocalFree(argv); ExitProcess(result);
    }
    for (int i=1;i<argc;i++) {
        if (lstrcmpW(argv[i],L"--self-test")==0) { selfTest=TRUE; continue; }
        if (lstrcmpW(argv[i],L"--")==0) { firstArgument=i+1; break; }
        if (i+1>=argc || lstrlenW(argv[i+1])>=32760) ExitProcess(2);
        if (lstrcmpW(argv[i],L"--game")==0) lstrcpyW(gamePath,argv[++i]);
        else if (lstrcmpW(argv[i],L"--memory-graphics")==0) {
            const WCHAR *value=argv[++i];
            if (lstrcmpW(value,L"1")!=0 && lstrcmpW(value,L"0")!=0) ExitProcess(2);
            graphicsRequested=lstrcmpW(value,L"1")==0;
        }
        else if (lstrcmpW(argv[i],L"--graphics-set")==0) {
            if (!GraphicsSetOption(argv[++i])) ExitProcess(2);
        }
        else if (lstrcmpW(argv[i],L"--graphics-unavailable")==0) {
            if (!GraphicsSetUnavailable(argv[++i])) ExitProcess(2);
        }
        else if (lstrcmpW(argv[i],L"--fps-plugin")==0) lstrcpyW(fpsPluginPath,argv[++i]);
        else if (lstrcmpW(argv[i],L"--fps-target")==0) {
            if (fpsTarget || !ParseNumber(argv[++i],&fpsTarget) ||
                (fpsTarget!=120 && fpsTarget!=240 && fpsTarget!=420)) ExitProcess(2);
        }
        else if (lstrcmpW(argv[i],L"--status")==0) lstrcpyW(statusPath,argv[++i]);
        else if (lstrcmpW(argv[i],L"--stop")==0) lstrcpyW(stopPath,argv[++i]);
        else if (lstrcmpW(argv[i],L"--allow-filter")==0) allowFilter=lstrcmpW(argv[++i],L"1")==0;
        else if (lstrcmpW(argv[i],L"--filter")==0) startFilter=lstrcmpW(argv[++i],L"1")==0;
        else if (lstrcmpW(argv[i],L"--hotkey-enabled")==0) enableHotkey=lstrcmpW(argv[++i],L"1")==0;
        else if (lstrcmpW(argv[i],L"--hotkey-modifiers")==0) {
            if (!ParseNumber(argv[++i],&hotkeyModifiers) || hotkeyModifiers>15) ExitProcess(2);
        }
        else if (lstrcmpW(argv[i],L"--hotkey-key")==0) {
            if (!ParseNumber(argv[++i],&hotkeyKey) || hotkeyKey<1 || hotkeyKey>254) ExitProcess(2);
        }
        else ExitProcess(2);
    }
    if (!*statusPath || (!selfTest && (!*gamePath || !*stopPath || !firstArgument))) ExitProcess(2);
    if ((fpsTarget && (!*fpsPluginPath || selfTest)) || (!fpsTarget && *fpsPluginPath)) ExitProcess(2);
    if (((graphicsCount || graphicsSkipped) && !graphicsRequested) || (graphicsRequested && selfTest)) ExitProcess(2);
    if (graphicsRequested) {
        BOOL cloud=FALSE;
        for (int i=firstArgument;i<argc;i++) if (lstrcmpiW(argv[i],L"-CloudGame")==0) cloud=TRUE;
        if (!cloud) ExitProcess(2);
    }
    lstrcpyW(tempPath,statusPath); lstrcatW(tempPath,L".tmp");
    if (allowFilter && enableHotkey)
        hotkeyRegistered=RegisterHotKey(NULL,1,hotkeyModifiers | MOD_NOREPEAT,hotkeyKey);
    if (selfTest) {
        if (!BeginProbe()) { errorCode=GetLastError(); exitCode=20; phase="self-test-error"; WriteStatus(); ExitProcess(exitCode); }
    } else {
        // One helper per exact game path in this Wine session.
        DWORD hash=2166136261u;
        for (WCHAR *s=gamePath;*s;s++) { WCHAR c=*s; if(c>=L'A'&&c<=L'Z') c+=32; hash=(hash^c)*16777619u; }
        WCHAR mutexName[80]; wsprintfW(mutexName,L"Local\\WuwaTouchBridge-%08lx",hash);
        instanceMutex=CreateMutexW(NULL,FALSE,mutexName);
        if (!instanceMutex || GetLastError()==ERROR_ALREADY_EXISTS) {
            phase="already-running"; exitCode=3; WriteStatus(); ExitProcess(exitCode);
        }
        RefreshTargets();
        if (targetCount) { phase="already-running"; exitCode=3; WriteStatus(); ExitProcess(exitCode); }
        GraphicsPrepare(gamePath);
        if (!LaunchGame(argc,argv,firstArgument)) {
            GraphicsClose();
            ReleaseFilter(); phase=fpsError ? "fps-error" : "launch-error"; exitCode=5; WriteStatus(); ExitProcess(exitCode);
        }
        everSeen=targetCount>0; phase="active";
    }
    LocalFree(argv);
    UINT_PTR timer=SetTimer(NULL,0,500,NULL);
    if (!timer) { ReleaseFilter(); phase="timer-error"; errorCode=GetLastError(); WriteStatus(); ExitProcess(6); }
    WriteStatus();
    MSG message;
    while (GetMessageW(&message,NULL,0,0)>0) {
        if (message.message==WM_HOTKEY && message.wParam==1) { ToggleFilter(); WriteStatus(); }
        if (message.message==WM_TIMER && message.wParam==timer) {
            if (selfTest) ProbeTick();
            else {
                if (GetFileAttributesW(stopPath)!=INVALID_FILE_ATTRIBUTES) {
                    char request[16]={0}; DWORD count=0;
                    HANDLE file=CreateFileW(stopPath,GENERIC_READ,FILE_SHARE_READ | FILE_SHARE_WRITE,NULL,OPEN_EXISTING,0,NULL);
                    if (file!=INVALID_HANDLE_VALUE) { ReadFile(file,request,15,&count,NULL); CloseHandle(file); }
                    DeleteFileW(stopPath);
                    if (count>=6 && request[0]=='t' && request[1]=='o' && request[2]=='g' && request[3]=='g' && request[4]=='l' && request[5]=='e') ToggleFilter();
                    else ReleaseFilter();
                }
                RefreshTargets();
                SendFpsRequest();
                GraphicsTick();
                ULONGLONG now=GetTickCount64();
                if (targetCount) {
                    everSeen=TRUE; lastSeen=now;
                    if (filtering) EnumWindows(ConfigureWindow,0);
                } else if ((everSeen && now-lastSeen>10000) || (!everSeen && now-startTime>120000)) {
                    ReleaseFilter(); phase="game-exited"; PostQuitMessage(0);
                }
                WriteStatus();
            }
        }
        TranslateMessage(&message); DispatchMessageW(&message);
    }
    KillTimer(NULL,timer);
    const char *finalPhase=phase;
    ReleaseFilter(); phase=finalPhase;
    if (hotkeyRegistered) { UnregisterHotKey(NULL,1); hotkeyRegistered=FALSE; }
    WriteStatus();
    GraphicsClose();
    if (instanceMutex) CloseHandle(instanceMutex);
    ExitProcess(exitCode);
}
