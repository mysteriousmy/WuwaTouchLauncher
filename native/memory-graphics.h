// Runtime graphics writes are confined to the exact child created by this bridge.
// No executable pages, code patches, privilege changes, or remote threads are used.
#include <bcrypt.h>
// Required by the freestanding MSVC ABI when reading/comparing world clocks.
int _fltused=0;

typedef enum { GRAPHICS_INT, GRAPHICS_BOOL, GRAPHICS_FLOAT, GRAPHICS_INT_REF, GRAPHICS_FLOAT_REF, GRAPHICS_BOOL_REF } GraphicsKind;
typedef struct {
    const WCHAR *name;
    DWORD slotRva, vtableRva;
    GraphicsKind kind;
    float minimum, maximum;
    UINT targetMask;
    BOOL sceneGated;
} GraphicsSpec;

// A test-only build may supply a fixture layout. There is deliberately no runtime
// option, configuration file, or environment variable for bypassing build checks.
#ifndef GRAPHICS_LAYOUT_HEADER
#define GRAPHICS_LAYOUT_HEADER "memory-graphics-layout.h"
#endif
#include GRAPHICS_LAYOUT_HEADER
#define GRAPHICS_COUNT (sizeof(graphicsSpecs)/sizeof(graphicsSpecs[0]))
#ifndef GRAPHICS_READY_TIMEOUT_MS
#define GRAPHICS_READY_TIMEOUT_MS 180000
#endif

typedef struct {
    BOOL requested, captured, wrote;
    float target;
    ULONG_PTR object, locations[2];
    BYTE original[8], desired[8];
    ULONG_PTR previousWorld;
    float previousTime;
    UINT sceneSamples;
    INT previousFg;
} GraphicsValue;
static GraphicsValue graphicsValues[GRAPHICS_COUNT];
static BOOL graphicsRequested, graphicsEverApplied;
static HANDLE graphicsProcess, graphicsLockedFile=INVALID_HANDLE_VALUE;
static DWORD graphicsPid, graphicsError, graphicsCount, graphicsApplied, graphicsWrites, graphicsSkipped;
static ULONG_PTR graphicsBase;
static ULONGLONG graphicsDeadline;
static const char *graphicsState="disabled";
static const char *graphicsFrameGenerationState="not-requested";
static const WCHAR *graphicsUnavailableKeys[]={
    L"smooth-fps",L"fixed-fps",L"npc-density",L"vegetation-dither",L"dynamic-bones",
    L"teammate-effects",L"hit-material",L"damage-effects",L"eye-mode",L"eye-temperature",
    L"eye-strength",L"eye-brightness",L"eye-texture"
};
static BOOL graphicsUnavailableRequested[sizeof(graphicsUnavailableKeys)/sizeof(graphicsUnavailableKeys[0])];

static BOOL GraphicsSetUnavailable(const WCHAR *key) {
    for (UINT i=0;i<sizeof(graphicsUnavailableKeys)/sizeof(graphicsUnavailableKeys[0]);i++)
        if (lstrcmpW(key,graphicsUnavailableKeys[i])==0 && !graphicsUnavailableRequested[i]) {
            graphicsUnavailableRequested[i]=TRUE; graphicsSkipped++; return TRUE;
        }
    return FALSE;
}

static BOOL GraphicsEqual(const BYTE *a,const BYTE *b,SIZE_T length) {
    for (SIZE_T i=0;i<length;i++) if (a[i]!=b[i]) return FALSE;
    return TRUE;
}

static BOOL GraphicsSetOption(const WCHAR *argument) {
    WCHAR name[128]; UINT length=0;
    while (*argument && *argument!=L'=' && length<127) name[length++]=*argument++;
    if (*argument++!=L'=' || !length) return FALSE;
    name[length]=0;
    BOOL negative=*argument==L'-';
    if (negative) argument++;
    UINT number=0, digits=0;
    while (*argument>=L'0' && *argument<=L'9') {
        if (number>10000) return FALSE;
        number=number*10+(UINT)(*argument++-L'0'); digits++;
    }
    if (!digits) return FALSE;
    float target=(float)number;
    if (*argument==L'.') {
        argument++; digits=0; UINT fraction=0, denominator=1;
        while (*argument>=L'0' && *argument<=L'9') {
            if (++digits>6) return FALSE;
            fraction=fraction*10+(UINT)(*argument++-L'0'); denominator*=10;
        }
        if (!digits) return FALSE;
        target=(float)((double)number+(double)fraction/(double)denominator);
    }
    if (*argument) return FALSE;
    if (negative) target=-target;
    for (UINT i=0;i<GRAPHICS_COUNT;i++) if (lstrcmpW(name,graphicsSpecs[i].name)==0) {
        const GraphicsSpec *s=&graphicsSpecs[i];
        BOOL floating=s->kind==GRAPHICS_FLOAT || s->kind==GRAPHICS_FLOAT_REF;
        if (graphicsValues[i].requested || !(target>=s->minimum && target<=s->maximum) ||
            (!floating && target!=(float)(INT)target)) return FALSE;
        if (s->targetMask) {
            INT bit=(INT)(target-s->minimum);
            if (bit<0 || bit>=32 || !(s->targetMask & (1u<<bit))) return FALSE;
        }
        graphicsValues[i].requested=TRUE; graphicsValues[i].target=target; graphicsCount++;
        return TRUE;
    }
    return FALSE;
}

static void GraphicsUnlockFile(void) {
    if (graphicsLockedFile!=INVALID_HANDLE_VALUE) CloseHandle(graphicsLockedFile);
    graphicsLockedFile=INVALID_HANDLE_VALUE;
}

static void GraphicsPrepare(const WCHAR *path) {
    if (!graphicsRequested) return;
    if (!graphicsCount) { graphicsState=graphicsSkipped?"unavailable":"no-settings"; return; }
    graphicsState="failed";
    BCRYPT_ALG_HANDLE algorithm=NULL;
    BCRYPT_HASH_HANDLE hash=NULL;
    BYTE *object=NULL, digest[32];
    DWORD objectSize=0, returned=0, count=0;
    static BYTE buffer[65536];
    BOOL ok=FALSE;
    graphicsLockedFile=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);
    if (graphicsLockedFile==INVALID_HANDLE_VALUE) { graphicsError=GetLastError(); return; }
    if (BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,NULL,0)<0 ||
        BCryptGetProperty(algorithm,BCRYPT_OBJECT_LENGTH,(PUCHAR)&objectSize,sizeof(objectSize),&returned,0)<0 ||
        !objectSize || objectSize>1048576) goto done;
    object=HeapAlloc(GetProcessHeap(),0,objectSize);
    if (!object || BCryptCreateHash(algorithm,&hash,object,objectSize,NULL,0,0)<0) goto done;
    for (;;) {
        if (!ReadFile(graphicsLockedFile,buffer,sizeof(buffer),&count,NULL)) {
            graphicsError=GetLastError(); goto done;
        }
        if (!count) break;
        if (BCryptHashData(hash,buffer,count,0)<0) goto done;
    }
    if (BCryptFinishHash(hash,digest,sizeof(digest),0)<0) goto done;
    if (!GraphicsEqual(digest,graphicsExpectedHash,sizeof(digest))) {
        graphicsState="unsupported-version"; graphicsError=ERROR_REVISION_MISMATCH; goto done;
    }
    ok=TRUE; graphicsState="waiting";
done:
    if (hash) BCryptDestroyHash(hash);
    if (object) HeapFree(GetProcessHeap(),0,object);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm,0);
    if (!ok) {
        if (!graphicsError) graphicsError=ERROR_INVALID_DATA;
        GraphicsUnlockFile();
    }
    // Keep the read-only file handle until CreateProcess has opened the image,
    // preventing a different executable from replacing the file after hashing.
}

static void GraphicsAttach(HANDLE child,DWORD pid) {
    if (lstrcmpA(graphicsState,"waiting")==0) {
        if (!DuplicateHandle(GetCurrentProcess(),child,GetCurrentProcess(),&graphicsProcess,
            0,FALSE,DUPLICATE_SAME_ACCESS)) {
            graphicsState="failed"; graphicsError=GetLastError();
        } else { graphicsPid=pid; graphicsDeadline=GetTickCount64()+GRAPHICS_READY_TIMEOUT_MS; }
    }
    GraphicsUnlockFile();
}

static BOOL GraphicsRead(ULONG_PTR address,void *data,SIZE_T size) {
    SIZE_T count=0;
    return ReadProcessMemory(graphicsProcess,(const void*)address,data,size,&count) && count==size;
}

static BOOL GraphicsSceneObject(ULONG_PTR address,DWORD vtableRva) {
    ULONG_PTR vtable=0;
    return address && address%8==0 && GraphicsRead(address,&vtable,sizeof(vtable)) &&
        vtable==graphicsBase+vtableRva;
}

// Read-only scene validation. A local pawn and an advancing world clock exclude
// login/loading and paused full-screen menus. No gameplay object is modified.
static INT GraphicsFrameGenerationTarget(GraphicsValue *value,INT current) {
    ULONG_PTR world=0,instance=0,players=0,player=0,controller=0,pawn=0,check=0;
    INT count=0; float seconds=0;
    BOOL valid=GraphicsRead(graphicsBase+graphicsWorldSlotRva,&world,sizeof(world)) &&
        GraphicsSceneObject(world,graphicsWorldVtableRva) &&
        GraphicsRead(world+0x680,&seconds,sizeof(seconds)) && seconds>=0 && seconds<100000000 &&
        GraphicsRead(world+0x1b8,&instance,sizeof(instance)) &&
        GraphicsSceneObject(instance,graphicsInstanceVtableRva) &&
        GraphicsRead(instance+0x48,&count,sizeof(count)) && count==1 &&
        GraphicsRead(instance+0x40,&players,sizeof(players)) && players && players%8==0 &&
        GraphicsRead(players,&player,sizeof(player)) && GraphicsSceneObject(player,graphicsPlayerVtableRva) &&
        GraphicsRead(player+0x38,&controller,sizeof(controller)) &&
        GraphicsSceneObject(controller,graphicsControllerVtableRva) &&
        GraphicsRead(controller+0x2e8,&pawn,sizeof(pawn)) && GraphicsSceneObject(pawn,graphicsPawnVtableRva) &&
        GraphicsRead(graphicsBase+graphicsWorldSlotRva,&check,sizeof(check)) && check==world &&
        GraphicsRead(controller+0x2e8,&check,sizeof(check)) && check==pawn;
    // An engine-initiated off transition also restarts the settling period.
    // Otherwise the last advancing sample at menu entry could turn FG back on.
    if (!valid || (current==0 && value->previousFg!=0)) value->sceneSamples=0;
    // Switching between valid playable pawns is ordinary gameplay, not a load.
    else if (world==value->previousWorld &&
        seconds>value->previousTime && seconds-value->previousTime<2.0f) {
        if (value->sceneSamples<2) value->sceneSamples++;
    } else value->sceneSamples=0;
    value->previousWorld=valid?world:0;
    value->previousTime=seconds; value->previousFg=current;
    return value->sceneSamples>=2?1:0;
}

static BOOL GraphicsWritable(ULONG_PTR address,SIZE_T size) {
    MEMORY_BASIC_INFORMATION page;
    if (!VirtualQueryEx(graphicsProcess,(void*)address,&page,sizeof(page)) || page.State!=MEM_COMMIT ||
        (page.Protect & (PAGE_GUARD | PAGE_NOACCESS))) return FALSE;
    DWORD protection=page.Protect & 0xff;
    if (protection!=PAGE_READWRITE && protection!=PAGE_WRITECOPY) return FALSE;
    ULONG_PTR offset=address-(ULONG_PTR)page.BaseAddress;
    return offset<page.RegionSize && size<=page.RegionSize-offset;
}

static BOOL GraphicsFindImage(void) {
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32,graphicsPid);
    if (snapshot==INVALID_HANDLE_VALUE) return FALSE;
    MODULEENTRY32W module={0}; module.dwSize=sizeof(module);
    BOOL found=Module32FirstW(snapshot,&module);
    CloseHandle(snapshot);
    if (!found) return FALSE;
    IMAGE_DOS_HEADER dos;
    IMAGE_NT_HEADERS64 nt;
    ULONG_PTR base=(ULONG_PTR)module.modBaseAddr;
    if (!GraphicsRead(base,&dos,sizeof(dos)) || dos.e_magic!=IMAGE_DOS_SIGNATURE ||
        dos.e_lfanew<sizeof(dos) || dos.e_lfanew>1048576 ||
        !GraphicsRead(base+(ULONG_PTR)dos.e_lfanew,&nt,sizeof(nt)) ||
        nt.Signature!=IMAGE_NT_SIGNATURE || nt.FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64 ||
        nt.OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC) return FALSE;
    for (UINT i=0;i<GRAPHICS_COUNT;i++)
        if (graphicsSpecs[i].slotRva+sizeof(ULONG_PTR)>nt.OptionalHeader.SizeOfImage ||
            graphicsSpecs[i].vtableRva>=nt.OptionalHeader.SizeOfImage) return FALSE;
    if (graphicsWorldSlotRva+sizeof(ULONG_PTR)>nt.OptionalHeader.SizeOfImage ||
        graphicsWorldVtableRva>=nt.OptionalHeader.SizeOfImage ||
        graphicsInstanceVtableRva>=nt.OptionalHeader.SizeOfImage ||
        graphicsPlayerVtableRva>=nt.OptionalHeader.SizeOfImage ||
        graphicsControllerVtableRva>=nt.OptionalHeader.SizeOfImage ||
        graphicsPawnVtableRva>=nt.OptionalHeader.SizeOfImage) return FALSE;
    graphicsBase=base;
    return TRUE;
}

static SIZE_T GraphicsValueSize(const GraphicsSpec *s) {
    return s->kind==GRAPHICS_BOOL || s->kind==GRAPHICS_BOOL_REF?1:4;
}

static BOOL GraphicsResolve(UINT index,BYTE *current) {
    const GraphicsSpec *s=&graphicsSpecs[index];
    GraphicsValue *v=&graphicsValues[index];
    ULONG_PTR object=0, vtable=0, locations[2];
    SIZE_T size=GraphicsValueSize(s);
    BOOL reference=s->kind==GRAPHICS_INT_REF || s->kind==GRAPHICS_FLOAT_REF || s->kind==GRAPHICS_BOOL_REF;
    BOOL floating=s->kind==GRAPHICS_FLOAT || s->kind==GRAPHICS_FLOAT_REF;
    if (!GraphicsRead(graphicsBase+s->slotRva,&object,sizeof(object)) || !object || object%8 ||
        (v->captured && v->object!=object) || !GraphicsRead(object,&vtable,sizeof(vtable)) ||
        vtable!=graphicsBase+s->vtableRva) return FALSE;
    if (reference) {
        if (!GraphicsRead(object+72,&locations[0],sizeof(ULONG_PTR)) || !locations[0] || locations[0]%size)
            return FALSE;
        locations[1]=object+80;
        if (locations[0]==locations[1]) return FALSE;
    } else { locations[0]=object+72; locations[1]=object+72+size; }
    for (UINT copy=0;copy<2;copy++) {
        if ((v->captured && locations[copy]!=v->locations[copy]) || !GraphicsWritable(locations[copy],size) ||
            !GraphicsRead(locations[copy],current+copy*size,size)) return FALSE;
        float value;
        if (size==1) value=(float)current[copy];
        else if (floating) memcpy(&value,current+copy*size,4);
        else { INT integer; memcpy(&integer,current+copy*size,4); value=(float)integer; }
        // This form also rejects NaN. Infinity cannot pass the bounded ranges.
        if (!(value>=s->minimum && value<=s->maximum)) return FALSE;
    }
    if (!v->captured) {
        v->object=object; v->locations[0]=locations[0]; v->locations[1]=locations[1];
        memcpy(v->original,current,size*2);
        if (size==1) v->desired[0]=v->desired[1]=(BYTE)v->target;
        else if (floating) { memcpy(v->desired,&v->target,4); memcpy(v->desired+4,&v->target,4); }
        else { INT integer=(INT)v->target; memcpy(v->desired,&integer,4); memcpy(v->desired+4,&integer,4); }
        v->captured=TRUE;
    }
    return TRUE;
}

static BOOL GraphicsWrite(UINT index,const BYTE *data) {
    GraphicsValue *v=&graphicsValues[index]; SIZE_T size=GraphicsValueSize(&graphicsSpecs[index]);
    for (UINT copy=0;copy<2;copy++) {
        SIZE_T written=0;
        if (!WriteProcessMemory(graphicsProcess,(void*)v->locations[copy],data+copy*size,size,&written) || written!=size)
            return FALSE;
    }
    return TRUE;
}

static void GraphicsRollback(void) {
    for (UINT i=0;i<GRAPHICS_COUNT;i++) {
        GraphicsValue *v=&graphicsValues[i]; BYTE current[8];
        SIZE_T size=GraphicsValueSize(&graphicsSpecs[i]);
        if (v->wrote && GraphicsResolve(i,current)) for (UINT copy=0;copy<2;copy++) {
            // Restore each unchanged copy separately, including a partial write.
            // Engine changes and changed reference pointers are preserved.
            if (GraphicsEqual(current+copy*size,v->desired+copy*size,size)) {
                SIZE_T written=0;
                WriteProcessMemory(graphicsProcess,(void*)v->locations[copy],v->original+copy*size,size,&written);
            }
        }
        v->wrote=FALSE;
    }
}

static void GraphicsTick(void) {
    if (!graphicsProcess || (lstrcmpA(graphicsState,"waiting")!=0 && lstrcmpA(graphicsState,"applied")!=0)) return;
    if (WaitForSingleObject(graphicsProcess,0)==WAIT_OBJECT_0) {
        CloseHandle(graphicsProcess); graphicsProcess=NULL; return;
    }
    BYTE current[GRAPHICS_COUNT][8];
    BOOL ready=graphicsBase || GraphicsFindImage();
    for (UINT i=0;i<GRAPHICS_COUNT && ready;i++)
        if (graphicsValues[i].requested && !GraphicsResolve(i,current[i])) ready=FALSE;
    if (!ready) {
        if (graphicsEverApplied || GetTickCount64()>graphicsDeadline) {
            GraphicsRollback(); graphicsState="layout-mismatch"; graphicsError=ERROR_INVALID_DATA; graphicsApplied=0;
        } else for (UINT i=0;i<GRAPHICS_COUNT;i++) graphicsValues[i].captured=FALSE;
        return;
    }
    graphicsApplied=0;
    BOOL fgWaiting=FALSE,fgActive=FALSE,fgOff=FALSE;
    for (UINT i=0;i<GRAPHICS_COUNT;i++) {
        GraphicsValue *v=&graphicsValues[i];
        if (!v->requested) continue;
        if (graphicsSpecs[i].sceneGated) {
            INT target=0, observed=0;
            memcpy(&observed,current[i],4);
            if (v->target) {
                target=GraphicsFrameGenerationTarget(v,observed);
                if (target) fgActive=TRUE; else fgWaiting=TRUE;
            } else fgOff=TRUE;
            memcpy(v->desired,&target,4); memcpy(v->desired+4,&target,4);
        }
        SIZE_T size=GraphicsValueSize(&graphicsSpecs[i])*2;
        if (!GraphicsEqual(current[i],v->desired,size)) {
            // A successful write is always checked again; simply issuing it is
            // not reported as applied. No page protections are changed.
            v->wrote=TRUE;
            if (!GraphicsWrite(i,v->desired) || !GraphicsResolve(i,current[i]) || !GraphicsEqual(current[i],v->desired,size)) {
                graphicsError=GetLastError(); if (!graphicsError) graphicsError=ERROR_WRITE_FAULT;
                GraphicsRollback(); graphicsState="failed"; graphicsApplied=0; return;
            }
            graphicsWrites++;
        }
        graphicsApplied++;
    }
    graphicsFrameGenerationState=fgWaiting?"waiting-scene":fgActive?"active":fgOff?"off":"not-requested";
    graphicsEverApplied=TRUE; graphicsState="applied";
}

static void GraphicsClose(void) {
    GraphicsUnlockFile();
    if (graphicsProcess) {
        if (WaitForSingleObject(graphicsProcess,0)!=WAIT_OBJECT_0) GraphicsRollback();
        CloseHandle(graphicsProcess); graphicsProcess=NULL;
    }
}
