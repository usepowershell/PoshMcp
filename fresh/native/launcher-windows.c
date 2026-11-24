#define UNICODE
#define _UNICODE
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <wchar.h>
#include <stdlib.h>

static wchar_t *skip_argument(wchar_t *text)
{
    int quoted = 0;
    while (*text)
    {
        if (*text == L'"') quoted = !quoted;
        else if (*text == L' ' && !quoted) break;
        ++text;
    }
    while (*text == L' ') ++text;
    return text;
}

int wmain(int argc, wchar_t **argv)
{
    if (argc < 3) return 64;
    DWORD parent_id = wcstoul(argv[1], NULL, 10);
    HANDLE parent = OpenProcess(SYNCHRONIZE, FALSE, parent_id);
    HANDLE job = CreateJobObjectW(NULL, NULL);
    if (!parent || !job) return 70;
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = {0};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, sizeof(limits))) return 70;
    STARTUPINFOEXW startup = {0};
    startup.StartupInfo.cb = sizeof(startup);
    startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
    HANDLE streams[3] = { GetStdHandle(STD_INPUT_HANDLE), GetStdHandle(STD_OUTPUT_HANDLE), GetStdHandle(STD_ERROR_HANDLE) };
    startup.StartupInfo.hStdInput = streams[0];
    startup.StartupInfo.hStdOutput = streams[1];
    startup.StartupInfo.hStdError = streams[2];
    SIZE_T size = 0;
    InitializeProcThreadAttributeList(NULL, 2, 0, &size);
    startup.lpAttributeList = HeapAlloc(GetProcessHeap(), 0, size);
    if (!startup.lpAttributeList ||
        !InitializeProcThreadAttributeList(startup.lpAttributeList, 2, 0, &size) ||
        !UpdateProcThreadAttribute(startup.lpAttributeList, 0, PROC_THREAD_ATTRIBUTE_JOB_LIST, &job, sizeof(job), NULL, NULL) ||
        !UpdateProcThreadAttribute(startup.lpAttributeList, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, streams, sizeof(streams), NULL, NULL))
        return 70;
    wchar_t *command = skip_argument(skip_argument(GetCommandLineW()));
    PROCESS_INFORMATION child = {0};
    if (!CreateProcessW(argv[2], command, NULL, NULL, TRUE, EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW,
        NULL, NULL, &startup.StartupInfo, &child)) return 72;
    CloseHandle(child.hThread);
    HANDLE wait_handles[2] = { child.hProcess, parent };
    DWORD wait = WaitForMultipleObjects(2, wait_handles, FALSE, INFINITE);
    DWORD code = 143;
    if (wait == WAIT_OBJECT_0) GetExitCodeProcess(child.hProcess, &code);
    CloseHandle(job);
    CloseHandle(child.hProcess);
    CloseHandle(parent);
    DeleteProcThreadAttributeList(startup.lpAttributeList);
    HeapFree(GetProcessHeap(), 0, startup.lpAttributeList);
    return (int)code;
}
