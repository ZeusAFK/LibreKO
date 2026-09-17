using System.Buffers;

namespace LibreKO.Network;

public static class Lzf
{
    private const int MaxLit = 1 << 5;    // 32
    private const int MaxOff = 1 << 13;   // 8192
    private const int MaxRef = (1 << 8) + (1 << 3); // 264
    private const int HashTableSize = 1 << 16; // 65536

    public static int Compress(byte[] input, int inputLength, byte[] output, int outputLength)
    {
        var hashTable = ArrayPool<int>.Shared.Rent(HashTableSize);
        Array.Fill(hashTable, -1, 0, HashTableSize);
        try
        {
        return CompressCore(input, inputLength, output, outputLength, hashTable);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(hashTable);
        }
    }

    private static int CompressCore(byte[] input, int inputLength, byte[] output, int outputLength, int[] hashTable)
    {

        if (inputLength == 0 || outputLength == 0)
            return 0;

        int inputIndex = 0;
        int outputIndex = 1; // Reserve the first byte for the initial literal run length.
        int lit = 0;

        int hval = (input[0] << 8) | input[1];

        while (inputIndex < inputLength - 2)
        {
            hval = (hval << 8) | input[inputIndex + 2];
            int hslot = ((hval >> 1) + hval) >> 1;
            hslot &= HashTableSize - 1;

            int reference = hashTable[hslot];
            hashTable[hslot] = inputIndex;

            int off;
            if (reference >= 0
                && reference < inputIndex
                && (off = inputIndex - reference - 1) < MaxOff
                && inputIndex + 4 < inputLength
                && input[reference] == input[inputIndex]
                && input[reference + 1] == input[inputIndex + 1]
                && input[reference + 2] == input[inputIndex + 2])
            {
                int len = 2;
                int maxLen = Math.Min(inputLength - inputIndex - len, MaxRef);

                if (lit > 0)
                    output[outputIndex - lit - 1] = (byte)(lit - 1);
                else
                    outputIndex--; // Undo the empty literal run.

                while (len < maxLen && input[reference + len] == input[inputIndex + len])
                    len++;

                if (outputIndex + 3 >= outputLength)
                    return 0;

                len -= 2;
                inputIndex++;
                if (len < 7)
                {
                    output[outputIndex++] = (byte)((off >> 8) + (len << 5));
                    output[outputIndex++] = (byte)off;
                }
                else
                {
                    output[outputIndex++] = (byte)((off >> 8) + (7 << 5));
                    output[outputIndex++] = (byte)(len - 7);
                    output[outputIndex++] = (byte)off;
                }

                lit = 0;
                if (outputIndex >= outputLength)
                    return 0;

                outputIndex++; // Start the next literal run.
                inputIndex += len + 1;

                if (inputIndex < inputLength - 2)
                    hval = (input[inputIndex] << 8) | input[inputIndex + 1];

                continue;
            }

            if (outputIndex >= outputLength)
                return 0;

            lit++;
            output[outputIndex++] = input[inputIndex++];

            if (lit == MaxLit)
            {
                output[outputIndex - lit - 1] = (byte)(lit - 1);
                lit = 0;
                if (outputIndex >= outputLength)
                    return 0;
                outputIndex++;
            }
        }

        if (outputIndex + 3 > outputLength)
            return 0;

        while (inputIndex < inputLength)
        {
            lit++;
            output[outputIndex++] = input[inputIndex++];

            if (lit == MaxLit)
            {
                output[outputIndex - lit - 1] = (byte)(lit - 1);
                lit = 0;
                if (inputIndex < inputLength)
                {
                    if (outputIndex >= outputLength)
                        return 0;
                }
                outputIndex++;
            }
        }

        if (lit > 0)
            output[outputIndex - lit - 1] = (byte)(lit - 1);
        else
            outputIndex--;

        return outputIndex;
    }

    public static int Decompress(byte[] input, int inputLength, byte[] output, int outputLength)
    {
        int inputIndex = 0;
        int outputIndex = 0;

        while (inputIndex < inputLength)
        {
            int ctrl = input[inputIndex++];

            if (ctrl < (1 << 5)) // Literal run
            {
                ctrl++;
                if (outputIndex + ctrl > outputLength) return 0;
                if (inputIndex + ctrl > inputLength) return 0;

                Array.Copy(input, inputIndex, output, outputIndex, ctrl);
                inputIndex += ctrl;
                outputIndex += ctrl;
            }
            else // Backreference
            {
                int len = ctrl >> 5;
                int reference = outputIndex - ((ctrl & 0x1f) << 8) - 1;

                if (inputIndex >= inputLength) return 0;

                if (len == 7)
                {
                    len += input[inputIndex++];
                    if (inputIndex >= inputLength) return 0;
                }

                reference -= input[inputIndex++];
                len += 2;

                if (outputIndex + len > outputLength) return 0;
                if (reference < 0) return 0;

                // Copy byte-by-byte (may overlap)
                for (int i = 0; i < len; i++)
                    output[outputIndex++] = output[reference++];
            }
        }

        return outputIndex;
    }

    public const int Margin = 128;
}
