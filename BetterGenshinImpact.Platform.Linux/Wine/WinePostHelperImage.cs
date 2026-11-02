using System.Buffers.Binary;
using System.Text;

namespace BetterGenshinImpact.Platform.Linux.Wine;

/// <summary>
/// 后台输入辅助程序的 Windows 可执行文件（PE32+，x86-64 控制台程序，约 2 KB），运行时生成，不依赖 .NET / mono / mingw。
/// 机器码由同目录 <c>post-helper.s</c> 用 GNU as 汇编而来（<c>as --64 post-helper.s &amp;&amp; objcopy -O binary -j .text</c>），
/// 程序里用到的导入表与数据地址是固定的（映像基址 0x140000000、没有重定位），与这里的布局一一对应：
/// <list type="bullet">
/// <item>.text  RVA 0x1000：机器码</item>
/// <item>.rdata RVA 0x2000：导入地址表（kernel32：GetStdHandle 0x2000、ReadFile 0x2008、WriteFile 0x2010、ExitProcess 0x2018；
/// user32：FindWindowW 0x2028、PostMessageW 0x2030、GetForegroundWindow 0x2038、MapVirtualKeyW 0x2040）、查找表、导入描述符、名称</item>
/// <item>.data  RVA 0x3000：512 字节读缓冲区，0x3200 存 HWND</item>
/// </list>
/// 协议见 post-helper.s 与 <see cref="WinePostMessageClient"/>
/// </summary>
public static class WinePostHelperImage
{
    private const ulong ImageBase = 0x140000000;
    private const uint TextRva = 0x1000, RdataRva = 0x2000, DataRva = 0x3000;
    private const uint FileAlign = 0x200, SectionAlign = 0x1000;

    /// <summary>post-helper.s 汇编出的机器码</summary>
    private static readonly byte[] Code =
    [
        0x48, 0x83, 0xEC, 0x48, 0xB9, 0xF6, 0xFF, 0xFF, 0xFF, 0x48, 0xB8, 0x00, 0x20, 0x00, 0x40, 0x01,
        0x00, 0x00, 0x00, 0xFF, 0x10, 0x48, 0x89, 0xC3, 0xB9, 0xF5, 0xFF, 0xFF, 0xFF, 0x48, 0xB8, 0x00,
        0x20, 0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x10, 0x48, 0x89, 0xC6, 0x48, 0xBF, 0x00, 0x30,
        0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0x41, 0xBC, 0x00, 0x02, 0x00, 0x00, 0xE8, 0xE2, 0x00, 0x00,
        0x00, 0x85, 0xC0, 0x0F, 0x84, 0xCC, 0x00, 0x00, 0x00, 0x48, 0x89, 0xF9, 0x66, 0x83, 0x3F, 0x00,
        0x75, 0x02, 0x31, 0xC9, 0x48, 0x8D, 0x97, 0x00, 0x01, 0x00, 0x00, 0x48, 0xB8, 0x28, 0x20, 0x00,
        0x40, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x10, 0x49, 0x89, 0xC5, 0x48, 0xBA, 0x00, 0x32, 0x00, 0x40,
        0x01, 0x00, 0x00, 0x00, 0x4C, 0x89, 0x2A, 0x48, 0x89, 0xF1, 0x41, 0xB8, 0x08, 0x00, 0x00, 0x00,
        0x4C, 0x8D, 0x4C, 0x24, 0x30, 0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00, 0x48, 0xB8,
        0x10, 0x20, 0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x10, 0x4D, 0x85, 0xED, 0x74, 0x76, 0x41,
        0xBC, 0x18, 0x00, 0x00, 0x00, 0xE8, 0x79, 0x00, 0x00, 0x00, 0x85, 0xC0, 0x74, 0x67, 0x83, 0x7F,
        0x04, 0x00, 0x74, 0x11, 0x48, 0xB8, 0x38, 0x20, 0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x10,
        0x4C, 0x39, 0xE8, 0x74, 0xDA, 0xF6, 0x47, 0x04, 0x02, 0x74, 0x2F, 0x8B, 0x4F, 0x08, 0xBA, 0x04,
        0x00, 0x00, 0x00, 0x48, 0xB8, 0x40, 0x20, 0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x10, 0x89,
        0xC1, 0x81, 0xE1, 0xFF, 0x00, 0x00, 0x00, 0xC1, 0xE1, 0x10, 0xA9, 0x00, 0xFF, 0x00, 0x00, 0x74,
        0x06, 0x81, 0xC9, 0x00, 0x00, 0x00, 0x01, 0x09, 0x4F, 0x10, 0x4C, 0x89, 0xE9, 0x8B, 0x17, 0x4C,
        0x8B, 0x47, 0x08, 0x4C, 0x8B, 0x4F, 0x10, 0x48, 0xB8, 0x30, 0x20, 0x00, 0x40, 0x01, 0x00, 0x00,
        0x00, 0xFF, 0x10, 0xEB, 0x8A, 0x31, 0xC9, 0x48, 0xB8, 0x18, 0x20, 0x00, 0x40, 0x01, 0x00, 0x00,
        0x00, 0xFF, 0x10, 0x48, 0x83, 0xEC, 0x38, 0x45, 0x31, 0xF6, 0x45, 0x39, 0xE6, 0x73, 0x40, 0x48,
        0x89, 0xD9, 0x4A, 0x8D, 0x14, 0x37, 0x45, 0x89, 0xE0, 0x45, 0x29, 0xF0, 0x4C, 0x8D, 0x4C, 0x24,
        0x28, 0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00,
        0x00, 0x00, 0x48, 0xB8, 0x08, 0x20, 0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0xFF, 0x10, 0x85, 0xC0,
        0x74, 0x17, 0x8B, 0x44, 0x24, 0x28, 0x85, 0xC0, 0x74, 0x0F, 0x41, 0x01, 0xC6, 0xEB, 0xBB, 0xB8,
        0x01, 0x00, 0x00, 0x00, 0x48, 0x83, 0xC4, 0x38, 0xC3, 0x31, 0xC0, 0x48, 0x83, 0xC4, 0x38, 0xC3,
    ];

    private static readonly (string Dll, string[] Functions)[] Imports =
    [
        ("kernel32.dll", ["GetStdHandle", "ReadFile", "WriteFile", "ExitProcess"]),
        ("user32.dll", ["FindWindowW", "PostMessageW", "GetForegroundWindow", "MapVirtualKeyW"]),
    ];

    /// <summary>生成可执行文件的字节</summary>
    public static byte[] Build()
    {
        // —— .rdata：IAT（每个 DLL 一组，以 0 结尾）、ILT（同样内容）、导入描述符、提示 / 名称、DLL 名 ——
        var rdata = new byte[FileAlign * 2];
        var iatSize = (uint)Imports.Sum(i => (i.Functions.Length + 1) * 8);
        var iltStart = iatSize;
        var descStart = iltStart + iatSize;
        var namesStart = descStart + (uint)(Imports.Length + 1) * 20;
        var names = namesStart;
        var iat = 0u;
        var ilt = iltStart;
        var desc = descStart;
        foreach (var (dll, functions) in Imports)
        {
            var dllIat = iat;
            var dllIlt = ilt;
            foreach (var fn in functions)
            {
                // 提示（2 字节）+ 名称 + 0，按 2 字节对齐
                var entry = names;
                var bytes = Encoding.ASCII.GetBytes(fn);
                bytes.CopyTo(rdata, (int)(entry + 2));
                names = Align(entry + 2 + (uint)bytes.Length + 1, 2);
                BinaryPrimitives.WriteUInt64LittleEndian(rdata.AsSpan((int)iat), RdataRva + entry);
                BinaryPrimitives.WriteUInt64LittleEndian(rdata.AsSpan((int)ilt), RdataRva + entry);
                iat += 8;
                ilt += 8;
            }

            iat += 8;
            ilt += 8;
            var dllName = names;
            Encoding.ASCII.GetBytes(dll).CopyTo(rdata, (int)dllName);
            names = Align(dllName + (uint)dll.Length + 1, 2);
            var d = rdata.AsSpan((int)desc, 20);
            BinaryPrimitives.WriteUInt32LittleEndian(d, RdataRva + dllIlt); // OriginalFirstThunk
            BinaryPrimitives.WriteUInt32LittleEndian(d[12..], RdataRva + dllName); // Name
            BinaryPrimitives.WriteUInt32LittleEndian(d[16..], RdataRva + dllIat); // FirstThunk
            desc += 20;
        }

        var rdataUsed = names;
        if (rdataUsed > rdata.Length) throw new InvalidOperationException("导入表超出 .rdata");

        // —— 头 ——
        const int peOffset = 0x80;
        const int sectionCount = 3;
        var headersSize = FileAlign;
        var textRaw = Align((uint)Code.Length, FileAlign);
        var rdataRaw = Align(rdataUsed, FileAlign);
        const uint dataRaw = FileAlign;
        var file = new byte[headersSize + textRaw + rdataRaw + dataRaw];
        var s = file.AsSpan();
        s[0] = (byte)'M';
        s[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(s[0x3C..], peOffset);
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(file, peOffset);
        var coff = s[(peOffset + 4)..];
        BinaryPrimitives.WriteUInt16LittleEndian(coff, 0x8664); // AMD64
        BinaryPrimitives.WriteUInt16LittleEndian(coff[2..], sectionCount);
        BinaryPrimitives.WriteUInt16LittleEndian(coff[16..], 240); // SizeOfOptionalHeader
        BinaryPrimitives.WriteUInt16LittleEndian(coff[18..], 0x0001 | 0x0002 | 0x0020); // RELOCS_STRIPPED | EXECUTABLE | LARGE_ADDRESS_AWARE
        var opt = coff[20..];
        BinaryPrimitives.WriteUInt16LittleEndian(opt, 0x20B); // PE32+
        opt[2] = 14; // 链接器版本，任意
        BinaryPrimitives.WriteUInt32LittleEndian(opt[4..], textRaw); // SizeOfCode
        BinaryPrimitives.WriteUInt32LittleEndian(opt[8..], rdataRaw + dataRaw); // SizeOfInitializedData
        BinaryPrimitives.WriteUInt32LittleEndian(opt[16..], TextRva); // AddressOfEntryPoint
        BinaryPrimitives.WriteUInt32LittleEndian(opt[20..], TextRva); // BaseOfCode
        BinaryPrimitives.WriteUInt64LittleEndian(opt[24..], ImageBase);
        BinaryPrimitives.WriteUInt32LittleEndian(opt[32..], SectionAlign);
        BinaryPrimitives.WriteUInt32LittleEndian(opt[36..], FileAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(opt[40..], 6); // OS 版本 6.0
        BinaryPrimitives.WriteUInt16LittleEndian(opt[48..], 6); // 子系统版本 6.0
        BinaryPrimitives.WriteUInt32LittleEndian(opt[56..], DataRva + SectionAlign); // SizeOfImage
        BinaryPrimitives.WriteUInt32LittleEndian(opt[60..], headersSize); // SizeOfHeaders
        BinaryPrimitives.WriteUInt16LittleEndian(opt[68..], 3); // 控制台子系统
        BinaryPrimitives.WriteUInt16LittleEndian(opt[70..], 0x0100 | 0x8000); // NX_COMPAT | TERMINAL_SERVER_AWARE（没有 DYNAMIC_BASE：固定基址）
        BinaryPrimitives.WriteUInt64LittleEndian(opt[72..], 0x100000); // 栈保留
        BinaryPrimitives.WriteUInt64LittleEndian(opt[80..], 0x1000); // 栈提交
        BinaryPrimitives.WriteUInt64LittleEndian(opt[88..], 0x100000); // 堆保留
        BinaryPrimitives.WriteUInt64LittleEndian(opt[96..], 0x1000); // 堆提交
        BinaryPrimitives.WriteUInt32LittleEndian(opt[108..], 16); // 数据目录数
        var dirs = opt[112..];
        BinaryPrimitives.WriteUInt32LittleEndian(dirs[8..], RdataRva + descStart); // 导入表
        BinaryPrimitives.WriteUInt32LittleEndian(dirs[12..], (uint)(Imports.Length + 1) * 20);
        BinaryPrimitives.WriteUInt32LittleEndian(dirs[(12 * 8)..], RdataRva); // IAT
        BinaryPrimitives.WriteUInt32LittleEndian(dirs[(12 * 8 + 4)..], iatSize);

        var sections = opt[240..];
        Section(sections, ".text", (uint)Code.Length, TextRva, textRaw, headersSize, 0x60000020); // CODE | EXECUTE | READ
        Section(sections[40..], ".rdata", rdataUsed, RdataRva, rdataRaw, headersSize + textRaw, 0xC0000040); // INITIALIZED_DATA | READ | WRITE（导入地址表由加载器填写）
        Section(sections[80..], ".data", 0x400, DataRva, dataRaw, headersSize + textRaw + rdataRaw, 0xC0000040); // INITIALIZED_DATA | READ | WRITE
        Code.CopyTo(file, (int)headersSize);
        rdata.AsSpan(0, (int)rdataUsed).CopyTo(s[(int)(headersSize + textRaw)..]);
        return file;
    }

    private static void Section(Span<byte> h, string name, uint virtualSize, uint rva, uint rawSize, uint rawPointer, uint characteristics)
    {
        Encoding.ASCII.GetBytes(name).CopyTo(h);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], virtualSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], rva);
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], rawSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[20..], rawPointer);
        BinaryPrimitives.WriteUInt32LittleEndian(h[36..], characteristics);
    }

    private static uint Align(uint v, uint a) => (v + a - 1) / a * a;
}
