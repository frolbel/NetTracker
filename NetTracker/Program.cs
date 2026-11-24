using Microsoft.Diagnostics.Tracing.Session;

namespace NetTracker
{
    internal class Program
    {
        /// <summary>
        /// Точка входа.
        /// </summary>
        /// <param name="args">Аргументы запуска.</param>
        static void Main(string[] args)
        {
            // 1. Проверка прав
            if (!TraceEventSession.IsElevated() ?? false)
            {
                Console.WriteLine("Пожалуйста, запустите программу от имени администратора.");
                ConsoleHelper.WaitForKeysBeforeExit();
                return;
            }

            // 2. Получение списка процессов
            HashSet<string>? targetProcesses = GetTargetProcesses(args);
            if (targetProcesses == null || targetProcesses.Count == 0)
            {
                ConsoleHelper.WaitForKeysBeforeExit();
                return;
            }

            // 3. Запуск мониторинга
            Console.WriteLine($"\nОжидание сетевой активности от: {string.Join(", ", targetProcesses)}...");
            Console.WriteLine("Нажмите Ctrl+C для завершения...\n");

            Console.WriteLine($"{"Время",-12} | {"IP-адрес",-15} | {"Порт",-5} | {"DNS-имя (Домен)"}");
            Console.WriteLine(new string('-', 80));

            using (var monitor = new NetworkMonitor(targetProcesses))
            {
                // Обработка Ctrl+C
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true; // Предотвращаем жесткое закрытие
                    monitor.Stop();
                };

                // Запускаем сбор
                // Блокирующий вызов - работает пока не вызовут Stop()
                monitor.Start();
            }

            // Добавление паузы перед закрытием окна при самостоятельном запуске
            ConsoleHelper.WaitForKeysBeforeExit();
        }

        /// <summary>
        /// Вспомогательный метод парсинга ввода.
        /// </summary>
        /// <param name="args">Аргументы запуска.</param>
        /// <returns>Коллекция имен процессов (без .exe) для фильтрации трафика.</returns>
        private static HashSet<string>? GetTargetProcesses(string[] args)
        {
            // Соединяем все аргументы в одну строку
            var input = args.Length > 0 ? string.Join(" ", args) : null;

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.Write("Введите имена процессов (через пробел или запятую): ");
                input = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("Имя процесса не может быть пустым.");
                return null;
            }

            // Разбиваем строку на коллекцию имен процессов, убирая .exe и переводя в нижний регистр
            var processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in input.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
            {
                processes.Add(p.Replace(".exe", ""));
            }

            if (processes.Count == 0)
            {
                Console.WriteLine("Не удалось распознать имена процессов.");
            }

            return processes;
        }
    }
}
