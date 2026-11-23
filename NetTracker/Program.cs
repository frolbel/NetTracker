using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace NetTracker
{
    internal class Program
    {
        private const string SessionName = "frolbel.NetTracker.Session";

        // Словарь для хранения связок: [IP-адрес] -> [Настоящее DNS имя]
        static ConcurrentDictionary<string, string> IpToDomainMap = new();

        static void Main(string[] args)
        {
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.WriteLine("Ошибка: Пожалуйста, запустите консоль от имени Администратора.");
                return;
            }

            // Очистка старых сессий, если они остались
            var activeSessions = TraceEventSession.GetActiveSessionNames();    
            foreach (var name in activeSessions)    
            {        
                if (name == SessionName)        
                {            
                    var session = TraceEventSession.GetActiveSession(name);
                    session?.Dispose();        
                }    
            }

            var input = "";
            if (args.Length > 0)
            {
                // Соединяем все аргументы в одну строку
                input = string.Join(" ", args);        
            }        
            else        
            {            
                Console.Write("Введите имена процессов (через пробел или запятую, например: telegram chrome): ");
                input = Console.ReadLine()?.Trim();
            } 
            
            if (string.IsNullOrWhiteSpace(input)) 
            { 
                Console.WriteLine("Имя процесса не может быть пустым. Нажмите любую клавишу для выхода..."); 
                Console.ReadKey(true); 
                return; 
            }

            // Разбиваем строку на массив имен, убираем .exe и переводим в нижний регистр
            var targetProcessNames = new HashSet<string>(
                input.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.ToLower().Replace(".exe", ""))        
            );

            if (targetProcessNames.Count == 0) 
            { 
                Console.WriteLine("Не удалось распознать имена процессов. Нажмите любую клавишу для выхода..."); 
                Console.ReadKey(true); 
                return; 
            }

            var uniqueIPs = new HashSet<string>();

            var processNamesDisplay = string.Join(", ", targetProcessNames);
            Console.WriteLine($"\nОжидание сетевой активности от: {processNamesDisplay}...");
            
            Console.WriteLine("Нажмите Ctrl+C для завершения...\n");
            Console.WriteLine($"{"Время",-10} | {"IP-адрес",-15} | {"Порт",-5} | DNS-имя (Домен)");
            Console.WriteLine(new string('-', 65));

            using (var session = new TraceEventSession("MyNetTrackerSession"))
            {
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);

                // Включаем прослушивание DNS-запросов Windows
                session.EnableProvider("Microsoft-Windows-DNS-Client");

                // Обработчик всех динамических событий (в т.ч. DNS)
                session.Source.Dynamic.All += (data) =>
                {
                    if (data.ProviderName == "Microsoft-Windows-DNS-Client")
                    {
                        try
                        {
                            // Извлекаем имя домена и результаты из системного события DNS
                            var domain = data.PayloadByName("QueryName") as string;
                            var results = data.PayloadByName("QueryResults") as string;

                            // Извлекаем все IPv4 из ответа с помощью регулярного выражения
                            if (!string.IsNullOrEmpty(domain) && !string.IsNullOrEmpty(results))
                            {
                                var ipMatches = Regex.Matches(results, @"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b");
                                foreach (Match match in ipMatches)
                                {
                                    IpToDomainMap[match.Value] = domain;
                                }
                            }
                        }
                        catch { }
                    }
                };

                // Обработчик TCP-подключений
                session.Source.Kernel.TcpIpConnect += (data) =>
                {
                    var processName = data.ProcessName;

                    if (string.IsNullOrEmpty(processName) || !targetProcessNames.Contains(processName.ToLower()))
                    {
                        return;
                    }

                    var ipAddress = data.daddr.ToString();

                    if (uniqueIPs.Add(ipAddress))
                    {
                        var domainName = "Неизвестный домен (прямой IP)";
                        if (IpToDomainMap.TryGetValue(ipAddress, out string? mappedDomain))
                        {
                            if (!string.IsNullOrEmpty(mappedDomain))
                            {
                                domainName = mappedDomain;
                            }
                        }

                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] | {ipAddress,-15} | {data.dport,-5} | {domainName}");
                    }
                };

                Console.CancelKeyPress += (s, e) =>
                {
                    session.Dispose();
                    e.Cancel = true;
                };

                // Запускаем сбор
                session.Source.Process();
            }

            Console.WriteLine("\nСбор данных завершен.");

            // Добавляем паузу перед закрытием окна
            Console.WriteLine("Нажмите любую клавишу, чтобы выйти..."); Console.ReadKey(true);
        }
    }
}
