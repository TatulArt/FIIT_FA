using Arithmetic.BigInt.Interfaces;

namespace Arithmetic.BigInt.MultiplyStrategy;

internal class KaratsubaMultiplier : IMultiplier
{
    // Порог: если длина числа (в 32-битных словах) меньше или равна 32,
    // переключаемся на обычное умножение столбиком.
    private const int SchoolbookThreshold = 32;

    // Реализация IMultiplier: принимает числа со знаком, считает модуль
    // рекурсией Карацубы, знак — как XOR знаков операндов.
    public BetterBigInteger Multiply(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        bool isNegative = a.IsNegative ^ b.IsNegative;
        uint[] product = MultiplyKaratsuba(a.GetDigits(), b.GetDigits());
        return new BetterBigInteger(product, isNegative);
    }

    // Перегрузка для работы с "голыми" разрядами: удобна другим стратегиям,
    // которым нужен только модуль произведения.
    public uint[] Multiply(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int leftLen = TrimmedLength(a);
        int rightLen = TrimmedLength(b);

        return MultiplyKaratsuba(a[..leftLen], b[..rightLen]);
    }

    // Рекурсивный алгоритм Карацубы на ReadOnlySpan
    public static uint[] MultiplyKaratsuba(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        int leftLength = TrimmedLength(left);
        int rightLength = TrimmedLength(right);

        // Если одно из чисел пустое, результат тоже пустой.
        if (leftLength == 0 || rightLength == 0)
        {
            return [];
        }

        // n — максимальная длина из двух чисел.
        int n = Math.Max(leftLength, rightLength);

        // Base case: если числа маленькие, умножаем столбиком.
        if (n <= SchoolbookThreshold)
        {
            return SimpleMultiplier.MultiplyMagnitude(left, right);
        }

        // Точка разбиения: делим числа пополам.
        int split = n / 2;

        // ИДЕАЛЬНАЯ НАРЕЗКА ЧЕРЕЗ SPAN: Нулевое выделение памяти в куче (Zero Allocation)
        ReadOnlySpan<uint> leftLow = left[..Math.Min(split, leftLength)];
        ReadOnlySpan<uint> leftHigh = left[Math.Min(split, leftLength)..leftLength];

        ReadOnlySpan<uint> rightLow = right[..Math.Min(split, rightLength)];
        ReadOnlySpan<uint> rightHigh = right[Math.Min(split, rightLength)..rightLength];

        // Три рекурсивных умножения (вместо четырёх).
        // z0 = младшие части: a0 * b0
        uint[] z0 = MultiplyKaratsuba(leftLow, rightLow);

        // z2 = старшие части: a1 * b1
        uint[] z2 = MultiplyKaratsuba(leftHigh, rightHigh);

        // z1 = (a0 + a1) * (b0 + b1)
        // При сложении выделяется память под сумму (uint[]), это неизбежно и правильно
        uint[] sumLeft = AddMagnitude(leftLow, leftHigh);
        uint[] sumRight = AddMagnitude(rightLow, rightHigh);
        uint[] z1 = MultiplyKaratsuba(sumLeft, sumRight);

        // Хитрость Карацубы: вычитаем z0 и z2, остаётся (a0*b1 + a1*b0)
        z1 = SubtractMagnitude(z1, z0);
        z1 = SubtractMagnitude(z1, z2);

        // Собираем результат: answer = z0 + z1 * B^split + z2 * B^(2*split)
        uint[] partLow = z0;
        uint[] partMid = ShiftWords(z1, split);
        uint[] partHigh = ShiftWords(z2, 2 * split);

        return AddMagnitude(partLow, AddMagnitude(partMid, partHigh));
    }

    // Сложение двух спанов слов (принимает ReadOnlySpan для экономии памяти)
    private static uint[] AddMagnitude(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        int leftLen = TrimmedLength(left);
        int rightLen = TrimmedLength(right);
        int max = Math.Max(leftLen, rightLen);
        
        uint[] result = new uint[max + 1]; 
        ulong carry = 0;                   

        for (int i = 0; i < max; i++)
        {
            ulong current = carry;
            if (i < leftLen) current += left[i];
            if (i < rightLen) current += right[i];

            result[i] = (uint)current;
            carry = current >> 32;
        }

        result[max] = (uint)carry;
        return Normalize(result);
    }

    // Вычитание: left - right (left должен быть >= right)
    private static uint[] SubtractMagnitude(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        int leftLen = TrimmedLength(left);
        int rightLen = TrimmedLength(right);

        uint[] result = new uint[leftLen];
        long borrow = 0; 

        for (int i = 0; i < leftLen; i++)
        {
            long current = (long)left[i] - borrow - (i < rightLen ? right[i] : 0);

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

        return Normalize(result);
    }

    // Сдвиг массива слов влево (навешивание нулей в младшие разряды little-endian)
    private static uint[] ShiftWords(ReadOnlySpan<uint> digits, int wordShift)
    {
        int len = TrimmedLength(digits);
        if (len == 0) return [];
        if (wordShift <= 0) return [.. digits[..len]];

        uint[] result = new uint[len + wordShift];
        
        // Быстрое системное копирование со сдвигом индексов
        digits[..len].CopyTo(result.AsSpan(wordShift));
        return result;
    }

    // Вспомогательный метод для подсчета реальной длины без ведущих нулей
    private static int TrimmedLength(ReadOnlySpan<uint> digits)
    {
        int length = digits.Length;
        while (length > 0 && digits[length - 1] == 0)
        {
            length--;
        }
        return length;
    }

    // Аллоцирует новый усеченный массив только если реально есть ведущие нули
    private static uint[] Normalize(uint[] digits)
    {
        int length = digits.Length;
        while (length > 0 && digits[length - 1] == 0)
        {
            length--;
        }

        if (length == 0) return [];
        if (length == digits.Length) return digits;

        uint[] result = new uint[length];
        Array.Copy(digits, result, length);
        return result;
    }
}