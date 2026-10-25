#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _ACRTIMP
#include <windows.h>
int _fltused=0;

// Model only the verified value layout, with exported RVAs for the test build.
typedef struct { const void *vtable; BYTE padding[64]; union { INT i[2]; BYTE b[8]; float f[2]; } value; } Variable;
__declspec(dllexport) const ULONGLONG intTable=0, boolTable=1;
static Variable enable={&intTable,{0},{{0,0}}};
static Variable automatic={&boolTable,{0},{{0x0101,0}}};
static Variable quality={&intTable,{0},{{-1,-1}}};
static Variable aa={&intTable,{0},{{2,2}}};
__declspec(dllexport) Variable *enableSlot, *autoSlot, *qualitySlot, *aaSlot;
__declspec(dllexport) const ULONGLONG floatTable=7, intRefTable=8, floatRefTable=9, boolRefTable=10;
static Variable fps={&floatTable,{0},{.f={60,60}}}, sharpness={&floatTable,{0},{.f={-0.2f,-0.2f}}};
static Variable scale={&floatTable,{0},{.f={100,100}}};
static Variable vsync={&intTable,{0},{{1,1}}}, syncInterval={&intTable,{0},{{1,1}}};
static Variable anisotropy={&intTable,{0},{{8,8}}}, lighting={&intTable,{0},{{2,2}}};
typedef struct { const void *vtable; BYTE padding[64]; void *reference; union { INT i; float f; } shadow; } Reference;
static INT intReference=1, alternateReference=1;
static float floatReference=1;
static Reference intRef={&intRefTable,{0},&intReference,{.i=1}};
static Reference floatRef={&floatRefTable,{0},&floatReference,{.f=1}};
__declspec(dllexport) Variable *fpsSlot,*sharpnessSlot,*scaleSlot,*vsyncSlot,*syncSlot,*anisotropySlot,*lightingSlot;
__declspec(dllexport) Reference *intRefSlot,*floatRefSlot;
static Variable fg={&intTable,{0},{{0,0}}}, fsrFg={&intTable,{0},{{0,0}}}, xessFg={&intTable,{0},{{0,0}}};
__declspec(dllexport) Variable *fgSlot,*fsrFgSlot,*xessFgSlot;
static BYTE boolReference[4]={0,85,102,119};
static Reference boolRef={&boolRefTable,{0},boolReference,{.i=0x77665500}};
__declspec(dllexport) Reference *boolRefSlot=&boolRef;
__declspec(dllexport) const ULONGLONG worldTable=2, instanceTable=3, playerTable=4, controllerTable=5, pawnTable=6;
typedef struct { const void *vtable; BYTE pad[0x2e8-8]; void *pawn; } Controller;
typedef struct { const void *vtable; BYTE pad[0x38-8]; Controller *controller; } Player;
typedef struct { const void *vtable; BYTE pad[0x40-8]; Player **players; INT count; } Instance;
typedef struct {
    const void *vtable; BYTE pad[0x1b8-8]; Instance *instance;
    BYTE padding[0x680-0x1c0]; float seconds;
} World;
static const void *pawn=&pawnTable;
static const void *alternatePawn=&pawnTable;
static Controller controller={&controllerTable,{0},NULL};
static Player player={&playerTable,{0},&controller};
static Player *players[]={&player};
static Instance instance={&instanceTable,{0},players,1};
static World world={&worldTable,{0},&instance,{0},0}, nextWorld={&worldTable,{0},&instance,{0},0};
__declspec(dllexport) World *worldSlot;
static BOOL Desired(void) {
    return enable.value.i[0]==1 && enable.value.i[1]==1 &&
        automatic.value.b[0]==0 && automatic.value.b[1]==0 &&
        quality.value.i[0]==0 && quality.value.i[1]==0 && aa.value.i[0]==0 && aa.value.i[1]==0;
}
void mainCRTStartup(void) {
    WCHAR path[32768], bad[4];
    GetEnvironmentVariableW(L"WUWA_MEMORY_OUTPUT",path,32768);
    Sleep(800); // Registration is deliberately later than the helper's first tick.
    enableSlot=&enable; autoSlot=&automatic; qualitySlot=&quality; aaSlot=&aa; fgSlot=&fg;fsrFgSlot=&fsrFg;xessFgSlot=&xessFg;
    fpsSlot=&fps;sharpnessSlot=&sharpness;scaleSlot=&scale;vsyncSlot=&vsync;syncSlot=&syncInterval;
    anisotropySlot=&anisotropy;lightingSlot=&lighting;intRefSlot=&intRef;floatRefSlot=&floatRef;
    BOOL extended=GetEnvironmentVariableW(L"WUWA_MEMORY_EXTENDED",bad,4)!=0;
    BOOL nan=GetEnvironmentVariableW(L"WUWA_MEMORY_NAN",bad,4)!=0;
    BOOL repoint=GetEnvironmentVariableW(L"WUWA_MEMORY_REPOINT",bad,4)!=0;
    BOOL partial=GetEnvironmentVariableW(L"WUWA_MEMORY_PARTIAL",bad,4)!=0;
    if (nan) fps.value.i[0]=0x7fc00000;
    BOOL extendedBefore=FALSE,extendedAfter=FALSE,extendedReset=FALSE,extendedMismatch=FALSE;
    if (GetEnvironmentVariableW(L"WUWA_MEMORY_BAD_TYPE",bad,4)) aa.vtable=&boolTable;
    BOOL before=FALSE, after=FALSE, reset=FALSE, lateMismatch=FALSE;
    BOOL changeLayout=GetEnvironmentVariableW(L"WUWA_MEMORY_LATE_MISMATCH",bad,4)!=0;
    WCHAR fgKind[4]={0};
    GetEnvironmentVariableW(L"WUWA_MEMORY_FG_KIND",fgKind,4);
    Variable *observedFg=fgKind[0]==L'1'?&fsrFg:fgKind[0]==L'2'?&xessFg:&fg;
    BOOL independent=GetEnvironmentVariableW(L"WUWA_MEMORY_FG_INDEPENDENT",bad,4)!=0;
    BOOL boolRefTest=GetEnvironmentVariableW(L"WUWA_MEMORY_BOOL_REF",bad,4)!=0;
    BOOL otherInterrupted=FALSE,otherStayedOn=FALSE;
    BOOL scene=GetEnvironmentVariableW(L"WUWA_MEMORY_SCENE",bad,4)!=0;
    BOOL loginOn=FALSE, playOn=FALSE, pausedOn=FALSE, resumeOn=FALSE, repaired=FALSE;
    BOOL changedOn=FALSE, invalidOn=FALSE, fgReset=FALSE, paused=FALSE, switchingOff=FALSE;
    if (scene) worldSlot=&world;
    ULONGLONG start=GetTickCount64();
    while (GetTickCount64()-start<(scene?20000:5500)) {
        ULONGLONG elapsed=GetTickCount64()-start;
        if (scene) {
            BOOL on=observedFg->value.i[0]!=0 || observedFg->value.i[1]!=0;
            if (elapsed<2000 && on) loginOn=TRUE;
            if (elapsed>=3500 && elapsed<5000 && observedFg->value.i[0]==1 && observedFg->value.i[1]==1) playOn=TRUE;
            if (elapsed>=4100 && elapsed<5000 && !on) switchingOff=TRUE;
            if (elapsed>=5000 && elapsed<7500) {
                if (!paused) { fg.value.i[0]=fg.value.i[1]=0;fsrFg.value.i[0]=fsrFg.value.i[1]=0;xessFg.value.i[0]=xessFg.value.i[1]=0; paused=TRUE; }
                else if (on) pausedOn=TRUE;
            } else { world.seconds+=0.02f; nextWorld.seconds+=0.02f; }
            if (elapsed>=9000 && elapsed<10500 && on) resumeOn=TRUE;
            if (elapsed>=10500 && !fgReset) { observedFg->value.i[0]=observedFg->value.i[1]=0; fgReset=TRUE; }
            if (elapsed>=12200 && elapsed<13000 && on) repaired=TRUE;
            if (independent && elapsed>=10800 && elapsed<11800) {
                if (fg.value.i[0]!=1 || xessFg.value.i[0]!=1) otherInterrupted=TRUE;
                else otherStayedOn=TRUE;
            }
            if (elapsed>=13000) worldSlot=&nextWorld;
            if (elapsed>=14700 && elapsed<15500 && on) changedOn=TRUE;
            controller.pawn=elapsed>=2000 && elapsed<15500?&pawn:NULL;
            if (elapsed>=4100 && elapsed<5000) controller.pawn=&alternatePawn;
            if (elapsed>=17000) { controller.pawn=&pawn; nextWorld.vtable=&boolTable; }
            if (elapsed>=16100 && on) invalidOn=TRUE;
        }
        if (extended) {
            BOOL desired=fps.value.f[0]==45.5f && fps.value.f[1]==45.5f &&
                sharpness.value.f[0]>-0.301f && sharpness.value.f[0]<-0.299f &&
                sharpness.value.f[1]>-0.301f && sharpness.value.f[1]<-0.299f &&
                intReference==0 && intRef.shadow.i==0 &&
                floatReference>0.639f && floatReference<0.641f && floatRef.shadow.f>0.639f && floatRef.shadow.f<0.641f;
            if (desired) { if (extendedReset) extendedAfter=TRUE; else extendedBefore=TRUE; }
            if (elapsed>=2000 && !extendedReset) {
                if (!nan) fps.value.f[0]=fps.value.f[1]=60;
                floatReference=1;floatRef.shadow.f=1;extendedReset=TRUE;
            }
            if (elapsed>=3500 && !extendedMismatch) {
                if (repoint) intRef.reference=&alternateReference;
                if (partial) {fps.value.f[0]=33;enable.value.i[0]=enable.value.i[1]=2;}
                extendedMismatch=TRUE;
            }
        }
        if (Desired()) { if (reset) after=TRUE; else before=TRUE; }
        if (elapsed>=2000 && !reset) {
            quality.value.i[0]=quality.value.i[1]=-1;
            reset=TRUE;
        }
        if (elapsed>=3000 && changeLayout && !lateMismatch) {
            // Simulate a later unexpected engine value. The helper must stop,
            // preserve this value, and roll back the still-valid siblings.
            enable.value.i[0]=enable.value.i[1]=2;
            lateMismatch=TRUE;
        }
        Sleep(20);
    }
    char output[512];
    int length=wsprintfA(output,
        "{\"beforeReset\":%d,\"afterReset\":%d,\"values\":[%d,%d,%d,%d,%d,%d,%d,%d]}",
        before,after,enable.value.i[0],enable.value.i[1],automatic.value.b[0],automatic.value.b[1],
        quality.value.i[0],quality.value.i[1],aa.value.i[0],aa.value.i[1]);
    if (scene) length=wsprintfA(output,
        "{\"loginOn\":%d,\"playOn\":%d,\"pausedOn\":%d,\"resumeOn\":%d,\"repaired\":%d,"
        "\"changedWorldOn\":%d,\"invalidSceneOn\":%d,\"switchingOff\":%d,\"fg\":[%d,%d],\"otherGraphicsApplied\":%d}",
        loginOn,playOn,pausedOn,resumeOn,repaired,changedOn,invalidOn,switchingOff,observedFg->value.i[0],observedFg->value.i[1],Desired());
    if (independent) length=wsprintfA(output,
        "{\"otherInterrupted\":%d,\"otherStayedOn\":%d,\"repaired\":%d,\"pausedOn\":%d,\"loginOn\":%d}",
        otherInterrupted,otherStayedOn,repaired,pausedOn,loginOn);
    if (boolRefTest) length=wsprintfA(output,
        "{\"value\":[%d,%d,%d,%d],\"shadow\":[%d,%d,%d,%d]}",
        boolReference[0],boolReference[1],boolReference[2],boolReference[3],
        ((BYTE*)&boolRef.shadow)[0],((BYTE*)&boolRef.shadow)[1],((BYTE*)&boolRef.shadow)[2],((BYTE*)&boolRef.shadow)[3]);
    if (extended) length=wsprintfA(output,
        "{\"beforeReset\":%d,\"afterReset\":%d,\"fpsMilli\":[%d,%d],\"sharpMilli\":[%d,%d],"
        "\"intRef\":[%d,%d,%d],\"floatRefMilli\":[%d,%d],\"enable\":[%d,%d]}",
        extendedBefore,extendedAfter,nan?-999:(INT)(fps.value.f[0]*1000),(INT)(fps.value.f[1]*1000),
        (INT)(sharpness.value.f[0]*1000),(INT)(sharpness.value.f[1]*1000),
        intReference,intRef.shadow.i,alternateReference,(INT)(floatReference*1000),(INT)(floatRef.shadow.f*1000),
        enable.value.i[0],enable.value.i[1]);
    HANDLE file=CreateFileW(path,GENERIC_WRITE,FILE_SHARE_READ,NULL,CREATE_ALWAYS,0,NULL);
    DWORD written;
    if (file==INVALID_HANDLE_VALUE || !WriteFile(file,output,length,&written,NULL)) ExitProcess(8);
    CloseHandle(file);
    ExitProcess(0);
}
