namespace NetTracker
{
    /// <summary>
    /// Отвечает за единый формат вывода табличных данных в консоль и Raw-файлы.
    /// </summary>
    public static class OutputFormatter
    {
        /// <summary>
        /// Возвращает отформатированный заголовок таблицы.
        /// </summary>
        public static string GetHeader() =>
            $"{"Время",-14} | {"IP-адрес",-15} | {"Порт",-5} | {"DNS-имя (Домен)"}";

        /// <summary>
        /// Возвращает строку-разделитель под ширину колонок.
        /// </summary>
        public static string GetSeparator() =>
            new string('-', 90);

        /// <summary>
        /// Форматирует единичную запись для вывода в строку таблицы.
        /// </summary>
        public static string FormatRecord(NetworkRecord record) =>
            FormatRecord(record.TimeStamp, record.DestIp, record.DestPort, record.Domain);

        /// <summary>
        /// Форматирует данные для вывода в строку таблицы (перегрузка без создания структуры).
        /// </summary>
        public static string FormatRecord(DateTime timeStamp, string destIp, int destPort, string domain) =>
            $"[{timeStamp:HH:mm:ss.fff}] | {destIp,-15} | {destPort,-5} | {domain}";
    }
}
