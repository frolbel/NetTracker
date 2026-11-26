using System.Net;
using System.Text;

namespace NetTracker
{


    public static class StatisticsGenerator
    {
        // Порог, выше которого количество запросов считается "подозрительно частым"
        private const int AnomalyThreshold = 10;

        // Порог: сколько запросов в ОДНУ СЕКУНДУ считать аномальным всплеском
        private const int BurstThresholdPerSecond = 5;

        public static string GenerateReport(IReadOnlyList<NetworkRecord> records)
        {
            if (records == null || records.Count == 0)
                return "Нет данных для генерации статистики.";

            var sb = new StringBuilder();
            sb.AppendLine("=== СТАТИСТИКА СЕТЕВОЙ АКТИВНОСТИ ===");
            sb.AppendLine($"Всего перехвачено соединений: {records.Count}");
            sb.AppendLine();

            // 1. DNS имена через запятую (уникальные, сортировка по алфавиту)
            var dnsNames = records
                .Where(r => r.Domain != NetworkMonitor.UnknownDomainText)
                .Select(r => r.Domain)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            sb.AppendLine("1. Разрешенные DNS-имена:");
            sb.AppendLine(dnsNames.Count > 0 ? string.Join(", ", dnsNames) : "Нет данных");
            sb.AppendLine();

            // 2. Публичные IP-адреса без DNS (не удалось разрезолвить)
            var publicUnresolvedIps = records
                .Where(r => r.Domain == NetworkMonitor.UnknownDomainText && IsPublicIp(r.DestIp))
                .Select(r => r.DestIp)
                .Distinct()
                .ToList();

            sb.AppendLine("2. Публичные IP-адреса (без DNS):");
            sb.AppendLine(publicUnresolvedIps.Count > 0 ? string.Join(", ", publicUnresolvedIps) : "Нет данных");
            sb.AppendLine();

            // 3. Домены, которые резолвятся на loopback (127.0.0.1 / ::1)
            var loopbackDomains = records
                .Where(r => r.Domain != NetworkMonitor.UnknownDomainText && IsLoopbackIp(r.DestIp))
                .Select(r => r.Domain)
                .Distinct()
                .ToList();

            sb.AppendLine("3. Домены, направленные на локальный интерфейс (Loopback):");
            sb.AppendLine(loopbackDomains.Count > 0 ? string.Join(", ", loopbackDomains) : "Нет данных");
            sb.AppendLine();

            // 4. Домены с аномально высоким числом обращений
            var anomalousDomains = records
                .Where(r => r.Domain != NetworkMonitor.UnknownDomainText)
                .GroupBy(r => r.Domain)
                .Select(g => new { Domain = g.Key, Count = g.Count() })
                .Where(x => x.Count >= AnomalyThreshold)
                .OrderByDescending(x => x.Count)
                .ToList();

            sb.AppendLine($"4. Частые обращения к DNS (>= {AnomalyThreshold} раз):");
            if (anomalousDomains.Count > 0)
            {
                foreach (var item in anomalousDomains)
                    sb.AppendLine($"- {item.Domain}: {item.Count} обращений");
            }
            else sb.AppendLine("Аномалий не выявлено.");
            sb.AppendLine();

            // 5. Публичные неразрешенные IP с аномально высоким числом обращений
            var anomalousIps = records
                .Where(r => r.Domain == NetworkMonitor.UnknownDomainText && IsPublicIp(r.DestIp))
                .GroupBy(r => r.DestIp)
                .Select(g => new { Ip = g.Key, Count = g.Count() })
                .Where(x => x.Count >= AnomalyThreshold)
                .OrderByDescending(x => x.Count)
                .ToList();

            sb.AppendLine($"5. Частые обращения к прямым публичным IP (>= {AnomalyThreshold} раз):");
            if (anomalousIps.Count > 0)
            {
                foreach (var item in anomalousIps)
                    sb.AppendLine($"- {item.Ip}: {item.Count} обращений");
            }
            else sb.AppendLine("Аномалий не выявлено.");

            // 6. Домены с аномально высокой частотой обращений (всплески)
            var anomalousDomainBursts = records
                .Where(r => r.Domain != NetworkMonitor.UnknownDomainText)
                // Группируем по домену и конкретной секунде (отбрасываем миллисекунды)
                .GroupBy(r => new { r.Domain, Time = new DateTime(r.TimeStamp.Year, r.TimeStamp.Month, r.TimeStamp.Day, r.TimeStamp.Hour, r.TimeStamp.Minute, r.TimeStamp.Second) })
                .Select(g => new { g.Key.Domain, g.Key.Time, Count = g.Count() })
                .Where(x => x.Count >= BurstThresholdPerSecond)
                .OrderByDescending(x => x.Count)
                .ToList();

            sb.AppendLine($"6. Аномальные всплески к DNS (>= {BurstThresholdPerSecond} запросов в секунду):");
            if (anomalousDomainBursts.Count > 0)
            {
                foreach (var item in anomalousDomainBursts.Take(20)) // Берем топ 20, чтобы не засорять вывод
                    sb.AppendLine($"- {item.Domain}: {item.Count} обращений за 1 сек ({item.Time:HH:mm:ss})");
            }
            else sb.AppendLine("Всплесков не выявлено.");
            sb.AppendLine();

            // 7. Публичные IP (без DNS) с аномально высокой частотой обращений
            var anomalousIpBursts = records
                .Where(r => r.Domain == NetworkMonitor.UnknownDomainText && IsPublicIp(r.DestIp))
                .GroupBy(r => new { r.DestIp, Time = new DateTime(r.TimeStamp.Year, r.TimeStamp.Month, r.TimeStamp.Day, r.TimeStamp.Hour, r.TimeStamp.Minute, r.TimeStamp.Second) })
                .Select(g => new { g.Key.DestIp, g.Key.Time, Count = g.Count() })
                .Where(x => x.Count >= BurstThresholdPerSecond)
                .OrderByDescending(x => x.Count)
                .ToList();

            sb.AppendLine($"7. Аномальные всплески к прямым публичным IP (>= {BurstThresholdPerSecond} запросов в секунду):");
            if (anomalousIpBursts.Count > 0)
            {
                foreach (var item in anomalousIpBursts.Take(20))
                    sb.AppendLine($"- {item.DestIp}: {item.Count} обращений за 1 сек ({item.Time:HH:mm:ss})");
            }
            else sb.AppendLine("Всплесков не выявлено.");

            return sb.ToString();
        }

        private static bool IsLoopbackIp(string ipString)
        {
            return ipString == "127.0.0.1" || ipString == "::1";
        }

        private static bool IsPublicIp(string ipString)
        {
            if (!IPAddress.TryParse(ipString, out var ip))
            {
                return false;
            }

            byte[] bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // Loopback
                if (bytes[0] == 127) return false; // 127.0.0.0/8

                // IPv4 приватные диапазоны:
                if (bytes[0] == 10) return false; // 10.0.0.0/8
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false; // 172.16.0.0/12
                if (bytes[0] == 192 && bytes[1] == 168) return false; // 192.168.0.0/16
                
                // Link-local адреса (RFC 3927, APIPA) — используются, когда DHCP не ответил.
                if (bytes[0] == 169 && bytes[1] == 254) return false; // 169.254.0.0/16

                // Cпециальный блок CGNAT из RFC 6598 — «Shared Address Space» (CGNAT, RFC 6598)
                // Используется провайдерами для NAT между абонентами и внешней сетью (carrier-grade NAT),
                // когда публичных IPv4 не хватает.
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return false; // 100.64.0.0/10
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // Упрощенная проверка IPv6: отсекаем Loopback (::1), Link-local (fe80::/10), Unique Local (fc00::/7)
                if (IPAddress.IsLoopback(ip)) return false;
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return false;
                if ((bytes[0] & 0xfe) == 0xfc) return false;
            }

            return true;
        }
    }
}
