namespace NetTracker
{
    /// <summary>
    /// Централизованный логгер для вывода информации в консоль с поддержкой уровней детализации и цветового кодирования.
    /// </summary>
    public static class AppLogger
    {
        /// <summary>
        /// Текущий уровень детализации вывода.
        /// </summary>
        /// <remarks>
        /// По умолчанию <see cref="LogLevel.Debug"/> (включает всё до уровня Debug включительно).
        /// </remarks>
        public static LogLevel CurrentLevel { get; set; } = LogLevel.Debug;

        /// <summary>
        /// Выводит сообщение об ошибке красным цветом (выводится в стандартный поток ошибок stderr).
        /// </summary>
        /// <param name="message">Текст ошибки.</param>
        /// <remarks>
        /// Отображается при уровне <see cref="LogLevel.Error"/> и выше.
        /// </remarks>
        public static void Error(string message)
        {
            if (CurrentLevel < LogLevel.Error) return;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(message);
            Console.ResetColor();
        }

        /// <summary>
        /// Выводит предупреждение желтым цветом.
        /// </summary>
        /// <param name="message">Текст предупреждения.</param>
        /// <remarks>
        /// Отображается при уровне <see cref="LogLevel.Warning"/> и выше.
        /// </remarks>
        public static void Warning(string message)
        {
            if (CurrentLevel < LogLevel.Warning) return;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        /// <summary>
        /// Выводит перехваченные данные (строки таблицы) стандартным цветом консоли.
        /// </summary>
        /// <param name="message">Строка с данными сетевого события.</param>
        /// <remarks>
        /// Отображается при уровне <see cref="LogLevel.Capture"/> и выше.
        /// </remarks>
        public static void Capture(string message)
        {
            if (CurrentLevel < LogLevel.Capture) return;
            Console.WriteLine(message); // Основные данные белым/стандартным цветом
        }

        /// <summary>
        /// Выводит статистику по перехваченные данные стандартным цветом консоли.
        /// </summary>
        /// <param name="message"></param>
        /// <remarks>
        /// Отображается при уровне <see cref="LogLevel.Statistics"/> и выше.
        /// </remarks>
        public static void Statistics(string message)
        {
            if (CurrentLevel < LogLevel.Statistics) return;
            Console.WriteLine(message); // Основные данные белым/стандартным цветом
        }

        /// <summary>
        /// Выводит информационное сообщение бирюзовым (Cyan) цветом.
        /// </summary>
        /// <param name="message">Текст информационного сообщения.</param>
        public static void Info(string message)
        {
            if (CurrentLevel < LogLevel.Info) return;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        /// <summary>
        /// Выводит отладочное сообщение темно-серым цветом, не отвлекая от основных данных.
        /// </summary>
        /// <param name="message">Текст отладочного сообщения.</param>
        /// <remarks>
        /// Отображается при уровне <see cref="LogLevel.Debug"/> и выше.
        /// </remarks>
        public static void Debug(string message)
        {
            if (CurrentLevel < LogLevel.Debug) return;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        /// <summary>
        /// Выводит низкоуровневую трассировочную информацию темно-пурпурным цветом.
        /// </summary>
        /// <param name="message">Текст трассировки.</param>
        /// <remarks>
        /// Отображается только при максимальном уровне <see cref="LogLevel.Trace"/>.
        /// </remarks>
        public static void Trace(string message)
        {
            if (CurrentLevel < LogLevel.Trace) return;
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine($"[TRACE] {message}");
            Console.ResetColor();
        }
    }
}
