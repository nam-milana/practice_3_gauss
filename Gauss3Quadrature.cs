using System;

// Класс содержит весь математический алгоритм.
// Он статический, потому что не хранит никакого состояния — только считает.
public static class Gauss3Quadrature
{
    // ─────────────────────────────────────────────────────────────────────────
    // Узлы и веса квадратуры Гаусса–Лежандра (3 точки)
    //
    //  Узлы (xi):  ξ₁ = -√(3/5),  ξ₂ = 0,  ξ₃ = +√(3/5)
    //  Веса (wi):   w₁ = 5/9,      w₂ = 8/9,  w₃ = 5/9
    //
    // Эти значения — константы метода, они не зависят от функции или пределов.
    // ─────────────────────────────────────────────────────────────────────────
    private static readonly double[] Xi = 
    {
        -Math.Sqrt(3.0 / 5.0),   // ξ₁ ≈ -0.7746
         0.0,                    // ξ₂ =  0
        +Math.Sqrt(3.0 / 5.0)    // ξ₃ ≈ +0.7746
    };

    private static readonly double[] Wi = 
    {
        5.0 / 9.0,   // w₁ ≈ 0.5556
        8.0 / 9.0,   // w₂ ≈ 0.8889
        5.0 / 9.0    // w₃ ≈ 0.5556
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Основной метод: составная квадратура Гаусса
    //
    // Идея: разбиваем отрезок [a, b] на N маленьких кусочков.
    // На каждом кусочке применяем 3-точечную формулу Гаусса.
    // Суммируем результаты — это и есть приближение интеграла.
    //
    // Формула для одного сегмента [x_{j-1}, x_j]:
    //   ∫ f(x) dx ≈ (h/2) * Σᵢ wᵢ * f( x_{j-1} + h/2 * (ξᵢ + 1) )
    // где h = x_j - x_{j-1}
    // ─────────────────────────────────────────────────────────────────────────
    public static double Compute(
        Func<double, double> integrand,       // подынтегральная функция f(x)
        double lowerBound,                    // нижний предел a
        double upperBound,                    // верхний предел b
        int segments,                         // количество разбиений N
        Action<int, double>? progressCallback = null) // коллбэк прогресса (необязательный)
    {
        // Проверяем, что аргументы имеют смысл
        ValidateArguments(integrand, lowerBound, upperBound, segments);

        // Длина одного сегмента h = (b - a) / N
        double step = (upperBound - lowerBound) / segments;

        // Здесь будем накапливать сумму по всем сегментам
        double totalSum = 0.0;

        // Проходим по каждому сегменту (j = 0, 1, ..., N-1)
        for (int j = 0; j < segments; j++)
        {
            // Левая и правая граница текущего сегмента
            double leftBound  = lowerBound + j * step;
            double rightBound = leftBound + step;

            // Полудлина сегмента — нужна для аффинного отображения узлов
            double halfStep = step / 2.0;

            // Применяем 3-точечную формулу Гаусса на текущем сегменте.
            // Суммируем вклад каждого из трёх узлов.
            double segmentSum = 0.0;

            for (int i = 0; i < 3; i++)
            {
                // Переводим узел ξᵢ из стандартного отрезка [-1, 1]
                // в точку на текущем сегменте [leftBound, rightBound].
                //
                // Формула отображения:
                //   x = leftBound + halfStep * (ξᵢ + 1)
                //
                // При ξ = -1 → x = leftBound  (левый конец)
                // При ξ =  0 → x = середина сегмента
                // При ξ = +1 → x = rightBound (правый конец)
                double x = leftBound + halfStep * (Xi[i] + 1.0);

                // Вычисляем вклад этого узла: вес * значение функции * полудлина сегмента
                double contribution = Wi[i] * integrand(x) * halfStep;

                segmentSum += contribution;
            }

            // Добавляем результат по текущему сегменту в общую сумму
            totalSum += segmentSum;

            // Если передан коллбэк прогресса — сообщаем, сколько сегментов уже обработано
            progressCallback?.Invoke(j + 1, totalSum);
        }

        return totalSum;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Метод для конкретного интеграла из задания:
    //
    //   ∫ e^√((1-x)/(1+x)) / ((1+x) * √(1-x²)) dx
    //
    // Функция имеет особенность при x → 1 (знаменатель → 0).
    // Чтобы её убрать, делаем замену переменной: x = cos(θ).
    //
    // Тогда:
    //   dx          = -sin(θ) dθ
    //   √(1 - x²)   = sin(θ)
    //   1 + x       = 1 + cos(θ)
    //   √((1-x)/(1+x)) = tan(θ/2)
    //
    // После подстановки особенность устраняется, и подынтегральная функция
    // принимает вид:
    //
    //   g(θ) = e^(tan(θ/2)) / (1 + cos(θ))
    //
    // Пределы интегрирования по θ:
    //   θ_нижний = arccos(b)
    //   θ_верхний = arccos(a)
    // ─────────────────────────────────────────────────────────────────────────
    public static double SolveTargetIntegral(
        double originalLower = 0.0,
        double originalUpper = 1.0,
        int segments = 200,
        Action<int, double>? progressCallback = null)
    {
        // Проверяем, что пределы лежат в допустимой области [-1, 1]
        if (originalLower < -1.0 || originalUpper > 1.0 || originalLower >= originalUpper)
        {
            throw new ArgumentException(
                "Пределы x должны лежать в [-1, 1] и удовлетворять условию a < b.");
        }

        // Вычисляем новые пределы после замены x = cos(θ)
        double thetaLower = Math.Acos(originalUpper); // θ при x = b
        double thetaUpper = Math.Acos(originalLower); // θ при x = a

        // Подынтегральная функция после замены переменной
        Func<double, double> transformedIntegrand = theta =>
        {
            double tanHalf = Math.Tan(theta / 2.0);
            return Math.Exp(tanHalf) / (1.0 + Math.Cos(theta));
        };

        // Вызываем универсальный метод Compute с преобразованной функцией
        return Compute(transformedIntegrand, thetaLower, thetaUpper, segments, progressCallback);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Проверка входных аргументов.
    // Лучше выбросить понятное исключение сразу, чем получить NaN или деление
    // на ноль где-то в середине вычисления.
    // ─────────────────────────────────────────────────────────────────────────
    private static void ValidateArguments(
        Func<double, double> integrand,
        double a,
        double b,
        int n)
    {
        if (integrand == null)
            throw new ArgumentNullException(nameof(integrand), "Функция не может быть null.");

        if (a >= b)
            throw new ArgumentException("Нижний предел должен быть строго меньше верхнего (a < b).");

        if (n <= 0)
            throw new ArgumentOutOfRangeException(nameof(n), "Количество разбиений N должно быть больше нуля.");
    }
}
