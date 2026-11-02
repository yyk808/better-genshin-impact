# BetterGI Linux 后台输入辅助程序（在游戏的 Wine 前缀里运行，x86-64 Windows 控制台程序）
# 协议（stdin，小端）：
#   1) 先读 512 字节：窗口类名（UTF-16，最多 127 字 + 0，256 字节）+ 窗口标题（同上）→ FindWindowW（类名为空则不限）
#      把找到的 HWND（8 字节，0 = 没找到）写到 stdout；没找到就退出
#   2) 之后每条 24 字节：u32 消息、u32 标志、u64 wParam、u64 lParam → PostMessageW(hwnd, …)
#      标志 1：仅当游戏不在前台时投递（GetForegroundWindow() != hwnd，用于 WM_ACTIVATE）
#      标志 2：按键消息，扫描码由 Wine 的键盘布局给出：MapVirtualKeyW(wParam, MAPVK_VK_TO_VSC_EX) 的低 8 位放进 lParam 16–23 位，
#              带 0xE0 前缀（扩展键）时置第 24 位
#   stdin 关闭即退出。没有重定位：映像基址固定 0x140000000，导入表地址见 WinePostHelperImage.cs
        .intel_syntax noprefix
        .set IAT_GetStdHandle,        0x140002000
        .set IAT_ReadFile,            0x140002008
        .set IAT_WriteFile,           0x140002010
        .set IAT_ExitProcess,         0x140002018
        .set IAT_FindWindowW,         0x140002028
        .set IAT_PostMessageW,        0x140002030
        .set IAT_GetForegroundWindow, 0x140002038
        .set IAT_MapVirtualKeyW,      0x140002040
        .set BUF,                     0x140003000
        .set HWNDSLOT,                0x140003200
        .text
start:
        sub rsp, 0x48
        mov ecx, -10
        movabs rax, IAT_GetStdHandle
        call [rax]
        mov rbx, rax                    # stdin
        mov ecx, -11
        movabs rax, IAT_GetStdHandle
        call [rax]
        mov rsi, rax                    # stdout
        movabs rdi, BUF
        mov r12d, 512
        call read_exact
        test eax, eax
        jz quit
        mov rcx, rdi
        cmp word ptr [rdi], 0
        jne 1f
        xor ecx, ecx
1:      lea rdx, [rdi + 256]
        movabs rax, IAT_FindWindowW
        call [rax]
        mov r13, rax                    # hwnd
        movabs rdx, HWNDSLOT
        mov [rdx], r13
        mov rcx, rsi
        mov r8d, 8
        lea r9, [rsp + 0x30]
        mov qword ptr [rsp + 0x20], 0
        movabs rax, IAT_WriteFile
        call [rax]
        test r13, r13
        jz quit
next:
        mov r12d, 24
        call read_exact
        test eax, eax
        jz quit
        cmp dword ptr [rdi + 4], 0
        je 2f
        movabs rax, IAT_GetForegroundWindow
        call [rax]
        cmp rax, r13
        je next
2:      test byte ptr [rdi + 4], 2
        jz 6f
        mov ecx, [rdi + 8]
        mov edx, 4
        movabs rax, IAT_MapVirtualKeyW
        call [rax]
        mov ecx, eax
        and ecx, 0xff
        shl ecx, 16
        test eax, 0xff00
        jz 7f
        or ecx, 0x1000000
7:      or dword ptr [rdi + 16], ecx
6:      mov rcx, r13
        mov edx, [rdi]
        mov r8, [rdi + 8]
        mov r9, [rdi + 16]
        movabs rax, IAT_PostMessageW
        call [rax]
        jmp next
quit:
        xor ecx, ecx
        movabs rax, IAT_ExitProcess
        call [rax]

# 从 stdin（rbx）读满 r12d 字节到 rdi；成功 eax = 1，失败或 stdin 关闭 eax = 0
read_exact:
        sub rsp, 0x38
        xor r14d, r14d
3:      cmp r14d, r12d
        jae 4f
        mov rcx, rbx
        lea rdx, [rdi + r14]
        mov r8d, r12d
        sub r8d, r14d
        lea r9, [rsp + 0x28]
        mov qword ptr [rsp + 0x20], 0
        mov dword ptr [rsp + 0x28], 0
        movabs rax, IAT_ReadFile
        call [rax]
        test eax, eax
        jz 5f
        mov eax, [rsp + 0x28]
        test eax, eax
        jz 5f
        add r14d, eax
        jmp 3b
4:      mov eax, 1
        add rsp, 0x38
        ret
5:      xor eax, eax
        add rsp, 0x38
        ret
