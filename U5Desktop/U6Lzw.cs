using System;

// C# port of Nuvie's U6Lzw (GPL) — the standard variable-length LZW used by
// Ultima 5 & 6 data files (.16 etc.). File begins with a 4-byte little-endian
// uncompressed size, then a 9->12-bit LZW stream (0x100 = clear dictionary,
// 0x101 = end marker).
internal static class U6Lzw
{
    private const int DictSize = 10000;
    private const int StackSize = 10000;

    public static byte[] Decompress(byte[] source)
    {
        int uncompSize = source[0] | (source[1] << 8) | (source[2] << 16) | (source[3] << 24);
        var dest = new byte[uncompSize];

        var dictRoot = new byte[DictSize];
        var dictWord = new int[DictSize];
        int dictContains = 0x102;

        var stack = new byte[StackSize];
        int stackTop = 0; // count of elements

        const int maxCodewordLength = 12;
        int codewordSize = 9;
        long bitsRead = 0;
        int nextFreeCodeword = 0x102;
        int dictionarySize = 0x200;
        long bytesWritten = 0;

        int pW = 0;
        int off = 4; // stream starts after the 4-byte size header
        bool end = false;

        while (!end)
        {
            int cW = GetNextCodeword(ref bitsRead, source, off, codewordSize);
            switch (cW)
            {
                case 0x100:
                    codewordSize = 9;
                    nextFreeCodeword = 0x102;
                    dictionarySize = 0x200;
                    dictContains = 0x102;
                    cW = GetNextCodeword(ref bitsRead, source, off, codewordSize);
                    dest[bytesWritten++] = (byte)cW;
                    break;
                case 0x101:
                    end = true;
                    break;
                default:
                    byte C;
                    if (cW < nextFreeCodeword)
                    {
                        stackTop = GetString(cW, dictRoot, dictWord, stack);
                        C = stack[stackTop - 1];
                        while (stackTop > 0) dest[bytesWritten++] = stack[--stackTop];
                        dictRoot[dictContains] = C; dictWord[dictContains] = pW; dictContains++;
                        nextFreeCodeword++;
                        if (nextFreeCodeword >= dictionarySize && codewordSize < maxCodewordLength)
                        { codewordSize++; dictionarySize *= 2; }
                    }
                    else
                    {
                        stackTop = GetString(pW, dictRoot, dictWord, stack);
                        C = stack[stackTop - 1];
                        while (stackTop > 0) dest[bytesWritten++] = stack[--stackTop];
                        dest[bytesWritten++] = C;
                        if (cW != nextFreeCodeword)
                            throw new Exception("U6Lzw: cW != next_free_codeword (corrupt stream)");
                        dictRoot[dictContains] = C; dictWord[dictContains] = pW; dictContains++;
                        nextFreeCodeword++;
                        if (nextFreeCodeword >= dictionarySize && codewordSize < maxCodewordLength)
                        { codewordSize++; dictionarySize *= 2; }
                    }
                    break;
            }
            pW = cW;
        }
        return dest;
    }

    // build string for codeword onto stack; returns element count
    private static int GetString(int codeword, byte[] dictRoot, int[] dictWord, byte[] stack)
    {
        int n = 0;
        int cur = codeword;
        while (cur > 0xff)
        {
            stack[n++] = dictRoot[cur];
            cur = dictWord[cur];
        }
        stack[n++] = (byte)cur;
        return n;
    }

    private static int GetNextCodeword(ref long bitsRead, byte[] source, int off, int codewordSize)
    {
        int i = off + (int)(bitsRead / 8);
        int b0 = source[i];
        int b1 = i + 1 < source.Length ? source[i + 1] : 0;
        int b2 = (codewordSize + (int)(bitsRead % 8) > 16 && i + 2 < source.Length) ? source[i + 2] : 0;
        int codeword = (b2 << 16) + (b1 << 8) + b0;
        codeword >>= (int)(bitsRead % 8);
        codeword &= (1 << codewordSize) - 1;
        bitsRead += codewordSize;
        return codeword;
    }
}
