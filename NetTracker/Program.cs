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
            // 1. Предварительный разбор аргументов
            var filteredArgs = args.Where(arg => arg != "--elevate" && arg != "-e").ToArray();
            bool forceElevate = args.Length != filteredArgs.Length;

            // 2. Проверка и обработка прав администратора
            if (!TraceEventSession.IsElevated() ?? false)
            {
                return HandleElevation(filteredArgs, forceElevate);
            }

            // 3. Получение списка процессов для мониторинга
            HashSet<string>? targetProcesses = GetTargetProcesses(filteredArgs);
            if (targetProcesses == null || targetProcesses.Count == 0)
            {
                ConsoleHelper.WaitForKeysBeforeExit();
                return ExitCode.BadArguments;
            }

            // 4. Запуск основного цикла мониторинга
            RunMonitoring(targetProcesses);

            ConsoleHelper.WaitForKeysBeforeExit();
            return ExitCode.Success;
        }

        /// <summary>
        /// Обрабатывает логику повышения привилегий (UAC) в зависимости от контекста запуска.
        /// </summary>
        private static int HandleElevation(string[] args, bool forceElevate)
        {
            if (ConsoleHelper.IsLaunchedFromShell() && !forceElevate)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("Ошибка: Для работы NetTracker требуются права администратора.");
                Console.ResetColor();
                Console.Error.WriteLine("Запустите оболочку от имени администратора или используйте флаг --elevate (-e) для запуска в новом окне.");
                return ExitCode.AccessDenied;
            }

            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                string argsString = string.Join(" ", args);
                string? shellName = ConsoleHelper.GetParentShellName();

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                if (shellName != null)
                {
                    startInfo.FileName = shellName;
                    if (ConsoleHelper.IsPowerShell(shellName))
                        startInfo.Arguments = $"-NoExit -Command \"& '{exePath}' {argsString}\"";
                    else
                        startInfo.Arguments = $"/K \"\"{exePath}\" {argsString}\"";
                }
                else
                {
                    startInfo.FileName = exePath!;
                    startInfo.Arguments = argsString;
                }

                Process.Start(startInfo);
                return ExitCode.ElevatedRelaunch;
            }
            catch
            {
                Console.WriteLine("Для работы программы необходимы права администратора.");
                ConsoleHelper.WaitForKeysBeforeExit();
                return ExitCode.AccessDenied;
            }
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

        /// <summary>
        /// Выполняет настройку интерфейса и запускает объект монитора.
        /// </summary>
        private static void RunMonitoring(HashSet<string> targetProcesses)
        {
            Console.WriteLine($"\nОжидание сетевой активности от: {string.Join(", ", targetProcesses)}...");
            Console.WriteLine("Нажмите Ctrl+C для завершения...\n");

            Console.WriteLine($"{"Время",-14} | {"IP-адрес",-15} | {"Порт",-5} | {"DNS-имя (Домен)"}");
            Console.WriteLine(new string('-', 90));

            using (var monitor = new NetworkMonitor(targetProcesses))
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    monitor.Stop();
                };

                monitor.Start();
            }
        }
    }
}
