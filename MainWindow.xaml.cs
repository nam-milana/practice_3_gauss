using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace GaussIntegral;

public partial class MainWindow : Window
{
    // Токен отмены — нужен, чтобы прервать вычисление по нажатию «Отмена»
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Нажатие «Вычислить»
    // ─────────────────────────────────────────────────────────────────────────
    private async void BtnCompute_Click(object sender, RoutedEventArgs e)
    {
        // Сначала скрываем прошлые результаты и ошибки
        ResultPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility  = Visibility.Collapsed;

        // ── Читаем и проверяем введённые значения ────────────────────────────

        // Попытка распарсить нижний предел.
        // InvariantCulture — чтобы работало и с точкой, и независимо от локали Windows.
        bool lowerOk = double.TryParse(
            TbLower.Text.Trim(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out double lower);

        if (!lowerOk)
        {
            ShowError("Некорректный нижний предел. Введите число, например: 0 или -0.5");
            return;
        }

        bool upperOk = double.TryParse(
            TbUpper.Text.Trim(),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out double upper);

        if (!upperOk)
        {
            ShowError("Некорректный верхний предел. Введите число, например: 0.9");
            return;
        }

        bool segmentsOk = int.TryParse(TbSegments.Text.Trim(), out int segments);

        if (!segmentsOk || segments <= 0)
        {
            ShowError("N должно быть целым числом больше нуля. Например: 200");
            return;
        }

        // ── Готовимся к вычислению ───────────────────────────────────────────

        // Блокируем поля и кнопку «Вычислить», включаем «Отмена»
        SetControlsEnabled(isComputing: true);

        // Сбрасываем прогресс-бар
        PbProgress.Value = 0;
        TbStatus.Text = "Вычисление...";

        // Создаём новый источник отмены для этого запуска
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        // Засекаем время
        Stopwatch stopwatch = Stopwatch.StartNew();

        // ── Запускаем вычисление в отдельном потоке ──────────────────────────
        //
        // Task.Run перемещает тяжёлую работу с UI-потока,
        // чтобы окно не зависало во время расчёта.
        //
        // IProgress<T> — безопасный способ передавать обновления прогресса
        // обратно в UI-поток (внутри он делает Dispatcher.Invoke сам).

        // Создаём объект прогресса: когда фоновый поток вызовет Report(),
        // этот лямбда выполнится в UI-потоке
        var progress = new Progress<(int сегмент, int всего)>(данные =>
        {
            double процент = (double)данные.сегмент / данные.всего * 100.0;
            PbProgress.Value = процент;
            TbStatus.Text = $"Обрабатывается сегмент {данные.сегмент} из {данные.всего}...";
        });

        try
        {
            double result = await Task.Run(() =>
            {
                // Эта функция выполняется в фоновом потоке.
                // Передаём ей коллбэк, который будет вызываться после каждого сегмента.
                return Gauss3Quadrature.SolveTargetIntegral(
                    originalLower: lower,
                    originalUpper: upper,
                    segments:      segments,
                    progressCallback: (текущийСегмент, _частичнаяСумма) =>
                    {
                        // Проверяем — не нажал ли пользователь «Отмена»
                        token.ThrowIfCancellationRequested();

                        // Сообщаем UI о прогрессе
                        ((IProgress<(int, int)>)progress).Report((текущийСегмент, segments));
                    }
                );
            }, token);

            stopwatch.Stop();

            // Всё прошло успешно — показываем результат
            PbProgress.Value = 100;
            TbStatus.Text = $"Готово! Время: {stopwatch.ElapsedMilliseconds} мс";

            TbResult.Text = result.ToString("G15");
            TbResultMeta.Text = $"∫ f(x) dx  на [{lower}, {upper}],  N = {segments}";
            ResultPanel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            // Пользователь нажал «Отмена» — это ожидаемая ситуация, не ошибка
            stopwatch.Stop();
            TbStatus.Text = "Вычисление отменено.";
        }
        catch (ArgumentException ex)
        {
            // Неправильные пределы интегрирования (например, a >= b или выход за [-1,1])
            stopwatch.Stop();
            TbStatus.Text = "Ошибка в параметрах.";
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            // Любая другая непредвиденная ошибка
            stopwatch.Stop();
            TbStatus.Text = "Произошла ошибка.";
            ShowError("Неожиданная ошибка: " + ex.Message);
        }
        finally
        {
            // finally выполняется всегда — и при успехе, и при отмене, и при ошибке.
            // Возвращаем интерфейс в исходное состояние.
            SetControlsEnabled(isComputing: false);
            _cts.Dispose();
            _cts = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Нажатие «Отмена»
    // ─────────────────────────────────────────────────────────────────────────
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        // Сигнализируем фоновому потоку, что нужно остановиться.
        // Поток сам проверит токен и бросит OperationCanceledException.
        _cts?.Cancel();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Вспомогательные методы
    // ─────────────────────────────────────────────────────────────────────────

    // Блокирует/разблокирует элементы управления во время вычисления
    private void SetControlsEnabled(bool isComputing)
    {
        BtnCompute.IsEnabled  = !isComputing;
        BtnCancel.IsEnabled   =  isComputing;
        TbLower.IsEnabled     = !isComputing;
        TbUpper.IsEnabled     = !isComputing;
        TbSegments.IsEnabled  = !isComputing;
    }

    // Показывает блок с ошибкой
    private void ShowError(string message)
    {
        TbError.Text          = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
