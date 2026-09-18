using Arithmetic.BigInt.Interfaces;

namespace Arithmetic.BigInt.MultiplyStrategy;

internal class SimpleMultiplier : IMultiplier
{
    public BetterBigInteger Multiply(BetterBigInteger multiplier, BetterBigInteger multiplicand)
    {
        ArgumentNullException.ThrowIfNull(multiplier);
        ArgumentNullException.ThrowIfNull(multiplicand);

        uint[] productDigits = MultiplyMagnitude(multiplier.GetDigits(), multiplicand.GetDigits());
        bool isResultNegative = multiplier.IsNegative ^ multiplicand.IsNegative;
        return new BetterBigInteger(productDigits, isResultNegative);
    }

    // Умножение «столбиком» над модулями: на входе разряды, на выходе разряды.
    // Метод ничего не знает о знаке, поэтому переиспользуется как базовый случай
    // рекурсии в KaratsubaMultiplier.
    public static uint[] MultiplyMagnitude(ReadOnlySpan<uint> multiplierDigits, ReadOnlySpan<uint> multiplicandDigits)
    {
        // Максимально возможная длина произведения двух чисел равна сумме длин их разрядов
        uint[] productDigits = new uint[multiplierDigits.Length + multiplicandDigits.Length];

        // Классическое умножение «столбиком» с временной сложностью O(N^2)
        for (int multiplierIndex = 0; multiplierIndex < multiplierDigits.Length; multiplierIndex++)
        {
            uint currentMultiplierDigit = multiplierDigits[multiplierIndex];

            // Оптимизация: если текущий разряд множителя равен 0, пропускаем итерацию
            if (currentMultiplierDigit == 0) continue;

            uint carry = 0;
            for (int multiplicandIndex = 0; multiplicandIndex < multiplicandDigits.Length; multiplicandIndex++)
            {
                uint currentMultiplicandDigit = multiplicandDigits[multiplicandIndex];

                int currentProductIndex = multiplierIndex + multiplicandIndex;
                uint currentAccumulatedValue = productDigits[currentProductIndex];

                // Вычисляем новое значение текущего разряда и перенос в следующий разряд
                carry = MultiplyLimbWithCarry(
                    currentMultiplierDigit, 
                    currentMultiplicandDigit, 
                    currentAccumulatedValue, 
                    carry, 
                    out uint lowDigit
                );
                
                productDigits[currentProductIndex] = lowDigit;
            }

            productDigits[multiplierIndex + multiplicandDigits.Length] = carry;
        }

        return productDigits;
    }
    
    // Перемножает два 32-битных разряда (uint) с учетом аккумулированного значения и входящего переноса.
    // Возвращает старшую 32-битную часть (перенос в следующий разряд), а через out-параметр — младшую часть.
    private static uint MultiplyLimbWithCarry(
        uint digitA, 
        uint digitB, 
        uint accumulatedDigit, 
        uint incomingCarry, 
        out uint resultLowDigit)
    {
        // Разбиваем 32-битные числа на 16-битные полуслова (High и Low части)
        uint digitALow = digitA & 0xFFFFu;
        uint digitAHigh = digitA >> 16;
        uint digitBLow = digitB & 0xFFFFu;
        uint digitBHigh = digitB >> 16;

        // Попарное перемножение полуслов
        uint productLow = digitALow * digitBLow;             // Младшие части
        uint productMid1 = digitAHigh * digitBLow;           // Перекрестное умножение 1
        uint productMid2 = digitALow * digitBHigh;           // Перекрестное умножение 2
        uint productHigh = digitAHigh * digitBHigh;          // Старшие части

        // Складываем промежуточные перекрестные произведения
        uint crossProductsSum = productMid1 + productMid2;
        uint crossProductOverflow = crossProductsSum < productMid1 ? 1u : 0u;

        // Распределяем перекрестную сумму по младшим и старшим 16 битам
        uint crossSumLowShifted = crossProductsSum << 16;
        uint crossSumHighShifted = (crossProductsSum >> 16) + (crossProductOverflow << 16);

        // Формируем базовые 64 бита произведения двух разрядов (без аккумулированных значений)
        uint baseProductLow = productLow + crossSumLowShifted;
        uint baseProductOverflow = baseProductLow < productLow ? 1u : 0u;

        uint baseProductHigh = productHigh + crossSumHighShifted + baseProductOverflow;

        // Прибавляем текущее значение, лежавшее в ячейке результата
        uint productWithAccumulated = baseProductLow + accumulatedDigit;
        uint accumulationOverflow = productWithAccumulated < baseProductLow ? 1u : 0u;

        // Прибавляем входящий перенос из предыдущей итерации
        uint finalProductLow = productWithAccumulated + incomingCarry;
        uint carryOverflow = finalProductLow < productWithAccumulated ? 1u : 0u;

        // Учитываем все возникшие переполнения в старшей 32-битной части (переносе)
        uint finalProductHigh = baseProductHigh + accumulationOverflow + carryOverflow;
        
        resultLowDigit = finalProductLow;
        return finalProductHigh;
    }
}