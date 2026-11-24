using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;

namespace NetTracker
{
    internal class Program
    {
        /// <summary>
        /// Точка входа.
        /// </summary>
        /// <param name="args">Аргументы запуска.</param>
        static int Main(string[] args)
        {
            // Список аргументов без нашего служебного флага
            var filteredArgs = args.Where(arg => arg != "--elevate" && arg != "-e").ToArray();
            bool forceElevate = args.Length != filteredArgs.Length;

            // 1. Проверка прав
            if (!TraceEventSession.IsElevated() ?? false)
            {
                // Если запуск из оболочки И НЕ задан флаг принудительного повышения
                if (ConsoleHelper.IsLaunchedFromShell() && !forceElevate)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("Ошибка: Для работы NetTracker требуются права администратора.");
                    Console.ResetColor();
                    Console.Error.WriteLine("Запустите оболочку от имени администратора или используйте флаг --elevate для запуска в новом окне.");

                    return ExitCode.AccessDenied;
                }
                else
                {
                    // Либо запуск из проводника, либо задан флаг --elevate в консоли
                    try
                    {
                        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                        var startInfo = new ProcessStartInfo(exePath!)
                        {
                            UseShellExecute = true,
                            Verb = "runas", // Вызов UAC
                        };

                        if (args.Length > 0)
                        {
                            // Передаем только имена процессов
                            startInfo.Arguments = string.Join(" ", filteredArgs);
                        }

                        Process.Start(startInfo);

                        return ExitCode.Success;
                    }
                    catch
                    {
                        Console.WriteLine("Для работы программы необходимы права администратора.");
                        ConsoleHelper.WaitForKeysBeforeExit();
                        return ExitCode.AccessDenied;
                    }
                }
            }

            // 2. Получение списка процессов
            HashSet<string>? targetProcesses = GetTargetProcesses(filteredArgs);
            if (targetProcesses == null || targetProcesses.Count == 0)
            {
                ConsoleHelper.WaitForKeysBeforeExit();
                return ExitCode.BadArguments;
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
            return ExitCode.Success;
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
