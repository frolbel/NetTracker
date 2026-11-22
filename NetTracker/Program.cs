using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace NetTracker
{
    internal class Program
    {
        // Словарь для хранения связок: [IP-адрес] -> [Настоящее DNS имя]
        static ConcurrentDictionary<string, string> IpToDomainMap = new();

        static void Main(string[] args)
        {
            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.WriteLine("Ошибка: Пожалуйста, запустите консоль от имени Администратора.");
                return;
            }

            Console.Write("Введите имя процесса для отслеживания (например, telegram или chrome): ");
            var targetProcess = Console.ReadLine()?.Trim().ToLower();

            if (string.IsNullOrEmpty(targetProcess))
            {
                Console.WriteLine("Имя процесса не может быть пустым.");
                return;
            }

            var uniqueIPs = new HashSet<string>();

            Console.WriteLine($"\nОжидание сетевой активности от '{targetProcess}'...");
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
                    var processName = data.ProcessName.ToLower();

                    if (processName.Contains(targetProcess))
                    {
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
        }
    }
}
