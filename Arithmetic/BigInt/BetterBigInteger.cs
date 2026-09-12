using Arithmetic.BigInt.Interfaces;
using Arithmetic.BigInt.MultiplyStrategy;

namespace Arithmetic.BigInt;

// Длинное целое число в формате "знак + модуль" (sign-magnitude).
// Разряды хранятся по основанию 2^32, little-endian (младший разряд — по индексу 0).
// Если число по модулю помещается в один uint, оно хранится в _smallValue, а _data == null.
public sealed class BetterBigInteger : IBigInteger
{
    private int _signBit;      // 0 - положительное (или ноль), 1 - отрицательное
    private uint _smallValue;  // используется, когда _data == null
    private uint[]? _data;     // используется, когда число не помещается в один uint

    public static readonly BetterBigInteger Zero = new([0], false);
    public static readonly BetterBigInteger One = new([1], false);
    public static readonly BetterBigInteger MinusOne = new([1], true);

    public bool IsNegative => _signBit == 1;

    private bool IsZeroValue => _data is null && _smallValue == 0;

    // ---------------------------------------------------------------------
    // Конструкторы
    // ---------------------------------------------------------------------
    
    // От массива цифр (little-endian). Массив нормализуется: обрезаются старшие
    // нулевые разряды, а isNegative для нуля игнорируется.
    public BetterBigInteger(uint[] digits, bool isNegative = false)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length == 0) digits = [0];

        uint[] trimmed = TrimLeadingZeros(digits);
        bool isZero = trimmed.Length == 1 && trimmed[0] == 0;

        _signBit = (!isZero && isNegative) ? 1 : 0;

        if (trimmed.Length == 1)
        {
            _smallValue = trimmed[0];
            _data = null;
        }
        else
        {
            _data = trimmed;
        }
    }

    // От перечисления цифр (little-endian).
    public BetterBigInteger(IEnumerable<uint> digits, bool isNegative = false)
        : this((digits ?? throw new ArgumentNullException(nameof(digits))).ToArray(), isNegative)
    {
    }

    // От строкового представления числа в системе счисления radix (2..32).
    public BetterBigInteger(string value, int radix)
        : this(ParseDigits(value, radix, out bool isNegative), isNegative)
    {
    }
    
    // Доступ к данным
    public ReadOnlySpan<uint> GetDigits() => _data ?? [_smallValue];
    
    // Сравнение
    public int CompareTo(IBigInteger? other)
    {
        if (other is null) return 1;
        if (IsNegative != other.IsNegative) return IsNegative ? -1 : 1;

        int magnitudeCmp = CompareMagnitudes(GetDigits(), other.GetDigits());
        return IsNegative ? -magnitudeCmp : magnitudeCmp;
    }

    public bool Equals(IBigInteger? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is IBigInteger other && Equals(other);

    public override int GetHashCode()
    {
        var digits = GetDigits();
        HashCode hash = new();
        hash.Add(IsNegative);
        for (int i = 0; i < digits.Length; i++)
        {
            hash.Add(digits[i]);
        }
        return hash.ToHashCode();
    }

    // ---------------------------------------------------------------------
    // Арифметика
    // ---------------------------------------------------------------------

    public static BetterBigInteger operator +(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.IsNegative == b.IsNegative)
        {
            uint[] sum = AddMagnitudes(a.GetDigits(), b.GetDigits());
            return new BetterBigInteger(sum, a.IsNegative);
        }

        int cmp = CompareMagnitudes(a.GetDigits(), b.GetDigits());
        if (cmp == 0) return Zero;

        return cmp > 0
            ? new BetterBigInteger(SubtractMagnitudes(a.GetDigits(), b.GetDigits()), a.IsNegative)
            : new BetterBigInteger(SubtractMagnitudes(b.GetDigits(), a.GetDigits()), b.IsNegative);
    }

    public static BetterBigInteger operator -(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a + (-b);
    }

    public static BetterBigInteger operator -(BetterBigInteger a)
    {
        ArgumentNullException.ThrowIfNull(a);
        // Конструктор сам канонизирует ноль как положительный, поэтому -0 == 0.
        return new BetterBigInteger(a.GetDigits().ToArray(), !a.IsNegative);
    }

    public static BetterBigInteger operator *(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        SimpleMultiplier mult = new();
        return mult.Multiply(a, b);
    }

    public static BetterBigInteger operator /(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        (uint[] quotient, _) = DivModMagnitudes(a.GetDigits(), b.GetDigits());
        bool negative = a.IsNegative != b.IsNegative; // XOR знаков; для нуля конструктор сам сбросит знак
        return new BetterBigInteger(quotient, negative);
    }

    public static BetterBigInteger operator %(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        (_, uint[] remainder) = DivModMagnitudes(a.GetDigits(), b.GetDigits());
        // Остаток берёт знак делимого (усечённое деление, как в стандартных числовых типах .NET).
        return new BetterBigInteger(remainder, a.IsNegative);
    }

    // ---------------------------------------------------------------------
    // Побитовые операции
    // ---------------------------------------------------------------------
    
    // ~x = -x - 1 - универсальное тождество, верное для любых целых чисел
    // (в том числе отрицательных), поэтому вычисляется через уже корректные
    // операции сложения/смены знака и не требует построения дополнительного кода.
    public static BetterBigInteger operator ~(BetterBigInteger a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return -(a + One);
    }

    public static BetterBigInteger operator &(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        RequireNonNegative(a);
        RequireNonNegative(b);
        return new BetterBigInteger(BitwiseAndMagnitudes(a.GetDigits(), b.GetDigits()), false);
    }

    public static BetterBigInteger operator |(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        RequireNonNegative(a);
        RequireNonNegative(b);
        return new BetterBigInteger(BitwiseOrMagnitudes(a.GetDigits(), b.GetDigits()), false);
    }

    public static BetterBigInteger operator ^(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        RequireNonNegative(a);
        RequireNonNegative(b);
        return new BetterBigInteger(BitwiseXorMagnitudes(a.GetDigits(), b.GetDigits()), false);
    }

    private static void RequireNonNegative(BetterBigInteger value)
    {
        if (value.IsNegative)
        {
            throw new InvalidOperationException(
                "Операции &, | и ^ в данной реализации определены только для неотрицательных чисел: " +
                "число хранится в формате \"знак + модуль\", и для корректной побитовой семантики " +
                "отрицательных чисел потребовалось бы отдельное построение дополнительного кода.");
        }
    }

    public static BetterBigInteger operator <<(BetterBigInteger a, int shift)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (shift < 0) return a >> (-shift);
        if (shift == 0) return new BetterBigInteger(a.GetDigits().ToArray(), a.IsNegative);

        var digits = a.GetDigits();
        int wordShift = shift / 32;
        int bitShift = shift % 32;
        uint[] result = new uint[digits.Length + wordShift + 1];

        for (int i = 0; i < digits.Length; i++)
        {
            ulong shifted = (ulong)digits[i] << bitShift;
            result[i + wordShift] |= (uint)shifted;
            if (bitShift != 0)
            {
                result[i + wordShift + 1] |= (uint)(shifted >> 32);
            }
        }

        return new BetterBigInteger(result, a.IsNegative);
    }

    public static BetterBigInteger operator >>(BetterBigInteger a, int shift)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (shift < 0) return a << (-shift);
        if (shift == 0) return new BetterBigInteger(a.GetDigits().ToArray(), a.IsNegative);

        var digits = a.GetDigits();
        int wordShift = shift / 32;
        int bitShift = shift % 32;

        // Сдвиг выполняется над модулем, то есть округляет частное к нулю.
        // Арифметический сдвиг вправо должен округлять вниз (к минус бесконечности),
        // поэтому для отрицательного числа, у которого хотя бы один из выброшенных
        // младших битов был единицей, модуль результата увеличивается на единицу.
        bool roundDown = a.IsNegative && HasLowBitsSet(digits, shift);

        if (wordShift >= digits.Length) return roundDown ? MinusOne : Zero;

        int resultLen = digits.Length - wordShift;
        uint[] result = new uint[resultLen];
        for (int i = 0; i < resultLen; i++)
        {
            ulong low = digits[i + wordShift];
            ulong high = (i + wordShift + 1 < digits.Length) ? digits[i + wordShift + 1] : 0;
            result[i] = bitShift == 0 ? (uint)low : (uint)((low >> bitShift) | (high << (32 - bitShift)));
        }

        BetterBigInteger shifted = new(result, a.IsNegative);
        return roundDown ? shifted - One : shifted;
    }

    // Проверяет, есть ли среди младших bitCount битов числа хотя бы одна единица.
    private static bool HasLowBitsSet(ReadOnlySpan<uint> value, int bitCount)
    {
        int wholeWords = Math.Min(bitCount / 32, value.Length);
        for (int i = 0; i < wholeWords; i++)
        {
            if (value[i] != 0) return true;
        }

        int restBits = bitCount % 32;
        if (restBits != 0 && wholeWords < value.Length)
        {
            uint mask = (1u << restBits) - 1u;
            if ((value[wholeWords] & mask) != 0) return true;
        }

        return false;
    }

    // ---------------------------------------------------------------------
    // Операторы сравнения
    // ---------------------------------------------------------------------

    public static bool operator ==(BetterBigInteger a, BetterBigInteger b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.Equals(b));

    public static bool operator !=(BetterBigInteger a, BetterBigInteger b) => !(a == b);

    public static bool operator <(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.CompareTo(b) < 0;
    }

    public static bool operator >(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.CompareTo(b) > 0;
    }

    public static bool operator <=(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.CompareTo(b) <= 0;
    }

    public static bool operator >=(BetterBigInteger a, BetterBigInteger b)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.CompareTo(b) >= 0;
    }

    // ---------------------------------------------------------------------
    // Преобразование в строку
    // ---------------------------------------------------------------------

    public override string ToString() => ToString(10);

    public string ToString(int radix)
    {
        if (radix is < 2 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(radix), "Основание системы счисления должно быть от 2 до 32.");
        }

        if (IsZeroValue) return "0";

        uint[] digits = GetDigits().ToArray();
        List<char> result = [];

        while (!(digits.Length == 1 && digits[0] == 0))
        {
            List<uint> quotient = [];
            ulong remainder = 0;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                ulong current = digits[i] + (remainder << 32);
                quotient.Add((uint)(current / (ulong)radix));
                remainder = current % (ulong)radix;
            }

            result.Add(ToChar(remainder));
            quotient.Reverse();
            while (quotient.Count > 1 && quotient[^1] == 0)
            {
                quotient.RemoveAt(quotient.Count - 1);
            }
            digits = quotient.ToArray();
        }

        if (IsNegative) result.Add('-');
        result.Reverse();
        return new string(result.ToArray());
    }

    private static char ToChar(ulong digit) => digit > 9 ? (char)('A' + digit - 10) : (char)('0' + digit);

    // ---------------------------------------------------------------------
    // Разбор строки (парсинг)
    // ---------------------------------------------------------------------

    private static uint[] ParseDigits(string value, int radix, out bool isNegative)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (radix is < 2 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(radix), "Основание системы счисления должно быть от 2 до 32.");
        }
        if (value.Length == 0)
        {
            throw new FormatException("Пустая строка не является допустимым числом.");
        }

        isNegative = value[0] == '-';
        int start = (isNegative || value[0] == '+') ? 1 : 0;
        if (start == value.Length)
        {
            throw new FormatException("Строка не содержит ни одной цифры.");
        }

        uint[] digits = [0];
        for (int i = start; i < value.Length; i++)
        {
            int digitValue = ParseDigitChar(value[i], radix);
            digits = MultiplyAddSmall(digits, radix, digitValue);

            int len = digits.Length;
            while (len > 1 && digits[len - 1] == 0) len--;
            if (len != digits.Length) Array.Resize(ref digits, len);
        }

        return digits;
    }

    private static int ParseDigitChar(char c, int radix)
    {
        int value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'Z' => c - 'A' + 10,
            >= 'a' and <= 'z' => c - 'a' + 10,
            _ => -1
        };

        if (value < 0 || value >= radix)
        {
            throw new FormatException($"Символ '{c}' недопустим для системы счисления с основанием {radix}.");
        }

        return value;
    }

    // Один проход Горнера: digits = digits * multiplier + addend.
    private static uint[] MultiplyAddSmall(uint[] digits, int multiplier, int addend)
    {
        uint[] result = new uint[digits.Length + 1];
        ulong carry = (ulong)addend;
        for (int i = 0; i < digits.Length; i++)
        {
            ulong product = (ulong)digits[i] * (ulong)multiplier + carry;
            result[i] = (uint)product;
            carry = product >> 32;
        }
        result[^1] = (uint)carry;
        return result;
    }

    // ---------------------------------------------------------------------
    // Вспомогательные операции над "голыми" модулями (без учёта знака)
    // ---------------------------------------------------------------------

    private static uint[] TrimLeadingZeros(uint[] digits)
    {
        int len = digits.Length;
        while (len > 1 && digits[len - 1] == 0) len--;
        if (len == digits.Length) return digits;

        uint[] trimmed = new uint[len];
        Array.Copy(digits, trimmed, len);
        return trimmed;
    }

    // Длина числа без старших незначащих нулей (минимум 1 разряд).
    // Числа, построенные конструктором, всегда канонизованы, но рабочие буферы
    // внутренних алгоритмов (например, остаток в делении) — нет.
    private static int EffectiveLength(ReadOnlySpan<uint> value)
    {
        int len = value.Length;
        while (len > 1 && value[len - 1] == 0) len--;
        return len;
    }

    private static int CompareMagnitudes(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int lengthA = EffectiveLength(a);
        int lengthB = EffectiveLength(b);
        if (lengthA != lengthB) return lengthA > lengthB ? 1 : -1;
        for (int i = lengthA - 1; i >= 0; i--)
        {
            if (a[i] != b[i]) return a[i] > b[i] ? 1 : -1;
        }
        return 0;
    }

    private static uint[] AddMagnitudes(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int len = Math.Max(a.Length, b.Length);
        uint[] result = new uint[len + 1];
        ulong carry = 0;
        for (int i = 0; i < len; i++)
        {
            ulong sum = carry;
            if (i < a.Length) sum += a[i];
            if (i < b.Length) sum += b[i];
            result[i] = (uint)sum;
            carry = sum >> 32;
        }
        result[len] = (uint)carry;
        return result;
    }

    // Вычитает b из a по модулю. Требует |a| >= |b|.
    private static uint[] SubtractMagnitudes(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        uint[] result = new uint[a.Length];
        long borrow = 0;
        for (int i = 0; i < a.Length; i++)
        {
            long diff = (long)a[i] - (i < b.Length ? b[i] : 0) - borrow;
            if (diff < 0)
            {
                diff += 1L << 32;
                borrow = 1;
            }
            else
            {
                borrow = 0;
            }
            result[i] = (uint)diff;
        }
        return result;
    }

    private static uint[] BitwiseAndMagnitudes(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int len = Math.Min(a.Length, b.Length);
        uint[] result = new uint[Math.Max(len, 1)];
        for (int i = 0; i < len; i++) result[i] = a[i] & b[i];
        return result;
    }

    private static uint[] BitwiseOrMagnitudes(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int len = Math.Max(a.Length, b.Length);
        uint[] result = new uint[len];
        for (int i = 0; i < len; i++)
        {
            result[i] = (i < a.Length ? a[i] : 0) | (i < b.Length ? b[i] : 0);
        }
        return result;
    }

    private static uint[] BitwiseXorMagnitudes(ReadOnlySpan<uint> a, ReadOnlySpan<uint> b)
    {
        int len = Math.Max(a.Length, b.Length);
        uint[] result = new uint[len];
        for (int i = 0; i < len; i++)
        {
            result[i] = (i < a.Length ? a[i] : 0) ^ (i < b.Length ? b[i] : 0);
        }
        return result;
    }

    // ---------------------------------------------------------------------
    // Деление по модулю (двоичное деление "столбиком", бит за битом)
    // ---------------------------------------------------------------------

    private static (uint[] Quotient, uint[] Remainder) DivModMagnitudes(ReadOnlySpan<uint> dividend, ReadOnlySpan<uint> divisor)
    {
        if (divisor.Length == 1 && divisor[0] == 0)
        {
            throw new DivideByZeroException("Деление на ноль.");
        }

        if (CompareMagnitudes(dividend, divisor) < 0)
        {
            return ([0], dividend.ToArray());
        }

        int bitLength = GetBitLength(dividend);
        uint[] quotient = new uint[dividend.Length];
        uint[] remainder = new uint[divisor.Length + 1]; // с запасом на перенос при сдвиге

        for (int bit = bitLength - 1; bit >= 0; bit--)
        {
            ShiftLeftOneInPlace(remainder);
            if (GetBit(dividend, bit))
            {
                remainder[0] |= 1u;
            }

            if (CompareMagnitudes(remainder, divisor) >= 0)
            {
                remainder = SubtractMagnitudes(remainder, divisor);
                quotient[bit / 32] |= 1u << (bit % 32);
            }
        }

        return (quotient, remainder);
    }

    private static int GetBitLength(ReadOnlySpan<uint> value)
    {
        int top = value.Length - 1;
        while (top > 0 && value[top] == 0) top--;

        int bits = top * 32;
        uint word = value[top];
        while (word != 0)
        {
            bits++;
            word >>= 1;
        }
        return bits;
    }

    private static bool GetBit(ReadOnlySpan<uint> value, int index)
    {
        int word = index / 32;
        if (word >= value.Length) return false;
        return ((value[word] >> (index % 32)) & 1u) != 0;
    }

    private static void ShiftLeftOneInPlace(uint[] value)
    {
        uint carry = 0;
        for (int i = 0; i < value.Length; i++)
        {
            uint nextCarry = value[i] >> 31;
            value[i] = (value[i] << 1) | carry;
            carry = nextCarry;
        }
    }
}