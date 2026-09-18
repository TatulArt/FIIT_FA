using Arithmetic.BigInt.Interfaces;

namespace Arithmetic.BigInt.MultiplyStrategy;

// Умножение методом Шёнхаге — Штрассена.
//
// Числа режутся на блоки, произведение сводится к свёртке блоков, а свёртка
// считается через теоретико-числовое БПФ (NTT) в кольце вычетов по модулю
// M = 2^B + 1. Прелесть этого кольца в том, что двойка в нём — корень степени 2B
// из единицы, поэтому умножение на корни в «бабочках» вырождается в битовый сдвиг,
// а взятие остатка — в вычитание старшей половины из младшей (2^B ≡ -1).
internal class FftMultiplier : IMultiplier
{
    // Ниже этого суммарного размера (в 32-битных словах) БПФ не окупается:
    // накладные расходы на преобразование больше самого умножения.
    private const int SchoolbookThreshold = 64;

    // Реализация IMultiplier: принимает числа со знаком, считает модуль
    // преобразованием Фурье, знак — как XOR знаков операндов.
    public BetterBigInteger Multiply(BetterBigInteger first, BetterBigInteger second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        bool resultIsNegative = first.IsNegative ^ second.IsNegative;
        uint[] productDigits = Multiply(first.GetDigits(), second.GetDigits());

        return new BetterBigInteger(productDigits, resultIsNegative);
    }

    // Перегрузка для работы с «голыми» разрядами: удобна другим стратегиям,
    // которым нужен только модуль произведения.
    public uint[] Multiply(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int leftLength = TrimmedLength(a);
        int rightLength = TrimmedLength(b);

        if (leftLength == 0 || rightLength == 0) return [];

        return SchonhageStrassen(a[..leftLength].ToArray(), b[..rightLength].ToArray());
    }

    // Ядро Шёнхаге — Штрассена. Перемножает два беззнаковых массива разрядов.
    private static uint[] SchonhageStrassen(uint[] left, uint[] right)
    {
        if (left.Length == 0 || right.Length == 0) return [];

        int totalLength = left.Length + right.Length;

        // ДНО РЕКУРСИИ: мелкие числа быстрее умножить столбиком,
        // чем разворачивать вокруг них всю машинерию БПФ.
        if (totalLength < SchoolbookThreshold)
        {
            return SimpleMultiplier.MultiplyMagnitude(left, right);
        }

        ChooseParameters(left.Length, right.Length, out int transformSize, out int wordsPerBlock, out int ringWords);

        int modulusBits = ringWords * 32; // B — размер кольца в битах, M = 2^B + 1

        // ШАГ 1. Режем оба числа на блоки по wordsPerBlock слов и дополняем нулями
        // до размера преобразования. Нулевое дополнение делает циклическую свёртку
        // равной обычной: «хвост» свёртки заворачиваться уже не на что.
        uint[][] leftBlocks = DisassembleToBlocks(left, transformSize, wordsPerBlock, ringWords);
        uint[][] rightBlocks = DisassembleToBlocks(right, transformSize, wordsPerBlock, ringWords);

        // ШАГ 2. Прямое преобразование обоих наборов блоков.
        NttSchonhage(leftBlocks, modulusBits, ringWords, invert: false);
        NttSchonhage(rightBlocks, modulusBits, ringWords, invert: false);

        // ШАГ 3. Поточечное умножение образов. Точки — сами по себе большие числа,
        // поэтому здесь алгоритм вызывает сам себя («матрёшечная» рекурсия).
        // Если рекурсия не уменьшает задачу, честнее сразу уйти в столбик.
        bool recurse = 2 * (ringWords + 1) < totalLength;
        for (int i = 0; i < transformSize; i++)
        {
            uint[] rawProduct = recurse
                ? SchonhageStrassen(Trim(leftBlocks[i]), Trim(rightBlocks[i]))
                : SimpleMultiplier.MultiplyMagnitude(Trim(leftBlocks[i]), Trim(rightBlocks[i]));

            leftBlocks[i] = TakeModulus(rawProduct, ringWords);
        }

        // ШАГ 4. Обратное преобразование возвращает коэффициенты свёртки.
        NttSchonhage(leftBlocks, modulusBits, ringWords, invert: true);

        // ШАГ 5. Сборка числа из коэффициентов с переносом разрядов.
        return AssembleFromBlocks(leftBlocks, wordsPerBlock, ringWords);
    }

    // Подбор параметров преобразования.
    //
    // n  = transformSize  — число точек БПФ, обязательно степень двойки;
    // kw = wordsPerBlock  — сколько слов исходного числа попадает в один блок;
    // bw = ringWords      — размер кольца в словах, B = 32*bw.
    //
    // Ограничения, без которых алгоритм даёт неверный ответ:
    //   1) блоков хватает на оба числа: ceil(lu/kw) + ceil(lv/kw) <= n,
    //      иначе свёртка завернётся сама на себя;
    //   2) коэффициент свёртки помещается в кольцо: он меньше n*(2^k)^2 = 2^(2k+t),
    //      поэтому берём B >= 2k + 32, что при bw >= 2*kw + 1 выполняется всегда;
    //   3) корень нужной степени существует: 2 имеет порядок 2B, значит n должно
    //      делить 2B = 64*bw, отсюда выравнивание bw на 2^(t-6).
    private static void ChooseParameters(int leftLength, int rightLength, out int transformSize, out int wordsPerBlock, out int ringWords)
    {
        int totalLength = leftLength + rightLength;

        transformSize = 0;
        wordsPerBlock = 0;
        ringWords = 0;
        double bestCost = double.MaxValue;

        for (int sizeLog = 3; sizeLog <= 24; sizeLog++)
        {
            int size = 1 << sizeLog;
            int blockWords = (totalLength + size - 3) / (size - 2); // ceil(totalLength / (size - 2))

            int blocksNeeded = (leftLength + blockWords - 1) / blockWords
                             + (rightLength + blockWords - 1) / blockWords;
            if (blocksNeeded > size) continue;

            int alignment = sizeLog > 6 ? 1 << (sizeLog - 6) : 1;
            int minimalRingWords = 2 * blockWords + 1;
            int ring = (minimalRingWords + alignment - 1) / alignment * alignment;

            // Поточечные умножения плюс сами преобразования (три штуки по n*log(n) бабочек).
            double cost = (double)size * ring * ring + 3.0 * size * sizeLog * ring;
            if (cost >= bestCost) continue;

            bestCost = cost;
            transformSize = size;
            wordsPerBlock = blockWords;
            ringWords = ring;
        }
    }

    // Теоретико-числовое преобразование (NTT) Шёнхаге — Штрассена.
    // Работает над массивом блоков по модулю 2^B + 1.
    // Все умножения на корни выполняются исключительно битовыми сдвигами.
    private static void NttSchonhage(uint[][] blocks, int modulusBits, int ringWords, bool invert)
    {
        int n = blocks.Length;
        int fullTurn = 2 * modulusBits; // порядок двойки в кольце: 2^(2B) = 1

        // 1. Бит-реверсивная перестановка блоков для вычислений на месте.
        // Мы берём порядковый номер ячейки, смотрим на него в двоичном виде,
        // зеркально разворачиваем эту последовательность нулей и единиц, получаем
        // новый номер ячейки и переносим элемент туда. Это раскладывает элементы
        // ровно в том порядке, в каком их требует деление на чётные и нечётные.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
            j ^= bit;
            if (i < j) (blocks[i], blocks[j]) = (blocks[j], blocks[i]);
        }

        // 2. Основной цикл «бабочек».
        for (int blockSize = 2; blockSize <= n; blockSize <<= 1)
        {
            int halfBlockSize = blockSize >> 1;
            int stepShift = fullTurn / blockSize; // корень степени blockSize — это 2^stepShift

            for (int blockStart = 0; blockStart < n; blockStart += blockSize)
            {
                for (int j = 0; j < halfBlockSize; j++)
                {
                    // Степень корня: в Шёнхаге — Штрассене корень это всегда двойка,
                    // поэтому умножение на него — просто битовый сдвиг.
                    int currentRootShift = invert
                        ? (fullTurn - j * stepShift) % fullTurn
                        : j * stepShift;

                    uint[] u = blocks[blockStart + j];
                    uint[] v = ShiftBitsInRing(blocks[blockStart + j + halfBlockSize], currentRootShift, modulusBits, ringWords);

                    blocks[blockStart + j] = AddInRing(u, v, ringWords);
                    blocks[blockStart + j + halfBlockSize] = SubtractInRing(u, v, ringWords);
                }
            }
        }

        // 3. Деление на размер задачи в конце обратного преобразования.
        // 1/n = 2^(-t) — снова всего лишь сдвиг по кольцу.
        if (invert)
        {
            int shiftAmount = (fullTurn - GetBitLength(n)) % fullTurn;
            for (int i = 0; i < n; i++)
            {
                blocks[i] = ShiftBitsInRing(blocks[i], shiftAmount, modulusBits, ringWords);
            }
        }
    }

    // Умножение на 2^shift внутри кольца вычетов по модулю 2^B + 1.
    // Заменяет тяжёлое умножение на комплексные экспоненты обычного БПФ.
    private static uint[] ShiftBitsInRing(uint[] value, int shift, int modulusBits, int ringWords)
    {
        int fullTurn = 2 * modulusBits;
        shift %= fullTurn;
        if (shift < 0) shift += fullTurn;

        // Сдвиг больше чем на B бит по правилу 2^B = -1 равносилен
        // сдвигу на (shift - B) бит со сменой знака.
        bool changeSign = shift >= modulusBits;
        if (changeSign) shift -= modulusBits;

        uint[] result = TakeModulus(ShiftLeftBits(value, shift), ringWords);
        return changeSign ? NegateInRing(result, ringWords) : result;
    }

    // Взятие остатка по модулю 2^B + 1 без деления.
    // Правило Шёнхаге — Штрассена: 2^B = -1, значит старшая половина
    // вычитается из младшей. Результат приводится к отрезку [0, 2^B]
    // и всегда занимает ровно ringWords + 1 слов.
    private static uint[] TakeModulus(uint[] value, int ringWords)
    {
        uint[] current = value;

        while (true)
        {
            int length = TrimmedLength(current);

            // Число уже меньше 2^B — это канонический вид.
            if (length <= ringWords) return Resize(current, ringWords + 1);

            // Ровно 2^B — тоже допустимый вычет, дальше сворачивать нечего.
            if (length == ringWords + 1 && current[ringWords] == 1 && TrimmedLength(current.AsSpan(0, ringWords)) == 0)
            {
                return Resize(current, ringWords + 1);
            }

            uint[] low = Slice(current, 0, ringWords);
            uint[] high = Slice(current, ringWords, length - ringWords);

            // low - high, а при нехватке занимаем целый модуль: low + (2^B + 1) - high.
            current = CompareMagnitudes(low, high) >= 0
                ? SubtractMagnitudes(low, high)
                : SubtractMagnitudes(AddMagnitudes(low, Modulus(ringWords)), high);
        }
    }

    // Сложение в кольце вычетов по модулю 2^B + 1.
    private static uint[] AddInRing(uint[] left, uint[] right, int ringWords)
        => TakeModulus(AddMagnitudes(left, right), ringWords);

    // Вычитание в кольце вычетов по модулю 2^B + 1.
    private static uint[] SubtractInRing(uint[] left, uint[] right, int ringWords)
        => AddInRing(left, NegateInRing(right, ringWords), ringWords);

    // Унарный минус в кольце вычетов: -X = (2^B + 1) - X, ноль остаётся нулём.
    private static uint[] NegateInRing(uint[] value, int ringWords)
    {
        if (TrimmedLength(value) == 0) return new uint[ringWords + 1];
        return TakeModulus(SubtractMagnitudes(Modulus(ringWords), value), ringWords);
    }

    // Модуль кольца M = 2^B + 1 в виде массива разрядов.
    private static uint[] Modulus(int ringWords)
    {
        uint[] modulus = new uint[ringWords + 1];
        modulus[0] = 1;           // единица
        modulus[ringWords] = 1;   // и 2^B
        return modulus;
    }

    // =========================================================================
    // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ СБОРКИ БЛОКОВ И СТОЛБИКОВОЙ АРИФМЕТИКИ
    // =========================================================================

    // Нарезка длинного числа на массив независимых блоков.
    // Каждый блок несёт wordsPerBlock слов данных, но живёт в кольце,
    // поэтому под него отводится ringWords + 1 слов.
    private static uint[][] DisassembleToBlocks(uint[] digits, int transformSize, int wordsPerBlock, int ringWords)
    {
        uint[][] blocks = new uint[transformSize][];
        for (int i = 0; i < transformSize; i++)
        {
            uint[] block = new uint[ringWords + 1];
            int startSourceIndex = i * wordsPerBlock;

            for (int j = 0; j < wordsPerBlock && startSourceIndex + j < digits.Length; j++)
            {
                block[j] = digits[startSourceIndex + j];
            }

            blocks[i] = block;
        }
        return blocks;
    }

    // Сборка итогового числа из коэффициентов свёртки.
    // Коэффициент с номером i имеет вес (2^32)^(i * wordsPerBlock),
    // поэтому кладётся со смещением и переносом в старшие разряды.
    private static uint[] AssembleFromBlocks(uint[][] blocks, int wordsPerBlock, int ringWords)
    {
        uint[] result = new uint[blocks.Length * wordsPerBlock + ringWords + 2];

        for (int i = 0; i < blocks.Length; i++)
        {
            int targetStartIndex = i * wordsPerBlock;
            uint[] currentBlock = blocks[i];
            ulong carry = 0;

            for (int j = 0; j < currentBlock.Length; j++)
            {
                ulong currentSum = result[targetStartIndex + j] + (ulong)currentBlock[j] + carry;
                result[targetStartIndex + j] = (uint)currentSum;
                carry = currentSum >> 32;
            }

            // Проталкиваем оставшийся перенос дальше в старшие разряды.
            int nextCarryIndex = targetStartIndex + currentBlock.Length;
            while (carry != 0)
            {
                ulong currentSum = result[nextCarryIndex] + carry;
                result[nextCarryIndex] = (uint)currentSum;
                carry = currentSum >> 32;
                nextCarryIndex++;
            }
        }

        return Normalize(result);
    }

    // Беззнаковое сложение двух массивов разрядов столбиком.
    private static uint[] AddMagnitudes(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        int max = Math.Max(left.Length, right.Length);
        uint[] result = new uint[max + 1];
        ulong carry = 0;

        for (int i = 0; i < max; i++)
        {
            ulong sum = carry + (i < left.Length ? left[i] : 0) + (i < right.Length ? right[i] : 0);
            result[i] = (uint)sum;
            carry = sum >> 32;
        }

        result[max] = (uint)carry;
        return result;
    }

    // Беззнаковое вычитание столбиком, требует left >= right.
    private static uint[] SubtractMagnitudes(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        uint[] result = new uint[left.Length];
        long borrow = 0;

        for (int i = 0; i < left.Length; i++)
        {
            long current = (long)left[i] - borrow - (i < right.Length ? right[i] : 0);
            if (current < 0)
            {
                current += 1L << 32;
                borrow = 1;
            }
            else
            {
                borrow = 0;
            }
            result[i] = (uint)current;
        }

        return result;
    }

    // Сравнение модулей: сначала по значащей длине, затем по старшим разрядам.
    private static int CompareMagnitudes(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        int leftLength = TrimmedLength(left);
        int rightLength = TrimmedLength(right);
        if (leftLength != rightLength) return leftLength > rightLength ? 1 : -1;

        for (int i = leftLength - 1; i >= 0; i--)
        {
            if (left[i] != right[i]) return left[i] > right[i] ? 1 : -1;
        }
        return 0;
    }

    // Битовый сдвиг влево для массива разрядов.
    private static uint[] ShiftLeftBits(ReadOnlySpan<uint> digits, int bitShift)
    {
        int wordShift = bitShift / 32;
        int actualBitShift = bitShift % 32;
        uint[] result = new uint[digits.Length + wordShift + 1];
        ulong carry = 0;

        for (int i = 0; i < digits.Length; i++)
        {
            ulong current = ((ulong)digits[i] << actualBitShift) | carry;
            result[i + wordShift] = (uint)current;
            carry = current >> 32;
        }

        result[digits.Length + wordShift] = (uint)carry;
        return result;
    }

    // Двоичный логарифм степени двойки: для n = 2^t возвращает t.
    private static int GetBitLength(int value)
    {
        int bits = 0;
        while (value > 1)
        {
            value >>= 1;
            bits++;
        }
        return bits;
    }

    // Значащая длина числа — без старших нулевых разрядов.
    private static int TrimmedLength(ReadOnlySpan<uint> digits)
    {
        int length = digits.Length;
        while (length > 0 && digits[length - 1] == 0) length--;
        return length;
    }

    // Копия куска массива фиксированной длины.
    private static uint[] Slice(uint[] digits, int start, int length)
    {
        uint[] result = new uint[length];
        Array.Copy(digits, start, result, 0, Math.Min(length, digits.Length - start));
        return result;
    }

    // Копия массива ровно заданной длины (лишние старшие разряды нулевые).
    private static uint[] Resize(uint[] digits, int length)
    {
        uint[] result = new uint[length];
        Array.Copy(digits, result, Math.Min(length, digits.Length));
        return result;
    }

    // Копия без старших нулевых разрядов.
    private static uint[] Trim(uint[] digits) => Normalize(digits);

    // Отрезает незначащие ведущие нули со старших индексов массива.
    private static uint[] Normalize(uint[] digits)
    {
        int length = TrimmedLength(digits);

        if (length == 0) return [];
        if (length == digits.Length) return digits;

        uint[] result = new uint[length];
        Array.Copy(digits, result, length);
        return result;
    }
}
