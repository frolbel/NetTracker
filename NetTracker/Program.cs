using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;
using System.Threading;

namespace NetTracker
{
    internal class Program
    {
        /// <summary>
        /// Точка входа.
        /// </summary>
        /// <param name="args">Аргументы запуска утилиты.</param>
        static int Main(string[] args)
        {
            // 1. Предварительный разбор аргументов запуска утилиты.
            var filteredArgs = args.Where(arg => arg != "--elevate" && arg != "-e").ToArray();
            bool forceElevate = args.Length != filteredArgs.Length;

            // 2. Проверка и обработка прав администратора.
            if (!TraceEventSession.IsElevated() ?? false)
            {
                return HandleElevation(filteredArgs, forceElevate);
            }

            // 3. Разбор запуска утилиты.
            string? outPath = null;
            var outType = ExportFormat.Raw;
            var processArgs = new List<string>();
            string? statsPath = null;
            bool printStatsToConsole = false;

            foreach (var arg in filteredArgs)
            {
                if (arg.StartsWith("--out=", StringComparison.OrdinalIgnoreCase))
                {
                    outPath = arg.Substring(6).Trim('"', '\'');
                }
                else if (arg.StartsWith("-o=", StringComparison.OrdinalIgnoreCase))
                {
                    outPath = arg.Substring(3).Trim('"', '\'');
                }
                else if (arg.StartsWith("--type=", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("-t=", StringComparison.OrdinalIgnoreCase))
                {
                    // Извлекаем значение после '='
                    var typeStr = arg.Substring(arg.IndexOf('=') + 1).Trim('"', '\'');

                    // Пытаемся безопасно спарсить строку в Enum (true означает игнорирование регистра)
                    if (!Enum.TryParse<ExportFormat>(typeStr, true, out outType))
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\n[Warning] Неизвестный формат экспорта '{typeStr}'. Будет использован формат по умолчанию (Raw).");
                        Console.ResetColor();
                        outType = ExportFormat.Raw;
                    }
                }
                else if (arg.StartsWith("--stats=", StringComparison.OrdinalIgnoreCase)) 
                { 
                    statsPath = arg.Substring(8).Trim('"', '\''); 
                }
                else if (arg.Equals("--stats", StringComparison.OrdinalIgnoreCase)) 
                { 
                    printStatsToConsole = true; 
                }
                else
                {
                    processArgs.Add(arg);
                }
            }

            // 4. Получение списка процессов для мониторинга
            HashSet<string>? targetProcesses = GetTargetProcesses(processArgs);
            if (targetProcesses == null || targetProcesses.Count == 0)
            {
                ConsoleHelper.WaitForKeysBeforeExit();
                return ExitCode.BadArguments;
            }

            // 5. Запуск основного цикла мониторинга

            // Предположение того, что не надо включать захват пакетов в память
            List<NetworkRecord>? capturedRecords = null;

            // Если запрошен экспорт ИЛИ статистика, нужно включить захват пакетов в память
            if (!string.IsNullOrWhiteSpace(outPath) || printStatsToConsole || !string.IsNullOrWhiteSpace(statsPath))
            {
                capturedRecords = new();
            }

            RunMonitoring(targetProcesses, capturedRecords);

            Console.WriteLine("\nСбор данных завершен.");

            if (capturedRecords != null && !string.IsNullOrWhiteSpace(outPath)) 
            { 
                DataExporter.Export(capturedRecords, outPath, outType); 
            }

            if (capturedRecords != null && (printStatsToConsole || !string.IsNullOrWhiteSpace(statsPath)))
            {
                var report = StatisticsGenerator.GenerateReport(capturedRecords);

                if (printStatsToConsole)
                {
                    Console.WriteLine();
                    Console.WriteLine(report);
                }
                if (!string.IsNullOrWhiteSpace(statsPath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(statsPath);
                        
                        if (!string.IsNullOrWhiteSpace(dir)) 
                        {
                            Directory.CreateDirectory(dir); 
                        }

                        File.WriteAllText(statsPath, report, System.Text.Encoding.UTF8);
                        Console.WriteLine($"\n[Статистика] Сохранена в файл: {statsPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine($"\n[Ошибка] Не удалось сохранить статистику: {ex.Message}");
                        Console.ResetColor();
                    }
                }
            }    

            ConsoleHelper.WaitForKeysBeforeExit();

            return ExitCode.Success;
        }

        /// <summary>
        /// Обрабатывает логику повышения привилегий (UAC) в зависимости от контекста запуска.
        /// </summary>
        /// <param name="args">Аргументы запуска утилиты.</param>
        /// <param name="forceElevate">Указывает, требуется ли принудительно запустить процесс с повышенными привилегиями.</param>
        /// <returns>Код завершения операции обработки повышения привилегий.</returns>
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
        private static HashSet<string>? GetTargetProcesses(IEnumerable<string> args)
        {
            // Соединяем все аргументы в одну строку
            var input = args.Count() > 0 ? string.Join(" ", args) : null;

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
        /// <param name="targetProcesses">Набор имён отслеживаемых процессов.</param>
        /// <param name="capturedRecords">
        /// Коллекция для накопления записей перехваченной сетевой активности.
        /// Если <see langword="null"/>, накопление записей не выполняется.
        /// </param>
        private static void RunMonitoring(HashSet<string> targetProcesses, IList<NetworkRecord>? capturedRecords)
        {
            Console.WriteLine($"\nОжидание сетевой активности от: {string.Join(", ", targetProcesses)}...");
            Console.WriteLine("Нажмите Ctrl+C для завершения...\n");

            Console.WriteLine($"{"Время",-14} | {"IP-адрес",-15} | {"Порт",-5} | {"DNS-имя (Домен)"}");
            Console.WriteLine(new string('-', 90));

            using (var monitor = new NetworkMonitor(targetProcesses, capturedRecords))
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
