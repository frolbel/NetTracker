namespace NetTracker
{
    /// <summary>
    /// Коды завершения работы утилиты.
    /// </summary>
    /// <remarks>
    /// Базируются на системных кодах ошибок Windows.
    /// </remarks>
    public readonly record struct ExitCode(int Value)
    {
        /// <summary>
        /// Выполняет неявное преобразование объекта ExitCode в целое число (int).
        /// </summary>
        /// <param name="code">Экземпляр кода завершения.</param>
        public static implicit operator int(ExitCode code) => code.Value;

        /// <summary>
        /// Успешное завершение работы.
        /// </summary>
        /// <remarks>
        /// Cистемный код: ERROR_SUCCESS = 0
        /// </remarks>
        public static readonly ExitCode Success = new(0);

        /// <summary>
        /// Ошибка: Отказано в доступе.
        /// </summary>
        /// <remarks>
        /// Cсистемный код: ERROR_ACCESS_DENIED = 5
        /// Возникает при попытке запуска без прав администратора.
        /// </remarks>
        public static readonly ExitCode AccessDenied = new(5);

        /// <summary>
        /// Ошибка: Неверные аргументы. 
        /// </summary>
        /// <remarks>
        /// Cистемный код: ERROR_BAD_ARGUMENTS = 160
        /// Возникает, если не указано имя процесса для мониторинга.
        /// </remarks>
        public static readonly ExitCode BadArguments = new(160);

        /// <summary>
        /// Текущий процесс успешно инициировал запуск копии с повышенными правами.
        /// </summary>
        /// <remarks>
        /// Cистемный код: ERROR_ELEVATION_REQUIRED = 740
        /// </remarks>
        public static readonly ExitCode ElevatedRelaunch = new(740);
    }
}
